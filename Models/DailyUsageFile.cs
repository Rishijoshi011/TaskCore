using System;
using System.Collections.Generic;

namespace TaskCore.Models
{
    public sealed class DailyUsageFile
    {
        public DateTime Date { get; set; }
        public List<AppUsage> Apps { get; set; } = new();
    }
}
