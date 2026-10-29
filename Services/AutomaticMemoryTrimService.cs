using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TaskCore.Logging;

namespace TaskCore.Services
{
    /// <summary>
    /// Background service that periodically checks system RAM usage and calls the
    /// existing <see cref="QuickActionsService.TrimRamAsync"/> when usage exceeds
    /// the configured threshold. Uses the same Lazy singleton + Timer pattern as
    /// <see cref="AutomationWatchdogService"/>.
    /// </summary>
    internal sealed class AutomaticMemoryTrimService : IDisposable
    {
        private static readonly Lazy<AutomaticMemoryTrimService> _instance = new(() => new AutomaticMemoryTrimService());
        public static AutomaticMemoryTrimService Instance => _instance.Value;

        private readonly object _lock = new();
        private Timer? _timer;
        private int _trimRunning;

        // ── GlobalMemoryStatusEx P/Invoke (same struct as MemoryViewModel) ───
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint dwLength;
            public uint dwMemoryLoad;       // 0-100 system-wide RAM usage %
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

        /// <summary>Returns current system RAM usage as a percentage (0-100), or null on failure.</summary>
        private static uint? GetMemoryLoadPercent()
        {
            try
            {
                var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                return GlobalMemoryStatusEx(ref status) ? status.dwMemoryLoad : null;
            }
            catch { return null; }
        }

        private AutomaticMemoryTrimService()
        {
            StartOrStop();
        }

        /// <summary>
        /// Reads the current settings and starts or stops the timer accordingly.
        /// Called from the constructor, and again from SettingsViewModel when the
        /// user changes any of the three automatic trim settings.
        /// </summary>
        public void StartOrStop()
        {
            lock (_lock)
            {
                try
                {
                    var settings = AppSettingsService.Load();
                    if (!settings.AutomaticMemoryTrimEnabled ||
                        settings.AutomaticMemoryTrimIntervalMinutes <= 0)
                    {
                        _timer?.Dispose();
                        _timer = null;
                        return;
                    }

                    // (Re-)create the timer with the configured interval.
                    // Using Timeout.InfiniteTimeSpan for the initial delay avoids an
                    // immediate check on startup — the first check happens after one full interval.
                    _timer?.Dispose();
                    var interval = TimeSpan.FromMinutes(settings.AutomaticMemoryTrimIntervalMinutes);
                    _timer = new Timer(_ => OnInterval(), null, interval, interval);
                }
                catch
                {
                }
            }
        }

        /// <summary>Fires once per configured interval.</summary>
        private void OnInterval()
        {
            // Guard: skip if a trim from a previous interval is still running.
            if (Interlocked.Exchange(ref _trimRunning, 1) == 1)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    var settings = AppSettingsService.Load();
                    if (!settings.AutomaticMemoryTrimEnabled)
                        return;

                    var load = GetMemoryLoadPercent();
                    if (!load.HasValue)
                        return;

                    if (load.Value >= settings.AutomaticMemoryTrimThresholdPercent)
                    {
                        TaskCoreLog.Info("AutoTrim",
                            $"Automatic memory trim triggered. Memory usage: {load.Value}%, threshold: {settings.AutomaticMemoryTrimThresholdPercent}%.");

                        // Call the EXACT SAME method the manual Quick Action uses.
                        await QuickActionsService.TrimRamAsync().ConfigureAwait(false);
                    }
                    // No logging when RAM is below threshold — avoids log noise.
                }
                catch (Exception ex)
                {
                    TaskCoreLog.Error("AutoTrim", $"Automatic memory trim error: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _trimRunning, 0);
                }
            });
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }
    }
}
