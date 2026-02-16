using System;
using System.IO;

namespace TaskCore.Services
{
    public static class WatchdogLogger
    {
        private static readonly object _lock = new();

        private static readonly string _dir = Path.Combine(AppContext.BaseDirectory, "Logs");
        private static string TodayPath => Path.Combine(_dir, $"watchdog_{DateTime.Now:yyyy-MM-dd}.txt");

        public static string LogDirectory => _dir;

        public static string TodaysLogFilePath => TodayPath;

        public static void Log(string processName, string action)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(_dir);
                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {processName} | {action}";
                    File.AppendAllText(TodayPath, line + Environment.NewLine);
                }
            }
            catch
            {
            }
        }

        public static void LogLine(string line)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(_dir);
                    File.AppendAllText(TodayPath, line + Environment.NewLine);
                }
            }
            catch
            {
            }
        }
    }
}
