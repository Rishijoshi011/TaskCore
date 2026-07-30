using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TaskCore.Services;

namespace TaskCore.ViewModels
{
    public partial class SettingsViewModel : ObservableObject, IDisposable
    {
        private bool _disposed;
        private readonly AppSettings _settings;

        public string AppVersion { get; }

        [ObservableProperty]
        private bool autoStartWithWindows;

        [ObservableProperty]
        private bool startMinimized;

        [ObservableProperty]
        private bool preferAdminForProtectedActions;

        [ObservableProperty]
        private int refreshIntervalMs;

        [ObservableProperty]
        private bool minimizeToTray;

        [ObservableProperty]
        private bool alwaysRunAsAdministrator;

        [ObservableProperty]
        private bool watchdogMode;

        private bool _watchdogModeChanging = false;

        [ObservableProperty]
        private bool automationWatchdogEnabled;

        [ObservableProperty]
        private bool automationRestartExplorerEnabled;

        [ObservableProperty]
        private bool automationCpuMitigationEnabled;

        [ObservableProperty]
        private bool automationEdgeWebViewCleanupEnabled;

        [ObservableProperty]
        private string customAlertSoundPath = string.Empty;

        [ObservableProperty]
        private string accentColor = "#FF9A4DFF";

        [ObservableProperty]
        private string copyStatusText = string.Empty;

        private CancellationTokenSource? _copyCts;

        public ObservableCollection<RefreshIntervalOption> RefreshIntervals { get; } = new()
        {
            new RefreshIntervalOption(500, "500 ms"),
            new RefreshIntervalOption(1000, "1 sec"),
            new RefreshIntervalOption(2000, "2 sec"),
            new RefreshIntervalOption(5000, "5 sec"),
        };

        [ObservableProperty]
        private int processFilterMode = 1;

        [ObservableProperty]
        private ObservableCollection<string> selectedProcesses = new();

        [ObservableProperty]
        private ObservableCollection<string> availableForSelection = new();

        [ObservableProperty]
        private string? selectedProcessToRemove;

        [ObservableProperty]
        private string? selectedAvailableProcess;

        [ObservableProperty]
        private string? selectedProcessSearch = string.Empty;

        partial void OnSelectedProcessSearchChanged(string? value)
        {
            ApplySelectedProcessFilter();
        }

        private System.Windows.Threading.DispatcherTimer? _refreshTimer;
        private List<string> _availableAll = new();

        public SettingsViewModel()
        {
            _settings = AppSettingsService.Load();

            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                AppVersion = v != null ? $"v{v.Major}.{v.Minor}.{v.Build}" : "v1.0.0";
            }
            catch
            {
                AppVersion = "v1.0.0";
            }

            AutoStartWithWindows = _settings.AutoStartWithWindows;
            StartMinimized = _settings.StartMinimized;
            PreferAdminForProtectedActions = _settings.PreferAdminForProtectedActions;
            // Sync AlwaysRunAsAdministrator with actual registry state (in case user manually changed it)
            AlwaysRunAsAdministrator = AdminRegistryService.IsEnabled();
            _settings.AlwaysRunAsAdministrator = AlwaysRunAsAdministrator;
            RefreshIntervalMs = _settings.RefreshIntervalMs;
            MinimizeToTray = _settings.MinimizeToTray;
            WatchdogMode = _settings.WatchdogMode;
            AutomationWatchdogEnabled = _settings.AutomationWatchdogEnabled;
            AutomationRestartExplorerEnabled = _settings.AutomationRestartExplorerEnabled;
            AutomationCpuMitigationEnabled = _settings.AutomationCpuMitigationEnabled;
            AutomationEdgeWebViewCleanupEnabled = _settings.AutomationEdgeWebViewCleanupEnabled;
            CustomAlertSoundPath = _settings.CustomAlertSoundPath ?? string.Empty;
            AccentColor = _settings.AccentColor ?? "#FF9A4DFF";

            try
            {
                AutoStartWithWindows = StartupRegistryService.IsEnabled();
            }
            catch
            {
            }

