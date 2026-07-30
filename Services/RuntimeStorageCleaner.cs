using System;
using System.IO;
using TaskCore.Logging;

namespace TaskCore.Services
{
    public static class RuntimeStorageCleaner
    {
        public static void RunAutoCleanup()
        {
            try
            {
                var baseDir = RuntimeStorageInitializer.BaseDirectory;
                if (!Directory.Exists(baseDir)) return;

                // 1. Delete temp files older than 7 days
                CleanFilesOlderThan(Path.Combine(baseDir, "Temp"), TimeSpan.FromDays(7));
                CleanFilesOlderThan(Path.Combine(baseDir, "Logs", "Temp"), TimeSpan.FromDays(7));

                // 2. Delete log files older than 30 days
                CleanFilesOlderThan(Path.Combine(baseDir, "Logs", "Application"), TimeSpan.FromDays(30));
                CleanFilesOlderThan(Path.Combine(baseDir, "Logs", "Performance"), TimeSpan.FromDays(30));
                CleanFilesOlderThan(Path.Combine(baseDir, "Logs", "Hardware"), TimeSpan.FromDays(30));
                CleanFilesOlderThan(Path.Combine(baseDir, "Logs", "Errors"), TimeSpan.FromDays(30));

                // 3. Delete session logs older than 90 days
                CleanFilesOlderThan(Path.Combine(baseDir, "SessionLogs"), TimeSpan.FromDays(90));

                // 4. Compress completed past session folders
                LogCompressionService.CompressOldSessions();

                // 5. Clean empty directories under Sessions
                CleanEmptyDirectories(Path.Combine(baseDir, "Sessions"));
                CleanEmptyDirectories(Path.Combine(baseDir, "Logs", "Sessions"));
            }
            catch (Exception ex)
            {
                TaskCoreLog.Error("Application", $"Auto cleanup encountered error: {ex.Message}");
            }
        }

        public static void ResetApplicationData()
        {
            try
            {
                // 1. Flush pending log entries
                AsyncLogBuffer.Instance.FlushSync();

                var baseDir = RuntimeStorageInitializer.BaseDirectory;
                if (!Directory.Exists(baseDir))
                {
                    RuntimeStorageInitializer.Bootstrap();
                    return;
                }

                // 2. Safely clear all runtime data subdirectories
                var subDirs = Directory.GetDirectories(baseDir);
                foreach (var dir in subDirs)
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                    catch
                    {
                    }
                }

                // 3. Clear loose root files (e.g. settings.json, rules.json)
                var files = Directory.GetFiles(baseDir);
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                    }
                }

                // 4. Re-bootstrap complete directory structure & default configuration
                RuntimeStorageInitializer.Bootstrap();
                TaskCoreLog.Info("Application", "Application runtime data successfully reset to default state.");
            }
            catch (Exception ex)
            {
                TaskCoreLog.Error("Application", $"ResetApplicationData failed: {ex.Message}");
            }
        }

        private static void CleanFilesOlderThan(string dirPath, TimeSpan age)
        {
            try
            {
                if (!Directory.Exists(dirPath)) return;

                var cutoff = DateTime.UtcNow - age;
                var files = Directory.GetFiles(dirPath, "*.*", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTimeUtc < cutoff)
                        {
                            File.Delete(file);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static void CleanEmptyDirectories(string dirPath)
        {
            try
            {
                if (!Directory.Exists(dirPath)) return;

                foreach (var subDir in Directory.GetDirectories(dirPath))
                {
                    CleanEmptyDirectories(subDir);
                    if (!Directory.EnumerateFileSystemEntries(subDir).Any())
                    {
                        try
                        {
                            Directory.Delete(subDir, recursive: false);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }
}
