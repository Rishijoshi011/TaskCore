using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace TaskCore.Services
{
    internal sealed class UsageSessionTracker
    {
        // Miss count threshold - increased to 30 seconds for better session continuity
        private const int MissThreshold = 30;
        
        private readonly Dictionary<string, (DateTime start, string path, HashSet<int> pids)> _trackedApps = new();
        private readonly Dictionary<string, (double peakCpu, double totalCpu, double peakMem, double totalMem, int samples)> _appMetrics = new();
        private readonly Dictionary<int, int> _missCountByApp = new();

        private readonly Dictionary<string, bool> _appIdleState = new();  // Track if app was started during idle

        // Session continuation: track recently ended sessions for 60-second window
        private readonly Dictionary<string, (DateTime endTime, double peakCpu, double totalCpu, double peakMem, double totalMem, int samples)> _recentlyEnded = new();
        private const int SessionContinuationWindowSeconds = 60;

        public IReadOnlyDictionary<string, (DateTime start, string path, HashSet<int> pids)> TrackedApps => _trackedApps;

        public List<EndedSession> ProcessTick(
            HashSet<string> currentApps,
            Dictionary<int, string> current,
            UsageMetricsSampler metricsSampler,
            bool isSystemIdle)
        {
            var endedSessions = new List<EndedSession>();

            // Check for apps that have ended (no processes remain)
            foreach (var appName in _trackedApps.Keys.ToList())
            {
                if (currentApps.Contains(appName))
                {
                    // App still has at least one process running - reset miss count
                    _missCountByApp.Remove(appName.GetHashCode());
                    continue;
                }

                // App has no processes - count misses
                var appKey = appName.GetHashCode();
                if (!_missCountByApp.TryGetValue(appKey, out var miss)) miss = 0;
                miss++;
                _missCountByApp[appKey] = miss;

                if (miss < MissThreshold)
                    continue;

                // App session has truly ended - prepare session record
                var (start, path, pids) = _trackedApps[appName];
                var end = DateTime.Now;

                double peakCpu = 0, avgCpu = 0, peakMem = 0, avgMem = 0;
                if (_appMetrics.TryGetValue(appName, out var metric))
                {
                    peakCpu = metric.peakCpu;
                    avgCpu = metric.samples > 0 ? metric.totalCpu / metric.samples : 0;
                    peakMem = metric.peakMem;
                    avgMem = metric.samples > 0 ? metric.totalMem / metric.samples : 0;
                }

                var isIdleSession = _appIdleState.TryGetValue(appName, out var wasIdle) && wasIdle;

                endedSessions.Add(new EndedSession
                {
                    AppName = appName,
                    FirstPid = pids.FirstOrDefault(),
                    Path = path,
                    Start = start,
                    End = end,
                    PeakCpu = peakCpu,
                    AvgCpu = avgCpu,
                    PeakMem = peakMem,
                    AvgMem = avgMem,
                    MissCount = miss,
                    PidsSeen = pids.Count,
                    IsIdle = isIdleSession
                });

                // Store for potential session continuation (within 60 seconds)
                _recentlyEnded[appName] = (end, peakCpu, metric.totalCpu, peakMem, metric.totalMem, metric.samples);

                _trackedApps.Remove(appName);
                _appMetrics.Remove(appName);
                _missCountByApp.Remove(appKey);
                _appIdleState.Remove(appName);
            }

            // Clean up old recently-ended sessions (older than 60 seconds) - do this outside the loop
            var now = DateTime.Now;
            var expired = _recentlyEnded.Where(x => (now - x.Value.endTime).TotalSeconds > SessionContinuationWindowSeconds).Select(x => x.Key).ToList();
            foreach (var key in expired)
                _recentlyEnded.Remove(key);

            // Check for new apps (first time we see any process for this app)
            foreach (var kvp in current)
            {
                var pid = kvp.Key;
                var appName = kvp.Value;

                if (!_trackedApps.ContainsKey(appName))
                {
                    // First time seeing this app - check if it's a continuation
                    string path;
                    try
                    {
                        var p = Process.GetProcessById(pid);
                        path = p.MainModule?.FileName ?? string.Empty;
                    }
                    catch
                    {
                        path = string.Empty;
                    }

                    if (_recentlyEnded.TryGetValue(appName, out var recent))
                    {
                        // Continue the previous session - restore metrics
                        _trackedApps[appName] = (DateTime.Now, path, new HashSet<int> { pid });
                        _appMetrics[appName] = (recent.peakCpu, recent.totalCpu, recent.peakMem, recent.totalMem, recent.samples);
                        _recentlyEnded.Remove(appName);
                    }
                    else
                    {
                        // Brand new session
                        _trackedApps[appName] = (DateTime.Now, path, new HashSet<int> { pid });
                    }
                    
                    _missCountByApp.Remove(appName.GetHashCode());
                    _appIdleState[appName] = isSystemIdle;  // Track if started during idle
                }
                else
                {
                    // App already tracked - add this PID to its set
                    var existing = _trackedApps[appName];
                    existing.pids.Add(pid);
                }

                // Collect metrics for this process and aggregate at app level
                try
                {
                    var process = Process.GetProcessById(pid);
                    var cpuUsage = metricsSampler.GetCpuUsage(process);
                    var memoryMB = process.WorkingSet64 / 1024.0 / 1024.0;

                    if (_appMetrics.TryGetValue(appName, out var existingMetrics))
                    {
                        var newPeakCpu = Math.Max(existingMetrics.peakCpu, cpuUsage);
                        var newPeakMem = Math.Max(existingMetrics.peakMem, memoryMB);
                        _appMetrics[appName] = (newPeakCpu, existingMetrics.totalCpu + cpuUsage, newPeakMem, existingMetrics.totalMem + memoryMB, existingMetrics.samples + 1);
                    }
                    else
                    {
                        _appMetrics[appName] = (cpuUsage, cpuUsage, memoryMB, memoryMB, 1);
                    }
                }
                catch
                {
                    // Process might have ended, skip metrics
                }
            }

            return endedSessions;
        }

        public void GetMetricsForApp(string appName, out double peakCpu, out double avgCpu, out double peakMem, out double avgMem)
        {
            peakCpu = 0;
            avgCpu = 0;
            peakMem = 0;
            avgMem = 0;

            if (_appMetrics.TryGetValue(appName, out var metric))
            {
                peakCpu = metric.peakCpu;
                avgCpu = metric.samples > 0 ? metric.totalCpu / metric.samples : 0;
                peakMem = metric.peakMem;
                avgMem = metric.samples > 0 ? metric.totalMem / metric.samples : 0;
            }
        }
    }

    internal sealed class EndedSession
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
        public int MissCount { get; set; }
        public int PidsSeen { get; set; }
        public bool IsIdle { get; set; }  // True if session occurred during idle/locked time
    }
}