            if (!RefreshIntervals.Any(x => x.ValueMs == RefreshIntervalMs))
                RefreshIntervalMs = 1000;

            try
            {
                RefreshIntervalService.Set(RefreshIntervalMs);
            }
            catch
            {
            }

            ProcessFilterMode = _settings.ProcessFilterMode;
            if (ProcessFilterMode < 0 || ProcessFilterMode > 2) ProcessFilterMode = 1;
            
            SelectedProcesses = new ObservableCollection<string>(
                (_settings.SelectedProcesses ?? new List<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s));

            // Only start process monitoring timer in normal mode
            if (!_settings.WatchdogMode)
            {
                RefreshAvailableForSelection();
                StartRefreshTimer();
            }
        }

        private void StartRefreshTimer()
        {
            _refreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _refreshTimer.Tick += (_, __) => ApplySelectedProcessFilter();
            _refreshTimer.Start();
        }

        private void ApplySelectedProcessFilter()
        {
            try
            {
                var q = SelectedProcessSearch ?? string.Empty;
                var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in Process.GetProcesses())
                {
                    try { running.Add(p.ProcessName); } catch { }
                }

                var selectedSet = new HashSet<string>(SelectedProcesses ?? new ObservableCollection<string>(), StringComparer.OrdinalIgnoreCase);
                var available = running
                    .Where(r => !selectedSet.Contains(r))
                    .Where(r => string.IsNullOrWhiteSpace(q) || r.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(r => r)
                    .ToList();

                AvailableForSelection = new ObservableCollection<string>(available);
            }
            catch
            {
                AvailableForSelection = new ObservableCollection<string>();
            }
        }

        partial void OnProcessFilterModeChanged(int value)
        {
            _settings.ProcessFilterMode = value;
            AppSettingsService.Save(_settings);
        }

