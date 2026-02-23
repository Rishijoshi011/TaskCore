using System;
using System.IO;

namespace TaskCore.Services
{
    /// <summary>
    /// Simple debug logger for usage tracking diagnostics.
    /// Only logs when DEBUG compilation symbol is defined.
    /// </summary>
    internal static class UsageDebugLogger
    {
        public static void LogFilter(string message)
        {
#if DEBUG
            try
            {
                File.AppendAllText(
                    Path.Combine(UsageTrackingStorage.BaseDirectory, "debug-filter.txt"),
                    $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch { }
#endif
        }

        public static void LogTick(string message)
        {
#if DEBUG
            try
            {
                File.AppendAllText(
                    Path.Combine(UsageTrackingStorage.BaseDirectory, "debug-tick.txt"),
                    $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch { }
#endif
        }

        public static void LogTrack(string message)
        {
#if DEBUG
            try
            {
                File.AppendAllText(
                    Path.Combine(UsageTrackingStorage.BaseDirectory, "debug-track.txt"),
                    $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch { }
#endif
        }
    }
}
