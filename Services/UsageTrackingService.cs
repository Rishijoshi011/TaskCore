using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TaskCore.Services
{
    public class UsageTrackingService
    {
        private static readonly Lazy<UsageTrackingService> _instance = new(() => new UsageTrackingService());
        public static UsageTrackingService Instance => _instance.Value;

        private readonly DispatcherTimer _timer;
        private readonly Dictionary<int, (DateTime start, string name, string path)> _tracked = new();
        private readonly Dictionary<int, (double peakCpu, double totalCpu, double peakMem, double totalMem, int samples)> _metrics = new();
        private readonly object _lock = new();
        private int _tickRunning;

        private UsageTrackingService()
        {
            try
            {
                UsageStorage.EnsureDirectory();
                UsageStorage.CleanupOldFiles(); // Clean up old files on startup
            }
            catch { }

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, __) => KickTick();
            _timer.Start();
        }

        // Public methods for UI
        public static List<Dictionary<string, object>> GetDailySessions(DateOnly date)
        {
            return UsageStorage.GetDailySessions(date);
        }

        public static string GetBaseDirectory()
        {
            return UsageStorage.BaseDirectory;
        }

        public static void CleanupOldFiles(int daysToKeep = 90)
        {
            UsageStorage.CleanupOldFiles(daysToKeep);
        }

        public static string ExportToCsv(DateOnly startDate, DateOnly endDate)
        {
            return UsageStorage.ExportToCsv(startDate, endDate);
        }

        public static string ExportToJson(DateOnly startDate, DateOnly endDate)
        {
            return UsageStorage.ExportToJson(startDate, endDate);
        }

        public static bool ValidateAndRepairFile(DateOnly date)
        {
            return UsageStorage.ValidateAndRepairFile(date);
        }

        private void KickTick()
        {
            if (Interlocked.Exchange(ref _tickRunning, 1) == 1)
                return;

            _ = Task.Run(() =>
            {
                try
                {
                    TickCore();
                }
                finally
                {
                    Interlocked.Exchange(ref _tickRunning, 0);
                }
            });
        }

        private void TickCore()
        {
            try
            {
                var current = Process.GetProcesses()
                    .Where(p => !IsSystemProcess(p))
                    .ToDictionary(p => p.Id, p => p.ProcessName);

                lock (_lock)
                {
                    var ended = _tracked.Keys.Where(pid => !current.ContainsKey(pid)).ToList();

                    foreach (var pid in ended)
                    {
                        var (start, name, path) = _tracked[pid];
                        var end = DateTime.Now;

                        // Get metrics if available
                        double peakCpu = 0, avgCpu = 0, peakMem = 0, avgMem = 0;
                        if (_metrics.TryGetValue(pid, out var metric))
                        {
                            peakCpu = metric.peakCpu;
                            avgCpu = metric.samples > 0 ? metric.totalCpu / metric.samples : 0;
                            peakMem = metric.peakMem;
                            avgMem = metric.samples > 0 ? metric.totalMem / metric.samples : 0;
                        }

                        UsageStorage.AppendSession(name, pid, path, start, end, peakCpu, avgCpu, peakMem, avgMem);
                        _tracked.Remove(pid);
                        _metrics.Remove(pid);
                    }

                    foreach (var kvp in current)
                    {
                        if (!_tracked.ContainsKey(kvp.Key))
                        {
                            string path;
                            try
                            {
                                var p = Process.GetProcessById(kvp.Key);
                                path = p.MainModule?.FileName ?? string.Empty;
                            }
                            catch
                            {
                                path = string.Empty;
                            }

                            _tracked[kvp.Key] = (DateTime.Now, kvp.Value, path);
                        }

                        // Collect metrics for running processes
                        try
                        {
                            var process = Process.GetProcessById(kvp.Key);
                            var cpuUsage = GetCpuUsage(process);
                            var memoryMB = process.WorkingSet64 / 1024.0 / 1024.0;

                            if (_metrics.TryGetValue(kvp.Key, out var existingMetrics))
                            {
                                var newPeakCpu = Math.Max(existingMetrics.peakCpu, cpuUsage);
                                var newPeakMem = Math.Max(existingMetrics.peakMem, memoryMB);
                                _metrics[kvp.Key] = (newPeakCpu, existingMetrics.totalCpu + cpuUsage, newPeakMem, existingMetrics.totalMem + memoryMB, existingMetrics.samples + 1);
                            }
                            else
                            {
                                _metrics[kvp.Key] = (cpuUsage, cpuUsage, memoryMB, memoryMB, 1);
                            }
                        }
                        catch
                        {
                            // Process might have ended, skip metrics
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(Path.Combine(UsageStorage.BaseDirectory, "usage-errors.txt"), $"[{DateTime.Now:HH:mm:ss}] Tick error: {ex.Message}\n");
                }
                catch { }
            }
        }

        private double GetCpuUsage(Process process)
        {
            try
            {
                process.Refresh();
                return process.TotalProcessorTime.TotalMilliseconds / Environment.ProcessorCount;
            }
            catch
            {
                return 0;
            }
        }

        private static class UsageStorage
        {
            public static string BaseDirectory => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TaskCore",
                "SessionLogs");

            public static void EnsureDirectory()
            {
                Directory.CreateDirectory(BaseDirectory);
            }

            // Atomic append to single daily file - no conflicts, no thousands of files
            public static void AppendSession(string name, int pid, string? path, DateTime start, DateTime end, double peakCpu = 0, double avgCpu = 0, double peakMem = 0, double avgMem = 0)
            {
                var date = DateOnly.FromDateTime(start);
                var file = Path.Combine(BaseDirectory, $"sessions-{date:yyyy-MM-dd}.jsonl");

                try
                {
                    // Create session record
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

                    // Append to single daily file with atomic appends - clean and scalable
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

            // Get all sessions for a specific date
            public static List<Dictionary<string, object>> GetDailySessions(DateOnly date)
            {
                var file = Path.Combine(BaseDirectory, $"sessions-{date:yyyy-MM-dd}.jsonl");
                var legacyFile = Path.Combine(BaseDirectory, $"sessions_{date:yyyy-MM-dd}.json");
                var sessions = new List<Dictionary<string, object>>();

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
                                var session = JsonSerializer.Deserialize<Dictionary<string, object>>(line);
                                if (session != null)
                                    sessions.Add(session);
                            }
                        }
                    }
                    else if (File.Exists(legacyFile))
                    {
                        // Legacy format written by AppSessionLogService (sessions_YYYY-MM-DD.json)
                        var json = File.ReadAllText(legacyFile);
                        using var doc = JsonDocument.Parse(json);

                        if (doc.RootElement.TryGetProperty("Sessions", out var legacySessions) && legacySessions.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var rec in legacySessions.EnumerateArray())
                            {
                                try
                                {
                                    var appName = rec.TryGetProperty("AppName", out var appEl) ? (appEl.GetString() ?? string.Empty) : string.Empty;
                                    var pid = rec.TryGetProperty("ProcessId", out var pidEl) ? pidEl.GetInt32() : 0;
                                    var processPath = rec.TryGetProperty("ProcessPath", out var pathEl) ? (pathEl.GetString() ?? string.Empty) : string.Empty;

                                    // Legacy timestamps are local times; UI expects utc keys but converts to local anyway.
                                    var startLocal = rec.TryGetProperty("StartTimeLocal", out var startEl) ? startEl.GetDateTime() : DateTime.MinValue;
                                    var endLocal = rec.TryGetProperty("EndTimeLocal", out var endEl) ? endEl.GetDateTime() : DateTime.MinValue;

                                    var durationSeconds = rec.TryGetProperty("DurationSeconds", out var durEl) ? (int)Math.Round(durEl.GetDouble()) : 0;
                                    var peakCpu = rec.TryGetProperty("PeakCpuPercent", out var peakCpuEl) ? peakCpuEl.GetDouble() : 0.0;
                                    var avgCpu = rec.TryGetProperty("AverageCpuPercent", out var avgCpuEl) ? avgCpuEl.GetDouble() : 0.0;
                                    var peakMem = rec.TryGetProperty("PeakMemoryMb", out var peakMemEl) ? peakMemEl.GetDouble() : 0.0;
                                    var avgMem = rec.TryGetProperty("AverageMemoryMb", out var avgMemEl) ? avgMemEl.GetDouble() : 0.0;

                                    var dict = new Dictionary<string, object>
                                    {
                                        ["app"] = appName,
                                        ["processId"] = pid,
                                        ["processPath"] = processPath,
                                        ["startTimeUtc"] = startLocal.Kind == DateTimeKind.Utc ? startLocal : startLocal.ToUniversalTime(),
                                        ["endTimeUtc"] = endLocal.Kind == DateTimeKind.Utc ? endLocal : endLocal.ToUniversalTime(),
                                        ["durationSeconds"] = durationSeconds,
                                        ["peakCpuPercent"] = peakCpu,
                                        ["averageCpuPercent"] = avgCpu,
                                        ["peakMemoryMB"] = peakMem,
                                        ["averageMemoryMB"] = avgMem
                                    };

                                    sessions.Add(dict);
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

            // Get summary grouped by app for a date
            public static Dictionary<string, object> GetDailySummary(DateOnly date)
            {
                var sessions = GetDailySessions(date);
                var apps = new Dictionary<string, object>();

                foreach (var session in sessions)
                {
                    var appName = session["app"].ToString();
                    
                    if (!apps.ContainsKey(appName))
                    {
                        apps[appName] = new
                        {
                            launchCount = 0,
                            totalRuntimeSeconds = 0,
                            sessions = new List<Dictionary<string, object>>()
                        };
                    }

                    var appData = (dynamic)apps[appName];
                    appData.launchCount++;
                    appData.totalRuntimeSeconds += Convert.ToInt32(session["durationSeconds"]);
                    ((List<Dictionary<string, object>>)appData.sessions).Add(session);
                }

                return new Dictionary<string, object>
                {
                    ["date"] = date.ToString("yyyy-MM-dd"),
                    ["apps"] = apps
                };
            }

            // Clean up files older than specified days
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
                            // Compress old files instead of deleting
                            var compressedFile = file + ".gz";
                            if (!File.Exists(compressedFile))
                            {
                                // Simple compression note - would need System.IO.Compression
                                File.Delete(file); // For now, just delete
                            }
                        }
                    }

                    // Clean up old error logs (keep only last 30 days)
                    var errorFile = Path.Combine(BaseDirectory, "errors.txt");
                    if (File.Exists(errorFile))
                    {
                        var errorLines = File.ReadAllLines(errorFile);
                        var recentLines = errorLines.Where(line => 
                        {
                            if (string.IsNullOrWhiteSpace(line)) return false;
                            var lineDateStr = line.Substring(1, 10); // Extract date from [YYYY-MM-DD]
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

            // Export data to CSV format
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
                            $"\"{session["app"]}\"," +
                            $"{session["processId"]}," +
                            $"{Convert.ToDateTime(session["startTimeUtc"]).ToLocalTime():yyyy-MM-dd HH:mm:ss}," +
                            $"{Convert.ToDateTime(session["endTimeUtc"]).ToLocalTime():yyyy-MM-dd HH:mm:ss}," +
                            $"{session["durationSeconds"]}," +
                            $"{session["peakCpuPercent"]}," +
                            $"{session["averageCpuPercent"]}," +
                            $"{session["peakMemoryMB"]}," +
                            $"{session["averageMemoryMB"]}");
                    }
                }

                return csv.ToString();
            }

            // Export data to JSON format
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
                    if (((Dictionary<string, object>)summary["apps"]).Any())
                    {
                        dataDict[date.ToString("yyyy-MM-dd")] = summary;
                    }
                }

                return JsonSerializer.Serialize(exportData, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
            }

            // Validate and repair corrupted files
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
                            // Skip invalid lines
                        }
                    }

                    // If we lost any lines, repair the file
                    if (validLines.Count != lines.Length)
                    {
                        File.WriteAllLines(file, validLines);
                        return true; // Repaired
                    }

                    return true; // Valid
                }
                catch
                {
                    return false; // Cannot repair
                }
            }
        }

        // Only filter out actual Windows system processes - everything else is tracked
        private static bool IsSystemProcess(Process p)
        {
            try
            {
                var name = p.ProcessName.ToLowerInvariant();
                
                // Core Windows system processes only
                if (name == "system" || 
                    name == "idle" ||
                    name.StartsWith("svchost") || 
                    name.StartsWith("csrss") ||
                    name.StartsWith("wininit") ||
                    name.StartsWith("smss") ||
                    name.StartsWith("dwm") ||
                    name.StartsWith("lsass") ||
                    name.StartsWith("lsm"))
                    return true;

                // Skip processes from Windows directory
                try
                {
                    if (p.MainModule?.FileName?.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase) == true)
                        return true;
                }
                catch
                {
                    // Access denied = likely system process
                    return true;
                }

                return false;
            }
            catch
            {
                // If we can't access process info, assume it's system
                return true;
            }
        }
    }
}
