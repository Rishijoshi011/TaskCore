using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TaskCore.Services;
using System.Text.Json;
using System.IO;

namespace TaskCore.ViewModels
{
    public partial class DetailsViewModel : ObservableObject, IDisposable
    {
        private bool _disposed;
        private readonly AppSettings _settings;

        private readonly DispatcherTimer _refreshTimer;
        private int _refreshInFlight;

        private List<SessionSummaryItem> _allAppSummaries = new();
        private List<SessionRecordItem> _allSessions = new();

        [ObservableProperty]
        private ObservableCollection<CurrentlyRunningItem> currentlyRunning = new();

        [ObservableProperty]
        private DateTime selectedDate = DateTime.Today;

        [ObservableProperty]
        private DateTime startDate = DateTime.Today.AddDays(-7);

        [ObservableProperty]
        private DateTime endDate = DateTime.Today;

        [ObservableProperty]
        private string searchQuery = string.Empty;

        [ObservableProperty]
        private ObservableCollection<SessionSummaryItem> appSummaries = new();

        [ObservableProperty]
        private ObservableCollection<SessionRecordItem> sessions = new();

        [ObservableProperty]
        private ObservableCollection<SessionSummaryItem> pagedAppSummaries = new();

        [ObservableProperty]
        private ObservableCollection<SessionRecordItem> pagedSessions = new();

        [ObservableProperty]
        private int summaryPageIndex;

        [ObservableProperty]
        private int sessionsPageIndex;

        public int SummaryPageSize { get; } = 12;
        public int SessionsPageSize { get; } = 20;

        public int SummaryTotalPages => Math.Max(1, (int)Math.Ceiling((_allAppSummaries.Count) / (double)SummaryPageSize));
        public int SessionsTotalPages => Math.Max(1, (int)Math.Ceiling((_allSessions.Count) / (double)SessionsPageSize));

        public string SummaryPageText => $"Page {Math.Min(SummaryTotalPages, SummaryPageIndex + 1)} / {SummaryTotalPages}";
        public string SessionsPageText => $"Page {Math.Min(SessionsTotalPages, SessionsPageIndex + 1)} / {SessionsTotalPages}";

        [ObservableProperty]
        private string topAppName = "-";

        [ObservableProperty]
        private string topAppSessionsText = "-";

        [ObservableProperty]
        private string longestSessionText = "-";

        [ObservableProperty]
        private string filterModeText = "Exclude Windows";

        [ObservableProperty]
        private string copyStatusText = string.Empty;

        private CancellationTokenSource? _copyCts;

        public string SessionLogsDirectory => UsageTrackingService.GetBaseDirectory();

        // Helper method to safely extract values from JsonElement
        private static T SafeGetValue<T>(Dictionary<string, object> dict, string key, Func<JsonElement, T> converter, T defaultValue = default!) where T : notnull
        {
            try
            {
                if (dict.TryGetValue(key, out var value) && value is JsonElement element)
                {
                    return converter(element);
                }
            }
            catch
            {
                // Return default on any error
            }
            return defaultValue;
        }

        private static int SafeGetInt(Dictionary<string, object> dict, string key)
        {
            if (!dict.TryGetValue(key, out var val)) return 0;
            if (val is JsonElement el)
            {
                if (el.ValueKind == JsonValueKind.Number) return el.GetInt32();
                if (int.TryParse(el.GetString(), out var parsed)) return parsed;
                return 0;
            }
            if (int.TryParse(val?.ToString(), out var i)) return i;
            return 0;
        }

        private static double SafeGetDouble(Dictionary<string, object> dict, string key)
        {
            if (!dict.TryGetValue(key, out var val)) return 0.0;
            if (val is JsonElement el)
            {
                if (el.ValueKind == JsonValueKind.Number) return el.GetDouble();
                if (double.TryParse(el.GetString(), out var parsed)) return parsed;
                return 0.0;
            }
            if (double.TryParse(val?.ToString(), out var d)) return d;
            return 0.0;
        }

