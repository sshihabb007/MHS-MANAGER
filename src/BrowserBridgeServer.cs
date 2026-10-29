using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MHSManager
{
    public sealed class BrowserDownloadRequest
    {
        public string url { get; set; }
        public string fileName { get; set; }
        public string referer { get; set; }
        public string cookies { get; set; }
        public string userAgent { get; set; }
        public bool startImmediately { get; set; }
        public bool showDialog { get; set; }

        public BrowserDownloadRequest()
        {
            startImmediately = true;
            showDialog = false;
        }
    }

    public sealed class BrowserBatchRequest
    {
        public List<BrowserDownloadRequest> links { get; set; }
        public string referer { get; set; }
        public string cookies { get; set; }
        public string userAgent { get; set; }
        public bool startImmediately { get; set; }
    }

    public sealed class BrowserBridgeServer : IDisposable
    {
        private readonly DownloadManagerController _controller;
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private int _port;
        private bool _running;

        public event Action<BrowserDownloadRequest> RequestShowAddDialog;

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

                        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                        {
                            await WriteJsonResponseAsync(ns, 204, "No Content", "").ConfigureAwait(false);
                            return;
                        }

                        JavaScriptSerializer ser = new JavaScriptSerializer();
                        ser.MaxJsonLength = 16 * 1024 * 1024;

                        if (string.Equals(req.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(req.Path, "/api/v1/ping", StringComparison.OrdinalIgnoreCase))
                        {
                            Dictionary<string, object> payload = new Dictionary<string, object>();
                            payload["ok"] = true;
                            payload["app"] = "MHS Manager";
                            payload["version"] = "1.0.0";
                            payload["port"] = _port;
                            payload["monitoredExtensions"] = _controller.Settings.MonitoredExtensions;
                            await WriteJsonResponseAsync(ns, 200, "OK", ser.Serialize(payload)).ConfigureAwait(false);
                            return;
                        }

                        if (string.Equals(req.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(req.Path, "/api/v1/status", StringComparison.OrdinalIgnoreCase))
                        {
                            List<DownloadItem> snap = _controller.GetDownloadsSnapshot();
                            Dictionary<string, object> payload = new Dictionary<string, object>();
                            payload["ok"] = true;
                            payload["activeCount"] = _controller.ActiveDownloadCount;
                            payload["totalCount"] = snap.Count;
                            payload["totalSpeedBps"] = (long)_controller.TotalSpeedBytesPerSec;
                            await WriteJsonResponseAsync(ns, 200, "OK", ser.Serialize(payload)).ConfigureAwait(false);
                            return;
                        }

                        if (string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(req.Path, "/api/v1/downloads", StringComparison.OrdinalIgnoreCase))
                        {
                            BrowserDownloadRequest dlReq = ser.Deserialize<BrowserDownloadRequest>(req.Body ?? "{}");
                            if (dlReq == null || string.IsNullOrWhiteSpace(dlReq.url))
                            {
                                await WriteJsonResponseAsync(ns, 400, "Bad Request", "{\"ok\":false,\"error\":\"Missing url\"}").ConfigureAwait(false);
                                return;
                            }

                            if (dlReq.showDialog && RequestShowAddDialog != null)
                            {
                                RequestShowAddDialog(dlReq);
                                await WriteJsonResponseAsync(ns, 200, "OK", "{\"ok\":true,\"mode\":\"dialog\"}").ConfigureAwait(false);
                                return;
                            }

                            DownloadItem item = CreateItemFromBrowserRequest(dlReq);
                            DownloadItem added = _controller.AddDownload(item, dlReq.startImmediately);

                            Dictionary<string, object> res = new Dictionary<string, object>();
                            res["ok"] = true;
                            res["id"] = added.Id;
                            res["fileName"] = added.FileName;
                            res["category"] = added.CategoryName;
                            await WriteJsonResponseAsync(ns, 200, "OK", ser.Serialize(res)).ConfigureAwait(false);
                            return;
                        }

                        if (string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(req.Path, "/api/v1/batch", StringComparison.OrdinalIgnoreCase))
                        {
                            BrowserBatchRequest batch = ser.Deserialize<BrowserBatchRequest>(req.Body ?? "{}");
                            int count = 0;
                            if (batch != null && batch.links != null)
                            {
                                for (int i = 0; i < batch.links.Count; i++)
                                {
                                    BrowserDownloadRequest entry = batch.links[i];
                                    if (entry == null || string.IsNullOrWhiteSpace(entry.url)) continue;
                                    if (string.IsNullOrEmpty(entry.referer)) entry.referer = batch.referer;
                                    if (string.IsNullOrEmpty(entry.cookies)) entry.cookies = batch.cookies;
                                    if (string.IsNullOrEmpty(entry.userAgent)) entry.userAgent = batch.userAgent;

                                    DownloadItem item = CreateItemFromBrowserRequest(entry);
                                    _controller.AddDownload(item, batch.startImmediately);
                                    count++;
                                }
                            }

                            Dictionary<string, object> res = new Dictionary<string, object>();
                            res["ok"] = true;
                            res["addedCount"] = count;
                            await WriteJsonResponseAsync(ns, 200, "OK", ser.Serialize(res)).ConfigureAwait(false);
                            return;
                        }

                        await WriteJsonResponseAsync(ns, 404, "Not Found", "{\"ok\":false,\"error\":\"Route not found\"}").ConfigureAwait(false);
                    }
                }
                catch
                {
                }
            }
        }

        private DownloadItem CreateItemFromBrowserRequest(BrowserDownloadRequest req)
        {
            string guessedName = !string.IsNullOrWhiteSpace(req.fileName)
                ? CategoryClassifier.SanitizeFileName(Path.GetFileName(req.fileName))
                : CategoryClassifier.GuessFileNameFromUrl(req.url);

            if (string.IsNullOrWhiteSpace(guessedName))
            {
                guessedName = CategoryClassifier.GuessFileNameFromUrl(req.url);
            }

            string cat = CategoryClassifier.ClassifyByFileName(guessedName);
            return new DownloadItem
            {
                Url = req.url.Trim(),
                FileName = guessedName,
                CategoryName = cat,
                SaveDirectory = CategoryClassifier.ResolveSaveDirectory(_controller.Settings, cat),
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

            int contentLength = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0)
                {
                    string hName = lines[i].Substring(0, colon).Trim();
                    string hVal = lines[i].Substring(colon + 1).Trim();
                    if (string.Equals(hName, "Content-Length", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(hVal, out contentLength);
                    }
                }
            }

            int bodyStart = headerEndIndex + 4;
            int alreadyBodyBytes = allSoFar.Length - bodyStart;
            MemoryStream bodyStream = new MemoryStream();
            if (alreadyBodyBytes > 0)
            {
                bodyStream.Write(allSoFar, bodyStart, Math.Min(alreadyBodyBytes, Math.Max(alreadyBodyBytes, contentLength)));
            }

            while (bodyStream.Length < contentLength && contentLength <= 16 * 1024 * 1024)
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
                Body = body
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

        private static async Task WriteJsonResponseAsync(NetworkStream stream, int statusCode, string reason, string jsonBody)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(jsonBody ?? "");
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("HTTP/1.1 {0} {1}\r\n", statusCode, reason);
            sb.Append("Content-Type: application/json; charset=utf-8\r\n");
            sb.AppendFormat("Content-Length: {0}\r\n", bodyBytes.Length);
            sb.Append("Access-Control-Allow-Origin: *\r\n");
            sb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
            sb.Append("Access-Control-Allow-Headers: Content-Type, X-MHS-Client\r\n");
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
        public static bool TryForwardAddUrl(int port, string url, string referer, string cookies, bool showDialog)
        {
            try
            {
                HttpHelpers.EnsureNetworkingConfigured();
                string endpoint = string.Format("http://127.0.0.1:{0}/api/v1/downloads", port > 0 ? port : 16888);
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(endpoint);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.Timeout = 2000;
                req.ReadWriteTimeout = 2000;

                BrowserDownloadRequest payload = new BrowserDownloadRequest
                {
                    url = url,
                    referer = referer,
                    cookies = cookies,
                    startImmediately = !showDialog,
                    showDialog = showDialog
                };
                JavaScriptSerializer ser = new JavaScriptSerializer();
                byte[] bytes = Encoding.UTF8.GetBytes(ser.Serialize(payload));
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
}
