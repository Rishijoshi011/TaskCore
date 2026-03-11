using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace TaskCore.Services
{
    /// <summary>
    /// Centralized logging service for TaskCore with automatic log rotation.
    /// All logs are stored in %LocalAppData%\TaskCore\Logs with date-based files.
    /// </summary>
    internal static class TaskCoreLogger
    {
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskCore", "Logs");

        private static readonly ReaderWriterLockSlim _lock = new();
        private const int MaxLogDays = 7;

        static TaskCoreLogger()
        {
            EnsureDirectory();
        }

        private static void EnsureDirectory()
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
            }
            catch { }
        }

        /// <summary>
        /// Log a message to the specified log file.
        /// </summary>
        /// <param name="logName">Name of the log file (without extension)</param>
        /// <param name="message">Message to log</param>
        /// <param name="level">Log level: INFO, WARN, ERROR, DEBUG</param>
        public static void Log(string logName, string message, string level = "INFO")
        {
            try
            {
                EnsureDirectory();
                var date = DateTime.Now;
                var logFile = Path.Combine(LogDirectory, $"{logName}-{date:yyyy-MM-dd}.txt");
                var timestamp = date.ToString("HH:mm:ss.fff");
                var logLine = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";

                _lock.EnterWriteLock();
                try
                {
                    File.AppendAllText(logFile, logLine);
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
            catch { }
        }

        /// <summary>
        /// Log an info message.
        /// </summary>
        public static void Info(string logName, string message) => Log(logName, message, "INFO");

        /// <summary>
        /// Log a warning message.
        /// </summary>
        public static void Warn(string logName, string message) => Log(logName, message, "WARN");

        /// <summary>
        /// Log an error message.
        /// </summary>
        public static void Error(string logName, string message) => Log(logName, message, "ERROR");

        /// <summary>
        /// Log a debug message.
        /// </summary>
        public static void Debug(string logName, string message) => Log(logName, message, "DEBUG");

        /// <summary>
        /// Specialized logging for session tracking events.
        /// </summary>
        public static class SessionTracking
        {
            private const string LogName = "session-tracking";

            public static void Startup() => Info(LogName, "=== App Startup ===");
            public static void Shutdown() => Info(LogName, "=== App Shutdown ===");
            public static void SessionStarted(string appName, DateTime startTime) => 
                Info(LogName, $"SESSION_START: {appName} at {startTime:HH:mm:ss}");
            public static void SessionEnded(string appName, TimeSpan duration) => 
                Info(LogName, $"SESSION_END: {appName}, duration={duration.TotalSeconds:F0}s");
            public static void ActiveSessionsSaved(int count) => 
                Info(LogName, $"ACTIVE_SESSIONS_SAVED: {count} sessions written to disk");
            public static void OrphanedSessionsRecovered(int count) => 
                Warn(LogName, $"ORPHANED_SESSIONS_RECOVERED: {count} sessions from previous crash");
            public static void SessionsFinalizedOnRecovery(int count) => 
                Info(LogName, $"SESSIONS_FINALIZED: {count} orphaned sessions marked as completed");
        }

        /// <summary>
        /// Specialized logging for shutdown/crash events.
        /// </summary>
        public static class Lifecycle
        {
            private const string LogName = "lifecycle";

            public static void DisposeCalled() => Info(LogName, "DISPOSE: UsageTrackingService.Dispose() called");
            public static void OnExitCalled() => Info(LogName, "ONEXIT: App.OnExit() called");
            public static void ProcessExit() => Warn(LogName, "PROCESS_EXIT: AppDomain.ProcessExit event fired");
            public static void UnhandledException(string exMessage) => 
                Error(LogName, $"UNHANDLED_EXCEPTION: {exMessage}");
            public static void GracefulShutdown() => Info(LogName, "SHUTDOWN: Graceful shutdown completed");
            public static void TaskManagerKill() => 
                Warn(LogName, "KILL_DETECTED: Previous session was force-killed (Task Manager)");
        }

        /// <summary>
        /// Specialized logging for errors.
        /// </summary>
        public static class Errors
        {
            private const string LogName = "errors";

            public static void BackgroundWriter(string message) => Error(LogName, $"BACKGROUND_WRITER: {message}");
            public static void SessionSave(string message) => Error(LogName, $"SESSION_SAVE: {message}");
            public static void ActiveSessionSave(string message) => Error(LogName, $"ACTIVE_SESSION_SAVE: {message}");
            public static void FileOperation(string message) => Error(LogName, $"FILE_OPERATION: {message}");
        }

        /// <summary>
        /// Clean up old log files (older than 7 days) and legacy scattered logs.
        /// Call this periodically (e.g., on startup).
        /// Note: Session data is kept indefinitely unless user manually deletes it.
        /// </summary>
        public static void CleanupOldLogs()
        {
            try
            {
                if (!Directory.Exists(LogDirectory)) return;

                // Only clean up legacy scattered log files, keep date-based logs indefinitely
                CleanupLegacyLogFiles();
            }
            catch { }
        }

        /// <summary>
        /// Clean up old scattered log files from before centralized logging.
        /// </summary>
        private static void CleanupLegacyLogFiles()
        {
            try
            {
                // Old log files that were scattered before TaskCoreLogger
                var legacyFiles = new[] { "gpuadapter.txt", "startup.txt", "shutdown-log.txt", 
                    "crash-log.txt", "recovery-log.txt", "active-sessions-errors.txt", 
                    "errors.txt", "usage-errors.txt" };

                foreach (var file in legacyFiles)
                {
                    try
                    {
                        var path = Path.Combine(LogDirectory, file);
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Delete all log files in the log directory. Use with caution.
        /// </summary>
        public static void CleanAllLogs()
        {
            try
            {
                if (!Directory.Exists(LogDirectory)) return;

                var files = Directory.GetFiles(LogDirectory, "*.txt");
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Get the full path to the log directory.
        /// </summary>
        public static string GetLogDirectory() => LogDirectory;

        /// <summary>
        /// List all log files with their sizes and last write times.
        /// </summary>
        public static (string name, long size, DateTime lastWrite)[] GetLogFiles()
        {
            try
            {
                if (!Directory.Exists(LogDirectory))
                    return Array.Empty<(string, long, DateTime)>();

                return Directory.GetFiles(LogDirectory, "*.txt")
                    .Select(f =>
                    {
                        var info = new FileInfo(f);
                        return (Path.GetFileName(f), info.Length, info.LastWriteTime);
                    })
                    .ToArray();
            }
            catch
            {
                return Array.Empty<(string, long, DateTime)>();
            }
        }
    }
}