        [RelayCommand]
        private void ResetApplicationData()
        {
            var result = MessageBox.Show(
                "Are you sure you want to reset all application data?\n\nThis will clear all logs, cached metrics, session history, and restore default settings.",
                "Reset Application Data",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                RuntimeStorageCleaner.ResetApplicationData();
                MessageBox.Show("TaskCore runtime data has been reset to default state.", "Reset Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        partial void OnSelectedProcessesChanged(ObservableCollection<string> value)
        {
            _settings.SelectedProcesses = value
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s)
                .ToList();

            AppSettingsService.Save(_settings);
            RefreshAvailableForSelection();
        }

        partial void OnAutoStartWithWindowsChanged(bool value)
        {
            _settings.AutoStartWithWindows = value;
            StartupRegistryService.SetEnabled(value, _settings.StartMinimized);
            AppSettingsService.Save(_settings);
        }

        partial void OnStartMinimizedChanged(bool value)
        {
            _settings.StartMinimized = value;
            if (_settings.AutoStartWithWindows)
            {
                StartupRegistryService.SetEnabled(true, value);
            }
            AppSettingsService.Save(_settings);
        }

        partial void OnPreferAdminForProtectedActionsChanged(bool value)
        {
            _settings.PreferAdminForProtectedActions = value;
            AppSettingsService.Save(_settings);
        }

        partial void OnAlwaysRunAsAdministratorChanged(bool value)
        {
            _settings.AlwaysRunAsAdministrator = value;
            AppSettingsService.Save(_settings);

            // Set/clear registry RUNASADMIN flag - this persists across reboots
            AdminRegistryService.SetEnabled(value);

            // If enabling and not currently admin, we need one restart to apply
            // After that, registry will handle it automatically
            if (value && !ElevationService.IsRunningAsAdmin())
            {
                var result = MessageBox.Show(
                    "Always run as administrator is now enabled.\n\n" +
                    "TaskCore will now always start as administrator (UAC prompt on launch).\n\n" +
                    "Restart now to apply?",
                    "Restart Required",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ElevationService.TryRelaunchAsAdminAndExit();
                }
            }
            else if (!value && ElevationService.IsRunningAsAdmin())
            {
                // Disabling while running as admin - inform user it takes effect next launch
                MessageBox.Show(
                    "Always run as administrator has been disabled.\n\n" +
                    "This will take effect the next time you start TaskCore.",
                    "Setting Saved",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        partial void OnRefreshIntervalMsChanged(int value)
        {
            _settings.RefreshIntervalMs = value;
            AppSettingsService.Save(_settings);

            try
            {
                RefreshIntervalService.Set(value);
            }
            catch
            {
            }
        }

        partial void OnMinimizeToTrayChanged(bool value)
        {
            _settings.MinimizeToTray = value;
            AppSettingsService.Save(_settings);
        }

        partial void OnAutomationWatchdogEnabledChanged(bool value)
        {
            _settings.AutomationWatchdogEnabled = value;
            AppSettingsService.Save(_settings);

            try
            {
                AutomationWatchdogService.Instance.StartOrStop();
            }
            catch
            {
            }
        }

        partial void OnAutomationRestartExplorerEnabledChanged(bool value)
        {
            _settings.AutomationRestartExplorerEnabled = value;
            AppSettingsService.Save(_settings);
        }

        partial void OnAutomationCpuMitigationEnabledChanged(bool value)
        {
            _settings.AutomationCpuMitigationEnabled = value;
            AppSettingsService.Save(_settings);
        }

        partial void OnAutomationEdgeWebViewCleanupEnabledChanged(bool value)
        {
            _settings.AutomationEdgeWebViewCleanupEnabled = value;
            AppSettingsService.Save(_settings);
        }

        partial void OnWatchdogModeChanging(bool value)
        {
            // Prevent re-entrancy
            if (_watchdogModeChanging) return;
            _watchdogModeChanging = true;

            try
            {
                // Only prompt if value actually changed from saved setting
                if (_settings.WatchdogMode == value)
                {
                    _watchdogModeChanging = false;
                    return;
                }

                _settings.WatchdogMode = value;
                AppSettingsService.Save(_settings);

                // Prompt user to restart
                var result = MessageBox.Show(
                    "Watchdog Mode setting has been changed.\n\n" +
                    (value
                        ? "The application will now run with only Rules and Settings tabs to reduce memory usage (~100MB)."
                        : "The application will now run with all tabs enabled.") +
                    "\n\nRestart now to apply changes?",
                    "Restart Required",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Assembly.GetExecutingAssembly().Location;
                    Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
                    Application.Current.Shutdown();
                }
            }
            finally
            {
                _watchdogModeChanging = false;
            }
        }

        partial void OnCustomAlertSoundPathChanged(string value)
        {
            _settings.CustomAlertSoundPath = value;
            AppSettingsService.Save(_settings);
        }

        partial void OnAccentColorChanged(string value)
        {
            // Normalize hex color to 8-digit format (#AARRGGBB)
            var normalized = NormalizeHexColor(value);
            if (normalized != value)
            {
                AccentColor = normalized;
                return; // Setter will be called again with normalized value
            }

            _settings.AccentColor = normalized;
            AppSettingsService.Save(_settings);
            ThemeService.ApplyAccentColor(normalized);
        }

        /// <summary>
        /// Normalizes hex color to #AARRGGBB format. Returns original if invalid.
        /// </summary>
        private static string NormalizeHexColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return "#FF9A4DFF"; // Default purple

            hex = hex.Trim();

            // Handle # prefix
            if (hex.StartsWith("#"))
                hex = hex.Substring(1);

            // Handle 3-digit hex (RGB) - expand to 6-digit
            if (hex.Length == 3)
            {
                hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
            }

            // Handle 6-digit hex (RRGGBB) - add FF alpha
            if (hex.Length == 6)
            {
                hex = "FF" + hex;
            }

            // Validate 8-digit hex
            if (hex.Length != 8 || !hex.All(c => "0123456789ABCDEFabcdef".Contains(c)))
                return "#FF9A4DFF"; // Default purple if invalid

            return "#" + hex.ToUpperInvariant();
        }

        [ObservableProperty]
        private string relaunchStatusText = string.Empty;

        [RelayCommand]
        private void RelaunchAsAdmin()
        {
            try
            {
                var success = ElevationService.TryRelaunchAsAdminAndExit();
                if (!success)
                {
                    RelaunchStatusText = "Failed to relaunch as admin";
                    MessageBox.Show(
                        "Failed to relaunch as administrator.\n\n" +
                        "Possible reasons:\n" +
                        "• User cancelled the UAC prompt\n" +
                        "• Antivirus blocked the operation\n" +
                        "• Executable path not found",
                        "Relaunch Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                RelaunchStatusText = $"Error: {ex.Message}";
                MessageBox.Show(
                    $"Failed to relaunch as administrator:\n\n{ex.Message}",
                    "Relaunch Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void SetPresetAccentColor(string? colorHex)
        {
            if (!string.IsNullOrWhiteSpace(colorHex))
            {
                AccentColor = colorHex;
            }
        }

        [RelayCommand]
        private void AddSelectedProcess()
        {
            var name = SelectedAvailableProcess;
            if (string.IsNullOrWhiteSpace(name)) return;

            if (SelectedProcesses.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                return;

            SelectedProcesses.Add(name);
            SaveSelectedProcesses();
            RefreshAvailableForSelection();
            OnPropertyChanged(nameof(SelectedProcesses));
        }

        [RelayCommand]
        private void RemoveSelectedProcess()
        {
            var name = SelectedProcessToRemove;
            if (string.IsNullOrWhiteSpace(name)) return;

            var match = SelectedProcesses.FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
            if (match == null) return;

            SelectedProcesses.Remove(match);
            SaveSelectedProcesses();
            RefreshAvailableForSelection();
            OnPropertyChanged(nameof(SelectedProcesses));
        }

        private void SaveSelectedProcesses()
        {
            try
            {
                _settings.SelectedProcesses = SelectedProcesses
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s)
                    .ToList();

                AppSettingsService.Save(_settings);
            }
            catch
            {
            }
        }

        private void RefreshAvailableForSelection() => ApplySelectedProcessFilter();

        [RelayCommand]
        private void BrowseCustomAlertSound()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Alert Sound",
                Filter = "WAV Audio (*.wav)|*.wav|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var sourcePath = dialog.FileName;
                    var soundsDir = Path.Combine(AppSettingsService.BaseDirectory, "Sounds");
                    Directory.CreateDirectory(soundsDir);

                    var ext = Path.GetExtension(sourcePath);
                    if (string.IsNullOrWhiteSpace(ext)) ext = ".wav";
                    var destPath = Path.Combine(soundsDir, $"alert_sound{ext}");
                    File.Copy(sourcePath, destPath, overwrite: true);

                    CustomAlertSoundPath = destPath;
                }
                catch
                {
                    CustomAlertSoundPath = dialog.FileName;
                }
            }
        }

        private void CopyToClipboard(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            try
            {
                Clipboard.SetText(text);
                ShowCopyStatus("Copied!");
            }
            catch
            {
                try
                {
                    Clipboard.SetDataObject(text);
                    ShowCopyStatus("Copied!");
                }
                catch
                {
                    ShowCopyStatus("Copy failed");
                }
            }
        }

        private void ShowCopyStatus(string msg)
        {
            try
            {
                CopyStatusText = msg;

                _copyCts?.Cancel();
                _copyCts = new CancellationTokenSource();
                var token = _copyCts.Token;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1500, token);
                        if (token.IsCancellationRequested) return;

                        Application.Current?.Dispatcher?.BeginInvoke(() =>
                        {
                            try { CopyStatusText = string.Empty; } catch { }
                        });
                    }
                    catch
                    {
                    }
                });
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop timer
            try { _refreshTimer?.Stop(); } catch { }

            // Cancel any pending copy status task
            try { _copyCts?.Cancel(); } catch { }
            try { _copyCts?.Dispose(); } catch { }
        }
    }

    public sealed class RefreshIntervalOption
    {
        public int ValueMs { get; }
        public string Label { get; }

        public RefreshIntervalOption(int valueMs, string label)
        {
            ValueMs = valueMs;
            Label = label;
        }
    }
}