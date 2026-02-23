using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TaskCore.Services
{
    internal sealed class UsageMetricsSampler
    {
        private readonly Dictionary<int, (DateTime time, TimeSpan cpu)> _cpuSnapshots = new();

        public double GetCpuUsage(Process process)
        {
            try
            {
                var currentTime = DateTime.Now;
                var currentCpu = process.TotalProcessorTime;
                var pid = process.Id;

                if (_cpuSnapshots.TryGetValue(pid, out var snapshot))
                {
                    var timeDelta = (currentTime - snapshot.time).TotalMilliseconds;
                    var cpuDelta = (currentCpu - snapshot.cpu).TotalMilliseconds;

                    if (timeDelta > 0)
                    {
                        var cpuPercent = (cpuDelta / timeDelta / Environment.ProcessorCount) * 100;
                        _cpuSnapshots[pid] = (currentTime, currentCpu);
                        return Math.Min(Math.Max(cpuPercent, 0), 100);
                    }
                }

                _cpuSnapshots[pid] = (currentTime, currentCpu);
                return 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
