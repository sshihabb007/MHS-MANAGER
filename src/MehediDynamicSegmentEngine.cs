using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ShihabMehediIDM
{
    public class ShihabProbeMetadata
    {
        public bool MehediSuccess { get; set; }
        public string ShihabFinalUrl { get; set; }
        public string MehediSuggestedFileName { get; set; }
        public long ShihabContentLength { get; set; }
        public bool MehediAcceptsRanges { get; set; }
        public string ShihabContentType { get; set; }
        public string MehediLastModifiedHeader { get; set; }
        public bool ShihabIsHtmlPage { get; set; }
        public bool MehediIsHlsPlaylist { get; set; }
        public string ShihabErrorMessage { get; set; }
    }

    public class MehediDynamicSegmentEngine
    {
        private readonly ShihabStateStore _shihabStore;
        private readonly MehediAppConfiguration _mehediConfig;
        private readonly List<ShihabDownloadItem> _shihabDownloads;
        private readonly Dictionary<string, CancellationTokenSource> _mehediActiveCts;
        private readonly object _shihabEngineLock = new object();
        private readonly object _mehediRateLock = new object();
        private long _shihabGlobalWindowStartMs = 0;
        private long _mehediGlobalBytesInWindow = 0;

        public event Action<ShihabDownloadItem> OnShihabItemUpdated;
        public event Action<ShihabDownloadItem> OnMehediItemCompleted;
        public event Action<ShihabDownloadItem> OnShihabItemFailed;
        public event Action<string> OnShihabEngineLog;

        public MehediDynamicSegmentEngine(
            ShihabStateStore shihabStore,
            MehediAppConfiguration mehediConfig,
            List<ShihabDownloadItem> shihabDownloads)
        {
            _shihabStore = shihabStore;
            _mehediConfig = mehediConfig;
            _shihabDownloads = shihabDownloads;
            _mehediActiveCts = new Dictionary<string, CancellationTokenSource>();

            ServicePointManager.DefaultConnectionLimit = 256;
            ServicePointManager.Expect100Continue = false;
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls | (SecurityProtocolType)12288;
            }
            catch
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
        }

        public List<ShihabDownloadItem> GetShihabDownloadsSnapshot()
        {
            lock (_shihabEngineLock)
            {
                return new List<ShihabDownloadItem>(_shihabDownloads);
            }
        }

        public bool IsMehediItemActive(string shihabItemId)
        {
            lock (_shihabEngineLock)
            {
                return _mehediActiveCts.ContainsKey(shihabItemId);
            }
        }

        public static bool IsShihabGoogleVideoUrl(string mehediUrl)
        {
            if (string.IsNullOrWhiteSpace(mehediUrl)) return false;
            Uri shihabUri;
            if (!Uri.TryCreate(mehediUrl, UriKind.Absolute, out shihabUri)) return false;
            string mehediHost = (shihabUri.Host ?? "").ToLowerInvariant();
            return mehediHost.EndsWith(".googlevideo.com", StringComparison.Ordinal) ||
                   mehediHost == "googlevideo.com" ||
                   shihabUri.AbsolutePath.IndexOf("/videoplayback", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string SanitizeMehediGoogleVideoUrl(string shihabRawUrl, out long mehediParsedClen)
        {
            mehediParsedClen = -1L;
            if (string.IsNullOrWhiteSpace(shihabRawUrl)) return shihabRawUrl;
            Uri shihabUri;
            if (!Uri.TryCreate(shihabRawUrl, UriKind.Absolute, out shihabUri)) return shihabRawUrl;

            string mehediQuery = shihabUri.Query;
            if (string.IsNullOrEmpty(mehediQuery) || mehediQuery == "?")
            {
                return shihabRawUrl;
            }

            string[] shihabPairs = mehediQuery.TrimStart('?').Split(new char[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
            bool mehediRangeIsSigned = false;
            for (int shihabI = 0; shihabI < shihabPairs.Length; shihabI++)
            {
                int mehediEq = shihabPairs[shihabI].IndexOf('=');
                if (mehediEq <= 0) continue;
                string shihabKey = shihabPairs[shihabI].Substring(0, mehediEq);
                string mehediVal = Uri.UnescapeDataString(shihabPairs[shihabI].Substring(mehediEq + 1));
                if (string.Equals(shihabKey, "clen", StringComparison.OrdinalIgnoreCase))
                {
                    long shihabClen;
                    if (long.TryParse(mehediVal, out shihabClen) && shihabClen > 0)
                    {
                        mehediParsedClen = shihabClen;
                    }
                }
                else if (string.Equals(shihabKey, "sparams", StringComparison.OrdinalIgnoreCase))
                {
                    string[] shihabSigned = mehediVal.Split(',');
                    for (int mehediS = 0; mehediS < shihabSigned.Length; mehediS++)
                    {
                        if (string.Equals(shihabSigned[mehediS].Trim(), "range", StringComparison.OrdinalIgnoreCase))
                        {
                            mehediRangeIsSigned = true;
                        }
                    }
                }
            }

            List<string> mehediKept = new List<string>(shihabPairs.Length);
            for (int shihabI = 0; shihabI < shihabPairs.Length; shihabI++)
            {
                int mehediEq = shihabPairs[shihabI].IndexOf('=');
                string shihabKey = mehediEq > 0 ? shihabPairs[shihabI].Substring(0, mehediEq) : shihabPairs[shihabI];
                if (string.Equals(shihabKey, "alr", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(shihabKey, "rn", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(shihabKey, "rbuf", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(shihabKey, "ump", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(shihabKey, "sabr", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!mehediRangeIsSigned && string.Equals(shihabKey, "range", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                mehediKept.Add(shihabPairs[shihabI]);
            }

            string shihabBase = shihabUri.GetLeftPart(UriPartial.Path);
            return mehediKept.Count > 0 ? (shihabBase + "?" + string.Join("&", mehediKept.ToArray())) : shihabBase;
        }

        private static string AppendShihabQueryRangeParam(string mehediCleanUrl, long shihabStart, long mehediEnd)
        {
            string shihabSep = mehediCleanUrl.IndexOf('?') >= 0 ? "&" : "?";
            return string.Format("{0}{1}range={2}-{3}", mehediCleanUrl, shihabSep, shihabStart, mehediEnd);
        }

        private HttpWebRequest CreateShihabHttpRequest(
            string mehediUrl,
            string shihabMethod,
            ShihabDownloadItem mehediContextItem,
            long shihabRangeStart,
            long mehediRangeEnd)
        {
            HttpWebRequest shihabReq = (HttpWebRequest)WebRequest.Create(mehediUrl);
            shihabReq.Method = shihabMethod;
            shihabReq.AllowAutoRedirect = true;
            shihabReq.MaximumAutomaticRedirections = 12;
            shihabReq.Timeout = 25000;
            shihabReq.ReadWriteTimeout = 30000;
            shihabReq.KeepAlive = true;
            shihabReq.Accept = "*/*";
            shihabReq.AutomaticDecompression = DecompressionMethods.None;

            string mehediUa = (mehediContextItem != null && !string.IsNullOrWhiteSpace(mehediContextItem.MehediUserAgent))
                ? mehediContextItem.MehediUserAgent
                : _mehediConfig.ShihabDefaultUserAgent;
            if (!string.IsNullOrWhiteSpace(mehediUa))
            {
                shihabReq.UserAgent = mehediUa;
            }
            if (mehediContextItem != null && !string.IsNullOrWhiteSpace(mehediContextItem.MehediReferer))
            {
                try { shihabReq.Referer = mehediContextItem.MehediReferer; } catch { }
            }
            if (mehediContextItem != null && !string.IsNullOrWhiteSpace(mehediContextItem.MehediOriginHeader))
            {
                try { shihabReq.Headers["Origin"] = mehediContextItem.MehediOriginHeader; } catch { }
            }
            if (mehediContextItem != null && !mehediContextItem.MehediOmitCookies && !string.IsNullOrWhiteSpace(mehediContextItem.ShihabCookies))
            {
                shihabReq.Headers[HttpRequestHeader.Cookie] = mehediContextItem.ShihabCookies;
            }

            string shihabUser = mehediContextItem != null ? mehediContextItem.ShihabAuthUser : "";
            string mehediPass = mehediContextItem != null ? mehediContextItem.MehediAuthPassword : "";
            if (string.IsNullOrWhiteSpace(shihabUser))
            {
                ShihabSiteLoginCredential shihabSavedCred = _mehediConfig.FindShihabCredentialForUrl(mehediUrl);
                if (shihabSavedCred != null)
                {
                    shihabUser = shihabSavedCred.MehediUserName;
                    mehediPass = shihabSavedCred.ShihabPassword;
                }
            }
            if (!string.IsNullOrWhiteSpace(shihabUser))
            {
                string mehediBasic = Convert.ToBase64String(Encoding.UTF8.GetBytes(shihabUser + ":" + (mehediPass ?? "")));
                shihabReq.Headers[HttpRequestHeader.Authorization] = "Basic " + mehediBasic;
            }

            if (_mehediConfig.MehediUseHttpProxy && !string.IsNullOrWhiteSpace(_mehediConfig.ShihabProxyAddress))
            {
                WebProxy shihabProxy = new WebProxy(_mehediConfig.ShihabProxyAddress.Trim(), _mehediConfig.MehediProxyPort);
                if (!string.IsNullOrWhiteSpace(_mehediConfig.ShihabProxyUser))
                {
                    shihabProxy.Credentials = new NetworkCredential(_mehediConfig.ShihabProxyUser, _mehediConfig.MehediProxyPassword ?? "");
                }
                shihabReq.Proxy = shihabProxy;
            }

            if (shihabRangeStart >= 0)
            {
                if (mehediRangeEnd >= shihabRangeStart)
                {
                    shihabReq.AddRange(shihabRangeStart, mehediRangeEnd);
                }
                else
                {
                    shihabReq.AddRange(shihabRangeStart);
                }
            }

            return shihabReq;
        }

        public ShihabProbeMetadata ProbeMehediUrl(string shihabUrl, ShihabDownloadItem mehediContextItem)
        {
            ShihabProbeMetadata shihabMeta = new ShihabProbeMetadata();
            shihabMeta.ShihabFinalUrl = shihabUrl;
            shihabMeta.ShihabContentLength = -1;
            shihabMeta.MehediSuggestedFileName = ShihabFormatHelpers.ExtractMehediFileNameFromUrl(shihabUrl);

            if (IsShihabGoogleVideoUrl(shihabUrl))
            {
                long mehediGvClen;
                string shihabCleanGv = SanitizeMehediGoogleVideoUrl(shihabUrl, out mehediGvClen);
                shihabMeta.ShihabFinalUrl = shihabCleanGv;
                if (mehediGvClen > 0)
                {
                    shihabMeta.ShihabContentLength = mehediGvClen;
                    shihabMeta.MehediAcceptsRanges = true;
                }
                try
                {
                    string mehediMiniSliceUrl = AppendShihabQueryRangeParam(shihabCleanGv, 0, 511);
                    HttpWebRequest shihabGvProbe = CreateShihabHttpRequest(mehediMiniSliceUrl, "GET", mehediContextItem, -1, -1);
                    using (HttpWebResponse mehediGvResp = (HttpWebResponse)shihabGvProbe.GetResponse())
                    {
                        shihabMeta.ShihabContentType = mehediGvResp.ContentType ?? "";
                        if (shihabMeta.ShihabContentLength <= 0 && mehediGvResp.ContentLength > 512)
                        {
                            shihabMeta.ShihabContentLength = mehediGvResp.ContentLength;
                        }
                    }
                    shihabMeta.MehediSuccess = true;
                    return shihabMeta;
                }
                catch (Exception shihabGvEx)
                {
                    shihabMeta.MehediSuccess = mehediGvClen > 0;
                    shihabMeta.ShihabErrorMessage = shihabGvEx.Message;
                    return shihabMeta;
                }
            }

            try
            {
                HttpWebRequest mehediRangeProbe = CreateShihabHttpRequest(shihabUrl, "GET", mehediContextItem, 0, 0);
                using (HttpWebResponse shihabResp = (HttpWebResponse)mehediRangeProbe.GetResponse())
                {
                    PopulateShihabProbeFromResponse(shihabMeta, shihabResp);
                }
                shihabMeta.MehediSuccess = true;
                return shihabMeta;
            }
            catch
            {
            }

            try
            {
                HttpWebRequest shihabHeadReq = CreateShihabHttpRequest(shihabUrl, "HEAD", mehediContextItem, -1, -1);
                using (HttpWebResponse mehediResp = (HttpWebResponse)shihabHeadReq.GetResponse())
                {
                    PopulateShihabProbeFromResponse(shihabMeta, mehediResp);
                }
                shihabMeta.MehediSuccess = true;
                return shihabMeta;
            }
            catch (Exception shihabEx)
            {
                shihabMeta.MehediSuccess = false;
                shihabMeta.ShihabErrorMessage = shihabEx.Message;
                return shihabMeta;
            }
        }

        private static void PopulateShihabProbeFromResponse(ShihabProbeMetadata mehediMeta, HttpWebResponse shihabResp)
        {
            if (shihabResp.ResponseUri != null)
            {
                mehediMeta.ShihabFinalUrl = shihabResp.ResponseUri.AbsoluteUri;
                mehediMeta.MehediSuggestedFileName = ShihabFormatHelpers.ExtractMehediFileNameFromUrl(mehediMeta.ShihabFinalUrl);
            }
            mehediMeta.ShihabContentType = shihabResp.ContentType ?? "";
            mehediMeta.MehediLastModifiedHeader = shihabResp.Headers["Last-Modified"] ?? "";

            string shihabContentDisp = shihabResp.Headers["Content-Disposition"] ?? "";
            string mehediParsedName = ParseShihabContentDispositionName(shihabContentDisp);
            if (!string.IsNullOrWhiteSpace(mehediParsedName))
            {
                mehediMeta.MehediSuggestedFileName = ShihabFormatHelpers.SanitizeShihabFileName(mehediParsedName);
            }

            string shihabContentRange = shihabResp.Headers["Content-Range"] ?? "";
            string mehediAcceptRanges = shihabResp.Headers["Accept-Ranges"] ?? "";
            if (shihabResp.StatusCode == HttpStatusCode.PartialContent && !string.IsNullOrEmpty(shihabContentRange))
            {
                mehediMeta.MehediAcceptsRanges = true;
                Match shihabMatch = Regex.Match(shihabContentRange, @"/(\d+)");
                long mehediTotalParsed;
                if (shihabMatch.Success && long.TryParse(shihabMatch.Groups[1].Value, out mehediTotalParsed))
                {
                    mehediMeta.ShihabContentLength = mehediTotalParsed;
                }
            }
            else
            {
                if (mehediAcceptRanges.IndexOf("bytes", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    mehediMeta.MehediAcceptsRanges = true;
                }
                if (shihabResp.ContentLength >= 0)
                {
                    mehediMeta.ShihabContentLength = shihabResp.ContentLength;
                }
            }

            string shihabCtLower = mehediMeta.ShihabContentType.ToLowerInvariant();
            if (shihabCtLower.Contains("text/html"))
            {
                mehediMeta.ShihabIsHtmlPage = true;
            }
            if (shihabCtLower.Contains("mpegurl") ||
                mehediMeta.ShihabFinalUrl.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mehediMeta.MehediIsHlsPlaylist = true;
            }
        }

        private static string ParseShihabContentDispositionName(string mehediHeader)
        {
            if (string.IsNullOrWhiteSpace(mehediHeader))
            {
                return "";
            }
            Match shihabUtf8Match = Regex.Match(mehediHeader, @"filename\*\s*=\s*UTF-8''([^;]+)", RegexOptions.IgnoreCase);
            if (shihabUtf8Match.Success)
            {
                return Uri.UnescapeDataString(shihabUtf8Match.Groups[1].Value.Trim().Trim('"'));
            }
            Match mehediSimpleMatch = Regex.Match(mehediHeader, @"filename\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (mehediSimpleMatch.Success)
            {
                return mehediSimpleMatch.Groups[1].Value.Trim();
            }
            Match shihabUnquoted = Regex.Match(mehediHeader, @"filename\s*=\s*([^;]+)", RegexOptions.IgnoreCase);
            if (shihabUnquoted.Success)
            {
                return shihabUnquoted.Groups[1].Value.Trim().Trim('"');
            }
            return "";
        }

        public void AddShihabDownloadItem(ShihabDownloadItem mehediItem, bool shihabStartImmediately)
        {
            lock (_shihabEngineLock)
            {
                _shihabDownloads.Insert(0, mehediItem);
                _shihabStore.SaveShihabDownloads(_shihabDownloads);
            }
            NotifyShihabItemChanged(mehediItem);
            if (shihabStartImmediately)
            {
                StartOrResumeMehediDownload(mehediItem);
            }
        }

        public void StartOrResumeMehediDownload(ShihabDownloadItem shihabItem)
        {
            if (shihabItem == null) return;
            CancellationTokenSource mehediCts;
            lock (_shihabEngineLock)
            {
                if (_mehediActiveCts.ContainsKey(shihabItem.ShihabId))
                {
                    return;
                }
                mehediCts = new CancellationTokenSource();
                _mehediActiveCts[shihabItem.ShihabId] = mehediCts;
            }

            shihabItem.MehediLastTryDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            shihabItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabConnecting, "Connecting...");
            NotifyShihabItemChanged(shihabItem);

            Task.Run(delegate
            {
                ExecuteShihabDownloadLifecycle(shihabItem, mehediCts.Token);
            });
        }

        public void PauseShihabDownload(ShihabDownloadItem mehediItem)
        {
            if (mehediItem == null) return;
            CancellationTokenSource shihabCts = null;
            lock (_shihabEngineLock)
            {
                if (_mehediActiveCts.TryGetValue(mehediItem.ShihabId, out shihabCts))
                {
                    _mehediActiveCts.Remove(mehediItem.ShihabId);
                }
            }
            if (shihabCts != null)
            {
                shihabCts.Cancel();
            }
            if (mehediItem.GetShihabStatusEnum() != ShihabDownloadStatus.ShihabCompleted)
            {
                mehediItem.ShihabCurrentSpeedBps = 0;
                mehediItem.MehediEtaSeconds = -1;
                mehediItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabPaused, string.Format("Paused ({0:0.0}%)", mehediItem.GetMehediPercent()));
                PersistShihabState();
                NotifyShihabItemChanged(mehediItem);
            }
        }

        public void StopAllMehediDownloads()
        {
            List<ShihabDownloadItem> shihabSnap = GetShihabDownloadsSnapshot();
            for (int mehediI = 0; mehediI < shihabSnap.Count; mehediI++)
            {
                if (IsMehediItemActive(shihabSnap[mehediI].ShihabId))
                {
                    PauseShihabDownload(shihabSnap[mehediI]);
                }
            }
        }

        public void RemoveShihabDownload(ShihabDownloadItem mehediItem, bool shihabDeleteCompletedDiskFile)
        {
            if (mehediItem == null) return;
            PauseShihabDownload(mehediItem);
            lock (_shihabEngineLock)
            {
                _shihabDownloads.Remove(mehediItem);
                _shihabStore.SaveShihabDownloads(_shihabDownloads);
            }
            try
            {
                string mehediPartDir = GetShihabItemTempFolder(mehediItem);
                if (Directory.Exists(mehediPartDir))
                {
                    Directory.Delete(mehediPartDir, true);
                }
                if (shihabDeleteCompletedDiskFile)
                {
                    string shihabTarget = mehediItem.GetShihabFullTargetPath();
                    if (File.Exists(shihabTarget))
                    {
                        File.Delete(shihabTarget);
                    }
                }
            }
            catch
            {
            }
        }

        private string GetShihabItemTempFolder(ShihabDownloadItem mehediItem)
        {
            return Path.Combine(_mehediConfig.MehediTemporaryDirectory, mehediItem.ShihabId);
        }

        private void ExecuteShihabDownloadLifecycle(ShihabDownloadItem shihabItem, CancellationToken mehediToken)
        {
            object shihabSegmentLock = new object();
            try
            {
                Directory.CreateDirectory(_mehediConfig.MehediTemporaryDirectory);
                string mehediItemTempDir = GetShihabItemTempFolder(shihabItem);
                Directory.CreateDirectory(mehediItemTempDir);

                if (string.IsNullOrWhiteSpace(shihabItem.MehediSaveDirectory))
                {
                    ShihabCategoryRule shihabCat = _mehediConfig.ResolveMehediCategoryForFile(shihabItem.ShihabFileName);
                    shihabItem.ShihabCategoryName = shihabCat.ShihabName;
                    shihabItem.MehediSaveDirectory = shihabCat.ShihabDefaultFolder;
                }
                Directory.CreateDirectory(shihabItem.MehediSaveDirectory);

                bool mehediIsGvUrl = IsShihabGoogleVideoUrl(shihabItem.MehediUrl);
                if (mehediIsGvUrl)
                {
                    long shihabGvClen;
                    shihabItem.MehediUrl = SanitizeMehediGoogleVideoUrl(shihabItem.MehediUrl, out shihabGvClen);
                    if (shihabGvClen > 0 && shihabItem.ShihabTotalBytes <= 0)
                    {
                        shihabItem.ShihabTotalBytes = shihabGvClen;
                    }
                }

                ShihabProbeMetadata mehediProbe = ProbeMehediUrl(shihabItem.MehediUrl, shihabItem);
                if (mehediToken.IsCancellationRequested) return;

                if (mehediProbe.MehediSuccess)
                {
                    if (mehediProbe.MehediIsHlsPlaylist || shihabItem.MehediUrl.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        shihabItem.MehediIsHlsStream = true;
                        ExecuteMehediHlsStreamDownload(shihabItem, mehediProbe.ShihabFinalUrl, mehediItemTempDir, mehediToken);
                        return;
                    }
                    if (shihabItem.ShihabTotalBytes <= 0 && mehediProbe.ShihabContentLength > 0)
                    {
                        shihabItem.ShihabTotalBytes = mehediProbe.ShihabContentLength;
                    }
                    shihabItem.ShihabResumeSupported = mehediProbe.MehediAcceptsRanges && shihabItem.ShihabTotalBytes > 0;
                    if (!string.IsNullOrWhiteSpace(mehediProbe.MehediLastModifiedHeader))
                    {
                        shihabItem.ShihabServerLastModified = mehediProbe.MehediLastModifiedHeader;
                    }
                    if ((shihabItem.ShihabFileName == "download.bin" || shihabItem.ShihabFileName == "shihab_download.bin") &&
                        !string.IsNullOrWhiteSpace(mehediProbe.MehediSuggestedFileName))
                    {
                        shihabItem.ShihabFileName = ShihabFormatHelpers.EnsureShihabUniqueFilePath(
                            shihabItem.MehediSaveDirectory,
                            mehediProbe.MehediSuggestedFileName);
                    }
                }
                else if (shihabItem.MehediUrl.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    shihabItem.MehediIsHlsStream = true;
                    ExecuteMehediHlsStreamDownload(shihabItem, shihabItem.MehediUrl, mehediItemTempDir, mehediToken);
                    return;
                }

                bool mehediHasCompanionAudio = !string.IsNullOrWhiteSpace(shihabItem.MehediAudioCompanionUrl);
                string shihabPrimaryAssembledPath = mehediHasCompanionAudio
                    ? Path.Combine(mehediItemTempDir, "primary_video_raw.part")
                    : shihabItem.GetShihabFullTargetPath();
                bool mehediShaAlreadyComputed = false;

                if (mehediIsGvUrl)
                {
                    DownloadMehediGoogleVideoOrRelay(shihabItem, shihabItem.MehediUrl, shihabPrimaryAssembledPath, shihabItem.ShihabTotalBytes, true, mehediToken);
                }
                else
                {
                    PrepareInitialShihabSegments(shihabItem, mehediItemTempDir);
                    PersistShihabState();

                    shihabItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabDownloading, "Receiving data...");
                    NotifyShihabItemChanged(shihabItem);

                    Stopwatch mehediRateWatch = Stopwatch.StartNew();
                    long shihabLastBytesSnapshot = CalculateMehediDownloadedSum(shihabItem, shihabSegmentLock);
                    long mehediLastRateTickMs = 0;

                    List<Task> shihabActiveTasks = new List<Task>();
                    int mehediNextSegmentId = shihabItem.MehediSegments.Count;

                    Action<MehediSegmentChunk> launchShihabWorker = null;
                    launchShihabWorker = delegate(MehediSegmentChunk mehediChunk)
                    {
                        Task shihabWorkerTask = Task.Run(delegate
                        {
                            DownloadSingleMehediSegment(shihabItem, mehediChunk, shihabSegmentLock, mehediToken);

                            if (mehediToken.IsCancellationRequested) return;

                            if (shihabItem.ShihabResumeSupported && shihabItem.ShihabTotalBytes > 0)
                            {
                                MehediSegmentChunk shihabSplitHalf = null;
                                lock (shihabSegmentLock)
                                {
                                    if (mehediChunk.GetMehediRemainingBytes() == 0 && shihabItem.MehediSegments.Count < 64)
                                    {
                                        MehediSegmentChunk mehediLargestActive = null;
                                        long shihabLargestRem = 0;
                                        for (int mehediIdx = 0; mehediIdx < shihabItem.MehediSegments.Count; mehediIdx++)
                                        {
                                            MehediSegmentChunk shihabCand = shihabItem.MehediSegments[mehediIdx];
                                            if (shihabCand.ShihabIsActiveWorker)
                                            {
                                                long mehediRem = shihabCand.GetMehediRemainingBytes();
                                                if (mehediRem > shihabLargestRem)
                                                {
                                                    shihabLargestRem = mehediRem;
                                                    mehediLargestActive = shihabCand;
                                                }
                                            }
                                        }

                                        // IDM Dynamic File Segmentation: halve the largest remaining active range if >= 512 KB left
                                        if (mehediLargestActive != null && shihabLargestRem >= 524288L)
                                        {
                                            long shihabCurrentCursor = mehediLargestActive.MehediStartByte + mehediLargestActive.MehediDownloadedBytes;
                                            long mehediOldEnd = mehediLargestActive.ShihabEndByte;
                                            long shihabMidpoint = shihabCurrentCursor + ((mehediOldEnd - shihabCurrentCursor + 1L) / 2L);
                                            if (shihabMidpoint > shihabCurrentCursor + 131072L && mehediOldEnd >= shihabMidpoint)
                                            {
                                                mehediLargestActive.ShihabEndByte = shihabMidpoint - 1L;
                                                mehediNextSegmentId++;
                                                shihabSplitHalf = new MehediSegmentChunk
                                                {
                                                    ShihabSegmentIndex = mehediNextSegmentId,
                                                    MehediStartByte = shihabMidpoint,
                                                    ShihabEndByte = mehediOldEnd,
                                                    MehediDownloadedBytes = 0,
                                                    ShihabSegmentState = "Dynamic split connecting...",
                                                    MehediPartFilePath = Path.Combine(mehediItemTempDir, string.Format("seg_{0:000}.shihabpart", mehediNextSegmentId)),
                                                    ShihabIsActiveWorker = true
                                                };
                                                int mehediInsertPos = shihabItem.MehediSegments.IndexOf(mehediLargestActive) + 1;
                                                shihabItem.MehediSegments.Insert(mehediInsertPos, shihabSplitHalf);
                                                EmitShihabLog(string.Format("[DynamicSplit] {0}: split active seg #{1} at byte {2} -> new seg #{3}",
                                                    shihabItem.ShihabFileName,
                                                    mehediLargestActive.ShihabSegmentIndex,
                                                    shihabMidpoint,
                                                    shihabSplitHalf.ShihabSegmentIndex));
                                            }
                                        }
                                    }
                                }

                                if (shihabSplitHalf != null)
                                {
                                    lock (shihabSegmentLock)
                                    {
                                        launchShihabWorker(shihabSplitHalf);
                                    }
                                }
                            }
                        });

                        lock (shihabSegmentLock)
                        {
                            shihabActiveTasks.Add(shihabWorkerTask);
                        }
                    };

                    lock (shihabSegmentLock)
                    {
                        List<MehediSegmentChunk> mehediInitialList = new List<MehediSegmentChunk>(shihabItem.MehediSegments);
                        for (int shihabI = 0; shihabI < mehediInitialList.Count; shihabI++)
                        {
                            MehediSegmentChunk mehediSeg = mehediInitialList[shihabI];
                            if (mehediSeg.ShihabEndByte < 0 || mehediSeg.GetMehediRemainingBytes() > 0)
                            {
                                mehediSeg.ShihabIsActiveWorker = true;
                                launchShihabWorker(mehediSeg);
                            }
                            else
                            {
                                mehediSeg.ShihabSegmentState = "Completed.";
                            }
                        }
                    }

                    while (!mehediToken.IsCancellationRequested)
                    {
                        Task[] mehediSnapshotTasks;
                        lock (shihabSegmentLock)
                        {
                            List<Task> shihabRunning = new List<Task>();
                            for (int mehediT = 0; mehediT < shihabActiveTasks.Count; mehediT++)
                            {
                                if (!shihabActiveTasks[mehediT].IsCompleted)
                                {
                                    shihabRunning.Add(shihabActiveTasks[mehediT]);
                                }
                            }
                            mehediSnapshotTasks = shihabRunning.ToArray();
                        }

                        long shihabNowBytes = CalculateMehediDownloadedSum(shihabItem, shihabSegmentLock);
                        shihabItem.MehediDownloadedBytes = shihabNowBytes;
                        long mehediNowMs = mehediRateWatch.ElapsedMilliseconds;
                        long shihabDeltaMs = mehediNowMs - mehediLastRateTickMs;
                        if (shihabDeltaMs >= 250)
                        {
                            double mehediInstBps = ((shihabNowBytes - shihabLastBytesSnapshot) * 1000.0) / Math.Max(1.0, (double)shihabDeltaMs);
                            shihabItem.ShihabCurrentSpeedBps = (shihabItem.ShihabCurrentSpeedBps <= 1.0)
                                ? mehediInstBps
                                : (shihabItem.ShihabCurrentSpeedBps * 0.65) + (mehediInstBps * 0.35);

                            if (shihabItem.ShihabTotalBytes > shihabNowBytes && shihabItem.ShihabCurrentSpeedBps > 10.0)
                            {
                                shihabItem.MehediEtaSeconds = (long)((shihabItem.ShihabTotalBytes - shihabNowBytes) / shihabItem.ShihabCurrentSpeedBps);
                            }
                            else
                            {
                                shihabItem.MehediEtaSeconds = -1;
                            }

                            shihabLastBytesSnapshot = shihabNowBytes;
                            mehediLastRateTickMs = mehediNowMs;
                            shihabItem.ShihabStatusText = string.Format("{0:0.0}%", shihabItem.GetMehediPercent());
                            NotifyShihabItemChanged(shihabItem);
                        }

                        if (mehediSnapshotTasks.Length == 0)
                        {
                            break;
                        }
                        Thread.Sleep(150);
                    }

                    if (mehediToken.IsCancellationRequested)
                    {
                        return;
                    }

                    shihabItem.MehediDownloadedBytes = CalculateMehediDownloadedSum(shihabItem, shihabSegmentLock);

                    bool mehediAllDone = true;
                    string shihabFailedReason = "";
                    lock (shihabSegmentLock)
                    {
                        for (int mehediI = 0; mehediI < shihabItem.MehediSegments.Count; mehediI++)
                        {
                            MehediSegmentChunk shihabSeg = shihabItem.MehediSegments[mehediI];
                            if (shihabSeg.ShihabEndByte >= 0 && shihabSeg.GetMehediRemainingBytes() > 0)
                            {
                                mehediAllDone = false;
                                shihabFailedReason = shihabSeg.ShihabSegmentState;
                                break;
                            }
                            if (shihabSeg.ShihabEndByte < 0 && shihabSeg.ShihabSegmentState != "Completed.")
                            {
                                mehediAllDone = false;
                                shihabFailedReason = shihabSeg.ShihabSegmentState;
                                break;
                            }
                        }
                    }

                    if (!mehediAllDone)
                    {
                        bool shihabCanTryBrowserRelay =
                            (shihabFailedReason != null && (
                                shihabFailedReason.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                shihabFailedReason.IndexOf("401", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                shihabFailedReason.IndexOf("Forbidden", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                shihabFailedReason.IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0)) ||
                            shihabItem.ShihabSourceTabId >= 0;

                        if (shihabCanTryBrowserRelay)
                        {
                            EmitShihabLog("[BrowserRelayFallback] Direct HTTP rejected (" + shihabFailedReason + "), requesting Browser Relay for " + shihabItem.ShihabFileName);
                            try
                            {
                                DownloadViaShihabBrowserRelay(shihabItem, shihabItem.MehediUrl, shihabPrimaryAssembledPath, shihabItem.ShihabTotalBytes, true, mehediToken);
                            }
                            catch (Exception mehediRelayEx)
                            {
                                if (mehediToken.IsCancellationRequested) return;
                                throw new IOException(string.IsNullOrEmpty(shihabFailedReason)
                                    ? mehediRelayEx.Message
                                    : (shihabFailedReason + " (" + mehediRelayEx.Message + ")"));
                            }
                        }
                        else
                        {
                            throw new IOException(string.IsNullOrEmpty(shihabFailedReason) ? "One or more segments interrupted." : shihabFailedReason);
                        }
                    }
                    else
                    {
                        shihabItem.ShihabCurrentSpeedBps = 0;
                        shihabItem.MehediEtaSeconds = 0;
                        shihabItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabRebuilding, "Rebuilding...");
                        NotifyShihabItemChanged(shihabItem);

                        RebuildMehediSegmentsToFinalFile(shihabItem, shihabSegmentLock, shihabPrimaryAssembledPath, !mehediHasCompanionAudio);
                        mehediShaAlreadyComputed = !mehediHasCompanionAudio;
                    }
                }

                if (mehediToken.IsCancellationRequested)
                {
                    return;
                }

                FinalizeShihabDownloadedFileAndOptionalAudioMux(
                    shihabItem,
                    shihabPrimaryAssembledPath,
                    mehediItemTempDir,
                    mehediShaAlreadyComputed,
                    mehediToken);

                try
                {
                    if (Directory.Exists(mehediItemTempDir))
                    {
                        Directory.Delete(mehediItemTempDir, true);
                    }
                }
                catch { }

                lock (_shihabEngineLock)
                {
                    _mehediActiveCts.Remove(shihabItem.ShihabId);
                }

                shihabItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabCompleted, "Complete");
                PersistShihabState();
                NotifyShihabItemChanged(shihabItem);
                if (OnMehediItemCompleted != null)
                {
                    OnMehediItemCompleted(shihabItem);
                }
            }
            catch (Exception shihabEx)
            {
                lock (_shihabEngineLock)
                {
                    _mehediActiveCts.Remove(shihabItem.ShihabId);
                }
                if (!mehediToken.IsCancellationRequested)
                {
                    shihabItem.ShihabCurrentSpeedBps = 0;
                    shihabItem.MehediEtaSeconds = -1;
                    shihabItem.ShihabRetryCount++;
                    shihabItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabError, "Error: " + shihabEx.Message);
                    PersistShihabState();
                    NotifyShihabItemChanged(shihabItem);
                    EmitShihabLog("[Error] " + shihabItem.ShihabFileName + " -> " + shihabEx.Message);
                    if (OnShihabItemFailed != null)
                    {
                        try { OnShihabItemFailed(shihabItem); } catch { }
                    }
                }
            }
        }

        private void PrepareInitialShihabSegments(ShihabDownloadItem shihabItem, string mehediItemTempDir)
        {
            if (shihabItem.MehediSegments != null && shihabItem.MehediSegments.Count > 0)
            {
                bool mehediValidExisting = true;
                for (int shihabI = 0; shihabI < shihabItem.MehediSegments.Count; shihabI++)
                {
                    MehediSegmentChunk mehediSeg = shihabItem.MehediSegments[shihabI];
                    if (!string.IsNullOrEmpty(mehediSeg.MehediPartFilePath) && File.Exists(mehediSeg.MehediPartFilePath))
                    {
                        long shihabDiskLen = new FileInfo(mehediSeg.MehediPartFilePath).Length;
                        if (mehediSeg.ShihabEndByte >= 0)
                        {
                            long mehediMaxSpan = mehediSeg.GetShihabTotalSpan();
                            mehediSeg.MehediDownloadedBytes = Math.Min(shihabDiskLen, mehediMaxSpan);
                        }
                        else
                        {
                            mehediSeg.MehediDownloadedBytes = shihabDiskLen;
                        }
                    }
                    else
                    {
                        mehediSeg.MehediDownloadedBytes = 0;
                        if (!shihabItem.ShihabResumeSupported && shihabI > 0)
                        {
                            mehediValidExisting = false;
                        }
                    }
                }
                if (mehediValidExisting && shihabItem.ShihabResumeSupported)
                {
                    return;
                }
            }

            shihabItem.MehediSegments = new List<MehediSegmentChunk>();
            int mehediDesiredConns = Math.Max(1, Math.Min(32, shihabItem.MehediMaxConnections > 0 ? shihabItem.MehediMaxConnections : _mehediConfig.ShihabDefaultMaxConnections));
            if (!shihabItem.ShihabResumeSupported || shihabItem.ShihabTotalBytes < 262144L)
            {
                mehediDesiredConns = 1;
            }

            if (mehediDesiredConns == 1)
            {
                shihabItem.MehediSegments.Add(new MehediSegmentChunk
                {
                    ShihabSegmentIndex = 1,
                    MehediStartByte = 0,
                    ShihabEndByte = shihabItem.ShihabTotalBytes > 0 ? (shihabItem.ShihabTotalBytes - 1L) : -1L,
                    MehediDownloadedBytes = 0,
                    ShihabSegmentState = "Connecting...",
                    MehediPartFilePath = Path.Combine(mehediItemTempDir, "seg_001.shihabpart")
                });
                return;
            }

            long shihabBaseSpan = shihabItem.ShihabTotalBytes / mehediDesiredConns;
            long mehediCursor = 0;
            for (int shihabIdx = 1; shihabIdx <= mehediDesiredConns; shihabIdx++)
            {
                long mehediEnd = (shihabIdx == mehediDesiredConns)
                    ? (shihabItem.ShihabTotalBytes - 1L)
                    : (mehediCursor + shihabBaseSpan - 1L);
                shihabItem.MehediSegments.Add(new MehediSegmentChunk
                {
                    ShihabSegmentIndex = shihabIdx,
                    MehediStartByte = mehediCursor,
                    ShihabEndByte = mehediEnd,
                    MehediDownloadedBytes = 0,
                    ShihabSegmentState = "Connecting...",
                    MehediPartFilePath = Path.Combine(mehediItemTempDir, string.Format("seg_{0:000}.shihabpart", shihabIdx))
                });
                mehediCursor = mehediEnd + 1L;
            }
        }

        private void DownloadSingleMehediSegment(
            ShihabDownloadItem shihabItem,
            MehediSegmentChunk mehediChunk,
            object shihabSegmentLock,
            CancellationToken mehediToken)
        {
            try
            {
                long shihabResumeOffset;
                long mehediCurrentEnd;
                lock (shihabSegmentLock)
                {
                    if (mehediChunk.ShihabEndByte >= 0 && mehediChunk.GetMehediRemainingBytes() <= 0)
                    {
                        mehediChunk.ShihabSegmentState = "Completed.";
                        mehediChunk.ShihabIsActiveWorker = false;
                        return;
                    }
                    if (!shihabItem.ShihabResumeSupported)
                    {
                        mehediChunk.MehediDownloadedBytes = 0;
                    }
                    shihabResumeOffset = mehediChunk.MehediStartByte + mehediChunk.MehediDownloadedBytes;
                    mehediCurrentEnd = mehediChunk.ShihabEndByte;
                    mehediChunk.ShihabSegmentState = "Connecting...";
                }

                long shihabReqStart = shihabItem.ShihabResumeSupported ? shihabResumeOffset : -1L;
                long mehediReqEnd = (shihabItem.ShihabResumeSupported && mehediCurrentEnd >= shihabResumeOffset) ? mehediCurrentEnd : -1L;

                HttpWebRequest shihabReq = CreateShihabHttpRequest(shihabItem.MehediUrl, "GET", shihabItem, shihabReqStart, mehediReqEnd);
                using (mehediToken.Register(delegate { try { shihabReq.Abort(); } catch { } }))
                using (HttpWebResponse mehediResp = (HttpWebResponse)shihabReq.GetResponse())
                using (Stream shihabNetStream = mehediResp.GetResponseStream())
                {
                    if (!shihabItem.ShihabResumeSupported && shihabItem.ShihabTotalBytes <= 0 && mehediResp.ContentLength > 0)
                    {
                        shihabItem.ShihabTotalBytes = mehediResp.ContentLength;
                    }

                    FileMode mehediFileMode = (shihabItem.ShihabResumeSupported && mehediChunk.MehediDownloadedBytes > 0 && File.Exists(mehediChunk.MehediPartFilePath))
                        ? FileMode.Open
                        : FileMode.Create;

                    using (FileStream shihabPartFs = new FileStream(mehediChunk.MehediPartFilePath, mehediFileMode, FileAccess.Write, FileShare.Read))
                    {
                        if (mehediFileMode == FileMode.Open)
                        {
                            shihabPartFs.Seek(mehediChunk.MehediDownloadedBytes, SeekOrigin.Begin);
                        }

                        lock (shihabSegmentLock)
                        {
                            mehediChunk.ShihabSegmentState = "Receiving data...";
                        }

                        byte[] mehediBuf = new byte[16384];
                        Stopwatch shihabItemThrottleWatch = Stopwatch.StartNew();
                        long mehediItemWindowStartMs = 0;
                        long shihabItemWindowBytes = 0;

                        while (!mehediToken.IsCancellationRequested)
                        {
                            int mehediRead = shihabNetStream.Read(mehediBuf, 0, mehediBuf.Length);
                            if (mehediRead <= 0)
                            {
                                break;
                            }

                            int shihabToWrite = mehediRead;
                            bool mehediReachedDynamicBoundary = false;
                            lock (shihabSegmentLock)
                            {
                                if (mehediChunk.ShihabEndByte >= 0)
                                {
                                    long shihabAllowed = (mehediChunk.ShihabEndByte - mehediChunk.MehediStartByte + 1L) - mehediChunk.MehediDownloadedBytes;
                                    if (shihabAllowed <= 0)
                                    {
                                        shihabToWrite = 0;
                                        mehediReachedDynamicBoundary = true;
                                    }
                                    else if (shihabToWrite > shihabAllowed)
                                    {
                                        shihabToWrite = (int)shihabAllowed;
                                        mehediReachedDynamicBoundary = true;
                                    }
                                }
                                if (shihabToWrite > 0)
                                {
                                    mehediChunk.MehediDownloadedBytes += shihabToWrite;
                                }
                            }

                            if (shihabToWrite > 0)
                            {
                                shihabPartFs.Write(mehediBuf, 0, shihabToWrite);
                                ApplyMehediSpeedLimiter(
                                    shihabItem,
                                    shihabToWrite,
                                    shihabItemThrottleWatch,
                                    ref mehediItemWindowStartMs,
                                    ref shihabItemWindowBytes,
                                    mehediToken);
                            }

                            if (mehediReachedDynamicBoundary)
                            {
                                break;
                            }
                        }
                        shihabPartFs.Flush();
                    }
                }

                lock (shihabSegmentLock)
                {
                    mehediChunk.ShihabIsActiveWorker = false;
                    if (!mehediToken.IsCancellationRequested &&
                        (mehediChunk.ShihabEndByte < 0 || mehediChunk.GetMehediRemainingBytes() == 0))
                    {
                        mehediChunk.ShihabSegmentState = "Completed.";
                    }
                }
            }
            catch (Exception shihabEx)
            {
                lock (shihabSegmentLock)
                {
                    mehediChunk.ShihabIsActiveWorker = false;
                    mehediChunk.ShihabSegmentState = mehediToken.IsCancellationRequested
                        ? "Disconnect."
                        : ("Error: " + shihabEx.Message);
                }
            }
        }

        private void ApplyMehediSpeedLimiter(
            ShihabDownloadItem shihabItem,
            int mehediTransferredBytes,
            Stopwatch shihabWatch,
            ref long mehediItemWindowStartMs,
            ref long shihabItemWindowBytes,
            CancellationToken mehediToken)
        {
            if (shihabItem.ShihabUseItemSpeedLimit && shihabItem.MehediItemSpeedLimitKb > 0)
            {
                int shihabConns = Math.Max(1, shihabItem.MehediSegments.Count);
                long mehediPerSegBps = Math.Max(4096L, (shihabItem.MehediItemSpeedLimitKb * 1024L) / shihabConns);
                long shihabNow = shihabWatch.ElapsedMilliseconds;
                if (shihabNow - mehediItemWindowStartMs >= 200)
                {
                    mehediItemWindowStartMs = shihabNow;
                    shihabItemWindowBytes = 0;
                }
                shihabItemWindowBytes += mehediTransferredBytes;
                long mehediAllowedIn200Ms = mehediPerSegBps / 5L;
                if (shihabItemWindowBytes > mehediAllowedIn200Ms)
                {
                    int shihabSleep = (int)Math.Min(250L, ((shihabItemWindowBytes - mehediAllowedIn200Ms) * 1000L) / mehediPerSegBps);
                    if (shihabSleep > 5 && !mehediToken.IsCancellationRequested)
                    {
                        Thread.Sleep(shihabSleep);
                    }
                }
            }

            if (_mehediConfig.MehediGlobalSpeedLimiterEnabled && _mehediConfig.ShihabGlobalSpeedLimitKb > 0)
            {
                int mehediGlobalSleep = 0;
                lock (_mehediRateLock)
                {
                    long shihabTick = Environment.TickCount & Int32.MaxValue;
                    if (shihabTick - _shihabGlobalWindowStartMs >= 200 || shihabTick < _shihabGlobalWindowStartMs)
                    {
                        _shihabGlobalWindowStartMs = shihabTick;
                        _mehediGlobalBytesInWindow = 0;
                    }
                    _mehediGlobalBytesInWindow += mehediTransferredBytes;
                    long mehediGlobalBps = Math.Max(8192L, _mehediConfig.ShihabGlobalSpeedLimitKb * 1024L);
                    long shihabQuota200Ms = mehediGlobalBps / 5L;
                    if (_mehediGlobalBytesInWindow > shihabQuota200Ms)
                    {
                        mehediGlobalSleep = (int)Math.Min(250L, ((_mehediGlobalBytesInWindow - shihabQuota200Ms) * 1000L) / mehediGlobalBps);
                    }
                }
                if (mehediGlobalSleep > 5 && !mehediToken.IsCancellationRequested)
                {
                    Thread.Sleep(mehediGlobalSleep);
                }
            }
        }

        private void DownloadMehediGoogleVideoOrRelay(
            ShihabDownloadItem shihabItem,
            string mehediRawUrl,
            string shihabOutputFilePath,
            long mehediKnownTotalBytes,
            bool shihabIsPrimaryStream,
            CancellationToken mehediToken)
        {
            long shihabClen;
            string mehediCleanUrl = SanitizeMehediGoogleVideoUrl(mehediRawUrl, out shihabClen);
            long shihabEffectiveTotal = shihabClen > 0 ? shihabClen : mehediKnownTotalBytes;

            if (shihabIsPrimaryStream)
            {
                if (shihabEffectiveTotal > 0)
                {
                    shihabItem.ShihabTotalBytes = shihabEffectiveTotal;
                    shihabItem.ShihabResumeSupported = true;
                }
                shihabItem.MehediSegments = new List<MehediSegmentChunk>
                {
                    new MehediSegmentChunk
                    {
                        ShihabSegmentIndex = 1,
                        MehediStartByte = 0,
                        ShihabEndByte = shihabEffectiveTotal > 0 ? (shihabEffectiveTotal - 1L) : -1L,
                        MehediDownloadedBytes = 0,
                        ShihabSegmentState = "Receiving data...",
                        MehediPartFilePath = shihabOutputFilePath,
                        ShihabIsActiveWorker = true
                    }
                };
                shihabItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabDownloading, "Receiving data...");
                NotifyShihabItemChanged(shihabItem);
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(shihabOutputFilePath));
                const long SHIHAB_SLICE_BYTES = 2L * 1024L * 1024L;
                long mehediOffset = 0;
                Stopwatch shihabRateWatch = Stopwatch.StartNew();
                long mehediLastRateMs = 0;
                long shihabLastRateBytes = 0;

                using (FileStream mehediOutFs = new FileStream(shihabOutputFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    byte[] shihabReadBuf = new byte[32768];
                    Stopwatch mehediThrottleWatch = Stopwatch.StartNew();
                    long shihabWinStart = 0;
                    long mehediWinBytes = 0;

                    while (!mehediToken.IsCancellationRequested)
                    {
                        if (shihabEffectiveTotal > 0 && mehediOffset >= shihabEffectiveTotal)
                        {
                            break;
                        }
                        long shihabSliceEnd = shihabEffectiveTotal > 0
                            ? Math.Min(shihabEffectiveTotal - 1L, mehediOffset + SHIHAB_SLICE_BYTES - 1L)
                            : (mehediOffset + SHIHAB_SLICE_BYTES - 1L);
                        long mehediExpectedChunkSpan = (shihabSliceEnd - mehediOffset) + 1L;
                        string shihabChunkUrl = AppendShihabQueryRangeParam(mehediCleanUrl, mehediOffset, shihabSliceEnd);

                        long mehediGotInSlice = 0;
                        HttpWebRequest shihabReq = CreateShihabHttpRequest(shihabChunkUrl, "GET", shihabItem, -1, -1);
                        using (mehediToken.Register(delegate { try { shihabReq.Abort(); } catch { } }))
                        using (HttpWebResponse mehediResp = (HttpWebResponse)shihabReq.GetResponse())
                        using (Stream shihabNet = mehediResp.GetResponseStream())
                        {
                            while (!mehediToken.IsCancellationRequested)
                            {
                                int mehediRead = shihabNet.Read(shihabReadBuf, 0, shihabReadBuf.Length);
                                if (mehediRead <= 0) break;
                                mehediOutFs.Write(shihabReadBuf, 0, mehediRead);
                                mehediOffset += mehediRead;
                                mehediGotInSlice += mehediRead;

                                ApplyMehediSpeedLimiter(shihabItem, mehediRead, mehediThrottleWatch, ref shihabWinStart, ref mehediWinBytes, mehediToken);

                                if (shihabIsPrimaryStream)
                                {
                                    shihabItem.MehediDownloadedBytes = mehediOffset;
                                    if (shihabItem.MehediSegments.Count > 0)
                                    {
                                        shihabItem.MehediSegments[0].MehediDownloadedBytes = mehediOffset;
                                    }
                                    long shihabNowMs = shihabRateWatch.ElapsedMilliseconds;
                                    if (shihabNowMs - mehediLastRateMs >= 220)
                                    {
                                        double mehediInstBps = ((mehediOffset - shihabLastRateBytes) * 1000.0) / Math.Max(1.0, (double)(shihabNowMs - mehediLastRateMs));
                                        shihabItem.ShihabCurrentSpeedBps = shihabItem.ShihabCurrentSpeedBps <= 1.0
                                            ? mehediInstBps
                                            : (shihabItem.ShihabCurrentSpeedBps * 0.65) + (mehediInstBps * 0.35);
                                        if (shihabItem.ShihabTotalBytes > mehediOffset && shihabItem.ShihabCurrentSpeedBps > 10.0)
                                        {
                                            shihabItem.MehediEtaSeconds = (long)((shihabItem.ShihabTotalBytes - mehediOffset) / shihabItem.ShihabCurrentSpeedBps);
                                        }
                                        shihabLastRateBytes = mehediOffset;
                                        mehediLastRateMs = shihabNowMs;
                                        shihabItem.ShihabStatusText = string.Format("{0:0.0}%", shihabItem.GetMehediPercent());
                                        NotifyShihabItemChanged(shihabItem);
                                    }
                                }
                            }
                        }

                        if (mehediGotInSlice <= 0)
                        {
                            break;
                        }
                        if (shihabEffectiveTotal <= 0 && mehediGotInSlice < mehediExpectedChunkSpan)
                        {
                            break;
                        }
                    }
                    mehediOutFs.Flush();
                }

                if (mehediToken.IsCancellationRequested)
                {
                    return;
                }
                if (mehediOffset <= 0)
                {
                    throw new IOException("GoogleVideo stream returned 0 bytes.");
                }
                if (shihabIsPrimaryStream)
                {
                    shihabItem.MehediDownloadedBytes = mehediOffset;
                    if (shihabItem.ShihabTotalBytes <= 0) shihabItem.ShihabTotalBytes = mehediOffset;
                    if (shihabItem.MehediSegments.Count > 0)
                    {
                        shihabItem.MehediSegments[0].MehediDownloadedBytes = mehediOffset;
                        shihabItem.MehediSegments[0].ShihabSegmentState = "Completed.";
                        shihabItem.MehediSegments[0].ShihabIsActiveWorker = false;
                    }
                }
            }
            catch (Exception shihabDirectEx)
            {
                if (mehediToken.IsCancellationRequested)
                {
                    return;
                }
                EmitShihabLog("[GoogleVideoRelay] Direct HTTP failed (" + shihabDirectEx.Message + "), switching to Browser Bridge Relay...");
                DownloadViaShihabBrowserRelay(shihabItem, mehediCleanUrl, shihabOutputFilePath, shihabEffectiveTotal, shihabIsPrimaryStream, mehediToken);
            }
        }

        private void DownloadViaShihabBrowserRelay(
            ShihabDownloadItem shihabItem,
            string mehediTargetUrl,
            string shihabOutputFilePath,
            long mehediExpectedBytes,
            bool shihabIsPrimaryStream,
            CancellationToken mehediToken)
        {
            Stopwatch shihabWatch = Stopwatch.StartNew();
            long mehediLastMs = 0;
            long shihabLastBytes = 0;

            if (shihabIsPrimaryStream)
            {
                shihabItem.MehediDownloadedBytes = 0;
                shihabItem.MehediSegments = new List<MehediSegmentChunk>
                {
                    new MehediSegmentChunk
                    {
                        ShihabSegmentIndex = 1,
                        MehediStartByte = 0,
                        ShihabEndByte = mehediExpectedBytes > 0 ? (mehediExpectedBytes - 1L) : -1L,
                        MehediDownloadedBytes = 0,
                        ShihabSegmentState = "Browser Bridge Relay...",
                        MehediPartFilePath = shihabOutputFilePath,
                        ShihabIsActiveWorker = true
                    }
                };
                shihabItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabDownloading, "Receiving via Browser Bridge...");
                NotifyShihabItemChanged(shihabItem);
            }
            else
            {
                shihabItem.ShihabStatusText = "Receiving audio via Browser Bridge...";
                NotifyShihabItemChanged(shihabItem);
            }

            long shihabReceivedTotal = ShihabBrowserRelayHub.ExecuteMehediRelayDownload(
                mehediTargetUrl,
                shihabOutputFilePath,
                shihabItem.ShihabSourceTabId,
                shihabItem.MehediReferer,
                shihabItem.MehediOriginHeader,
                shihabItem.MehediOmitCookies,
                mehediExpectedBytes,
                delegate(long mehediGotBytes, long shihabRepTotal)
                {
                    if (shihabIsPrimaryStream)
                    {
                        if (shihabRepTotal > 0)
                        {
                            shihabItem.ShihabTotalBytes = shihabRepTotal;
                            shihabItem.ShihabResumeSupported = true;
                        }
                        shihabItem.MehediDownloadedBytes = mehediGotBytes;
                        if (shihabItem.MehediSegments.Count > 0)
                        {
                            shihabItem.MehediSegments[0].MehediDownloadedBytes = mehediGotBytes;
                            if (shihabRepTotal > 0) shihabItem.MehediSegments[0].ShihabEndByte = shihabRepTotal - 1L;
                        }
                        long mehediNowMs = shihabWatch.ElapsedMilliseconds;
                        if (mehediNowMs - mehediLastMs >= 200)
                        {
                            double shihabBps = ((mehediGotBytes - shihabLastBytes) * 1000.0) / Math.Max(1.0, (double)(mehediNowMs - mehediLastMs));
                            shihabItem.ShihabCurrentSpeedBps = shihabItem.ShihabCurrentSpeedBps <= 1.0
                                ? shihabBps
                                : (shihabItem.ShihabCurrentSpeedBps * 0.65) + (shihabBps * 0.35);
                            if (shihabItem.ShihabTotalBytes > mehediGotBytes && shihabItem.ShihabCurrentSpeedBps > 10.0)
                            {
                                shihabItem.MehediEtaSeconds = (long)((shihabItem.ShihabTotalBytes - mehediGotBytes) / shihabItem.ShihabCurrentSpeedBps);
                            }
                            shihabLastBytes = mehediGotBytes;
                            mehediLastMs = mehediNowMs;
                            shihabItem.ShihabStatusText = string.Format("{0:0.0}%", shihabItem.GetMehediPercent());
                            NotifyShihabItemChanged(shihabItem);
                        }
                    }
                    else
                    {
                        long mehediAudDenom = shihabRepTotal > 0 ? shihabRepTotal : mehediExpectedBytes;
                        shihabItem.ShihabStatusText = mehediAudDenom > 0
                            ? string.Format("Audio track {0:0}%", Math.Min(100.0, (mehediGotBytes * 100.0) / mehediAudDenom))
                            : ("Audio track " + ShihabFormatHelpers.FormatMehediBytes(mehediGotBytes));
                        NotifyShihabItemChanged(shihabItem);
                    }
                },
                mehediToken);

            if (shihabIsPrimaryStream)
            {
                shihabItem.MehediDownloadedBytes = shihabReceivedTotal;
                if (shihabItem.ShihabTotalBytes <= 0) shihabItem.ShihabTotalBytes = shihabReceivedTotal;
                if (shihabItem.MehediSegments.Count > 0)
                {
                    shihabItem.MehediSegments[0].MehediDownloadedBytes = shihabReceivedTotal;
                    shihabItem.MehediSegments[0].ShihabSegmentState = "Completed.";
                    shihabItem.MehediSegments[0].ShihabIsActiveWorker = false;
                }
            }
        }

        private void DownloadMehediAuxiliaryStream(
            ShihabDownloadItem shihabItem,
            string mehediAuxUrl,
            long shihabAuxKnownBytes,
            string mehediAuxOutputPath,
            CancellationToken shihabToken)
        {
            shihabItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabDownloading, "Downloading audio track...");
            NotifyShihabItemChanged(shihabItem);

            if (IsShihabGoogleVideoUrl(mehediAuxUrl))
            {
                DownloadMehediGoogleVideoOrRelay(shihabItem, mehediAuxUrl, mehediAuxOutputPath, shihabAuxKnownBytes, false, shihabToken);
                return;
            }

            if (mehediAuxUrl.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                DownloadMehediHlsMediaPlaylistToFile(mehediAuxUrl, mehediAuxOutputPath, "HLS Audio", shihabItem, false, shihabToken);
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(mehediAuxOutputPath));
                HttpWebRequest shihabReq = CreateShihabHttpRequest(mehediAuxUrl, "GET", shihabItem, -1, -1);
                using (shihabToken.Register(delegate { try { shihabReq.Abort(); } catch { } }))
                using (HttpWebResponse mehediResp = (HttpWebResponse)shihabReq.GetResponse())
                using (Stream shihabNet = mehediResp.GetResponseStream())
                using (FileStream mehediFs = new FileStream(mehediAuxOutputPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    long shihabTotal = mehediResp.ContentLength > 0 ? mehediResp.ContentLength : shihabAuxKnownBytes;
                    byte[] mehediBuf = new byte[32768];
                    long shihabGot = 0;
                    int mehediRead;
                    while (!shihabToken.IsCancellationRequested && (mehediRead = shihabNet.Read(mehediBuf, 0, mehediBuf.Length)) > 0)
                    {
                        mehediFs.Write(mehediBuf, 0, mehediRead);
                        shihabGot += mehediRead;
                        if (shihabTotal > 0)
                        {
                            shihabItem.ShihabStatusText = string.Format("Audio track {0:0}%", Math.Min(100.0, (shihabGot * 100.0) / shihabTotal));
                        }
                    }
                    mehediFs.Flush();
                    if (shihabGot <= 0 && !shihabToken.IsCancellationRequested)
                    {
                        throw new IOException("Audio stream returned 0 bytes.");
                    }
                }
            }
            catch (Exception mehediAuxEx)
            {
                if (shihabToken.IsCancellationRequested) return;
                EmitShihabLog("[AudioRelayFallback] Direct audio HTTP failed (" + mehediAuxEx.Message + "), switching to Browser Bridge Relay...");
                DownloadViaShihabBrowserRelay(shihabItem, mehediAuxUrl, mehediAuxOutputPath, shihabAuxKnownBytes, false, shihabToken);
            }
        }

        private void FinalizeShihabDownloadedFileAndOptionalAudioMux(
            ShihabDownloadItem shihabItem,
            string mehediPrimaryAssembledPath,
            string shihabItemTempDir,
            bool mehediShaAlreadyComputedOnTarget,
            CancellationToken shihabToken)
        {
            ShihabMediaProbeInfo mehediPrimaryMedia = MehediMediaMuxer.InspectShihabMediaFile(mehediPrimaryAssembledPath);
            if (mehediPrimaryMedia.ShihabIsEncryptedDrm)
            {
                try { if (File.Exists(mehediPrimaryAssembledPath)) File.Delete(mehediPrimaryAssembledPath); } catch { }
                throw new InvalidOperationException("Protected/encrypted DRM stream cannot be downloaded (DRM protections are respected).");
            }

            string shihabFinalTargetPath = shihabItem.GetShihabFullTargetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(shihabFinalTargetPath));

            bool mehediNeedsAudioMux =
                !string.IsNullOrWhiteSpace(shihabItem.MehediAudioCompanionUrl) &&
                !(mehediPrimaryMedia.ShihabHasVideo && mehediPrimaryMedia.MehediHasAudio);

            if (mehediNeedsAudioMux)
            {
                string shihabAudioTempPath = Path.Combine(shihabItemTempDir, "companion_audio_raw.part");
                DownloadMehediAuxiliaryStream(
                    shihabItem,
                    shihabItem.MehediAudioCompanionUrl,
                    shihabItem.ShihabAudioCompanionBytes,
                    shihabAudioTempPath,
                    shihabToken);
                if (shihabToken.IsCancellationRequested) return;

                shihabItem.ShihabCurrentSpeedBps = 0;
                shihabItem.MehediEtaSeconds = 0;
                shihabItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabRebuilding, "Merging video + audio...");
                NotifyShihabItemChanged(shihabItem);

                string mehediMuxedActualPath = MehediMediaMuxer.MuxShihabVideoAndAudio(
                    mehediPrimaryAssembledPath,
                    shihabAudioTempPath,
                    shihabFinalTargetPath);

                shihabFinalTargetPath = mehediMuxedActualPath;
                shihabItem.ShihabFileName = Path.GetFileName(mehediMuxedActualPath);
                mehediShaAlreadyComputedOnTarget = false;
            }
            else if (!string.Equals(Path.GetFullPath(mehediPrimaryAssembledPath), Path.GetFullPath(shihabFinalTargetPath), StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(shihabFinalTargetPath))
                {
                    File.Delete(shihabFinalTargetPath);
                }
                File.Move(mehediPrimaryAssembledPath, shihabFinalTargetPath);
                mehediShaAlreadyComputedOnTarget = false;
            }

            if (!mehediShaAlreadyComputedOnTarget || string.IsNullOrEmpty(shihabItem.MehediSha256Hex))
            {
                ComputeShihabFileSha256AndSize(shihabItem, shihabFinalTargetPath);
            }

            if ((_mehediConfig.ShihabSetServerFileTimestamp || _mehediConfig.ShihabSetServerCreationDate) &&
                !string.IsNullOrWhiteSpace(shihabItem.ShihabServerLastModified))
            {
                DateTime mehediServerDt;
                if (DateTime.TryParse(shihabItem.ShihabServerLastModified, out mehediServerDt))
                {
                    if (_mehediConfig.ShihabSetServerFileTimestamp)
                    {
                        try { File.SetLastWriteTime(shihabFinalTargetPath, mehediServerDt); } catch { }
                    }
                    if (_mehediConfig.ShihabSetServerCreationDate)
                    {
                        try { File.SetCreationTime(shihabFinalTargetPath, mehediServerDt); } catch { }
                    }
                }
            }

            if (_mehediConfig.ShihabEnableVirusCheck && !string.IsNullOrWhiteSpace(_mehediConfig.MehediVirusScannerExe))
            {
                try
                {
                    string shihabScannerExe = _mehediConfig.MehediVirusScannerExe.Trim().Trim('"');
                    if (File.Exists(shihabScannerExe) && File.Exists(shihabFinalTargetPath))
                    {
                        string mehediArgTemplate = string.IsNullOrWhiteSpace(_mehediConfig.ShihabVirusScannerArgs)
                            ? "\"%1\""
                            : _mehediConfig.ShihabVirusScannerArgs;
                        string shihabFormattedArgs = mehediArgTemplate.IndexOf("%1", StringComparison.Ordinal) >= 0
                            ? mehediArgTemplate.Replace("%1", shihabFinalTargetPath)
                            : (mehediArgTemplate + " \"" + shihabFinalTargetPath + "\"");
                        ProcessStartInfo mehediScanPsi = new ProcessStartInfo(shihabScannerExe, shihabFormattedArgs)
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        Process.Start(mehediScanPsi);
                    }
                }
                catch { }
            }
        }

        private static void ComputeShihabFileSha256AndSize(ShihabDownloadItem mehediItem, string shihabFilePath)
        {
            long mehediLen = 0;
            byte[] shihabBuf = new byte[262144];
            using (SHA256 mehediSha = SHA256.Create())
            using (FileStream shihabFs = new FileStream(shihabFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int mehediRead;
                while ((mehediRead = shihabFs.Read(shihabBuf, 0, shihabBuf.Length)) > 0)
                {
                    mehediSha.TransformBlock(shihabBuf, 0, mehediRead, null, 0);
                    mehediLen += mehediRead;
                }
                mehediSha.TransformFinalBlock(new byte[0], 0, 0);
                StringBuilder shihabHex = new StringBuilder(64);
                for (int mehediH = 0; mehediH < mehediSha.Hash.Length; mehediH++)
                {
                    shihabHex.Append(mehediSha.Hash[mehediH].ToString("x2"));
                }
                mehediItem.MehediSha256Hex = shihabHex.ToString();
            }
            mehediItem.MehediDownloadedBytes = mehediLen;
            mehediItem.ShihabTotalBytes = mehediLen;
        }

        private void RebuildMehediSegmentsToFinalFile(
            ShihabDownloadItem shihabItem,
            object mehediSegmentLock,
            string shihabDestinationPath,
            bool mehediComputeSha256)
        {
            List<MehediSegmentChunk> shihabOrdered;
            lock (mehediSegmentLock)
            {
                shihabOrdered = new List<MehediSegmentChunk>(shihabItem.MehediSegments);
            }
            shihabOrdered.Sort(delegate(MehediSegmentChunk mehediA, MehediSegmentChunk shihabB)
            {
                return mehediA.MehediStartByte.CompareTo(shihabB.MehediStartByte);
            });

            string mehediTargetFull = string.IsNullOrEmpty(shihabDestinationPath)
                ? shihabItem.GetShihabFullTargetPath()
                : shihabDestinationPath;
            Directory.CreateDirectory(Path.GetDirectoryName(mehediTargetFull));

            byte[] shihabCopyBuffer = new byte[262144];
            long mehediTotalAssembled = 0;

            using (SHA256 shihabSha = mehediComputeSha256 ? SHA256.Create() : null)
            using (FileStream mehediOutFs = new FileStream(mehediTargetFull, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                for (int shihabIdx = 0; shihabIdx < shihabOrdered.Count; shihabIdx++)
                {
                    MehediSegmentChunk mehediSeg = shihabOrdered[shihabIdx];
                    if (string.IsNullOrEmpty(mehediSeg.MehediPartFilePath) || !File.Exists(mehediSeg.MehediPartFilePath))
                    {
                        throw new IOException("Missing temporary part file for segment #" + mehediSeg.ShihabSegmentIndex);
                    }
                    long shihabExpectedBytes = (mehediSeg.ShihabEndByte >= 0)
                        ? mehediSeg.GetShihabTotalSpan()
                        : mehediSeg.MehediDownloadedBytes;
                    long mehediRemainingToCopy = shihabExpectedBytes;

                    using (FileStream shihabPartIn = new FileStream(mehediSeg.MehediPartFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        while (mehediRemainingToCopy > 0)
                        {
                            int shihabAsk = (int)Math.Min((long)shihabCopyBuffer.Length, mehediRemainingToCopy);
                            int mehediGot = shihabPartIn.Read(shihabCopyBuffer, 0, shihabAsk);
                            if (mehediGot <= 0) break;
                            mehediOutFs.Write(shihabCopyBuffer, 0, mehediGot);
                            if (shihabSha != null)
                            {
                                shihabSha.TransformBlock(shihabCopyBuffer, 0, mehediGot, null, 0);
                            }
                            mehediRemainingToCopy -= mehediGot;
                            mehediTotalAssembled += mehediGot;
                        }
                    }
                }
                mehediOutFs.Flush();

                if (shihabSha != null)
                {
                    shihabSha.TransformFinalBlock(new byte[0], 0, 0);
                    StringBuilder mehediHex = new StringBuilder(64);
                    byte[] shihabHash = shihabSha.Hash;
                    for (int mehediH = 0; mehediH < shihabHash.Length; mehediH++)
                    {
                        mehediHex.Append(shihabHash[mehediH].ToString("x2"));
                    }
                    shihabItem.MehediSha256Hex = mehediHex.ToString();
                }
            }

            shihabItem.MehediDownloadedBytes = mehediTotalAssembled;
            if (shihabItem.ShihabTotalBytes <= 0)
            {
                shihabItem.ShihabTotalBytes = mehediTotalAssembled;
            }
        }

        private void ExecuteMehediHlsStreamDownload(
            ShihabDownloadItem shihabItem,
            string mehediPlaylistUrl,
            string shihabTempDir,
            CancellationToken mehediToken)
        {
            string shihabManifestText = DownloadMehediText(mehediPlaylistUrl, shihabItem);
            if (shihabManifestText.IndexOf("#EXT-X-KEY", StringComparison.OrdinalIgnoreCase) >= 0 &&
                shihabManifestText.IndexOf("METHOD=NONE", StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException("Protected/encrypted HLS stream cannot be downloaded (DRM/encryption measures are respected).");
            }

            string mehediActiveVideoPlaylistUrl = mehediPlaylistUrl;
            string shihabDiscoveredAudioPlaylistUrl = shihabItem.MehediAudioCompanionUrl ?? "";

            if (shihabManifestText.IndexOf("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string mehediMatchedAudioUri;
                string shihabBestVariant = SelectHighestBandwidthShihabVariant(shihabManifestText, mehediPlaylistUrl, out mehediMatchedAudioUri);
                if (!string.IsNullOrEmpty(shihabBestVariant))
                {
                    mehediActiveVideoPlaylistUrl = shihabBestVariant;
                }
                if (string.IsNullOrWhiteSpace(shihabDiscoveredAudioPlaylistUrl) && !string.IsNullOrEmpty(mehediMatchedAudioUri))
                {
                    shihabDiscoveredAudioPlaylistUrl = mehediMatchedAudioUri;
                }
            }

            bool mehediHasSeparateAudioHls = !string.IsNullOrWhiteSpace(shihabDiscoveredAudioPlaylistUrl) &&
                !string.Equals(shihabDiscoveredAudioPlaylistUrl, mehediActiveVideoPlaylistUrl, StringComparison.OrdinalIgnoreCase);

            string shihabVideoPartPath = Path.Combine(shihabTempDir, "hls_video_raw.part");
            DownloadMehediHlsMediaPlaylistToFile(mehediActiveVideoPlaylistUrl, shihabVideoPartPath, "HLS", shihabItem, true, mehediToken);
            if (mehediToken.IsCancellationRequested) return;

            ShihabMediaProbeInfo mehediVidProbe = MehediMediaMuxer.InspectShihabMediaFile(shihabVideoPartPath);
            if (mehediVidProbe.ShihabIsEncryptedDrm)
            {
                throw new InvalidOperationException("Protected/encrypted HLS fMP4 stream cannot be downloaded (DRM protections are respected).");
            }

            bool shihabIsFmp4Hls = mehediVidProbe.MehediContainerFormat == "mp4";
            if (shihabItem.ShihabFileName.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
                (shihabIsFmp4Hls && shihabItem.ShihabFileName.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)))
            {
                string mehediNewExt = shihabIsFmp4Hls ? ".mp4" : ".ts";
                shihabItem.ShihabFileName = ShihabFormatHelpers.EnsureShihabUniqueFilePath(
                    shihabItem.MehediSaveDirectory,
                    Path.GetFileNameWithoutExtension(shihabItem.ShihabFileName) + mehediNewExt);
            }

            string mehediTargetFull = shihabItem.GetShihabFullTargetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(mehediTargetFull));

            if (mehediHasSeparateAudioHls)
            {
                string shihabAudioPartPath = Path.Combine(shihabTempDir, "hls_audio_raw.part");
                DownloadMehediHlsMediaPlaylistToFile(shihabDiscoveredAudioPlaylistUrl, shihabAudioPartPath, "HLS Audio", shihabItem, false, mehediToken);
                if (mehediToken.IsCancellationRequested) return;

                ShihabMediaProbeInfo mehediAudProbe = MehediMediaMuxer.InspectShihabMediaFile(shihabAudioPartPath);
                if (shihabIsFmp4Hls && mehediAudProbe.MehediContainerFormat == "mp4")
                {
                    shihabItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabRebuilding, "Merging HLS video + audio...");
                    NotifyShihabItemChanged(shihabItem);
                    mehediTargetFull = MehediMediaMuxer.MuxShihabVideoAndAudio(shihabVideoPartPath, shihabAudioPartPath, mehediTargetFull);
                    shihabItem.ShihabFileName = Path.GetFileName(mehediTargetFull);
                }
                else
                {
                    if (File.Exists(mehediTargetFull)) File.Delete(mehediTargetFull);
                    File.Move(shihabVideoPartPath, mehediTargetFull);
                }
            }
            else
            {
                if (File.Exists(mehediTargetFull)) File.Delete(mehediTargetFull);
                File.Move(shihabVideoPartPath, mehediTargetFull);
            }

            ComputeShihabFileSha256AndSize(shihabItem, mehediTargetFull);

            try
            {
                if (Directory.Exists(shihabTempDir)) Directory.Delete(shihabTempDir, true);
            }
            catch { }

            lock (_shihabEngineLock)
            {
                _mehediActiveCts.Remove(shihabItem.ShihabId);
            }
            shihabItem.ShihabCurrentSpeedBps = 0;
            shihabItem.MehediEtaSeconds = 0;
            shihabItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabCompleted, "Complete");
            PersistShihabState();
            NotifyShihabItemChanged(shihabItem);
            if (OnMehediItemCompleted != null)
            {
                OnMehediItemCompleted(shihabItem);
            }
        }

        private void DownloadMehediHlsMediaPlaylistToFile(
            string shihabMediaPlaylistUrl,
            string mehediOutputFilePath,
            string shihabLabelPrefix,
            ShihabDownloadItem mehediItem,
            bool shihabUpdateMainBytes,
            CancellationToken mehediToken)
        {
            string shihabManifestText = DownloadMehediText(shihabMediaPlaylistUrl, mehediItem);
            if (shihabManifestText.IndexOf("#EXT-X-KEY", StringComparison.OrdinalIgnoreCase) >= 0 &&
                shihabManifestText.IndexOf("METHOD=NONE", StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException("Protected/encrypted HLS stream cannot be downloaded (DRM/encryption measures are respected).");
            }

            Uri mehediBaseUri = new Uri(shihabMediaPlaylistUrl);
            string[] shihabLines = shihabManifestText.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> mehediSegmentUrls = new List<string>();
            List<long> shihabSegStarts = new List<long>();
            List<long> mehediSegEnds = new List<long>();

            long shihabNextRangeStart = -1L;
            long mehediNextRangeEnd = -1L;
            long shihabPrevByteRangeEnd = 0L;

            for (int shihabI = 0; shihabI < shihabLines.Length; shihabI++)
            {
                string mehediLine = shihabLines[shihabI].Trim();
                if (string.IsNullOrEmpty(mehediLine)) continue;

                if (mehediLine.StartsWith("#EXT-X-MAP:", StringComparison.OrdinalIgnoreCase))
                {
                    Match shihabMapUriMatch = Regex.Match(mehediLine, @"URI=""([^""]+)""", RegexOptions.IgnoreCase);
                    if (shihabMapUriMatch.Success)
                    {
                        Uri mehediMapUri;
                        if (Uri.TryCreate(mehediBaseUri, shihabMapUriMatch.Groups[1].Value, out mehediMapUri))
                        {
                            long shihabMapStart = -1L, mehediMapEnd = -1L;
                            Match shihabMapRange = Regex.Match(mehediLine, @"BYTERANGE=""(\d+)(?:@(\d+))?""", RegexOptions.IgnoreCase);
                            if (shihabMapRange.Success)
                            {
                                long mehediLen = long.Parse(shihabMapRange.Groups[1].Value);
                                shihabMapStart = shihabMapRange.Groups[2].Success ? long.Parse(shihabMapRange.Groups[2].Value) : 0L;
                                mehediMapEnd = shihabMapStart + mehediLen - 1L;
                            }
                            mehediSegmentUrls.Add(mehediMapUri.AbsoluteUri);
                            shihabSegStarts.Add(shihabMapStart);
                            mehediSegEnds.Add(mehediMapEnd);
                        }
                    }
                    continue;
                }

                if (mehediLine.StartsWith("#EXT-X-BYTERANGE:", StringComparison.OrdinalIgnoreCase))
                {
                    Match shihabBr = Regex.Match(mehediLine, @"(\d+)(?:@(\d+))?");
                    if (shihabBr.Success)
                    {
                        long mehediLen = long.Parse(shihabBr.Groups[1].Value);
                        shihabNextRangeStart = shihabBr.Groups[2].Success ? long.Parse(shihabBr.Groups[2].Value) : shihabPrevByteRangeEnd;
                        mehediNextRangeEnd = shihabNextRangeStart + mehediLen - 1L;
                        shihabPrevByteRangeEnd = mehediNextRangeEnd + 1L;
                    }
                    continue;
                }

                if (mehediLine.StartsWith("#")) continue;

                Uri shihabResolved;
                if (Uri.TryCreate(mehediBaseUri, mehediLine, out shihabResolved))
                {
                    mehediSegmentUrls.Add(shihabResolved.AbsoluteUri);
                    shihabSegStarts.Add(shihabNextRangeStart);
                    mehediSegEnds.Add(mehediNextRangeEnd);
                    shihabNextRangeStart = -1L;
                    mehediNextRangeEnd = -1L;
                }
            }

            if (mehediSegmentUrls.Count == 0)
            {
                throw new IOException("No media segments found in M3U8 playlist.");
            }

            mehediItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabDownloading, string.Format("{0} 0/{1}", shihabLabelPrefix, mehediSegmentUrls.Count));
            NotifyShihabItemChanged(mehediItem);

            Directory.CreateDirectory(Path.GetDirectoryName(mehediOutputFilePath));
            long shihabAccumulatedBytes = 0;
            using (FileStream shihabOut = new FileStream(mehediOutputFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] mehediBuf = new byte[32768];
                for (int mehediIdx = 0; mehediIdx < mehediSegmentUrls.Count; mehediIdx++)
                {
                    if (mehediToken.IsCancellationRequested) return;
                    HttpWebRequest shihabSegReq = CreateShihabHttpRequest(
                        mehediSegmentUrls[mehediIdx],
                        "GET",
                        mehediItem,
                        shihabSegStarts[mehediIdx],
                        mehediSegEnds[mehediIdx]);
                    using (HttpWebResponse mehediSegResp = (HttpWebResponse)shihabSegReq.GetResponse())
                    using (Stream shihabSegStream = mehediSegResp.GetResponseStream())
                    {
                        int shihabRead;
                        while ((shihabRead = shihabSegStream.Read(mehediBuf, 0, mehediBuf.Length)) > 0)
                        {
                            shihabOut.Write(mehediBuf, 0, shihabRead);
                            shihabAccumulatedBytes += shihabRead;
                        }
                    }
                    if (shihabUpdateMainBytes)
                    {
                        mehediItem.MehediDownloadedBytes = shihabAccumulatedBytes;
                        mehediItem.ShihabTotalBytes = (long)((shihabAccumulatedBytes / (double)(mehediIdx + 1)) * mehediSegmentUrls.Count);
                    }
                    mehediItem.ShihabStatusText = string.Format("{0} {1}/{2}", shihabLabelPrefix, mehediIdx + 1, mehediSegmentUrls.Count);
                    NotifyShihabItemChanged(mehediItem);
                }
                shihabOut.Flush();
            }
        }

        private string DownloadMehediText(string shihabUrl, ShihabDownloadItem mehediItem)
        {
            HttpWebRequest shihabReq = CreateShihabHttpRequest(shihabUrl, "GET", mehediItem, -1, -1);
            using (HttpWebResponse mehediResp = (HttpWebResponse)shihabReq.GetResponse())
            using (StreamReader shihabReader = new StreamReader(mehediResp.GetResponseStream(), Encoding.UTF8))
            {
                return shihabReader.ReadToEnd();
            }
        }

        private static string SelectHighestBandwidthShihabVariant(
            string mehediMasterText,
            string shihabMasterUrl,
            out string mehediMatchedAudioPlaylistUrl)
        {
            mehediMatchedAudioPlaylistUrl = "";
            string[] mehediLines = mehediMasterText.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            long shihabBestBw = -1;
            string mehediBestUri = "";
            string shihabBestAudioGroup = "";
            Uri shihabBase = new Uri(shihabMasterUrl);

            Dictionary<string, string> mehediAudioGroupUris = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int shihabI = 0; shihabI < mehediLines.Length; shihabI++)
            {
                string mehediLine = mehediLines[shihabI].Trim();
                if (mehediLine.StartsWith("#EXT-X-MEDIA:", StringComparison.OrdinalIgnoreCase) &&
                    mehediLine.IndexOf("TYPE=AUDIO", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Match shihabGrp = Regex.Match(mehediLine, @"GROUP-ID=""([^""]+)""", RegexOptions.IgnoreCase);
                    Match mehediUriMatch = Regex.Match(mehediLine, @"URI=""([^""]+)""", RegexOptions.IgnoreCase);
                    if (shihabGrp.Success && mehediUriMatch.Success)
                    {
                        Uri shihabAudAbs;
                        if (Uri.TryCreate(shihabBase, mehediUriMatch.Groups[1].Value, out shihabAudAbs))
                        {
                            string mehediGroupKey = shihabGrp.Groups[1].Value;
                            bool shihabIsDefault = mehediLine.IndexOf("DEFAULT=YES", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!mehediAudioGroupUris.ContainsKey(mehediGroupKey) || shihabIsDefault)
                            {
                                mehediAudioGroupUris[mehediGroupKey] = shihabAudAbs.AbsoluteUri;
                            }
                        }
                    }
                }
            }

            for (int mehediI = 0; mehediI < mehediLines.Length - 1; mehediI++)
            {
                string shihabLine = mehediLines[mehediI].Trim();
                if (shihabLine.StartsWith("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase))
                {
                    long mehediBw = 0;
                    Match shihabMatch = Regex.Match(shihabLine, @"BANDWIDTH=(\d+)", RegexOptions.IgnoreCase);
                    if (shihabMatch.Success)
                    {
                        long.TryParse(shihabMatch.Groups[1].Value, out mehediBw);
                    }
                    Match mehediAudAttr = Regex.Match(shihabLine, @"AUDIO=""([^""]+)""", RegexOptions.IgnoreCase);
                    string shihabNext = mehediLines[mehediI + 1].Trim();
                    if (!shihabNext.StartsWith("#") && mehediBw >= shihabBestBw)
                    {
                        Uri mehediResolved;
                        if (Uri.TryCreate(shihabBase, shihabNext, out mehediResolved))
                        {
                            shihabBestBw = mehediBw;
                            mehediBestUri = mehediResolved.AbsoluteUri;
                            shihabBestAudioGroup = mehediAudAttr.Success ? mehediAudAttr.Groups[1].Value : "";
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(shihabBestAudioGroup) && mehediAudioGroupUris.ContainsKey(shihabBestAudioGroup))
            {
                mehediMatchedAudioPlaylistUrl = mehediAudioGroupUris[shihabBestAudioGroup];
            }
            return mehediBestUri;
        }

        private static long CalculateMehediDownloadedSum(ShihabDownloadItem shihabItem, object mehediSegmentLock)
        {
            long shihabSum = 0;
            lock (mehediSegmentLock)
            {
                for (int mehediI = 0; mehediI < shihabItem.MehediSegments.Count; mehediI++)
                {
                    shihabSum += shihabItem.MehediSegments[mehediI].MehediDownloadedBytes;
                }
            }
            return shihabSum;
        }

        public void TickMehediQueuesAndScheduler()
        {
            if (_mehediConfig.MehediQueues == null) return;
            DateTime shihabNow = DateTime.Now;
            string mehediCurrentHhMm = shihabNow.ToString("HH:mm");
            string shihabDayShort = shihabNow.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture);

            for (int mehediQ = 0; mehediQ < _mehediConfig.MehediQueues.Count; mehediQ++)
            {
                MehediQueueConfig shihabQueue = _mehediConfig.MehediQueues[mehediQ];
                bool mehediDayMatches = string.IsNullOrEmpty(shihabQueue.MehediActiveDaysCsv) ||
                    shihabQueue.MehediActiveDaysCsv.IndexOf(shihabDayShort, StringComparison.OrdinalIgnoreCase) >= 0;

                if (shihabQueue.MehediEnableScheduledStart && mehediDayMatches &&
                    shihabQueue.ShihabStartTimeHhMm == mehediCurrentHhMm &&
                    shihabQueue.ShihabLastAutoStartMinute != mehediCurrentHhMm)
                {
                    shihabQueue.ShihabLastAutoStartMinute = mehediCurrentHhMm;
                    shihabQueue.MehediIsRunningNow = true;
                }

                if (shihabQueue.MehediEnableScheduledStop &&
                    shihabQueue.ShihabStopTimeHhMm == mehediCurrentHhMm &&
                    shihabQueue.MehediLastAutoStopMinute != mehediCurrentHhMm)
                {
                    shihabQueue.MehediLastAutoStopMinute = mehediCurrentHhMm;
                    StopShihabQueue(shihabQueue.ShihabQueueName);
                }

                if (shihabQueue.MehediIsRunningNow)
                {
                    PumpRunningMehediQueue(shihabQueue);
                }
            }
        }

        public void StartShihabQueue(string mehediQueueName)
        {
            for (int shihabI = 0; shihabI < _mehediConfig.MehediQueues.Count; shihabI++)
            {
                MehediQueueConfig mehediQ = _mehediConfig.MehediQueues[shihabI];
                if (string.Equals(mehediQ.ShihabQueueName, mehediQueueName, StringComparison.OrdinalIgnoreCase))
                {
                    mehediQ.MehediIsRunningNow = true;
                    PumpRunningMehediQueue(mehediQ);
                }
            }
        }

        public void StopShihabQueue(string mehediQueueName)
        {
            for (int shihabI = 0; shihabI < _mehediConfig.MehediQueues.Count; shihabI++)
            {
                MehediQueueConfig mehediQ = _mehediConfig.MehediQueues[shihabI];
                if (string.Equals(mehediQ.ShihabQueueName, mehediQueueName, StringComparison.OrdinalIgnoreCase))
                {
                    mehediQ.MehediIsRunningNow = false;
                }
            }
            List<ShihabDownloadItem> shihabSnap = GetShihabDownloadsSnapshot();
            for (int mehediJ = 0; mehediJ < shihabSnap.Count; mehediJ++)
            {
                ShihabDownloadItem shihabIt = shihabSnap[mehediJ];
                if (string.Equals(shihabIt.MehediQueueName, mehediQueueName, StringComparison.OrdinalIgnoreCase) &&
                    IsMehediItemActive(shihabIt.ShihabId))
                {
                    PauseShihabDownload(shihabIt);
                }
            }
        }

        private void PumpRunningMehediQueue(MehediQueueConfig shihabQueue)
        {
            List<ShihabDownloadItem> mehediSnap = GetShihabDownloadsSnapshot();
            int shihabActiveInQueue = 0;
            List<ShihabDownloadItem> mehediWaiting = new List<ShihabDownloadItem>();

            for (int shihabI = 0; shihabI < mehediSnap.Count; shihabI++)
            {
                ShihabDownloadItem mehediItem = mehediSnap[shihabI];
                if (!string.Equals(mehediItem.MehediQueueName, shihabQueue.ShihabQueueName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (IsMehediItemActive(mehediItem.ShihabId))
                {
                    shihabActiveInQueue++;
                }
                else
                {
                    ShihabDownloadStatus shihabSt = mehediItem.GetShihabStatusEnum();
                    if (shihabSt == ShihabDownloadStatus.ShihabQueued ||
                        shihabSt == ShihabDownloadStatus.ShihabPaused ||
                        (shihabSt == ShihabDownloadStatus.ShihabError && mehediItem.ShihabRetryCount <= shihabQueue.ShihabRetriesPerFile))
                    {
                        mehediWaiting.Add(mehediItem);
                    }
                }
            }

            int shihabMaxConc = Math.Max(1, shihabQueue.MehediMaxConcurrentFiles);
            int mehediSlots = shihabMaxConc - shihabActiveInQueue;
            for (int shihabK = 0; shihabK < mehediWaiting.Count && shihabK < mehediSlots; shihabK++)
            {
                StartOrResumeMehediDownload(mehediWaiting[shihabK]);
                shihabActiveInQueue++;
            }

            if (shihabActiveInQueue == 0 && mehediWaiting.Count == 0)
            {
                shihabQueue.MehediIsRunningNow = false;
            }
        }

        public void PersistShihabState()
        {
            lock (_shihabEngineLock)
            {
                _shihabStore.SaveShihabDownloads(_shihabDownloads);
            }
        }

        private void NotifyShihabItemChanged(ShihabDownloadItem mehediItem)
        {
            if (OnShihabItemUpdated != null)
            {
                OnShihabItemUpdated(mehediItem);
            }
        }

        private void EmitShihabLog(string mehediText)
        {
            if (OnShihabEngineLog != null)
            {
                OnShihabEngineLog(mehediText);
            }
        }
    }
}

