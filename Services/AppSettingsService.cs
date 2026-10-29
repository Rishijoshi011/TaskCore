using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        public static void EnsureDefaultSettingsExist()
        {
            try
            {
                var configDir = Path.Combine(_dir, "Config");
                Directory.CreateDirectory(configDir);
                var configPath = Path.Combine(configDir, "settings.json");

                if (!File.Exists(_path) && !File.Exists(configPath))
                {
                    var defaults = new AppSettings();
                    Save(defaults);
                }
            }
            catch
            {
            }
        }

        public static AppSettings Load()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_path))
                    {
                        TaskCoreLogger.Warn("settings", "settings.json not found, checking backup");
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

                                    TaskCoreLogger.Info("settings", "Settings restored from backup");
                                    return bakSettings;
                                }
                            }
                            catch (Exception ex)
                            {
                                TaskCoreLogger.Error("settings", $"Backup restore failed: {ex.Message}");
                            }
                        }

                        TaskCoreLogger.Info("settings", "Creating new default settings");
                        var newSettings = new AppSettings();
                        Save(newSettings);
                        return newSettings;
                    }

                    var json = File.ReadAllText(_path);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings == null)
                    {
                        throw new JsonException("Deserialized settings is null");
                    }
                    TaskCoreLogger.Info("settings", "Settings loaded successfully");
                    return settings;
                }
                catch (Exception ex)
                {
                    TaskCoreLogger.Error("settings", $"Load failed: {ex.Message}, attempting recovery from backup");
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

                                TaskCoreLogger.Info("settings", "Settings recovered from backup");
                                return bakSettings;
                            }
                        }
                    }
                    catch (Exception ex2)
                    {
                        TaskCoreLogger.Error("settings", $"Recovery failed: {ex2.Message}");
                    }

                    TaskCoreLogger.Warn("settings", "Returning default settings");
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
                    // Validate settings before saving
                    if (!ValidateSettings(settings))
                    {
                        TaskCoreLogger.Error("settings", "Validation failed, aborting save");
                        return;
                    }

                    Directory.CreateDirectory(_dir);

                    var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

                    // Write to tmp first to avoid corrupting settings.json if the process is killed mid-write.
                    File.WriteAllText(_tmpPath, json);
                    TaskCoreLogger.Info("settings", $"Settings written to temp file ({json.Length} bytes)");

                    // Atomically swap tmp into place and keep a .bak.
                    try
                    {
                        File.Replace(_tmpPath, _path, _bakPath, ignoreMetadataErrors: true);
                        TaskCoreLogger.Info("settings", "Settings saved successfully (atomic replace)");
                    }
                    catch (Exception ex)
                    {
                        TaskCoreLogger.Warn("settings", $"File.Replace failed: {ex.Message}, using fallback");
                        // Fall back if File.Replace fails (e.g. different volume).
                        try
                        {
                            if (File.Exists(_path))
                            {
                                try { File.Copy(_path, _bakPath, overwrite: true); } catch { }
                            }

                            File.Copy(_tmpPath, _path, overwrite: true);
                            try { File.Delete(_tmpPath); } catch { }
                            TaskCoreLogger.Info("settings", "Settings saved successfully (fallback copy)");
                        }
                        catch (Exception ex2)
                        {
                            TaskCoreLogger.Error("settings", $"Fallback save failed: {ex2.Message}");
                        }
                    }

                    // Create timestamped backup AFTER successful save
                    CreateTimestampedBackup();
                }
                catch (Exception ex)
                {
                    TaskCoreLogger.Error("settings", $"Save failed: {ex.Message}");
                }
            }
        }

        private static void CreateTimestampedBackup()
        {
            try
            {
                if (!File.Exists(_path)) return;

                var backupDir = Path.Combine(_dir, "SettingsBackups");
                Directory.CreateDirectory(backupDir);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupFile = Path.Combine(backupDir, $"settings_{timestamp}.json");

                File.Copy(_path, backupFile, overwrite: true);
                TaskCoreLogger.Info("settings", $"Timestamped backup created: settings_{timestamp}.json");

                // Keep only last 10 backups
                var backups = Directory.GetFiles(backupDir, "settings_*.json")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .Skip(10);

                foreach (var oldBackup in backups)
                {
                    try { File.Delete(oldBackup); } catch { }
                }
            }
            catch (Exception ex)
            {
                TaskCoreLogger.Error("settings", $"Timestamped backup failed: {ex.Message}");
            }
        }

        public static bool ValidateSettings(AppSettings settings)
        {
            if (settings == null) return false;

            // Validate numeric ranges
            if (settings.RefreshIntervalMs < 100 || settings.RefreshIntervalMs > 60000)
                settings.RefreshIntervalMs = 1000; // Reset to default

            if (settings.ProcessFilterMode < 0 || settings.ProcessFilterMode > 2)
                settings.ProcessFilterMode = 1; // Reset to default

            // Validate automation thresholds
            if (settings.AutomationCpuOffenderThresholdPercent < 50 || settings.AutomationCpuOffenderThresholdPercent > 100)
                settings.AutomationCpuOffenderThresholdPercent = 95;

            if (settings.AutomationCpuOffenderSustainSeconds < 1 || settings.AutomationCpuOffenderSustainSeconds > 300)
                settings.AutomationCpuOffenderSustainSeconds = 10;

            // Validate lists are not null
            settings.SelectedProcesses ??= new System.Collections.Generic.List<string>();
            settings.CustomFilteredProcesses ??= new System.Collections.Generic.List<string>();
            settings.Rules ??= new List<Rule>();

            return true;
        }
    }

    public sealed class AppSettings
    {
        public bool AutoStartWithWindows { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public bool PreferAdminForProtectedActions { get; set; } = false;
        public bool AlwaysRunAsAdministrator { get; set; } = false;
        public int RefreshIntervalMs { get; set; } = 1000;
        public bool MinimizeToTray { get; set; } = true;

        public string ProcessesSortField { get; set; } = "Cpu";
        public bool ProcessesSortDescending { get; set; } = true;

        // Sound alert path (used by Rules Engine)
        public string CustomAlertSoundPath { get; set; } = string.Empty;
        public bool SoundAlertEnabled { get; set; } = true;
        public string SoundMode { get; set; } = "Default";

        // Theme settings
        public string AccentColor { get; set; } = "#FF9A4DFF"; // Default purple

        public System.Collections.Generic.Dictionary<string, bool>? SessionTrackerSummaryColumns { get; set; } = new();
        public System.Collections.Generic.Dictionary<string, bool>? SessionTrackerSessionColumns { get; set; } = new();

        // Process filter mode for Currently Running table: 0=All, 1=ExcludeWindows, 2=SelectedOnly
        public int ProcessFilterMode { get; set; } = 1;

        // List of user-selected process names to show when ProcessFilterMode=2
        public System.Collections.Generic.List<string>? SelectedProcesses { get; set; } = new();

        // User-configurable custom process filter patterns
        public System.Collections.Generic.List<string>? CustomFilteredProcesses { get; set; } = new();
        public bool EnableCustomProcessFilters { get; set; } = false;

        // Rules engine settings
        public List<Rule>? Rules { get; set; } = new();
        public bool RulesEngineEnabled { get; set; } = true;

        // Minimal Watchdog Mode - only Rules and Settings tabs for low memory usage
        public bool WatchdogMode { get; set; } = false;

        // Process Insights / ML.NET Analysis — when disabled, no ML inference or analysis runs.
        // Session tracking, watchdog, graphs and process monitoring are unaffected.
        public bool ProcessInsightsEnabled { get; set; } = true;

        public bool AutomationWatchdogEnabled { get; set; } = false;

        public bool AutomationRestartExplorerEnabled { get; set; } = false;
        public int AutomationExplorerNotRespondingSeconds { get; set; } = 10;
        public int AutomationExplorerRestartCooldownSeconds { get; set; } = 120;

        public bool AutomationCpuMitigationEnabled { get; set; } = false;
        public int AutomationCpuOffenderThresholdPercent { get; set; } = 95;
        public int AutomationCpuOffenderSustainSeconds { get; set; } = 10;
        public int AutomationCpuKillAfterSeconds { get; set; } = 10;
        public int AutomationCpuActionCooldownSeconds { get; set; } = 60;

        public bool AutomationEdgeWebViewCleanupEnabled { get; set; } = false;
        public int AutomationEdgeWebViewMaxInstances { get; set; } = 80;
        public int AutomationEdgeWebViewSoftLimitInstances { get; set; } = 40;
        public int AutomationEdgeWebViewHardLimitInstances { get; set; } = 60;
        public int AutomationEdgeWebViewPanicLimitInstances { get; set; } = 70;
        public int AutomationEdgeWebViewIdleSeconds { get; set; } = 60;
        public int AutomationEdgeWebViewCleanupCooldownSeconds { get; set; } = 30;
        public int AutomationEdgeWebViewMaxKillsPerRun { get; set; } = 3;

        // Automatic Memory Trim — periodically checks system RAM usage and
        // runs the same TrimRam action as the Dashboard Quick Action.
        public bool AutomaticMemoryTrimEnabled { get; set; } = false;
        public int AutomaticMemoryTrimIntervalMinutes { get; set; } = 30;
        public int AutomaticMemoryTrimThresholdPercent { get; set; } = 85;
    }

    public enum ProcessFilterMode
    {
        All = 0,
        ExcludeWindows = 1,
        SelectedOnly = 2
    }
}
