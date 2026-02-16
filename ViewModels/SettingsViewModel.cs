using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TaskCore.Services;

namespace TaskCore.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly AppSettings _settings;

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
        private string? selectedAvailableProcess;

        [ObservableProperty]
        private string? selectedWatchedProcess;

        private List<string> _availableAll = new();

        public SettingsViewModel()
        {
            _settings = AppSettingsService.Load();

            AutoStartWithWindows = _settings.AutoStartWithWindows;
            PreferAdminForProtectedActions = _settings.PreferAdminForProtectedActions;
            RefreshIntervalMs = _settings.RefreshIntervalMs;
            WatchdogMode = _settings.WatchdogMode;
            WatchdogEnabled = _settings.WatchdogEnabled;
            WatchdogLoggingEnabled = _settings.WatchdogLoggingEnabled;
            WatchdogSoundAlertEnabled = _settings.WatchdogSoundAlertEnabled;

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