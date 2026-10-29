using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MHSManager
{
    public sealed class UrlProbeResult
    {
        public bool Success { get; set; }
        public string FinalUrl { get; set; }
        public string SuggestedFileName { get; set; }
        public long TotalBytes { get; set; }
        public bool SupportsRanges { get; set; }
        public bool IsHls { get; set; }
        public string ContentType { get; set; }
        public string ETag { get; set; }
        public string LastModified { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class PartMetadata
    {
        public string Url { get; set; }
        public long TotalBytes { get; set; }
        public bool SupportsRanges { get; set; }
        public string ETag { get; set; }
        public string LastModified { get; set; }
        public List<SegmentInfo> Segments { get; set; }
    }

    public static class DownloadEngine
    {
        public const long MinDynamicSplitBytes = 256 * 1024; // 256 KB minimum stealable tail

        public static async Task<UrlProbeResult> ProbeUrlAsync(
            string url,
            string userAgent,
            string referer,
            string cookies,
            string authUser,
            string authPassword,
            CancellationToken token)
        {
            UrlProbeResult result = new UrlProbeResult
            {
                FinalUrl = url,
                TotalBytes = -1,
                SuggestedFileName = CategoryClassifier.GuessFileNameFromUrl(url)
            };

            if (HttpHelpers.IsHlsUrlOrMime(url, null))
            {
                result.Success = true;
                result.IsHls = true;
                result.SupportsRanges = true;
                result.SuggestedFileName = HttpHelpers.EnsureNonM3u8VideoFileName(result.SuggestedFileName);
                return result;
            }

            bool needFallbackGet = false;
            Exception firstError = null;

            // Attempt 1: GET with Range: bytes=0-0 (works with S3 presigned URLs and tests 206 support)
            try
            {
                HttpWebRequest req = HttpHelpers.CreateRequest(
                    url, userAgent, referer, cookies, authUser, authPassword, 15000, false);
                req.AddRange(0, 0);

                using (token.Register(delegate { try { req.Abort(); } catch { } }))
                using (HttpWebResponse resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
                {
                    PopulateProbeFromResponse(result, resp, true);
                    return result;
                }
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested)
                {
                    result.Success = false;
                    result.ErrorMessage = "Cancelled";
                    return result;
                }
                firstError = ex;
                needFallbackGet = true;
            }

            if (needFallbackGet)
            {
                try
                {
                    HttpWebRequest req2 = HttpHelpers.CreateRequest(
                        url, userAgent, referer, cookies, authUser, authPassword, 15000, false);

                    using (token.Register(delegate { try { req2.Abort(); } catch { } }))
                    using (HttpWebResponse resp2 = (HttpWebResponse)await req2.GetResponseAsync().ConfigureAwait(false))
                    {
                        PopulateProbeFromResponse(result, resp2, false);
                        return result;
                    }
                }
                catch (Exception ex2)
                {
                    result.Success = false;
                    result.ErrorMessage = (ex2 != null ? ex2.Message : null) ??
                                          (firstError != null ? firstError.Message : "Failed to probe URL.");
                }
            }

            return result;
        }

        private static void PopulateProbeFromResponse(UrlProbeResult result, HttpWebResponse resp, bool sentRangeZero)
        {
            result.Success = true;
            if (resp.ResponseUri != null)
            {
                result.FinalUrl = resp.ResponseUri.AbsoluteUri;
            }
            result.ContentType = resp.ContentType ?? "";
            result.ETag = resp.Headers["ETag"] ?? "";
            result.LastModified = resp.Headers["Last-Modified"] ?? "";

            if (resp.StatusCode == HttpStatusCode.PartialContent)
            {
                result.SupportsRanges = true;
                long total = HttpHelpers.ParseTotalFromContentRange(resp.Headers["Content-Range"]);
                result.TotalBytes = total >= 0 ? total : resp.ContentLength;
            }
            else
            {
                string acceptRanges = resp.Headers["Accept-Ranges"] ?? "";
                result.SupportsRanges = string.Equals(acceptRanges.Trim(), "bytes", StringComparison.OrdinalIgnoreCase) && !sentRangeZero;
                result.TotalBytes = resp.ContentLength;
            }

            result.IsHls = HttpHelpers.IsHlsUrlOrMime(result.FinalUrl, result.ContentType);

            string cdName = HttpHelpers.ParseContentDispositionFileName(resp.Headers["Content-Disposition"]);
            if (!string.IsNullOrWhiteSpace(cdName))
            {
                result.SuggestedFileName = cdName;
            }
            else
            {
                result.SuggestedFileName = CategoryClassifier.GuessFileNameFromUrl(result.FinalUrl);
            }

            if (result.IsHls)
            {
                result.SupportsRanges = true;
                result.TotalBytes = -1;
                result.SuggestedFileName = HttpHelpers.EnsureNonM3u8VideoFileName(result.SuggestedFileName);
            }
        }
    }

    public sealed class DownloadTaskRunner
    {
        private readonly DownloadItem _item;
        private readonly AppSettings _settings;
        private readonly TokenBucketLimiter _globalLimiter;
        private readonly TokenBucketLimiter _itemLimiter;
        private readonly Action<DownloadItem> _onProgress;
        private readonly Action<DownloadItem> _onFinished;
        private readonly CancellationTokenSource _cts;
        private readonly object _segLock = new object();
        private readonly object _fileLock = new object();

        private volatile bool _pauseRequested;
        private long _lastMetaSaveTicks;

        public DownloadItem Item
        {
            get { return _item; }
        }

        public bool IsPauseRequested
        {
            get { return _pauseRequested; }
        }

        public DownloadTaskRunner(
            DownloadItem item,
            AppSettings settings,
            TokenBucketLimiter globalLimiter,
            Action<DownloadItem> onProgress,
            Action<DownloadItem> onFinished)
        {
            _item = item;
            _settings = settings;
            _globalLimiter = globalLimiter;
            _itemLimiter = new TokenBucketLimiter(item.MaxSpeedBytesPerSec > 0, item.MaxSpeedBytesPerSec);
            _onProgress = onProgress;
            _onFinished = onFinished;
            _cts = new CancellationTokenSource();
        }

        public void UpdateItemSpeedLimit(long maxBytesPerSec)
        {
            _item.MaxSpeedBytesPerSec = Math.Max(0, maxBytesPerSec);
            _itemLimiter.Configure(_item.MaxSpeedBytesPerSec > 0, _item.MaxSpeedBytesPerSec);
        }

        public void RequestPause()
        {
            _pauseRequested = true;
            try
            {
                _cts.Cancel();
            }
            catch
            {
            }
        }

        public Task StartAsync()
        {
            return Task.Run(async delegate
            {
                await RunInternalAsync().ConfigureAwait(false);
            });
        }

        private async Task RunInternalAsync()
        {
            CancellationToken token = _cts.Token;
            _item.ErrorMessage = null;

            try
            {
                if (string.IsNullOrWhiteSpace(_item.SaveDirectory))
                {
                    _item.SaveDirectory = CategoryClassifier.ResolveSaveDirectory(_settings, _item.CategoryName);
                }
                if (!Directory.Exists(_item.SaveDirectory))
                {
                    Directory.CreateDirectory(_item.SaveDirectory);
                }

                // Route HLS streams directly to HlsDownloader
                if (_item.IsHlsStream || HttpHelpers.IsHlsUrlOrMime(_item.Url, null))
                {
                    await HlsDownloader.ExecuteAsync(
                        _item,
                        _settings.DefaultUserAgent,
                        _globalLimiter,
                        _itemLimiter,
                        _onProgress,
                        token).ConfigureAwait(false);
                    return;
                }

                // Try loading saved .mhsmeta checkpoint if .mhspart exists
                TryRestorePartMetadata();

                // Probe URL if we don't know size/capabilities yet or if starting fresh
                bool hasResumablePart = File.Exists(_item.PartFilePath) &&
                                        _item.SupportsRanges &&
                                        _item.TotalBytes > 0 &&
                                        _item.Segments != null &&
                                        _item.Segments.Count > 0;

                if (!hasResumablePart)
                {
                    _item.Status = DownloadStatus.Probing;
                    _item.AddLog("Probing server capabilities for " + _item.Url);
                    NotifyProgress();

                    UrlProbeResult probe = await DownloadEngine.ProbeUrlAsync(
                        _item.Url,
                        !string.IsNullOrWhiteSpace(_item.UserAgent) ? _item.UserAgent : _settings.DefaultUserAgent,
                        _item.Referer,
                        _item.Cookies,
                        _item.AuthUser,
                        _item.AuthPassword,
                        token).ConfigureAwait(false);

                    if (!probe.Success)
                    {
                        throw new IOException(probe.ErrorMessage ?? "Server probe failed.");
                    }

                    if (probe.IsHls)
                    {
                        _item.IsHlsStream = true;
                        await HlsDownloader.ExecuteAsync(
                            _item,
                            _settings.DefaultUserAgent,
                            _globalLimiter,
                            _itemLimiter,
                            _onProgress,
                            token).ConfigureAwait(false);
                        return;
                    }

                    _item.TotalBytes = probe.TotalBytes;
                    _item.SupportsRanges = probe.SupportsRanges;
                    _item.ETag = probe.ETag;
                    _item.LastModified = probe.LastModified;

                    if (string.IsNullOrWhiteSpace(_item.FileName) || _item.FileName == "download.bin")
                    {
                        if (!string.IsNullOrWhiteSpace(probe.SuggestedFileName))
                        {
                            _item.FileName = probe.SuggestedFileName;
                        }
                    }

                    _item.AddLog(string.Format(
                        "Server response: Size={0}, Resume/Range={1}",
                        _item.TotalBytes >= 0 ? FormattingHelpers.FormatBytes(_item.TotalBytes) : "Unknown",
                        _item.SupportsRanges ? "Yes (206 Partial Content)" : "No (Single Stream)"));
                }
                else
                {
                    _item.AddLog(string.Format(
                        "Resuming partial download ({0} / {1})",
                        FormattingHelpers.FormatBytes(_item.DownloadedBytes),
                        FormattingHelpers.FormatBytes(_item.TotalBytes)));
                }

                if (_item.SupportsRanges && _item.TotalBytes > 0)
                {
                    await DownloadSegmentedAsync(hasResumablePart, token).ConfigureAwait(false);
                }
                else
                {
                    await DownloadSingleStreamAsync(token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                MarkPausedOrCancelled();
            }
            catch (Exception ex)
            {
                if (_pauseRequested || token.IsCancellationRequested)
                {
                    MarkPausedOrCancelled();
                }
                else
                {
                    _item.CurrentSpeedBytesPerSec = 0;
                    _item.ActiveConnections = 0;
                    _item.Status = DownloadStatus.Error;
                    _item.ErrorMessage = ex.Message;
                    _item.AddLog("ERROR: " + ex.Message);
                    SavePartMetadataSafe();
                }
            }
            finally
            {
                _item.CurrentSpeedBytesPerSec = 0;
                _item.ActiveConnections = 0;
                ResetActiveSegmentSpeeds();
                NotifyProgress();
                if (_onFinished != null)
                {
                    _onFinished(_item);
                }
            }
        }

        private void MarkPausedOrCancelled()
        {
            _item.CurrentSpeedBytesPerSec = 0;
            _item.ActiveConnections = 0;
            _item.Status = DownloadStatus.Paused;
            _item.AddLog("Download paused; checkpoint saved.");
            SavePartMetadataSafe();
        }

        private void ResetActiveSegmentSpeeds()
        {
            lock (_segLock)
            {
                if (_item.Segments == null) return;
                for (int i = 0; i < _item.Segments.Count; i++)
                {
                    SegmentInfo s = _item.Segments[i];
                    s.SpeedBytesPerSec = 0;
                    if (s.RemainingBytes <= 0)
                    {
                        s.State = "Completed";
                    }
                    else if (s.State == "Downloading" || s.State == "Connecting")
                    {
                        s.State = _pauseRequested ? "Paused" : "Pending";
                    }
                }
            }
        }

        private async Task DownloadSegmentedAsync(bool resuming, CancellationToken token)
        {
            lock (_segLock)
            {
                if (!resuming || _item.Segments == null || _item.Segments.Count == 0)
                {
                    InitializeSegments(_item.TotalBytes, _item.MaxConnections);
                }
                else
                {
                    for (int i = 0; i < _item.Segments.Count; i++)
                    {
                        SegmentInfo s = _item.Segments[i];
                        s.SpeedBytesPerSec = 0;
                        s.State = s.RemainingBytes > 0 ? "Pending" : "Completed";
                    }
                }
                RecomputeDownloadedBytesLocked();
            }

            SavePartMetadataSafe();

            using (FileStream partStream = new FileStream(
                _item.PartFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read,
                65536))
            {
                if (partStream.Length != _item.TotalBytes)
                {
                    partStream.SetLength(_item.TotalBytes);
                }

                _item.Status = DownloadStatus.Downloading;
                NotifyProgress();

                int maxWorkers = Math.Max(1, Math.Min(32, _item.MaxConnections));
                List<Task> workers = new List<Task>(maxWorkers);
                int activeCount = 0;

                for (int w = 0; w < maxWorkers; w++)
                {
                    int workerId = w + 1;
                    workers.Add(Task.Run(async delegate
                    {
                        Interlocked.Increment(ref activeCount);
                        _item.ActiveConnections = Volatile.Read(ref activeCount);
                        try
                        {
                            while (!token.IsCancellationRequested)
                            {
                                SegmentInfo seg = ClaimOrStealSegment();
                                if (seg == null)
                                {
                                    break;
                                }

                                await DownloadSegmentWithRetriesAsync(workerId, seg, partStream, token).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            int rem = Interlocked.Decrement(ref activeCount);
                            _item.ActiveConnections = Math.Max(0, rem);
                        }
                    }, token));
                }

                await Task.WhenAll(workers).ConfigureAwait(false);
                partStream.Flush(true);
            }

            token.ThrowIfCancellationRequested();

            // Verify all byte ranges completed
            lock (_segLock)
            {
                RecomputeDownloadedBytesLocked();
                for (int i = 0; i < _item.Segments.Count; i++)
                {
                    if (_item.Segments[i].RemainingBytes > 0)
                    {
                        throw new IOException(string.Format(
                            "Segment #{0} incomplete ({1} remaining).",
                            _item.Segments[i].Id,
                            FormattingHelpers.FormatBytes(_item.Segments[i].RemainingBytes)));
                    }
                }
            }

            await FinalizeCompletedPartFileAsync(token).ConfigureAwait(false);
        }

        private void InitializeSegments(long totalBytes, int requestedConnections)
        {
            _item.Segments.Clear();
            int count = Math.Max(1, Math.Min(32, requestedConnections));

            // Keep initial segments >= 128 KB so tiny files don't spawn superfluous sockets
            const long minInitialChunk = 128 * 1024;
            if (totalBytes < minInitialChunk)
            {
                count = 1;
            }
            else if (totalBytes / count < minInitialChunk)
            {
                count = Math.Max(1, (int)(totalBytes / minInitialChunk));
            }

            long baseChunk = totalBytes / count;
            long cursor = 0;
            for (int i = 0; i < count; i++)
            {
                long start = cursor;
                long end = (i == count - 1) ? (totalBytes - 1) : (cursor + baseChunk - 1);
                _item.Segments.Add(new SegmentInfo
                {
                    Id = i + 1,
                    StartByte = start,
                    CurrentByte = start,
                    EndByte = end,
                    State = "Pending"
                });
                cursor = end + 1;
            }
        }

        private SegmentInfo ClaimOrStealSegment()
        {
            lock (_segLock)
            {
                // 1. Claim any pending unstarted/paused segment first
                for (int i = 0; i < _item.Segments.Count; i++)
                {
                    SegmentInfo s = _item.Segments[i];
                    if (s.State == "Pending" && s.RemainingBytes > 0)
                    {
                        s.State = "Connecting";
                        return s;
                    }
                }

                // 2. Dynamic work-stealing: find active segment with the largest remaining tail >= 256 KB
                SegmentInfo largest = null;
                for (int i = 0; i < _item.Segments.Count; i++)
                {
                    SegmentInfo s = _item.Segments[i];
                    if ((s.State == "Downloading" || s.State == "Connecting") &&
                        s.RemainingBytes >= DownloadEngine.MinDynamicSplitBytes)
                    {
                        if (largest == null || s.RemainingBytes > largest.RemainingBytes)
                        {
                            largest = s;
                        }
                    }
                }

                if (largest != null)
                {
                    long half = largest.RemainingBytes / 2;
                    long splitStart = largest.CurrentByte + half;
                    long oldEnd = largest.EndByte;

                    if (splitStart > largest.CurrentByte && splitStart <= oldEnd)
                    {
                        largest.EndByte = splitStart - 1;
                        SegmentInfo stolen = new SegmentInfo
                        {
                            Id = _item.Segments.Count + 1,
                            StartByte = splitStart,
                            CurrentByte = splitStart,
                            EndByte = oldEnd,
                            State = "Connecting"
                        };
                        _item.Segments.Add(stolen);
                        _item.AddLog(string.Format(
                            "Dynamic split: Stole {0} from segment #{1} -> new segment #{2}",
                            FormattingHelpers.FormatBytes(stolen.TotalBytes),
                            largest.Id,
                            stolen.Id));
                        return stolen;
                    }
                }

                return null;
            }
        }

        private async Task DownloadSegmentWithRetriesAsync(
            int workerId,
            SegmentInfo seg,
            FileStream partStream,
            CancellationToken token)
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= 4; attempt++)
            {
                token.ThrowIfCancellationRequested();

                long rangeStart;
                long rangeEnd;
                lock (_segLock)
                {
                    rangeStart = seg.CurrentByte;
                    rangeEnd = seg.EndByte;
                    if (rangeStart > rangeEnd)
                    {
                        seg.State = "Completed";
                        seg.SpeedBytesPerSec = 0;
                        return;
                    }
                    seg.State = "Connecting";
                }

                bool completedCleanly = false;
                try
                {
                    HttpWebRequest req = HttpHelpers.CreateRequest(
                        _item.Url,
                        !string.IsNullOrWhiteSpace(_item.UserAgent) ? _item.UserAgent : _settings.DefaultUserAgent,
                        _item.Referer,
                        _item.Cookies,
                        _item.AuthUser,
                        _item.AuthPassword,
                        25000,
                        false);

                    req.AddRange(rangeStart, rangeEnd);

                    using (token.Register(delegate { try { req.Abort(); } catch { } }))
                    using (HttpWebResponse resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
                    {
                        if (rangeStart > 0 && resp.StatusCode != HttpStatusCode.PartialContent)
                        {
                            throw new IOException("Server ignored HTTP Range header and returned " + (int)resp.StatusCode);
                        }

                        lock (_segLock)
                        {
                            seg.State = "Downloading";
                        }

                        using (Stream netStream = resp.GetResponseStream())
                        {
                            byte[] buffer = new byte[65536];
                            Stopwatch speedWatch = Stopwatch.StartNew();
                            long windowBytes = 0;

                            while (!token.IsCancellationRequested)
                            {
                                long remainingHint;
                                lock (_segLock)
                                {
                                    remainingHint = seg.EndByte - seg.CurrentByte + 1;
                                }
                                if (remainingHint <= 0)
                                {
                                    break;
                                }

                                int toRead = (int)Math.Min((long)buffer.Length, remainingHint);
                                int bytesRead = await netStream.ReadAsync(buffer, 0, toRead, token).ConfigureAwait(false);
                                if (bytesRead <= 0)
                                {
                                    break;
                                }

                                if (_globalLimiter != null && _globalLimiter.IsActive)
                                {
                                    await _globalLimiter.ThrottleAsync(bytesRead, token).ConfigureAwait(false);
                                }
                                if (_itemLimiter != null && _itemLimiter.IsActive)
                                {
                                    await _itemLimiter.ThrottleAsync(bytesRead, token).ConfigureAwait(false);
                                }

                                bool finishedSegment = false;
                                lock (_segLock)
                                {
                                    long maxAllowed = seg.EndByte - seg.CurrentByte + 1;
                                    if (maxAllowed <= 0)
                                    {
                                        seg.State = "Completed";
                                        seg.SpeedBytesPerSec = 0;
                                        finishedSegment = true;
                                    }
                                    else
                                    {
                                        int writeLen = (int)Math.Min((long)bytesRead, maxAllowed);
                                        long writeOffset = seg.CurrentByte;

                                        lock (_fileLock)
                                        {
                                            partStream.Position = writeOffset;
                                            partStream.Write(buffer, 0, writeLen);
                                        }

                                        seg.CurrentByte += writeLen;
                                        windowBytes += writeLen;

                                        double elapsedSec = speedWatch.Elapsed.TotalSeconds;
                                        if (elapsedSec >= 0.25)
                                        {
                                            double inst = windowBytes / elapsedSec;
                                            seg.SpeedBytesPerSec = seg.SpeedBytesPerSec <= 1.0
                                                ? inst
                                                : (seg.SpeedBytesPerSec * 0.6) + (inst * 0.4);
                                            windowBytes = 0;
                                            speedWatch.Restart();
                                        }

                                        RecomputeDownloadedBytesLocked();

                                        if (seg.CurrentByte > seg.EndByte)
                                        {
                                            seg.State = "Completed";
                                            seg.SpeedBytesPerSec = 0;
                                            finishedSegment = true;
                                        }
                                    }
                                }

                                MaybeSavePartMetadataPeriodic();

                                if (finishedSegment)
                                {
                                    break;
                                }
                            }
                        }
                    }

                    lock (_segLock)
                    {
                        if (seg.RemainingBytes <= 0)
                        {
                            seg.State = "Completed";
                            seg.SpeedBytesPerSec = 0;
                            completedCleanly = true;
                        }
                    }

                    if (completedCleanly)
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(token);
                    }
                    lastException = ex;
                    lock (_segLock)
                    {
                        seg.SpeedBytesPerSec = 0;
                        seg.State = "Retrying";
                    }
                    _item.AddLog(string.Format(
                        "Worker #{0} segment #{1} retry {2}/4: {3}",
                        workerId, seg.Id, attempt, ex.Message));
                }

                if (attempt < 4)
                {
                    await Task.Delay(500 * attempt, token).ConfigureAwait(false);
                }
            }

            lock (_segLock)
            {
                if (seg.RemainingBytes <= 0)
                {
                    seg.State = "Completed";
                    seg.SpeedBytesPerSec = 0;
                    return;
                }
                seg.State = "Error";
                seg.SpeedBytesPerSec = 0;
            }

            throw lastException ?? new IOException("Segment #" + seg.Id + " failed after 4 attempts.");
        }

        private async Task DownloadSingleStreamAsync(CancellationToken token)
        {
            lock (_segLock)
            {
                _item.Segments.Clear();
                _item.Segments.Add(new SegmentInfo
                {
                    Id = 1,
                    StartByte = 0,
                    CurrentByte = 0,
                    EndByte = _item.TotalBytes > 0 ? (_item.TotalBytes - 1) : 0,
                    State = "Downloading"
                });
                _item.DownloadedBytes = 0;
            }

            _item.Status = DownloadStatus.Downloading;
            _item.ActiveConnections = 1;
            NotifyProgress();

            HttpWebRequest req = HttpHelpers.CreateRequest(
                _item.Url,
                !string.IsNullOrWhiteSpace(_item.UserAgent) ? _item.UserAgent : _settings.DefaultUserAgent,
                _item.Referer,
                _item.Cookies,
                _item.AuthUser,
                _item.AuthPassword,
                30000,
                false);

            using (token.Register(delegate { try { req.Abort(); } catch { } }))
            using (HttpWebResponse resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
            using (Stream netStream = resp.GetResponseStream())
            using (FileStream fs = new FileStream(_item.PartFilePath, FileMode.Create, FileAccess.Write, FileShare.Read, 65536))
            {
                if (resp.ContentLength > 0)
                {
                    _item.TotalBytes = resp.ContentLength;
                    _item.Segments[0].EndByte = resp.ContentLength - 1;
                }

                byte[] buffer = new byte[65536];
                long totalWritten = 0;
                Stopwatch sw = Stopwatch.StartNew();
                long winBytes = 0;

                int read;
                while ((read = await netStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                {
                    if (_globalLimiter != null && _globalLimiter.IsActive)
                    {
                        await _globalLimiter.ThrottleAsync(read, token).ConfigureAwait(false);
                    }
                    if (_itemLimiter != null && _itemLimiter.IsActive)
                    {
                        await _itemLimiter.ThrottleAsync(read, token).ConfigureAwait(false);
                    }

                    fs.Write(buffer, 0, read);
                    totalWritten += read;
                    winBytes += read;

                    lock (_segLock)
                    {
                        _item.DownloadedBytes = totalWritten;
                        _item.Segments[0].CurrentByte = totalWritten;
                        if (_item.TotalBytes <= 0 || _item.Segments[0].EndByte < totalWritten)
                        {
                            _item.Segments[0].EndByte = totalWritten;
                        }
                        if (sw.Elapsed.TotalSeconds >= 0.25)
                        {
                            _item.Segments[0].SpeedBytesPerSec = winBytes / sw.Elapsed.TotalSeconds;
                            winBytes = 0;
                            sw.Restart();
                        }
                    }
                }

                fs.Flush(true);
                _item.TotalBytes = totalWritten;
                _item.DownloadedBytes = totalWritten;
                _item.Segments[0].EndByte = Math.Max(0, totalWritten - 1);
                _item.Segments[0].CurrentByte = totalWritten;
                _item.Segments[0].State = "Completed";
                _item.Segments[0].SpeedBytesPerSec = 0;
            }

            await FinalizeCompletedPartFileAsync(token).ConfigureAwait(false);
        }

        private async Task FinalizeCompletedPartFileAsync(CancellationToken token)
        {
            _item.Status = DownloadStatus.Merging;
            _item.AddLog("Verifying file integrity (SHA-256)...");
            NotifyProgress();

            string sha256 = await HttpHelpers.ComputeSha256Async(_item.PartFilePath, token).ConfigureAwait(false);
            _item.ComputedSha256 = sha256;

            if (!string.IsNullOrWhiteSpace(_item.ExpectedSha256) &&
                !string.Equals(_item.ExpectedSha256.Trim(), sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(string.Format(
                    "SHA-256 checksum mismatch! Expected {0}, computed {1}.",
                    _item.ExpectedSha256.Trim(),
                    sha256));
            }

            string target = _item.FullTargetPath;
            if (File.Exists(target))
            {
                File.Delete(target);
            }
            File.Move(_item.PartFilePath, target);

            try
            {
                if (File.Exists(_item.MetaFilePath))
                {
                    File.Delete(_item.MetaFilePath);
                }
            }
            catch
            {
            }

            FileInfo fi = new FileInfo(target);
            _item.TotalBytes = fi.Length;
            _item.DownloadedBytes = fi.Length;
            _item.CompletedUtc = DateTime.UtcNow;
            _item.Status = DownloadStatus.Completed;
            _item.ErrorMessage = null;
            _item.AddLog(string.Format(
                "Completed: {0} ({1}) | SHA-256: {2}...",
                _item.FileName,
                FormattingHelpers.FormatBytes(fi.Length),
                sha256.Length >= 16 ? sha256.Substring(0, 16) : sha256));
        }

        private void RecomputeDownloadedBytesLocked()
        {
            long sum = 0;
            for (int i = 0; i < _item.Segments.Count; i++)
            {
                sum += _item.Segments[i].DownloadedBytes;
            }
            if (_item.TotalBytes > 0 && sum > _item.TotalBytes)
            {
                sum = _item.TotalBytes;
            }
            _item.DownloadedBytes = sum;
        }

        private void MaybeSavePartMetadataPeriodic()
        {
            long now = Environment.TickCount & Int32.MaxValue;
            long prev = Interlocked.Read(ref _lastMetaSaveTicks);
            if (now - prev >= 1000)
            {
                Interlocked.Exchange(ref _lastMetaSaveTicks, now);
                SavePartMetadataSafe();
                NotifyProgress();
            }
        }

        private void SavePartMetadataSafe()
        {
            try
            {
                PartMetadata meta = new PartMetadata();
                lock (_segLock)
                {
                    meta.Url = _item.Url;
                    meta.TotalBytes = _item.TotalBytes;
                    meta.SupportsRanges = _item.SupportsRanges;
                    meta.ETag = _item.ETag;
                    meta.LastModified = _item.LastModified;
                    meta.Segments = new List<SegmentInfo>(_item.Segments.Count);
                    for (int i = 0; i < _item.Segments.Count; i++)
                    {
                        SegmentInfo s = _item.Segments[i];
                        meta.Segments.Add(new SegmentInfo
                        {
                            Id = s.Id,
                            StartByte = s.StartByte,
                            CurrentByte = s.CurrentByte,
                            EndByte = s.EndByte,
                            State = s.State
                        });
                    }
                }

                JavaScriptSerializer ser = new JavaScriptSerializer();
                string json = ser.Serialize(meta);
                string tmp = _item.MetaFilePath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(_item.MetaFilePath))
                {
                    File.Delete(_item.MetaFilePath);
                }
                File.Move(tmp, _item.MetaFilePath);
            }
            catch
            {
            }
        }

        private void TryRestorePartMetadata()
        {
            try
            {
                if (!File.Exists(_item.PartFilePath) || !File.Exists(_item.MetaFilePath))
                {
                    return;
                }
                string json = File.ReadAllText(_item.MetaFilePath);
                JavaScriptSerializer ser = new JavaScriptSerializer();
                PartMetadata meta = ser.Deserialize<PartMetadata>(json);
                if (meta != null && meta.TotalBytes > 0 && meta.Segments != null && meta.Segments.Count > 0)
                {
                    lock (_segLock)
                    {
                        _item.TotalBytes = meta.TotalBytes;
                        _item.SupportsRanges = meta.SupportsRanges;
                        _item.ETag = meta.ETag;
                        _item.LastModified = meta.LastModified;
                        _item.Segments = meta.Segments;
                        RecomputeDownloadedBytesLocked();
                    }
                }
            }
            catch
            {
            }
        }

        private void NotifyProgress()
        {
            if (_onProgress != null)
            {
                _onProgress(_item);
            }
        }
    }
}

