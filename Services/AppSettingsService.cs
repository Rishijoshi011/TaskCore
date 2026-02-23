using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TaskCore.Models;

namespace TaskCore.Services
{
    public static class AppSettingsService
    {
        private static readonly object _lock = new();

        public static string BaseDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskCore");

        private static readonly string _dir = BaseDirectory;

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
        public bool MinimizeToTray { get; set; } = true;

        public string ProcessesSortField { get; set; } = "Cpu";
        public bool ProcessesSortDescending { get; set; } = true;

        // Sound alert path (used by Rules Engine)
        public string CustomAlertSoundPath { get; set; } = string.Empty;
        public bool SoundAlertEnabled { get; set; } = true;
        public string SoundMode { get; set; } = "Default";

        public System.Collections.Generic.Dictionary<string, bool>? SessionTrackerSummaryColumns { get; set; } = new();
        public System.Collections.Generic.Dictionary<string, bool>? SessionTrackerSessionColumns { get; set; } = new();

        // Process filter mode for Currently Running table: 0=All, 1=ExcludeWindows, 2=SelectedOnly
        public int ProcessFilterMode { get; set; } = 1;

        // List of user-selected process names to show when ProcessFilterMode=2
        public System.Collections.Generic.List<string>? SelectedProcesses { get; set; } = new();

        // Rules engine settings
        public List<Rule>? Rules { get; set; } = new();
        public bool RulesEngineEnabled { get; set; } = true;
    }

    public enum ProcessFilterMode
    {
        All = 0,
        ExcludeWindows = 1,
        SelectedOnly = 2
    }
}
