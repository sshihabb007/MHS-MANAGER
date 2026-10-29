using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("MHS Manager")]
[assembly: AssemblyDescription("Open-Source Multi-Connection Download Manager & Stream Merger")]
[assembly: AssemblyCompany("MHS Open Source Project")]
[assembly: AssemblyProduct("MHS Manager")]
[assembly: AssemblyCopyright("Copyright (c) 2026 MHS Open Source Contributors (MIT License)")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace MHSManager
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            HttpHelpers.EnsureNetworkingConfigured();

            if (args != null && args.Length > 0 && HasArg(args, "--cli-download"))
            {
                return RunCliDownloadMode(args);
            }

            string startupAddUrl = GetArgValue(args, "--add-url");
            string startupReferer = GetArgValue(args, "--referer");
            string startupCookies = GetArgValue(args, "--cookies");
            int autoExitMs = 0;
            string autoExitRaw = GetArgValue(args, "--auto-exit-ms");
            if (!string.IsNullOrEmpty(autoExitRaw))
            {
                int.TryParse(autoExitRaw, out autoExitMs);
            }

            PersistedState state = StateRepository.Load();

            // Forward --add-url to an already-running MHS Manager GUI instance if active
            bool createdNew;
            using (Mutex singleInstanceMutex = new Mutex(true, @"Local\MHSManager_SingleInstance_v1", out createdNew))
            {
                if (!createdNew)
                {
                    if (!string.IsNullOrWhiteSpace(startupAddUrl))
                    {
                        BrowserBridgeClient.TryForwardAddUrl(
                            state.Settings.BrowserBridgePort,
                            startupAddUrl,
                            startupReferer,
                            startupCookies,
                            true);
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

                    MainForm form = new MainForm(
                        controller,
                        bridge,
                        startupAddUrl,
                        startupReferer,
                        startupCookies,
                        autoExitMs);

                    Application.Run(form);
                }
            }

            return 0;
        }

        private static int RunCliDownloadMode(string[] args)
        {
            string url = GetArgValue(args, "--cli-download");
            string outPath = GetArgValue(args, "--out");
            string connsRaw = GetArgValue(args, "--connections");
            string speedKbRaw = GetArgValue(args, "--speed-limit-kbps");
            string expectedSha = GetArgValue(args, "--sha256");
            string referer = GetArgValue(args, "--referer");
            string cookies = GetArgValue(args, "--cookies");
            string reportJsonPath = GetArgValue(args, "--report-json");
            string pauseAfterMsRaw = GetArgValue(args, "--pause-after-ms");

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(outPath))
            {
                TryWriteStdOut("Usage: MHSManager.exe --cli-download <url> --out <filepath> [--connections 8] [--speed-limit-kbps 0] [--sha256 <hex>] [--report-json <path>]");
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

            string fullOut = Path.GetFullPath(outPath);
            string dir = Path.GetDirectoryName(fullOut);
            string fileName = Path.GetFileName(fullOut);

            AppSettings settings = new AppSettings();
            settings.DefaultConnectionsPerDownload = connections;

            DownloadItem item = new DownloadItem
            {
                Url = url,
                FileName = fileName,
                SaveDirectory = dir,
                CategoryName = CategoryClassifier.ClassifyByFileName(fileName),
                MaxConnections = connections,
                MaxSpeedBytesPerSec = speedCapBps,
                ExpectedSha256 = expectedSha,
                Referer = referer,
                Cookies = cookies
            };

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

            int dynamicSplits = 0;
            if (item.RecentLogs != null)
            {
                for (int i = 0; i < item.RecentLogs.Count; i++)
                {
                    if (item.RecentLogs[i].IndexOf("Dynamic split:", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        dynamicSplits++;
                    }
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
                report["segmentCount"] = item.Segments != null ? item.Segments.Count : 0;
                report["dynamicSplits"] = dynamicSplits;
                report["computedSha256"] = item.ComputedSha256 ?? "";
                report["errorMessage"] = item.ErrorMessage ?? "";
                report["elapsedMs"] = sw.ElapsedMilliseconds;
                report["logs"] = item.RecentLogs;

                JavaScriptSerializer ser = new JavaScriptSerializer();
                File.WriteAllText(reportJsonPath, ser.Serialize(report));
            }

            TryWriteStdOut(string.Format(
                "Status={0} Bytes={1}/{2} Segments={3} Splits={4} SHA256={5} Error={6}",
                item.Status,
                item.DownloadedBytes,
                item.TotalBytes,
                item.Segments != null ? item.Segments.Count : 0,
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

