using System;

namespace TaskCore.ViewModels
{
    public sealed class SessionSummaryItem
    {
        public string AppName { get; set; } = string.Empty;
        public int SessionCount { get; set; }
        public double LongestSessionSeconds { get; set; }
        public double PeakCpuPercent { get; set; }
        public double AverageCpuPercent { get; set; }
        public double PeakMemoryMb { get; set; }
        public double AverageMemoryMb { get; set; }

        public string LongestSessionDisplay => TimeSpan.FromSeconds(Math.Max(0, LongestSessionSeconds)).ToString(@"hh\:mm\:ss");
    }
}
