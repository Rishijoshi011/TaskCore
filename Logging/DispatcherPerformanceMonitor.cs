using System;
using System.Windows.Threading;

namespace TaskCore.Logging
{
    public sealed class DispatcherPerformanceMonitor
    {
        private static DispatcherPerformanceMonitor? _instance;
        public static DispatcherPerformanceMonitor Instance => _instance ??= new DispatcherPerformanceMonitor();

        private readonly DispatcherTimer _heartbeatTimer;
        private DateTime _lastTickUtc;

        private DispatcherPerformanceMonitor()
        {
            _lastTickUtc = DateTime.UtcNow;
            _heartbeatTimer = new DispatcherTimer(DispatcherPriority.Send)
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };

            _heartbeatTimer.Tick += OnTick;
        }

        public void Start()
        {
            _lastTickUtc = DateTime.UtcNow;
            if (!_heartbeatTimer.IsEnabled)
            {
                _heartbeatTimer.Start();
            }
        }

        public void Stop()
        {
            if (_heartbeatTimer.IsEnabled)
            {
                _heartbeatTimer.Stop();
            }
        }

        private void OnTick(object? sender, EventArgs e)
        {
            var now = DateTime.UtcNow;
            var elapsedMs = (now - _lastTickUtc).TotalMilliseconds;
            _lastTickUtc = now;

            // Target interval is 250ms. If elapsed > 350ms, UI thread was blocked for > 100ms!
            if (elapsedMs > 350)
            {
                var freezeDurationMs = elapsedMs - 250;
                TaskCoreLog.Warn(
                    "Performance",
                    $"UI Thread Freeze Detected! WPF Dispatcher message pump was blocked for {freezeDurationMs:F0}ms.",
                    method: "OnTick",
                    filePath: nameof(DispatcherPerformanceMonitor));
            }
        }
    }
}
