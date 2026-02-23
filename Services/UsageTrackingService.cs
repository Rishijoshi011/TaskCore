using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TaskCore.Services
{
    public partial class UsageTrackingService
    {
        private static readonly Lazy<UsageTrackingService> _instance = new(() => new UsageTrackingService());
        public static UsageTrackingService Instance => _instance.Value;

        private readonly DispatcherTimer _timer;
        private readonly UsageMetricsSampler _metricsSampler = new();
        private readonly UsageSessionTracker _sessionTracker = new();
        private Dictionary<string, (DateTime start, string path, HashSet<int> pids)> _trackedApps => _sessionTracker.TrackedApps as Dictionary<string, (DateTime start, string path, HashSet<int> pids)>;
        private readonly object _lock = new();
        private int _tickRunning;

        private UsageTrackingService()
        {
            try
            {
                UsageTrackingStorage.EnsureDirectory();
                UsageTrackingStorage.CleanupOldFiles(); // Clean up old files on startup
            }
            catch { }

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, __) => KickTick();
            _timer.Start();
        }

        
        public static List<SessionRecord> GetDailySessions(DateOnly date)
        {
            return UsageTrackingStorage.GetDailySessions(date);
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
            try
            {
                File.AppendAllText(Path.Combine(UsageTrackingStorage.BaseDirectory, "debug-filter.txt"),
                    $"[{DateTime.Now:HH:mm:ss}] GetCurrentlyRunning called - FilterMode={filterMode}, Selected={string.Join(",", selectedProcesses)}, Tracked={string.Join(",", _trackedApps.Keys)}\n");
            }
            catch { }

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
                            try
                            {
                                File.AppendAllText(Path.Combine(UsageTrackingStorage.BaseDirectory, "debug-filter.txt"),
                                    $"[{DateTime.Now:HH:mm:ss}] SKIPPED {appName} - not in selected list\n");
                            }
                            catch { }
                            continue;
                        }
                    }
                    else if (filterMode == 1) // ExcludeWindows (default)
                    {
                        // Skip system processes
                        if (ProcessFilter.IsSystemAppName(appName))
                        {
                            // DEBUG
                            try
                            {
                                File.AppendAllText(Path.Combine(UsageTrackingStorage.BaseDirectory, "debug-filter.txt"),
                                    $"[{DateTime.Now:HH:mm:ss}] SKIPPED {appName} - is system app\n");
                            }
                            catch { }
                            continue;
                        }
                    }
                    // mode 0 (All) - no filtering
                    
                    // DEBUG
                    try
                    {
                        File.AppendAllText(Path.Combine(UsageTrackingStorage.BaseDirectory, "debug-filter.txt"),
                            $"[{DateTime.Now:HH:mm:ss}] ADDED {appName} to result\n");
                    }
                    catch { }

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
                try
                {
                    var debugLines = new List<string>();
                    debugLines.Add($"[{DateTime.Now:HH:mm:ss}] === TICK ===");
                    debugLines.Add($"Total processes: {allProcesses.Length}");
                    debugLines.Add($"Non-system processes: {current.Count}");
                    debugLines.Add($"Currently tracked apps: {string.Join(", ", _trackedApps.Keys)}");
                    
                    var userApps = current.Values.Distinct().Where(n => 
                        !n.StartsWith("svchost", StringComparison.OrdinalIgnoreCase) &&
                        !n.StartsWith("csrss", StringComparison.OrdinalIgnoreCase) &&
                        !n.StartsWith("dwm", StringComparison.OrdinalIgnoreCase));
                    debugLines.Add($"User apps detected: {string.Join(", ", userApps.Take(10))}");
                    
                    File.AppendAllLines(Path.Combine(UsageTrackingStorage.BaseDirectory, "debug-tick.txt"), debugLines);
                }
                catch { }

                // Group current processes by app name
                var currentApps = current.Values.Distinct().ToHashSet();

                lock (_lock)
                {
                    var endedSessions = _sessionTracker.ProcessTick(currentApps, current, _metricsSampler);

                    // Write ended sessions to storage
                    foreach (var session in endedSessions)
                    {
                        UsageTrackingStorage.AppendSession(
                            session.AppName,
                            session.FirstPid,
                            session.Path,
                            session.Start,
                            session.End,
                            session.PeakCpu,
                            session.AvgCpu,
                            session.PeakMem,
                            session.AvgMem);

                        // Debug log
                        try
                        {
                            UsageTrackingStorage.AppendDebugTrack(
                                $"[{DateTime.Now:HH:mm:ss}] END {session.AppName} (App session) after {session.MissCount} misses, " +
                                $"duration {(session.End - session.Start).TotalSeconds:F0}s, PIDs seen: {session.PidsSeen}\n");
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(UsageTrackingStorage.BaseDirectory, "usage-errors.txt"), $"[{DateTime.Now:HH:mm:ss}] Tick error: {ex.Message}\n");
                }
                catch { }
            }
        }
    }
}
