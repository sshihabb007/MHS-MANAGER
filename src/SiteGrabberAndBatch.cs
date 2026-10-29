using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MHSManager
{
    public static class BatchUrlGenerator
    {
        // Matches [01-20], [1-100:2], or [a-z] / [A-Z]
        private static readonly Regex PatternRegex = new Regex(
            @"\[(\d+)-(\d+)(?::(\d+))?\]|\[([a-zA-Z])-([a-zA-Z])\]",
            RegexOptions.Compiled);

        public static List<string> Expand(string pattern, int maxResults)
        {
            List<string> results = new List<string>();
            if (string.IsNullOrWhiteSpace(pattern)) return results;

            int cap = maxResults > 0 ? maxResults : 2000;
            ExpandRecursive(pattern.Trim(), results, cap);
            return results;
        }

        private static void ExpandRecursive(string current, List<string> output, int maxResults)
        {
            if (output.Count >= maxResults) return;

            Match m = PatternRegex.Match(current);
            if (!m.Success)
            {
                output.Add(current);
                return;
            }

            string prefix = current.Substring(0, m.Index);
            string suffix = current.Substring(m.Index + m.Length);

            if (m.Groups[1].Success && m.Groups[2].Success)
            {
                string startRaw = m.Groups[1].Value;
                string endRaw = m.Groups[2].Value;
                long startVal;
                long endVal;
                if (!long.TryParse(startRaw, out startVal) || !long.TryParse(endRaw, out endVal))
                {
                    output.Add(current);
                    return;
                }

                long step = 1;
                if (m.Groups[3].Success)
                {
                    long parsedStep;
                    if (long.TryParse(m.Groups[3].Value, out parsedStep) && parsedStep > 0)
                    {
                        step = parsedStep;
                    }
                }

                int padWidth = (startRaw.Length > 1 && startRaw.StartsWith("0"))
                    ? startRaw.Length
                    : ((endRaw.Length > 1 && endRaw.StartsWith("0")) ? endRaw.Length : 0);

                if (startVal <= endVal)
                {
                    for (long v = startVal; v <= endVal && output.Count < maxResults; v += step)
                    {
                        string numStr = padWidth > 0 ? v.ToString().PadLeft(padWidth, '0') : v.ToString();
                        ExpandRecursive(prefix + numStr + suffix, output, maxResults);
                    }
                }
                else
                {
                    for (long v = startVal; v >= endVal && output.Count < maxResults; v -= step)
                    {
                        string numStr = padWidth > 0 ? v.ToString().PadLeft(padWidth, '0') : v.ToString();
                        ExpandRecursive(prefix + numStr + suffix, output, maxResults);
                    }
                }
            }
            else if (m.Groups[4].Success && m.Groups[5].Success)
            {
                char cStart = m.Groups[4].Value[0];
                char cEnd = m.Groups[5].Value[0];
                if (cStart <= cEnd)
                {
                    for (char c = cStart; c <= cEnd && output.Count < maxResults; c++)
                    {
                        ExpandRecursive(prefix + c + suffix, output, maxResults);
                    }
                }
                else
                {
                    for (char c = cStart; c >= cEnd && output.Count < maxResults; c--)
                    {
                        ExpandRecursive(prefix + c + suffix, output, maxResults);
                    }
                }
            }
        }
    }

    public sealed class GrabbedLink
    {
        public string Url { get; set; }
        public string FileName { get; set; }
        public string Category { get; set; }
        public string SourceTag { get; set; }
    }

    public static class SiteLinkGrabber
    {
        private static readonly Regex BaseHrefRegex = new Regex(
            @"<base\b[^>]*\bhref\s*=\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TagLinkRegex = new Regex(
            @"<(a|video|audio|source|img|embed)\b[^>]*\b(?:href|src|data-src)\s*=\s*(?:""([^""]+)""|'([^']+)'|([^\s>""']+))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DirectMediaUrlRegex = new Regex(
            @"https?://[^\s""'<>\\]+?\.(?:m3u8|mp4|mkv|webm|mp3|zip|7z|rar|iso|exe|msi|pdf)(?:\?[^\s""'<>\\]*)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static Task<List<GrabbedLink>> GrabLinksAsync(
            string pageUrl,
            string userAgent,
            string cookies,
            CancellationToken token)
        {
            return GrabLinksAsync(pageUrl, userAgent, cookies, null, token);
        }

        public static async Task<List<GrabbedLink>> GrabLinksAsync(
            string pageUrl,
            string userAgent,
            string cookies,
            AppSettings settings,
            CancellationToken token)
        {
            HttpWebRequest req = HttpHelpers.CreateRequest(
                pageUrl, userAgent, pageUrl, cookies, null, null, 20000, true, settings);

            string html;
            Uri effectiveBaseUri = new Uri(pageUrl);

            using (token.Register(delegate { try { req.Abort(); } catch { } }))
            using (HttpWebResponse resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
            {
                if (resp.ResponseUri != null)
                {
                    effectiveBaseUri = resp.ResponseUri;
                }
                using (Stream s = resp.GetResponseStream())
                using (StreamReader sr = new StreamReader(s, Encoding.UTF8, true))
                {
                    html = await sr.ReadToEndAsync().ConfigureAwait(false);
                }
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                return new List<GrabbedLink>();
            }

            Match baseMatch = BaseHrefRegex.Match(html);
            if (baseMatch.Success)
            {
                try
                {
                    effectiveBaseUri = new Uri(effectiveBaseUri, WebUtility.HtmlDecode(baseMatch.Groups[1].Value.Trim()));
                }
                catch
                {
                }
            }

            List<GrabbedLink> results = new List<GrabbedLink>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            MatchCollection tagMatches = TagLinkRegex.Matches(html);
            foreach (Match m in tagMatches)
            {
                string tag = m.Groups[1].Value.ToLowerInvariant();
                string rawHref = m.Groups[2].Success ? m.Groups[2].Value
                    : (m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value);

                AddCandidate(effectiveBaseUri, rawHref, "<" + tag + ">", seen, results);
            }

            MatchCollection mediaMatches = DirectMediaUrlRegex.Matches(html);
            foreach (Match m in mediaMatches)
            {
                AddCandidate(effectiveBaseUri, m.Value, "Media/Script", seen, results);
            }

            return results;
        }

        public static bool IsHtmlPageLink(string url, string fileName)
        {
            string ext = Path.GetExtension(fileName ?? "");
            if (string.Equals(ext, ".html", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".htm", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".php", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".asp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".aspx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".jsp", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (string.Equals(ext, ".bin", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Uri u = new Uri(url);
                    string pathExt = Path.GetExtension(u.AbsolutePath);
                    if (string.IsNullOrEmpty(pathExt)) return true;
                }
                catch
                {
                }
            }
            return false;
        }

        public static bool IsImageLink(string fileName)
        {
            string ext = Path.GetExtension(fileName ?? "");
            return string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".gif", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".webp", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".svg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".ico", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddCandidate(
            Uri baseUri,
            string rawHref,
            string sourceTag,
            HashSet<string> seen,
            List<GrabbedLink> output)
        {
            if (string.IsNullOrWhiteSpace(rawHref)) return;
            string decoded = WebUtility.HtmlDecode(rawHref.Trim());
            if (decoded.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                decoded.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                decoded.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                decoded.StartsWith("#"))
            {
                return;
            }

            Uri resolved;
            if (!Uri.TryCreate(baseUri, decoded, out resolved)) return;
            if (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps) return;

            string abs = resolved.AbsoluteUri;
            int hashIdx = abs.IndexOf('#');
            if (hashIdx > 0) abs = abs.Substring(0, hashIdx);

            if (!seen.Add(abs)) return;

            string fileName = CategoryClassifier.GuessFileNameFromUrl(abs);
            string category = CategoryClassifier.ClassifyByFileName(fileName);

            output.Add(new GrabbedLink
            {
                Url = abs,
                FileName = fileName,
                Category = category,
                SourceTag = sourceTag
            });
        }
    }

    public sealed class ImportedDownloadSpec
    {
        public string Url { get; set; }
        public string FileName { get; set; }
        public string Referer { get; set; }
        public string Cookies { get; set; }
        public string UserAgent { get; set; }
        public string AuthUser { get; set; }
        public string AuthPassword { get; set; }
    }

    /// <summary>
    /// Compatible reader/writer for Internet Download Manager (.ef2) export files and plain .txt URL lists.
    /// </summary>
    public static class IdmEf2Serializer
    {
        public static string ExportToEf2(IEnumerable<DownloadItem> items)
        {
            StringBuilder sb = new StringBuilder();
            if (items == null) return "";

            foreach (DownloadItem d in items)
            {
                if (d == null || string.IsNullOrWhiteSpace(d.Url)) continue;
                sb.Append("<\r\n");
                sb.Append(d.Url.Trim()).Append("\r\n");
                if (!string.IsNullOrWhiteSpace(d.Referer))
                {
                    sb.Append("referer: ").Append(d.Referer.Trim()).Append("\r\n");
                }
                if (!string.IsNullOrWhiteSpace(d.Cookies))
                {
                    sb.Append("cookies: ").Append(d.Cookies.Trim()).Append("\r\n");
                }
                if (!string.IsNullOrWhiteSpace(d.UserAgent))
                {
                    sb.Append("User-Agent: ").Append(d.UserAgent.Trim()).Append("\r\n");
                }
                if (!string.IsNullOrWhiteSpace(d.FileName) && d.HasExplicitFileName)
                {
                    sb.Append("filename: ").Append(d.FileName.Trim()).Append("\r\n");
                }
                sb.Append(">\r\n");
            }
            return sb.ToString();
        }

        public static string ExportToTextList(IEnumerable<DownloadItem> items)
        {
            StringBuilder sb = new StringBuilder();
            if (items == null) return "";
            foreach (DownloadItem d in items)
            {
                if (d == null || string.IsNullOrWhiteSpace(d.Url)) continue;
                sb.Append(d.Url.Trim()).Append("\r\n");
            }
            return sb.ToString();
        }

        public static List<ImportedDownloadSpec> ParseEf2(string content)
        {
            List<ImportedDownloadSpec> results = new List<ImportedDownloadSpec>();
            if (string.IsNullOrWhiteSpace(content)) return results;

            string[] lines = content.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            ImportedDownloadSpec current = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line == "<")
                {
                    current = new ImportedDownloadSpec();
                    continue;
                }
                if (line == ">")
                {
                    if (current != null && !string.IsNullOrWhiteSpace(current.Url))
                    {
                        results.Add(current);
                    }
                    current = null;
                    continue;
                }

                if (current == null)
                {
                    Uri standalone;
                    if (Uri.TryCreate(line, UriKind.Absolute, out standalone) &&
                        (standalone.Scheme == Uri.UriSchemeHttp || standalone.Scheme == Uri.UriSchemeHttps))
                    {
                        results.Add(new ImportedDownloadSpec { Url = line });
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(current.Url) &&
                    (line.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     line.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    current.Url = line;
                    continue;
                }

                int colon = line.IndexOf(':');
                if (colon > 0)
                {
                    string key = line.Substring(0, colon).Trim();
                    string val = line.Substring(colon + 1).Trim();
                    if (string.Equals(key, "referer", StringComparison.OrdinalIgnoreCase))
                    {
                        current.Referer = val;
                    }
                    else if (string.Equals(key, "cookies", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(key, "cookie", StringComparison.OrdinalIgnoreCase))
                    {
                        current.Cookies = val;
                    }
                    else if (string.Equals(key, "user-agent", StringComparison.OrdinalIgnoreCase))
                    {
                        current.UserAgent = val;
                    }
                    else if (string.Equals(key, "filename", StringComparison.OrdinalIgnoreCase))
                    {
                        current.FileName = CategoryClassifier.SanitizeFileName(val);
                    }
                    else if (string.Equals(key, "username", StringComparison.OrdinalIgnoreCase))
                    {
                        current.AuthUser = val;
                    }
                    else if (string.Equals(key, "password", StringComparison.OrdinalIgnoreCase))
                    {
                        current.AuthPassword = val;
                    }
                }
            }

            if (current != null && !string.IsNullOrWhiteSpace(current.Url))
            {
                results.Add(current);
            }

            return results;
        }

        public static List<ImportedDownloadSpec> ParseTextUrlList(string content)
        {
            List<ImportedDownloadSpec> list = new List<ImportedDownloadSpec>();
            if (string.IsNullOrWhiteSpace(content)) return list;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = content.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(";")) continue;
                Uri u;
                if (Uri.TryCreate(line, UriKind.Absolute, out u) &&
                    (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) &&
                    seen.Add(u.AbsoluteUri))
                {
                    list.Add(new ImportedDownloadSpec { Url = u.AbsoluteUri });
                }
            }
            return list;
        }
    }
}
