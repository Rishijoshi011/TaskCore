using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using TaskCore.Helpers;
using TaskCore.Models;

namespace TaskCore.Services
{
    public class ProcessService
    {
        // CPU deltas per PID: (lastTotalProcessorTime, lastSampleTimeUtc)
        private readonly Dictionary<int, (TimeSpan lastCpuTime, DateTime lastSampleUtc)> _cpuSamples = new();

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

        public List<ProcessGroup> GetGroupedProcesses()
        {
            var groups = new Dictionary<string, ProcessGroup>(StringComparer.OrdinalIgnoreCase);
            var activePids = new HashSet<int>();

            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch
            {
                processes = Array.Empty<Process>();
            }

            foreach (var proc in processes)
            {
                try
                {
                    activePids.Add(proc.Id);

                    // Skip inaccessible processes early to avoid exceptions
                    if (!IsProcessAccessible(proc))
                        continue;

                    // Name
                    string name = proc.ProcessName + ".exe";

                    var groupKey = name;
                    var groupDisplayName = name;
                    if (string.Equals(name, "msedgewebview2.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        groupKey = $"{name}:{proc.Id}";
                        groupDisplayName = $"{name} ({proc.Id})";
                    }

                    // CPU + Memory
                    double cpu = GetCpuUsage(proc);
                    double memMb = proc.WorkingSet64 / (1024.0 * 1024.0);

                    // Path (safe)
                    string path = GetSafeProcessPath(proc);

                    // Build item
                    var item = new ProcessItem
                    {
                        Name = name,
                        PID = proc.Id,
                        CpuUsage = Math.Round(cpu, 1),
                        MemoryMB = Math.Round(memMb, 1),
                        Path = path,
                        Icon = IconService.GetIconForPath(path)
                    };

                    // Add or get group
                    if (!groups.TryGetValue(groupKey, out var group))
                    {
                        group = new ProcessGroup
                        {
                            GroupName = groupDisplayName,
                            Items = new BulkObservableCollection<ProcessItem>(),
                            Icon = item.Icon
                        };
                        groups[groupKey] = group;
                    }

                    group.Items.Add(item);
                }
                catch (Win32Exception)
                {
                    continue;
                }
                catch
                {
                    // Inaccessible system processes → ignore
                }
                finally
                {
                    // CRITICAL: Always dispose Process handle to prevent kernel handle leaks
                    try { proc.Dispose(); } catch { }
                }
            }

            // SUM CPU + Memory for parent rows
            foreach (var g in groups.Values)
            {
                g.TotalCpu = Math.Round(g.Items.Sum(p => p.CpuUsage), 1);
                g.TotalMemory = Math.Round(g.Items.Sum(p => p.MemoryMB), 1);
            }

            CleanupDeadCpuSamples(activePids);

            // Sort by CPU first, then RAM
            return groups.Values
                         .OrderByDescending(g => g.TotalCpu)
                         .ThenByDescending(g => g.TotalMemory)
                         .ToList();
        }

        private string GetSafeProcessPath(Process proc)
        {
            try
            {
                return proc.MainModule?.FileName ?? "";
            }
            catch (Win32Exception)
            {
                return ""; // access denied → safe ignore
            }
            catch
            {
                return "";
            }
        }

        private double GetCpuUsage(Process proc)
        {
            try
            {
                var now = DateTime.UtcNow;
                var currentCpuTime = proc.TotalProcessorTime;

                if (_cpuSamples.TryGetValue(proc.Id, out var prevSample))
                {
                    var timeDeltaMs = (now - prevSample.lastSampleUtc).TotalMilliseconds;
                    var cpuDeltaMs = (currentCpuTime - prevSample.lastCpuTime).TotalMilliseconds;

                    _cpuSamples[proc.Id] = (currentCpuTime, now);

                    if (timeDeltaMs <= 0) return 0.0;

                    var pct = (cpuDeltaMs / timeDeltaMs) * 100.0 / Environment.ProcessorCount;
                    return Math.Clamp(pct, 0.0, 100.0);
                }

                _cpuSamples[proc.Id] = (currentCpuTime, now);
                return 0.0;
            }
            catch (Win32Exception)
            {
                return 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private void CleanupDeadCpuSamples(HashSet<int> activePids)
        {
            var deadPids = _cpuSamples.Keys.Where(pid => !activePids.Contains(pid)).ToList();
            foreach (var pid in deadPids)
            {
                _cpuSamples.Remove(pid);
            }
        }
    }
}
