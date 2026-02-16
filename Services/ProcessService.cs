using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using TaskCore.Models;

namespace TaskCore.Services
{
    public class ProcessService
    {
        // CPU counters per PID
        private readonly Dictionary<int, PerformanceCounter> cpuCounters = new();

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

            foreach (var proc in Process.GetProcesses())
            {
                // Skip inaccessible processes early to avoid exceptions
                if (!IsProcessAccessible(proc))
                    continue;

                try
                {
                    // Name
                    string name = proc.ProcessName + ".exe";

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
                    if (!groups.TryGetValue(name, out var group))
                    {
                        group = new ProcessGroup
                        {
                            GroupName = name,
                            Items = new ObservableCollection<ProcessItem>(),
                            Icon = item.Icon
                        };
                        groups[name] = group;
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
            }

            // SUM CPU + Memory for parent rows
            foreach (var g in groups.Values)
            {
                g.TotalCpu = Math.Round(g.Items.Sum(p => p.CpuUsage), 1);
                g.TotalMemory = Math.Round(g.Items.Sum(p => p.MemoryMB), 1);
            }

            CleanupDeadCounters();

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
                if (!cpuCounters.TryGetValue(proc.Id, out var counter))
                {
                    counter = new PerformanceCounter("Process", "% Processor Time", proc.ProcessName, true);
                    counter.NextValue(); // warm-up
                    cpuCounters[proc.Id] = counter;
                    // Return 0 for newly created counters (warm-up call)
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


        private void CleanupDeadCounters()
        {
            var dead = cpuCounters.Keys
                .Where(pid =>
                {
                    try { _ = Process.GetProcessById(pid); return false; }
                    catch { return true; }
                })
                .ToList();

            foreach (var pid in dead)
            {
                try { cpuCounters[pid]?.Dispose(); } catch { }
                cpuCounters.Remove(pid);
            }
        }
    }
}
