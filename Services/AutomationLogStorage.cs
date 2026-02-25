using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace TaskCore.Services
{
    internal static class AutomationLogStorage
    {
        private static readonly object _lock = new();

        private static string LogDir => Path.Combine(AppSettingsService.BaseDirectory, "AutomationLogs");

        private static string GetDailyPath(DateTime localDate)
            => Path.Combine(LogDir, $"automation-{localDate:yyyy-MM-dd}.jsonl");

        public static void Append(AutomationLogEvent evt)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(LogDir);

                    var path = GetDailyPath(DateTime.Now.Date);
                    var json = JsonSerializer.Serialize(evt);
                    File.AppendAllText(path, json + Environment.NewLine);
                }
            }
            catch
            {
            }
        }

        public sealed class AutomationLogEvent
        {
            public DateTime timestampUtc { get; set; } = DateTime.UtcNow;
            public string category { get; set; } = string.Empty;
            public string name { get; set; } = string.Empty;
            public string decision { get; set; } = string.Empty;
            public string action { get; set; } = string.Empty;
            public bool success { get; set; } = true;
            public string error { get; set; } = string.Empty;

            public SystemSnapshot? system { get; set; }
            public ProcessSnapshot? process { get; set; }
            public Dictionary<string, string>? tags { get; set; }
        }

        public sealed class SystemSnapshot
        {
            public double totalCpuPercent { get; set; }
            public double availableMemoryMb { get; set; }
        }

        public sealed class ProcessSnapshot
        {
            public int pid { get; set; }
            public string name { get; set; } = string.Empty;
            public string path { get; set; } = string.Empty;
            public double workingSetMb { get; set; }
            public double cpuPercent { get; set; }
            public bool responding { get; set; }
        }

        public static ProcessSnapshot? TrySnapshot(Process p, double cpuPercent = 0)
        {
            try
            {
                return new ProcessSnapshot
                {
                    pid = p.Id,
                    name = p.ProcessName,
                    path = TryGetPath(p),
                    workingSetMb = p.WorkingSet64 / (1024.0 * 1024.0),
                    cpuPercent = cpuPercent,
                    responding = TryIsResponding(p)
                };
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetPath(Process p)
        {
            try
            {
                return p.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryIsResponding(Process p)
        {
            try
            {
                return p.Responding;
            }
            catch
            {
                return true;
            }
        }
    }
}
