using System;
using System.IO;
using TaskCore.Logging;

namespace TaskCore.Services
{
    /// <summary>
    /// Enterprise logging bridge forwarding calls to TaskCore.Logging engine.
    /// Supports automatic rotation (50MB limit) and non-deleting compression archives.
    /// </summary>
    internal static class TaskCoreLogger
    {
        public static void Log(string logName, string message, string level = "INFO", string logType = "Application")
        {
            var parsedLevel = level switch
            {
                "WARN" => LogLevel.WARN,
                "ERROR" => LogLevel.ERROR,
                "DEBUG" => LogLevel.DEBUG,
                "FATAL" => LogLevel.FATAL,
                "PERF" => LogLevel.PERF,
                _ => LogLevel.INFO
            };

            TaskCoreLog.Log(parsedLevel, logType, $"[{logName}] {message}");
        }

        public static void Info(string logName, string message) => Log(logName, message, "INFO");
        public static void Warn(string logName, string message) => Log(logName, message, "WARN");
        public static void Error(string logName, string message) => Log(logName, message, "ERROR");
        public static void Debug(string logName, string message) => Log(logName, message, "DEBUG");

        public static class SessionTracking
        {
            private const string Category = "SessionTracker";

            public static void Startup() => TaskCoreLog.Info(Category, "=== App Startup ===");
            public static void Shutdown() => TaskCoreLog.Info(Category, "=== App Shutdown ===");
            public static void SessionStarted(string appName, DateTime startTime) => 
                TaskCoreLog.Info(Category, $"SESSION_START: {appName} at {startTime:HH:mm:ss}");
            public static void SessionEnded(string appName, TimeSpan duration) => 
                TaskCoreLog.Info(Category, $"SESSION_END: {appName}, duration={duration.TotalSeconds:F0}s");
            public static void ActiveSessionsSaved(int count) => 
                TaskCoreLog.Info(Category, $"ACTIVE_SESSIONS_SAVED: {count} sessions written to disk");
            public static void OrphanedSessionsRecovered(int count) => 
                TaskCoreLog.Warn(Category, $"ORPHANED_SESSIONS_RECOVERED: {count} sessions from previous crash");
            public static void SessionsFinalizedOnRecovery(int count) => 
                TaskCoreLog.Info(Category, $"SESSIONS_FINALIZED: {count} orphaned sessions marked as completed");
        }

        public static class Lifecycle
        {
            private const string Category = "Application";

            public static void DisposeCalled() => TaskCoreLog.Info(Category, "DISPOSE: UsageTrackingService.Dispose() called");
            public static void OnExitCalled() => TaskCoreLog.Info(Category, "ONEXIT: App.OnExit() called");
            public static void ProcessExit() => TaskCoreLog.Warn(Category, "PROCESS_EXIT: AppDomain.ProcessExit event fired");
            public static void UnhandledException(string exMessage) => 
                TaskCoreLog.Error(Category, $"UNHANDLED_EXCEPTION: {exMessage}");
            public static void GracefulShutdown() => TaskCoreLog.Info(Category, "SHUTDOWN: Graceful shutdown completed");
            public static void TaskManagerKill() => 
                TaskCoreLog.Warn(Category, "KILL_DETECTED: Previous session was force-killed (Task Manager)");
        }

        public static class Errors
        {
            private const string Category = "Errors";

            public static void BackgroundWriter(string message) => TaskCoreLog.Error(Category, $"BACKGROUND_WRITER: {message}");
            public static void SessionSave(string message) => TaskCoreLog.Error(Category, $"SESSION_SAVE: {message}");
            public static void ActiveSessionSave(string message) => TaskCoreLog.Error(Category, $"ACTIVE_SESSION_SAVE: {message}");
            public static void FileOperation(string message) => TaskCoreLog.Error(Category, $"FILE_OPERATION: {message}");
        }

        public static void CleanupOldLogs()
        {
            // Compress completed old sessions into Zip archives without deleting historical data
            LogCompressionService.CompressOldSessions();
        }

        public static void CleanAllLogs()
        {
            try
            {
                var dir = SessionManager.GetLogsDirectory();
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
            }
        }

        public static string Get_baseDir() => SessionManager.GetLogsDirectory();

        public static (string name, long size, DateTime lastWrite)[] GetLogFiles()
        {
            try
            {
                var sessionDir = SessionManager.SessionDirectory;
                if (!Directory.Exists(sessionDir))
                    return Array.Empty<(string, long, DateTime)>();

                return Directory.GetFiles(sessionDir, "*.log")
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
