using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TaskCore.Services
{
    public partial class UsageTrackingService
    {
        private static readonly Lazy<UsageTrackingService> _instance = new(() => new UsageTrackingService());
        public static UsageTrackingService Instance => _instance.Value;

        private readonly DispatcherTimer _timer;
        private readonly UsageMetricsSampler _metricsSampler = new();
        private readonly UsageSessionTracker _sessionTracker = new();
        private IReadOnlyDictionary<string, (DateTime start, string path, HashSet<int> pids)> _trackedApps => _sessionTracker.TrackedApps;
        private readonly object _lock = new();
        private int _tickRunning;
        private bool _isSessionLocked = false;
        private DateTime _lastInputTime = DateTime.Now;
        private readonly TimeSpan _idleThreshold = TimeSpan.FromMinutes(5);

        // Background persistence queue
        private readonly System.Collections.Concurrent.ConcurrentQueue<SessionWriteRequest> _writeQueue = new();
        private readonly System.Threading.CancellationTokenSource _writeCts = new();
        private readonly Task _backgroundWriter;

        private sealed class SessionWriteRequest
        {
            public string AppName { get; set; } = string.Empty;
            public int FirstPid { get; set; }
            public string Path { get; set; } = string.Empty;
            public DateTime Start { get; set; }
            public DateTime End { get; set; }
            public double PeakCpu { get; set; }
            public double AvgCpu { get; set; }
            public double PeakMem { get; set; }
            public double AvgMem { get; set; }
            public bool IsIdle { get; set; }
        }

        private UsageTrackingService()
        {
            try
            {
                UsageTrackingStorage.EnsureDirectory();
                UsageTrackingStorage.CleanupOldFiles();
            }
            catch { }

            // Finalize any orphaned sessions from previous crash/kill
            UsageTrackingStorage.FinalizeOrphanedSessions();

            // Subscribe to session switch events to detect lock/unlock
            SystemEvents.SessionSwitch += OnSessionSwitch;

            // Subscribe to crash/exit handlers to save sessions
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            // Start background writer
            _backgroundWriter = Task.Run(BackgroundWriterLoop);

            // Start periodic active session saver (for crash recovery)
            _ = Task.Run(PeriodicActiveSessionSaver);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, __) => KickTick();
            _timer.Start();
        }

        private async Task PeriodicActiveSessionSaver()
        {
            while (!_writeCts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), _writeCts.Token);
                    
                    // Only save if there are active sessions (skip when idle)
                    if (_trackedApps.Count > 0)
                    {
                        SaveActiveSessionsToDisk();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch { }
            }
        }

        private void SaveActiveSessionsToDisk()
        {
            try
            {
                var sessions = new List<ActiveSessionData>();
                var now = DateTime.UtcNow;

                lock (_lock)
                {
                    foreach (var kvp in _trackedApps)
                    {
                        var appName = kvp.Key;
                        var (startTime, path, pids) = kvp.Value;
                        
                        _sessionTracker.GetMetricsForApp(appName, out var peakCpu, out var avgCpu, out var peakMem, out var avgMem);
                        
                        sessions.Add(new ActiveSessionData
                        {
                            appName = appName,
                            firstPid = pids.FirstOrDefault(),
                            path = path,
                            startTime = startTime.ToUniversalTime(),
                            lastUpdateTime = now,
                            peakCpu = peakCpu,
                            avgCpu = avgCpu,
                            peakMem = peakMem,
                            avgMem = avgMem
                        });
                    }
                }

                UsageTrackingStorage.SaveActiveSessions(sessions);
                
                if (sessions.Count > 0)
                {
                    TaskCoreLogger.SessionTracking.ActiveSessionsSaved(sessions.Count);
                }
            }
            catch (Exception ex)
            {
                TaskCoreLogger.Errors.ActiveSessionSave($"SaveActiveSessionsToDisk error: {ex.Message}");
            }
        }

        private void OnProcessExit(object? sender, EventArgs e)
        {
            try
            {
                TaskCoreLogger.Lifecycle.ProcessExit();
                SaveRunningSessionsOnShutdown();
            }
            catch { }
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                TaskCoreLogger.Lifecycle.UnhandledException(ex?.Message ?? "Unknown");
                SaveRunningSessionsOnShutdown();
            }
            catch { }
        }

        private async Task BackgroundWriterLoop()
        {
            while (!_writeCts.IsCancellationRequested)
            {
                try
                {
                    // Process up to 100 sessions per batch
                    var batch = new List<SessionWriteRequest>();
                    for (int i = 0; i < 100 && _writeQueue.TryDequeue(out var request); i++)
                    {
                        batch.Add(request);
                    }

                    if (batch.Count > 0)
                    {
                        foreach (var req in batch)
                        {
                            UsageTrackingStorage.AppendSession(
                                req.AppName,
                                req.FirstPid,
                                req.Path,
                                req.Start,
                                req.End,
                                req.PeakCpu,
                                req.AvgCpu,
                                req.PeakMem,
                                req.AvgMem,
                                req.IsIdle);
                        }
                    }
                    else
                    {
                        // No items, wait a bit
                        await Task.Delay(100, _writeCts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    TaskCoreLogger.Errors.BackgroundWriter($"Background writer error: {ex.Message}");
                    try
                    {
                        await Task.Delay(1000).ConfigureAwait(false);
                    }
                    catch { break; }
                }
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                case SessionSwitchReason.SessionLogoff:
                case SessionSwitchReason.RemoteDisconnect:
                    _isSessionLocked = true;
                    break;
                case SessionSwitchReason.SessionUnlock:
                case SessionSwitchReason.SessionLogon:
                case SessionSwitchReason.RemoteConnect:
                    _isSessionLocked = false;
                    _lastInputTime = DateTime.Now;
                    break;
            }
        }

        private bool IsSystemIdle()
        {
            if (_isSessionLocked)
                return true;

            // Check for user input inactivity using GetLastInputInfo
            var idleTime = GetIdleTime();
            return idleTime > _idleThreshold;
        }

        private TimeSpan GetIdleTime()
        {
            try
            {
                var lastInputInfo = new NativeMethods.LastInputInfo();
                lastInputInfo.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(lastInputInfo);
                
                if (NativeMethods.GetLastInputInfo(ref lastInputInfo))
                {
                    var idleTicks = Environment.TickCount - lastInputInfo.dwTime;
                    return TimeSpan.FromMilliseconds(idleTicks);
                }
            }
            catch { }
            
            return TimeSpan.Zero;
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            public struct LastInputInfo
            {
                public uint cbSize;
                public uint dwTime;
            }

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool GetLastInputInfo(ref LastInputInfo plii);
        }

        
        public static List<SessionRecord> GetDailySessions(DateOnly date, bool mergeOverlappingSessions = true)
        {
            return UsageTrackingStorage.GetDailySessions(date, mergeOverlappingSessions);
        }

        public static string GetBaseDirectory()
        {
            return UsageTrackingStorage.BaseDirectory;
        }

        public static void CleanupOldFiles(int daysToKeep = 90)
        {
            UsageTrackingStorage.CleanupOldFiles(daysToKeep);
        }

        public static string ExportToCsv(DateOnly startDate, DateOnly endDate)
        {
            return UsageTrackingStorage.ExportToCsv(startDate, endDate);
        }

        public static string ExportToJson(DateOnly startDate, DateOnly endDate)
        {
            return UsageTrackingStorage.ExportToJson(startDate, endDate);
        }

        public static bool ValidateAndRepairFile(DateOnly date)
        {
            return UsageTrackingStorage.ValidateAndRepairFile(date);
        }

        // Get currently running apps for real-time display (filtered based on user settings)
        public List<(string appName, int firstPid, string path, DateTime startTime, double peakCpu, double avgCpu, double peakMem, double avgMem)> GetCurrentlyRunning()
        {
            var settings = AppSettingsService.Load();
            var filterMode = settings.ProcessFilterMode;
            var selectedProcesses = settings.SelectedProcesses ?? new List<string>();

            // DEBUG
            UsageDebugLogger.LogFilter($"GetCurrentlyRunning called - FilterMode={filterMode}, Selected={string.Join(",", selectedProcesses)}, Tracked={string.Join(",", _trackedApps.Keys)}");

            lock (_lock)
            {
                var result = new List<(string, int, string, DateTime, double, double, double, double)>();
                foreach (var kvp in _trackedApps)
                {
                    var appName = kvp.Key;
                    
                    // Apply filter based on mode
                    if (filterMode == 2) // SelectedOnly
                    {
                        // Only show if in user's selected list
                        if (!selectedProcesses.Contains(appName, StringComparer.OrdinalIgnoreCase))
                        {
                            // DEBUG
                            UsageDebugLogger.LogFilter($"SKIPPED {appName} - not in selected list");
                            continue;
                        }
                    }
                    else if (filterMode == 1) // ExcludeWindows (default)
                    {
                        // Skip system processes
                        if (ProcessFilter.IsSystemAppName(appName))
                        {
                            // DEBUG
                            UsageDebugLogger.LogFilter($"SKIPPED {appName} - is system app");
                            continue;
                        }
                    }
                    // mode 0 (All) - no filtering
                    
                    // DEBUG
                    UsageDebugLogger.LogFilter($"ADDED {appName} to result");

                    var (start, path, pids) = kvp.Value;
                    var firstPid = pids.FirstOrDefault();
                    
                    _sessionTracker.GetMetricsForApp(appName, out var peakCpu, out var avgCpu, out var peakMem, out var avgMem);
                    
                    result.Add((appName, firstPid, path, start, peakCpu, avgCpu, peakMem, avgMem));
                }
                return result;
            }
        }

        private void KickTick()
        {
            if (Interlocked.Exchange(ref _tickRunning, 1) == 1)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    TickCore();
                }
                finally
                {
                    Interlocked.Exchange(ref _tickRunning, 0);
                }
            });
        }

        private void TickCore()
        {
            try
            {
                // Load settings to check filter mode
                var settings = AppSettingsService.Load();
                var filterMode = settings.ProcessFilterMode;
                var selectedProcesses = settings.SelectedProcesses ?? new List<string>();

                var (allProcesses, current) = UsageProcessEnumerator.GetCurrentProcessMap(
                    filterMode,
                    selectedProcesses,
                    ProcessFilter.IsSystemProcess);

                // DEBUG: Log what processes are being tracked vs filtered
                UsageDebugLogger.LogTick("=== TICK ===");
                UsageDebugLogger.LogTick($"Total processes: {allProcesses.Length}");
                UsageDebugLogger.LogTick($"Non-system processes: {current.Count}");
                UsageDebugLogger.LogTick($"Currently tracked apps: {string.Join(", ", _trackedApps.Keys)}");

                var userApps = current.Values.Distinct().Where(n =>
                    !n.StartsWith("svchost", StringComparison.OrdinalIgnoreCase) &&
                    !n.StartsWith("csrss", StringComparison.OrdinalIgnoreCase) &&
                    !n.StartsWith("dwm", StringComparison.OrdinalIgnoreCase));
                UsageDebugLogger.LogTick($"User apps detected: {string.Join(", ", userApps.Take(10))}");

                // Group current processes by app name
                var currentApps = current.Values.Distinct().ToHashSet();

                // Check if system is idle/locked
                var isSystemIdle = IsSystemIdle();

                lock (_lock)
                {
                    var endedSessions = _sessionTracker.ProcessTick(currentApps, current, _metricsSampler, isSystemIdle);

                    // Queue ended sessions for async background persistence
                    foreach (var session in endedSessions)
                    {
                        _writeQueue.Enqueue(new SessionWriteRequest
                        {
                            AppName = session.AppName,
                            FirstPid = session.FirstPid,
                            Path = session.Path,
                            Start = session.Start,
                            End = session.End,
                            PeakCpu = session.PeakCpu,
                            AvgCpu = session.AvgCpu,
                            PeakMem = session.PeakMem,
                            AvgMem = session.AvgMem,
                            IsIdle = session.IsIdle
                        });

                        // Debug log
                        UsageDebugLogger.LogTrack($"END {session.AppName} (App session) after {session.MissCount} misses, duration {(session.End - session.Start).TotalSeconds:F0}s, PIDs seen: {session.PidsSeen}");
                    }
                }
            }
            catch (Exception ex)
            {
                TaskCoreLogger.Errors.BackgroundWriter($"Tick error: {ex.Message}");
            }
        }

        private bool _disposed;

        ~UsageTrackingService()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                _timer?.Stop();
                _writeCts?.Cancel();
                
                try
                {
                    _backgroundWriter?.Wait(TimeSpan.FromSeconds(5));
                }
                catch { }

                // Write any remaining queued sessions
                while (_writeQueue.TryDequeue(out var request))
                {
                    try
                    {
                        UsageTrackingStorage.AppendSession(
                            request.AppName,
                            request.FirstPid,
                            request.Path,
                            request.Start,
                            request.End,
                            request.PeakCpu,
                            request.AvgCpu,
                            request.PeakMem,
                            request.AvgMem,
                            request.IsIdle);
                    }
                    catch { }
                }
            }

            // Save running sessions as completed on shutdown (run even in finalizer)
            SaveRunningSessionsOnShutdown();

            // Delete active sessions file on graceful shutdown (since we saved them properly)
            if (disposing)
            {
                UsageTrackingStorage.DeleteActiveSessions();
            }

            SystemEvents.SessionSwitch -= OnSessionSwitch;
        }

        private void SaveRunningSessionsOnShutdown()
        {
            try
            {
                var shutdownTime = DateTime.Now;
                
                TaskCoreLogger.Lifecycle.DisposeCalled();
                TaskCoreLogger.Info("shutdown", $"Starting session save at {shutdownTime:HH:mm:ss}");
                
                lock (_lock)
                {
                    TaskCoreLogger.Info("shutdown", $"Tracked apps count: {_trackedApps.Count}");
                    
                    foreach (var kvp in _trackedApps)
                    {
                        var appName = kvp.Key;
                        var (startTime, path, pids) = kvp.Value;
                        
                        _sessionTracker.GetMetricsForApp(appName, out var peakCpu, out var avgCpu, out var peakMem, out var avgMem);
                        
                        TaskCoreLogger.SessionTracking.SessionEnded(appName, shutdownTime - startTime);
                        
                        UsageTrackingStorage.AppendSession(
                            appName,
                            pids.FirstOrDefault(),
                            path,
                            startTime,
                            shutdownTime,
                            peakCpu,
                            avgCpu,
                            peakMem,
                            avgMem,
                            false);
                    }
                    
                    TaskCoreLogger.Info("shutdown", $"Save complete, saved {_trackedApps.Count} sessions");
                }
                
                TaskCoreLogger.Lifecycle.GracefulShutdown();
            }
            catch (Exception ex)
            {
                TaskCoreLogger.Error("shutdown", $"ERROR during shutdown save: {ex.Message}");
            }
        }
    }
}
