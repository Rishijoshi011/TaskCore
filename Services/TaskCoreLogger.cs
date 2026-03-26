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
        private static readonly string _baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskCore",
            "Logs");

        private static readonly ReaderWriterLockSlim _lock = new();
        private const int MaxLogDays = 7;
        private const long MaxLogFileSizeBytes = 30 * 1024 * 1024; // 30MB per file
        private const int MaxRotatedFiles = 3; // Keep up to 3 rotated files per day

        static TaskCoreLogger()
        {
            EnsureDirectory();
        }

        private static void EnsureDirectory()
        {
            try
            {
                Directory.CreateDirectory(_baseDir);
                Directory.CreateDirectory(Path.Combine(_baseDir, "Application"));
                Directory.CreateDirectory(Path.Combine(_baseDir, "Sessions"));
                Directory.CreateDirectory(Path.Combine(_baseDir, "Automation"));
                Directory.CreateDirectory(Path.Combine(_baseDir, "Rules"));
            }
            catch { }
        }

        /// <summary>
        /// Log a message to the specified log file with automatic size-based rotation.
        /// </summary>
        /// <param name="logName">Name of the log file (without extension)</param>
        /// <param name="message">Message to log</param>
        /// <param name="level">Log level: INFO, WARN, ERROR, DEBUG</param>
        /// <param name="logType">Type of log (Application, Sessions, Automation, Rules)</param>
        public static void Log(string logName, string message, string level = "INFO", string logType = "Application")
        {
            try
            {
                EnsureDirectory();
                var date = DateTime.Now;
                var logFile = GetLogFilePath(logName, date, logType);
                var timestamp = date.ToString("HH:mm:ss.fff");
                var logLine = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";

                _lock.EnterWriteLock();
                try
                {
                    // Check file size and rotate if needed
                    RotateIfNeeded(logFile);
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
        /// Gets the log file path for a given date, using rotation if file is too large.
        /// </summary>
        private static string GetLogFilePath(string logName, DateTime date, string logType)
        {
            var logDir = Path.Combine(_baseDir, logType);
            var baseFile = Path.Combine(logDir, $"{logName}-{date:yyyy-MM-dd}.txt");
            
            // If base file doesn't exist or is under size limit, use it
            if (!File.Exists(baseFile))
                return baseFile;
                
            var info = new FileInfo(baseFile);
            if (info.Length < MaxLogFileSizeBytes)
                return baseFile;
            
            // Find available rotated file
            for (int i = 1; i <= MaxRotatedFiles; i++)
            {
                var rotatedFile = Path.Combine(logDir, $"{logName}-{date:yyyy-MM-dd}.{i}.txt");
                if (!File.Exists(rotatedFile))
                    return rotatedFile;
                    
                var rotatedInfo = new FileInfo(rotatedFile);
                if (rotatedInfo.Length < MaxLogFileSizeBytes)
                    return rotatedFile;
            }
            
            // All rotated files are full - overwrite the oldest one (highest number)
            return Path.Combine(logDir, $"{logName}-{date:yyyy-MM-dd}.{MaxRotatedFiles}.txt");
        }

        /// <summary>
        /// Checks if the log file needs rotation and performs rotation if necessary.
        /// </summary>
        private static void RotateIfNeeded(string logFile)
        {
            try
            {
                if (!File.Exists(logFile))
                    return;
                    
                var info = new FileInfo(logFile);
                if (info.Length < MaxLogFileSizeBytes)
                    return;
                
                // File is too large - extract rotation number from filename
                var fileName = Path.GetFileNameWithoutExtension(logFile);
                var parts = fileName.Split('.');
                var datePart = parts[0]; // e.g., "process-analytics-2026-03-26"
                
                // Find next available rotation slot
                for (int i = 1; i <= MaxRotatedFiles; i++)
                {
                    var rotatedFile = $"{datePart}.{i}.txt";
                    if (!File.Exists(rotatedFile))
                        return; // Next write will go to this file
                        
                    var rotatedInfo = new FileInfo(rotatedFile);
                    if (rotatedInfo.Length < MaxLogFileSizeBytes)
                        return; // Has space
                }
                
                // All rotations full - delete oldest and shift
                var basePath = Path.Combine(Path.GetDirectoryName(logFile), datePart);
                var oldestFile = $"{basePath}.{MaxRotatedFiles}.txt";
                if (File.Exists(oldestFile))
                    File.Delete(oldestFile);
                
                // Shift files down
                for (int i = MaxRotatedFiles - 1; i >= 1; i--)
                {
                    var srcFile = $"{basePath}.{i}.txt";
                    var dstFile = $"{basePath}.{i + 1}.txt";
                    if (File.Exists(srcFile))
                        File.Move(srcFile, dstFile);
                }
                
                // Move current to .1
                File.Move(logFile, $"{basePath}.1.txt");
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
                if (!Directory.Exists(_baseDir)) return;

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
                        var path = Path.Combine(_baseDir, file);
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
                if (!Directory.Exists(_baseDir)) return;

                var files = Directory.GetFiles(_baseDir, "*.txt");
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
        public static string Get_baseDir() => _baseDir;

        /// <summary>
        /// List all log files with their sizes and last write times.
        /// </summary>
        public static (string name, long size, DateTime lastWrite)[] GetLogFiles()
        {
            try
            {
                if (!Directory.Exists(_baseDir))
                    return Array.Empty<(string, long, DateTime)>();

                return Directory.GetFiles(_baseDir, "*.txt")
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
