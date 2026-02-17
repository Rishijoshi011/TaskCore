using System;
using System.IO;
using System.Text.Json;
using TaskCore.Models;

namespace TaskCore.Services
{
    public static class AppSessionLogService
    {
        private static readonly object _lock = new();

        private static readonly string _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskCore",
            "SessionLogs");

        public static string LogDirectory => _dir;

        public static string GetDayLogPath(DateOnly dateLocal)
        {
            return Path.Combine(_dir, $"sessions_{dateLocal:yyyy-MM-dd}.json");
        }

        public static AppSessionDayLog LoadDay(DateOnly dateLocal)
        {
            lock (_lock)
            {
                try
                {
                    var path = GetDayLogPath(dateLocal);
                    if (!File.Exists(path))
                        return new AppSessionDayLog { DateLocal = dateLocal };

                    var json = File.ReadAllText(path);
                    var log = JsonSerializer.Deserialize<AppSessionDayLog>(json);
                    if (log == null)
                        return new AppSessionDayLog { DateLocal = dateLocal };

                    log.DateLocal = dateLocal;
                    log.Sessions ??= new();
                    return log;
                }
                catch
                {
                    return new AppSessionDayLog { DateLocal = dateLocal };
                }
            }
        }

        public static void AppendSession(AppSessionRecord session)
        {
            var date = DateOnly.FromDateTime(session.StartTimeLocal);

            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(_dir);
                    var log = LoadDay(date);
                    log.Sessions.Add(session);

                    var json = JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(GetDayLogPath(date), json);
                }
                catch
                {
                }
            }
        }
    }
}
