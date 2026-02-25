using System;

namespace TaskCore.Services
{
    public partial class UsageTrackingService
    {
        // Session record for proper deserialization
        public class SessionRecord
        {
            public string app { get; set; } = string.Empty;
            public int processId { get; set; }
            public string processPath { get; set; } = string.Empty;
            public DateTime startTimeUtc { get; set; }
            public DateTime endTimeUtc { get; set; }
            public int durationSeconds { get; set; }
            public double peakCpuPercent { get; set; }
            public double averageCpuPercent { get; set; }
            public double peakMemoryMB { get; set; }
            public double averageMemoryMB { get; set; }
            public bool isIdle { get; set; }  // True if session occurred during idle/locked time
        }
    }
}
