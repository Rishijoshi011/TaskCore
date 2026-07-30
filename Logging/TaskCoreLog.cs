using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace TaskCore.Logging
{
    public static class TaskCoreLog
    {
        public static void Info(
            string category,
            string message,
            Exception? exception = null,
            [CallerMemberName] string method = "",
            [CallerFilePath] string filePath = "")
        {
            Log(LogLevel.INFO, category, message, exception, method, filePath);
        }

        public static void Warn(
            string category,
            string message,
            Exception? exception = null,
            [CallerMemberName] string method = "",
            [CallerFilePath] string filePath = "")
        {
            Log(LogLevel.WARN, category, message, exception, method, filePath);
        }

        public static void Error(
            string category,
            string message,
            Exception? exception = null,
            [CallerMemberName] string method = "",
            [CallerFilePath] string filePath = "")
        {
            Log(LogLevel.ERROR, category, message, exception, method, filePath);
        }

        public static void Debug(
            string category,
            string message,
            Exception? exception = null,
            [CallerMemberName] string method = "",
            [CallerFilePath] string filePath = "")
        {
            Log(LogLevel.DEBUG, category, message, exception, method, filePath);
        }

        public static void Fatal(
            string category,
            string message,
            Exception? exception = null,
            [CallerMemberName] string method = "",
            [CallerFilePath] string filePath = "")
        {
            Log(LogLevel.FATAL, category, message, exception, method, filePath);
        }

        public static void Perf(
            string category,
            string message,
            double elapsedMs,
            [CallerMemberName] string method = "",
            [CallerFilePath] string filePath = "")
        {
            Log(LogLevel.PERF, category, $"[{elapsedMs:F1}ms] {message}", null, method, filePath);
        }

        public static void Log(
            LogLevel level,
            string category,
            string message,
            Exception? exception = null,
            [CallerMemberName] string method = "",
            [CallerFilePath] string filePath = "")
        {
            try
            {
                var module = Path.GetFileNameWithoutExtension(filePath);
                var entry = new LogEntry(level, category, module, method, message, exception);
                AsyncLogBuffer.Instance.TryEnqueue(entry);
            }
            catch
            {
            }
        }
    }
}
