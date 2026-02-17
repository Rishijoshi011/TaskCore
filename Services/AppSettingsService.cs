using System;
using System.IO;
using System.Text.Json;

namespace TaskCore.Services
{
    public static class AppSettingsService
    {
        private static readonly object _lock = new();

        private static readonly string _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskCore");

        private static readonly string _path = Path.Combine(_dir, "settings.json");

        public static AppSettings Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_path))
                        return new AppSettings();

                    var json = File.ReadAllText(_path);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    return settings ?? new AppSettings();
                }
                catch
                {
                    return new AppSettings();
                }
            }
        }

        public static void Save(AppSettings settings)
        {
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(_dir);
                    var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_path, json);
                }
                catch
                {
                }
            }
        }
    }

    public sealed class AppSettings
    {
        public bool AutoStartWithWindows { get; set; } = false;
        public bool PreferAdminForProtectedActions { get; set; } = false;
        public int RefreshIntervalMs { get; set; } = 1000;

        public string WatchdogMode { get; set; } = "Alert Only";
        public bool WatchdogEnabled { get; set; } = false;
        public bool WatchdogSoundAlertEnabled { get; set; } = true;
        public bool WatchdogLoggingEnabled { get; set; } = true;

        public string CustomAlertSoundPath { get; set; } = string.Empty;

        public System.Collections.Generic.List<string>? WatchdogTargets { get; set; } = new();

        public System.Collections.Generic.Dictionary<string, bool>? SessionTrackerSummaryColumns { get; set; } = new();
        public System.Collections.Generic.Dictionary<string, bool>? SessionTrackerSessionColumns { get; set; } = new();
    }
}
