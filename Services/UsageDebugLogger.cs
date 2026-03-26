using System;
using System.IO;

namespace TaskCore.Services
{
    /// <summary>
    /// Simple debug logger for usage tracking diagnostics.
    /// Only logs when DEBUG compilation symbol is defined.
    /// All methods are no-ops in release builds via Conditional attribute.
    /// </summary>
    internal static class UsageDebugLogger
    {
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogFilter(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(UsageTrackingStorage.BaseDirectory, "debug-filter.txt"),
                    $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogTick(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(UsageTrackingStorage.BaseDirectory, "debug-tick.txt"),
                    $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogTrack(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(UsageTrackingStorage.BaseDirectory, "debug-track.txt"),
                    $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch { }
        }
    }
}
