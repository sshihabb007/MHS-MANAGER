using System;
using System.Collections.Generic;
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
    public sealed class DualHashResult
    {
        public string Md5Hex { get; set; }
        public string Sha256Hex { get; set; }
    }

    public static class HttpHelpers
    {
        private static bool _initialized;
        private static readonly object InitLock = new object();

        public static void EnsureNetworkingConfigured()
        {
            if (_initialized) return;
            lock (InitLock)
            {
                if (_initialized) return;
                ServicePointManager.DefaultConnectionLimit = 256;
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.MaxServicePointIdleTime = 30000;
                try
                {
                    // Enable TLS 1.2 (3072) and TLS 1.3 (12288) where supported by OS, plus TLS 1.1/1.0 fallback
                    ServicePointManager.SecurityProtocol =
                        (SecurityProtocolType)3072 |
                        (SecurityProtocolType)12288 |
                        SecurityProtocolType.Tls11 |
                        SecurityProtocolType.Tls;
                }
                catch
                {
                    ServicePointManager.SecurityProtocol =
                        (SecurityProtocolType)3072 |
                        SecurityProtocolType.Tls11 |
                        SecurityProtocolType.Tls;
                }
                _initialized = true;
            }
        }

        public static SiteLoginEntry FindMatchingSiteLogin(string url, List<SiteLoginEntry> logins)
        {
            if (string.IsNullOrWhiteSpace(url) || logins == null || logins.Count == 0)
            {
                return null;
            }

            Uri uri;
            string host = "";
            if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri))
            {
                host = uri.Host ?? "";
            }

            SiteLoginEntry best = null;
            int bestLen = -1;

            for (int i = 0; i < logins.Count; i++)
            {
                SiteLoginEntry entry = logins[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.HostPrefix) || string.IsNullOrWhiteSpace(entry.UserName))
                {
                    continue;
                }

                string pat = entry.HostPrefix.Trim();
                bool matched = false;
                if (pat.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    pat.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    matched = url.StartsWith(pat, StringComparison.OrdinalIgnoreCase);
                }
                else if (!string.IsNullOrEmpty(host))
                {
                    if (string.Equals(host, pat, StringComparison.OrdinalIgnoreCase) ||
                        host.EndsWith("." + pat.TrimStart('*', '.'), StringComparison.OrdinalIgnoreCase) ||
                        HostExclusionMatcher.WildcardMatch(host, pat))
                    {
                        matched = true;
                    }
                }

                if (matched && pat.Length > bestLen)
                {
                    best = entry;
                    bestLen = pat.Length;
                }
            }

            return best;
        }

        public static HttpWebRequest CreateRequest(
            string url,
            string userAgent,
            string referer,
            string cookies,
            string authUser,
            string authPassword,
            int timeoutMs,
            bool allowCompression)
        {
            return CreateRequest(url, userAgent, referer, cookies, authUser, authPassword, timeoutMs, allowCompression, null);
        }

        public static HttpWebRequest CreateRequest(
            string url,
            string userAgent,
            string referer,
            string cookies,
            string authUser,
            string authPassword,
            int timeoutMs,
            bool allowCompression,
            AppSettings settings)
        {
            EnsureNetworkingConfigured();

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.AllowAutoRedirect = true;
            req.MaximumAutomaticRedirections = 12;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.KeepAlive = true;
            req.AutomaticDecompression = allowCompression
                ? (DecompressionMethods.GZip | DecompressionMethods.Deflate)
                : DecompressionMethods.None;

            if (req.ServicePoint != null)
            {
                req.ServicePoint.ConnectionLimit = 256;
                req.ServicePoint.Expect100Continue = false;
                req.ServicePoint.UseNagleAlgorithm = false;
            }

            if (settings != null)
            {
                if (settings.ProxyType == ProxyMode.None)
                {
                    req.Proxy = null;
                }
                else if (settings.ProxyType == ProxyMode.Manual &&
                         !string.IsNullOrWhiteSpace(settings.ProxyAddress) &&
                         settings.ProxyPort > 0)
                {
                    try
                    {
                        WebProxy proxy = new WebProxy(settings.ProxyAddress.Trim(), settings.ProxyPort);
                        if (!string.IsNullOrWhiteSpace(settings.ProxyUser))
                        {
                            proxy.Credentials = new NetworkCredential(settings.ProxyUser.Trim(), settings.ProxyPassword ?? "");
                        }
                        req.Proxy = proxy;
                    }
                    catch
                    {
                    }
                }
            }

            string effectiveUa = userAgent;
            if (string.IsNullOrWhiteSpace(effectiveUa) && settings != null)
            {
                effectiveUa = settings.DefaultUserAgent;
            }
            req.UserAgent = !string.IsNullOrWhiteSpace(effectiveUa)
                ? effectiveUa
                : "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

            req.Accept = "*/*";

            if (!string.IsNullOrWhiteSpace(referer))
            {
                try
                {
                    req.Referer = referer.Trim();
                }
                catch
                {
                    req.Headers["Referer"] = referer.Trim();
                }
            }

            if (!string.IsNullOrWhiteSpace(cookies))
            {
                req.Headers["Cookie"] = cookies.Trim();
            }

            string effectiveUser = authUser;
            string effectivePass = authPassword;
            if (string.IsNullOrWhiteSpace(effectiveUser) && settings != null && settings.SiteLogins != null)
            {
                SiteLoginEntry login = FindMatchingSiteLogin(url, settings.SiteLogins);
                if (login != null)
                {
                    effectiveUser = login.UserName;
                    effectivePass = login.Password;
                }
            }

            if (!string.IsNullOrWhiteSpace(effectiveUser))
            {
                req.Credentials = new NetworkCredential(effectiveUser.Trim(), effectivePass ?? "");
                string pair = effectiveUser.Trim() + ":" + (effectivePass ?? "");
                string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(pair));
                req.Headers["Authorization"] = "Basic " + b64;
            }

            return req;
        }

        public static string ParseContentDispositionFileName(string contentDisposition)
        {
            if (string.IsNullOrWhiteSpace(contentDisposition)) return null;

            // RFC 5987: filename*=UTF-8''encoded_name
            Match starMatch = Regex.Match(
                contentDisposition,
                @"filename\*\s*=\s*(?:UTF-8''|utf-8''|[^']*''|)([^;\r\n]+)",
                RegexOptions.IgnoreCase);
            if (starMatch.Success)
            {
                string raw = starMatch.Groups[1].Value.Trim().Trim('"', '\'');
                try
                {
                    string decoded = Uri.UnescapeDataString(raw);
                    string clean = CategoryClassifier.SanitizeFileName(Path.GetFileName(decoded));
                    if (!string.IsNullOrWhiteSpace(clean)) return clean;
                }
                catch
                {
                }
            }

            // Standard filename="name.ext" or filename=name.ext
            Match stdMatch = Regex.Match(
                contentDisposition,
                @"filename\s*=\s*(?:""([^""]+)""|([^;\r\n]+))",
                RegexOptions.IgnoreCase);
            if (stdMatch.Success)
            {
                string val = stdMatch.Groups[1].Success ? stdMatch.Groups[1].Value : stdMatch.Groups[2].Value;
                val = val.Trim().Trim('"', '\'');
                try
                {
                    val = Uri.UnescapeDataString(val);
                }
                catch
                {
                }
                string clean = CategoryClassifier.SanitizeFileName(Path.GetFileName(val));
                if (!string.IsNullOrWhiteSpace(clean)) return clean;
            }

            return null;
        }

        public static long ParseTotalFromContentRange(string contentRange)
        {
            if (string.IsNullOrWhiteSpace(contentRange)) return -1;
            int slashIdx = contentRange.LastIndexOf('/');
            if (slashIdx < 0 || slashIdx + 1 >= contentRange.Length) return -1;
            string totalPart = contentRange.Substring(slashIdx + 1).Trim();
            if (totalPart == "*") return -1;
            long total;
            if (long.TryParse(totalPart, out total) && total >= 0)
            {
                return total;
            }
            return -1;
        }

        public static bool TryParseHttpDate(string headerValue, out DateTime utcTime)
        {
            utcTime = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(headerValue)) return false;
            DateTimeOffset dto;
            if (DateTimeOffset.TryParse(headerValue.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dto))
            {
                utcTime = dto.UtcDateTime;
                return true;
            }
            return false;
        }

        public static bool IsHlsUrlOrMime(string url, string contentType)
        {
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                string ct = contentType.ToLowerInvariant();
                if (ct.Contains("mpegurl") || ct.Contains("vnd.apple.mpegurl") || ct.Contains("x-mpegurl"))
                {
                    return true;
                }
            }
            if (!string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    Uri u = new Uri(url);
                    if (u.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    if (url.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static Task<DualHashResult> ComputeDualHashesAsync(string filePath, CancellationToken token)
        {
            return Task.Run(delegate
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128))
                using (MD5 md5 = MD5.Create())
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] buffer = new byte[1024 * 128];
                    int read;
                    while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        md5.TransformBlock(buffer, 0, read, null, 0);
                        sha.TransformBlock(buffer, 0, read, null, 0);
                    }
                    md5.TransformFinalBlock(buffer, 0, 0);
                    sha.TransformFinalBlock(buffer, 0, 0);

                    return new DualHashResult
                    {
                        Md5Hex = BytesToHex(md5.Hash),
                        Sha256Hex = BytesToHex(sha.Hash)
                    };
                }
            }, token);
        }

        public static Task<string> ComputeSha256Async(string filePath, CancellationToken token)
        {
            return Task.Run(delegate
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] buffer = new byte[1024 * 128];
                    int read;
                    while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        sha.TransformBlock(buffer, 0, read, null, 0);
                    }
                    sha.TransformFinalBlock(buffer, 0, 0);
                    return BytesToHex(sha.Hash);
                }
            }, token);
        }

        public static string BytesToHex(byte[] bytes)
        {
            if (bytes == null) return "";
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("x2"));
            }
            return sb.ToString();
        }

        public static string EnsureNonM3u8VideoFileName(string fileName)
        {
            return EnsureHlsOutputFileName(fileName, false, false);
        }

        public static string EnsureHlsOutputFileName(string fileName, bool isFmp4, bool hasExplicitFileName)
        {
            if (hasExplicitFileName && !string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }

            string extTarget = isFmp4 ? ".mp4" : ".ts";
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "stream" + extTarget;
            }
            if (fileName.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                return fileName.Substring(0, fileName.Length - 5) + extTarget;
            }
            if (isFmp4 && fileName.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
            {
                return fileName.Substring(0, fileName.Length - 3) + ".mp4";
            }
            if (fileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            {
                return fileName.Substring(0, fileName.Length - 4) + extTarget;
            }
            if (string.IsNullOrEmpty(Path.GetExtension(fileName)))
            {
                return fileName + extTarget;
            }
            return fileName;
        }
    }
}
