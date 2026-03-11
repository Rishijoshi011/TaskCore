using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TaskCore.Services
{
    internal static class UsageTrackingStorage
    {
        private sealed class DailySessionsCacheEntry
        {
            public DateTime lastWriteUtc;
            public long length;
            public List<UsageTrackingService.SessionRecord> sessions = new();
        }

        private static readonly object _dailySessionsCacheLock = new();
        private static readonly Dictionary<string, DailySessionsCacheEntry> _dailySessionsCache = new();

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

        public static void AppendSession(string name, int pid, string? path, DateTime start, DateTime end, double peakCpu = 0, double avgCpu = 0, double peakMem = 0, double avgMem = 0, bool isIdle = false)
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
                    averageMemoryMB = Math.Round(avgMem, 2),
                    isIdle = isIdle  // Track if session was during idle/locked time
                };

                var jsonLine = JsonSerializer.Serialize(session) + Environment.NewLine;
                File.AppendAllText(file, jsonLine);
            }
            catch (Exception ex)
            {
                TaskCoreLogger.Errors.SessionSave($"Failed to append session for {name}: {ex.Message}");
            }
        }

        public static List<UsageTrackingService.SessionRecord> GetDailySessions(DateOnly date, bool mergeOverlappingSessions = true)
        {
            var file = Path.Combine(BaseDirectory, $"sessions-{date:yyyy-MM-dd}.jsonl");
            var compressedFile = file + ".gz";
            var legacyFile = Path.Combine(BaseDirectory, $"sessions_{date:yyyy-MM-dd}.json");
            string? chosenFile = null;
            bool isCompressed = false;

            if (File.Exists(file))
                chosenFile = file;
            else if (File.Exists(compressedFile))
            {
                chosenFile = compressedFile;
                isCompressed = true;
            }
            else if (File.Exists(legacyFile))
                chosenFile = legacyFile;

            if (chosenFile == null)
                return new List<UsageTrackingService.SessionRecord>();

            try
            {
                var info = new FileInfo(chosenFile);
                var lastWriteUtc = info.LastWriteTimeUtc;
                var length = info.Exists ? info.Length : 0;

                lock (_dailySessionsCacheLock)
                {
                    var cacheKey = mergeOverlappingSessions ? $"{date}_merged" : date.ToString();
                    if (_dailySessionsCache.TryGetValue(cacheKey, out var cached)
                        && cached.lastWriteUtc == lastWriteUtc
                        && cached.length == length)
                    {
                        return mergeOverlappingSessions ? MergeOverlappingSessions(cached.sessions) : cached.sessions;
                    }
                }
            }
            catch
            {
                // ignore cache failures and fall back to direct read
            }

            var sessions = new List<UsageTrackingService.SessionRecord>();

            try
            {
                if (isCompressed)
                {
                    // Read from compressed file
                    using var fileStream = File.OpenRead(compressedFile);
                    using var gzipStream = new System.IO.Compression.GZipStream(fileStream, System.IO.Compression.CompressionMode.Decompress);
                    using var reader = new StreamReader(gzipStream);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var session = JsonSerializer.Deserialize<UsageTrackingService.SessionRecord>(line);
                            if (session != null)
                                sessions.Add(session);
                        }
                    }
                }
                else if (string.Equals(chosenFile, file, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var line in File.ReadLines(file))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var session = JsonSerializer.Deserialize<UsageTrackingService.SessionRecord>(line);
                            if (session != null)
                                sessions.Add(session);
                        }
                    }
                }
                else if (string.Equals(chosenFile, legacyFile, StringComparison.OrdinalIgnoreCase))
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
                TaskCoreLogger.Errors.FileOperation($"Failed to read daily sessions: {ex.Message}");
            }

            try
            {
                var info = new FileInfo(chosenFile);
                var lastWriteUtc = info.LastWriteTimeUtc;
                var length = info.Exists ? info.Length : 0;

                lock (_dailySessionsCacheLock)
                {
                    var cacheKey = mergeOverlappingSessions ? $"{date}_merged" : date.ToString();
                    _dailySessionsCache[cacheKey] = new DailySessionsCacheEntry
                    {
                        lastWriteUtc = lastWriteUtc,
                        length = length,
                        sessions = sessions
                    };

                    // keep cache bounded (avoid unbounded growth)
                    if (_dailySessionsCache.Count > 62) // 31 days * 2 (merged and unmerged keys)
                    {
                        var cutoff = DateTime.Today.AddDays(-31);
                        var keysToRemove = _dailySessionsCache.Keys
                            .Where(k =>
                            {
                                // Parse date from key (format: "yyyy-MM-dd" or "yyyy-MM-dd_merged")
                                var underscoreIndex = k.IndexOf('_');
                                var dateStr = underscoreIndex >= 0 ? k.Substring(0, underscoreIndex) : k;
                                return DateTime.TryParse(dateStr, out var keyDate) && keyDate < cutoff;
                            })
                            .ToList();
                        foreach (var k in keysToRemove)
                            _dailySessionsCache.Remove(k);
                    }
                }
            }
            catch
            {
            }

            return mergeOverlappingSessions ? MergeOverlappingSessions(sessions) : sessions;
        }

        private static List<UsageTrackingService.SessionRecord> MergeOverlappingSessions(List<UsageTrackingService.SessionRecord> sessions)
        {
            if (sessions.Count == 0) 
            {
                return sessions;
            }

            // Filter out only core Windows system processes (be conservative)
            var userSessions = sessions
                .Where(s => !IsCoreWindowsProcess(s.app))
                .ToList();

            // Group by app name (case-insensitive)
            var groupedSessions = userSessions
                .GroupBy(s => s.app?.ToLowerInvariant() ?? "unknown")
                .ToList();

            var mergedSessions = new List<UsageTrackingService.SessionRecord>();

            foreach (var group in groupedSessions)
            {
                var sortedSessions = group
                    .OrderBy(s => s.startTimeUtc)
                    .ToList();

                var currentMerged = new List<UsageTrackingService.SessionRecord>();

                foreach (var session in sortedSessions)
                {
                    // Skip invalid sessions
                    if (session.durationSeconds <= 0 || session.endTimeUtc <= session.startTimeUtc)
                        continue;

                    // Find if this session overlaps or is close to any existing merged session
                    var overlappingMerged = currentMerged
                        .FirstOrDefault(m => ShouldMergeSessions(session, m));

                    if (overlappingMerged != null)
                    {
                        // Merge the sessions
                        var newStart = session.startTimeUtc < overlappingMerged.startTimeUtc ? session.startTimeUtc : overlappingMerged.startTimeUtc;
                        var newEnd = session.endTimeUtc > overlappingMerged.endTimeUtc ? session.endTimeUtc : overlappingMerged.endTimeUtc;
                        var newDuration = (int)(newEnd - newStart).TotalSeconds;

                        // Update the merged session with combined metrics
                        overlappingMerged.startTimeUtc = newStart;
                        overlappingMerged.endTimeUtc = newEnd;
                        overlappingMerged.durationSeconds = newDuration;
                        
                        // Take the maximum of performance metrics
                        overlappingMerged.peakCpuPercent = Math.Max(overlappingMerged.peakCpuPercent, session.peakCpuPercent);
                        overlappingMerged.peakMemoryMB = Math.Max(overlappingMerged.peakMemoryMB, session.peakMemoryMB);
                        
                        // Average the CPU metrics weighted by duration
                        var totalCpuTime = overlappingMerged.averageCpuPercent * overlappingMerged.durationSeconds + 
                                         session.averageCpuPercent * session.durationSeconds;
                        overlappingMerged.averageCpuPercent = totalCpuTime / newDuration;
                        
                        // Average the memory metrics weighted by duration
                        var totalMemTime = overlappingMerged.averageMemoryMB * overlappingMerged.durationSeconds + 
                                         session.averageMemoryMB * session.durationSeconds;
                        overlappingMerged.averageMemoryMB = totalMemTime / newDuration;
                        
                        // Mark as idle if either session was idle
                        overlappingMerged.isIdle = overlappingMerged.isIdle || session.isIdle;
                    }
                    else
                    {
                        // Add as a new merged session
                        currentMerged.Add(new UsageTrackingService.SessionRecord
                        {
                            app = session.app,
                            processId = session.processId,
                            processPath = session.processPath,
                            startTimeUtc = session.startTimeUtc,
                            endTimeUtc = session.endTimeUtc,
                            durationSeconds = session.durationSeconds,
                            peakCpuPercent = session.peakCpuPercent,
                            averageCpuPercent = session.averageCpuPercent,
                            peakMemoryMB = session.peakMemoryMB,
                            averageMemoryMB = session.averageMemoryMB,
                            isIdle = session.isIdle
                        });
                    }
                }

                mergedSessions.AddRange(currentMerged);
            }

            return mergedSessions.OrderBy(s => s.startTimeUtc).ToList();
        }

        private static bool ShouldMergeSessions(UsageTrackingService.SessionRecord session1, UsageTrackingService.SessionRecord session2)
        {
            // Check for direct overlap
            if (session1.startTimeUtc < session2.endTimeUtc && session1.endTimeUtc > session2.startTimeUtc)
                return true;

            // Check for small gap (consolidate fragmented sessions)
            const int maxGapSeconds = 30; // Consolidate sessions with gaps up to 30 seconds
            
            if (session1.endTimeUtc < session2.startTimeUtc)
            {
                var gap = (int)(session2.startTimeUtc - session1.endTimeUtc).TotalSeconds;
                return gap <= maxGapSeconds;
            }
            else if (session2.endTimeUtc < session1.startTimeUtc)
            {
                var gap = (int)(session1.startTimeUtc - session2.endTimeUtc).TotalSeconds;
                return gap <= maxGapSeconds;
            }

            return false;
        }

        private static bool IsCoreWindowsProcess(string? appName)
        {
            if (string.IsNullOrWhiteSpace(appName)) return false;

            var name = appName.ToLowerInvariant();

            // Only filter core Windows system processes that are definitely not user apps
            var coreProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "svchost", "csrss", "wininit", "smss", "dwm", "lsass", "lsm",
                "services", "winlogon", "conhost", "sihost", "fontdrvhost",
                "spoolsv", "ctfmon", "audiodg", "smsvchost", "registry",
                "system", "idle", "searchindexer", "runtimebroker",
                "taskhost", "taskhostw", "backgroundtaskhost",
                "searchprotocolhost", "searchfilterhost",
                "mousocoreworker", "sppsvc", "tiworker", "trustedinstaller",
                "compattelrunner", "ngciso", "wmiadap", "wmiprvse",
                "sppextcomobj", "backgrounddownload", "backgroundtransferhost",
                "bdeuisrv", "defrag", "devicecensus", "dism", "dismhost",
                "dllhost", "lpremove", "mscorsvw", "ngen", "ngentask",
                "onedrivelauncher", "onedrivestandaloneupdater",
                "pickerhost", "provtool", "rundll32", "sc", "slui",
                "srtasks", "usoclient", "vsixautoupdate", "wermgr",
                "wmiprvse", "wmiadap"
            };

            // Check exact match or starts with
            if (coreProcesses.Contains(name)) return true;
            
            // Check if it's a core Windows process by prefix
            foreach (var proc in coreProcesses)
            {
                if (name.StartsWith(proc, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
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

        /// <summary>
        /// Compresses old session files to save disk space. Does NOT delete any data.
        /// Data is kept indefinitely unless manually deleted by user.
        /// </summary>
        public static void CleanupOldFiles(int compressAfterDays = 30)
        {
            try
            {
                if (!Directory.Exists(BaseDirectory)) return;

                var now = DateTime.Now;
                var cutoffCompress = now.AddDays(-compressAfterDays);
                var files = Directory.GetFiles(BaseDirectory, "*.jsonl");

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    var compressedFile = file + ".gz";

                    // Compress files older than compressAfterDays (to save space)
                    // Note: We keep both compressed and uncompressed for recent files
                    if (fileInfo.LastWriteTime < cutoffCompress && !File.Exists(compressedFile))
                    {
                        try
                        {
                            CompressFile(file, compressedFile);
                            // Delete original only after successful compression
                            if (File.Exists(compressedFile))
                            {
                                File.Delete(file);
                                TaskCoreLogger.Info("storage", $"Compressed {Path.GetFileName(file)} to save space");
                            }
                        }
                        catch (Exception ex)
                        {
                            TaskCoreLogger.Errors.FileOperation($"Failed to compress {file}: {ex.Message}");
                        }
                    }
                }

                // Clean up old errors.txt file (not session data)
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

                TaskCoreLogger.Info("storage", "Cleanup completed - session data retained indefinitely");
            }
            catch (Exception ex)
            {
                File.AppendAllText(Path.Combine(BaseDirectory, "errors.txt"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Cleanup error: {ex.Message}\n");
            }
        }

        private static void CompressFile(string sourceFile, string destinationFile)
        {
            using var inputFileStream = File.OpenRead(sourceFile);
            using var outputFileStream = File.Create(destinationFile);
            using var gzipStream = new System.IO.Compression.GZipStream(outputFileStream, System.IO.Compression.CompressionLevel.Optimal);
            inputFileStream.CopyTo(gzipStream);
        }

        private static void DecompressFile(string sourceFile, string destinationFile)
        {
            using var inputFileStream = File.OpenRead(sourceFile);
            using var gzipStream = new System.IO.Compression.GZipStream(inputFileStream, System.IO.Compression.CompressionMode.Decompress);
            using var outputFileStream = File.Create(destinationFile);
            gzipStream.CopyTo(outputFileStream);
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

        // Active session persistence for crash recovery
        private static readonly string ActiveSessionsFile = Path.Combine(BaseDirectory, "active-sessions.json");
        private static readonly object _activeSessionsLock = new();

        public static void SaveActiveSessions(List<ActiveSessionData> sessions)
        {
            try
            {
                var data = new ActiveSessionsData
                {
                    savedAt = DateTime.UtcNow,
                    sessions = sessions
                };
                var json = JsonSerializer.Serialize(data);
                lock (_activeSessionsLock)
                {
                    File.WriteAllText(ActiveSessionsFile, json);
                }
            }
            catch (Exception ex)
            {
                TaskCoreLogger.Errors.ActiveSessionSave($"SaveActiveSessions error: {ex.Message}");
            }
        }

        public static List<ActiveSessionData> LoadActiveSessions()
        {
            try
            {
                lock (_activeSessionsLock)
                {
                    if (!File.Exists(ActiveSessionsFile))
                        return new List<ActiveSessionData>();

                    var json = File.ReadAllText(ActiveSessionsFile);
                    var data = JsonSerializer.Deserialize<ActiveSessionsData>(json);
                    return data?.sessions ?? new List<ActiveSessionData>();
                }
            }
            catch
            {
                return new List<ActiveSessionData>();
            }
        }

        public static void DeleteActiveSessions()
        {
            try
            {
                lock (_activeSessionsLock)
                {
                    if (File.Exists(ActiveSessionsFile))
                        File.Delete(ActiveSessionsFile);
                }
            }
            catch { }
        }

        public static void FinalizeOrphanedSessions()
        {
            try
            {
                var orphanedSessions = LoadActiveSessions();
                if (orphanedSessions.Count == 0) return;

                TaskCoreLogger.Lifecycle.TaskManagerKill();
                TaskCoreLogger.SessionTracking.OrphanedSessionsRecovered(orphanedSessions.Count);

                var now = DateTime.UtcNow;
                foreach (var session in orphanedSessions)
                {
                    // Calculate end time - use the last saved time or now
                    var endTime = session.lastUpdateTime > session.startTime 
                        ? session.lastUpdateTime 
                        : now;

                    // Ensure minimum 1 second duration
                    if (endTime <= session.startTime)
                        endTime = session.startTime.AddSeconds(1);

                    AppendSession(
                        session.appName,
                        session.firstPid,
                        session.path,
                        session.startTime,
                        endTime,
                        session.peakCpu,
                        session.avgCpu,
                        session.peakMem,
                        session.avgMem,
                        false);

                    TaskCoreLogger.Info("recovery", $"Finalized: {session.appName}, duration: {(endTime - session.startTime).TotalSeconds:F0}s");
                }

                DeleteActiveSessions();
                TaskCoreLogger.SessionTracking.SessionsFinalizedOnRecovery(orphanedSessions.Count);
            }
            catch (Exception ex)
            {
                TaskCoreLogger.Error("recovery", $"ERROR finalizing orphaned sessions: {ex.Message}");
            }
        }
    }

    public class ActiveSessionData
    {
        public string appName { get; set; } = string.Empty;
        public int firstPid { get; set; }
        public string path { get; set; } = string.Empty;
        public DateTime startTime { get; set; }
        public DateTime lastUpdateTime { get; set; }
        public double peakCpu { get; set; }
        public double avgCpu { get; set; }
        public double peakMem { get; set; }
        public double avgMem { get; set; }
        public int samples { get; set; }
    }

    public class ActiveSessionsData
    {
        public DateTime savedAt { get; set; }
        public List<ActiveSessionData> sessions { get; set; } = new();
    }
}
