using System;

namespace TaskCore.Models
{
    public sealed class AppSessionRecord
    {
        public string AppName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string? ProcessPath { get; set; }

        public DateTime StartTimeLocal { get; set; }
        public DateTime EndTimeLocal { get; set; }
        public double DurationSeconds { get; set; }

        public double PeakCpuPercent { get; set; }
        public double AverageCpuPercent { get; set; }

        public double PeakMemoryMb { get; set; }
        public double AverageMemoryMb { get; set; }

        public double? PeakGpuPercent { get; set; }
        public double? AverageGpuPercent { get; set; }

        public bool? Crashed { get; set; }
        public int SampleCount { get; set; }
    }
}
