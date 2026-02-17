using System;

namespace TaskCore.ViewModels
{
    public sealed class SessionRecordItem
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

        public string DurationDisplay => TimeSpan.FromSeconds(Math.Max(0, DurationSeconds)).ToString(@"hh\:mm\:ss");
        public string StartDisplay => StartTimeLocal.ToString("HH:mm:ss");
        public string EndDisplay => EndTimeLocal.ToString("HH:mm:ss");
    }
}
