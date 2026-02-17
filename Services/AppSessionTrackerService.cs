using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using TaskCore.Models;

namespace TaskCore.Services
{
    public sealed class AppSessionTrackerService
    {
        public static AppSessionTrackerService Instance { get; } = new AppSessionTrackerService();

        private readonly object _lock = new();

        private readonly DispatcherTimer _timer;
        private int _tickRunning;

        private readonly Dictionary<int, ActiveSession> _activeByPid = new();

        private readonly HashSet<string> _ignoredNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "System",
            "Idle",
            "Registry",
            "svchost",
            "csrss",
            "wininit",
            "services",
            "lsass",
            "smss",
            "winlogon",
            "dwm"
        };

        private readonly string[] _ignoredPathPrefixes =
        {
            @"C:\\Windows\\System32\\",
            @"C:\\Windows\\SysWOW64\\",
            @"C:\\Windows\\"
        };

        private AppSessionTrackerService()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RefreshIntervalService.CurrentMs) };
            _timer.Tick += (_, __) => Tick();
            _timer.Start();

            try
            {
                RefreshIntervalService.RefreshIntervalChanged += OnRefreshIntervalChanged;
            }
            catch
            {
            }
        }

        private void OnRefreshIntervalChanged(int ms)
        {
            try
            {
                _timer.Interval = TimeSpan.FromMilliseconds(RefreshIntervalService.Normalize(ms));
            }
            catch
            {
            }
        }

        private void Tick()
        {
            if (Interlocked.Exchange(ref _tickRunning, 1) == 1)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    EvaluateOnce();
                }
                finally
                {
                    Interlocked.Exchange(ref _tickRunning, 0);
                }
            });
        }

        private void EvaluateOnce()
        {
            Dictionary<int, ProcessSnapshot> snapshots;

            try
            {
                snapshots = TakeSnapshots();
            }
            catch
            {
                return;
            }

            lock (_lock)
            {
                // Start/update sessions
                foreach (var kv in snapshots)
                {
                    var pid = kv.Key;
                    var snap = kv.Value;

                    if (!_activeByPid.TryGetValue(pid, out var session))
                    {
                        session = new ActiveSession
                        {
                            AppName = snap.AppName,
                            ProcessId = pid,
                            ProcessPath = snap.Path,
                            StartLocal = DateTime.Now,
                            LastSeenLocal = DateTime.Now
                        };

                        _activeByPid[pid] = session;
                    }

                    session.LastSeenLocal = DateTime.Now;
                    session.SampleCount++;

                    session.CpuSum += snap.CpuPercent;
                    if (snap.CpuPercent > session.PeakCpu) session.PeakCpu = snap.CpuPercent;

                    session.MemSum += snap.MemoryMb;
                    if (snap.MemoryMb > session.PeakMem) session.PeakMem = snap.MemoryMb;

                    // GPU not implemented yet (kept null)
                }

                // Stop sessions which disappeared
                var endedPids = _activeByPid.Keys.Where(pid => !snapshots.ContainsKey(pid)).ToList();
                foreach (var pid in endedPids)
                {
                    var session = _activeByPid[pid];
                    _activeByPid.Remove(pid);

                    FinalizeAndPersist(session);
                }
            }
        }

        private void FinalizeAndPersist(ActiveSession s)
        {
            var end = DateTime.Now;
            var duration = end - s.StartLocal;

            var avgCpu = s.SampleCount > 0 ? (s.CpuSum / s.SampleCount) : 0;
            var avgMem = s.SampleCount > 0 ? (s.MemSum / s.SampleCount) : 0;

            var record = new AppSessionRecord
            {
                AppName = s.AppName,
                ProcessId = s.ProcessId,
                ProcessPath = s.ProcessPath,
                StartTimeLocal = s.StartLocal,
                EndTimeLocal = end,
                DurationSeconds = Math.Max(0, duration.TotalSeconds),
                PeakCpuPercent = Math.Round(s.PeakCpu, 1),
                AverageCpuPercent = Math.Round(avgCpu, 1),
                PeakMemoryMb = Math.Round(s.PeakMem, 1),
                AverageMemoryMb = Math.Round(avgMem, 1),
                PeakGpuPercent = null,
                AverageGpuPercent = null,
                Crashed = s.Crashed,
                SampleCount = s.SampleCount
            };

            AppSessionLogService.AppendSession(record);
        }

        private Dictionary<int, ProcessSnapshot> TakeSnapshots()
        {
            var dict = new Dictionary<int, ProcessSnapshot>();

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    // Skip inaccessible processes early.
                    if (!IsProcessAccessible(proc))
                        continue;

                    var name = proc.ProcessName;
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (_ignoredNames.Contains(name))
                        continue;

                    string path = GetSafeProcessPath(proc);
                    if (ShouldIgnorePath(path))
                        continue;

                    if (!LooksLikeUserAppPath(path))
                        continue;

                    var cpu = GetCpuUsage(proc);
                    var mem = proc.WorkingSet64 / (1024.0 * 1024.0);

                    dict[proc.Id] = new ProcessSnapshot
                    {
                        AppName = name + ".exe",
                        Path = path,
                        CpuPercent = cpu,
                        MemoryMb = mem
                    };
                }
                catch
                {
                }
            }

            CleanupDeadCounters();
            return dict;
        }

        private static bool IsProcessAccessible(Process proc)
        {
            try
            {
                _ = proc.Handle;
                return true;
            }
            catch (Win32Exception)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static string GetSafeProcessPath(Process proc)
        {
            try
            {
                return proc.MainModule?.FileName ?? string.Empty;
            }
            catch (Win32Exception)
            {
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool ShouldIgnorePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return true;

            for (int i = 0; i < _ignoredPathPrefixes.Length; i++)
            {
                if (path.StartsWith(_ignoredPathPrefixes[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool LooksLikeUserAppPath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return false;

                var norm = path.Replace('/', '\\');

                if (norm.StartsWith(@"C:\\Program Files\\", StringComparison.OrdinalIgnoreCase)) return true;
                if (norm.StartsWith(@"C:\\Program Files (x86)\\", StringComparison.OrdinalIgnoreCase)) return true;

                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).Replace('/', '\\');
                var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).Replace('/', '\\');

                if (!string.IsNullOrWhiteSpace(localAppData) && norm.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.IsNullOrWhiteSpace(roamingAppData) && norm.StartsWith(roamingAppData, StringComparison.OrdinalIgnoreCase)) return true;

                // Custom installed paths: if it exists and is not under Windows directory, allow.
                // This catches e.g. D:\Apps\..., portable apps, etc.
                if (File.Exists(norm) && !norm.StartsWith(@"C:\\Windows\\", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
            }

            return false;
        }

        // CPU counters per PID (needs PID->instance mapping because PerfCounter instance names are not unique per process name)
        private readonly PerformanceCounterCategory _processCategory = new("Process");
        private readonly Dictionary<int, string> _instanceNameByPid = new();
        private readonly Dictionary<int, PerformanceCounter> _cpuCountersByPid = new();

        private double GetCpuUsage(Process proc)
        {
            try
            {
                if (!_instanceNameByPid.TryGetValue(proc.Id, out var instanceName) || string.IsNullOrWhiteSpace(instanceName))
                {
                    instanceName = TryResolveProcessInstanceName(proc.Id);
                    if (string.IsNullOrWhiteSpace(instanceName))
                        return 0;

                    _instanceNameByPid[proc.Id] = instanceName;
                }

                if (!_cpuCountersByPid.TryGetValue(proc.Id, out var counter))
                {
                    counter = new PerformanceCounter("Process", "% Processor Time", instanceName, true);
                    counter.NextValue(); // warm-up
                    _cpuCountersByPid[proc.Id] = counter;
                    return 0;
                }

                return counter.NextValue() / Environment.ProcessorCount;
            }
            catch (Win32Exception)
            {
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private string? TryResolveProcessInstanceName(int pid)
        {
            try
            {
                // Map PID -> perf counter instance by scanning instances and comparing "ID Process" counter.
                // This is required because instance names can be like chrome, chrome#1, chrome#2, ...
                var instances = _processCategory.GetInstanceNames();
                for (int i = 0; i < instances.Length; i++)
                {
                    var inst = instances[i];
                    try
                    {
                        using var idCounter = new PerformanceCounter("Process", "ID Process", inst, true);
                        var val = (int)idCounter.RawValue;
                        if (val == pid)
                            return inst;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private void CleanupDeadCounters()
        {
            var dead = _cpuCountersByPid.Keys
                .Where(pid =>
                {
                    try { _ = Process.GetProcessById(pid); return false; }
                    catch { return true; }
                })
                .ToList();

            foreach (var pid in dead)
            {
                try { _cpuCountersByPid[pid]?.Dispose(); } catch { }
                _cpuCountersByPid.Remove(pid);
                _instanceNameByPid.Remove(pid);
            }
        }

        private sealed class ActiveSession
        {
            public required string AppName { get; init; }
            public int ProcessId { get; init; }
            public string ProcessPath { get; init; } = string.Empty;

            public DateTime StartLocal { get; init; }
            public DateTime LastSeenLocal { get; set; }

            public double PeakCpu { get; set; }
            public double CpuSum { get; set; }

            public double PeakMem { get; set; }
            public double MemSum { get; set; }

            public bool? Crashed { get; set; }
            public int SampleCount { get; set; }
        }

        private sealed class ProcessSnapshot
        {
            public string AppName { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public double CpuPercent { get; set; }
            public double MemoryMb { get; set; }
        }
    }
}
