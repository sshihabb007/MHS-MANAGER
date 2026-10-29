using System;
using System.Collections.Generic;
using System.IO;
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
    }

    public class DownloadItem
    {
        public string Id { get; set; }
        public string Url { get; set; }
        public string FileName { get; set; }
        public string SaveDirectory { get; set; }
        public string CategoryName { get; set; }
        public string QueueName { get; set; }
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
        public string ETag { get; set; }
        public string LastModified { get; set; }
        public DownloadStatus Status { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime AddedUtc { get; set; }
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
            lock (RecentLogs)
            {
                string line = string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss"), message);
                RecentLogs.Add(line);
                if (RecentLogs.Count > 250)
                {
                    RecentLogs.RemoveAt(0);
                }
            }
        }
    }

    public class QueueDefinition
    {
        public string Name { get; set; }
        public int MaxConcurrentDownloads { get; set; }
        public bool EnableScheduleStart { get; set; }
        public int StartHour { get; set; }
        public int StartMinute { get; set; }
        public bool EnableScheduleStop { get; set; }
        public int StopHour { get; set; }
        public int StopMinute { get; set; }
        public bool IsRunning { get; set; }

        public QueueDefinition()
        {
            Name = "Main Queue";
            MaxConcurrentDownloads = 4;
            StartHour = 1;
            StartMinute = 0;
            StopHour = 7;
            StopMinute = 0;
        }
    }

    public class AppSettings
    {
        public string BaseDownloadDirectory { get; set; }
        public int DefaultConnectionsPerDownload { get; set; }
        public bool UseCategorySubfolders { get; set; }
        public bool EnableClipboardMonitor { get; set; }
        public bool ShowProgressDialogOnStart { get; set; }
        public bool EnableGlobalSpeedLimit { get; set; }
        public long GlobalSpeedLimitBytesPerSec { get; set; }
        public bool EnableBrowserBridge { get; set; }
        public int BrowserBridgePort { get; set; }
        public string DefaultUserAgent { get; set; }
        public string MonitoredExtensions { get; set; }
        public bool MinimizeToTrayOnClose { get; set; }
        public List<QueueDefinition> Queues { get; set; }

        public AppSettings()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            BaseDownloadDirectory = Path.Combine(userProfile, "Downloads", "MHS Downloads");
            DefaultConnectionsPerDownload = 8;
            UseCategorySubfolders = true;
            EnableClipboardMonitor = true;
            ShowProgressDialogOnStart = true;
            EnableGlobalSpeedLimit = false;
            GlobalSpeedLimitBytesPerSec = 2 * 1024 * 1024; // 2 MB/s when enabled
            EnableBrowserBridge = true;
            BrowserBridgePort = 16888;
            DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
            MonitoredExtensions = "zip|rar|7z|tar|gz|xz|exe|msi|msix|iso|apk|dmg|mp4|mkv|webm|avi|mov|flv|m3u8|ts|mp3|flac|wav|m4a|ogg|pdf|epub|docx|xlsx|pptx";
            MinimizeToTrayOnClose = false;
            Queues = new List<QueueDefinition>
            {
                new QueueDefinition { Name = "Main Queue", MaxConcurrentDownloads = 4 },
                new QueueDefinition
                {
                    Name = "Night Queue",
                    MaxConcurrentDownloads = 2,
                    EnableScheduleStart = false,
                    StartHour = 1,
                    StartMinute = 0,
                    EnableScheduleStop = false,
                    StopHour = 6,
                    StopMinute = 30
                }
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
            string root = settings.BaseDownloadDirectory;
            if (string.IsNullOrEmpty(root))
            {
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "MHS Downloads");
            }
            if (settings.UseCategorySubfolders && !string.IsNullOrEmpty(category) && !string.Equals(category, "General", StringComparison.OrdinalIgnoreCase))
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

        public static string GetAppDataDirectory()
        {
            string dir = Path.Combine(
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
                    return new PersistedState();
                }
                try
                {
                    string json = File.ReadAllText(path);
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    ser.MaxJsonLength = 32 * 1024 * 1024;
                    PersistedState state = ser.Deserialize<PersistedState>(json);
                    if (state == null) return new PersistedState();
                    if (state.Settings == null) state.Settings = new AppSettings();
                    if (state.Downloads == null) state.Downloads = new List<DownloadItem>();
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
                    return state;
                }
                catch
                {
                    return new PersistedState();
                }
            }
        }

        public static void Save(PersistedState state)
        {
            lock (SyncLock)
            {
                try
                {
                    string path = GetStateFilePath();
                    string tmp = path + ".tmp";
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    ser.MaxJsonLength = 32 * 1024 * 1024;
                    string json = ser.Serialize(state);
                    File.WriteAllText(tmp, json);
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
}

