using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace MHSManager
{
    public sealed class BrowserDownloadRequest
    {
        public string url { get; set; }
        public string fileName { get; set; }
        public string saveDirectory { get; set; }
        public string referer { get; set; }
        public string cookies { get; set; }
        public string userAgent { get; set; }
        public bool startImmediately { get; set; }
        public bool showDialog { get; set; }
        public bool holdInQueue { get; set; }
        public bool silent { get; set; }

        public BrowserDownloadRequest()
        {
            startImmediately = true;
            showDialog = false;
            holdInQueue = false;
            silent = false;
        }
    }

    public sealed class BrowserBatchRequest
    {
        public List<BrowserDownloadRequest> links { get; set; }
        public string referer { get; set; }
        public string cookies { get; set; }
        public string userAgent { get; set; }
        public bool startImmediately { get; set; }
        public bool showBatchDialog { get; set; }
    }

    public sealed class BrowserRefreshUrlRequest
    {
        public string id { get; set; }
        public string url { get; set; }
        public string referer { get; set; }
        public string cookies { get; set; }
        public string userAgent { get; set; }
        public bool resumeNow { get; set; }

        public BrowserRefreshUrlRequest()
        {
            resumeNow = true;
        }
    }

    public sealed class BrowserRemoveRequest
    {
        public string id { get; set; }
        public bool deleteDiskFiles { get; set; }
    }

    public sealed class BrowserQueueCommandRequest
    {
        public string queueName { get; set; }
    }

    public sealed class BrowserBridgeServer : IDisposable
    {
        private readonly DownloadManagerController _controller;
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private int _port;
        private bool _running;

        public event Action<BrowserDownloadRequest> RequestShowAddDialog;
        public event Action<BrowserBatchRequest> RequestShowBatchDialog;

        public bool IsRunning
        {
            get { return _running; }
        }

        public int Port
        {
            get { return _port; }
        }

        public BrowserBridgeServer(DownloadManagerController controller)
        {
            _controller = controller;
        }

        public bool Start(int port)
        {
            Stop();
            try
            {
                _port = port > 0 ? port : 16888;
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
                _running = true;

                CancellationToken token = _cts.Token;
                Task.Run(async delegate
                {
                    await AcceptLoopAsync(token).ConfigureAwait(false);
                });
                return true;
            }
            catch
            {
                _running = false;
                return false;
            }
        }

        public void Stop()
        {
            _running = false;
            if (_cts != null)
            {
                try { _cts.Cancel(); } catch { }
                _cts = null;
            }
            if (_listener != null)
            {
                try { _listener.Stop(); } catch { }
                _listener = null;
            }
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                TcpClient client = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch
                {
                    if (token.IsCancellationRequested || !_running) break;
                }

                if (client != null)
                {
                    TcpClient captured = client;
                    Task ignored = Task.Run(async delegate
                    {
                        await HandleClientAsync(captured).ConfigureAwait(false);
                    });
                    if (ignored == null) { }
                }
            }
        }

        private static bool IsAllowedExtensionOrigin(string origin)
        {
            if (string.IsNullOrEmpty(origin)) return false;
            return origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
                   origin.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase) ||
                   origin.StartsWith("safari-web-extension://", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsValidToken(string providedToken)
        {
            string expected = _controller.Settings.BridgeAuthToken;
            if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(providedToken))
            {
                return false;
            }
            byte[] a = Encoding.UTF8.GetBytes(expected.Trim());
            byte[] b = Encoding.UTF8.GetBytes(providedToken.Trim());
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= (a[i] ^ b[i]);
            }
            return diff == 0;
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            {
                client.ReceiveTimeout = 8000;
                client.SendTimeout = 8000;
                try
                {
                    using (NetworkStream ns = client.GetStream())
                    {
                        ParsedHttpRequest req = await ReadHttpRequestAsync(ns).ConfigureAwait(false);
                        if (req == null) return;

                        string origin = req.GetHeader("Origin");
                        bool hasOrigin = !string.IsNullOrEmpty(origin);
                        bool isExtensionOrigin = IsAllowedExtensionOrigin(origin);

                        // Strictly reject any web page origin (http://, https://, null, file://)
                        if (hasOrigin && !isExtensionOrigin)
                        {
                            await WriteJsonResponseAsync(
                                ns,
                                403,
                                "Forbidden",
                                "{\"ok\":false,\"error\":\"Cross-origin web requests are forbidden.\"}",
                                null).ConfigureAwait(false);
                            return;
                        }

                        string corsEchoOrigin = isExtensionOrigin ? origin : null;

                        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                        {
                            await WriteJsonResponseAsync(ns, 204, "No Content", "", corsEchoOrigin).ConfigureAwait(false);
                            return;
                        }

                        JavaScriptSerializer ser = new JavaScriptSerializer();
                        ser.MaxJsonLength = 16 * 1024 * 1024;
                        string tokenHeader = req.GetHeader("X-MHS-Token");
                        bool tokenValid = IsValidToken(tokenHeader);

                        if (string.Equals(req.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(req.Path, "/api/v1/ping", StringComparison.OrdinalIgnoreCase))
                        {
                            Dictionary<string, object> payload = new Dictionary<string, object>();
                            payload["ok"] = true;
                            payload["app"] = "MHS Manager";
                            payload["version"] = "1.1.0";
                            payload["port"] = _port;
                            payload["requiresToken"] = true;
                            payload["tokenValid"] = tokenValid;
                            payload["monitoredExtensions"] = _controller.Settings.MonitoredExtensions;
                            payload["excludedHosts"] = _controller.Settings.ExcludedHosts ?? "";
                            await WriteJsonResponseAsync(ns, 200, "OK", ser.Serialize(payload), corsEchoOrigin).ConfigureAwait(false);
                            return;
                        }

                        if (string.Equals(req.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(req.Path, "/api/v1/status", StringComparison.OrdinalIgnoreCase))
                        {
                            List<DownloadItem> snap = _controller.GetDownloadsSnapshot();
                            Dictionary<string, object> payload = new Dictionary<string, object>();
                            payload["ok"] = true;
                            payload["tokenValid"] = tokenValid;
                            payload["activeCount"] = _controller.ActiveDownloadCount;
                            payload["totalCount"] = snap.Count;
                            payload["totalSpeedBps"] = (long)_controller.TotalSpeedBytesPerSec;

                            if (tokenValid)
                            {
                                List<Dictionary<string, object>> itemsOut = new List<Dictionary<string, object>>();
                                int maxItems = Math.Min(50, snap.Count);
                                for (int i = 0; i < maxItems; i++)
                                {
                                    DownloadItem d = snap[i];
                                    Dictionary<string, object> row = new Dictionary<string, object>();
                                    row["id"] = d.Id;
                                    row["fileName"] = d.FileName;
                                    row["status"] = d.Status.ToString();
                                    row["totalBytes"] = d.TotalBytes;
                                    row["downloadedBytes"] = d.DownloadedBytes;
                                    row["progress"] = Math.Round(d.ProgressPercent, 1);
                                    itemsOut.Add(row);
                                }
                                payload["items"] = itemsOut;
                            }

                            await WriteJsonResponseAsync(ns, 200, "OK", ser.Serialize(payload), corsEchoOrigin).ConfigureAwait(false);
                            return;
                        }

                        // All mutating POST routes require valid X-MHS-Token
                        if (!tokenValid)
                        {
                            await WriteJsonResponseAsync(
                                ns,
                                401,
                                "Unauthorized",
                                "{\"ok\":false,\"error\":\"Missing or invalid X-MHS-Token. Pair extension via Native Host or Options -> Browser Integration.\"}",
                                corsEchoOrigin).ConfigureAwait(false);
                            return;
                        }

                        if (string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(req.Path, "/api/v1/downloads", StringComparison.OrdinalIgnoreCase))
                        {
                            BrowserDownloadRequest dlReq = ser.Deserialize<BrowserDownloadRequest>(req.Body ?? "{}");
                            if (dlReq == null || string.IsNullOrWhiteSpace(dlReq.url))
                            {
                                await WriteJsonResponseAsync(ns, 400, "Bad Request", "{\"ok\":false,\"error\":\"Missing url\"}", corsEchoOrigin).ConfigureAwait(false);
                                return;
                            }

                            bool shouldPrompt = dlReq.showDialog ||
                                (_controller.Settings.ShowStartDownloadDialog && !dlReq.silent && !dlReq.holdInQueue);

                            if (shouldPrompt && RequestShowAddDialog != null)
                            {
                                RequestShowAddDialog(dlReq);
                                await WriteJsonResponseAsync(ns, 200, "OK", "{\"ok\":true,\"mode\":\"dialog\"}", corsEchoOrigin).ConfigureAwait(false);
                                return;
                            }

                            DownloadItem item = CreateItemFromBrowserRequest(dlReq);
                            bool startNow = dlReq.startImmediately && !dlReq.holdInQueue;
                            DownloadItem added = _controller.AddDownload(item, startNow, dlReq.holdInQueue);

                            Dictionary<string, object> res = new Dictionary<string, object>();
                            res["ok"] = true;
                            res["id"] = added.Id;
                            res["fileName"] = added.FileName;
                            res["category"] = added.CategoryName;
                            res["status"] = added.Status.ToString();
                            await WriteJsonResponseAsync(ns, 200, "OK", ser.Serialize(res), corsEchoOrigin).ConfigureAwait(false);
                            return;
                        }

                        if (string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(req.Path, "/api/v1/batch", StringComparison.OrdinalIgnoreCase))
                        {
                            BrowserBatchRequest batch = ser.Deserialize<BrowserBatchRequest>(req.Body ?? "{}");
                            if (batch != null && batch.showBatchDialog && RequestShowBatchDialog != null)
                            {
                                RequestShowBatchDialog(batch);
                                await WriteJsonResponseAsync(ns, 200, "OK", "{\"ok\":true,\"mode\":\"batch_dialog\",\"addedCount\":0}", corsEchoOrigin).ConfigureAwait(false);
                                return;
                            }

                            int count = 0;
                            if (batch != null && batch.links != null)
                            {
                                for (int i = 0; i < batch.links.Count; i++)
                                {
                                    BrowserDownloadRequest entry = batch.links[i];
                                    if (entry == null || string.IsNullOrWhiteSpace(entry.url)) continue;
                                    if (HostExclusionMatcher.IsExcluded(entry.url, batch.referer, _controller.Settings.ExcludedHosts))
                                    {
                                        continue;
                                    }
                                    if (string.IsNullOrEmpty(entry.referer)) entry.referer = batch.referer;
                                    if (string.IsNullOrEmpty(entry.cookies)) entry.cookies = batch.cookies;
                                    if (string.IsNullOrEmpty(entry.userAgent)) entry.userAgent = batch.userAgent;

                                    DownloadItem item = CreateItemFromBrowserRequest(entry);
                                    _controller.AddDownload(item, batch.startImmediately, false);
                                    count++;
                                }
                            }

                            Dictionary<string, object> res = new Dictionary<string, object>();
                            res["ok"] = true;
                            res["addedCount"] = count;
                            await WriteJsonResponseAsync(ns, 200, "OK", ser.Serialize(res), corsEchoOrigin).ConfigureAwait(false);
                            return;
                        }

                        if (string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(req.Path, "/api/v1/refresh-url", StringComparison.OrdinalIgnoreCase))
                        {
                            BrowserRefreshUrlRequest rReq = ser.Deserialize<BrowserRefreshUrlRequest>(req.Body ?? "{}");
                            if (rReq == null || string.IsNullOrWhiteSpace(rReq.id) || string.IsNullOrWhiteSpace(rReq.url))
                            {
                                await WriteJsonResponseAsync(ns, 400, "Bad Request", "{\"ok\":false,\"error\":\"Missing id or url\"}", corsEchoOrigin).ConfigureAwait(false);
                                return;
                            }
                            bool ok = await _controller.RefreshDownloadAddressAsync(
                                rReq.id, rReq.url, rReq.referer, rReq.cookies, rReq.userAgent, rReq.resumeNow).ConfigureAwait(false);
                            await WriteJsonResponseAsync(
                                ns,
                                ok ? 200 : 404,
                                ok ? "OK" : "Not Found",
                                ok ? "{\"ok\":true}" : "{\"ok\":false,\"error\":\"Download item not found\"}",
                                corsEchoOrigin).ConfigureAwait(false);
                            return;
                        }

                        if (string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(req.Path, "/api/v1/remove", StringComparison.OrdinalIgnoreCase))
                        {
                            BrowserRemoveRequest remReq = ser.Deserialize<BrowserRemoveRequest>(req.Body ?? "{}");
                            if (remReq == null || string.IsNullOrWhiteSpace(remReq.id))
                            {
                                await WriteJsonResponseAsync(ns, 400, "Bad Request", "{\"ok\":false,\"error\":\"Missing id\"}", corsEchoOrigin).ConfigureAwait(false);
                                return;
                            }
                            await _controller.RemoveDownloadAsync(remReq.id, remReq.deleteDiskFiles).ConfigureAwait(false);
                            await WriteJsonResponseAsync(ns, 200, "OK", "{\"ok\":true}", corsEchoOrigin).ConfigureAwait(false);
                            return;
                        }

                        if (string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
                            (string.Equals(req.Path, "/api/v1/queue/start", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(req.Path, "/api/v1/queue/stop", StringComparison.OrdinalIgnoreCase)))
                        {
                            BrowserQueueCommandRequest qReq = ser.Deserialize<BrowserQueueCommandRequest>(req.Body ?? "{}");
                            string qName = (qReq != null && !string.IsNullOrWhiteSpace(qReq.queueName))
                                ? qReq.queueName.Trim()
                                : "Main Queue";
                            if (req.Path.EndsWith("/start", StringComparison.OrdinalIgnoreCase))
                            {
                                _controller.StartQueue(qName);
                            }
                            else
                            {
                                _controller.StopQueue(qName);
                            }
                            await WriteJsonResponseAsync(ns, 200, "OK", "{\"ok\":true}", corsEchoOrigin).ConfigureAwait(false);
                            return;
                        }

                        await WriteJsonResponseAsync(ns, 404, "Not Found", "{\"ok\":false,\"error\":\"Route not found\"}", corsEchoOrigin).ConfigureAwait(false);
                    }
                }
                catch
                {
                }
            }
        }

        private DownloadItem CreateItemFromBrowserRequest(BrowserDownloadRequest req)
        {
            bool explicitName = !string.IsNullOrWhiteSpace(req.fileName);
            string guessedName = explicitName
                ? CategoryClassifier.SanitizeFileName(Path.GetFileName(req.fileName))
                : CategoryClassifier.GuessFileNameFromUrl(req.url);

            if (string.IsNullOrWhiteSpace(guessedName))
            {
                guessedName = CategoryClassifier.GuessFileNameFromUrl(req.url);
                explicitName = false;
            }

            string cat = CategoryClassifier.ClassifyByFileName(guessedName);
            string saveDir = !string.IsNullOrWhiteSpace(req.saveDirectory)
                ? req.saveDirectory.Trim()
                : CategoryClassifier.ResolveSaveDirectory(_controller.Settings, cat);

            return new DownloadItem
            {
                Url = req.url.Trim(),
                FileName = guessedName,
                HasExplicitFileName = explicitName && req.silent,
                CategoryName = cat,
                SaveDirectory = saveDir,
                Referer = req.referer,
                Cookies = req.cookies,
                UserAgent = req.userAgent,
                MaxConnections = _controller.Settings.DefaultConnectionsPerDownload
            };
        }

        private sealed class ParsedHttpRequest
        {
            public string Method { get; set; }
            public string Path { get; set; }
            public string Body { get; set; }
            public Dictionary<string, string> Headers { get; set; }

            public string GetHeader(string name)
            {
                if (Headers == null || string.IsNullOrEmpty(name)) return null;
                string v;
                return Headers.TryGetValue(name, out v) ? v : null;
            }
        }

        private static async Task<ParsedHttpRequest> ReadHttpRequestAsync(NetworkStream stream)
        {
            MemoryStream ms = new MemoryStream();
            byte[] buf = new byte[4096];
            int headerEndIndex = -1;

            while (headerEndIndex < 0 && ms.Length < 65536)
            {
                int r = await stream.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false);
                if (r <= 0) break;
                ms.Write(buf, 0, r);
                headerEndIndex = FindDoubleCrlf(ms.GetBuffer(), (int)ms.Length);
            }

            if (headerEndIndex < 0) return null;

            byte[] allSoFar = ms.ToArray();
            string headerText = Encoding.ASCII.GetString(allSoFar, 0, headerEndIndex);
            string[] lines = headerText.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return null;

            string[] firstParts = lines[0].Split(' ');
            if (firstParts.Length < 2) return null;

            string method = firstParts[0].Trim();
            string rawTarget = firstParts[1].Trim();
            int qPos = rawTarget.IndexOf('?');
            string cleanPath = qPos >= 0 ? rawTarget.Substring(0, qPos) : rawTarget;

            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int contentLength = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0)
                {
                    string hName = lines[i].Substring(0, colon).Trim();
                    string hVal = lines[i].Substring(colon + 1).Trim();
                    headers[hName] = hVal;
                    if (string.Equals(hName, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(hVal, out contentLength);
                    }
                }
            }

            if (contentLength < 0 || contentLength > 16 * 1024 * 1024)
            {
                return null;
            }

            int bodyStart = headerEndIndex + 4;
            int alreadyBodyBytes = allSoFar.Length - bodyStart;
            MemoryStream bodyStream = new MemoryStream();
            if (alreadyBodyBytes > 0 && contentLength > 0)
            {
                bodyStream.Write(allSoFar, bodyStart, Math.Min(alreadyBodyBytes, contentLength));
            }

            while (bodyStream.Length < contentLength)
            {
                int needed = contentLength - (int)bodyStream.Length;
                int r = await stream.ReadAsync(buf, 0, Math.Min(buf.Length, needed)).ConfigureAwait(false);
                if (r <= 0) break;
                bodyStream.Write(buf, 0, r);
            }

            string body = contentLength > 0
                ? Encoding.UTF8.GetString(bodyStream.ToArray(), 0, Math.Min((int)bodyStream.Length, contentLength))
                : "";

            return new ParsedHttpRequest
            {
                Method = method,
                Path = cleanPath,
                Body = body,
                Headers = headers
            };
        }

        private static int FindDoubleCrlf(byte[] data, int length)
        {
            for (int i = 0; i <= length - 4; i++)
            {
                if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10)
                {
                    return i;
                }
            }
            return -1;
        }

        private static async Task WriteJsonResponseAsync(
            NetworkStream stream,
            int statusCode,
            string reason,
            string jsonBody,
            string allowedCorsOrigin)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody ?? "");
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("HTTP/1.1 {0} {1}\r\n", statusCode, reason);
            sb.Append("Content-Type: application/json; charset=utf-8\r\n");
            sb.AppendFormat("Content-Length: {0}\r\n", bodyBytes.Length);
            if (!string.IsNullOrEmpty(allowedCorsOrigin))
            {
                sb.AppendFormat("Access-Control-Allow-Origin: {0}\r\n", allowedCorsOrigin);
                sb.Append("Vary: Origin\r\n");
                sb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
                sb.Append("Access-Control-Allow-Headers: Content-Type, X-MHS-Client, X-MHS-Token\r\n");
            }
            sb.Append("Connection: close\r\n\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
            await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
            if (bodyBytes.Length > 0)
            {
                await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length).ConfigureAwait(false);
            }
            await stream.FlushAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
            Stop();
        }
    }

    public static class BrowserBridgeClient
    {
        public static bool TryPing(int port, string authToken)
        {
            try
            {
                HttpHelpers.EnsureNetworkingConfigured();
                string endpoint = string.Format("http://127.0.0.1:{0}/api/v1/ping", port > 0 ? port : 16888);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(endpoint);
                req.Method = "GET";
                req.Timeout = 1200;
                req.ReadWriteTimeout = 1200;
                req.Proxy = null;
                if (!string.IsNullOrEmpty(authToken))
                {
                    req.Headers["X-MHS-Token"] = authToken;
                }
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    return resp.StatusCode == HttpStatusCode.OK;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool TryForwardAddUrl(int port, string url, string referer, string cookies, bool showDialog)
        {
            PersistedState st = StateRepository.Load();
            BrowserDownloadRequest req = new BrowserDownloadRequest
            {
                url = url,
                referer = referer,
                cookies = cookies,
                startImmediately = !showDialog,
                showDialog = showDialog
            };
            return TryForwardDownloadRequest(port, st.Settings.BridgeAuthToken, req);
        }

        public static bool TryForwardDownloadRequest(int port, string authToken, BrowserDownloadRequest payload)
        {
            if (payload == null) return false;
            JavaScriptSerializer ser = new JavaScriptSerializer();
            return SendAuthenticatedPost(port, authToken, "/api/v1/downloads", ser.Serialize(payload));
        }

        public static bool TryForwardBatchRequest(int port, string authToken, BrowserBatchRequest payload)
        {
            if (payload == null) return false;
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 16 * 1024 * 1024;
            return SendAuthenticatedPost(port, authToken, "/api/v1/batch", ser.Serialize(payload));
        }

        public static bool TryForwardQueueCommand(int port, string authToken, string queueName, bool start)
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            BrowserQueueCommandRequest payload = new BrowserQueueCommandRequest { queueName = queueName ?? "Main Queue" };
            string route = start ? "/api/v1/queue/start" : "/api/v1/queue/stop";
            return SendAuthenticatedPost(port, authToken, route, ser.Serialize(payload));
        }

        private static bool SendAuthenticatedPost(int port, string authToken, string path, string jsonBody)
        {
            try
            {
                HttpHelpers.EnsureNetworkingConfigured();
                string endpoint = string.Format("http://127.0.0.1:{0}{1}", port > 0 ? port : 16888, path);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(endpoint);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.Timeout = 3000;
                req.ReadWriteTimeout = 3000;
                req.Proxy = null;
                req.Headers["X-MHS-Client"] = "mhs-local-ipc";
                if (!string.IsNullOrEmpty(authToken))
                {
                    req.Headers["X-MHS-Token"] = authToken;
                }

                byte[] bytes = Encoding.UTF8.GetBytes(jsonBody ?? "{}");
                req.ContentLength = bytes.Length;

                using (Stream s = req.GetRequestStream())
                {
                    s.Write(bytes, 0, bytes.Length);
                }
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    return resp.StatusCode == HttpStatusCode.OK;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    public static class NativeMessagingRegistrar
    {
        public const string HostName = "com.mhsmanager.host";
        private const string ChromeRegKey = @"Software\Google\Chrome\NativeMessagingHosts\com.mhsmanager.host";
        private const string EdgeRegKey = @"Software\Microsoft\Edge\NativeMessagingHosts\com.mhsmanager.host";

        public static string GetManifestPath()
        {
            return Path.Combine(StateRepository.GetAppDataDirectory(), HostName + ".json");
        }

        public static bool IsRegistered()
        {
            try
            {
                string manifest = GetManifestPath();
                if (!File.Exists(manifest)) return false;
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ChromeRegKey, false))
                {
                    if (key != null) return true;
                }
                using (RegistryKey key2 = Registry.CurrentUser.OpenSubKey(EdgeRegKey, false))
                {
                    if (key2 != null) return true;
                }
            }
            catch
            {
            }
            return false;
        }

        public static List<string> ParseExtensionOrigins(string extensionIdsRaw)
        {
            List<string> origins = new List<string>();
            if (string.IsNullOrWhiteSpace(extensionIdsRaw)) return origins;

            string[] parts = extensionIdsRaw.Split(
                new char[] { ',', ';', '|', ' ', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < parts.Length; i++)
            {
                string token = parts[i].Trim();
                if (string.IsNullOrEmpty(token)) continue;
                string origin;
                if (token.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
                {
                    origin = token.EndsWith("/") ? token : (token + "/");
                }
                else
                {
                    origin = "chrome-extension://" + token.Trim('/') + "/";
                }
                if (seen.Add(origin))
                {
                    origins.Add(origin);
                }
            }
            return origins;
        }

        public static string Register(string extensionIdsRaw, string executablePath, bool registerChrome, bool registerEdge)
        {
            List<string> origins = ParseExtensionOrigins(extensionIdsRaw);
            if (origins.Count == 0)
            {
                throw new ArgumentException("Please enter at least one Chromium Extension ID (32 lowercase letters from chrome://extensions).");
            }

            string manifestPath = GetManifestPath();
            if (File.Exists(manifestPath))
            {
                string backup = manifestPath + ".bak";
                File.Copy(manifestPath, backup, true);
            }

            Dictionary<string, object> manifest = new Dictionary<string, object>();
            manifest["name"] = HostName;
            manifest["description"] = "MHS Manager Native Messaging Host";
            manifest["path"] = Path.GetFullPath(executablePath);
            manifest["type"] = "stdio";
            manifest["allowed_origins"] = origins;

            JavaScriptSerializer ser = new JavaScriptSerializer();
            string json = ser.Serialize(manifest);
            File.WriteAllText(manifestPath, json, StateRepository.Utf8NoBom);

            if (registerChrome)
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(ChromeRegKey))
                {
                    if (key != null) key.SetValue("", manifestPath, RegistryValueKind.String);
                }
            }
            if (registerEdge)
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(EdgeRegKey))
                {
                    if (key != null) key.SetValue("", manifestPath, RegistryValueKind.String);
                }
            }

            return manifestPath;
        }
    }

    /// <summary>
    /// Implements Chromium/WebExtension Native Messaging protocol (4-byte little-endian length + UTF-8 JSON over stdio).
    /// Modeled after IDMMsgHost.exe's stdio contract while using the isolated com.mhsmanager.host namespace.
    /// </summary>
    public static class NativeMessagingHost
    {
        public static int RunStdioLoop(string executablePath)
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 16 * 1024 * 1024;

            try
            {
                using (Stream stdin = Console.OpenStandardInput())
                using (Stream stdout = Console.OpenStandardOutput())
                {
                    byte[] lenBuf = new byte[4];
                    while (true)
                    {
                        int lenRead = ReadExact(stdin, lenBuf, 0, 4);
                        if (lenRead == 0)
                        {
                            break; // Clean EOF
                        }
                        if (lenRead < 4)
                        {
                            break;
                        }

                        int msgLen = BitConverter.ToInt32(lenBuf, 0);
                        if (msgLen <= 0 || msgLen > 16 * 1024 * 1024)
                        {
                            break;
                        }

                        byte[] payloadBuf = new byte[msgLen];
                        int bodyRead = ReadExact(stdin, payloadBuf, 0, msgLen);
                        if (bodyRead < msgLen)
                        {
                            break;
                        }

                        string requestJson = Encoding.UTF8.GetString(payloadBuf);
                        Dictionary<string, object> response = ProcessMessage(requestJson, executablePath, ser);
                        string responseJson = ser.Serialize(response);
                        byte[] outBytes = Encoding.UTF8.GetBytes(responseJson);
                        byte[] outLen = BitConverter.GetBytes(outBytes.Length);

                        stdout.Write(outLen, 0, 4);
                        stdout.Write(outBytes, 0, outBytes.Length);
                        stdout.Flush();
                    }
                }
                return 0;
            }
            catch
            {
                return 1;
            }
        }

        private static int ReadExact(Stream stream, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int r = stream.Read(buffer, offset + total, count - total);
                if (r <= 0)
                {
                    return total;
                }
                total += r;
            }
            return total;
        }

        private static Dictionary<string, object> ProcessMessage(string requestJson, string executablePath, JavaScriptSerializer ser)
        {
            Dictionary<string, object> res = new Dictionary<string, object>();
            try
            {
                Dictionary<string, object> msg = ser.Deserialize<Dictionary<string, object>>(requestJson);
                string type = msg != null && msg.ContainsKey("type") && msg["type"] != null
                    ? msg["type"].ToString()
                    : "ping";

                PersistedState state = StateRepository.Load();
                int port = state.Settings.BrowserBridgePort > 0 ? state.Settings.BrowserBridgePort : 16888;
                string token = state.Settings.BridgeAuthToken;

                if (string.Equals(type, "ping", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type, "get_config", StringComparison.OrdinalIgnoreCase))
                {
                    bool online = BrowserBridgeClient.TryPing(port, token);
                    res["ok"] = true;
                    res["host"] = NativeMessagingRegistrar.HostName;
                    res["version"] = "1.1.0";
                    res["desktopOnline"] = online;
                    res["port"] = port;
                    res["token"] = token;
                    res["monitoredExtensions"] = state.Settings.MonitoredExtensions ?? "";
                    res["excludedHosts"] = state.Settings.ExcludedHosts ?? "";
                    return res;
                }

                if (string.Equals(type, "download", StringComparison.OrdinalIgnoreCase))
                {
                    if (!EnsureDesktopAppRunning(executablePath, port, token))
                    {
                        res["ok"] = false;
                        res["error"] = "Failed to start MHS Manager desktop application.";
                        return res;
                    }
                    // Reload in case newly launched desktop app initialized state
                    state = StateRepository.Load();
                    port = state.Settings.BrowserBridgePort;
                    token = state.Settings.BridgeAuthToken;

                    BrowserDownloadRequest dlReq = ser.Deserialize<BrowserDownloadRequest>(requestJson);
                    bool ok = BrowserBridgeClient.TryForwardDownloadRequest(port, token, dlReq);
                    res["ok"] = ok;
                    return res;
                }

                if (string.Equals(type, "batch", StringComparison.OrdinalIgnoreCase))
                {
                    if (!EnsureDesktopAppRunning(executablePath, port, token))
                    {
                        res["ok"] = false;
                        res["error"] = "Failed to start MHS Manager desktop application.";
                        return res;
                    }
                    state = StateRepository.Load();
                    port = state.Settings.BrowserBridgePort;
                    token = state.Settings.BridgeAuthToken;

                    BrowserBatchRequest batchReq = ser.Deserialize<BrowserBatchRequest>(requestJson);
                    bool ok = BrowserBridgeClient.TryForwardBatchRequest(port, token, batchReq);
                    res["ok"] = ok;
                    return res;
                }

                if (string.Equals(type, "start_queue", StringComparison.OrdinalIgnoreCase))
                {
                    if (!EnsureDesktopAppRunning(executablePath, port, token))
                    {
                        res["ok"] = false;
                        return res;
                    }
                    res["ok"] = BrowserBridgeClient.TryForwardQueueCommand(port, token, "Main Queue", true);
                    return res;
                }

                res["ok"] = false;
                res["error"] = "Unknown native message type: " + type;
                return res;
            }
            catch (Exception ex)
            {
                res["ok"] = false;
                res["error"] = ex.Message;
                return res;
            }
        }

        private static bool EnsureDesktopAppRunning(string executablePath, int port, string token)
        {
            if (BrowserBridgeClient.TryPing(port, token))
            {
                return true;
            }

            try
            {
                if (!string.IsNullOrEmpty(executablePath) && File.Exists(executablePath))
                {
                    ProcessStartInfo psi = new ProcessStartInfo(executablePath);
                    psi.UseShellExecute = false;
                    string customAppData = StateRepository.CustomAppDataDirectory;
                    if (!string.IsNullOrWhiteSpace(customAppData))
                    {
                        psi.Arguments = "--appdata-dir \"" + customAppData + "\"";
                    }
                    Process.Start(psi);

                    for (int i = 0; i < 25; i++)
                    {
                        Thread.Sleep(150);
                        PersistedState latest = StateRepository.Load();
                        if (BrowserBridgeClient.TryPing(latest.Settings.BrowserBridgePort, latest.Settings.BridgeAuthToken))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
