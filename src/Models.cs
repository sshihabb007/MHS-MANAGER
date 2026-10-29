using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MHSManager
{
    public enum DownloadStatus
    {
        Queued = 0,
        Probing = 1,
        Downloading = 2,
        Merging = 3,
        Paused = 4,
        Completed = 5,
        Error = 6
    }

    public enum DownloadCategory
    {
        General = 0,
        Compressed = 1,
        Documents = 2,
        Music = 3,
        Programs = 4,
        Video = 5
    }

    public enum CompletionAction
    {
        None = 0,
        OpenFile = 1,
        OpenFolder = 2
    }

    public enum ProxyMode
    {
        System = 0,
        None = 1,
        Manual = 2
    }

    public enum QueuePostAction
    {
        None = 0,
        ExitApp = 1,
        Sleep = 2,
        Hibernate = 3,
        Shutdown = 4
    }

    public class SiteLoginEntry
    {
        public string HostPrefix { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }

        public SiteLoginEntry Clone()
        {
            return new SiteLoginEntry
            {
                HostPrefix = HostPrefix,
                UserName = UserName,
                Password = Password
            };
        }
    }

    public class SegmentInfo
    {
        public int Id { get; set; }
        public long StartByte { get; set; }
        public long CurrentByte { get; set; }
        public long EndByte { get; set; }
        public string State { get; set; }
        public double SpeedBytesPerSec { get; set; }

        public SegmentInfo()
        {
            State = "Idle";
        }

        [ScriptIgnore]
        public long TotalBytes
        {
            get { return EndByte >= StartByte ? (EndByte - StartByte + 1) : 0; }
        }

        [ScriptIgnore]
        public long DownloadedBytes
        {
            get
            {
                long done = CurrentByte - StartByte;
                return done < 0 ? 0 : done;
            }
        }

        [ScriptIgnore]
        public long RemainingBytes
        {
            get
            {
                long rem = EndByte - CurrentByte + 1;
                return rem < 0 ? 0 : rem;
            }
        }

        public SegmentInfo Clone()
        {
            return new SegmentInfo
            {
                Id = Id,
                StartByte = StartByte,
                CurrentByte = CurrentByte,
                EndByte = EndByte,
                State = State,
                SpeedBytesPerSec = SpeedBytesPerSec
            };
        }
    }

    public class DownloadItem
    {
        [ScriptIgnore]
        public readonly object SyncRoot = new object();

        public string Id { get; set; }
        public string Url { get; set; }
        public string FileName { get; set; }
        public string SaveDirectory { get; set; }
        public string CategoryName { get; set; }
        public string QueueName { get; set; }
        public string Description { get; set; }
        public string Referer { get; set; }
        public string Cookies { get; set; }
        public string UserAgent { get; set; }
        public string AuthUser { get; set; }
        public string AuthPassword { get; set; }
        public int MaxConnections { get; set; }
        public long TotalBytes { get; set; }
        public long DownloadedBytes { get; set; }
        public bool SupportsRanges { get; set; }
        public bool IsHlsStream { get; set; }
        public bool HasExplicitFileName { get; set; }
        public bool OverwriteExisting { get; set; }
        public bool IgnoreServerModifiedOnResume { get; set; }
        public string ETag { get; set; }
        public string LastModified { get; set; }
        public DownloadStatus Status { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime AddedUtc { get; set; }
        public DateTime? LastTryUtc { get; set; }
        public DateTime? NextRetryUtc { get; set; }
        public int RetryCount { get; set; }
        public DateTime? CompletedUtc { get; set; }
        public long MaxSpeedBytesPerSec { get; set; }
        public CompletionAction OnComplete { get; set; }
        public string ExpectedSha256 { get; set; }
        public string ComputedSha256 { get; set; }
        public List<SegmentInfo> Segments { get; set; }

        [ScriptIgnore]
        public double CurrentSpeedBytesPerSec { get; set; }

        [ScriptIgnore]
        public int ActiveConnections { get; set; }

        [ScriptIgnore]
        public List<string> RecentLogs { get; set; }

        public DownloadItem()
        {
            Id = Guid.NewGuid().ToString("N");
            CategoryName = "General";
            QueueName = "Main Queue";
            Description = "";
            MaxConnections = 8;
            Status = DownloadStatus.Queued;
            AddedUtc = DateTime.UtcNow;
            OnComplete = CompletionAction.None;
            Segments = new List<SegmentInfo>();
            RecentLogs = new List<string>();
        }

        [ScriptIgnore]
        public string FullTargetPath
        {
            get { return Path.Combine(SaveDirectory ?? "", FileName ?? "download.bin"); }
        }

        [ScriptIgnore]
        public string PartFilePath
        {
            get { return FullTargetPath + ".mhspart"; }
        }

        [ScriptIgnore]
        public string MetaFilePath
        {
            get { return FullTargetPath + ".mhsmeta"; }
        }

        [ScriptIgnore]
        public double ProgressPercent
        {
            get
            {
                if (Status == DownloadStatus.Completed) return 100.0;
                if (TotalBytes <= 0) return 0.0;
                double pct = (DownloadedBytes * 100.0) / TotalBytes;
                if (pct < 0.0) return 0.0;
                if (pct > 100.0) return 100.0;
                return pct;
            }
        }

        public void AddLog(string message)
        {
            lock (SyncRoot)
            {
                if (RecentLogs == null)
                {
                    RecentLogs = new List<string>();
                }
                string line = string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss"), message);
                RecentLogs.Add(line);
                if (RecentLogs.Count > 250)
                {
                    RecentLogs.RemoveAt(0);
                }
            }
        }

        public string[] GetLogsSnapshot()
        {
            lock (SyncRoot)
            {
                if (RecentLogs == null || RecentLogs.Count == 0)
                {
                    return new string[0];
                }
                return RecentLogs.ToArray();
            }
        }

        public List<SegmentInfo> GetSegmentsSnapshot()
        {
            lock (SyncRoot)
            {
                List<SegmentInfo> copy = new List<SegmentInfo>();
                if (Segments != null)
                {
                    for (int i = 0; i < Segments.Count; i++)
                    {
                        SegmentInfo seg = Segments[i];
                        if (seg != null)
                        {
                            copy.Add(seg.Clone());
                        }
                    }
                }
                return copy;
            }
        }

        public DownloadItem CloneForPersistence()
        {
            lock (SyncRoot)
            {
                List<SegmentInfo> segCopy = new List<SegmentInfo>();
                if (Segments != null)
                {
                    for (int i = 0; i < Segments.Count; i++)
                    {
                        if (Segments[i] != null)
                        {
                            SegmentInfo sc = Segments[i].Clone();
                            sc.SpeedBytesPerSec = 0;
                            segCopy.Add(sc);
                        }
                    }
                }

                return new DownloadItem
                {
                    Id = Id,
                    Url = Url,
                    FileName = FileName,
                    SaveDirectory = SaveDirectory,
                    CategoryName = CategoryName,
                    QueueName = QueueName,
                    Description = Description,
                    Referer = Referer,
                    Cookies = Cookies,
                    UserAgent = UserAgent,
                    AuthUser = AuthUser,
                    AuthPassword = AuthPassword,
                    MaxConnections = MaxConnections,
                    TotalBytes = TotalBytes,
                    DownloadedBytes = DownloadedBytes,
                    SupportsRanges = SupportsRanges,
                    IsHlsStream = IsHlsStream,
                    HasExplicitFileName = HasExplicitFileName,
                    OverwriteExisting = OverwriteExisting,
                    IgnoreServerModifiedOnResume = IgnoreServerModifiedOnResume,
                    ETag = ETag,
                    LastModified = LastModified,
                    Status = Status,
                    ErrorMessage = ErrorMessage,
                    AddedUtc = AddedUtc,
                    LastTryUtc = LastTryUtc,
                    NextRetryUtc = NextRetryUtc,
                    RetryCount = RetryCount,
                    CompletedUtc = CompletedUtc,
                    MaxSpeedBytesPerSec = MaxSpeedBytesPerSec,
                    OnComplete = OnComplete,
                    ExpectedSha256 = ExpectedSha256,
                    ComputedSha256 = ComputedSha256,
                    Segments = segCopy
                };
            }
        }
    }

    public class QueueDefinition
    {
        public string Name { get; set; }
        public int MaxConcurrentDownloads { get; set; }
        public int MaxRetriesPerItem { get; set; }
        public int RetryDelaySeconds { get; set; }
        public bool EnableScheduleStart { get; set; }
        public int StartHour { get; set; }
        public int StartMinute { get; set; }
        public bool EnableScheduleStop { get; set; }
        public int StopHour { get; set; }
        public int StopMinute { get; set; }
        public int DaysOfWeekMask { get; set; }
        public QueuePostAction PostCompletionAction { get; set; }
        public string LastAutoStartDayKey { get; set; }
        public string LastAutoStopDayKey { get; set; }
        public bool IsRunning { get; set; }

        public QueueDefinition()
        {
            Name = "Main Queue";
            MaxConcurrentDownloads = 4;
            MaxRetriesPerItem = 5;
            RetryDelaySeconds = 30;
            StartHour = 1;
            StartMinute = 0;
            StopHour = 7;
            StopMinute = 0;
            DaysOfWeekMask = 127; // All 7 days enabled by default
            PostCompletionAction = QueuePostAction.None;
            LastAutoStartDayKey = "";
            LastAutoStopDayKey = "";
            IsRunning = false;
        }

        public QueueDefinition Clone()
        {
            return new QueueDefinition
            {
                Name = Name,
                MaxConcurrentDownloads = MaxConcurrentDownloads,
                MaxRetriesPerItem = MaxRetriesPerItem,
                RetryDelaySeconds = RetryDelaySeconds,
                EnableScheduleStart = EnableScheduleStart,
                StartHour = StartHour,
                StartMinute = StartMinute,
                EnableScheduleStop = EnableScheduleStop,
                StopHour = StopHour,
                StopMinute = StopMinute,
                DaysOfWeekMask = DaysOfWeekMask,
                PostCompletionAction = PostCompletionAction,
                LastAutoStartDayKey = LastAutoStartDayKey,
                LastAutoStopDayKey = LastAutoStopDayKey,
                IsRunning = IsRunning
            };
        }
    }

    public class AppSettings
    {
        public string BaseDownloadDirectory { get; set; }
        public int DefaultConnectionsPerDownload { get; set; }
        public bool UseCategorySubfolders { get; set; }
        public bool EnableClipboardMonitor { get; set; }
        public bool ShowStartDownloadDialog { get; set; }
        public bool ShowProgressDialogOnStart { get; set; }
        public bool ShowDownloadCompleteDialog { get; set; }
        public bool ShowDropTarget { get; set; }
        public bool SetServerLastModifiedTime { get; set; }
        public bool EnableGlobalSpeedLimit { get; set; }
        public long GlobalSpeedLimitBytesPerSec { get; set; }
        public bool EnableBrowserBridge { get; set; }
        public int BrowserBridgePort { get; set; }
        public string BridgeAuthToken { get; set; }
        public string DefaultUserAgent { get; set; }
        public string MonitoredExtensions { get; set; }
        public string ExcludedHosts { get; set; }
        public bool MinimizeToTrayOnClose { get; set; }
        public ProxyMode ProxyType { get; set; }
        public string ProxyAddress { get; set; }
        public int ProxyPort { get; set; }
        public string ProxyUser { get; set; }
        public string ProxyPassword { get; set; }
        public List<SiteLoginEntry> SiteLogins { get; set; }
        public List<QueueDefinition> Queues { get; set; }

        public AppSettings()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            BaseDownloadDirectory = Path.Combine(userProfile, "Downloads", "MHS Downloads");
            DefaultConnectionsPerDownload = 8;
            UseCategorySubfolders = true;
            EnableClipboardMonitor = true;
            ShowStartDownloadDialog = false;
            ShowProgressDialogOnStart = true;
            ShowDownloadCompleteDialog = true;
            ShowDropTarget = false;
            SetServerLastModifiedTime = true;
            EnableGlobalSpeedLimit = false;
            GlobalSpeedLimitBytesPerSec = 2 * 1024 * 1024; // 2 MB/s when enabled
            EnableBrowserBridge = true;
            BrowserBridgePort = 16888;
            BridgeAuthToken = GenerateSecureToken();
            DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
            MonitoredExtensions = "zip|rar|7z|tar|gz|xz|exe|msi|msix|iso|apk|dmg|mp4|mkv|webm|avi|mov|flv|m3u8|ts|mp3|flac|wav|m4a|ogg|pdf|epub|docx|xlsx|pptx";
            ExcludedHosts = "";
            MinimizeToTrayOnClose = false;
            ProxyType = ProxyMode.System;
            ProxyAddress = "";
            ProxyPort = 8080;
            ProxyUser = "";
            ProxyPassword = "";
            SiteLogins = new List<SiteLoginEntry>();
            Queues = new List<QueueDefinition>
            {
                new QueueDefinition
                {
                    Name = "Main Queue",
                    MaxConcurrentDownloads = 4,
                    MaxRetriesPerItem = 5,
                    RetryDelaySeconds = 30,
                    IsRunning = true
                },
                new QueueDefinition
                {
                    Name = "Synchronization Queue",
                    MaxConcurrentDownloads = 2,
                    MaxRetriesPerItem = 5,
                    RetryDelaySeconds = 30,
                    EnableScheduleStart = false,
                    StartHour = 1,
                    StartMinute = 0,
                    EnableScheduleStop = false,
                    StopHour = 6,
                    StopMinute = 30,
                    IsRunning = false
                }
            };
        }

        public static string GenerateSecureToken()
        {
            byte[] buf = new byte[32];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(buf);
            }
            StringBuilder sb = new StringBuilder(64);
            for (int i = 0; i < buf.Length; i++)
            {
                sb.Append(buf[i].ToString("x2"));
            }
            return sb.ToString();
        }

        public AppSettings CloneForPersistence()
        {
            List<SiteLoginEntry> loginCopy = new List<SiteLoginEntry>();
            if (SiteLogins != null)
            {
                for (int i = 0; i < SiteLogins.Count; i++)
                {
                    if (SiteLogins[i] != null) loginCopy.Add(SiteLogins[i].Clone());
                }
            }

            List<QueueDefinition> queueCopy = new List<QueueDefinition>();
            if (Queues != null)
            {
                for (int i = 0; i < Queues.Count; i++)
                {
                    if (Queues[i] != null) queueCopy.Add(Queues[i].Clone());
                }
            }

            return new AppSettings
            {
                BaseDownloadDirectory = BaseDownloadDirectory,
                DefaultConnectionsPerDownload = DefaultConnectionsPerDownload,
                UseCategorySubfolders = UseCategorySubfolders,
                EnableClipboardMonitor = EnableClipboardMonitor,
                ShowStartDownloadDialog = ShowStartDownloadDialog,
                ShowProgressDialogOnStart = ShowProgressDialogOnStart,
                ShowDownloadCompleteDialog = ShowDownloadCompleteDialog,
                ShowDropTarget = ShowDropTarget,
                SetServerLastModifiedTime = SetServerLastModifiedTime,
                EnableGlobalSpeedLimit = EnableGlobalSpeedLimit,
                GlobalSpeedLimitBytesPerSec = GlobalSpeedLimitBytesPerSec,
                EnableBrowserBridge = EnableBrowserBridge,
                BrowserBridgePort = BrowserBridgePort,
                BridgeAuthToken = BridgeAuthToken,
                DefaultUserAgent = DefaultUserAgent,
                MonitoredExtensions = MonitoredExtensions,
                ExcludedHosts = ExcludedHosts,
                MinimizeToTrayOnClose = MinimizeToTrayOnClose,
                ProxyType = ProxyType,
                ProxyAddress = ProxyAddress,
                ProxyPort = ProxyPort,
                ProxyUser = ProxyUser,
                ProxyPassword = ProxyPassword,
                SiteLogins = loginCopy,
                Queues = queueCopy
            };
        }
    }

    public class PersistedState
    {
        public AppSettings Settings { get; set; }
        public List<DownloadItem> Downloads { get; set; }

        public PersistedState()
        {
            Settings = new AppSettings();
            Downloads = new List<DownloadItem>();
        }
    }

    public static class HostExclusionMatcher
    {
        public static bool IsExcluded(string url, string refererUrl, string excludedPatterns)
        {
            if (string.IsNullOrWhiteSpace(excludedPatterns)) return false;
            string[] patterns = excludedPatterns.Split(
                new char[] { '|', ',', ';', '\r', '\n', ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            if (patterns.Length == 0) return false;

            string host1 = ExtractHost(url);
            string host2 = ExtractHost(refererUrl);

            for (int i = 0; i < patterns.Length; i++)
            {
                string pat = patterns[i].Trim();
                if (string.IsNullOrEmpty(pat)) continue;
                if (!string.IsNullOrEmpty(host1) && WildcardMatch(host1, pat)) return true;
                if (!string.IsNullOrEmpty(host2) && WildcardMatch(host2, pat)) return true;
            }
            return false;
        }

        private static string ExtractHost(string rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl)) return "";
            Uri u;
            if (Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out u))
            {
                return u.Host ?? "";
            }
            return "";
        }

        public static bool WildcardMatch(string input, string pattern)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(pattern)) return false;
            if (string.Equals(input, pattern, StringComparison.OrdinalIgnoreCase)) return true;
            if (pattern.StartsWith("*.", StringComparison.Ordinal) && pattern.Length > 2)
            {
                string suffix = pattern.Substring(2);
                if (string.Equals(input, suffix, StringComparison.OrdinalIgnoreCase) ||
                    input.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
        }
    }

    public static class CategoryClassifier
    {
        private static readonly HashSet<string> CompressedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".tgz", ".zst", ".cab" };

        private static readonly HashSet<string> DocumentExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv", ".epub", ".rtf", ".md" };

        private static readonly HashSet<string> MusicExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma" };

        private static readonly HashSet<string> ProgramExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".exe", ".msi", ".msix", ".appx", ".iso", ".img", ".apk", ".dmg", ".deb", ".rpm" };

        private static readonly HashSet<string> VideoExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".mkv", ".webm", ".avi", ".mov", ".flv", ".wmv", ".m4v", ".ts", ".m3u8" };

        public static string ClassifyByFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "General";
            string ext = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(ext)) return "General";
            if (CompressedExts.Contains(ext)) return "Compressed";
            if (DocumentExts.Contains(ext)) return "Documents";
            if (MusicExts.Contains(ext)) return "Music";
            if (ProgramExts.Contains(ext)) return "Programs";
            if (VideoExts.Contains(ext)) return "Video";
            return "General";
        }

        public static string ResolveSaveDirectory(AppSettings settings, string category)
        {
            string root = settings != null ? settings.BaseDownloadDirectory : null;
            if (string.IsNullOrEmpty(root))
            {
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "MHS Downloads");
            }
            if (settings != null &&
                settings.UseCategorySubfolders &&
                !string.IsNullOrEmpty(category) &&
                !string.Equals(category, "General", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(root, category);
            }
            return root;
        }

        public static string GuessFileNameFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "download.bin";
            try
            {
                Uri uri = new Uri(url.Trim());
                string leaf = Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath));
                leaf = SanitizeFileName(leaf);
                if (!string.IsNullOrWhiteSpace(leaf) && leaf != "/")
                {
                    if (string.IsNullOrEmpty(Path.GetExtension(leaf)))
                    {
                        return leaf + ".bin";
                    }
                    return leaf;
                }
            }
            catch
            {
            }
            return "download.bin";
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            char[] invalid = Path.GetInvalidFileNameChars();
            char[] chars = name.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }
            string cleaned = new string(chars).Trim('.', ' ');
            return cleaned;
        }
    }

    public static class FormattingHelpers
    {
        public static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "Unknown";
            if (bytes == 0) return "0 B";
            string[] units = new string[] { "B", "KB", "MB", "GB", "TB" };
            double val = bytes;
            int idx = 0;
            while (val >= 1024.0 && idx < units.Length - 1)
            {
                val /= 1024.0;
                idx++;
            }
            return idx == 0
                ? string.Format("{0} {1}", bytes, units[idx])
                : string.Format("{0:0.00} {1}", val, units[idx]);
        }

        public static string FormatSpeed(double bytesPerSec)
        {
            if (bytesPerSec <= 0.5) return "0 B/s";
            return FormatBytes((long)bytesPerSec) + "/s";
        }

        public static string FormatEta(long remainingBytes, double speedBytesPerSec)
        {
            if (remainingBytes <= 0) return "0s";
            if (speedBytesPerSec <= 1.0) return "--";
            double seconds = remainingBytes / speedBytesPerSec;
            if (seconds > 86400 * 30) return "> 30d";
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalHours >= 1.0)
            {
                return string.Format("{0}h {1}m {2}s", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
            }
            if (ts.TotalMinutes >= 1.0)
            {
                return string.Format("{0}m {1}s", ts.Minutes, ts.Seconds);
            }
            return string.Format("{0}s", Math.Max(1, ts.Seconds));
        }
    }

    public static class StateRepository
    {
        private static readonly object SyncLock = new object();
        public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        public static string CustomAppDataDirectory { get; set; }

        public static string GetAppDataDirectory()
        {
            string custom = CustomAppDataDirectory;
            if (string.IsNullOrWhiteSpace(custom))
            {
                custom = Environment.GetEnvironmentVariable("MHS_APPDATA_DIR");
            }

            string dir = !string.IsNullOrWhiteSpace(custom)
                ? Path.GetFullPath(custom.Trim())
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MHSManager");

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        public static string GetStateFilePath()
        {
            return Path.Combine(GetAppDataDirectory(), "state.json");
        }

        public static PersistedState Load()
        {
            lock (SyncLock)
            {
                string path = GetStateFilePath();
                if (!File.Exists(path))
                {
                    PersistedState fresh = new PersistedState();
                    SaveInternal(fresh);
                    return fresh;
                }
                try
                {
                    string json = File.ReadAllText(path);
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    ser.MaxJsonLength = 32 * 1024 * 1024;
                    PersistedState state = ser.Deserialize<PersistedState>(json);
                    if (state == null) state = new PersistedState();
                    if (state.Settings == null) state.Settings = new AppSettings();
                    if (state.Downloads == null) state.Downloads = new List<DownloadItem>();

                    bool needsSave = false;
                    if (string.IsNullOrWhiteSpace(state.Settings.BridgeAuthToken))
                    {
                        state.Settings.BridgeAuthToken = AppSettings.GenerateSecureToken();
                        needsSave = true;
                    }
                    if (state.Settings.SiteLogins == null)
                    {
                        state.Settings.SiteLogins = new List<SiteLoginEntry>();
                    }
                    if (state.Settings.Queues == null || state.Settings.Queues.Count == 0)
                    {
                        state.Settings.Queues = new AppSettings().Queues;
                        needsSave = true;
                    }
                    else
                    {
                        for (int q = 0; q < state.Settings.Queues.Count; q++)
                        {
                            QueueDefinition qd = state.Settings.Queues[q];
                            if (qd.DaysOfWeekMask <= 0) qd.DaysOfWeekMask = 127;
                            if (qd.RetryDelaySeconds <= 0) qd.RetryDelaySeconds = 30;
                            if (qd.MaxRetriesPerItem < 0) qd.MaxRetriesPerItem = 5;
                        }
                    }

                    foreach (DownloadItem item in state.Downloads)
                    {
                        if (item.Status == DownloadStatus.Downloading ||
                            item.Status == DownloadStatus.Probing ||
                            item.Status == DownloadStatus.Merging)
                        {
                            item.Status = DownloadStatus.Paused;
                        }
                        if (item.Segments == null) item.Segments = new List<SegmentInfo>();
                        if (item.RecentLogs == null) item.RecentLogs = new List<string>();
                    }

                    if (needsSave)
                    {
                        SaveInternal(state);
                    }
                    return state;
                }
                catch
                {
                    PersistedState fallback = new PersistedState();
                    SaveInternal(fallback);
                    return fallback;
                }
            }
        }

        public static void Save(PersistedState state)
        {
            if (state == null) return;
            lock (SyncLock)
            {
                SaveInternal(state);
            }
        }

        private static void SaveInternal(PersistedState state)
        {
            try
            {
                string path = GetStateFilePath();
                string tmp = path + ".tmp";
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 32 * 1024 * 1024;
                string json = ser.Serialize(state);
                File.WriteAllText(tmp, json, Utf8NoBom);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(tmp, path);
            }
            catch
            {
            }
        }
    }
}
