using System;
using System.Collections.Generic;

namespace TaskCore.Models
{
    public sealed class AppSessionDayLog
    {
        public string SchemaVersion { get; set; } = "1";
        public DateOnly DateLocal { get; set; }
        public List<AppSessionRecord> Sessions { get; set; } = new();
    }
}
