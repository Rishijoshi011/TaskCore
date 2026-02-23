using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TaskCore.Services
{
    internal static class UsageTrackingStorage
    {
        private sealed class AppSummaryData
        {
            public int launchCount { get; set; }
            public int totalRuntimeSeconds { get; set; }
            public List<UsageTrackingService.SessionRecord> sessions { get; set; } = new();
        }

        private sealed class DailySummary
        {
            public string date { get; set; } = string.Empty;
            public Dictionary<string, AppSummaryData> apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        public static string BaseDirectory => Path.Combine(
            AppSettingsService.BaseDirectory,
            "SessionLogs");

        public static void EnsureDirectory()
        {
            Directory.CreateDirectory(BaseDirectory);
        }

        public static void AppendDebugTick(string message)
        {
            try
            {
                File.AppendAllText(Path.Combine(BaseDirectory, "debug-tick.txt"), message);
            }
            catch
            {
            }
        }

        public static void AppendDebugTrack(string message)
        {
            try
            {
                File.AppendAllText(Path.Combine(BaseDirectory, "debug-track.txt"), message);
            }
            catch
            {
            }
        }

        public static void AppendSession(string name, int pid, string? path, DateTime start, DateTime end, double peakCpu = 0, double avgCpu = 0, double peakMem = 0, double avgMem = 0)
        {
            var date = DateOnly.FromDateTime(start);
            var file = Path.Combine(BaseDirectory, $"sessions-{date:yyyy-MM-dd}.jsonl");

            try
            {
                var session = new
                {
                    app = name,
                    processId = pid,
                    processPath = path ?? string.Empty,
                    startTimeUtc = start,
                    endTimeUtc = end,
                    durationSeconds = (int)(end - start).TotalSeconds,
                    peakCpuPercent = Math.Round(peakCpu, 2),
                    averageCpuPercent = Math.Round(avgCpu, 2),
                    peakMemoryMB = Math.Round(peakMem, 2),
                    averageMemoryMB = Math.Round(avgMem, 2)
                };

                var jsonLine = JsonSerializer.Serialize(session) + Environment.NewLine;
                File.AppendAllText(file, jsonLine);
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(BaseDirectory, "errors.txt"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Failed to append session for {name}: {ex.Message}\n");
                }
                catch { }
            }
        }

        public static List<UsageTrackingService.SessionRecord> GetDailySessions(DateOnly date)
        {
            var file = Path.Combine(BaseDirectory, $"sessions-{date:yyyy-MM-dd}.jsonl");
            var legacyFile = Path.Combine(BaseDirectory, $"sessions_{date:yyyy-MM-dd}.json");
            var sessions = new List<UsageTrackingService.SessionRecord>();

            if (!File.Exists(file) && !File.Exists(legacyFile)) return sessions;

            try
            {
                if (File.Exists(file))
                {
                    var lines = File.ReadAllLines(file);
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var session = JsonSerializer.Deserialize<UsageTrackingService.SessionRecord>(line);
                            if (session != null)
                                sessions.Add(session);
                        }
                    }
                }
                else if (File.Exists(legacyFile))
                {
                    var json = File.ReadAllText(legacyFile);
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("Sessions", out var legacySessions) && legacySessions.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var rec in legacySessions.EnumerateArray())
                        {
                            try
                            {
                                var startLocal = rec.TryGetProperty("StartTimeLocal", out var startEl) ? startEl.GetDateTime() : DateTime.MinValue;
                                var endLocal = rec.TryGetProperty("EndTimeLocal", out var endEl) ? endEl.GetDateTime() : DateTime.MinValue;

                                var session = new UsageTrackingService.SessionRecord
                                {
                                    app = rec.TryGetProperty("AppName", out var appEl) ? (appEl.GetString() ?? string.Empty) : string.Empty,
                                    processId = rec.TryGetProperty("ProcessId", out var pidEl) ? pidEl.GetInt32() : 0,
                                    processPath = rec.TryGetProperty("ProcessPath", out var pathEl) ? (pathEl.GetString() ?? string.Empty) : string.Empty,
                                    startTimeUtc = startLocal.Kind == DateTimeKind.Utc ? startLocal : startLocal.ToUniversalTime(),
                                    endTimeUtc = endLocal.Kind == DateTimeKind.Utc ? endLocal : endLocal.ToUniversalTime(),
                                    durationSeconds = rec.TryGetProperty("DurationSeconds", out var durEl) ? (int)Math.Round(durEl.GetDouble()) : 0,
                                    peakCpuPercent = rec.TryGetProperty("PeakCpuPercent", out var peakCpuEl) ? peakCpuEl.GetDouble() : 0.0,
                                    averageCpuPercent = rec.TryGetProperty("AverageCpuPercent", out var avgCpuEl) ? avgCpuEl.GetDouble() : 0.0,
                                    peakMemoryMB = rec.TryGetProperty("PeakMemoryMb", out var peakMemEl) ? peakMemEl.GetDouble() : 0.0,
                                    averageMemoryMB = rec.TryGetProperty("AverageMemoryMb", out var avgMemEl) ? avgMemEl.GetDouble() : 0.0
                                };

                                sessions.Add(session);
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(BaseDirectory, "errors.txt"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Failed to read daily sessions: {ex.Message}\n");
                }
                catch { }
            }

            return sessions;
        }

        private static DailySummary GetDailySummary(DateOnly date)
        {
            var sessions = GetDailySessions(date);
            var apps = new Dictionary<string, AppSummaryData>(StringComparer.OrdinalIgnoreCase);

            foreach (var session in sessions)
            {
                if (!apps.TryGetValue(session.app, out var appData))
                {
                    appData = new AppSummaryData();
                    apps[session.app] = appData;
                }

                appData.launchCount++;
                appData.totalRuntimeSeconds += session.durationSeconds;
                appData.sessions.Add(session);
            }

            return new DailySummary
            {
                date = date.ToString("yyyy-MM-dd"),
                apps = apps
            };
        }

        public static void CleanupOldFiles(int daysToKeep = 90)
        {
            try
            {
                if (!Directory.Exists(BaseDirectory)) return;

                var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                var files = Directory.GetFiles(BaseDirectory, "*.jsonl");

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTime < cutoffDate)
                    {
                        var compressedFile = file + ".gz";
                        if (!File.Exists(compressedFile))
                        {
                            File.Delete(file);
                        }
                    }
                }

                var errorFile = Path.Combine(BaseDirectory, "errors.txt");
                if (File.Exists(errorFile))
                {
                    var errorLines = File.ReadAllLines(errorFile);
                    var recentLines = errorLines.Where(line =>
                    {
                        if (string.IsNullOrWhiteSpace(line)) return false;
                        var lineDateStr = line.Substring(1, 10);
                        return DateTime.TryParse(lineDateStr, out var lineDate) && lineDate > DateTime.Now.AddDays(-30);
                    }).ToArray();
                    File.WriteAllLines(errorFile, recentLines);
                }
            }
            catch (Exception ex)
            {
                File.AppendAllText(Path.Combine(BaseDirectory, "errors.txt"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Cleanup error: {ex.Message}\n");
            }
        }

        public static string ExportToCsv(DateOnly startDate, DateOnly endDate)
        {
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Date,App,Process ID,Start Time,End Time,Duration (seconds),Peak CPU (%),Avg CPU (%),Peak Memory (MB),Avg Memory (MB)");

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                var sessions = GetDailySessions(date);
                foreach (var session in sessions)
                {
                    csv.AppendLine($"{date:yyyy-MM-dd}," +
                        $"\"{session.app}\"," +
                        $"{session.processId}," +
                        $"{session.startTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}," +
                        $"{session.endTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}," +
                        $"{session.durationSeconds}," +
                        $"{session.peakCpuPercent}," +
                        $"{session.averageCpuPercent}," +
                        $"{session.peakMemoryMB}," +
                        $"{session.averageMemoryMB}");
                }
            }

            return csv.ToString();
        }

        public static string ExportToJson(DateOnly startDate, DateOnly endDate)
        {
            var exportData = new Dictionary<string, object>
            {
                ["exportDate"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["dateRange"] = new
                {
                    start = startDate.ToString("yyyy-MM-dd"),
                    end = endDate.ToString("yyyy-MM-dd")
                },
                ["data"] = new Dictionary<string, object>()
            };

            var dataDict = (Dictionary<string, object>)exportData["data"];

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                var summary = GetDailySummary(date);
                if (summary.apps.Any())
                {
                    dataDict[date.ToString("yyyy-MM-dd")] = summary;
                }
            }

            return JsonSerializer.Serialize(exportData, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        public static bool ValidateAndRepairFile(DateOnly date)
        {
            try
            {
                var file = Path.Combine(BaseDirectory, $"sessions-{date:yyyy-MM-dd}.jsonl");
                if (!File.Exists(file)) return true;

                var lines = File.ReadAllLines(file);
                var validLines = new List<string>();

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var session = JsonSerializer.Deserialize<Dictionary<string, object>>(line);
                        if (session != null && session.ContainsKey("app") && session.ContainsKey("processId"))
                        {
                            validLines.Add(line);
                        }
                    }
                    catch
                    {
                    }
                }

                if (validLines.Count != lines.Length)
                {
                    File.WriteAllLines(file, validLines);
                    return true;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
