using System;
using System.Collections.Generic;

namespace TaskCore.Models
{
    public sealed class ProcessMetrics
    {
        public string ProcessName { get; set; } = string.Empty;

        public double CpuUsage { get; set; }

        public double MemoryUsage { get; set; }

        public DateTime StartTime { get; set; }

        public double RuntimeMinutes { get; set; }

        public int RestartCount { get; set; }

        public List<double> HistoricalCpuUsage { get; set; } = new();

        public List<double> HistoricalMemoryUsage { get; set; } = new();
    }
}
