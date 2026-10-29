using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MHSManager
{
    public sealed class DownloadManagerController : IDisposable
    {
        private readonly object _lock = new object();
        private readonly PersistedState _state;
        private readonly Dictionary<string, DownloadTaskRunner> _runners;
        private readonly Dictionary<string, long> _lastSampleBytes;
        private readonly TokenBucketLimiter _globalLimiter;
        private readonly Timer _tickTimer;
        private readonly Stopwatch _tickWatch;

        private double _lastSampleSeconds;
        private double _totalSpeedBps;
        private volatile bool _dirty;
        private int _tickCounter;
        private string _lastScheduleMinuteKey;
        private bool _disposed;

        public event Action StateUpdated;
        public event Action<DownloadItem> DownloadStarted;
        public event Action<DownloadItem> DownloadCompleted;
        public event Action<QueueDefinition> QueuePostActionTriggered;

        public PersistedState State
        {
            get { return _state; }
        }

        public AppSettings Settings
        {
            get { return _state.Settings; }
        }

        public double TotalSpeedBytesPerSec
        {
            get { return _totalSpeedBps; }
        }

        public DownloadManagerController(PersistedState state)
        {
            _state = state ?? new PersistedState();
            if (_state.Settings == null) _state.Settings = new AppSettings();
            if (_state.Downloads == null) _state.Downloads = new List<DownloadItem>();

            _runners = new Dictionary<string, DownloadTaskRunner>(StringComparer.OrdinalIgnoreCase);
            _lastSampleBytes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            _globalLimiter = new TokenBucketLimiter(
                _state.Settings.EnableGlobalSpeedLimit,
                _state.Settings.GlobalSpeedLimitBytesPerSec);

            _tickWatch = Stopwatch.StartNew();
            _lastSampleSeconds = _tickWatch.Elapsed.TotalSeconds;
            _lastScheduleMinuteKey = "";

            _tickTimer = new Timer(OnTimerTick, null, 1000, 1000);
        }

        public PersistedState CreatePersistenceSnapshot()
        {
            lock (_lock)
            {
                PersistedState snap = new PersistedState();
                snap.Settings = _state.Settings != null
                    ? _state.Settings.CloneForPersistence()
                    : new AppSettings();
                snap.Downloads = new List<DownloadItem>(_state.Downloads.Count);
                for (int i = 0; i < _state.Downloads.Count; i++)
                {
                    DownloadItem d = _state.Downloads[i];
                    if (d != null)
                    {
                        snap.Downloads.Add(d.CloneForPersistence());
                    }
                }
                return snap;
            }
        }

        public void SaveStateSafe()
        {
            PersistedState snap = CreatePersistenceSnapshot();
            StateRepository.Save(snap);
        }

        public List<DownloadItem> GetDownloadsSnapshot()
        {
            lock (_lock)
            {
                return new List<DownloadItem>(_state.Downloads);
            }
        }

        public DownloadItem FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (_lock)
            {
                for (int i = 0; i < _state.Downloads.Count; i++)
                {
                    if (string.Equals(_state.Downloads[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        return _state.Downloads[i];
                    }
                }
            }
            return null;
        }

        public DownloadItem FindByUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            string trimmed = url.Trim();
            lock (_lock)
            {
                for (int i = 0; i < _state.Downloads.Count; i++)
                {
                    if (string.Equals((_state.Downloads[i].Url ?? "").Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        return _state.Downloads[i];
                    }
                }
            }
            return null;
        }

        public int ActiveDownloadCount
        {
            get
            {
                lock (_lock)
                {
                    return _runners.Count;
                }
            }
        }

        public bool HasActiveOrQueuedWork()
        {
            lock (_lock)
            {
                if (_runners.Count > 0) return true;
                for (int i = 0; i < _state.Downloads.Count; i++)
                {
                    DownloadItem d = _state.Downloads[i];
                    if (d.Status == DownloadStatus.Downloading ||
                        d.Status == DownloadStatus.Probing ||
                        d.Status == DownloadStatus.Merging)
                    {
                        return true;
                    }
                    QueueDefinition q = FindQueueLocked(d.QueueName);
                    if (q != null && q.IsRunning)
                    {
                        if (d.Status == DownloadStatus.Queued) return true;
                        if (d.Status == DownloadStatus.Error && d.NextRetryUtc.HasValue) return true;
                    }
                }
                return false;
            }
        }

        public DownloadItem AddDownload(DownloadItem item, bool startImmediately)
        {
            return AddDownload(item, startImmediately, false);
        }

        public DownloadItem AddDownload(DownloadItem item, bool startImmediately, bool holdPausedInQueue)
        {
            if (item == null) throw new ArgumentNullException("item");

            if (string.IsNullOrWhiteSpace(item.FileName))
            {
                item.FileName = CategoryClassifier.GuessFileNameFromUrl(item.Url);
                item.HasExplicitFileName = false;
            }
            if (HttpHelpers.IsHlsUrlOrMime(item.Url, null))
            {
                item.IsHlsStream = true;
                item.FileName = HttpHelpers.EnsureHlsOutputFileName(item.FileName, false, item.HasExplicitFileName);
            }
            if (string.IsNullOrWhiteSpace(item.CategoryName))
            {
                item.CategoryName = CategoryClassifier.ClassifyByFileName(item.FileName);
            }
            if (string.IsNullOrWhiteSpace(item.SaveDirectory))
            {
                item.SaveDirectory = CategoryClassifier.ResolveSaveDirectory(_state.Settings, item.CategoryName);
            }
            if (string.IsNullOrWhiteSpace(item.QueueName))
            {
                item.QueueName = "Main Queue";
            }
            if (item.MaxConnections <= 0)
            {
                item.MaxConnections = _state.Settings.DefaultConnectionsPerDownload;
            }

            item.RetryCount = 0;
            item.NextRetryUtc = null;
            if (holdPausedInQueue && !startImmediately)
            {
                item.Status = DownloadStatus.Paused;
            }
            else
            {
                item.Status = DownloadStatus.Queued;
            }

            if (!item.OverwriteExisting)
            {
                EnsureUniqueFileNameIfNew(item);
            }

            lock (_lock)
            {
                _state.Downloads.Insert(0, item);
                _dirty = true;
            }

            SaveStateSafe();
            RaiseStateUpdated();

            if (startImmediately)
            {
                StartDownload(item.Id);
            }
            else if (!holdPausedInQueue)
            {
                PumpRunningQueues();
            }

            return item;
        }

        private void ResolveAutoFileNameForRunner(DownloadItem item, string suggestedFileName)
        {
            if (item == null || item.HasExplicitFileName || string.IsNullOrWhiteSpace(suggestedFileName))
            {
                return;
            }

            string clean = CategoryClassifier.SanitizeFileName(suggestedFileName);
            if (string.IsNullOrWhiteSpace(clean)) return;

            lock (_lock)
            {
                string oldCategory = item.CategoryName;
                string defaultOldDir = CategoryClassifier.ResolveSaveDirectory(_state.Settings, oldCategory);
                bool wasUsingDefaultDir = string.Equals(
                    (item.SaveDirectory ?? "").TrimEnd('\\', '/'),
                    (defaultOldDir ?? "").TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);

                string newCat = CategoryClassifier.ClassifyByFileName(clean);
                item.CategoryName = newCat;
                if (wasUsingDefaultDir && !File.Exists(item.PartFilePath))
                {
                    item.SaveDirectory = CategoryClassifier.ResolveSaveDirectory(_state.Settings, newCat);
                }

                item.FileName = clean;
                if (!item.OverwriteExisting)
                {
                    EnsureUniqueFileNameLocked(item);
                }
                _dirty = true;
            }
        }

        private void EnsureUniqueFileNameIfNew(DownloadItem item)
        {
            lock (_lock)
            {
                EnsureUniqueFileNameLocked(item);
            }
        }

        private void EnsureUniqueFileNameLocked(DownloadItem item)
        {
            try
            {
                string dir = item.SaveDirectory;
                if (string.IsNullOrEmpty(dir)) return;
                string clean = CategoryClassifier.SanitizeFileName(item.FileName);
                if (string.IsNullOrWhiteSpace(clean)) clean = "download.bin";

                string baseName = Path.GetFileNameWithoutExtension(clean);
                string ext = Path.GetExtension(clean);
                string candidate = clean;
                int suffix = 1;

                while (File.Exists(Path.Combine(dir, candidate)) ||
                       File.Exists(Path.Combine(dir, candidate + ".mhspart")) ||
                       HasDownloadWithTargetLocked(dir, candidate, item.Id))
                {
                    candidate = string.Format("{0} ({1}){2}", baseName, suffix, ext);
                    suffix++;
                    if (suffix > 9999) break;
                }
                item.FileName = candidate;
            }
            catch
            {
            }
        }

        private bool HasDownloadWithTargetLocked(string dir, string fileName, string excludeId)
        {
            for (int i = 0; i < _state.Downloads.Count; i++)
            {
                DownloadItem d = _state.Downloads[i];
                if (string.Equals(d.Id, excludeId, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(d.SaveDirectory ?? "", dir ?? "", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(d.FileName ?? "", fileName ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public void StartDownload(string id)
        {
            DownloadTaskRunner runnerToStart = null;
            DownloadItem startedItem = null;

            lock (_lock)
            {
                if (_runners.ContainsKey(id))
                {
                    return;
                }

                DownloadItem item = FindByIdLocked(id);
                if (item == null || item.Status == DownloadStatus.Completed)
                {
                    return;
                }

                item.Status = DownloadStatus.Probing;
                item.ErrorMessage = null;
                item.NextRetryUtc = null;
                _lastSampleBytes[item.Id] = item.DownloadedBytes;

                runnerToStart = new DownloadTaskRunner(
                    item,
                    _state.Settings,
                    _globalLimiter,
                    OnRunnerProgress,
                    OnRunnerFinished,
                    ResolveAutoFileNameForRunner);

                _runners[item.Id] = runnerToStart;
                startedItem = item;
                _dirty = true;
            }

            if (runnerToStart != null)
            {
                runnerToStart.StartAsync();
                if (DownloadStarted != null && startedItem != null)
                {
                    DownloadStarted(startedItem);
                }
                RaiseStateUpdated();
            }
        }

        public void ResumeAllUnfinished()
        {
            List<string> ids = new List<string>();
            lock (_lock)
            {
                for (int i = 0; i < _state.Settings.Queues.Count; i++)
                {
                    if (string.Equals(_state.Settings.Queues[i].Name, "Main Queue", StringComparison.OrdinalIgnoreCase))
                    {
                        _state.Settings.Queues[i].IsRunning = true;
                    }
                }
                for (int i = _state.Downloads.Count - 1; i >= 0; i--)
                {
                    DownloadItem d = _state.Downloads[i];
                    if (d.Status == DownloadStatus.Paused || d.Status == DownloadStatus.Error)
                    {
                        d.Status = DownloadStatus.Queued;
                        d.NextRetryUtc = null;
                        ids.Add(d.Id);
                    }
                }
                _dirty = true;
            }
            PumpRunningQueues();
            for (int i = 0; i < ids.Count && ActiveDownloadCount < 4; i++)
            {
                StartDownload(ids[i]);
            }
            SaveStateSafe();
            RaiseStateUpdated();
        }

        public void PauseDownload(string id)
        {
            DownloadTaskRunner runner = null;
            lock (_lock)
            {
                if (_runners.TryGetValue(id, out runner))
                {
                    runner.RequestPause();
                }
                else
                {
                    DownloadItem item = FindByIdLocked(id);
                    if (item != null && (item.Status == DownloadStatus.Queued || item.Status == DownloadStatus.Error))
                    {
                        item.Status = DownloadStatus.Paused;
                        item.NextRetryUtc = null;
                        _dirty = true;
                    }
                }
            }
            RaiseStateUpdated();
        }

        public void PauseAll()
        {
            List<DownloadTaskRunner> active;
            lock (_lock)
            {
                for (int i = 0; i < _state.Settings.Queues.Count; i++)
                {
                    _state.Settings.Queues[i].IsRunning = false;
                }
                active = new List<DownloadTaskRunner>(_runners.Values);
                for (int i = 0; i < _state.Downloads.Count; i++)
                {
                    if (_state.Downloads[i].Status == DownloadStatus.Queued ||
                        (_state.Downloads[i].Status == DownloadStatus.Error && _state.Downloads[i].NextRetryUtc.HasValue))
                    {
                        _state.Downloads[i].Status = DownloadStatus.Paused;
                        _state.Downloads[i].NextRetryUtc = null;
                    }
                }
                _dirty = true;
            }

            for (int i = 0; i < active.Count; i++)
            {
                active[i].RequestPause();
            }

            SaveStateSafe();
            RaiseStateUpdated();
        }

        public void RemoveDownload(string id, bool deleteDiskFiles)
        {
            Task cleanupTask = RemoveDownloadAsync(id, deleteDiskFiles);
            if (cleanupTask == null) { }
        }

        public async Task RemoveDownloadAsync(string id, bool deleteDiskFiles)
        {
            DownloadItem target = null;
            DownloadTaskRunner runner = null;

            lock (_lock)
            {
                if (_runners.TryGetValue(id, out runner))
                {
                    runner.RequestCancelForRemoval();
                    _runners.Remove(id);
                }
                for (int i = 0; i < _state.Downloads.Count; i++)
                {
                    if (string.Equals(_state.Downloads[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        target = _state.Downloads[i];
                        _state.Downloads.RemoveAt(i);
                        break;
                    }
                }
                _lastSampleBytes.Remove(id);
                _dirty = true;
            }

            SaveStateSafe();
            RaiseStateUpdated();

            if (runner != null && runner.ExecutionTask != null)
            {
                try
                {
                    await runner.ExecutionTask.ConfigureAwait(false);
                }
                catch
                {
                }
            }

            if (target != null)
            {
                await TryCleanupPartialFilesWithRetryAsync(target, deleteDiskFiles).ConfigureAwait(false);
            }

            PumpRunningQueues();
        }

        public async void RedownloadFromBeginning(string id)
        {
            DownloadItem item = null;
            DownloadTaskRunner runner = null;

            lock (_lock)
            {
                item = FindByIdLocked(id);
                if (item == null) return;
                if (_runners.TryGetValue(id, out runner))
                {
                    runner.RequestCancelForRemoval();
                    _runners.Remove(id);
                }
            }

            if (runner != null && runner.ExecutionTask != null)
            {
                try { await runner.ExecutionTask.ConfigureAwait(false); } catch { }
            }

            await TryCleanupPartialFilesWithRetryAsync(item, true).ConfigureAwait(false);

            lock (item.SyncRoot)
            {
                item.DownloadedBytes = 0;
                item.Segments.Clear();
                item.ErrorMessage = null;
                item.ComputedSha256 = null;
                item.RetryCount = 0;
                item.NextRetryUtc = null;
                item.CompletedUtc = null;
                item.Status = DownloadStatus.Queued;
            }

            SaveStateSafe();
            StartDownload(item.Id);
        }

        public async Task<bool> RefreshDownloadAddressAsync(
            string id,
            string newUrl,
            string referer,
            string cookies,
            string userAgent,
            bool resumeNow)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(newUrl))
            {
                return false;
            }

            DownloadItem item = null;
            DownloadTaskRunner runner = null;

            lock (_lock)
            {
                item = FindByIdLocked(id);
                if (item == null) return false;
                if (_runners.TryGetValue(id, out runner))
                {
                    runner.RequestPause();
                }
            }

            if (runner != null && runner.ExecutionTask != null)
            {
                try { await runner.ExecutionTask.ConfigureAwait(false); } catch { }
            }

            lock (item.SyncRoot)
            {
                item.Url = newUrl.Trim();
                if (!string.IsNullOrWhiteSpace(referer)) item.Referer = referer.Trim();
                if (!string.IsNullOrWhiteSpace(cookies)) item.Cookies = cookies.Trim();
                if (!string.IsNullOrWhiteSpace(userAgent)) item.UserAgent = userAgent.Trim();
                item.ErrorMessage = null;
                item.RetryCount = 0;
                item.NextRetryUtc = null;
            }

            // Update URL inside .mhsmeta so checkpoint matches the refreshed signed URL
            try
            {
                if (File.Exists(item.MetaFilePath))
                {
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    PartMetadata meta = ser.Deserialize<PartMetadata>(File.ReadAllText(item.MetaFilePath));
                    if (meta != null)
                    {
                        meta.Url = item.Url;
                        File.WriteAllText(item.MetaFilePath, ser.Serialize(meta), StateRepository.Utf8NoBom);
                    }
                }
            }
            catch
            {
            }

            item.AddLog("Refreshed download address -> " + item.Url);
            SaveStateSafe();
            RaiseStateUpdated();

            if (resumeNow && item.Status != DownloadStatus.Completed)
            {
                StartDownload(item.Id);
            }
            return true;
        }

        public int ClearCompleted()
        {
            int removed = 0;
            lock (_lock)
            {
                for (int i = _state.Downloads.Count - 1; i >= 0; i--)
                {
                    if (_state.Downloads[i].Status == DownloadStatus.Completed)
                    {
                        _lastSampleBytes.Remove(_state.Downloads[i].Id);
                        _state.Downloads.RemoveAt(i);
                        removed++;
                    }
                }
                if (removed > 0) _dirty = true;
            }
            if (removed > 0)
            {
                SaveStateSafe();
                RaiseStateUpdated();
            }
            return removed;
        }

        private static async Task TryCleanupPartialFilesWithRetryAsync(DownloadItem item, bool deleteFinalFile)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                bool allClean = true;

                try
                {
                    if (File.Exists(item.PartFilePath)) File.Delete(item.PartFilePath);
                }
                catch
                {
                    allClean = false;
                }

                try
                {
                    if (File.Exists(item.MetaFilePath)) File.Delete(item.MetaFilePath);
                }
                catch
                {
                    allClean = false;
                }

                try
                {
                    if (File.Exists(item.MetaFilePath + ".tmp")) File.Delete(item.MetaFilePath + ".tmp");
                }
                catch
                {
                }

                try
                {
                    string hlsDir = item.PartFilePath + ".hls";
                    if (Directory.Exists(hlsDir)) Directory.Delete(hlsDir, true);
                }
                catch
                {
                    allClean = false;
                }

                if (deleteFinalFile)
                {
                    try
                    {
                        if (File.Exists(item.FullTargetPath)) File.Delete(item.FullTargetPath);
                    }
                    catch
                    {
                        allClean = false;
                    }
                }

                if (allClean)
                {
                    return;
                }

                await Task.Delay(50 * (attempt + 1)).ConfigureAwait(false);
            }
        }

        public void StartQueue(string queueName)
        {
            lock (_lock)
            {
                QueueDefinition q = FindQueueLocked(queueName);
                if (q != null)
                {
                    q.IsRunning = true;
                    for (int i = 0; i < _state.Downloads.Count; i++)
                    {
                        DownloadItem d = _state.Downloads[i];
                        if (string.Equals(d.QueueName, q.Name, StringComparison.OrdinalIgnoreCase) &&
                            (d.Status == DownloadStatus.Paused || d.Status == DownloadStatus.Error))
                        {
                            d.Status = DownloadStatus.Queued;
                            d.RetryCount = 0;
                            d.NextRetryUtc = null;
                        }
                    }
                    _dirty = true;
                }
            }
            SaveStateSafe();
            PumpRunningQueues();
            RaiseStateUpdated();
        }

        public void StopQueue(string queueName)
        {
            List<DownloadTaskRunner> toPause = new List<DownloadTaskRunner>();
            lock (_lock)
            {
                QueueDefinition q = FindQueueLocked(queueName);
                if (q != null)
                {
                    q.IsRunning = false;
                    foreach (KeyValuePair<string, DownloadTaskRunner> kvp in _runners)
                    {
                        if (string.Equals(kvp.Value.Item.QueueName, q.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            toPause.Add(kvp.Value);
                        }
                    }
                    for (int i = 0; i < _state.Downloads.Count; i++)
                    {
                        DownloadItem d = _state.Downloads[i];
                        if (string.Equals(d.QueueName, q.Name, StringComparison.OrdinalIgnoreCase) &&
                            d.NextRetryUtc.HasValue)
                        {
                            d.NextRetryUtc = null;
                        }
                    }
                    _dirty = true;
                }
            }

            for (int i = 0; i < toPause.Count; i++)
            {
                toPause[i].RequestPause();
            }
            SaveStateSafe();
            RaiseStateUpdated();
        }

        public void UpdateItemSpeedLimit(string id, long maxBytesPerSec)
        {
            lock (_lock)
            {
                DownloadItem item = FindByIdLocked(id);
                if (item != null)
                {
                    item.MaxSpeedBytesPerSec = Math.Max(0, maxBytesPerSec);
                    _dirty = true;
                }
                DownloadTaskRunner runner;
                if (_runners.TryGetValue(id, out runner))
                {
                    runner.UpdateItemSpeedLimit(maxBytesPerSec);
                }
            }
            SaveStateSafe();
            RaiseStateUpdated();
        }

        public void ApplySettingsChanged()
        {
            _globalLimiter.Configure(
                _state.Settings.EnableGlobalSpeedLimit,
                _state.Settings.GlobalSpeedLimitBytesPerSec);
            SaveStateSafe();
            PumpRunningQueues();
            RaiseStateUpdated();
        }

        private DownloadItem FindByIdLocked(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < _state.Downloads.Count; i++)
            {
                if (string.Equals(_state.Downloads[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return _state.Downloads[i];
                }
            }
            return null;
        }

        private QueueDefinition FindQueueLocked(string name)
        {
            for (int i = 0; i < _state.Settings.Queues.Count; i++)
            {
                if (string.Equals(_state.Settings.Queues[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return _state.Settings.Queues[i];
                }
            }
            return null;
        }

        private void PumpRunningQueues()
        {
            List<string> idsToStart = new List<string>();

            lock (_lock)
            {
                for (int qIdx = 0; qIdx < _state.Settings.Queues.Count; qIdx++)
                {
                    QueueDefinition q = _state.Settings.Queues[qIdx];
                    if (!q.IsRunning) continue;

                    int activeInQueue = 0;
                    foreach (KeyValuePair<string, DownloadTaskRunner> kvp in _runners)
                    {
                        if (string.Equals(kvp.Value.Item.QueueName, q.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            activeInQueue++;
                        }
                    }

                    int slots = Math.Max(1, q.MaxConcurrentDownloads) - activeInQueue;
                    if (slots <= 0) continue;

                    // Start oldest queued items first (tail of list)
                    for (int i = _state.Downloads.Count - 1; i >= 0 && slots > 0; i--)
                    {
                        DownloadItem candidate = _state.Downloads[i];
                        if (candidate.Status == DownloadStatus.Queued &&
                            string.Equals(candidate.QueueName, q.Name, StringComparison.OrdinalIgnoreCase) &&
                            !_runners.ContainsKey(candidate.Id))
                        {
                            idsToStart.Add(candidate.Id);
                            slots--;
                        }
                    }
                }
            }

            for (int i = 0; i < idsToStart.Count; i++)
            {
                StartDownload(idsToStart[i]);
            }
        }

        private void OnRunnerProgress(DownloadItem item)
        {
            _dirty = true;
            RaiseStateUpdated();
        }

        private void OnRunnerFinished(DownloadItem item)
        {
            bool stillOwned = false;
            bool completedNow = false;
            QueueDefinition finishedQueuePost = null;

            lock (_lock)
            {
                _runners.Remove(item.Id);
                _lastSampleBytes.Remove(item.Id);
                item.CurrentSpeedBytesPerSec = 0;

                stillOwned = (FindByIdLocked(item.Id) != null);
                if (!stillOwned)
                {
                    return;
                }

                completedNow = (item.Status == DownloadStatus.Completed);

                if (item.Status == DownloadStatus.Error)
                {
                    QueueDefinition q = FindQueueLocked(item.QueueName);
                    if (q != null && q.IsRunning && item.RetryCount < q.MaxRetriesPerItem)
                    {
                        item.RetryCount++;
                        int delaySec = q.RetryDelaySeconds > 0 ? q.RetryDelaySeconds : 30;
                        item.NextRetryUtc = DateTime.UtcNow.AddSeconds(delaySec);
                        item.AddLog(string.Format(
                            "Scheduler: Retry {0}/{1} scheduled in {2}s...",
                            item.RetryCount,
                            q.MaxRetriesPerItem,
                            delaySec));
                    }
                }

                _dirty = true;
            }

            SaveStateSafe();
            PumpRunningQueues();

            if (completedNow)
            {
                lock (_lock)
                {
                    QueueDefinition q = FindQueueLocked(item.QueueName);
                    if (q != null && q.IsRunning && q.PostCompletionAction != QueuePostAction.None)
                    {
                        bool anyRemainingInQueue = false;
                        for (int i = 0; i < _state.Downloads.Count; i++)
                        {
                            DownloadItem d = _state.Downloads[i];
                            if (!string.Equals(d.QueueName, q.Name, StringComparison.OrdinalIgnoreCase)) continue;
                            if (d.Status == DownloadStatus.Queued ||
                                d.Status == DownloadStatus.Probing ||
                                d.Status == DownloadStatus.Downloading ||
                                d.Status == DownloadStatus.Merging ||
                                (d.Status == DownloadStatus.Error && d.NextRetryUtc.HasValue))
                            {
                                anyRemainingInQueue = true;
                                break;
                            }
                        }
                        if (!anyRemainingInQueue)
                        {
                            finishedQueuePost = q;
                        }
                    }
                }
            }

            RaiseStateUpdated();

            if (completedNow)
            {
                HandleCompletionAction(item);
                if (DownloadCompleted != null)
                {
                    DownloadCompleted(item);
                }
                if (finishedQueuePost != null && QueuePostActionTriggered != null)
                {
                    QueuePostActionTriggered(finishedQueuePost);
                }
            }
        }

        private static void HandleCompletionAction(DownloadItem item)
        {
            try
            {
                if (item.OnComplete == CompletionAction.OpenFile && File.Exists(item.FullTargetPath))
                {
                    Process.Start(new ProcessStartInfo(item.FullTargetPath) { UseShellExecute = true });
                }
                else if (item.OnComplete == CompletionAction.OpenFolder && File.Exists(item.FullTargetPath))
                {
                    Process.Start("explorer.exe", "/select,\"" + item.FullTargetPath + "\"");
                }
            }
            catch
            {
            }
        }

        private void OnTimerTick(object stateObj)
        {
            if (_disposed) return;

            double nowSec = _tickWatch.Elapsed.TotalSeconds;
            double dt = nowSec - _lastSampleSeconds;
            if (dt <= 0.05) return;
            _lastSampleSeconds = nowSec;

            double aggregateBps = 0;
            bool hadActive = false;
            bool wokeRetry = false;
            DateTime utcNow = DateTime.UtcNow;

            lock (_lock)
            {
                foreach (KeyValuePair<string, DownloadTaskRunner> kvp in _runners)
                {
                    hadActive = true;
                    DownloadItem item = kvp.Value.Item;
                    long curBytes = item.DownloadedBytes;
                    long prevBytes;
                    if (!_lastSampleBytes.TryGetValue(item.Id, out prevBytes))
                    {
                        prevBytes = curBytes;
                    }
                    _lastSampleBytes[item.Id] = curBytes;

                    long delta = Math.Max(0, curBytes - prevBytes);
                    double instBps = delta / dt;
                    item.CurrentSpeedBytesPerSec = item.CurrentSpeedBytesPerSec <= 1.0
                        ? instBps
                        : (item.CurrentSpeedBytesPerSec * 0.55) + (instBps * 0.45);

                    aggregateBps += item.CurrentSpeedBytesPerSec;
                }

                for (int i = 0; i < _state.Downloads.Count; i++)
                {
                    DownloadItem d = _state.Downloads[i];
                    if (d.Status == DownloadStatus.Error && d.NextRetryUtc.HasValue && utcNow >= d.NextRetryUtc.Value)
                    {
                        QueueDefinition q = FindQueueLocked(d.QueueName);
                        if (q != null && q.IsRunning)
                        {
                            d.NextRetryUtc = null;
                            d.Status = DownloadStatus.Queued;
                            wokeRetry = true;
                            _dirty = true;
                        }
                    }
                }
            }

            _totalSpeedBps = hadActive ? aggregateBps : 0.0;

            if (wokeRetry)
            {
                PumpRunningQueues();
            }

            CheckQueueSchedules();

            int tick = Interlocked.Increment(ref _tickCounter);
            if (_dirty && (tick % 5 == 0))
            {
                _dirty = false;
                SaveStateSafe();
            }

            if (hadActive || wokeRetry)
            {
                RaiseStateUpdated();
            }
        }

        private void CheckQueueSchedules()
        {
            DateTime now = DateTime.Now;
            string minuteKey = now.ToString("yyyyMMddHHmm");
            if (minuteKey == _lastScheduleMinuteKey) return;
            _lastScheduleMinuteKey = minuteKey;

            string todayKey = now.ToString("yyyyMMdd");
            int dayBit = 1 << (int)now.DayOfWeek;
            int nowMinutes = now.Hour * 60 + now.Minute;

            List<string> toStart = new List<string>();
            List<string> toStop = new List<string>();

            lock (_lock)
            {
                for (int i = 0; i < _state.Settings.Queues.Count; i++)
                {
                    QueueDefinition q = _state.Settings.Queues[i];
                    int mask = q.DaysOfWeekMask > 0 ? q.DaysOfWeekMask : 127;
                    if ((mask & dayBit) == 0)
                    {
                        continue;
                    }

                    int startMin = q.StartHour * 60 + q.StartMinute;
                    int stopMin = q.StopHour * 60 + q.StopMinute;

                    if (q.EnableScheduleStart && q.LastAutoStartDayKey != todayKey)
                    {
                        bool inStartWindow = nowMinutes >= startMin &&
                            (!q.EnableScheduleStop || stopMin <= startMin || nowMinutes < stopMin);
                        if (inStartWindow)
                        {
                            q.LastAutoStartDayKey = todayKey;
                            if (!q.IsRunning)
                            {
                                toStart.Add(q.Name);
                            }
                            _dirty = true;
                        }
                    }

                    if (q.EnableScheduleStop && q.LastAutoStopDayKey != todayKey)
                    {
                        bool reachedStop = nowMinutes >= stopMin &&
                            (!q.EnableScheduleStart || stopMin >= startMin || nowMinutes < startMin);
                        if (reachedStop)
                        {
                            q.LastAutoStopDayKey = todayKey;
                            if (q.IsRunning)
                            {
                                toStop.Add(q.Name);
                            }
                            _dirty = true;
                        }
                    }
                }
            }

            for (int i = 0; i < toStart.Count; i++) StartQueue(toStart[i]);
            for (int i = 0; i < toStop.Count; i++) StopQueue(toStop[i]);
        }

        private void RaiseStateUpdated()
        {
            Action handler = StateUpdated;
            if (handler != null)
            {
                try { handler(); } catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _tickTimer.Dispose(); } catch { }
            PauseAll();
        }
    }
}
