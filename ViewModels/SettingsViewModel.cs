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
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly AppSettings _settings;

        public string AppVersion { get; }

        [ObservableProperty]
        private bool autoStartWithWindows;

        [ObservableProperty]
        private bool preferAdminForProtectedActions;

        [ObservableProperty]
        private int refreshIntervalMs;

        [ObservableProperty]
        private string watchdogMode = "Alert Only";

        [ObservableProperty]
        private bool watchdogEnabled;

        [ObservableProperty]
        private bool watchdogLoggingEnabled;

        [ObservableProperty]
        private bool watchdogSoundAlertEnabled;

        [ObservableProperty]
        private string customAlertSoundPath = string.Empty;

        [ObservableProperty]
        private string watchdogLogsDirectory = string.Empty;

        [ObservableProperty]
        private string watchdogTodaysLogFile = string.Empty;

        [ObservableProperty]
        private ObservableCollection<string> watchdogRecentActivity = new();

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

        public ObservableCollection<string> WatchdogModes { get; } = new()
        {
            "Alert Only",
            "Auto Kill",
            "Log Only"
        };

        [ObservableProperty]
        private string watchdogProcessSearch = string.Empty;

        [ObservableProperty]
        private ObservableCollection<string> watchdogAvailableProcesses = new();

        [ObservableProperty]
        private ObservableCollection<string> watchdogWatchedProcesses = new();

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
        private string? selectedWatchedProcess;

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
            PreferAdminForProtectedActions = _settings.PreferAdminForProtectedActions;
            RefreshIntervalMs = _settings.RefreshIntervalMs;
            WatchdogMode = _settings.WatchdogMode;
            WatchdogEnabled = _settings.WatchdogEnabled;
            WatchdogLoggingEnabled = _settings.WatchdogLoggingEnabled;
            WatchdogSoundAlertEnabled = _settings.WatchdogSoundAlertEnabled;
            CustomAlertSoundPath = _settings.CustomAlertSoundPath ?? string.Empty;

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

            if (!WatchdogModes.Contains(WatchdogMode))
                WatchdogMode = "Alert Only";

            try
            {
                WatchdogWatchedProcesses = new ObservableCollection<string>(
                    (_settings.WatchdogTargets ?? new List<string>())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(s => s));
            }
            catch
            {
                WatchdogWatchedProcesses = new ObservableCollection<string>();
            }

            RefreshWatchdogProcessList();

            ProcessFilterMode = _settings.ProcessFilterMode;
            if (ProcessFilterMode < 0 || ProcessFilterMode > 2) ProcessFilterMode = 1;
            
            SelectedProcesses = new ObservableCollection<string>(
                (_settings.SelectedProcesses ?? new List<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s));

            RefreshAvailableForSelection();
            StartRefreshTimer();

            try
            {
                WatchdogLogsDirectory = WatchdogLogger.LogDirectory;
                WatchdogTodaysLogFile = WatchdogLogger.TodaysLogFilePath;

                var svc = WatchdogService.Instance;
                svc.ActivityChanged += OnWatchdogActivityChanged;
                RefreshActivityFromService();
            }
            catch
            {
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
            StartupRegistryService.SetEnabled(value);
            AppSettingsService.Save(_settings);
        }

        partial void OnPreferAdminForProtectedActionsChanged(bool value)
        {
            _settings.PreferAdminForProtectedActions = value;
            AppSettingsService.Save(_settings);
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

        partial void OnWatchdogModeChanged(string value)
        {
            _settings.WatchdogMode = value;
            AppSettingsService.Save(_settings);
        }

        partial void OnWatchdogEnabledChanged(bool value)
        {
            _settings.WatchdogEnabled = value;
            AppSettingsService.Save(_settings);
        }

        partial void OnWatchdogLoggingEnabledChanged(bool value)
        {
            _settings.WatchdogLoggingEnabled = value;
            AppSettingsService.Save(_settings);
        }

        partial void OnWatchdogSoundAlertEnabledChanged(bool value)
        {
            _settings.WatchdogSoundAlertEnabled = value;
            AppSettingsService.Save(_settings);
        }

        partial void OnCustomAlertSoundPathChanged(string value)
        {
            _settings.CustomAlertSoundPath = value;
            AppSettingsService.Save(_settings);
        }

        partial void OnWatchdogProcessSearchChanged(string value)
        {
            ApplyProcessFilter();
        }

        [RelayCommand]
        private void RelaunchAsAdmin()
        {
            ElevationService.TryRelaunchAsAdminAndExit();
        }

        [RelayCommand]
        private void RefreshWatchdogProcessList()
        {
            try
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        var n = p.ProcessName;
                        if (string.IsNullOrWhiteSpace(n)) continue;
                        names.Add(n + ".exe");
                    }
                    catch
                    {
                    }
                }

                _availableAll = names.OrderBy(x => x).ToList();
                ApplyProcessFilter();
            }
            catch
            {
                _availableAll = new List<string>();
                ApplyProcessFilter();
            }
        }

        private void ApplyProcessFilter()
        {
            try
            {
                var q = WatchdogProcessSearch ?? string.Empty;
                var list = _availableAll;
                if (!string.IsNullOrWhiteSpace(q))
                    list = list.Where(x => x.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

                WatchdogAvailableProcesses = new ObservableCollection<string>(list);
            }
            catch
            {
            }
        }

        [RelayCommand]
        private void AddWatchedProcess()
        {
            var name = SelectedAvailableProcess;
            if (string.IsNullOrWhiteSpace(name)) return;

            if (WatchdogWatchedProcesses.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)))
                return;

            WatchdogWatchedProcesses.Add(name);
            SaveWatchdogTargets();
        }

        [RelayCommand]
        private void RemoveWatchedProcess()
        {
            var name = SelectedWatchedProcess;
            if (string.IsNullOrWhiteSpace(name)) return;

            var match = WatchdogWatchedProcesses.FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
            if (match == null) return;

            WatchdogWatchedProcesses.Remove(match);
            SaveWatchdogTargets();
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

        private void SaveWatchdogTargets()
        {
            try
            {
                _settings.WatchdogTargets = WatchdogWatchedProcesses
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

        private void OnWatchdogActivityChanged()
        {
            RefreshActivityFromService();
        }

        private void RefreshActivityFromService()
        {
            try
            {
                WatchdogTodaysLogFile = WatchdogLogger.TodaysLogFilePath;
                var items = WatchdogService.Instance.GetRecentActivity();
                WatchdogRecentActivity = new ObservableCollection<string>(items);
            }
            catch
            {
            }
        }

        [RelayCommand]
        private void CopyLogsDirectory()
        {
            CopyToClipboard(WatchdogLogsDirectory);
        }

        [RelayCommand]
        private void CopyTodaysLogFile()
        {
            CopyToClipboard(WatchdogTodaysLogFile);
        }

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
                    if (!string.Equals(Path.GetExtension(sourcePath), ".wav", StringComparison.OrdinalIgnoreCase))
                    {
                        CustomAlertSoundPath = sourcePath;
                        return;
                    }

                    var appDir = AppDomain.CurrentDomain.BaseDirectory;
                    var soundsDir = Path.Combine(appDir, "Sounds");
                    Directory.CreateDirectory(soundsDir);

                    var destPath = Path.Combine(soundsDir, "watchdog_alert.wav");
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