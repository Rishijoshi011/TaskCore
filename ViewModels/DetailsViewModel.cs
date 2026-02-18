using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TaskCore.Services;
using System.Text.Json;
using System.IO;

namespace TaskCore.ViewModels
{
    public partial class DetailsViewModel : ObservableObject
    {
        private readonly AppSettings _settings;

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
        private string topAppName = "-";

        [ObservableProperty]
        private string topAppSessionsText = "-";

        [ObservableProperty]
        private string longestSessionText = "-";

        [ObservableProperty]
        private string copyStatusText = string.Empty;

        private CancellationTokenSource? _copyCts;

        public string SessionLogsDirectory => UsageTrackingService.GetBaseDirectory();

        // Helper method to safely extract values from JsonElement
        private static T SafeGetValue<T>(Dictionary<string, object> dict, string key, Func<JsonElement, T> converter, T defaultValue = default)
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
                var sessions = UsageTrackingService.GetDailySessions(date);

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
                        lines.Add("First session keys: " + string.Join(", ", first.Keys));
                        lines.Add($"First session sample: app={first.GetValueOrDefault("app")}, pid={first.GetValueOrDefault("processId")}");
                    }
                    File.AppendAllLines(debugFile, lines);
                }
                catch { }

                // Apply search filter
                var q = SearchQuery ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(q))
                {
                    sessions = sessions.Where(s => 
                        (s["app"]?.ToString() ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                // Group sessions by app for summaries
                var appGroups = sessions.GroupBy(s => s["app"]?.ToString() ?? "Unknown")
                                       .ToDictionary(g => g.Key, g => g.ToList());

                // Update app summaries
                AppSummaries = new ObservableCollection<SessionSummaryItem>(
                    appGroups.Select(kvp => new SessionSummaryItem
                    {
                        AppName = kvp.Key,
                        SessionCount = kvp.Value.Count,
                        LongestSessionSeconds = kvp.Value.Max(s => SafeGetInt(s, "durationSeconds")),
                        PeakCpuPercent = kvp.Value.Average(s => SafeGetDouble(s, "peakCpuPercent")),
                        AverageCpuPercent = kvp.Value.Average(s => SafeGetDouble(s, "averageCpuPercent")),
                        PeakMemoryMb = kvp.Value.Average(s => SafeGetDouble(s, "peakMemoryMB")),
                        AverageMemoryMb = kvp.Value.Average(s => SafeGetDouble(s, "averageMemoryMB"))
                    })
                    .OrderByDescending(s => s.LongestSessionSeconds)
                );

                // Update sessions list (all sessions, ordered by start time)
                Sessions = new ObservableCollection<SessionRecordItem>(
                    sessions
                        .OrderByDescending(s => SafeGetDateTime(s, "startTimeUtc"))
                        .Select(s => new SessionRecordItem
                        {
                            AppName = s["app"]?.ToString() ?? "Unknown",
                            ProcessId = SafeGetInt(s, "processId"),
                            StartTimeLocal = SafeGetDateTime(s, "startTimeUtc").ToLocalTime(),
                            EndTimeLocal = SafeGetDateTime(s, "endTimeUtc").ToLocalTime(),
                            DurationSeconds = SafeGetInt(s, "durationSeconds"),
                            PeakCpuPercent = SafeGetDouble(s, "peakCpuPercent"),
                            AverageCpuPercent = SafeGetDouble(s, "averageCpuPercent"),
                            PeakMemoryMb = SafeGetDouble(s, "peakMemoryMB"),
                            AverageMemoryMb = SafeGetDouble(s, "averageMemoryMB")
                        })
                );

                // Update summary stats
                UpdateSummaryStats(appGroups);
            }
            catch (Exception ex)
            {
                // Clear data on error
                AppSummaries.Clear();
                Sessions.Clear();
                UpdateSummaryStats(new Dictionary<string, List<Dictionary<string, object>>>());
                
                // Log error
                try
                {
                    var errorFile = Path.Combine(UsageTrackingService.GetBaseDirectory(), "errors.txt");
                    File.AppendAllText(errorFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UI Refresh error: {ex.Message}\n");
                }
                catch { }
            }
        }

        private void UpdateSummaryStats(Dictionary<string, List<Dictionary<string, object>>> appGroups)
        {
            if (appGroups.Any())
            {
                // Top app by total runtime
                var topApp = appGroups.OrderByDescending(kvp => kvp.Value.Sum(s => SafeGetInt(s, "durationSeconds"))).First();
                TopAppName = topApp.Key;
                TopAppSessionsText = $"{topApp.Value.Count} sessions";

                // Longest single session
                var longestSession = appGroups.SelectMany(kvp => kvp.Value)
                                             .OrderByDescending(s => SafeGetInt(s, "durationSeconds"))
                                             .First();
                var duration = SafeGetInt(longestSession, "durationSeconds");
                LongestSessionText = $"{longestSession["app"]} ({TimeSpan.FromSeconds(duration):hh\\:mm\\:ss})";
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
                var filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
                File.WriteAllText(filePath, csv);
                
                ShowCopyStatus($"Exported to {fileName}");
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
                var filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
                File.WriteAllText(filePath, json);
                
                ShowCopyStatus($"Exported to {fileName}");
            }
            catch (Exception ex)
            {
                ShowCopyStatus($"Export failed: {ex.Message}");
            }
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
    }
}
