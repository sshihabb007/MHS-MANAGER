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

        public static async Task<List<GrabbedLink>> GrabLinksAsync(
            string pageUrl,
            string userAgent,
            string cookies,
            CancellationToken token)
        {
            HttpWebRequest req = HttpHelpers.CreateRequest(
                pageUrl, userAgent, pageUrl, cookies, null, null, 20000, true);

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
}

