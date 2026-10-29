using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MHSManager
{
    /// <summary>
    /// Thread-safe token-bucket rate limiter supporting both global and per-download caps.
    /// </summary>
    public class TokenBucketLimiter
    {
        private readonly object _lock = new object();
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private double _availableTokens;
        private double _lastTicks;
        private long _rateBytesPerSec;
        private bool _enabled;

        public TokenBucketLimiter(bool enabled, long rateBytesPerSec)
        {
            _enabled = enabled;
            _rateBytesPerSec = Math.Max(0, rateBytesPerSec);
            _availableTokens = _rateBytesPerSec;
            _lastTicks = _watch.Elapsed.TotalSeconds;
        }

        public void Configure(bool enabled, long rateBytesPerSec)
        {
            lock (_lock)
            {
                _enabled = enabled;
                _rateBytesPerSec = Math.Max(0, rateBytesPerSec);
                if (_availableTokens > _rateBytesPerSec)
                {
                    _availableTokens = _rateBytesPerSec;
                }
            }
        }

        public bool IsActive
        {
            get
            {
                lock (_lock)
                {
                    return _enabled && _rateBytesPerSec > 0;
                }
            }
        }

        public async Task ThrottleAsync(int bytesCount, CancellationToken token)
        {
            if (bytesCount <= 0) return;

            while (!token.IsCancellationRequested)
            {
                int waitMs = 0;
                lock (_lock)
                {
                    if (!_enabled || _rateBytesPerSec <= 0)
                    {
                        return;
                    }

                    double now = _watch.Elapsed.TotalSeconds;
                    double elapsed = now - _lastTicks;
                    if (elapsed > 0)
                    {
                        _availableTokens = Math.Min(_rateBytesPerSec, _availableTokens + elapsed * _rateBytesPerSec);
                        _lastTicks = now;
                    }

                    if (_availableTokens >= bytesCount || bytesCount > _rateBytesPerSec)
                    {
                        _availableTokens -= bytesCount;
                        return;
                    }

                    double deficit = bytesCount - _availableTokens;
                    waitMs = (int)Math.Ceiling((deficit / _rateBytesPerSec) * 1000.0);
                    if (waitMs < 10) waitMs = 10;
                    if (waitMs > 250) waitMs = 250;
                }

                await Task.Delay(waitMs, token).ConfigureAwait(false);
            }
        }
    }
}

