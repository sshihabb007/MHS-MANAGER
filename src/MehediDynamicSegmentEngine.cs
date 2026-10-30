using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
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

        public static bool IsRealShihabGoogleVideoDomain(string mehediUrl)
        {
            if (string.IsNullOrWhiteSpace(mehediUrl)) return false;
            Uri shihabUri;
            if (!Uri.TryCreate(mehediUrl, UriKind.Absolute, out shihabUri)) return false;
            string mehediHost = (shihabUri.Host ?? "").ToLowerInvariant();
            return mehediHost.EndsWith(".googlevideo.com", StringComparison.Ordinal) ||
                   mehediHost == "googlevideo.com";
        }

        public static string GetShihabUrlQueryParam(string mehediUrl, string shihabParamKey)
        {
            if (string.IsNullOrWhiteSpace(mehediUrl) || string.IsNullOrWhiteSpace(shihabParamKey)) return "";
            Uri shihabUri;
            if (!Uri.TryCreate(mehediUrl, UriKind.Absolute, out shihabUri)) return "";
            string mehediQuery = shihabUri.Query;
            if (string.IsNullOrEmpty(mehediQuery) || mehediQuery == "?") return "";
            string[] shihabPairs = mehediQuery.TrimStart('?').Split(new char[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
            for (int mehediI = 0; mehediI < shihabPairs.Length; mehediI++)
            {
                int shihabEq = shihabPairs[mehediI].IndexOf('=');
                if (shihabEq <= 0) continue;
                string mehediKey = shihabPairs[mehediI].Substring(0, shihabEq);
                if (string.Equals(mehediKey, shihabParamKey, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(shihabPairs[mehediI].Substring(shihabEq + 1));
                }
            }
            return "";
        }

        public static bool IsDirectMobileShihabGoogleVideoUrl(string mehediUrl)
        {
            if (!IsRealShihabGoogleVideoDomain(mehediUrl)) return false;
            string shihabClient = GetShihabUrlQueryParam(mehediUrl, "c");
            if (string.Equals(shihabClient, "VISIONOS", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (string.Equals(shihabClient, "ANDROID", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(shihabClient, "IOS", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(GetShihabUrlQueryParam(mehediUrl, "bui")))
                {
                    double mehediDurSec;
                    if (double.TryParse(
                        GetShihabUrlQueryParam(mehediUrl, "dur"),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out mehediDurSec) && mehediDurSec > 55.0)
                    {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        public static long GetShihabGoogleVideoBurstSliceBytes(string mehediUrl)
        {
            if (!IsRealShihabGoogleVideoDomain(mehediUrl))
            {
                return -1L;
            }
            string shihabMime = GetShihabUrlQueryParam(mehediUrl, "mime");
            if ((!string.IsNullOrEmpty(shihabMime) && shihabMime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) ||
                mehediUrl.IndexOf("mime=audio", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Keep each HTTP range slice inside YouTube's unthrottled ~1.5 MB audio fast-start burst window
                return 1048576L;
            }
            // Keep each HTTP range slice inside YouTube's unthrottled ~9.5 MB video fast-start burst window
            return 4194304L;
        }

        public static bool IsPoisonedWebShihabGoogleVideoUrl(string mehediUrl)
        {
            if (!IsRealShihabGoogleVideoDomain(mehediUrl)) return false;
            string shihabClient = GetShihabUrlQueryParam(mehediUrl, "c").ToUpperInvariant();
            if (shihabClient == "WEB" ||
                shihabClient == "MWEB" ||
                shihabClient == "WEB_REMIX" ||
                shihabClient == "WEB_CREATOR" ||
                shihabClient == "TVHTML5")
            {
                return true;
            }
            if (!string.IsNullOrEmpty(GetShihabUrlQueryParam(mehediUrl, "siu")) ||
                !string.IsNullOrEmpty(GetShihabUrlQueryParam(mehediUrl, "sabr")) ||
                !string.IsNullOrEmpty(GetShihabUrlQueryParam(mehediUrl, "ump")))
            {
                return true;
            }
            long mehediExpireUnix;
            if (long.TryParse(GetShihabUrlQueryParam(mehediUrl, "expire"), out mehediExpireUnix) && mehediExpireUnix > 0)
            {
                long shihabNowUnix = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                if (mehediExpireUnix <= shihabNowUnix + 30L)
                {
                    return true;
                }
            }
            return false;
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

            string shihabGvClient = IsRealShihabGoogleVideoDomain(mehediUrl) ? GetShihabUrlQueryParam(mehediUrl, "c") : "";
            bool mehediIsDirectInnerTubeGv =
                string.Equals(shihabGvClient, "VISIONOS", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(shihabGvClient, "ANDROID", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(shihabGvClient, "IOS", StringComparison.OrdinalIgnoreCase);

            string mehediUa = (mehediContextItem != null && !string.IsNullOrWhiteSpace(mehediContextItem.MehediUserAgent))
                ? mehediContextItem.MehediUserAgent
                : _mehediConfig.ShihabDefaultUserAgent;
            if (mehediIsDirectInnerTubeGv)
            {
                if (string.Equals(shihabGvClient, "VISIONOS", StringComparison.OrdinalIgnoreCase))
                {
                    mehediUa = MehediYouTubeInnerTubeResolver.ShihabVisionOsUserAgent;
                }
                else if (string.Equals(shihabGvClient, "IOS", StringComparison.OrdinalIgnoreCase))
                {
                    mehediUa = MehediYouTubeInnerTubeResolver.MehediIosUserAgent;
                }
                else
                {
                    mehediUa = MehediYouTubeInnerTubeResolver.ShihabAndroidUserAgent;
                }
            }
            if (!string.IsNullOrWhiteSpace(mehediUa))
            {
                shihabReq.UserAgent = mehediUa;
            }
            if (!mehediIsDirectInnerTubeGv && mehediContextItem != null && !string.IsNullOrWhiteSpace(mehediContextItem.MehediReferer))
            {
                try { shihabReq.Referer = mehediContextItem.MehediReferer; } catch { }
            }
            if (!mehediIsDirectInnerTubeGv && mehediContextItem != null && !string.IsNullOrWhiteSpace(mehediContextItem.MehediOriginHeader))
            {
                try { shihabReq.Headers["Origin"] = mehediContextItem.MehediOriginHeader; } catch { }
            }
            if (!mehediIsDirectInnerTubeGv && mehediContextItem != null && !mehediContextItem.MehediOmitCookies && !string.IsNullOrWhiteSpace(mehediContextItem.ShihabCookies))
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

            string mehediWatchVidId;
            if (MehediYouTubeInnerTubeResolver.TryExtractShihabYouTubeWatchVideoId(shihabUrl, out mehediWatchVidId))
            {
                ShihabYouTubeStreamsReplyDto shihabYtResolved = MehediYouTubeInnerTubeResolver.ResolveShihabYouTubeStreams(mehediWatchVidId, "");
                if (shihabYtResolved != null && shihabYtResolved.ShihabOk && shihabYtResolved.MehediStreams != null && shihabYtResolved.MehediStreams.Count > 0)
                {
                    MehediYouTubeResolvedStreamDto shihabBestVid;
                    MehediYouTubeResolvedStreamDto mehediBestAud;
                    MehediYouTubeInnerTubeResolver.SelectBestShihabStreamPair(
                        shihabYtResolved,
                        mehediContextItem != null ? mehediContextItem.ShihabQualityLabel : "",
                        false,
                        out shihabBestVid,
                        out mehediBestAud);
                    if (shihabBestVid != null)
                    {
                        shihabMeta.MehediSuccess = true;
                        shihabMeta.ShihabFinalUrl = shihabBestVid.ShihabUrl;
                        shihabMeta.ShihabContentType = shihabBestVid.MehediMimeType ?? "video/mp4";
                        shihabMeta.MehediAcceptsRanges = true;
                        long shihabCombinedSize = shihabBestVid.ShihabSizeBytes > 0
                            ? (shihabBestVid.ShihabSizeBytes + (mehediBestAud != null && mehediBestAud.ShihabSizeBytes > 0 ? mehediBestAud.ShihabSizeBytes : 0L))
                            : -1L;
                        shihabMeta.ShihabContentLength = shihabBestVid.ShihabSizeBytes > 0 ? shihabBestVid.ShihabSizeBytes : shihabCombinedSize;
                        string mehediTitleStem = ShihabFormatHelpers.SanitizeShihabFileName(
                            string.IsNullOrWhiteSpace(shihabYtResolved.ShihabTitle) ? ("YouTube_" + mehediWatchVidId) : shihabYtResolved.ShihabTitle);
                        string shihabExt = shihabBestVid.ShihabExt ?? "mp4";
                        string mehediQualPart = !string.IsNullOrWhiteSpace(shihabBestVid.ShihabResolution) ? (" [" + shihabBestVid.ShihabResolution + "]") : "";
                        shihabMeta.MehediSuggestedFileName = mehediTitleStem + mehediQualPart + "." + shihabExt;

                        if (mehediContextItem != null)
                        {
                            if (string.IsNullOrWhiteSpace(mehediContextItem.MehediReferer))
                            {
                                mehediContextItem.MehediReferer = shihabUrl;
                            }
                            mehediContextItem.MehediUrl = shihabBestVid.ShihabUrl;
                            mehediContextItem.MehediUserAgent = shihabBestVid.MehediUserAgent ?? MehediYouTubeInnerTubeResolver.ShihabVisionOsUserAgent;
                            mehediContextItem.MehediOmitCookies = true;
                            mehediContextItem.MehediOriginHeader = "";
                            if (!string.IsNullOrWhiteSpace(shihabBestVid.ShihabResolution))
                            {
                                mehediContextItem.ShihabQualityLabel = shihabBestVid.ShihabResolution;
                            }
                            if (mehediBestAud != null && !string.IsNullOrWhiteSpace(mehediBestAud.ShihabUrl))
                            {
                                mehediContextItem.MehediAudioCompanionUrl = mehediBestAud.ShihabUrl;
                                mehediContextItem.ShihabAudioCompanionBytes = mehediBestAud.ShihabSizeBytes;
                            }
                        }
                        return shihabMeta;
                    }
                }
            }

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
                if (IsDirectMobileShihabGoogleVideoUrl(shihabCleanGv) && mehediGvClen > 0)
                {
                    shihabMeta.MehediSuccess = true;
                    string mehediMimeParam = GetShihabUrlQueryParam(shihabCleanGv, "mime");
                    shihabMeta.ShihabContentType = string.IsNullOrWhiteSpace(mehediMimeParam) ? "video/mp4" : mehediMimeParam;
                    return shihabMeta;
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

                TryHealOrResolveShihabYouTubeItem(shihabItem, mehediItemTempDir, false);

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

                mehediIsGvUrl = IsShihabGoogleVideoUrl(shihabItem.MehediUrl);

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
                    if ((shihabItem.ShihabFileName == "download.bin" || shihabItem.ShihabFileName == "shihab_download.bin" || shihabItem.ShihabFileName == "videoplayback") &&
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

                if (mehediIsGvUrl && !IsDirectMobileShihabGoogleVideoUrl(shihabItem.MehediUrl))
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
                        if (IsRealShihabGoogleVideoDomain(shihabItem.MehediUrl) &&
                            TryHealOrResolveShihabYouTubeItem(shihabItem, mehediItemTempDir, true))
                        {
                            EmitShihabLog("[YouTubeAutoHeal] Refreshed expired/403 YouTube stream URL for " + shihabItem.ShihabFileName);
                            mehediHasCompanionAudio = !string.IsNullOrWhiteSpace(shihabItem.MehediAudioCompanionUrl);
                            shihabPrimaryAssembledPath = mehediHasCompanionAudio
                                ? Path.Combine(mehediItemTempDir, "primary_video_raw.part")
                                : shihabItem.GetShihabFullTargetPath();
                            DownloadDirectShihabStreamToFile(shihabItem, shihabItem.MehediUrl, shihabPrimaryAssembledPath, shihabItem.ShihabTotalBytes, true, mehediToken);
                        }
                        else
                        {
                            bool shihabCanTryBrowserRelay =
                                !IsDirectMobileShihabGoogleVideoUrl(shihabItem.MehediUrl) &&
                                ((shihabFailedReason != null && (
                                    shihabFailedReason.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    shihabFailedReason.IndexOf("401", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    shihabFailedReason.IndexOf("Forbidden", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    shihabFailedReason.IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0)) ||
                                shihabItem.ShihabSourceTabId >= 0);

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
            FileStream shihabPartFs = null;
            try
            {
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
                    mehediChunk.ShihabSegmentState = "Connecting...";
                }

                long shihabGvSliceCap = shihabItem.ShihabResumeSupported
                    ? GetShihabGoogleVideoBurstSliceBytes(shihabItem.MehediUrl)
                    : -1L;
                byte[] mehediBuf = new byte[32768];
                Stopwatch shihabItemThrottleWatch = Stopwatch.StartNew();
                long mehediItemWindowStartMs = 0;
                long shihabItemWindowBytes = 0;
                int mehediTransientRetries = 0;

                while (!mehediToken.IsCancellationRequested)
                {
                    long shihabSubStart;
                    long mehediCurrentEnd;
                    lock (shihabSegmentLock)
                    {
                        if (mehediChunk.ShihabEndByte >= 0 && mehediChunk.GetMehediRemainingBytes() <= 0)
                        {
                            break;
                        }
                        shihabSubStart = mehediChunk.MehediStartByte + mehediChunk.MehediDownloadedBytes;
                        mehediCurrentEnd = mehediChunk.ShihabEndByte;
                    }

                    long shihabReqStart = shihabItem.ShihabResumeSupported ? shihabSubStart : -1L;
                    long mehediReqEnd = (shihabItem.ShihabResumeSupported && mehediCurrentEnd >= shihabSubStart) ? mehediCurrentEnd : -1L;
                    if (shihabGvSliceCap > 0 && shihabReqStart >= 0)
                    {
                        long shihabBurstEnd = shihabReqStart + shihabGvSliceCap - 1L;
                        mehediReqEnd = (mehediReqEnd >= shihabReqStart)
                            ? Math.Min(mehediReqEnd, shihabBurstEnd)
                            : shihabBurstEnd;
                    }

                    long mehediSubSliceReadTotal = 0;
                    bool shihabReachedDynamicBoundary = false;

                    try
                    {
                        HttpWebRequest shihabReq = CreateShihabHttpRequest(shihabItem.MehediUrl, "GET", shihabItem, shihabReqStart, mehediReqEnd);
                        using (mehediToken.Register(delegate { try { shihabReq.Abort(); } catch { } }))
                        using (HttpWebResponse mehediResp = (HttpWebResponse)shihabReq.GetResponse())
                        using (Stream shihabNetStream = mehediResp.GetResponseStream())
                        {
                            if (shihabReqStart > 0 && mehediResp.StatusCode != HttpStatusCode.PartialContent)
                            {
                                throw new IOException("Server did not honor HTTP byte-range request (HTTP " + (int)mehediResp.StatusCode + ").");
                            }

                            if (!shihabItem.ShihabResumeSupported && shihabItem.ShihabTotalBytes <= 0 && mehediResp.ContentLength > 0)
                            {
                                shihabItem.ShihabTotalBytes = mehediResp.ContentLength;
                            }

                            if (shihabPartFs == null)
                            {
                                FileMode mehediFileMode = (shihabItem.ShihabResumeSupported && mehediChunk.MehediDownloadedBytes > 0 && File.Exists(mehediChunk.MehediPartFilePath))
                                    ? FileMode.Open
                                    : FileMode.Create;
                                shihabPartFs = new FileStream(mehediChunk.MehediPartFilePath, mehediFileMode, FileAccess.Write, FileShare.Read);
                                if (mehediFileMode == FileMode.Open)
                                {
                                    shihabPartFs.Seek(mehediChunk.MehediDownloadedBytes, SeekOrigin.Begin);
                                }
                            }

                            lock (shihabSegmentLock)
                            {
                                mehediChunk.ShihabSegmentState = "Receiving data...";
                            }

                            while (!mehediToken.IsCancellationRequested)
                            {
                                int mehediRead = shihabNetStream.Read(mehediBuf, 0, mehediBuf.Length);
                                if (mehediRead <= 0)
                                {
                                    break;
                                }

                                mehediSubSliceReadTotal += mehediRead;
                                int shihabToWrite = mehediRead;
                                lock (shihabSegmentLock)
                                {
                                    if (mehediChunk.ShihabEndByte >= 0)
                                    {
                                        long shihabAllowed = (mehediChunk.ShihabEndByte - mehediChunk.MehediStartByte + 1L) - mehediChunk.MehediDownloadedBytes;
                                        if (shihabAllowed <= 0)
                                        {
                                            shihabToWrite = 0;
                                            shihabReachedDynamicBoundary = true;
                                        }
                                        else if (shihabToWrite > shihabAllowed)
                                        {
                                            shihabToWrite = (int)shihabAllowed;
                                            shihabReachedDynamicBoundary = true;
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

                                if (shihabReachedDynamicBoundary)
                                {
                                    break;
                                }
                            }
                        }

                        if (mehediSubSliceReadTotal > 0)
                        {
                            mehediTransientRetries = 0;
                        }
                    }
                    catch (Exception shihabSubEx)
                    {
                        if (mehediToken.IsCancellationRequested)
                        {
                            break;
                        }
                        string mehediErrMsg = shihabSubEx.Message ?? "";
                        bool mehediIsAuthOrForbidden =
                            mehediErrMsg.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            mehediErrMsg.IndexOf("401", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            mehediErrMsg.IndexOf("Forbidden", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            mehediErrMsg.IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            mehediErrMsg.IndexOf("did not honor HTTP byte-range", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (!shihabItem.ShihabResumeSupported || mehediIsAuthOrForbidden || mehediTransientRetries >= 2)
                        {
                            throw;
                        }
                        mehediTransientRetries++;
                        Thread.Sleep(150 * mehediTransientRetries);
                        continue;
                    }

                    if (!shihabItem.ShihabResumeSupported || shihabReachedDynamicBoundary || mehediSubSliceReadTotal <= 0)
                    {
                        break;
                    }
                    if (shihabGvSliceCap <= 0 && mehediCurrentEnd < 0)
                    {
                        break;
                    }
                    if (mehediCurrentEnd < 0 && mehediReqEnd >= shihabReqStart && mehediSubSliceReadTotal < (mehediReqEnd - shihabReqStart + 1L))
                    {
                        break;
                    }
                }

                if (shihabPartFs != null)
                {
                    shihabPartFs.Flush();
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
            finally
            {
                if (shihabPartFs != null)
                {
                    try { shihabPartFs.Dispose(); } catch { }
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
                long shihabBurstCap = GetShihabGoogleVideoBurstSliceBytes(mehediCleanUrl);
                long shihabSliceBytes = shihabBurstCap > 0 ? shihabBurstCap : (2L * 1024L * 1024L);
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
                            ? Math.Min(shihabEffectiveTotal - 1L, mehediOffset + shihabSliceBytes - 1L)
                            : (mehediOffset + shihabSliceBytes - 1L);
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
                                }

                                long shihabNowMs = shihabRateWatch.ElapsedMilliseconds;
                                if (shihabNowMs - mehediLastRateMs >= 220)
                                {
                                    double mehediInstBps = ((mehediOffset - shihabLastRateBytes) * 1000.0) / Math.Max(1.0, (double)(shihabNowMs - mehediLastRateMs));
                                    shihabItem.ShihabCurrentSpeedBps = shihabItem.ShihabCurrentSpeedBps <= 1.0
                                        ? mehediInstBps
                                        : (shihabItem.ShihabCurrentSpeedBps * 0.65) + (mehediInstBps * 0.35);
                                    long shihabTargetDenom = shihabIsPrimaryStream ? shihabItem.ShihabTotalBytes : shihabEffectiveTotal;
                                    if (shihabTargetDenom > mehediOffset && shihabItem.ShihabCurrentSpeedBps > 10.0)
                                    {
                                        shihabItem.MehediEtaSeconds = (long)((shihabTargetDenom - mehediOffset) / shihabItem.ShihabCurrentSpeedBps);
                                    }
                                    shihabLastRateBytes = mehediOffset;
                                    mehediLastRateMs = shihabNowMs;
                                    if (shihabIsPrimaryStream)
                                    {
                                        shihabItem.ShihabStatusText = string.Format("{0:0.0}%", shihabItem.GetMehediPercent());
                                    }
                                    else
                                    {
                                        shihabItem.ShihabStatusText = shihabEffectiveTotal > 0
                                            ? string.Format("Audio track {0:0.0}% ({1} / {2})",
                                                Math.Min(100.0, (mehediOffset * 100.0) / shihabEffectiveTotal),
                                                ShihabFormatHelpers.FormatMehediBytes(mehediOffset),
                                                ShihabFormatHelpers.FormatMehediBytes(shihabEffectiveTotal))
                                            : ("Audio track " + ShihabFormatHelpers.FormatMehediBytes(mehediOffset));
                                    }
                                    NotifyShihabItemChanged(shihabItem);
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

            if (mehediAuxUrl.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                DownloadMehediHlsMediaPlaylistToFile(mehediAuxUrl, mehediAuxOutputPath, "HLS Audio", shihabItem, false, shihabToken);
                return;
            }

            if (IsShihabGoogleVideoUrl(mehediAuxUrl))
            {
                if (IsRealShihabGoogleVideoDomain(mehediAuxUrl) && IsPoisonedWebShihabGoogleVideoUrl(mehediAuxUrl))
                {
                    if (TryHealOrResolveShihabYouTubeItem(shihabItem, null, true) && !string.IsNullOrWhiteSpace(shihabItem.MehediAudioCompanionUrl))
                    {
                        mehediAuxUrl = shihabItem.MehediAudioCompanionUrl;
                        shihabAuxKnownBytes = shihabItem.ShihabAudioCompanionBytes;
                    }
                }

                long shihabAuxClen;
                mehediAuxUrl = SanitizeMehediGoogleVideoUrl(mehediAuxUrl, out shihabAuxClen);
                if (shihabAuxClen > 0 && shihabAuxKnownBytes <= 0)
                {
                    shihabAuxKnownBytes = shihabAuxClen;
                }

                if (!IsDirectMobileShihabGoogleVideoUrl(mehediAuxUrl))
                {
                    DownloadMehediGoogleVideoOrRelay(shihabItem, mehediAuxUrl, mehediAuxOutputPath, shihabAuxKnownBytes, false, shihabToken);
                    return;
                }
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(mehediAuxOutputPath));
                bool mehediAuxSupportsRanges = IsRealShihabGoogleVideoDomain(mehediAuxUrl);
                if (shihabAuxKnownBytes <= 0 || !mehediAuxSupportsRanges)
                {
                    ShihabProbeMetadata mehediAuxProbe = ProbeMehediUrl(mehediAuxUrl, shihabItem);
                    if (shihabToken.IsCancellationRequested) return;
                    if (mehediAuxProbe.MehediSuccess)
                    {
                        if (shihabAuxKnownBytes <= 0 && mehediAuxProbe.ShihabContentLength > 0)
                        {
                            shihabAuxKnownBytes = mehediAuxProbe.ShihabContentLength;
                        }
                        if (mehediAuxProbe.MehediAcceptsRanges)
                        {
                            mehediAuxSupportsRanges = true;
                        }
                    }
                }

                if (mehediAuxSupportsRanges && shihabAuxKnownBytes > 262144L)
                {
                    long shihabAuxSliceCap = GetShihabGoogleVideoBurstSliceBytes(mehediAuxUrl);
                    if (shihabAuxSliceCap <= 0)
                    {
                        shihabAuxSliceCap = 1048576L;
                    }
                    DownloadFastParallelShihabRangeToFile(
                        shihabItem,
                        mehediAuxUrl,
                        mehediAuxOutputPath,
                        shihabAuxKnownBytes,
                        shihabAuxSliceCap,
                        false,
                        shihabToken);
                    return;
                }

                bool shihabUseExplicitAuxRange = IsRealShihabGoogleVideoDomain(mehediAuxUrl) && shihabAuxKnownBytes > 0;
                HttpWebRequest shihabReq = CreateShihabHttpRequest(
                    mehediAuxUrl,
                    "GET",
                    shihabItem,
                    shihabUseExplicitAuxRange ? 0L : -1L,
                    shihabUseExplicitAuxRange ? (shihabAuxKnownBytes - 1L) : -1L);
                using (shihabToken.Register(delegate { try { shihabReq.Abort(); } catch { } }))
                using (HttpWebResponse mehediResp = (HttpWebResponse)shihabReq.GetResponse())
                using (Stream shihabNet = mehediResp.GetResponseStream())
                using (FileStream mehediFs = new FileStream(mehediAuxOutputPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    long shihabTotal = mehediResp.ContentLength > 0 ? mehediResp.ContentLength : shihabAuxKnownBytes;
                    byte[] mehediBuf = new byte[32768];
                    long shihabGot = 0;
                    int mehediRead;
                    Stopwatch mehediRateWatch = Stopwatch.StartNew();
                    long shihabLastRateMs = 0;
                    long mehediLastRateBytes = 0;
                    long shihabWinStart = 0;
                    long mehediWinBytes = 0;

                    while (!shihabToken.IsCancellationRequested && (mehediRead = shihabNet.Read(mehediBuf, 0, mehediBuf.Length)) > 0)
                    {
                        ApplyMehediSpeedLimiter(shihabItem, mehediRead, mehediRateWatch, ref shihabWinStart, ref mehediWinBytes, shihabToken);
                        mehediFs.Write(mehediBuf, 0, mehediRead);
                        shihabGot += mehediRead;
                        long shihabNowMs = mehediRateWatch.ElapsedMilliseconds;
                        if (shihabNowMs - shihabLastRateMs >= 200)
                        {
                            double mehediInstBps = ((shihabGot - mehediLastRateBytes) * 1000.0) / Math.Max(1.0, (double)(shihabNowMs - shihabLastRateMs));
                            shihabItem.ShihabCurrentSpeedBps = shihabItem.ShihabCurrentSpeedBps <= 1.0
                                ? mehediInstBps
                                : (shihabItem.ShihabCurrentSpeedBps * 0.65) + (mehediInstBps * 0.35);
                            if (shihabTotal > shihabGot && shihabItem.ShihabCurrentSpeedBps > 10.0)
                            {
                                shihabItem.MehediEtaSeconds = (long)((shihabTotal - shihabGot) / shihabItem.ShihabCurrentSpeedBps);
                            }
                            mehediLastRateBytes = shihabGot;
                            shihabLastRateMs = shihabNowMs;
                            shihabItem.ShihabStatusText = shihabTotal > 0
                                ? string.Format("Audio track {0:0.0}% ({1} / {2})",
                                    Math.Min(100.0, (shihabGot * 100.0) / shihabTotal),
                                    ShihabFormatHelpers.FormatMehediBytes(shihabGot),
                                    ShihabFormatHelpers.FormatMehediBytes(shihabTotal))
                                : ("Audio track " + ShihabFormatHelpers.FormatMehediBytes(shihabGot));
                            NotifyShihabItemChanged(shihabItem);
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
                if (IsRealShihabGoogleVideoDomain(mehediAuxUrl) &&
                    TryHealOrResolveShihabYouTubeItem(shihabItem, null, true) &&
                    !string.IsNullOrWhiteSpace(shihabItem.MehediAudioCompanionUrl) &&
                    !string.Equals(shihabItem.MehediAudioCompanionUrl, mehediAuxUrl, StringComparison.Ordinal))
                {
                    EmitShihabLog("[YouTubeAudioHeal] Refreshed companion audio URL for " + shihabItem.ShihabFileName);
                    DownloadDirectShihabStreamToFile(shihabItem, shihabItem.MehediAudioCompanionUrl, mehediAuxOutputPath, shihabItem.ShihabAudioCompanionBytes, false, shihabToken);
                    return;
                }
                EmitShihabLog("[AudioRelayFallback] Direct audio HTTP failed (" + mehediAuxEx.Message + "), switching to Browser Bridge Relay...");
                DownloadViaShihabBrowserRelay(shihabItem, mehediAuxUrl, mehediAuxOutputPath, shihabAuxKnownBytes, false, shihabToken);
            }
        }

        private void DownloadFastParallelShihabRangeToFile(
            ShihabDownloadItem shihabItem,
            string mehediTargetUrl,
            string shihabDestPath,
            long mehediTotalBytes,
            long shihabMaxSliceBytes,
            bool mehediIsPrimaryStream,
            CancellationToken shihabToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(shihabDestPath));

            long mehediSliceSpan = Math.Max(65536L, shihabMaxSliceBytes);
            int shihabSliceCount = (int)((mehediTotalBytes + mehediSliceSpan - 1L) / mehediSliceSpan);
            int mehediConfiguredConns = shihabItem.MehediMaxConnections > 0
                ? shihabItem.MehediMaxConnections
                : _mehediConfig.ShihabDefaultMaxConnections;
            int shihabWorkerCount = Math.Min(shihabSliceCount, Math.Max(1, Math.Min(16, mehediConfiguredConns)));

            if (mehediIsPrimaryStream)
            {
                shihabItem.ShihabTotalBytes = mehediTotalBytes;
                shihabItem.ShihabResumeSupported = true;
                shihabItem.MehediSegments = new List<MehediSegmentChunk>();
                long shihabVisSpan = mehediTotalBytes / shihabWorkerCount;
                long mehediVisCursor = 0;
                for (int shihabV = 1; shihabV <= shihabWorkerCount; shihabV++)
                {
                    long mehediVisEnd = (shihabV == shihabWorkerCount)
                        ? (mehediTotalBytes - 1L)
                        : (mehediVisCursor + shihabVisSpan - 1L);
                    shihabItem.MehediSegments.Add(new MehediSegmentChunk
                    {
                        ShihabSegmentIndex = shihabV,
                        MehediStartByte = mehediVisCursor,
                        ShihabEndByte = mehediVisEnd,
                        MehediDownloadedBytes = 0,
                        ShihabSegmentState = "Receiving data...",
                        MehediPartFilePath = shihabDestPath,
                        ShihabIsActiveWorker = true
                    });
                    mehediVisCursor = mehediVisEnd + 1L;
                }
                shihabItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabDownloading, "Receiving data...");
                NotifyShihabItemChanged(shihabItem);
            }
            else
            {
                shihabItem.ShihabStatusText = string.Format("Audio track 0.0% (0 B / {0})", ShihabFormatHelpers.FormatMehediBytes(mehediTotalBytes));
                NotifyShihabItemChanged(shihabItem);
            }

            using (FileStream mehediInitFs = new FileStream(shihabDestPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                mehediInitFs.SetLength(mehediTotalBytes);
            }

            long shihabSharedBytesDone = 0L;
            Stopwatch mehediRateWatch = Stopwatch.StartNew();
            long shihabLastTickMs = 0L;
            long mehediLastTickBytes = 0L;
            object shihabProgressLock = new object();
            int mehediNextSliceCursor = -1;
            Exception shihabFirstFatalError = null;
            object mehediErrorLock = new object();

            using (CancellationTokenSource mehediLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(shihabToken))
            {
                CancellationToken shihabWorkerToken = mehediLinkedCts.Token;
                Task[] shihabWorkers = new Task[shihabWorkerCount];

                for (int mehediW = 0; mehediW < shihabWorkerCount; mehediW++)
                {
                    int shihabWorkerIdx = mehediW;
                    shihabWorkers[mehediW] = Task.Run(delegate
                    {
                        try
                        {
                            using (FileStream mehediWorkerFs = new FileStream(shihabDestPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                            {
                                byte[] shihabWorkerBuf = new byte[65536];
                                Stopwatch mehediThrottleWatch = Stopwatch.StartNew();
                                long shihabWinStartMs = 0L;
                                long mehediWinBytes = 0L;

                                while (!shihabWorkerToken.IsCancellationRequested)
                                {
                                    int mehediSliceIdx = Interlocked.Increment(ref mehediNextSliceCursor);
                                    if (mehediSliceIdx >= shihabSliceCount)
                                    {
                                        break;
                                    }

                                    long shihabSliceStart = (long)mehediSliceIdx * mehediSliceSpan;
                                    long mehediSliceEnd = Math.Min(mehediTotalBytes - 1L, shihabSliceStart + mehediSliceSpan - 1L);
                                    long shihabExpectedSpan = (mehediSliceEnd - shihabSliceStart) + 1L;

                                    for (int mehediAttempt = 0; mehediAttempt < 3; mehediAttempt++)
                                    {
                                        long shihabGotInAttempt = 0L;
                                        try
                                        {
                                            mehediWorkerFs.Seek(shihabSliceStart, SeekOrigin.Begin);
                                            HttpWebRequest shihabReq = CreateShihabHttpRequest(mehediTargetUrl, "GET", shihabItem, shihabSliceStart, mehediSliceEnd);
                                            using (shihabWorkerToken.Register(delegate { try { shihabReq.Abort(); } catch { } }))
                                            using (HttpWebResponse mehediResp = (HttpWebResponse)shihabReq.GetResponse())
                                            using (Stream shihabNet = mehediResp.GetResponseStream())
                                            {
                                                if (shihabSliceStart > 0 && mehediResp.StatusCode != HttpStatusCode.PartialContent)
                                                {
                                                    throw new IOException("Server did not honor HTTP byte-range request (HTTP " + (int)mehediResp.StatusCode + ").");
                                                }

                                                while (!shihabWorkerToken.IsCancellationRequested)
                                                {
                                                    int mehediRead = shihabNet.Read(shihabWorkerBuf, 0, shihabWorkerBuf.Length);
                                                    if (mehediRead <= 0)
                                                    {
                                                        break;
                                                    }

                                                    int shihabWriteLen = (int)Math.Min((long)mehediRead, shihabExpectedSpan - shihabGotInAttempt);
                                                    if (shihabWriteLen <= 0)
                                                    {
                                                        break;
                                                    }

                                                    ApplyMehediSpeedLimiter(shihabItem, shihabWriteLen, mehediThrottleWatch, ref shihabWinStartMs, ref mehediWinBytes, shihabWorkerToken);
                                                    mehediWorkerFs.Write(shihabWorkerBuf, 0, shihabWriteLen);
                                                    shihabGotInAttempt += shihabWriteLen;
                                                    long mehediCurTotal = Interlocked.Add(ref shihabSharedBytesDone, shihabWriteLen);

                                                    if (mehediIsPrimaryStream && shihabWorkerIdx < shihabItem.MehediSegments.Count)
                                                    {
                                                        MehediSegmentChunk shihabVisChunk = shihabItem.MehediSegments[shihabWorkerIdx];
                                                        long mehediVisMax = shihabVisChunk.GetShihabTotalSpan();
                                                        shihabVisChunk.MehediDownloadedBytes = Math.Min(mehediVisMax, shihabVisChunk.MehediDownloadedBytes + shihabWriteLen);
                                                    }

                                                    long shihabNowMs = mehediRateWatch.ElapsedMilliseconds;
                                                    if (shihabNowMs - Interlocked.Read(ref shihabLastTickMs) >= 150)
                                                    {
                                                        if (Monitor.TryEnter(shihabProgressLock))
                                                        {
                                                            try
                                                            {
                                                                long mehediPrevTick = Interlocked.Read(ref shihabLastTickMs);
                                                                long shihabDeltaMs = shihabNowMs - mehediPrevTick;
                                                                if (shihabDeltaMs >= 150)
                                                                {
                                                                    double mehediInstBps = ((mehediCurTotal - mehediLastTickBytes) * 1000.0) / Math.Max(1.0, (double)shihabDeltaMs);
                                                                    shihabItem.ShihabCurrentSpeedBps = shihabItem.ShihabCurrentSpeedBps <= 1.0
                                                                        ? mehediInstBps
                                                                        : (shihabItem.ShihabCurrentSpeedBps * 0.6) + (mehediInstBps * 0.4);
                                                                    long shihabRemBytes = Math.Max(0L, mehediTotalBytes - mehediCurTotal);
                                                                    shihabItem.MehediEtaSeconds = shihabItem.ShihabCurrentSpeedBps > 10.0
                                                                        ? (long)(shihabRemBytes / shihabItem.ShihabCurrentSpeedBps)
                                                                        : 0L;
                                                                    mehediLastTickBytes = mehediCurTotal;
                                                                    Interlocked.Exchange(ref shihabLastTickMs, shihabNowMs);

                                                                    if (mehediIsPrimaryStream)
                                                                    {
                                                                        shihabItem.MehediDownloadedBytes = mehediCurTotal;
                                                                        shihabItem.ShihabStatusText = string.Format("{0:0.0}%", shihabItem.GetMehediPercent());
                                                                    }
                                                                    else
                                                                    {
                                                                        double mehediAudPct = Math.Min(100.0, (mehediCurTotal * 100.0) / mehediTotalBytes);
                                                                        shihabItem.ShihabStatusText = string.Format("Audio track {0:0.0}% ({1} / {2})",
                                                                            mehediAudPct,
                                                                            ShihabFormatHelpers.FormatMehediBytes(mehediCurTotal),
                                                                            ShihabFormatHelpers.FormatMehediBytes(mehediTotalBytes));
                                                                    }
                                                                    NotifyShihabItemChanged(shihabItem);
                                                                }
                                                            }
                                                            finally
                                                            {
                                                                Monitor.Exit(shihabProgressLock);
                                                            }
                                                        }
                                                    }

                                                    if (shihabGotInAttempt >= shihabExpectedSpan)
                                                    {
                                                        break;
                                                    }
                                                }

                                                if (!shihabWorkerToken.IsCancellationRequested && shihabGotInAttempt != shihabExpectedSpan)
                                                {
                                                    throw new IOException(string.Format("Incomplete parallel range slice ({0}/{1} bytes).", shihabGotInAttempt, shihabExpectedSpan));
                                                }
                                            }
                                            break;
                                        }
                                        catch (Exception mehediSliceEx)
                                        {
                                            if (shihabGotInAttempt > 0)
                                            {
                                                Interlocked.Add(ref shihabSharedBytesDone, -shihabGotInAttempt);
                                            }
                                            if (shihabWorkerToken.IsCancellationRequested)
                                            {
                                                return;
                                            }
                                            string shihabSliceMsg = mehediSliceEx.Message ?? "";
                                            bool mehediNonRetryable =
                                                shihabSliceMsg.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                shihabSliceMsg.IndexOf("401", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                shihabSliceMsg.IndexOf("Forbidden", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                shihabSliceMsg.IndexOf("did not honor HTTP byte-range", StringComparison.OrdinalIgnoreCase) >= 0;

                                            if (mehediAttempt == 2 || mehediNonRetryable)
                                            {
                                                lock (mehediErrorLock)
                                                {
                                                    if (shihabFirstFatalError == null)
                                                    {
                                                        shihabFirstFatalError = mehediSliceEx;
                                                    }
                                                }
                                                try { mehediLinkedCts.Cancel(); } catch { }
                                                return;
                                            }
                                            Thread.Sleep(120 * (mehediAttempt + 1));
                                        }
                                    }
                                }
                                mehediWorkerFs.Flush();
                            }
                        }
                        catch (Exception shihabWorkerOuterEx)
                        {
                            if (!shihabWorkerToken.IsCancellationRequested)
                            {
                                lock (mehediErrorLock)
                                {
                                    if (shihabFirstFatalError == null)
                                    {
                                        shihabFirstFatalError = shihabWorkerOuterEx;
                                    }
                                }
                                try { mehediLinkedCts.Cancel(); } catch { }
                            }
                        }
                    });
                }

                Task.WaitAll(shihabWorkers);
            }

            if (shihabToken.IsCancellationRequested)
            {
                return;
            }
            if (shihabFirstFatalError != null)
            {
                throw shihabFirstFatalError;
            }

            long mehediFinalGot = Interlocked.Read(ref shihabSharedBytesDone);
            if (mehediFinalGot != mehediTotalBytes)
            {
                throw new IOException(string.Format("Parallel stream incomplete ({0}/{1} bytes).", mehediFinalGot, mehediTotalBytes));
            }

            if (mehediIsPrimaryStream)
            {
                shihabItem.MehediDownloadedBytes = mehediTotalBytes;
                for (int shihabFinIdx = 0; shihabFinIdx < shihabItem.MehediSegments.Count; shihabFinIdx++)
                {
                    shihabItem.MehediSegments[shihabFinIdx].MehediDownloadedBytes = shihabItem.MehediSegments[shihabFinIdx].GetShihabTotalSpan();
                    shihabItem.MehediSegments[shihabFinIdx].ShihabSegmentState = "Completed.";
                    shihabItem.MehediSegments[shihabFinIdx].ShihabIsActiveWorker = false;
                }
                shihabItem.ShihabStatusText = "100.0%";
            }
            else
            {
                shihabItem.ShihabStatusText = string.Format("Audio track 100.0% ({0})", ShihabFormatHelpers.FormatMehediBytes(mehediTotalBytes));
            }
            NotifyShihabItemChanged(shihabItem);
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

        private void DownloadDirectShihabStreamToFile(
            ShihabDownloadItem shihabItem,
            string mehediDirectUrl,
            string shihabDestPath,
            long mehediExpectedBytes,
            bool shihabIsPrimaryStream,
            CancellationToken mehediToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(shihabDestPath));

            if (IsRealShihabGoogleVideoDomain(mehediDirectUrl))
            {
                long shihabDirectClen;
                mehediDirectUrl = SanitizeMehediGoogleVideoUrl(mehediDirectUrl, out shihabDirectClen);
                if (shihabDirectClen > 0 && mehediExpectedBytes <= 0)
                {
                    mehediExpectedBytes = shihabDirectClen;
                }
                if (mehediExpectedBytes > 262144L)
                {
                    long shihabGvSlice = GetShihabGoogleVideoBurstSliceBytes(mehediDirectUrl);
                    DownloadFastParallelShihabRangeToFile(
                        shihabItem,
                        mehediDirectUrl,
                        shihabDestPath,
                        mehediExpectedBytes,
                        shihabGvSlice > 0 ? shihabGvSlice : 1048576L,
                        shihabIsPrimaryStream,
                        mehediToken);
                    return;
                }
            }

            if (shihabIsPrimaryStream)
            {
                shihabItem.MehediSegments.Clear();
                MehediSegmentChunk mehediSingleSeg = new MehediSegmentChunk
                {
                    ShihabSegmentIndex = 1,
                    MehediStartByte = 0,
                    ShihabEndByte = mehediExpectedBytes > 0 ? (mehediExpectedBytes - 1L) : -1L,
                    MehediDownloadedBytes = 0,
                    ShihabSegmentState = "Receiving direct stream...",
                    MehediPartFilePath = shihabDestPath,
                    ShihabIsActiveWorker = true
                };
                shihabItem.MehediSegments.Add(mehediSingleSeg);
                shihabItem.SetMehediStatusEnum(ShihabDownloadStatus.ShihabDownloading, "Receiving data...");
                NotifyShihabItemChanged(shihabItem);
            }

            Stopwatch mehediRateWatch = Stopwatch.StartNew();
            long shihabLastBytes = 0;
            long mehediLastTickMs = 0;
            long shihabThrottleWindowMs = 0;
            long mehediThrottleWindowBytes = 0;

            bool mehediUseExplicitDirectRange = IsRealShihabGoogleVideoDomain(mehediDirectUrl) && mehediExpectedBytes > 0;
            HttpWebRequest shihabReq = CreateShihabHttpRequest(
                mehediDirectUrl,
                "GET",
                shihabItem,
                mehediUseExplicitDirectRange ? 0L : -1L,
                mehediUseExplicitDirectRange ? (mehediExpectedBytes - 1L) : -1L);
            using (mehediToken.Register(delegate { try { shihabReq.Abort(); } catch { } }))
            using (HttpWebResponse mehediResp = (HttpWebResponse)shihabReq.GetResponse())
            using (Stream shihabNet = mehediResp.GetResponseStream())
            using (FileStream mehediFs = new FileStream(shihabDestPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                long shihabTotal = mehediResp.ContentLength > 0 ? mehediResp.ContentLength : mehediExpectedBytes;
                if (shihabIsPrimaryStream && shihabTotal > 0)
                {
                    shihabItem.ShihabTotalBytes = shihabTotal;
                }

                byte[] mehediBuf = new byte[65536];
                long shihabGot = 0;
                int mehediRead;
                while (!mehediToken.IsCancellationRequested && (mehediRead = shihabNet.Read(mehediBuf, 0, mehediBuf.Length)) > 0)
                {
                    ApplyMehediSpeedLimiter(shihabItem, mehediRead, mehediRateWatch, ref shihabThrottleWindowMs, ref mehediThrottleWindowBytes, mehediToken);
                    mehediFs.Write(mehediBuf, 0, mehediRead);
                    shihabGot += mehediRead;

                    if (shihabIsPrimaryStream)
                    {
                        shihabItem.MehediDownloadedBytes = shihabGot;
                        if (shihabItem.MehediSegments.Count > 0)
                        {
                            shihabItem.MehediSegments[0].MehediDownloadedBytes = shihabGot;
                        }
                        long mehediNowMs = mehediRateWatch.ElapsedMilliseconds;
                        long shihabDeltaMs = mehediNowMs - mehediLastTickMs;
                        if (shihabDeltaMs >= 250)
                        {
                            double mehediInstBps = ((shihabGot - shihabLastBytes) * 1000.0) / Math.Max(1.0, (double)shihabDeltaMs);
                            shihabItem.ShihabCurrentSpeedBps = shihabItem.ShihabCurrentSpeedBps <= 1.0
                                ? mehediInstBps
                                : (shihabItem.ShihabCurrentSpeedBps * 0.65) + (mehediInstBps * 0.35);
                            if (shihabItem.ShihabTotalBytes > shihabGot && shihabItem.ShihabCurrentSpeedBps > 10.0)
                            {
                                shihabItem.MehediEtaSeconds = (long)((shihabItem.ShihabTotalBytes - shihabGot) / shihabItem.ShihabCurrentSpeedBps);
                            }
                            shihabLastBytes = shihabGot;
                            mehediLastTickMs = mehediNowMs;
                            shihabItem.ShihabStatusText = string.Format("{0:0.0}%", shihabItem.GetMehediPercent());
                            NotifyShihabItemChanged(shihabItem);
                        }
                    }
                    else if (shihabTotal > 0)
                    {
                        shihabItem.ShihabStatusText = string.Format("Audio track {0:0}%", Math.Min(100.0, (shihabGot * 100.0) / shihabTotal));
                        NotifyShihabItemChanged(shihabItem);
                    }
                }
                mehediFs.Flush();
                if (shihabGot <= 0 && !mehediToken.IsCancellationRequested)
                {
                    throw new IOException("Direct stream returned 0 bytes.");
                }

                if (shihabIsPrimaryStream)
                {
                    shihabItem.MehediDownloadedBytes = shihabGot;
                    if (shihabItem.ShihabTotalBytes <= 0) shihabItem.ShihabTotalBytes = shihabGot;
                    if (shihabItem.MehediSegments.Count > 0)
                    {
                        shihabItem.MehediSegments[0].MehediDownloadedBytes = shihabGot;
                        shihabItem.MehediSegments[0].ShihabSegmentState = "Completed.";
                        shihabItem.MehediSegments[0].ShihabIsActiveWorker = false;
                    }
                }
            }
        }

        private bool TryHealOrResolveShihabYouTubeItem(
            ShihabDownloadItem shihabItem,
            string mehediItemTempDir,
            bool shihabForceRefresh)
        {
            if (shihabItem == null || string.IsNullOrWhiteSpace(shihabItem.MehediUrl))
            {
                return false;
            }

            string mehediWatchVidId;
            bool shihabIsWatchUrl = MehediYouTubeInnerTubeResolver.TryExtractShihabYouTubeWatchVideoId(shihabItem.MehediUrl, out mehediWatchVidId);
            bool mehediNeedsPrimaryHeal =
                shihabForceRefresh ||
                shihabIsWatchUrl ||
                IsPoisonedWebShihabGoogleVideoUrl(shihabItem.MehediUrl) ||
                (IsRealShihabGoogleVideoDomain(shihabItem.MehediUrl) && !IsDirectMobileShihabGoogleVideoUrl(shihabItem.MehediUrl));
            bool shihabNeedsAudioHeal =
                !string.IsNullOrWhiteSpace(shihabItem.MehediAudioCompanionUrl) &&
                (shihabForceRefresh ||
                 IsPoisonedWebShihabGoogleVideoUrl(shihabItem.MehediAudioCompanionUrl) ||
                 (IsRealShihabGoogleVideoDomain(shihabItem.MehediAudioCompanionUrl) && !IsDirectMobileShihabGoogleVideoUrl(shihabItem.MehediAudioCompanionUrl)));

            if (!mehediNeedsPrimaryHeal && !shihabNeedsAudioHeal)
            {
                return false;
            }

            string shihabVideoId = MehediYouTubeInnerTubeResolver.ExtractShihabVideoIdFromItem(shihabItem);
            if (string.IsNullOrWhiteSpace(shihabVideoId))
            {
                return false;
            }

            ShihabYouTubeStreamsReplyDto mehediResolved = MehediYouTubeInnerTubeResolver.ResolveShihabYouTubeStreams(
                shihabVideoId,
                "",
                shihabForceRefresh);
            if (mehediResolved == null || !mehediResolved.ShihabOk || mehediResolved.MehediStreams == null || mehediResolved.MehediStreams.Count == 0)
            {
                return false;
            }

            string shihabExtLower = (Path.GetExtension(shihabItem.ShihabFileName ?? "") ?? "").ToLowerInvariant();
            bool mehediWantsAudioOnly =
                shihabExtLower == ".m4a" ||
                shihabExtLower == ".mp3" ||
                shihabExtLower == ".opus" ||
                shihabExtLower == ".ogg" ||
                (shihabItem.ShihabQualityLabel != null && shihabItem.ShihabQualityLabel.IndexOf("Audio", StringComparison.OrdinalIgnoreCase) >= 0);

            string shihabDesiredQual = shihabItem.ShihabQualityLabel ?? "";
            if (string.IsNullOrWhiteSpace(shihabDesiredQual) && !string.IsNullOrWhiteSpace(shihabItem.ShihabFileName))
            {
                Match mehediQualMatch = Regex.Match(shihabItem.ShihabFileName, @"\[(\d{3,4}p(?:\d{2})?)\]", RegexOptions.IgnoreCase);
                if (mehediQualMatch.Success)
                {
                    shihabDesiredQual = mehediQualMatch.Groups[1].Value;
                }
            }
            if (string.IsNullOrWhiteSpace(shihabDesiredQual) && IsRealShihabGoogleVideoDomain(shihabItem.MehediUrl))
            {
                shihabDesiredQual = MehediYouTubeInnerTubeResolver.InferShihabResolutionFromItag(GetShihabUrlQueryParam(shihabItem.MehediUrl, "itag"));
            }

            MehediYouTubeResolvedStreamDto shihabChosenPrimary;
            MehediYouTubeResolvedStreamDto mehediChosenAudio;
            MehediYouTubeInnerTubeResolver.SelectBestShihabStreamPair(
                mehediResolved,
                shihabDesiredQual,
                mehediWantsAudioOnly,
                out shihabChosenPrimary,
                out mehediChosenAudio);

            if (shihabChosenPrimary == null || string.IsNullOrWhiteSpace(shihabChosenPrimary.ShihabUrl))
            {
                return false;
            }

            if (mehediNeedsPrimaryHeal)
            {
                bool shihabUrlChanged = !string.Equals(shihabItem.MehediUrl, shihabChosenPrimary.ShihabUrl, StringComparison.Ordinal);
                shihabItem.MehediUrl = shihabChosenPrimary.ShihabUrl;
                if (shihabChosenPrimary.ShihabSizeBytes > 0)
                {
                    shihabItem.ShihabTotalBytes = shihabChosenPrimary.ShihabSizeBytes;
                }
                shihabItem.MehediUserAgent = shihabChosenPrimary.MehediUserAgent ?? MehediYouTubeInnerTubeResolver.ShihabVisionOsUserAgent;
                shihabItem.MehediOmitCookies = true;
                shihabItem.MehediOriginHeader = "";
                if (string.IsNullOrWhiteSpace(shihabItem.MehediReferer) ||
                    string.Equals(shihabItem.MehediReferer.Trim(), "https://www.youtube.com/", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(shihabItem.MehediReferer.Trim(), "https://www.youtube.com", StringComparison.OrdinalIgnoreCase))
                {
                    shihabItem.MehediReferer = "https://www.youtube.com/watch?v=" + shihabVideoId;
                }

                if (shihabUrlChanged)
                {
                    shihabItem.MehediDownloadedBytes = 0;
                    if (shihabItem.MehediSegments != null)
                    {
                        shihabItem.MehediSegments.Clear();
                    }
                    if (!string.IsNullOrWhiteSpace(mehediItemTempDir) && Directory.Exists(mehediItemTempDir))
                    {
                        try
                        {
                            string[] mehediOldParts = Directory.GetFiles(mehediItemTempDir, "*.shihabpart");
                            for (int shihabP = 0; shihabP < mehediOldParts.Length; shihabP++)
                            {
                                try { File.Delete(mehediOldParts[shihabP]); } catch { }
                            }
                        }
                        catch { }
                    }
                }

                if (!string.IsNullOrWhiteSpace(mehediResolved.ShihabTitle) &&
                    (string.IsNullOrWhiteSpace(shihabItem.ShihabFileName) ||
                     shihabItem.ShihabFileName.StartsWith("videoplayback", StringComparison.OrdinalIgnoreCase) ||
                     shihabItem.ShihabFileName == "download.bin" ||
                     ShihabFormatHelpers.IsMehediGibberishCdnFileName(shihabItem.ShihabFileName)))
                {
                    string mehediExt = "." + (string.IsNullOrWhiteSpace(shihabChosenPrimary.ShihabExt) ? (mehediWantsAudioOnly ? "m4a" : "mp4") : shihabChosenPrimary.ShihabExt);
                    string shihabTag = mehediWantsAudioOnly ? "Audio" : (shihabChosenPrimary.ShihabResolution ?? "");
                    string mehediStem = ShihabFormatHelpers.SanitizeShihabFileName(mehediResolved.ShihabTitle);
                    string shihabCandidateName = !string.IsNullOrWhiteSpace(shihabTag)
                        ? string.Format("{0} [{1}]{2}", mehediStem, shihabTag, mehediExt)
                        : (mehediStem + mehediExt);
                    shihabItem.ShihabFileName = !string.IsNullOrWhiteSpace(shihabItem.MehediSaveDirectory)
                        ? ShihabFormatHelpers.EnsureShihabUniqueFilePath(shihabItem.MehediSaveDirectory, shihabCandidateName)
                        : ShihabFormatHelpers.SanitizeShihabFileName(shihabCandidateName);
                }
            }

            if (mehediWantsAudioOnly || (shihabChosenPrimary.ShihabHasVideo && shihabChosenPrimary.MehediHasAudio))
            {
                shihabItem.MehediAudioCompanionUrl = "";
                shihabItem.ShihabAudioCompanionBytes = -1L;
            }
            else if (mehediChosenAudio != null && !string.IsNullOrWhiteSpace(mehediChosenAudio.ShihabUrl))
            {
                shihabItem.MehediAudioCompanionUrl = mehediChosenAudio.ShihabUrl;
                shihabItem.ShihabAudioCompanionBytes = mehediChosenAudio.ShihabSizeBytes;
            }

            EmitShihabLog(string.Format("[YouTubeResolver] Resolved {0} ({1}, client={2}) -> direct multi-segment CDN stream",
                shihabVideoId,
                mehediWantsAudioOnly ? "Audio" : (shihabChosenPrimary.ShihabResolution ?? "Video"),
                mehediResolved.ShihabClientUsed ?? "ANDROID"));
            PersistShihabState();
            return true;
        }
    }

    [DataContract]
    public class ShihabYouTubeStreamsReplyDto
    {
        [DataMember(Name = "ok")] public bool ShihabOk { get; set; }
        [DataMember(Name = "videoId")] public string MehediVideoId { get; set; }
        [DataMember(Name = "title")] public string ShihabTitle { get; set; }
        [DataMember(Name = "durationSec")] public int MehediDurationSec { get; set; }
        [DataMember(Name = "clientUsed")] public string ShihabClientUsed { get; set; }
        [DataMember(Name = "error")] public string MehediError { get; set; }
        [DataMember(Name = "streams")] public List<MehediYouTubeResolvedStreamDto> MehediStreams { get; set; }
    }

    [DataContract]
    public class MehediYouTubeResolvedStreamDto
    {
        [DataMember(Name = "itag")] public int ShihabItag { get; set; }
        [DataMember(Name = "url")] public string ShihabUrl { get; set; }
        [DataMember(Name = "mimeType")] public string MehediMimeType { get; set; }
        [DataMember(Name = "codecs")] public string ShihabCodecs { get; set; }
        [DataMember(Name = "ext")] public string ShihabExt { get; set; }
        [DataMember(Name = "width")] public int MehediWidth { get; set; }
        [DataMember(Name = "height")] public int ShihabHeight { get; set; }
        [DataMember(Name = "fps")] public int MehediFps { get; set; }
        [DataMember(Name = "resolution")] public string ShihabResolution { get; set; }
        [DataMember(Name = "trackRole")] public string MehediTrackRole { get; set; }
        [DataMember(Name = "hasVideo")] public bool ShihabHasVideo { get; set; }
        [DataMember(Name = "hasAudio")] public bool MehediHasAudio { get; set; }
        [DataMember(Name = "sizeBytes")] public long ShihabSizeBytes { get; set; }
        [DataMember(Name = "bitrate")] public long MehediBitrate { get; set; }
        [DataMember(Name = "isOriginalAudio")] public bool ShihabIsOriginalAudio { get; set; }
        [DataMember(Name = "isDrcAudio")] public bool MehediIsDrcAudio { get; set; }
        [DataMember(Name = "isHdr")] public bool MehediIsHdr { get; set; }
        [DataMember(Name = "userAgent")] public string MehediUserAgent { get; set; }
        [DataMember(Name = "audioCompanionUrl")] public string MehediAudioCompanionUrl { get; set; }
        [DataMember(Name = "audioCompanionBytes")] public long ShihabAudioCompanionBytes { get; set; }
    }

    [DataContract]
    internal class ShihabYtPlayerRequestDto
    {
        [DataMember(Name = "videoId")] public string ShihabVideoId { get; set; }
        [DataMember(Name = "racyCheckOk")] public bool MehediRacyCheckOk { get; set; }
        [DataMember(Name = "contentCheckOk")] public bool ShihabContentCheckOk { get; set; }
        [DataMember(Name = "context")] public MehediYtContextDto MehediContext { get; set; }
        [DataMember(Name = "playbackContext")] public MehediYtPlaybackContextDto ShihabPlaybackContext { get; set; }
    }

    [DataContract]
    internal class MehediYtContextDto
    {
        [DataMember(Name = "client")] public ShihabYtClientDto ShihabClient { get; set; }
    }

    [DataContract]
    internal class ShihabYtClientDto
    {
        [DataMember(Name = "clientName", EmitDefaultValue = false)] public string ShihabClientName { get; set; }
        [DataMember(Name = "clientVersion", EmitDefaultValue = false)] public string MehediClientVersion { get; set; }
        [DataMember(Name = "androidSdkVersion", EmitDefaultValue = false)] public int ShihabAndroidSdkVersion { get; set; }
        [DataMember(Name = "deviceMake", EmitDefaultValue = false)] public string MehediDeviceMake { get; set; }
        [DataMember(Name = "deviceModel", EmitDefaultValue = false)] public string ShihabDeviceModel { get; set; }
        [DataMember(Name = "osName", EmitDefaultValue = false)] public string MehediOsName { get; set; }
        [DataMember(Name = "osVersion", EmitDefaultValue = false)] public string ShihabOsVersion { get; set; }
        [DataMember(Name = "userAgent", EmitDefaultValue = false)] public string MehediUserAgent { get; set; }
        [DataMember(Name = "hl", EmitDefaultValue = false)] public string ShihabHl { get; set; }
        [DataMember(Name = "gl", EmitDefaultValue = false)] public string MehediGl { get; set; }
        [DataMember(Name = "utcOffsetMinutes")] public int ShihabUtcOffsetMinutes { get; set; }
        [DataMember(Name = "visitorData", EmitDefaultValue = false)] public string MehediVisitorData { get; set; }
    }

    [DataContract]
    internal class MehediYtPlaybackContextDto
    {
        [DataMember(Name = "contentPlaybackContext")] public ShihabYtContentPlaybackContextDto MehediContentPlaybackContext { get; set; }
    }

    [DataContract]
    internal class ShihabYtContentPlaybackContextDto
    {
        [DataMember(Name = "html5Preference")] public string ShihabHtml5Preference { get; set; }
    }

    [DataContract]
    internal class MehediYtPlayerResponseDto
    {
        [DataMember(Name = "playabilityStatus")] public ShihabYtPlayabilityStatusDto ShihabPlayabilityStatus { get; set; }
        [DataMember(Name = "videoDetails")] public MehediYtVideoDetailsDto MehediVideoDetails { get; set; }
        [DataMember(Name = "streamingData")] public ShihabYtStreamingDataDto ShihabStreamingData { get; set; }
    }

    [DataContract]
    internal class ShihabYtPlayabilityStatusDto
    {
        [DataMember(Name = "status")] public string ShihabStatus { get; set; }
        [DataMember(Name = "reason")] public string MehediReason { get; set; }
    }

    [DataContract]
    internal class MehediYtVideoDetailsDto
    {
        [DataMember(Name = "videoId")] public string ShihabVideoId { get; set; }
        [DataMember(Name = "title")] public string MehediTitle { get; set; }
        [DataMember(Name = "lengthSeconds")] public string ShihabLengthSeconds { get; set; }
    }

    [DataContract]
    internal class ShihabYtStreamingDataDto
    {
        [DataMember(Name = "formats")] public List<MehediYtFormatDto> ShihabFormats { get; set; }
        [DataMember(Name = "adaptiveFormats")] public List<MehediYtFormatDto> MehediAdaptiveFormats { get; set; }
    }

    [DataContract]
    internal class MehediYtFormatDto
    {
        [DataMember(Name = "itag")] public int ShihabItag { get; set; }
        [DataMember(Name = "url")] public string ShihabUrl { get; set; }
        [DataMember(Name = "signatureCipher")] public string MehediSignatureCipher { get; set; }
        [DataMember(Name = "cipher")] public string ShihabCipher { get; set; }
        [DataMember(Name = "mimeType")] public string MehediMimeType { get; set; }
        [DataMember(Name = "bitrate")] public long ShihabBitrate { get; set; }
        [DataMember(Name = "averageBitrate")] public long MehediAverageBitrate { get; set; }
        [DataMember(Name = "width")] public int ShihabWidth { get; set; }
        [DataMember(Name = "height")] public int MehediHeight { get; set; }
        [DataMember(Name = "fps")] public int ShihabFps { get; set; }
        [DataMember(Name = "qualityLabel")] public string MehediQualityLabel { get; set; }
        [DataMember(Name = "contentLength")] public string ShihabContentLength { get; set; }
        [DataMember(Name = "approxDurationMs")] public string MehediApproxDurationMs { get; set; }
        [DataMember(Name = "audioChannels")] public int ShihabAudioChannels { get; set; }
        [DataMember(Name = "audioSampleRate")] public string MehediAudioSampleRate { get; set; }
        [DataMember(Name = "drmFamilies")] public List<string> ShihabDrmFamilies { get; set; }
        [DataMember(Name = "audioTrack")] public ShihabYtAudioTrackDto MehediAudioTrack { get; set; }
        [DataMember(Name = "colorInfo")] public MehediYtColorInfoDto ShihabColorInfo { get; set; }
    }

    [DataContract]
    internal class ShihabYtAudioTrackDto
    {
        [DataMember(Name = "displayName")] public string ShihabDisplayName { get; set; }
        [DataMember(Name = "id")] public string MehediId { get; set; }
        [DataMember(Name = "audioIsDefault")] public bool ShihabAudioIsDefault { get; set; }
    }

    [DataContract]
    internal class MehediYtColorInfoDto
    {
        [DataMember(Name = "transferCharacteristics")] public string ShihabTransferCharacteristics { get; set; }
    }

    internal sealed class ShihabCachedYtEntry
    {
        public DateTime MehediCachedAtUtc;
        public ShihabYouTubeStreamsReplyDto ShihabReply;
    }

    public static class MehediYouTubeInnerTubeResolver
    {
        public const string ShihabVisionOsClientVersion = "1.02";
        public const string ShihabVisionOsUserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 15_7_3) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/26.0 Safari/605.1.15";
        public const string ShihabAndroidClientVersion = "20.10.38";
        public const string ShihabAndroidUserAgent = "com.google.android.youtube/20.10.38 (Linux; U; Android 14) gzip";
        public const string MehediIosClientVersion = "20.10.4";
        public const string MehediIosUserAgent = "com.google.ios.youtube/20.10.4 (iPhone16,2; U; CPU iOS 18_3_2 like Mac OS X;)";

        private static readonly object _shihabCacheLock = new object();
        private static readonly Dictionary<string, ShihabCachedYtEntry> _mehediStreamCache = new Dictionary<string, ShihabCachedYtEntry>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _shihabTitleToVideoIdCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string _mehediCachedVisitorData = "";
        private static DateTime _shihabVisitorDataCachedAtUtc = DateTime.MinValue;

        public static string NormalizeShihabVisitorData(string mehediRawVisitorData)
        {
            if (string.IsNullOrWhiteSpace(mehediRawVisitorData)) return "";
            string shihabCleaned = mehediRawVisitorData.Trim();
            for (int mehediPass = 0; mehediPass < 2 && shihabCleaned.IndexOf('%') >= 0; mehediPass++)
            {
                try
                {
                    shihabCleaned = Uri.UnescapeDataString(shihabCleaned);
                }
                catch
                {
                    break;
                }
            }
            return shihabCleaned.Trim();
        }

        public static string ObtainFreshShihabVisitorData(string mehediVideoId, bool shihabForceRefresh)
        {
            if (!shihabForceRefresh)
            {
                lock (_shihabCacheLock)
                {
                    if (!string.IsNullOrWhiteSpace(_mehediCachedVisitorData) &&
                        (DateTime.UtcNow - _shihabVisitorDataCachedAtUtc).TotalMinutes < 10.0)
                    {
                        return _mehediCachedVisitorData;
                    }
                }
            }

            string[] shihabProbeUrls = IsValidShihabYouTubeVideoId(mehediVideoId)
                ? new string[] { "https://www.youtube.com/watch?v=" + mehediVideoId, "https://www.youtube.com/sw.js_data" }
                : new string[] { "https://www.youtube.com/sw.js_data" };

            for (int mehediU = 0; mehediU < shihabProbeUrls.Length; mehediU++)
            {
                try
                {
                    HttpWebRequest shihabVisReq = (HttpWebRequest)WebRequest.Create(shihabProbeUrls[mehediU]);
                    shihabVisReq.Method = "GET";
                    shihabVisReq.Timeout = 10000;
                    shihabVisReq.ReadWriteTimeout = 10000;
                    shihabVisReq.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    shihabVisReq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
                    shihabVisReq.Headers["Accept-Language"] = "en-US,en;q=0.9";

                    using (HttpWebResponse mehediVisResp = (HttpWebResponse)shihabVisReq.GetResponse())
                    using (Stream shihabVisStream = mehediVisResp.GetResponseStream())
                    using (StreamReader mehediVisReader = new StreamReader(shihabVisStream, Encoding.UTF8))
                    {
                        string shihabBody = mehediVisReader.ReadToEnd();
                        Match mehediVisMatch = Regex.Match(shihabBody, @"""VISITOR_DATA""\s*:\s*""([^""]+)""");
                        if (!mehediVisMatch.Success)
                        {
                            mehediVisMatch = Regex.Match(shihabBody, @"(Cgt[A-Za-z0-9_\-%=]{16,})");
                        }
                        if (mehediVisMatch.Success)
                        {
                            string shihabNormalized = NormalizeShihabVisitorData(mehediVisMatch.Groups[1].Value);
                            if (!string.IsNullOrWhiteSpace(shihabNormalized))
                            {
                                lock (_shihabCacheLock)
                                {
                                    _mehediCachedVisitorData = shihabNormalized;
                                    _shihabVisitorDataCachedAtUtc = DateTime.UtcNow;
                                }
                                return shihabNormalized;
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            return "";
        }

        public static bool IsValidShihabYouTubeVideoId(string mehediCandidate)
        {
            if (string.IsNullOrWhiteSpace(mehediCandidate) || mehediCandidate.Length != 11)
            {
                return false;
            }
            for (int shihabI = 0; shihabI < mehediCandidate.Length; shihabI++)
            {
                char mehediCh = mehediCandidate[shihabI];
                bool shihabOk = (mehediCh >= 'A' && mehediCh <= 'Z') ||
                                (mehediCh >= 'a' && mehediCh <= 'z') ||
                                (mehediCh >= '0' && mehediCh <= '9') ||
                                mehediCh == '_' ||
                                mehediCh == '-';
                if (!shihabOk) return false;
            }
            return true;
        }

        public static bool TryExtractShihabYouTubeWatchVideoId(string mehediRawUrl, out string shihabVideoId)
        {
            shihabVideoId = "";
            if (string.IsNullOrWhiteSpace(mehediRawUrl)) return false;
            Uri shihabUri;
            if (!Uri.TryCreate(mehediRawUrl.Trim(), UriKind.Absolute, out shihabUri)) return false;

            string mehediHost = (shihabUri.Host ?? "").ToLowerInvariant();
            if (mehediHost == "youtu.be" || mehediHost.EndsWith(".youtu.be", StringComparison.Ordinal))
            {
                string shihabSeg = shihabUri.AbsolutePath.Trim('/');
                int mehediSlash = shihabSeg.IndexOf('/');
                if (mehediSlash > 0) shihabSeg = shihabSeg.Substring(0, mehediSlash);
                if (IsValidShihabYouTubeVideoId(shihabSeg))
                {
                    shihabVideoId = shihabSeg;
                    return true;
                }
                return false;
            }

            if (mehediHost == "youtube.com" ||
                mehediHost.EndsWith(".youtube.com", StringComparison.Ordinal) ||
                mehediHost == "youtube-nocookie.com" ||
                mehediHost.EndsWith(".youtube-nocookie.com", StringComparison.Ordinal))
            {
                string shihabVParam = MehediDynamicSegmentEngine.GetShihabUrlQueryParam(mehediRawUrl, "v");
                if (IsValidShihabYouTubeVideoId(shihabVParam))
                {
                    shihabVideoId = shihabVParam;
                    return true;
                }

                Match mehediPathMatch = Regex.Match(
                    shihabUri.AbsolutePath ?? "",
                    @"^/(?:shorts|embed|live|v|reels)/([A-Za-z0-9_-]{11})(?:[/?#]|$)",
                    RegexOptions.IgnoreCase);
                if (mehediPathMatch.Success && IsValidShihabYouTubeVideoId(mehediPathMatch.Groups[1].Value))
                {
                    shihabVideoId = mehediPathMatch.Groups[1].Value;
                    return true;
                }
            }
            return false;
        }

        public static string ExtractShihabVideoIdFromItem(ShihabDownloadItem shihabItem)
        {
            if (shihabItem == null) return "";

            string mehediVid;
            if (TryExtractShihabYouTubeWatchVideoId(shihabItem.MehediUrl, out mehediVid))
            {
                return mehediVid;
            }

            if (MehediDynamicSegmentEngine.IsRealShihabGoogleVideoDomain(shihabItem.MehediUrl))
            {
                string shihabDocId = MehediDynamicSegmentEngine.GetShihabUrlQueryParam(shihabItem.MehediUrl, "docid");
                if (IsValidShihabYouTubeVideoId(shihabDocId))
                {
                    return shihabDocId;
                }
            }

            if (TryExtractShihabYouTubeWatchVideoId(shihabItem.MehediReferer, out mehediVid))
            {
                return mehediVid;
            }

            if (!string.IsNullOrWhiteSpace(shihabItem.ShihabDescription))
            {
                if (TryExtractShihabYouTubeWatchVideoId(shihabItem.ShihabDescription, out mehediVid))
                {
                    return mehediVid;
                }
                Match shihabDescMatch = Regex.Match(shihabItem.ShihabDescription, @"(?:v=|yt:|youtu\.be/)([A-Za-z0-9_-]{11})");
                if (shihabDescMatch.Success && IsValidShihabYouTubeVideoId(shihabDescMatch.Groups[1].Value))
                {
                    return shihabDescMatch.Groups[1].Value;
                }
            }

            if (MehediDynamicSegmentEngine.IsRealShihabGoogleVideoDomain(shihabItem.MehediUrl) &&
                !string.IsNullOrWhiteSpace(shihabItem.ShihabFileName))
            {
                string mehediStem = Path.GetFileNameWithoutExtension(shihabItem.ShihabFileName);
                mehediStem = Regex.Replace(mehediStem, @"\s*\[[^\]]+\]\s*(?:_\d+)?$", "").Trim();
                mehediStem = Regex.Replace(mehediStem, @"_\d+$", "").Trim();
                if (mehediStem.Length >= 6 &&
                    !mehediStem.StartsWith("videoplayback", StringComparison.OrdinalIgnoreCase) &&
                    !mehediStem.StartsWith("download", StringComparison.OrdinalIgnoreCase))
                {
                    string shihabSearchedId = SearchShihabYouTubeVideoIdByTitle(mehediStem);
                    if (IsValidShihabYouTubeVideoId(shihabSearchedId))
                    {
                        return shihabSearchedId;
                    }
                }
            }

            return "";
        }

        public static string SearchShihabYouTubeVideoIdByTitle(string mehediTitleQuery)
        {
            if (string.IsNullOrWhiteSpace(mehediTitleQuery)) return "";
            lock (_shihabCacheLock)
            {
                string shihabCached;
                if (_shihabTitleToVideoIdCache.TryGetValue(mehediTitleQuery, out shihabCached))
                {
                    return shihabCached;
                }
            }

            try
            {
                string mehediSearchUrl = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(mehediTitleQuery) + "&sp=EgIQAQ%253D%253D";
                HttpWebRequest shihabReq = (HttpWebRequest)WebRequest.Create(mehediSearchUrl);
                shihabReq.Method = "GET";
                shihabReq.Timeout = 12000;
                shihabReq.ReadWriteTimeout = 12000;
                shihabReq.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                shihabReq.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
                shihabReq.Headers["Accept-Language"] = "en-US,en;q=0.9";

                using (HttpWebResponse mehediResp = (HttpWebResponse)shihabReq.GetResponse())
                using (Stream shihabStream = mehediResp.GetResponseStream())
                using (StreamReader mehediReader = new StreamReader(shihabStream, Encoding.UTF8))
                {
                    string shihabHtml = mehediReader.ReadToEnd();
                    Match mehediMatch = Regex.Match(shihabHtml, @"""videoRenderer""\s*:\s*\{\s*""videoId""\s*:\s*""([A-Za-z0-9_-]{11})""");
                    if (!mehediMatch.Success)
                    {
                        mehediMatch = Regex.Match(shihabHtml, @"""videoId""\s*:\s*""([A-Za-z0-9_-]{11})""");
                    }
                    if (mehediMatch.Success && IsValidShihabYouTubeVideoId(mehediMatch.Groups[1].Value))
                    {
                        string shihabFound = mehediMatch.Groups[1].Value;
                        lock (_shihabCacheLock)
                        {
                            _shihabTitleToVideoIdCache[mehediTitleQuery] = shihabFound;
                        }
                        return shihabFound;
                    }
                }
            }
            catch
            {
            }
            return "";
        }

        public static string InferShihabResolutionFromItag(string mehediItagText)
        {
            int shihabItag;
            if (!int.TryParse(mehediItagText, out shihabItag)) return "";
            switch (shihabItag)
            {
                case 313:
                case 315:
                case 401:
                case 305:
                    return "2160p";
                case 271:
                case 308:
                case 400:
                case 304:
                    return "1440p";
                case 137:
                case 299:
                case 248:
                case 303:
                case 399:
                    return "1080p";
                case 22:
                case 136:
                case 298:
                case 247:
                case 302:
                case 398:
                    return "720p";
                case 135:
                case 244:
                case 397:
                    return "480p";
                case 18:
                case 134:
                case 243:
                case 396:
                    return "360p";
                case 133:
                case 242:
                case 395:
                    return "240p";
                case 160:
                case 278:
                case 394:
                    return "144p";
                default:
                    return "";
            }
        }

        public static ShihabYouTubeStreamsReplyDto ResolveShihabYouTubeStreams(string mehediVideoId, string shihabVisitorData)
        {
            return ResolveShihabYouTubeStreams(mehediVideoId, shihabVisitorData, false);
        }

        public static ShihabYouTubeStreamsReplyDto ResolveShihabYouTubeStreams(
            string mehediVideoId,
            string shihabVisitorData,
            bool mehediForceRefresh)
        {
            ShihabYouTubeStreamsReplyDto shihabFail = new ShihabYouTubeStreamsReplyDto
            {
                ShihabOk = false,
                MehediVideoId = mehediVideoId ?? "",
                ShihabTitle = "",
                MehediStreams = new List<MehediYouTubeResolvedStreamDto>()
            };

            if (!IsValidShihabYouTubeVideoId(mehediVideoId))
            {
                shihabFail.MehediError = "Invalid YouTube videoId.";
                return shihabFail;
            }

            if (!mehediForceRefresh)
            {
                lock (_shihabCacheLock)
                {
                    ShihabCachedYtEntry mehediHit;
                    if (_mehediStreamCache.TryGetValue(mehediVideoId, out mehediHit) &&
                        mehediHit != null &&
                        mehediHit.ShihabReply != null &&
                        (DateTime.UtcNow - mehediHit.MehediCachedAtUtc).TotalMinutes < 15.0)
                    {
                        return mehediHit.ShihabReply;
                    }
                }
            }

            string mehediEffectiveVis = NormalizeShihabVisitorData(shihabVisitorData);
            if (string.IsNullOrWhiteSpace(mehediEffectiveVis))
            {
                mehediEffectiveVis = ObtainFreshShihabVisitorData(mehediVideoId, mehediForceRefresh);
            }

            string shihabLastErr = "";
            string[] mehediClientsOrder = new string[] { "VISIONOS", "ANDROID", "IOS" };
            for (int shihabC = 0; shihabC < mehediClientsOrder.Length; shihabC++)
            {
                string mehediClientKey = mehediClientsOrder[shihabC];
                string shihabAttemptErr;
                ShihabYouTubeStreamsReplyDto mehediAttempt = QuerySingleShihabInnerTubeClient(
                    mehediVideoId,
                    mehediClientKey,
                    mehediEffectiveVis,
                    out shihabAttemptErr);

                if ((mehediAttempt == null || !mehediAttempt.ShihabOk) &&
                    string.Equals(mehediClientKey, "VISIONOS", StringComparison.OrdinalIgnoreCase))
                {
                    string shihabRefreshedVis = ObtainFreshShihabVisitorData(mehediVideoId, true);
                    if (!string.IsNullOrWhiteSpace(shihabRefreshedVis) &&
                        !string.Equals(shihabRefreshedVis, mehediEffectiveVis, StringComparison.Ordinal))
                    {
                        mehediEffectiveVis = shihabRefreshedVis;
                        mehediAttempt = QuerySingleShihabInnerTubeClient(
                            mehediVideoId,
                            mehediClientKey,
                            mehediEffectiveVis,
                            out shihabAttemptErr);
                    }
                }

                if (mehediAttempt != null && mehediAttempt.ShihabOk && mehediAttempt.MehediStreams != null && mehediAttempt.MehediStreams.Count > 0)
                {
                    lock (_shihabCacheLock)
                    {
                        _mehediStreamCache[mehediVideoId] = new ShihabCachedYtEntry
                        {
                            MehediCachedAtUtc = DateTime.UtcNow,
                            ShihabReply = mehediAttempt
                        };
                    }
                    return mehediAttempt;
                }
                if (!string.IsNullOrWhiteSpace(shihabAttemptErr))
                {
                    shihabLastErr = shihabAttemptErr;
                }
            }

            shihabFail.MehediError = string.IsNullOrWhiteSpace(shihabLastErr)
                ? "No unencrypted direct YouTube streams returned."
                : shihabLastErr;
            return shihabFail;
        }

        private static ShihabYouTubeStreamsReplyDto QuerySingleShihabInnerTubeClient(
            string mehediVideoId,
            string shihabClientKind,
            string mehediVisitorData,
            out string shihabErrorOut)
        {
            shihabErrorOut = "";
            try
            {
                string shihabCleanVis = NormalizeShihabVisitorData(mehediVisitorData);
                bool shihabIsVisionOs = string.Equals(shihabClientKind, "VISIONOS", StringComparison.OrdinalIgnoreCase);
                bool mehediIsIos = string.Equals(shihabClientKind, "IOS", StringComparison.OrdinalIgnoreCase);

                string shihabUa = shihabIsVisionOs
                    ? ShihabVisionOsUserAgent
                    : (mehediIsIos ? MehediIosUserAgent : ShihabAndroidUserAgent);
                string mehediClientVer = shihabIsVisionOs
                    ? ShihabVisionOsClientVersion
                    : (mehediIsIos ? MehediIosClientVersion : ShihabAndroidClientVersion);
                string shihabClientNameHeader = shihabIsVisionOs
                    ? "101"
                    : (mehediIsIos ? "5" : "3");

                ShihabYtClientDto mehediClientObj;
                if (shihabIsVisionOs)
                {
                    mehediClientObj = new ShihabYtClientDto
                    {
                        ShihabClientName = "VISIONOS",
                        MehediClientVersion = mehediClientVer,
                        MehediDeviceMake = "Apple",
                        ShihabDeviceModel = "RealityDevice17,1",
                        MehediOsName = "visionOS",
                        ShihabOsVersion = "26.5.23O471",
                        MehediUserAgent = shihabUa,
                        ShihabHl = "en",
                        ShihabUtcOffsetMinutes = 0,
                        MehediVisitorData = string.IsNullOrWhiteSpace(shihabCleanVis) ? null : shihabCleanVis
                    };
                }
                else if (mehediIsIos)
                {
                    mehediClientObj = new ShihabYtClientDto
                    {
                        ShihabClientName = "IOS",
                        MehediClientVersion = mehediClientVer,
                        MehediDeviceMake = "Apple",
                        ShihabDeviceModel = "iPhone16,2",
                        MehediOsName = "iPhone",
                        ShihabOsVersion = "18.3.2.22D82",
                        MehediUserAgent = shihabUa,
                        ShihabHl = "en",
                        ShihabUtcOffsetMinutes = 0,
                        MehediVisitorData = string.IsNullOrWhiteSpace(shihabCleanVis) ? null : shihabCleanVis
                    };
                }
                else
                {
                    mehediClientObj = new ShihabYtClientDto
                    {
                        ShihabClientName = "ANDROID",
                        MehediClientVersion = mehediClientVer,
                        ShihabAndroidSdkVersion = 34,
                        MehediOsName = "Android",
                        ShihabOsVersion = "14",
                        MehediUserAgent = shihabUa,
                        ShihabHl = "en",
                        ShihabUtcOffsetMinutes = 0,
                        MehediVisitorData = string.IsNullOrWhiteSpace(shihabCleanVis) ? null : shihabCleanVis
                    };
                }

                ShihabYtPlayerRequestDto shihabPayload = new ShihabYtPlayerRequestDto
                {
                    ShihabVideoId = mehediVideoId,
                    MehediRacyCheckOk = true,
                    ShihabContentCheckOk = true,
                    MehediContext = new MehediYtContextDto { ShihabClient = mehediClientObj },
                    ShihabPlaybackContext = new MehediYtPlaybackContextDto
                    {
                        MehediContentPlaybackContext = new ShihabYtContentPlaybackContextDto
                        {
                            ShihabHtml5Preference = "HTML5_PREF_WANTS"
                        }
                    }
                };

                byte[] mehediBodyBytes;
                using (MemoryStream shihabMs = new MemoryStream())
                {
                    DataContractJsonSerializer mehediReqSer = new DataContractJsonSerializer(typeof(ShihabYtPlayerRequestDto));
                    mehediReqSer.WriteObject(shihabMs, shihabPayload);
                    mehediBodyBytes = shihabMs.ToArray();
                }

                HttpWebRequest shihabHttp = (HttpWebRequest)WebRequest.Create("https://www.youtube.com/youtubei/v1/player?prettyPrint=false");
                shihabHttp.Method = "POST";
                shihabHttp.Timeout = 15000;
                shihabHttp.ReadWriteTimeout = 15000;
                shihabHttp.ContentType = "application/json";
                shihabHttp.Accept = "application/json";
                shihabHttp.UserAgent = shihabUa;
                shihabHttp.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                shihabHttp.Headers["X-YouTube-Client-Name"] = shihabClientNameHeader;
                shihabHttp.Headers["X-YouTube-Client-Version"] = mehediClientVer;
                shihabHttp.Headers["X-Goog-Api-Format-Version"] = "2";
                if (!string.IsNullOrWhiteSpace(shihabCleanVis))
                {
                    shihabHttp.Headers["X-Goog-Visitor-Id"] = shihabCleanVis;
                }
                shihabHttp.ContentLength = mehediBodyBytes.Length;

                using (Stream mehediReqStream = shihabHttp.GetRequestStream())
                {
                    mehediReqStream.Write(mehediBodyBytes, 0, mehediBodyBytes.Length);
                }

                MehediYtPlayerResponseDto mehediParsedResp;
                using (HttpWebResponse shihabResp = (HttpWebResponse)shihabHttp.GetResponse())
                using (Stream mehediRespStream = shihabResp.GetResponseStream())
                {
                    DataContractJsonSerializer shihabRespSer = new DataContractJsonSerializer(typeof(MehediYtPlayerResponseDto));
                    mehediParsedResp = shihabRespSer.ReadObject(mehediRespStream) as MehediYtPlayerResponseDto;
                }

                if (mehediParsedResp == null)
                {
                    shihabErrorOut = shihabClientKind + " player returned empty JSON.";
                    return null;
                }

                string shihabPlayStatus = mehediParsedResp.ShihabPlayabilityStatus != null
                    ? (mehediParsedResp.ShihabPlayabilityStatus.ShihabStatus ?? "")
                    : "";
                if (!string.Equals(shihabPlayStatus, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    string mehediReason = mehediParsedResp.ShihabPlayabilityStatus != null
                        ? (mehediParsedResp.ShihabPlayabilityStatus.MehediReason ?? shihabPlayStatus)
                        : "Unplayable";
                    shihabErrorOut = shihabClientKind + ": " + mehediReason;
                    return null;
                }

                if (mehediParsedResp.ShihabStreamingData == null)
                {
                    shihabErrorOut = shihabClientKind + ": missing streamingData.";
                    return null;
                }

                List<MehediYtFormatDto> shihabRawFormats = new List<MehediYtFormatDto>();
                if (mehediParsedResp.ShihabStreamingData.ShihabFormats != null)
                {
                    shihabRawFormats.AddRange(mehediParsedResp.ShihabStreamingData.ShihabFormats);
                }
                if (mehediParsedResp.ShihabStreamingData.MehediAdaptiveFormats != null)
                {
                    shihabRawFormats.AddRange(mehediParsedResp.ShihabStreamingData.MehediAdaptiveFormats);
                }

                bool mehediHasExplicitDefaultAudio = false;
                for (int shihabI = 0; shihabI < shihabRawFormats.Count; shihabI++)
                {
                    MehediYtFormatDto mehediF = shihabRawFormats[shihabI];
                    if (mehediF != null && mehediF.MehediAudioTrack != null && mehediF.MehediAudioTrack.ShihabAudioIsDefault)
                    {
                        mehediHasExplicitDefaultAudio = true;
                        break;
                    }
                }

                int shihabDurationSec = 0;
                if (mehediParsedResp.MehediVideoDetails != null && !string.IsNullOrWhiteSpace(mehediParsedResp.MehediVideoDetails.ShihabLengthSeconds))
                {
                    int.TryParse(mehediParsedResp.MehediVideoDetails.ShihabLengthSeconds, out shihabDurationSec);
                }

                List<MehediYouTubeResolvedStreamDto> mehediCandidateStreams = new List<MehediYouTubeResolvedStreamDto>();
                for (int shihabI = 0; shihabI < shihabRawFormats.Count; shihabI++)
                {
                    MehediYtFormatDto mehediFmt = shihabRawFormats[shihabI];
                    if (mehediFmt == null) continue;
                    if (mehediFmt.ShihabDrmFamilies != null && mehediFmt.ShihabDrmFamilies.Count > 0) continue;
                    if (string.IsNullOrWhiteSpace(mehediFmt.ShihabUrl)) continue;
                    if (!string.IsNullOrWhiteSpace(mehediFmt.MehediSignatureCipher) || !string.IsNullOrWhiteSpace(mehediFmt.ShihabCipher)) continue;
                    if (MehediDynamicSegmentEngine.IsPoisonedWebShihabGoogleVideoUrl(mehediFmt.ShihabUrl)) continue;

                    // Skip foreign dubbed audio tracks when the video exposes default/original audio metadata
                    if (mehediHasExplicitDefaultAudio && mehediFmt.MehediAudioTrack != null && !mehediFmt.MehediAudioTrack.ShihabAudioIsDefault)
                    {
                        continue;
                    }

                    string shihabMimeRaw = mehediFmt.MehediMimeType ?? "";
                    string mehediBaseMime = shihabMimeRaw.Split(';')[0].Trim().ToLowerInvariant();
                    string shihabCodecs = "";
                    Match mehediCodecMatch = Regex.Match(shihabMimeRaw, @"codecs=""([^""]+)""", RegexOptions.IgnoreCase);
                    if (mehediCodecMatch.Success)
                    {
                        shihabCodecs = mehediCodecMatch.Groups[1].Value;
                    }

                    bool mehediIsAudioMime = mehediBaseMime.StartsWith("audio/", StringComparison.Ordinal);
                    bool shihabIsVideoMime = mehediBaseMime.StartsWith("video/", StringComparison.Ordinal);
                    if (!mehediIsAudioMime && !shihabIsVideoMime) continue;

                    bool shihabHasVideo = shihabIsVideoMime;
                    bool mehediHasAudio = mehediIsAudioMime ||
                                          mehediFmt.ShihabAudioChannels > 0 ||
                                          !string.IsNullOrWhiteSpace(mehediFmt.MehediAudioSampleRate) ||
                                          shihabCodecs.IndexOf("mp4a", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          shihabCodecs.IndexOf("opus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          shihabCodecs.IndexOf("vorbis", StringComparison.OrdinalIgnoreCase) >= 0;

                    string shihabExt = "mp4";
                    if (mehediBaseMime.IndexOf("webm", StringComparison.Ordinal) >= 0)
                    {
                        shihabExt = "webm";
                    }
                    else if (mehediIsAudioMime)
                    {
                        shihabExt = "m4a";
                    }
                    else if (mehediBaseMime.IndexOf("3gpp", StringComparison.Ordinal) >= 0)
                    {
                        continue;
                    }

                    long mehediClen = -1L;
                    if (!string.IsNullOrWhiteSpace(mehediFmt.ShihabContentLength))
                    {
                        long.TryParse(mehediFmt.ShihabContentLength, out mehediClen);
                    }
                    long mehediQueryClen;
                    string shihabCleanUrl = MehediDynamicSegmentEngine.SanitizeMehediGoogleVideoUrl(mehediFmt.ShihabUrl, out mehediQueryClen);
                    if (mehediClen <= 0 && mehediQueryClen > 0)
                    {
                        mehediClen = mehediQueryClen;
                    }
                    long shihabBitrate = mehediFmt.MehediAverageBitrate > 0 ? mehediFmt.MehediAverageBitrate : mehediFmt.ShihabBitrate;
                    if (mehediClen <= 0 && shihabBitrate > 0 && shihabDurationSec > 0)
                    {
                        mehediClen = (shihabBitrate * (long)shihabDurationSec) / 8L;
                    }

                    string mehediRes = "";
                    if (shihabHasVideo)
                    {
                        if (!string.IsNullOrWhiteSpace(mehediFmt.MehediQualityLabel))
                        {
                            Match shihabQlMatch = Regex.Match(mehediFmt.MehediQualityLabel.Trim(), @"^(\d{3,4}p)");
                            mehediRes = shihabQlMatch.Success ? shihabQlMatch.Groups[1].Value : mehediFmt.MehediQualityLabel.Trim();
                        }
                        else if (mehediFmt.MehediHeight > 0)
                        {
                            int shihabShortSide = mehediFmt.ShihabWidth > 0 ? Math.Min(mehediFmt.ShihabWidth, mehediFmt.MehediHeight) : mehediFmt.MehediHeight;
                            mehediRes = shihabShortSide + "p";
                        }
                    }

                    bool shihabIsOrigAud = false;
                    if (mehediFmt.MehediAudioTrack != null)
                    {
                        string mehediDisp = mehediFmt.MehediAudioTrack.ShihabDisplayName ?? "";
                        shihabIsOrigAud = mehediFmt.MehediAudioTrack.ShihabAudioIsDefault ||
                                          mehediDisp.IndexOf("original", StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    else if (mehediIsAudioMime && !mehediHasExplicitDefaultAudio)
                    {
                        shihabIsOrigAud = true;
                    }

                    string shihabXtags = MehediDynamicSegmentEngine.GetShihabUrlQueryParam(shihabCleanUrl, "xtags");
                    string mehediTrackIdStr = mehediFmt.MehediAudioTrack != null ? (mehediFmt.MehediAudioTrack.MehediId ?? "") : "";
                    bool shihabIsDrc =
                        (!string.IsNullOrEmpty(shihabXtags) && shihabXtags.IndexOf("drc=1", StringComparison.OrdinalIgnoreCase) >= 0) ||
                        (!string.IsNullOrEmpty(mehediTrackIdStr) && mehediTrackIdStr.IndexOf("drc", StringComparison.OrdinalIgnoreCase) >= 0);

                    bool mehediIsHdr = false;
                    if (mehediFmt.ShihabColorInfo != null && !string.IsNullOrWhiteSpace(mehediFmt.ShihabColorInfo.ShihabTransferCharacteristics))
                    {
                        string shihabTc = mehediFmt.ShihabColorInfo.ShihabTransferCharacteristics;
                        mehediIsHdr = shihabTc.IndexOf("SMPTEST2084", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      shihabTc.IndexOf("ARIB_STD_B67", StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    if (!string.IsNullOrWhiteSpace(mehediFmt.MehediQualityLabel) &&
                        mehediFmt.MehediQualityLabel.IndexOf("HDR", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        mehediIsHdr = true;
                    }

                    string shihabRole = (shihabHasVideo && mehediHasAudio)
                        ? "progressive"
                        : (shihabHasVideo ? "video_only" : "audio");

                    mehediCandidateStreams.Add(new MehediYouTubeResolvedStreamDto
                    {
                        ShihabItag = mehediFmt.ShihabItag,
                        ShihabUrl = shihabCleanUrl,
                        MehediMimeType = mehediBaseMime,
                        ShihabCodecs = shihabCodecs,
                        ShihabExt = shihabExt,
                        MehediWidth = mehediFmt.ShihabWidth,
                        ShihabHeight = mehediFmt.MehediHeight,
                        MehediFps = mehediFmt.ShihabFps,
                        ShihabResolution = mehediRes,
                        MehediTrackRole = shihabRole,
                        ShihabHasVideo = shihabHasVideo,
                        MehediHasAudio = mehediHasAudio,
                        ShihabSizeBytes = mehediClen,
                        MehediBitrate = shihabBitrate,
                        ShihabIsOriginalAudio = shihabIsOrigAud,
                        MehediIsDrcAudio = shihabIsDrc,
                        MehediIsHdr = mehediIsHdr,
                        MehediUserAgent = shihabUa,
                        MehediAudioCompanionUrl = "",
                        ShihabAudioCompanionBytes = -1L
                    });
                }

                if (mehediCandidateStreams.Count == 0)
                {
                    shihabErrorOut = shihabClientKind + ": zero direct formats available.";
                    return null;
                }

                // Identify best M4A audio (itag 140 preferred) and best WebM Opus audio (itag 251 preferred)
                MehediYouTubeResolvedStreamDto shihabBestM4aAudio = SelectBestShihabAudioByExt(mehediCandidateStreams, "m4a");
                MehediYouTubeResolvedStreamDto mehediBestWebmAudio = SelectBestShihabAudioByExt(mehediCandidateStreams, "webm");

                // Deduplicate video streams per resolution so each quality (2160p, 1440p, 1080p, 720p, 480p, 360p, 240p, 144p)
                // gets the single most compatible, highest-quality SDR track paired with its exact original audio track.
                Dictionary<string, MehediYouTubeResolvedStreamDto> shihabBestVideoByRes = new Dictionary<string, MehediYouTubeResolvedStreamDto>(StringComparer.OrdinalIgnoreCase);
                for (int mehediI = 0; mehediI < mehediCandidateStreams.Count; mehediI++)
                {
                    MehediYouTubeResolvedStreamDto shihabCand = mehediCandidateStreams[mehediI];
                    if (!shihabCand.ShihabHasVideo) continue;
                    string mehediKey = string.IsNullOrWhiteSpace(shihabCand.ShihabResolution)
                        ? ("itag_" + shihabCand.ShihabItag)
                        : shihabCand.ShihabResolution;

                    // Attach matching companion audio to video_only tracks
                    if (!shihabCand.MehediHasAudio)
                    {
                        MehediYouTubeResolvedStreamDto shihabAudMatch = string.Equals(shihabCand.ShihabExt, "mp4", StringComparison.OrdinalIgnoreCase)
                            ? (shihabBestM4aAudio ?? mehediBestWebmAudio)
                            : (mehediBestWebmAudio ?? shihabBestM4aAudio);
                        if (shihabAudMatch != null)
                        {
                            shihabCand.MehediAudioCompanionUrl = shihabAudMatch.ShihabUrl;
                            shihabCand.ShihabAudioCompanionBytes = shihabAudMatch.ShihabSizeBytes;
                        }
                        else
                        {
                            // Skip mute-only video if no audio track exists at all
                            continue;
                        }
                    }

                    MehediYouTubeResolvedStreamDto mehediCurrentBest;
                    if (!shihabBestVideoByRes.TryGetValue(mehediKey, out mehediCurrentBest) ||
                        CompareShihabVideoFormatPreference(shihabCand, mehediCurrentBest) > 0)
                    {
                        shihabBestVideoByRes[mehediKey] = shihabCand;
                    }
                }

                List<MehediYouTubeResolvedStreamDto> mehediFinalStreams = new List<MehediYouTubeResolvedStreamDto>(shihabBestVideoByRes.Values);
                mehediFinalStreams.Sort(delegate(MehediYouTubeResolvedStreamDto shihabA, MehediYouTubeResolvedStreamDto mehediB)
                {
                    int shihabHa = ParseMehediResolutionNumber(shihabA.ShihabResolution);
                    int mehediHb = ParseMehediResolutionNumber(mehediB.ShihabResolution);
                    if (shihabHa != mehediHb) return mehediHb.CompareTo(shihabHa);
                    return mehediB.MehediBitrate.CompareTo(shihabA.MehediBitrate);
                });

                if (shihabBestM4aAudio != null)
                {
                    mehediFinalStreams.Add(shihabBestM4aAudio);
                }
                else if (mehediBestWebmAudio != null)
                {
                    mehediFinalStreams.Add(mehediBestWebmAudio);
                }

                string shihabCleanTitle = mehediParsedResp.MehediVideoDetails != null
                    ? (mehediParsedResp.MehediVideoDetails.MehediTitle ?? "")
                    : "";

                return new ShihabYouTubeStreamsReplyDto
                {
                    ShihabOk = mehediFinalStreams.Count > 0,
                    MehediVideoId = mehediVideoId,
                    ShihabTitle = shihabCleanTitle,
                    MehediDurationSec = shihabDurationSec,
                    ShihabClientUsed = shihabIsVisionOs ? "VISIONOS" : (mehediIsIos ? "IOS" : "ANDROID"),
                    MehediError = "",
                    MehediStreams = mehediFinalStreams
                };
            }
            catch (Exception shihabEx)
            {
                shihabErrorOut = shihabClientKind + " exception: " + shihabEx.Message;
                return null;
            }
        }

        private static MehediYouTubeResolvedStreamDto SelectBestShihabAudioByExt(
            List<MehediYouTubeResolvedStreamDto> shihabAll,
            string mehediTargetExt)
        {
            MehediYouTubeResolvedStreamDto shihabBest = null;
            for (int mehediI = 0; mehediI < shihabAll.Count; mehediI++)
            {
                MehediYouTubeResolvedStreamDto shihabCand = shihabAll[mehediI];
                if (shihabCand.ShihabHasVideo || !shihabCand.MehediHasAudio) continue;
                if (!string.Equals(shihabCand.ShihabExt, mehediTargetExt, StringComparison.OrdinalIgnoreCase)) continue;
                if (shihabBest == null)
                {
                    shihabBest = shihabCand;
                    continue;
                }
                if (shihabCand.ShihabIsOriginalAudio != shihabBest.ShihabIsOriginalAudio)
                {
                    if (shihabCand.ShihabIsOriginalAudio) shihabBest = shihabCand;
                    continue;
                }
                if (shihabCand.ShihabItag == 140 && shihabBest.ShihabItag != 140)
                {
                    shihabBest = shihabCand;
                    continue;
                }
                if (shihabBest.ShihabItag == 140 && shihabCand.ShihabItag != 140)
                {
                    continue;
                }
                if (shihabCand.ShihabItag == 251 && shihabBest.ShihabItag != 251)
                {
                    shihabBest = shihabCand;
                    continue;
                }
                if (shihabBest.ShihabItag == 251 && shihabCand.ShihabItag != 251)
                {
                    continue;
                }
                if (shihabCand.MehediIsDrcAudio != shihabBest.MehediIsDrcAudio)
                {
                    if (!shihabCand.MehediIsDrcAudio) shihabBest = shihabCand;
                    continue;
                }
                if (shihabCand.MehediBitrate > shihabBest.MehediBitrate)
                {
                    shihabBest = shihabCand;
                }
            }
            return shihabBest;
        }

        private static int CompareShihabVideoFormatPreference(
            MehediYouTubeResolvedStreamDto shihabCand,
            MehediYouTubeResolvedStreamDto mehediExisting)
        {
            // 1. Prefer SDR over HDR for universal playback compatibility
            if (shihabCand.MehediIsHdr != mehediExisting.MehediIsHdr)
            {
                return shihabCand.MehediIsHdr ? -1 : 1;
            }

            // 2. Prefer MP4 container over WebM when available at the same resolution
            bool shihabCandMp4 = string.Equals(shihabCand.ShihabExt, "mp4", StringComparison.OrdinalIgnoreCase);
            bool mehediExistMp4 = string.Equals(mehediExisting.ShihabExt, "mp4", StringComparison.OrdinalIgnoreCase);
            if (shihabCandMp4 != mehediExistMp4)
            {
                return shihabCandMp4 ? 1 : -1;
            }

            // 3. Inside MP4, prefer H.264 (avc1) over AV1 (av01) for <=1080p universal player support,
            //    and prefer adaptive H.264 + 128k original M4A over low-bitrate baseline progressive itag=18
            bool shihabCandAvc = (shihabCand.ShihabCodecs ?? "").IndexOf("avc1", StringComparison.OrdinalIgnoreCase) >= 0;
            bool mehediExistAvc = (mehediExisting.ShihabCodecs ?? "").IndexOf("avc1", StringComparison.OrdinalIgnoreCase) >= 0;
            if (shihabCandAvc != mehediExistAvc)
            {
                return shihabCandAvc ? 1 : -1;
            }

            if (shihabCand.ShihabItag != 18 && mehediExisting.ShihabItag == 18 && !string.IsNullOrWhiteSpace(shihabCand.MehediAudioCompanionUrl))
            {
                return 1;
            }
            if (shihabCand.ShihabItag == 18 && mehediExisting.ShihabItag != 18 && !string.IsNullOrWhiteSpace(mehediExisting.MehediAudioCompanionUrl))
            {
                return -1;
            }

            // 4. Higher FPS / bitrate tie-breaker
            if (shihabCand.MehediFps != mehediExisting.MehediFps)
            {
                return shihabCand.MehediFps.CompareTo(mehediExisting.MehediFps);
            }
            return shihabCand.MehediBitrate.CompareTo(mehediExisting.MehediBitrate);
        }

        public static int ParseMehediResolutionNumber(string shihabRes)
        {
            if (string.IsNullOrWhiteSpace(shihabRes)) return 0;
            Match mehediMatch = Regex.Match(shihabRes, @"(\d{3,4})");
            int shihabVal;
            if (mehediMatch.Success && int.TryParse(mehediMatch.Groups[1].Value, out shihabVal))
            {
                return shihabVal;
            }
            return 0;
        }

        public static void SelectBestShihabStreamPair(
            ShihabYouTubeStreamsReplyDto mehediReply,
            string shihabDesiredQuality,
            bool mehediWantsAudioOnly,
            out MehediYouTubeResolvedStreamDto shihabSelectedPrimary,
            out MehediYouTubeResolvedStreamDto mehediSelectedCompanionAudio)
        {
            shihabSelectedPrimary = null;
            mehediSelectedCompanionAudio = null;
            if (mehediReply == null || mehediReply.MehediStreams == null || mehediReply.MehediStreams.Count == 0)
            {
                return;
            }

            MehediYouTubeResolvedStreamDto shihabAudioDto = null;
            for (int mehediI = 0; mehediI < mehediReply.MehediStreams.Count; mehediI++)
            {
                MehediYouTubeResolvedStreamDto shihabS = mehediReply.MehediStreams[mehediI];
                if (!shihabS.ShihabHasVideo && shihabS.MehediHasAudio)
                {
                    shihabAudioDto = shihabS;
                    break;
                }
            }

            if (mehediWantsAudioOnly)
            {
                shihabSelectedPrimary = shihabAudioDto;
                return;
            }

            int mehediDesiredHeight = ParseMehediResolutionNumber(shihabDesiredQuality);
            MehediYouTubeResolvedStreamDto shihabBestVideo = null;

            for (int mehediI = 0; mehediI < mehediReply.MehediStreams.Count; mehediI++)
            {
                MehediYouTubeResolvedStreamDto shihabVid = mehediReply.MehediStreams[mehediI];
                if (!shihabVid.ShihabHasVideo) continue;

                if (mehediDesiredHeight > 0)
                {
                    int shihabH = ParseMehediResolutionNumber(shihabVid.ShihabResolution);
                    if (shihabH == mehediDesiredHeight)
                    {
                        shihabBestVideo = shihabVid;
                        break;
                    }
                }
                else
                {
                    // Default when pasting a watch URL directly: prefer 1080p MP4 if available, else highest available
                    int shihabH = ParseMehediResolutionNumber(shihabVid.ShihabResolution);
                    if (shihabBestVideo == null)
                    {
                        shihabBestVideo = shihabVid;
                    }
                    if (shihabH == 1080)
                    {
                        shihabBestVideo = shihabVid;
                        break;
                    }
                }
            }

            if (shihabBestVideo == null)
            {
                for (int mehediI = 0; mehediI < mehediReply.MehediStreams.Count; mehediI++)
                {
                    if (mehediReply.MehediStreams[mehediI].ShihabHasVideo)
                    {
                        shihabBestVideo = mehediReply.MehediStreams[mehediI];
                        break;
                    }
                }
            }

            shihabSelectedPrimary = shihabBestVideo;
            if (shihabBestVideo != null && !shihabBestVideo.MehediHasAudio)
            {
                if (!string.IsNullOrWhiteSpace(shihabBestVideo.MehediAudioCompanionUrl))
                {
                    mehediSelectedCompanionAudio = new MehediYouTubeResolvedStreamDto
                    {
                        ShihabUrl = shihabBestVideo.MehediAudioCompanionUrl,
                        ShihabSizeBytes = shihabBestVideo.ShihabAudioCompanionBytes,
                        MehediHasAudio = true,
                        ShihabHasVideo = false,
                        MehediUserAgent = shihabBestVideo.MehediUserAgent
                    };
                }
                else
                {
                    mehediSelectedCompanionAudio = shihabAudioDto;
                }
            }
        }
    }
}

