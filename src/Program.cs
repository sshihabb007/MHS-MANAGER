using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("MHS Manager")]
[assembly: AssemblyDescription("Open-Source Multi-Connection Download Manager & Stream Merger")]
[assembly: AssemblyCompany("MHS Open Source Project")]
[assembly: AssemblyProduct("MHS Manager")]
[assembly: AssemblyCopyright("Copyright (c) 2026 MHS Open Source Contributors (MIT License)")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace MHSManager
{
    public sealed class StartupLaunchOptions
    {
        public string AddUrl { get; set; }
        public string SaveDirectory { get; set; }
        public string ExplicitFileName { get; set; }
        public string Referer { get; set; }
        public string Cookies { get; set; }
        public bool SilentMode { get; set; }
        public bool HoldInQueue { get; set; }
        public bool StartQueueNow { get; set; }
        public bool ExitOnComplete { get; set; }
        public bool StopQueueOnComplete { get; set; }
        public int AutoExitMs { get; set; }
    }

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            HttpHelpers.EnsureNetworkingConfigured();

            string customAppData = GetArgValue(args, "--appdata-dir");
            if (!string.IsNullOrWhiteSpace(customAppData))
            {
                StateRepository.CustomAppDataDirectory = customAppData;
            }

            if (IsNativeMessagingInvocation(args))
            {
                return NativeMessagingHost.RunStdioLoop(Application.ExecutablePath);
            }

            if (args != null && args.Length > 0 && HasArg(args, "--register-native-host"))
            {
                string extId = GetArgValue(args, "--register-native-host");
                try
                {
                    string manifestPath = NativeMessagingRegistrar.Register(extId, Application.ExecutablePath, true, true);
                    TryWriteStdOut("Registered Native Messaging Host: " + manifestPath);
                    return 0;
                }
                catch (Exception ex)
                {
                    TryWriteStdOut("ERROR: " + ex.Message);
                    return 1;
                }
            }

            if (args != null && args.Length > 0 && HasArg(args, "--cli-download"))
            {
                return RunCliDownloadMode(args);
            }

            StartupLaunchOptions launch = ParseGuiAndIdmArguments(args);
            PersistedState state = StateRepository.Load();

            string mutexName = BuildScopedMutexName();
            bool createdNew;
            using (Mutex singleInstanceMutex = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew)
                {
                    int port = state.Settings.BrowserBridgePort;
                    string token = state.Settings.BridgeAuthToken;

                    if (launch.StartQueueNow)
                    {
                        BrowserBridgeClient.TryForwardQueueCommand(port, token, "Main Queue", true);
                    }

                    if (!string.IsNullOrWhiteSpace(launch.AddUrl))
                    {
                        BrowserDownloadRequest forwardReq = new BrowserDownloadRequest
                        {
                            url = launch.AddUrl,
                            fileName = launch.ExplicitFileName,
                            saveDirectory = launch.SaveDirectory,
                            referer = launch.Referer,
                            cookies = launch.Cookies,
                            showDialog = !launch.SilentMode && !launch.HoldInQueue,
                            startImmediately = !launch.HoldInQueue && launch.SilentMode,
                            holdInQueue = launch.HoldInQueue,
                            silent = launch.SilentMode || launch.HoldInQueue
                        };
                        BrowserBridgeClient.TryForwardDownloadRequest(port, token, forwardReq);
                    }
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (DownloadManagerController controller = new DownloadManagerController(state))
                using (BrowserBridgeServer bridge = new BrowserBridgeServer(controller))
                {
                    if (state.Settings.EnableBrowserBridge)
                    {
                        bridge.Start(state.Settings.BrowserBridgePort);
                    }

                    MainForm form = new MainForm(controller, bridge, launch);
                    Application.Run(form);
                }
            }

            return 0;
        }

        private static string BuildScopedMutexName()
        {
            string appData = StateRepository.GetAppDataDirectory().ToLowerInvariant();
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(appData));
                string suffix = HttpHelpers.BytesToHex(hash).Substring(0, 16);
                return @"Local\MHSManager_SingleInstance_" + suffix;
            }
        }

        private static bool IsNativeMessagingInvocation(string[] args)
        {
            if (args == null || args.Length == 0) return false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] ?? "";
                if (string.Equals(a, "--native-messaging-host", StringComparison.OrdinalIgnoreCase) ||
                    a.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
                    a.StartsWith("moz-extension://", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Parses both MHS flags (--add-url, --referer, --cookies, --auto-exit-ms) and standard IDM switches:
        /// /d URL [/p local_path] [/f local_filename] [/q] [/h] [/n] [/a] [/s]
        /// </summary>
        private static StartupLaunchOptions ParseGuiAndIdmArguments(string[] args)
        {
            StartupLaunchOptions opt = new StartupLaunchOptions();
            if (args == null || args.Length == 0) return opt;

            opt.AddUrl = GetArgValue(args, "--add-url");
            opt.Referer = GetArgValue(args, "--referer");
            opt.Cookies = GetArgValue(args, "--cookies");

            string autoExitRaw = GetArgValue(args, "--auto-exit-ms");
            if (!string.IsNullOrEmpty(autoExitRaw))
            {
                int parsedMs;
                if (int.TryParse(autoExitRaw, out parsedMs))
                {
                    opt.AutoExitMs = parsedMs;
                }
            }

            for (int i = 0; i < args.Length; i++)
            {
                string a = (args[i] ?? "").Trim();
                if (string.Equals(a, "/d", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "-d", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length) { opt.AddUrl = args[++i]; }
                }
                else if (string.Equals(a, "/p", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "-p", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length) { opt.SaveDirectory = args[++i]; }
                }
                else if (string.Equals(a, "/f", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "-f", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length) { opt.ExplicitFileName = CategoryClassifier.SanitizeFileName(Path.GetFileName(args[++i])); }
                }
                else if (string.Equals(a, "/n", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "-n", StringComparison.OrdinalIgnoreCase))
                {
                    opt.SilentMode = true;
                }
                else if (string.Equals(a, "/a", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "-a", StringComparison.OrdinalIgnoreCase))
                {
                    opt.HoldInQueue = true;
                    opt.SilentMode = true;
                }
                else if (string.Equals(a, "/q", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "-q", StringComparison.OrdinalIgnoreCase))
                {
                    opt.ExitOnComplete = true;
                }
                else if (string.Equals(a, "/h", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase))
                {
                    opt.StopQueueOnComplete = true;
                }
                else if (string.Equals(a, "/s", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "-s", StringComparison.OrdinalIgnoreCase))
                {
                    opt.StartQueueNow = true;
                }
            }

            return opt;
        }

        private static int RunCliDownloadMode(string[] args)
        {
            string url = GetArgValue(args, "--cli-download");
            string outPath = GetArgValue(args, "--out");
            string outDirOnly = GetArgValue(args, "--out-dir");
            string connsRaw = GetArgValue(args, "--connections");
            string speedKbRaw = GetArgValue(args, "--speed-limit-kbps");
            string expectedSha = GetArgValue(args, "--sha256");
            string referer = GetArgValue(args, "--referer");
            string cookies = GetArgValue(args, "--cookies");
            string reportJsonPath = GetArgValue(args, "--report-json");
            string pauseAfterMsRaw = GetArgValue(args, "--pause-after-ms");
            bool autoNameMode = HasArg(args, "--auto-filename") || (!string.IsNullOrWhiteSpace(outDirOnly) && string.IsNullOrWhiteSpace(outPath));

            if (string.IsNullOrWhiteSpace(url) || (string.IsNullOrWhiteSpace(outPath) && string.IsNullOrWhiteSpace(outDirOnly)))
            {
                TryWriteStdOut("Usage: MHSManager.exe --cli-download <url> (--out <filepath> | --out-dir <folder>) [--connections 8] [--speed-limit-kbps 0] [--sha256 <hex>] [--report-json <path>]");
                return 2;
            }

            int connections = 8;
            if (!string.IsNullOrEmpty(connsRaw))
            {
                int parsedConns;
                if (int.TryParse(connsRaw, out parsedConns) && parsedConns >= 1 && parsedConns <= 32)
                {
                    connections = parsedConns;
                }
            }

            long speedCapBps = 0;
            if (!string.IsNullOrEmpty(speedKbRaw))
            {
                long parsedKb;
                if (long.TryParse(speedKbRaw, out parsedKb) && parsedKb > 0)
                {
                    speedCapBps = parsedKb * 1024L;
                }
            }

            int pauseAfterMs = 0;
            if (!string.IsNullOrEmpty(pauseAfterMsRaw))
            {
                int.TryParse(pauseAfterMsRaw, out pauseAfterMs);
            }

            string dir;
            string fileName;
            bool explicitName;
            bool overwriteExisting;

            if (!string.IsNullOrWhiteSpace(outPath) && !autoNameMode)
            {
                string fullOut = Path.GetFullPath(outPath);
                dir = Path.GetDirectoryName(fullOut);
                fileName = Path.GetFileName(fullOut);
                explicitName = true;
                overwriteExisting = true;
            }
            else
            {
                dir = Path.GetFullPath(!string.IsNullOrWhiteSpace(outDirOnly) ? outDirOnly : Path.GetDirectoryName(Path.GetFullPath(outPath)));
                fileName = CategoryClassifier.GuessFileNameFromUrl(url);
                explicitName = false;
                overwriteExisting = false;
            }

            AppSettings settings = StateRepository.Load().Settings;
            settings.DefaultConnectionsPerDownload = connections;

            DownloadItem item = new DownloadItem
            {
                Url = url,
                FileName = fileName,
                HasExplicitFileName = explicitName,
                OverwriteExisting = overwriteExisting,
                SaveDirectory = dir,
                CategoryName = CategoryClassifier.ClassifyByFileName(fileName),
                MaxConnections = connections,
                MaxSpeedBytesPerSec = speedCapBps,
                ExpectedSha256 = expectedSha,
                Referer = referer,
                Cookies = cookies
            };

            if (!overwriteExisting)
            {
                item.FileName = DownloadEngine.EnsureUniqueDiskFileName(dir, item.FileName);
            }

            TokenBucketLimiter globalLimiter = new TokenBucketLimiter(false, 0);
            ManualResetEventSlim doneEvent = new ManualResetEventSlim(false);
            Stopwatch sw = Stopwatch.StartNew();

            DownloadTaskRunner runner = new DownloadTaskRunner(
                item,
                settings,
                globalLimiter,
                null,
                delegate { doneEvent.Set(); });

            runner.StartAsync();

            if (pauseAfterMs > 0)
            {
                Thread.Sleep(pauseAfterMs);
                runner.RequestPause();
            }

            doneEvent.Wait();
            sw.Stop();

            string[] logsSnap = item.GetLogsSnapshot();
            List<SegmentInfo> segSnap = item.GetSegmentsSnapshot();

            int dynamicSplits = 0;
            for (int i = 0; i < logsSnap.Length; i++)
            {
                if (logsSnap[i].IndexOf("Dynamic split:", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    dynamicSplits++;
                }
            }

            if (!string.IsNullOrWhiteSpace(reportJsonPath))
            {
                Dictionary<string, object> report = new Dictionary<string, object>();
                report["status"] = item.Status.ToString();
                report["fileName"] = item.FileName;
                report["fullPath"] = item.FullTargetPath;
                report["totalBytes"] = item.TotalBytes;
                report["downloadedBytes"] = item.DownloadedBytes;
                report["supportsRanges"] = item.SupportsRanges;
                report["isHlsStream"] = item.IsHlsStream;
                report["segmentCount"] = segSnap.Count;
                report["dynamicSplits"] = dynamicSplits;
                report["computedSha256"] = item.ComputedSha256 ?? "";
                report["errorMessage"] = item.ErrorMessage ?? "";
                report["elapsedMs"] = sw.ElapsedMilliseconds;
                report["logs"] = logsSnap;

                JavaScriptSerializer ser = new JavaScriptSerializer();
                File.WriteAllText(reportJsonPath, ser.Serialize(report), StateRepository.Utf8NoBom);
            }

            TryWriteStdOut(string.Format(
                "Status={0} Bytes={1}/{2} Segments={3} Splits={4} SHA256={5} Error={6}",
                item.Status,
                item.DownloadedBytes,
                item.TotalBytes,
                segSnap.Count,
                dynamicSplits,
                item.ComputedSha256 ?? "",
                item.ErrorMessage ?? "none"));

            if (pauseAfterMs > 0 && item.Status == DownloadStatus.Paused)
            {
                return 0;
            }
            return item.Status == DownloadStatus.Completed ? 0 : 1;
        }

        private static void TryWriteStdOut(string line)
        {
            try { Console.WriteLine(line); } catch { }
        }

        private static bool HasArg(string[] args, string flag)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string GetArgValue(string[] args, string key)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
