using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

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

        public DownloadItem AddDownload(DownloadItem item, bool startImmediately)
        {
            if (item == null) throw new ArgumentNullException("item");

            if (string.IsNullOrWhiteSpace(item.FileName))
            {
                item.FileName = CategoryClassifier.GuessFileNameFromUrl(item.Url);
            }
            if (HttpHelpers.IsHlsUrlOrMime(item.Url, null))
            {
                item.IsHlsStream = true;
                item.FileName = HttpHelpers.EnsureNonM3u8VideoFileName(item.FileName);
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

            EnsureUniqueFileNameIfNew(item);

            lock (_lock)
            {
                _state.Downloads.Insert(0, item);
                _dirty = true;
            }

            StateRepository.Save(_state);
            RaiseStateUpdated();

            if (startImmediately)
            {
                StartDownload(item.Id);
            }
            else
            {
                PumpRunningQueues();
            }

            return item;
        }

        private void EnsureUniqueFileNameIfNew(DownloadItem item)
        {
            try
            {
                string dir = item.SaveDirectory;
                if (string.IsNullOrEmpty(dir)) return;
                string baseName = Path.GetFileNameWithoutExtension(item.FileName);
                string ext = Path.GetExtension(item.FileName);
                string candidate = item.FileName;
                int suffix = 1;

                while (File.Exists(Path.Combine(dir, candidate)) ||
                       File.Exists(Path.Combine(dir, candidate + ".mhspart")) ||
                       HasDownloadWithTarget(dir, candidate, item.Id))
                {
                    candidate = string.Format("{0} ({1}){2}", baseName, suffix, ext);
                    suffix++;
                    if (suffix > 999) break;
                }
                item.FileName = candidate;
            }
            catch
            {
            }
        }

        private bool HasDownloadWithTarget(string dir, string fileName, string excludeId)
        {
            lock (_lock)
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

                DownloadItem item = FindById(id);
                if (item == null || item.Status == DownloadStatus.Completed)
                {
                    return;
                }

                item.Status = DownloadStatus.Probing;
                item.ErrorMessage = null;
                _lastSampleBytes[item.Id] = item.DownloadedBytes;

                runnerToStart = new DownloadTaskRunner(
                    item,
                    _state.Settings,
                    _globalLimiter,
                    OnRunnerProgress,
                    OnRunnerFinished);

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
                    DownloadItem item = FindById(id);
                    if (item != null && item.Status == DownloadStatus.Queued)
                    {
                        item.Status = DownloadStatus.Paused;
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
                    if (_state.Downloads[i].Status == DownloadStatus.Queued)
                    {
                        _state.Downloads[i].Status = DownloadStatus.Paused;
                    }
                }
                _dirty = true;
            }

            for (int i = 0; i < active.Count; i++)
            {
                active[i].RequestPause();
            }

            StateRepository.Save(_state);
            RaiseStateUpdated();
        }

        public void RemoveDownload(string id, bool deleteDiskFiles)
        {
            DownloadItem target = null;
            DownloadTaskRunner runner = null;

            lock (_lock)
            {
                if (_runners.TryGetValue(id, out runner))
                {
                    runner.RequestPause();
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

            if (target != null)
            {
                TryCleanupPartialFiles(target, deleteDiskFiles);
            }

            StateRepository.Save(_state);
            RaiseStateUpdated();
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
                StateRepository.Save(_state);
                RaiseStateUpdated();
            }
            return removed;
        }

        private static void TryCleanupPartialFiles(DownloadItem item, bool deleteFinalFile)
        {
            try
            {
                if (File.Exists(item.PartFilePath)) File.Delete(item.PartFilePath);
            }
            catch { }

            try
            {
                if (File.Exists(item.MetaFilePath)) File.Delete(item.MetaFilePath);
            }
            catch { }

            try
            {
                string hlsDir = item.PartFilePath + ".hls";
                if (Directory.Exists(hlsDir)) Directory.Delete(hlsDir, true);
            }
            catch { }

            if (deleteFinalFile)
            {
                try
                {
                    if (File.Exists(item.FullTargetPath)) File.Delete(item.FullTargetPath);
                }
                catch { }
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
                        }
                    }
                    _dirty = true;
                }
            }
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
                    _dirty = true;
                }
            }

            for (int i = 0; i < toPause.Count; i++)
            {
                toPause[i].RequestPause();
            }
            RaiseStateUpdated();
        }

        public void UpdateItemSpeedLimit(string id, long maxBytesPerSec)
        {
            lock (_lock)
            {
                DownloadItem item = FindById(id);
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
            StateRepository.Save(_state);
            RaiseStateUpdated();
        }

        public void ApplySettingsChanged()
        {
            _globalLimiter.Configure(
                _state.Settings.EnableGlobalSpeedLimit,
                _state.Settings.GlobalSpeedLimitBytesPerSec);
            StateRepository.Save(_state);
            PumpRunningQueues();
            RaiseStateUpdated();
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
            bool completedNow = false;
            lock (_lock)
            {
                _runners.Remove(item.Id);
                _lastSampleBytes.Remove(item.Id);
                item.CurrentSpeedBytesPerSec = 0;
                completedNow = (item.Status == DownloadStatus.Completed);
                _dirty = true;
            }

            StateRepository.Save(_state);
            PumpRunningQueues();
            RaiseStateUpdated();

            if (completedNow)
            {
                HandleCompletionAction(item);
                if (DownloadCompleted != null)
                {
                    DownloadCompleted(item);
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
            }

            _totalSpeedBps = hadActive ? aggregateBps : 0.0;

            CheckQueueSchedules();

            int tick = Interlocked.Increment(ref _tickCounter);
            if (_dirty && (tick % 5 == 0))
            {
                _dirty = false;
                StateRepository.Save(_state);
            }

            if (hadActive)
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

            List<string> toStart = new List<string>();
            List<string> toStop = new List<string>();

            lock (_lock)
            {
                for (int i = 0; i < _state.Settings.Queues.Count; i++)
                {
                    QueueDefinition q = _state.Settings.Queues[i];
                    if (q.EnableScheduleStart && q.StartHour == now.Hour && q.StartMinute == now.Minute && !q.IsRunning)
                    {
                        toStart.Add(q.Name);
                    }
                    if (q.EnableScheduleStop && q.StopHour == now.Hour && q.StopMinute == now.Minute && q.IsRunning)
                    {
                        toStop.Add(q.Name);
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