        private static DateTime SafeGetDateTime(Dictionary<string, object> dict, string key)
        {
            if (!dict.TryGetValue(key, out var val)) return DateTime.MinValue;
            if (val is JsonElement el)
            {
                if (el.ValueKind == JsonValueKind.String) return DateTime.Parse(el.GetString()!);
                return DateTime.MinValue;
            }
            if (DateTime.TryParse(val?.ToString(), out var dt)) return dt;
            return DateTime.MinValue;
        }

        private static string SafeGetString(Dictionary<string, object> dict, string key)
        {
            if (!dict.TryGetValue(key, out var val)) return string.Empty;
            if (val is JsonElement el)
            {
                if (el.ValueKind == JsonValueKind.String) return el.GetString() ?? string.Empty;
                return string.Empty;
            }
            return val?.ToString() ?? string.Empty;
        }

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

        private void CoercePageIndexes()
        {
            try
            {
                var maxSummary = Math.Max(0, SummaryTotalPages - 1);
                var maxSessions = Math.Max(0, SessionsTotalPages - 1);

                if (SummaryPageIndex > maxSummary) SummaryPageIndex = maxSummary;
                if (SessionsPageIndex > maxSessions) SessionsPageIndex = maxSessions;
                if (SummaryPageIndex < 0) SummaryPageIndex = 0;
                if (SessionsPageIndex < 0) SessionsPageIndex = 0;
            }
            catch
            {
            }
        }

        private void ApplyPaging()
        {
            try
            {
                var summarySkip = SummaryPageIndex * SummaryPageSize;
                var sessionsSkip = SessionsPageIndex * SessionsPageSize;

                PagedAppSummaries = new ObservableCollection<SessionSummaryItem>(
                    _allAppSummaries.Skip(summarySkip).Take(SummaryPageSize));

                PagedSessions = new ObservableCollection<SessionRecordItem>(
                    _allSessions.Skip(sessionsSkip).Take(SessionsPageSize));

                OnPropertyChanged(nameof(SummaryTotalPages));
                OnPropertyChanged(nameof(SessionsTotalPages));
                OnPropertyChanged(nameof(SummaryPageText));
                OnPropertyChanged(nameof(SessionsPageText));
            }
            catch
            {
            }
        }

        partial void OnSummaryPageIndexChanged(int value)
        {
            try
            {
                CoercePageIndexes();
                ApplyPaging();
            }
            catch
            {
            }
        }

        partial void OnSessionsPageIndexChanged(int value)
        {
            try
            {
                CoercePageIndexes();
                ApplyPaging();
            }
            catch
            {
            }
        }

        [RelayCommand]
        private void SummaryPrevPage()
        {
            if (SummaryPageIndex > 0) SummaryPageIndex--;
        }

        [RelayCommand]
        private void SummaryNextPage()
        {
            if (SummaryPageIndex < (SummaryTotalPages - 1)) SummaryPageIndex++;
        }

        [RelayCommand]
        private void SessionsPrevPage()
        {
            if (SessionsPageIndex > 0) SessionsPageIndex--;
        }

