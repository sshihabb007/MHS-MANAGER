using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MHSManager
{
    internal sealed class HlsSegmentSpec
    {
        public int Index { get; set; }
        public string Url { get; set; }
        public bool IsInitMap { get; set; }
        public long MediaSequence { get; set; }
        public string KeyUrl { get; set; }
        public byte[] CustomIv { get; set; }
        public long ByteRangeOffset { get; set; }
        public long ByteRangeLength { get; set; }

        public HlsSegmentSpec()
        {
            ByteRangeOffset = -1;
            ByteRangeLength = -1;
        }
    }

    public static class HlsDownloader
    {
        public static Task ExecuteAsync(
            DownloadItem item,
            string defaultUserAgent,
            TokenBucketLimiter globalLimiter,
            TokenBucketLimiter itemLimiter,
            Action<DownloadItem> onProgress,
            CancellationToken token)
        {
            AppSettings fallback = new AppSettings { DefaultUserAgent = defaultUserAgent };
            return ExecuteAsync(item, fallback, globalLimiter, itemLimiter, onProgress, token);
        }

        public static async Task ExecuteAsync(
            DownloadItem item,
            AppSettings settings,
            TokenBucketLimiter globalLimiter,
            TokenBucketLimiter itemLimiter,
            Action<DownloadItem> onProgress,
            CancellationToken token)
        {
            string defaultUserAgent = settings != null ? settings.DefaultUserAgent : null;
            item.IsHlsStream = true;
            item.SupportsRanges = true;
            item.FileName = HttpHelpers.EnsureHlsOutputFileName(item.FileName, false, item.HasExplicitFileName);

            if (!string.IsNullOrEmpty(item.SaveDirectory) && !Directory.Exists(item.SaveDirectory))
            {
                Directory.CreateDirectory(item.SaveDirectory);
            }

            item.Status = DownloadStatus.Probing;
            item.AddLog("Fetching HLS playlist: " + item.Url);
            if (onProgress != null) onProgress(item);

            string playlistUrl = item.Url;
            string playlistText = await FetchStringAsync(playlistUrl, item, defaultUserAgent, settings, token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(playlistText) || !playlistText.Contains("#EXTM3U"))
            {
                throw new InvalidOperationException("Invalid HLS M3U8 playlist response.");
            }

            // Check if Master Playlist (#EXT-X-STREAM-INF)
            if (playlistText.IndexOf("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string bestVariantUrl = SelectBestVariantUrl(playlistUrl, playlistText);
                if (string.IsNullOrEmpty(bestVariantUrl))
                {
                    throw new InvalidOperationException("No playable variant stream found in HLS master playlist.");
                }
                playlistUrl = bestVariantUrl;
                item.AddLog("Selected highest quality HLS variant: " + playlistUrl);
                playlistText = await FetchStringAsync(playlistUrl, item, defaultUserAgent, settings, token).ConfigureAwait(false);
            }

            bool isFmp4;
            List<HlsSegmentSpec> specs = ParseMediaPlaylist(playlistUrl, playlistText, out isFmp4);
            if (specs.Count == 0)
            {
                throw new InvalidOperationException("HLS media playlist contained zero media segments.");
            }

            if (isFmp4 && !item.HasExplicitFileName)
            {
                item.FileName = HttpHelpers.EnsureHlsOutputFileName(item.FileName, true, false);
            }

            item.AddLog(string.Format(
                "Parsed {0} HLS segments ({1}).",
                specs.Count,
                isFmp4 ? "fMP4 / CMAF" : "MPEG-TS"));

            string tempDir = item.PartFilePath + ".hls";
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            int completedSegments = 0;
            long totalBytesDownloaded = 0;

            lock (item.SyncRoot)
            {
                item.Segments.Clear();
                for (int i = 0; i < specs.Count; i++)
                {
                    SegmentInfo uiSeg = new SegmentInfo
                    {
                        Id = i + 1,
                        StartByte = i * 1000L,
                        CurrentByte = i * 1000L,
                        EndByte = ((i + 1) * 1000L) - 1L,
                        State = "Pending"
                    };

                    string segFile = GetSegmentPartPath(tempDir, i);
                    if (File.Exists(segFile))
                    {
                        FileInfo fi = new FileInfo(segFile);
                        if (fi.Length > 0)
                        {
                            completedSegments++;
                            totalBytesDownloaded += fi.Length;
                            uiSeg.CurrentByte = uiSeg.EndByte + 1;
                            uiSeg.State = "Completed";
                        }
                    }

                    item.Segments.Add(uiSeg);
                }
                UpdateHlsTotals(item, completedSegments, specs.Count, totalBytesDownloaded);
            }

            item.Status = DownloadStatus.Downloading;
            if (onProgress != null) onProgress(item);

            Dictionary<string, byte[]> keyCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            object keyLock = new object();
            int nextIndex = 0;

            int workerCount = Math.Max(1, Math.Min(item.MaxConnections, Math.Min(16, specs.Count)));
            List<Task> workers = new List<Task>(workerCount);
            int activeWorkers = 0;

            for (int w = 0; w < workerCount; w++)
            {
                workers.Add(Task.Run(async delegate
                {
                    Interlocked.Increment(ref activeWorkers);
                    item.ActiveConnections = Volatile.Read(ref activeWorkers);
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            int myIdx = -1;
                            lock (item.SyncRoot)
                            {
                                while (nextIndex < specs.Count)
                                {
                                    int candidate = nextIndex++;
                                    if (item.Segments[candidate].State != "Completed")
                                    {
                                        myIdx = candidate;
                                        item.Segments[candidate].State = "Downloading";
                                        break;
                                    }
                                }
                            }

                            if (myIdx < 0)
                            {
                                break;
                            }

                            HlsSegmentSpec spec = specs[myIdx];
                            byte[] keyBytes = null;
                            if (!string.IsNullOrEmpty(spec.KeyUrl))
                            {
                                lock (keyLock)
                                {
                                    keyCache.TryGetValue(spec.KeyUrl, out keyBytes);
                                }
                                if (keyBytes == null)
                                {
                                    byte[] fetchedKey = await DownloadBytesWithRetryAsync(
                                        spec.KeyUrl,
                                        -1,
                                        -1,
                                        item,
                                        defaultUserAgent,
                                        settings,
                                        globalLimiter,
                                        itemLimiter,
                                        null,
                                        token).ConfigureAwait(false);
                                    lock (keyLock)
                                    {
                                        keyCache[spec.KeyUrl] = fetchedKey;
                                        keyBytes = fetchedKey;
                                    }
                                }
                            }

                            Stopwatch sw = Stopwatch.StartNew();
                            int capturedIdx = myIdx;
                            byte[] rawPayload = await DownloadBytesWithRetryAsync(
                                spec.Url,
                                spec.ByteRangeOffset,
                                spec.ByteRangeLength,
                                item,
                                defaultUserAgent,
                                settings,
                                globalLimiter,
                                itemLimiter,
                                delegate(int chunkLen)
                                {
                                    double sec = Math.Max(0.001, sw.Elapsed.TotalSeconds);
                                    lock (item.SyncRoot)
                                    {
                                        if (capturedIdx < item.Segments.Count)
                                        {
                                            item.Segments[capturedIdx].SpeedBytesPerSec = chunkLen / sec;
                                        }
                                    }
                                },
                                token).ConfigureAwait(false);

                            if (keyBytes != null && keyBytes.Length == 16)
                            {
                                rawPayload = DecryptAes128Cbc(rawPayload, keyBytes, spec.CustomIv, spec.MediaSequence);
                            }

                            string segPath = GetSegmentPartPath(tempDir, myIdx);
                            string segTmp = segPath + ".writing";
                            File.WriteAllBytes(segTmp, rawPayload);
                            if (File.Exists(segPath))
                            {
                                File.Delete(segPath);
                            }
                            File.Move(segTmp, segPath);

                            lock (item.SyncRoot)
                            {
                                completedSegments++;
                                totalBytesDownloaded += rawPayload.Length;
                                SegmentInfo uiSeg = item.Segments[myIdx];
                                uiSeg.CurrentByte = uiSeg.EndByte + 1;
                                uiSeg.State = "Completed";
                                uiSeg.SpeedBytesPerSec = 0;
                                UpdateHlsTotals(item, completedSegments, specs.Count, totalBytesDownloaded);
                            }

                            if (onProgress != null) onProgress(item);
                        }
                    }
                    finally
                    {
                        int rem = Interlocked.Decrement(ref activeWorkers);
                        item.ActiveConnections = Math.Max(0, rem);
                    }
                }, token));
            }

            await Task.WhenAll(workers).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            // Merge all segments sequentially into item.PartFilePath
            item.Status = DownloadStatus.Merging;
            item.AddLog("Merging HLS segments into final media stream...");
            if (onProgress != null) onProgress(item);

            string partPath = item.PartFilePath;
            long mergedBytes = 0;
            using (FileStream outStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128))
            {
                byte[] copyBuf = new byte[1024 * 128];
                for (int i = 0; i < specs.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    string segPath = GetSegmentPartPath(tempDir, i);
                    if (!File.Exists(segPath))
                    {
                        throw new IOException("Missing downloaded HLS segment #" + i);
                    }
                    using (FileStream segIn = new FileStream(segPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        int r;
                        while ((r = segIn.Read(copyBuf, 0, copyBuf.Length)) > 0)
                        {
                            outStream.Write(copyBuf, 0, r);
                            mergedBytes += r;
                        }
                    }
                }
                outStream.Flush(true);
            }

            string sha256 = await HttpHelpers.ComputeSha256Async(partPath, token).ConfigureAwait(false);
            item.ComputedSha256 = sha256;
            if (!string.IsNullOrWhiteSpace(item.ExpectedSha256) &&
                !string.Equals(item.ExpectedSha256.Trim(), sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(string.Format(
                    "SHA-256 verification failed. Expected {0}, got {1}.",
                    item.ExpectedSha256.Trim(),
                    sha256));
            }

            string oldMetaPath = item.MetaFilePath;
            string targetPath = item.FullTargetPath;
            if (File.Exists(targetPath))
            {
                if (item.OverwriteExisting)
                {
                    File.Delete(targetPath);
                }
                else
                {
                    item.FileName = DownloadEngine.EnsureUniqueDiskFileName(item.SaveDirectory, item.FileName);
                    targetPath = item.FullTargetPath;
                }
            }
            File.Move(partPath, targetPath);

            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                if (File.Exists(oldMetaPath))
                {
                    File.Delete(oldMetaPath);
                }
            }
            catch
            {
            }

            item.TotalBytes = mergedBytes;
            item.DownloadedBytes = mergedBytes;
            item.CurrentSpeedBytesPerSec = 0;
            item.RetryCount = 0;
            item.NextRetryUtc = null;
            item.CompletedUtc = DateTime.UtcNow;
            item.Status = DownloadStatus.Completed;
            item.ErrorMessage = null;
            item.AddLog(string.Format("HLS stream merged ({0}, SHA-256: {1}...)",
                FormattingHelpers.FormatBytes(mergedBytes),
                sha256.Length >= 12 ? sha256.Substring(0, 12) : sha256));
            if (onProgress != null) onProgress(item);
        }

        private static void UpdateHlsTotals(DownloadItem item, int completedCount, int totalCount, long downloadedBytes)
        {
            item.DownloadedBytes = downloadedBytes;
            if (completedCount >= totalCount && totalCount > 0)
            {
                item.TotalBytes = downloadedBytes;
            }
            else if (completedCount > 0)
            {
                long est = (long)Math.Round(((double)downloadedBytes / completedCount) * totalCount);
                item.TotalBytes = Math.Max(downloadedBytes, est);
            }
            else
            {
                item.TotalBytes = 0;
            }
        }

        private static string GetSegmentPartPath(string tempDir, int index)
        {
            return Path.Combine(tempDir, string.Format("seg_{0:D6}.part", index));
        }

        private static string SelectBestVariantUrl(string masterUrl, string masterText)
        {
            Uri baseUri = new Uri(masterUrl);
            string[] lines = masterText.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            long bestBandwidth = -1;
            string bestUrl = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.OrdinalIgnoreCase))
                {
                    long bw = 0;
                    Match bwMatch = Regex.Match(line, @"BANDWIDTH=(\d+)", RegexOptions.IgnoreCase);
                    if (bwMatch.Success)
                    {
                        long.TryParse(bwMatch.Groups[1].Value, out bw);
                    }

                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        string next = lines[j].Trim();
                        if (string.IsNullOrEmpty(next)) continue;
                        if (next.StartsWith("#")) continue;

                        Uri resolved = new Uri(baseUri, next);
                        if (bw > bestBandwidth || bestUrl == null)
                        {
                            bestBandwidth = bw;
                            bestUrl = resolved.AbsoluteUri;
                        }
                        i = j;
                        break;
                    }
                }
            }

            return bestUrl;
        }

        private static bool TryParseByteRangeSpec(string spec, ref long lastEndExclusive, out long offset, out long length)
        {
            offset = -1;
            length = -1;
            if (string.IsNullOrWhiteSpace(spec)) return false;

            string trimmed = spec.Trim().Trim('"');
            int atIdx = trimmed.IndexOf('@');
            if (atIdx > 0)
            {
                long parsedLen;
                long parsedOff;
                if (long.TryParse(trimmed.Substring(0, atIdx).Trim(), out parsedLen) &&
                    long.TryParse(trimmed.Substring(atIdx + 1).Trim(), out parsedOff) &&
                    parsedLen > 0 && parsedOff >= 0)
                {
                    length = parsedLen;
                    offset = parsedOff;
                    lastEndExclusive = offset + length;
                    return true;
                }
            }
            else
            {
                long parsedLen;
                if (long.TryParse(trimmed, out parsedLen) && parsedLen > 0)
                {
                    length = parsedLen;
                    offset = lastEndExclusive >= 0 ? lastEndExclusive : 0;
                    lastEndExclusive = offset + length;
                    return true;
                }
            }
            return false;
        }

        private static List<HlsSegmentSpec> ParseMediaPlaylist(string mediaUrl, string mediaText, out bool isFmp4)
        {
            isFmp4 = false;
            Uri baseUri = new Uri(mediaUrl);
            List<HlsSegmentSpec> list = new List<HlsSegmentSpec>();
            string[] lines = mediaText.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            long mediaSeq = 0;
            string currentKeyUrl = null;
            byte[] currentIv = null;
            bool pendingInf = false;
            long pendingByteRangeOffset = -1;
            long pendingByteRangeLength = -1;
            long lastMediaByteRangeEnd = 0;
            long lastMapByteRangeEnd = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.OrdinalIgnoreCase))
                {
                    string seqStr = line.Substring("#EXT-X-MEDIA-SEQUENCE:".Length).Trim();
                    long parsedSeq;
                    if (long.TryParse(seqStr, out parsedSeq))
                    {
                        mediaSeq = parsedSeq;
                    }
                }
                else if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                {
                    pendingInf = true;
                }
                else if (line.StartsWith("#EXT-X-BYTERANGE:", StringComparison.OrdinalIgnoreCase))
                {
                    string brRaw = line.Substring("#EXT-X-BYTERANGE:".Length).Trim();
                    long brOff;
                    long brLen;
                    if (TryParseByteRangeSpec(brRaw, ref lastMediaByteRangeEnd, out brOff, out brLen))
                    {
                        pendingByteRangeOffset = brOff;
                        pendingByteRangeLength = brLen;
                    }
                }
                else if (line.StartsWith("#EXT-X-KEY:", StringComparison.OrdinalIgnoreCase))
                {
                    if (line.IndexOf("KEYFORMAT=", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        (line.IndexOf("widevine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         line.IndexOf("playready", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         line.IndexOf("streamingkeydelivery", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        throw new InvalidOperationException("DRM-protected HLS streams are not supported.");
                    }

                    Match methodMatch = Regex.Match(line, @"METHOD=([^,\s]+)", RegexOptions.IgnoreCase);
                    string method = methodMatch.Success ? methodMatch.Groups[1].Value.Trim('"') : "NONE";
                    if (string.Equals(method, "NONE", StringComparison.OrdinalIgnoreCase))
                    {
                        currentKeyUrl = null;
                        currentIv = null;
                    }
                    else if (string.Equals(method, "AES-128", StringComparison.OrdinalIgnoreCase))
                    {
                        Match uriMatch = Regex.Match(line, @"URI=""([^""]+)""", RegexOptions.IgnoreCase);
                        if (uriMatch.Success)
                        {
                            currentKeyUrl = new Uri(baseUri, uriMatch.Groups[1].Value).AbsoluteUri;
                        }
                        Match ivMatch = Regex.Match(line, @"IV=0x([0-9a-fA-F]{1,32})", RegexOptions.IgnoreCase);
                        if (ivMatch.Success)
                        {
                            currentIv = ParseHex16(ivMatch.Groups[1].Value);
                        }
                        else
                        {
                            currentIv = null;
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("Unsupported HLS encryption method: " + method);
                    }
                }
                else if (line.StartsWith("#EXT-X-MAP:", StringComparison.OrdinalIgnoreCase))
                {
                    Match mapMatch = Regex.Match(line, @"URI=""([^""]+)""", RegexOptions.IgnoreCase);
                    if (mapMatch.Success)
                    {
                        string mapUrl = new Uri(baseUri, mapMatch.Groups[1].Value).AbsoluteUri;
                        if (!mapUrl.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
                        {
                            isFmp4 = true;
                        }

                        long mapBrOff = -1;
                        long mapBrLen = -1;
                        Match mapBrMatch = Regex.Match(line, @"BYTERANGE=""([^""]+)""", RegexOptions.IgnoreCase);
                        if (mapBrMatch.Success)
                        {
                            TryParseByteRangeSpec(mapBrMatch.Groups[1].Value, ref lastMapByteRangeEnd, out mapBrOff, out mapBrLen);
                        }

                        list.Add(new HlsSegmentSpec
                        {
                            Index = list.Count,
                            Url = mapUrl,
                            IsInitMap = true,
                            MediaSequence = mediaSeq,
                            KeyUrl = currentKeyUrl,
                            CustomIv = currentIv,
                            ByteRangeOffset = mapBrOff,
                            ByteRangeLength = mapBrLen
                        });
                    }
                }
                else if (!line.StartsWith("#"))
                {
                    if (!pendingInf)
                    {
                        continue;
                    }

                    string segUrl = new Uri(baseUri, line).AbsoluteUri;
                    if (segUrl.IndexOf(".m4s", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        segUrl.IndexOf(".cmfv", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isFmp4 = true;
                    }

                    list.Add(new HlsSegmentSpec
                    {
                        Index = list.Count,
                        Url = segUrl,
                        IsInitMap = false,
                        MediaSequence = mediaSeq,
                        KeyUrl = currentKeyUrl,
                        CustomIv = currentIv,
                        ByteRangeOffset = pendingByteRangeOffset,
                        ByteRangeLength = pendingByteRangeLength
                    });
                    mediaSeq++;
                    pendingInf = false;
                    pendingByteRangeOffset = -1;
                    pendingByteRangeLength = -1;
                }
            }

            return list;
        }

        private static byte[] ParseHex16(string hex)
        {
            string padded = hex.PadLeft(32, '0');
            byte[] iv = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                iv[i] = byte.Parse(padded.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return iv;
        }

        private static byte[] DecryptAes128Cbc(byte[] cipherBytes, byte[] keyBytes, byte[] customIv, long mediaSeq)
        {
            byte[] iv = customIv;
            if (iv == null)
            {
                iv = new byte[16];
                byte[] seqBytes = BitConverter.GetBytes(mediaSeq);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(seqBytes);
                }
                Buffer.BlockCopy(seqBytes, 0, iv, 8, 8);
            }

            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 128;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = keyBytes;
                aes.IV = iv;

                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                {
                    return decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                }
            }
        }

        private static async Task<string> FetchStringAsync(
            string url,
            DownloadItem item,
            string defaultUserAgent,
            AppSettings settings,
            CancellationToken token)
        {
            HttpWebRequest req = HttpHelpers.CreateRequest(
                url,
                !string.IsNullOrWhiteSpace(item.UserAgent) ? item.UserAgent : defaultUserAgent,
                item.Referer,
                item.Cookies,
                item.AuthUser,
                item.AuthPassword,
                20000,
                true,
                settings);

            using (token.Register(delegate { try { req.Abort(); } catch { } }))
            using (HttpWebResponse resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
            using (Stream s = resp.GetResponseStream())
            using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
            {
                return await sr.ReadToEndAsync().ConfigureAwait(false);
            }
        }

        private static async Task<byte[]> DownloadBytesWithRetryAsync(
            string url,
            long byteRangeOffset,
            long byteRangeLength,
            DownloadItem item,
            string defaultUserAgent,
            AppSettings settings,
            TokenBucketLimiter globalLimiter,
            TokenBucketLimiter itemLimiter,
            Action<int> onChunk,
            CancellationToken token)
        {
            Exception lastErr = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    HttpWebRequest req = HttpHelpers.CreateRequest(
                        url,
                        !string.IsNullOrWhiteSpace(item.UserAgent) ? item.UserAgent : defaultUserAgent,
                        item.Referer,
                        item.Cookies,
                        item.AuthUser,
                        item.AuthPassword,
                        25000,
                        false,
                        settings);

                    if (byteRangeOffset >= 0 && byteRangeLength > 0)
                    {
                        req.AddRange(byteRangeOffset, byteRangeOffset + byteRangeLength - 1);
                    }

                    using (token.Register(delegate { try { req.Abort(); } catch { } }))
                    using (HttpWebResponse resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
                    using (Stream netStream = resp.GetResponseStream())
                    using (MemoryStream ms = new MemoryStream())
                    {
                        byte[] buf = new byte[32768];
                        int read;
                        while ((read = await netStream.ReadAsync(buf, 0, buf.Length, token).ConfigureAwait(false)) > 0)
                        {
                            if (globalLimiter != null && globalLimiter.IsActive)
                            {
                                await globalLimiter.ThrottleAsync(read, token).ConfigureAwait(false);
                            }
                            if (itemLimiter != null && itemLimiter.IsActive)
                            {
                                await itemLimiter.ThrottleAsync(read, token).ConfigureAwait(false);
                            }
                            ms.Write(buf, 0, read);
                            if (onChunk != null) onChunk((int)ms.Length);
                        }
                        return ms.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) throw new OperationCanceledException(token);
                    lastErr = ex;
                }

                if (attempt < 3)
                {
                    await Task.Delay(400 * attempt, token).ConfigureAwait(false);
                }
            }

            throw lastErr ?? new IOException("Failed to download HLS segment: " + url);
        }
    }
}
