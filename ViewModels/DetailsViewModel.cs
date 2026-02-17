using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TaskCore.Services;

namespace TaskCore.ViewModels
{
    public partial class DetailsViewModel : ObservableObject
    {
        private readonly AppSettings _settings;

        [ObservableProperty]
        private DateTime selectedDate = DateTime.Today;

        [ObservableProperty]
        private string searchQuery = string.Empty;

        [ObservableProperty]
        private ObservableCollection<SessionSummaryItem> appSummaries = new();

        [ObservableProperty]
        private ObservableCollection<SessionRecordItem> sessions = new();

        [ObservableProperty]
        private string topAppName = "-";

        [ObservableProperty]
        private string topAppSessionsText = "-";

        [ObservableProperty]
        private string longestSessionText = "-";

        [ObservableProperty]
        private string copyStatusText = string.Empty;

        private CancellationTokenSource? _copyCts;

        public string SessionLogsDirectory => AppSessionLogService.LogDirectory;

        [RelayCommand]
        private void CopySessionLogsDirectory()
        {
            var text = SessionLogsDirectory;
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

        [ObservableProperty] private bool showSummaryApp = true;
        [ObservableProperty] private bool showSummarySessions = true;
        [ObservableProperty] private bool showSummaryLongest = true;
        [ObservableProperty] private bool showSummaryPeakCpu = true;
        [ObservableProperty] private bool showSummaryAvgCpu = true;
        [ObservableProperty] private bool showSummaryPeakMem = true;
        [ObservableProperty] private bool showSummaryAvgMem = true;

        [ObservableProperty] private bool showSessionApp = true;
        [ObservableProperty] private bool showSessionPid = true;
        [ObservableProperty] private bool showSessionStart = true;
        [ObservableProperty] private bool showSessionEnd = true;
        [ObservableProperty] private bool showSessionDuration = true;
        [ObservableProperty] private bool showSessionPeakCpu = true;
        [ObservableProperty] private bool showSessionAvgCpu = true;
        [ObservableProperty] private bool showSessionPeakMem = true;
        [ObservableProperty] private bool showSessionAvgMem = true;

        public DetailsViewModel()
        {
            _settings = AppSettingsService.Load();
            LoadColumnPreferences();
            Refresh();
        }

        private void LoadColumnPreferences()
        {
            try
            {
                _settings.SessionTrackerSummaryColumns ??= new();
                _settings.SessionTrackerSessionColumns ??= new();

                ShowSummaryApp = GetPref(_settings.SessionTrackerSummaryColumns, "App", true);
                ShowSummarySessions = GetPref(_settings.SessionTrackerSummaryColumns, "Sessions", true);
                ShowSummaryLongest = GetPref(_settings.SessionTrackerSummaryColumns, "Longest", true);
                ShowSummaryPeakCpu = GetPref(_settings.SessionTrackerSummaryColumns, "PeakCpu", true);
                ShowSummaryAvgCpu = GetPref(_settings.SessionTrackerSummaryColumns, "AvgCpu", true);
                ShowSummaryPeakMem = GetPref(_settings.SessionTrackerSummaryColumns, "PeakMem", true);
                ShowSummaryAvgMem = GetPref(_settings.SessionTrackerSummaryColumns, "AvgMem", true);

                ShowSessionApp = GetPref(_settings.SessionTrackerSessionColumns, "App", true);
                ShowSessionPid = GetPref(_settings.SessionTrackerSessionColumns, "PID", true);
                ShowSessionStart = GetPref(_settings.SessionTrackerSessionColumns, "Start", true);
                ShowSessionEnd = GetPref(_settings.SessionTrackerSessionColumns, "End", true);
                ShowSessionDuration = GetPref(_settings.SessionTrackerSessionColumns, "Duration", true);
                ShowSessionPeakCpu = GetPref(_settings.SessionTrackerSessionColumns, "PeakCpu", true);
                ShowSessionAvgCpu = GetPref(_settings.SessionTrackerSessionColumns, "AvgCpu", true);
                ShowSessionPeakMem = GetPref(_settings.SessionTrackerSessionColumns, "PeakMem", true);
                ShowSessionAvgMem = GetPref(_settings.SessionTrackerSessionColumns, "AvgMem", true);
            }
            catch
            {
            }
        }

        private static bool GetPref(System.Collections.Generic.Dictionary<string, bool> dict, string key, bool def)
        {
            try
            {
                return dict.TryGetValue(key, out var v) ? v : def;
            }
            catch
            {
                return def;
            }
        }

        private void SaveColumnPreferences()
        {
            try
            {
                _settings.SessionTrackerSummaryColumns ??= new();
                _settings.SessionTrackerSessionColumns ??= new();

                var s = _settings.SessionTrackerSummaryColumns;
                s["App"] = ShowSummaryApp;
                s["Sessions"] = ShowSummarySessions;
                s["Longest"] = ShowSummaryLongest;
                s["PeakCpu"] = ShowSummaryPeakCpu;
                s["AvgCpu"] = ShowSummaryAvgCpu;
                s["PeakMem"] = ShowSummaryPeakMem;
                s["AvgMem"] = ShowSummaryAvgMem;

                var d = _settings.SessionTrackerSessionColumns;
                d["App"] = ShowSessionApp;
                d["PID"] = ShowSessionPid;
                d["Start"] = ShowSessionStart;
                d["End"] = ShowSessionEnd;
                d["Duration"] = ShowSessionDuration;
                d["PeakCpu"] = ShowSessionPeakCpu;
                d["AvgCpu"] = ShowSessionAvgCpu;
                d["PeakMem"] = ShowSessionPeakMem;
                d["AvgMem"] = ShowSessionAvgMem;

                AppSettingsService.Save(_settings);
            }
            catch
            {
            }
        }

        partial void OnShowSummaryAppChanged(bool value) => SaveColumnPreferences();
        partial void OnShowSummarySessionsChanged(bool value) => SaveColumnPreferences();
        partial void OnShowSummaryLongestChanged(bool value) => SaveColumnPreferences();
        partial void OnShowSummaryPeakCpuChanged(bool value) => SaveColumnPreferences();
        partial void OnShowSummaryAvgCpuChanged(bool value) => SaveColumnPreferences();
        partial void OnShowSummaryPeakMemChanged(bool value) => SaveColumnPreferences();
        partial void OnShowSummaryAvgMemChanged(bool value) => SaveColumnPreferences();

        partial void OnShowSessionAppChanged(bool value) => SaveColumnPreferences();
        partial void OnShowSessionPidChanged(bool value) => SaveColumnPreferences();
        partial void OnShowSessionStartChanged(bool value) => SaveColumnPreferences();
        partial void OnShowSessionEndChanged(bool value) => SaveColumnPreferences();
        partial void OnShowSessionDurationChanged(bool value) => SaveColumnPreferences();
        partial void OnShowSessionPeakCpuChanged(bool value) => SaveColumnPreferences();
        partial void OnShowSessionAvgCpuChanged(bool value) => SaveColumnPreferences();
        partial void OnShowSessionPeakMemChanged(bool value) => SaveColumnPreferences();
        partial void OnShowSessionAvgMemChanged(bool value) => SaveColumnPreferences();

        partial void OnSelectedDateChanged(DateTime value)
        {
            Refresh();
        }

        partial void OnSearchQueryChanged(string value)
        {
            Refresh();
        }

        [RelayCommand]
        private void Refresh()
        {
            try
            {
                var date = DateOnly.FromDateTime(SelectedDate);
                var log = AppSessionLogService.LoadDay(date);
                var list = (log.Sessions ?? new()).ToList();

                var q = SearchQuery ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(q))
                {
                    list = list.Where(s => (s.AppName ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                Sessions = new ObservableCollection<SessionRecordItem>(
                    list
                        .OrderByDescending(s => s.StartTimeLocal)
                        .Select(s => new SessionRecordItem
                        {
                            AppName = s.AppName,
                            ProcessId = s.ProcessId,
                            ProcessPath = s.ProcessPath,
                            StartTimeLocal = s.StartTimeLocal,
                            EndTimeLocal = s.EndTimeLocal,
                            DurationSeconds = s.DurationSeconds,
                            PeakCpuPercent = s.PeakCpuPercent,
                            AverageCpuPercent = s.AverageCpuPercent,
                            PeakMemoryMb = s.PeakMemoryMb,
                            AverageMemoryMb = s.AverageMemoryMb
                        }));

                var summaries = list
                    .GroupBy(s => s.AppName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                    {
                        var sessions = g.ToList();
                        var longest = sessions.Count == 0 ? 0 : sessions.Max(x => x.DurationSeconds);
                        var peakCpu = sessions.Count == 0 ? 0 : sessions.Max(x => x.PeakCpuPercent);
                        var peakMem = sessions.Count == 0 ? 0 : sessions.Max(x => x.PeakMemoryMb);

                        var weightedCpuDen = sessions.Sum(x => Math.Max(1, x.SampleCount));
                        var weightedMemDen = weightedCpuDen;
                        var avgCpu = weightedCpuDen > 0 ? sessions.Sum(x => x.AverageCpuPercent * Math.Max(1, x.SampleCount)) / weightedCpuDen : 0;
                        var avgMem = weightedMemDen > 0 ? sessions.Sum(x => x.AverageMemoryMb * Math.Max(1, x.SampleCount)) / weightedMemDen : 0;

                        return new SessionSummaryItem
                        {
                            AppName = g.Key,
                            SessionCount = sessions.Count,
                            LongestSessionSeconds = longest,
                            PeakCpuPercent = Math.Round(peakCpu, 1),
                            AverageCpuPercent = Math.Round(avgCpu, 1),
                            PeakMemoryMb = Math.Round(peakMem, 1),
                            AverageMemoryMb = Math.Round(avgMem, 1)
                        };
                    })
                    .OrderByDescending(x => x.SessionCount)
                    .ThenByDescending(x => x.PeakCpuPercent)
                    .ToList();

                AppSummaries = new ObservableCollection<SessionSummaryItem>(summaries);

                var top = summaries.FirstOrDefault();
                if (top == null || string.IsNullOrWhiteSpace(top.AppName))
                {
                    TopAppName = "-";
                    TopAppSessionsText = "-";
                    LongestSessionText = "-";
                }
                else
                {
                    TopAppName = top.AppName;
                    TopAppSessionsText = $"{top.SessionCount} sessions";
                    LongestSessionText = top.LongestSessionDisplay;
                }
            }
            catch
            {
                Sessions = new ObservableCollection<SessionRecordItem>();
                AppSummaries = new ObservableCollection<SessionSummaryItem>();
                TopAppName = "-";
                TopAppSessionsText = "-";
                LongestSessionText = "-";
            }
        }
    }
}
