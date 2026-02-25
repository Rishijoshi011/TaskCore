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
        private static readonly string _bakPath = Path.Combine(_dir, "settings.json.bak");
        private static readonly string _tmpPath = Path.Combine(_dir, "settings.json.tmp");

        public static AppSettings Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_path))
                    {
                        if (File.Exists(_bakPath))
                        {
                            try
                            {
                                var bakJson = File.ReadAllText(_bakPath);
                                var bakSettings = JsonSerializer.Deserialize<AppSettings>(bakJson);
                                if (bakSettings != null)
                                {
                                    try
                                    {
                                        Directory.CreateDirectory(_dir);
                                        File.WriteAllText(_path, bakJson);
                                    }
                                    catch
                                    {
                                    }

                                    return bakSettings;
                                }
                            }
                            catch
                            {
                            }
                        }

                        return new AppSettings();
                    }

                    var json = File.ReadAllText(_path);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    return settings ?? new AppSettings();
                }
                catch
                {
                    // If settings.json is corrupted/partially written, try recovering from .bak.
                    try
                    {
                        if (File.Exists(_bakPath))
                        {
                            var bakJson = File.ReadAllText(_bakPath);
                            var bakSettings = JsonSerializer.Deserialize<AppSettings>(bakJson);
                            if (bakSettings != null)
                            {
                                try
                                {
                                    Directory.CreateDirectory(_dir);
                                    File.WriteAllText(_path, bakJson);
                                }
                                catch
                                {
                                }

                                return bakSettings;
                            }
                        }
                    }
                    catch
                    {
                    }

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

                    // Write to tmp first to avoid corrupting settings.json if the process is killed mid-write.
                    File.WriteAllText(_tmpPath, json);

                    // Atomically swap tmp into place and keep a .bak.
                    try
                    {
                        File.Replace(_tmpPath, _path, _bakPath, ignoreMetadataErrors: true);
                    }
                    catch
                    {
                        // Fall back if File.Replace fails (e.g. different volume).
                        try
                        {
                            if (File.Exists(_path))
                            {
                                try { File.Copy(_path, _bakPath, overwrite: true); } catch { }
                            }

                            File.Copy(_tmpPath, _path, overwrite: true);
                            try { File.Delete(_tmpPath); } catch { }
                        }
                        catch
                        {
                        }
                    }
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

        // Minimal Watchdog Mode - only Rules and Settings tabs for low memory usage
        public bool WatchdogMode { get; set; } = false;
    }

    public enum ProcessFilterMode
    {
        All = 0,
        ExcludeWindows = 1,
        SelectedOnly = 2
    }
}