        [RelayCommand]
        private void SessionsNextPage()
        {
            if (SessionsPageIndex < (SessionsTotalPages - 1)) SessionsPageIndex++;
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

        [ObservableProperty] private bool showRunningApp = true;
        [ObservableProperty] private bool showRunningPid = true;
        [ObservableProperty] private bool showRunningStart = true;
        [ObservableProperty] private bool showRunningDuration = true;
        [ObservableProperty] private bool showRunningCpu = true;
        [ObservableProperty] private bool showRunningMem = true;

        public DetailsViewModel()
        {
            _settings = AppSettingsService.Load();
            LoadColumnPreferences();

            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += (_, __) => RefreshIfActive();
            AppViewStateService.ViewChanged += OnViewChanged;
            if (string.Equals(AppViewStateService.CurrentView, "Details", StringComparison.OrdinalIgnoreCase))
                _refreshTimer.Start();

            Refresh();
        }

        private void OnViewChanged(string viewName)
        {
            try
            {
                if (string.Equals(viewName, "Details", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_refreshTimer.IsEnabled)
                        _refreshTimer.Start();
                    Refresh();
                }
                else
                {
                    if (_refreshTimer.IsEnabled)
                        _refreshTimer.Stop();
                }
            }
            catch
            {
            }
        }

        private void RefreshIfActive()
        {
            try
            {
                if (!string.Equals(AppViewStateService.CurrentView, "Details", StringComparison.OrdinalIgnoreCase))
                    return;

                if (!DateOnly.FromDateTime(SelectedDate).Equals(DateOnly.FromDateTime(DateTime.Today)))
                    return;

                if (Interlocked.Exchange(ref _refreshInFlight, 1) == 1)
                    return;

                try
                {
                    Refresh();
                }
                finally
                {
                    Interlocked.Exchange(ref _refreshInFlight, 0);
                }
            }
            catch
            {
            }
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

        // Date navigation properties - match Dashboard behavior
        [ObservableProperty]
        private bool canGoToPreviousDate;

        [ObservableProperty]
        private bool canGoToNextDate;

        partial void OnSelectedDateChanged(DateTime value)
        {
            UpdateDateNavigation();
            Refresh();
        }

        private void UpdateDateNavigation()
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var selectedDateOnly = DateOnly.FromDateTime(SelectedDate);

            // Can go to previous date if we're not before the 7-day window
            CanGoToPreviousDate = selectedDateOnly > DateOnly.FromDateTime(DateTime.Today.AddDays(-7));

            // Can go to next date if we haven't reached today
            CanGoToNextDate = selectedDateOnly < today;
        }

        [RelayCommand]
        private void GoToPreviousDate()
        {
            if (CanGoToPreviousDate)
            {
                SelectedDate = SelectedDate.AddDays(-1);
            }
        }

        [RelayCommand]
        private void GoToNextDate()
        {
            if (CanGoToNextDate)
            {
                SelectedDate = SelectedDate.AddDays(1);
            }
        }

        [RelayCommand]
        private void GoToToday()
        {
            SelectedDate = DateTime.Today;
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
                var sessions = UsageTrackingService.GetDailySessions(date);

                // Update currently running apps (real-time)
                var running = UsageTrackingService.Instance.GetCurrentlyRunning();
                CurrentlyRunning = new ObservableCollection<CurrentlyRunningItem>(
                    running.Select(r => new CurrentlyRunningItem
                    {
                        AppName = r.appName,
                        ProcessId = r.firstPid,
                        StartTimeLocal = r.startTime,
                        CurrentCpuPercent = r.avgCpu,
                        CurrentMemoryMb = r.avgMem,
                        ProcessPath = r.path
                    }).OrderBy(x => x.AppName)
                );

                // DEBUG: write session count and first entry to a debug file
                try
                {
                    var debugFile = Path.Combine(UsageTrackingService.GetBaseDirectory(), "debug-refresh.txt");
                    var lines = new List<string>
                    {
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Refresh for date {date:yyyy-MM-dd}",
                        $"Sessions count: {sessions.Count}"
                    };
                    if (sessions.Count > 0)
                    {
                        var first = sessions.First();
                        lines.Add($"First session sample: app={first.app}, pid={first.processId}");
                    }
                    File.AppendAllLines(debugFile, lines);
                }
                catch { }

                // Apply search filter
                var q = SearchQuery ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(q))
                {
                    sessions = sessions.Where(s => 
                        (s.app ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                // Apply ProcessFilterMode to App Summary (same logic as Currently Running)
                var settings = AppSettingsService.Load();
                var filterMode = settings.ProcessFilterMode;
                var selectedProcesses = settings.SelectedProcesses ?? new List<string>();

                // Update FilterModeText for UI display
                FilterModeText = filterMode switch
                {
                    0 => "All Processes",
                    1 => "Exclude Windows",
                    2 => $"Selected Only ({selectedProcesses.Count} apps)",
                    _ => "Exclude Windows"
                };

                if (filterMode == 2) // SelectedOnly
                {
                    sessions = sessions.Where(s => 
                        selectedProcesses.Contains(s.app ?? string.Empty, StringComparer.OrdinalIgnoreCase)).ToList();
                }
                else if (filterMode == 1) // ExcludeWindows (default)
                {
                    sessions = sessions.Where(s => 
                        !ProcessFilter.IsSystemAppName(s.app ?? string.Empty)).ToList();
                }
                // mode 0 (All) - no filtering

                // Group sessions by app for summaries
                var appGroups = sessions.GroupBy(s => string.IsNullOrWhiteSpace(s.app) ? "Unknown" : s.app)
                                       .ToDictionary(g => g.Key, g => g.ToList());

                // Update app summaries
                _allAppSummaries = appGroups.Select(kvp => new SessionSummaryItem
                    {
                        AppName = kvp.Key,
                        ProcessPath = kvp.Value.FirstOrDefault()?.processPath,
                        SessionCount = kvp.Value.Count,
                        LongestSessionSeconds = kvp.Value.Count > 0 ? kvp.Value.Max(s => s.durationSeconds) : 0,
                        PeakCpuPercent = kvp.Value.Count > 0 ? kvp.Value.Average(s => s.peakCpuPercent) : 0,
                        AverageCpuPercent = kvp.Value.Count > 0 ? kvp.Value.Average(s => s.averageCpuPercent) : 0,
                        PeakMemoryMb = kvp.Value.Count > 0 ? kvp.Value.Average(s => s.peakMemoryMB) : 0,
                        AverageMemoryMb = kvp.Value.Count > 0 ? kvp.Value.Average(s => s.averageMemoryMB) : 0
                    })
                    .OrderByDescending(s => s.LongestSessionSeconds)
                    .ToList();

                AppSummaries = new ObservableCollection<SessionSummaryItem>(_allAppSummaries);

                // Update sessions list (all sessions, ordered by start time)
                _allSessions = sessions
                        .OrderByDescending(s => s.startTimeUtc)
                        .Select(s => new SessionRecordItem
                        {
                            AppName = string.IsNullOrWhiteSpace(s.app) ? "Unknown" : s.app,
                            ProcessId = s.processId,
                            ProcessPath = s.processPath,
                            StartTimeLocal = s.startTimeUtc.ToLocalTime(),
                            EndTimeLocal = s.endTimeUtc.ToLocalTime(),
                            DurationSeconds = s.durationSeconds,
                            PeakCpuPercent = s.peakCpuPercent,
                            AverageCpuPercent = s.averageCpuPercent,
                            PeakMemoryMb = s.peakMemoryMB,
                            AverageMemoryMb = s.averageMemoryMB
                        })
                        .ToList();

                Sessions = new ObservableCollection<SessionRecordItem>(_allSessions);

                CoercePageIndexes();
                ApplyPaging();

                // Update summary stats
                UpdateSummaryStats(appGroups);
            }
            catch (Exception ex)
            {
                // Clear data on error
                AppSummaries.Clear();
                Sessions.Clear();
                PagedAppSummaries.Clear();
                PagedSessions.Clear();
                _allAppSummaries = new List<SessionSummaryItem>();
                _allSessions = new List<SessionRecordItem>();
                OnPropertyChanged(nameof(SummaryTotalPages));
                OnPropertyChanged(nameof(SessionsTotalPages));
                OnPropertyChanged(nameof(SummaryPageText));
                OnPropertyChanged(nameof(SessionsPageText));
                UpdateSummaryStats(new Dictionary<string, List<UsageTrackingService.SessionRecord>>());
                
                // Log error
                try
                {
                    var errorFile = Path.Combine(UsageTrackingService.GetBaseDirectory(), "errors.txt");
                    File.AppendAllText(errorFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UI Refresh error: {ex.Message}\n");
                }
                catch { }
            }
        }

        private void UpdateSummaryStats(Dictionary<string, List<UsageTrackingService.SessionRecord>> appGroups)
        {
            if (appGroups.Any())
            {
                // Top app by total runtime
                var topApp = appGroups.OrderByDescending(kvp => kvp.Value.Sum(s => s.durationSeconds)).First();
                TopAppName = topApp.Key;
                TopAppSessionsText = $"{topApp.Value.Count} sessions";

                // Longest single session
                var longestSession = appGroups.SelectMany(kvp => kvp.Value)
                                             .OrderByDescending(s => s.durationSeconds)
                                             .First();
                var duration = longestSession.durationSeconds;
                LongestSessionText = $"{longestSession.app} ({TimeSpan.FromSeconds(duration):hh\\:mm\\:ss})";
            }
            else
            {
                TopAppName = "-";
                TopAppSessionsText = "-";
                LongestSessionText = "-";
            }
        }

        [RelayCommand]
        private void RefreshData()
        {
            try
            {
                // Validate and reload session data for the selected date
                Refresh();
                ShowCopyStatus("Refreshed");
            }
            catch (Exception ex)
            {
                // Handle errors (e.g., file not found, invalid JSON) gracefully
                ShowCopyStatus($"Refresh failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private void ExportToCsv()
        {
            try
            {
                var start = DateOnly.FromDateTime(StartDate);
                var end = DateOnly.FromDateTime(EndDate);
                var csv = UsageTrackingService.ExportToCsv(start, end);
                
                var fileName = $"usage-export-{start:yyyy-MM-dd}-to-{end:yyyy-MM-dd}.csv";
                var dialog = new SaveFileDialog
                {
                    Title = "Export Usage (CSV)",
                    FileName = fileName,
                    DefaultExt = ".csv",
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    AddExtension = true,
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog() != true)
                    return;

                File.WriteAllText(dialog.FileName, csv);

                ShowCopyStatus($"Exported to {Path.GetFileName(dialog.FileName)}");
            }
            catch (Exception ex)
            {
                ShowCopyStatus($"Export failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private void ExportToJson()
        {
            try
            {
                var start = DateOnly.FromDateTime(StartDate);
                var end = DateOnly.FromDateTime(EndDate);
                var json = UsageTrackingService.ExportToJson(start, end);
                
                var fileName = $"usage-export-{start:yyyy-MM-dd}-to-{end:yyyy-MM-dd}.json";
                var dialog = new SaveFileDialog
                {
                    Title = "Export Usage (JSON)",
                    FileName = fileName,
                    DefaultExt = ".json",
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    AddExtension = true,
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog() != true)
                    return;

                File.WriteAllText(dialog.FileName, json);

                ShowCopyStatus($"Exported to {Path.GetFileName(dialog.FileName)}");
            }
            catch (Exception ex)
            {
                ShowCopyStatus($"Export failed: {ex.Message}");
            }
        }

        [RelayCommand]
        private void OpenSettings()
        {
            AppViewStateService.SetView("Settings");
        }

        [RelayCommand]
        private void CleanupOldData()
        {
            try
            {
                UsageTrackingService.CleanupOldFiles();
                ShowCopyStatus("Cleanup completed");
                Refresh(); // Refresh to show updated data
            }
            catch (Exception ex)
            {
                ShowCopyStatus($"Cleanup failed: {ex.Message}");
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

            // Unsubscribe events
            try { AppViewStateService.ViewChanged -= OnViewChanged; } catch { }
        }
    }
}
