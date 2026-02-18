using System;

namespace TaskCore.Models
{
    public sealed class AppSession
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double PeakCpu { get; set; }
        public double AvgCpu { get; set; }
        public double PeakMemoryMB { get; set; }
        public double AvgMemoryMB { get; set; }

        public double DurationSeconds => Math.Max(0, (EndTime - StartTime).TotalSeconds);
    }
}
