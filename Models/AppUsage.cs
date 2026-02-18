using System.Collections.Generic;

namespace TaskCore.Models
{
    public sealed class AppUsage
    {
        public string ProcessName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public int LaunchCount { get; set; }
        public double TotalRunTimeSeconds { get; set; }
        public List<AppSession> Sessions { get; set; } = new();
    }
}
