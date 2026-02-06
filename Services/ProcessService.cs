using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TaskCore.Models;

namespace TaskCore.Services
{
    public class ProcessService
    {
        private readonly Dictionary<int, TimeSpan> _lastCpuTime = new();
        private readonly Dictionary<int, DateTime> _lastSampleTime = new();

        public List<ProcessInfo> GetProcesses()
        {
            var result = new List<ProcessInfo>();
            var now = DateTime.UtcNow;
            var cpuCount = Environment.ProcessorCount;

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    // ⚠️ These are the 2 most common crash points
                    var totalCpu = process.TotalProcessorTime;
                    var memoryMb = process.WorkingSet64 / 1024f / 1024f;

                    float cpuUsage = 0;

                    if (_lastCpuTime.TryGetValue(process.Id, out var lastCpu) &&
                        _lastSampleTime.TryGetValue(process.Id, out var lastTime))
                    {
                        var cpuDelta = (totalCpu - lastCpu).TotalMilliseconds;
                        var timeDelta = (now - lastTime).TotalMilliseconds;

                        if (timeDelta > 250) // 🔥 throttle CPU calc
                        {
                            cpuUsage = (float)(cpuDelta / (timeDelta * cpuCount) * 100);
                        }
                    }

                    _lastCpuTime[process.Id] = totalCpu;
                    _lastSampleTime[process.Id] = now;

                    result.Add(new ProcessInfo
                    {
                        Name = process.ProcessName,
                        Id = process.Id,
                        Memory = memoryMb,
                        Cpu = Math.Clamp(cpuUsage, 0, 100)
                    });
                }
                catch
                {
                    // ✔️ silently skip system / exited / protected processes
                }
            }

            CleanupDeadProcesses(result);
            return result;
        }

        private void CleanupDeadProcesses(List<ProcessInfo> processes)
        {
            var alive = processes.Select(p => p.Id).ToHashSet();

            foreach (var id in _lastCpuTime.Keys.Where(id => !alive.Contains(id)).ToList())
            {
                _lastCpuTime.Remove(id);
                _lastSampleTime.Remove(id);
            }
        }

        public void KillProcess(int pid)
        {
            try
            {
                Process.GetProcessById(pid).Kill();
            }
            catch
            {
                // ignore access denied / already exited
            }
        }
    }
}
