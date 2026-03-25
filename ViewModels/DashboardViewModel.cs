using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskCore.Models;
using TaskCore.Services;

namespace TaskCore.ViewModels
{
    public sealed partial class TopOffenderItem : ObservableObject
    {
        [ObservableProperty] private string name = string.Empty;
        [ObservableProperty] private string detail = string.Empty;
        [ObservableProperty] private int rank;
        [ObservableProperty] private string duration = string.Empty;
        [ObservableProperty] private double impactScore;
        [ObservableProperty] private double barWidth;
        [ObservableProperty] private string barColor = "#9A4DFF";
        [ObservableProperty] private System.Windows.Media.SolidColorBrush barBrush = new(System.Windows.Media.Color.FromRgb(154, 77, 255));
        [ObservableProperty] private string cpuText = string.Empty;
        [ObservableProperty] private string ramText = string.Empty;
        [ObservableProperty] private double peakCpu;
        [ObservableProperty] private double peakRam;
        [ObservableProperty] private bool isTopRank;
    }

    public sealed partial class InsightItem : ObservableObject
    {
        [ObservableProperty] private string title = string.Empty;
        [ObservableProperty] private string detail = string.Empty;
        [ObservableProperty] private string processName = string.Empty;
        [ObservableProperty] private double impactScore;
        [ObservableProperty] private string category = string.Empty;
        [ObservableProperty] private double predictedCpu;
        [ObservableProperty] private double predictedMemory;
        [ObservableProperty] private bool isAnomaly;
        [ObservableProperty] private string icon = "📊";
        [ObservableProperty] private string colorBrush = "#9A4DFF";

        public void ShowDetails()
        {
            // This will be handled by the command in DashboardViewModel
        }
    }

    public sealed partial class HeatmapHourCell : ObservableObject
    {
        [ObservableProperty] private System.Windows.Media.Brush brush = System.Windows.Media.Brushes.Transparent;
        [ObservableProperty] private string toolTip = string.Empty;
    }

    public sealed partial class WeeklyOverviewItem : ObservableObject
    {
        [ObservableProperty] private string dayLabel = string.Empty;
        [ObservableProperty] private double barWidth;
        [ObservableProperty] private string usageText = string.Empty;
        [ObservableProperty] private bool isToday;
        [ObservableProperty] private string trendArrow = string.Empty;
        [ObservableProperty] private string peakAppName = string.Empty;
        [ObservableProperty] private string barColor = "#9A4DFF";
        [ObservableProperty] private bool showAverageLine;
        [ObservableProperty] private double averageLinePosition;
    }

    public partial class DashboardViewModel : ObservableObject, IDisposable
    {
        private bool _disposed;
        private readonly DispatcherTimer _refreshTimer;
        private readonly ProcessAnalyticsEngine _analyticsEngine;

        private PerformanceCounter? _diskReadBytes;
        private PerformanceCounter? _diskWriteBytes;
        private PerformanceCounter[] _gpuEngineUtil = Array.Empty<PerformanceCounter>();

        private double _peakDiskReadMb;
        private double _peakDiskWriteMb;
        private double _peakGpuUtil;

        [ObservableProperty] private bool isEmpty = true;
        public bool IsNotEmpty => !IsEmpty;

        partial void OnIsEmptyChanged(bool value)
        {
            try
            {
                OnPropertyChanged(nameof(IsNotEmpty));
            }
            catch
            {
            }
        }

        [RelayCommand]
        private void RestartExplorer()
        {
            ExecuteCommandWithLogging(
                "RestartExplorer",
                "cmd.exe",
                "/c taskkill /f /im explorer.exe & start explorer.exe",
                "Explorer restarted successfully",
                "Failed to restart Explorer"
            );
        }

        [RelayCommand]
        private void FlushDns()
        {
            ExecuteCommandWithLogging(
                "FlushDns",
                "ipconfig",
                "/flushdns",
                "DNS cache flushed successfully",
                "Failed to flush DNS"
            );
        }

        [RelayCommand]
        private void ClearTemp()
        {
            try
            {
                ActionStatusText = "Clearing temp files...";
                long freedBytes = 0;
                int deletedFiles = 0;
                int deletedDirs = 0;
                var errors = new System.Collections.Generic.List<string>();
                var tempPaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
                    Path.GetTempPath()
                };

                foreach (var tempPath in tempPaths)
                {
                    if (!Directory.Exists(tempPath)) continue;
                    try
                    {
                        foreach (var file in Directory.GetFiles(tempPath, "*.*", SearchOption.TopDirectoryOnly))
                        {
                            try
                            {
                                var fi = new FileInfo(file);
                                freedBytes += fi.Length;
                                File.Delete(file);
                                deletedFiles++;
                            }
                            catch (Exception ex) { errors.Add($"File {file}: {ex.Message}"); }
                        }
                        foreach (var dir in Directory.GetDirectories(tempPath))
                        {
                            try
                            {
                                Directory.Delete(dir, true);
                                deletedDirs++;
                            }
                            catch (Exception ex) { errors.Add($"Dir {dir}: {ex.Message}"); }
                        }
                    }
                    catch (Exception ex) { errors.Add($"Path {tempPath}: {ex.Message}"); }
                }

                var logMsg = $"[ClearTemp] Deleted {deletedFiles} files, {deletedDirs} directories, freed {freedBytes / 1024 / 1024} MB. Errors: {errors.Count}";
                LogQuickAction(logMsg);

                ActionStatusText = $"Cleared {freedBytes / 1024 / 1024} MB ({errors.Count} errors)";
            }
            catch (Exception ex)
            {
                LogQuickAction($"[ClearTemp] Failed: {ex.Message}");
                ActionStatusText = $"Failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private void RestartAudioService()
        {
            ExecuteCommandWithLogging(
                "RestartAudioService",
                "cmd.exe",
                "/c net stop audiosrv & net start audiosrv",
                "Audio service restarted successfully",
                "Failed to restart audio service"
            );
        }

        [RelayCommand]
        private void RestartGpuDriver()
        {
            ExecuteCommandWithLogging(
                "RestartGpuDriver",
                "powershell.exe",
                "-Command \"Get-PnpDevice -Class DisplayAdapter -Status OK | Disable-PnpDevice -Confirm:$false; Start-Sleep 2; Get-PnpDevice -Class DisplayAdapter | Enable-PnpDevice -Confirm:$false\"",
                "GPU driver restart initiated (check log for details)",
                "Failed to restart GPU driver"
            );
        }

        [RelayCommand]
        private void ResetNetworkStack()
        {
            Task.Run(() =>
            {
                var results = new System.Collections.Generic.List<string>();
                var commands = new[]
                {
                    ("netsh", "winsock reset"),
                    ("netsh", "int ip reset"),
                    ("ipconfig", "/flushdns"),
                    ("ipconfig", "/release"),
                    ("ipconfig", "/renew")
                };

                foreach (var (exe, args) in commands)
                {
                    var (success, output, exitCode) = ExecuteCommandAndCaptureOutput(exe, args, 10000);
                    results.Add($"[{exe} {args}] Exit: {exitCode}, Success: {success}");
                    if (!string.IsNullOrEmpty(output))
                        results.Add($"  Output: {output.Substring(0, Math.Min(200, output.Length))}");
                }

                LogQuickAction($"[ResetNetworkStack]{Environment.NewLine}" + string.Join(Environment.NewLine, results));
                ActionStatusText = "Network stack reset completed (see log)";
            });
        }

        [RelayCommand]
        private void RenewIp()
        {
            Task.Run(() =>
            {
                var (releaseSuccess, releaseOutput, releaseCode) = ExecuteCommandAndCaptureOutput("ipconfig", "/release", 5000);
                Thread.Sleep(1000);
                var (renewSuccess, renewOutput, renewCode) = ExecuteCommandAndCaptureOutput("ipconfig", "/renew", 10000);

                var logMsg = $"[RenewIp] Release Exit: {releaseCode}, Renew Exit: {renewCode}";
                LogQuickAction(logMsg);

                ActionStatusText = renewSuccess ? "IP renewed successfully" : $"IP renew failed (exit: {renewCode})";
            });
        }

        [RelayCommand]
        private void RestartPrintSpooler()
        {
            ExecuteCommandWithLogging(
                "RestartPrintSpooler",
                "cmd.exe",
                "/c net stop spooler & net start spooler",
                "Print spooler restarted successfully",
                "Failed to restart print spooler"
            );
        }

        [RelayCommand]
        private void RestartWindowsSearch()
        {
            ExecuteCommandWithLogging(
                "RestartWindowsSearch",
                "cmd.exe",
                "/c net stop wsearch & net start wsearch",
                "Windows Search restarted successfully",
                "Failed to restart Windows Search"
            );
        }

        [RelayCommand]
        private void ClearIconCache()
        {
            ExecuteCommandWithLogging(
                "ClearIconCache",
                "cmd.exe",
                "/c taskkill /f /im explorer.exe & del /f /q %localappdata%\\IconCache.db & start explorer.exe",
                "Icon cache cleared successfully",
                "Failed to clear icon cache"
            );
        }

        [RelayCommand]
        private void SfcScan()
        {
            try
            {
                ActionStatusText = "Starting SFC scan...";
                var psi = new ProcessStartInfo("sfc", "/scannow")
                {
                    CreateNoWindow = false,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                var proc = Process.Start(psi);
                LogQuickAction($"[SfcScan] Process started (PID: {proc?.Id}). Requires admin elevation.");
                ActionStatusText = "SFC scan started (requires admin - see log)";
            }
            catch (Exception ex)
            {
                LogQuickAction($"[SfcScan] Failed: {ex.Message}");
                ActionStatusText = $"Failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private void DismRepair()
        {
            try
            {
                ActionStatusText = "Starting DISM repair...";
                var psi = new ProcessStartInfo("DISM.exe", "/Online /Cleanup-Image /RestoreHealth")
                {
                    CreateNoWindow = false,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                var proc = Process.Start(psi);
                LogQuickAction($"[DismRepair] Process started (PID: {proc?.Id}). Requires admin elevation.");
                ActionStatusText = "DISM repair started (requires admin - see log)";
            }
            catch (Exception ex)
            {
                LogQuickAction($"[DismRepair] Failed: {ex.Message}");
                ActionStatusText = $"Failed: {ex.Message}";
            }
        }

        private void ExecuteCommandWithLogging(string actionName, string fileName, string arguments, string successText, string failureText)
        {
            Task.Run(() =>
            {
                try
                {
                    ActionStatusText = $"Running {actionName}...";
                    var (success, output, exitCode) = ExecuteCommandAndCaptureOutput(fileName, arguments, 15000);

                    var logEntry = $"[{actionName}] ExitCode: {exitCode}, Success: {success}";
                    if (!string.IsNullOrEmpty(output))
                        logEntry += $", Output: {output.Substring(0, Math.Min(300, output.Length))}";
                    LogQuickAction(logEntry);

                    ActionStatusText = success ? successText : $"{failureText} (exit: {exitCode})";
                }
                catch (Exception ex)
                {
                    LogQuickAction($"[{actionName}] Exception: {ex.Message}");
                    ActionStatusText = $"{failureText}: {ex.Message}";
                }
            });
        }

        private (bool success, string output, int exitCode) ExecuteCommandAndCaptureOutput(string fileName, string arguments, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo(fileName, arguments)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return (false, "Failed to start process", -1);

                string output = proc.StandardOutput.ReadToEnd();
                string error = proc.StandardError.ReadToEnd();
                proc.WaitForExit(timeoutMs);

                var fullOutput = output + (string.IsNullOrEmpty(error) ? "" : $" [ERR: {error}]");
                bool success = proc.ExitCode == 0;

                return (success, fullOutput, proc.ExitCode);
            }
            catch (Exception ex)
            {
                return (false, $"Exception: {ex.Message}", -1);
            }
        }

        private void LogQuickAction(string message)
        {
            try
            {
                var logDir = Path.Combine(UsageTrackingService.GetBaseDirectory(), "Logs");
                Directory.CreateDirectory(logDir);
                var logFile = Path.Combine(logDir, $"quickactions-{DateTime.Now:yyyy-MM-dd}.txt");
                var entry = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(logFile, entry);
            }
            catch { }
        }

        [ObservableProperty] private string todayDateText = DateTime.Today.ToString("ddd, dd MMM");
        [ObservableProperty] private string totalUsageText = "-";
        [ObservableProperty] private string totalUsageVsYesterdayText = string.Empty;
        [ObservableProperty] private string lastUpdatedText = "Updated: --";
        [ObservableProperty] private string sessionCountText = "0 sessions";

        // Date picker for viewing past data
        [ObservableProperty] private DateOnly selectedDate = DateOnly.FromDateTime(DateTime.Today);
        [ObservableProperty] private ObservableCollection<DateOnly> availableDates = new();
        [ObservableProperty] private bool canGoToPreviousDate = false;
        [ObservableProperty] private bool canGoToNextDate = false;

        [ObservableProperty] private string peakCpuText = "N/A";
        [ObservableProperty] private string peakRamText = "N/A";
        [ObservableProperty] private string peakGpuUsageText = "N/A";
        [ObservableProperty] private string peakDiskReadText = "N/A";
        [ObservableProperty] private string peakDiskWriteText = "N/A";
        [ObservableProperty] private string cpuTempText = "N/A";
        [ObservableProperty] private string gpuTempText = "N/A";

        [ObservableProperty] private string topOffenderName = "-";
        [ObservableProperty] private string topOffenderDetail = "-";

        [ObservableProperty] private string stabilityText = "OK";
        [ObservableProperty] private string stabilityDetail = "No issues detected";

        [ObservableProperty] private ObservableCollection<string> recentAlerts = new();
        public bool HasNoAlerts => RecentAlerts.Count == 0;

        [ObservableProperty] private ObservableCollection<TopOffenderItem> topOffenders = new();
        public bool HasNoTopOffenders => TopOffenders.Count == 0;

        [ObservableProperty] private ObservableCollection<InsightItem> insights = new();
        public bool HasNoInsights => Insights.Count == 0;

        // Throttle analytics to reduce CPU usage (run max once per 30 seconds)
        private DateTime _lastInsightsBuildTime = DateTime.MinValue;
        private readonly TimeSpan _insightsThrottleInterval = TimeSpan.FromSeconds(30);

        [ObservableProperty] private bool isInsightPopupOpen = false;
        [ObservableProperty] private InsightItem? selectedInsight = null;
        [ObservableProperty] private string insightPopupTitle = string.Empty;
        [ObservableProperty] private string insightPopupContent = string.Empty;

        [ObservableProperty] private ObservableCollection<HeatmapHourCell> hourlyHeatmap = new();
        [ObservableProperty] private ObservableCollection<WeeklyOverviewItem> weeklyOverview = new();

        [ObservableProperty] private string actionStatusText = string.Empty;

        public DashboardViewModel()
        {
            _analyticsEngine = new ProcessAnalyticsEngine(new ProcessAnalyticsEngine.Options
            {
                MaxHistorySamples = 60,
                AnomalyZThreshold = 2.0,
                HeavyCpuThreshold = 60.0,
                PredictionMinutes = 5,
                TopOffenders = 10
            });

            BuildHeatmapSkeleton();
            LoadAvailableDates();

            try
            {
                RecentAlerts.CollectionChanged += OnRecentAlertsChanged;
                TopOffenders.CollectionChanged += OnTopOffendersChanged;
                Insights.CollectionChanged += OnInsightsChanged;
            }
            catch
            {
            }

            _refreshTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += (_, __) => RefreshIfActive();

            try
            {
                AppViewStateService.ViewChanged += OnViewChanged;
                OnViewChanged(AppViewStateService.CurrentView);
            }
            catch
            {
            }

            UpdateDateNavigation();
            Refresh();
        }

        private void LoadAvailableDates()
        {
            try
            {
                AvailableDates.Clear();
                var files = Directory.GetFiles(UsageTrackingService.GetBaseDirectory(), "sessions-*.jsonl");
                var dates = files
                    .Select(f =>
                    {
                        var fileName = Path.GetFileNameWithoutExtension(f);
                        var dateStr = fileName.Replace("sessions-", "");
                        return DateOnly.TryParse(dateStr, out var date) ? date : (DateOnly?)null;
                    })
                    .Where(d => d.HasValue)
                    .Select(d => d!.Value)
                    .OrderBy(d => d)
                    .ToList();

                foreach (var date in dates)
                {
                    AvailableDates.Add(date);
                }

                // Always include today even if no data yet
                var today = DateOnly.FromDateTime(DateTime.Today);
                if (!AvailableDates.Contains(today))
                {
                    AvailableDates.Add(today);
                }

                // Sort again after adding today
                var sorted = AvailableDates.OrderBy(d => d).ToList();
                AvailableDates.Clear();
                foreach (var d in sorted)
                {
                    AvailableDates.Add(d);
                }

                // Ensure SelectedDate is set to today if not already set or if current selection is not in list
                if (!AvailableDates.Contains(SelectedDate))
                {
                    SelectedDate = today;
                }
                else
                {
                    // Force UI refresh even if date hasn't changed, to ensure ComboBox displays correctly
                    OnPropertyChanged(nameof(SelectedDate));
                }
            }
            catch { }
        }

        private void OnRecentAlertsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                OnPropertyChanged(nameof(HasNoAlerts));
            }
            catch
            {
            }
        }

        private void OnTopOffendersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                OnPropertyChanged(nameof(HasNoTopOffenders));
            }
            catch
            {
            }
        }

        private void OnInsightsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                OnPropertyChanged(nameof(HasNoInsights));
            }
            catch
            {
            }
        }

        partial void OnSelectedDateChanged(DateOnly value)
        {
            UpdateDateNavigation();
            Refresh();
        }

        private void UpdateDateNavigation()
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            CanGoToPreviousDate = AvailableDates.Any() && SelectedDate > AvailableDates.Min();
            CanGoToNextDate = SelectedDate < today;
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
            SelectedDate = DateOnly.FromDateTime(DateTime.Today);
        }

        public void PauseRefresh()
        {
            try
            {
                if (_refreshTimer.IsEnabled)
                    _refreshTimer.Stop();
            }
            catch { }
        }

        public void ResumeRefresh()
        {
            try
            {
                if (!_refreshTimer.IsEnabled)
                    _refreshTimer.Start();
                Refresh();
            }
            catch { }
        }

        private void OnViewChanged(string viewName)
        {
            try
            {
                if (string.Equals(viewName, "Dashboard", StringComparison.OrdinalIgnoreCase))
                {
                    ResetPerfPeaksIfNewDay();
                    EnsurePerfCountersInitialized();

                    // Refresh available dates in case new session files were added
                    LoadAvailableDates();

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
                if (!string.Equals(AppViewStateService.CurrentView, "Dashboard", StringComparison.OrdinalIgnoreCase))
                    return;

                EnsurePerfCountersInitialized();
                SamplePerfCounters();
                Refresh();
            }
            catch
            {
            }
        }

        private DateOnly _lastPerfDay = DateOnly.FromDateTime(DateTime.Today);

        private void ResetPerfPeaksIfNewDay()
        {
            try
            {
                var today = DateOnly.FromDateTime(DateTime.Today);
                if (today == _lastPerfDay) return;

                _lastPerfDay = today;
                _peakDiskReadMb = 0;
                _peakDiskWriteMb = 0;
                _peakGpuUtil = 0;
                PeakGpuUsageText = "N/A";
                PeakDiskReadText = "N/A";
                PeakDiskWriteText = "N/A";
            }
            catch
            {
            }
        }

        private void EnsurePerfCountersInitialized()
        {
            try
            {
                if (_diskReadBytes == null)
                {
                    try
                    {
                        _diskReadBytes = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
                        _ = _diskReadBytes.NextValue();
                    }
                    catch
                    {
                        _diskReadBytes = null;
                    }
                }

                if (_diskWriteBytes == null)
                {
                    try
                    {
                        _diskWriteBytes = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
                        _ = _diskWriteBytes.NextValue();
                    }
                    catch
                    {
                        _diskWriteBytes = null;
                    }
                }

                if (_gpuEngineUtil.Length == 0)
                {
                    try
                    {
                        var cat = new PerformanceCounterCategory("GPU Engine");
                        var instances = cat.GetInstanceNames();

                        // Prefer 3D engines if present; otherwise fallback to all engines.
                        var preferred = instances
                            .Where(n => n.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                            .Take(16)
                            .ToArray();

                        var chosen = preferred.Length > 0 ? preferred : instances.Take(16).ToArray();

                        _gpuEngineUtil = chosen
                            .Select(inst =>
                            {
                                try
                                {
                                    var pc = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst);
                                    _ = pc.NextValue();
                                    return pc;
                                }
                                catch
                                {
                                    return null;
                                }
                            })
                            .Where(pc => pc != null)
                            .Cast<PerformanceCounter>()
                            .ToArray();
                    }
                    catch
                    {
                        _gpuEngineUtil = Array.Empty<PerformanceCounter>();
                    }
                }
            }
            catch
            {
            }
        }

        private void SamplePerfCounters()
        {
            try
            {
                // Disk
                try
                {
                    if (_diskReadBytes != null)
                    {
                        var readMb = _diskReadBytes.NextValue() / (1024.0 * 1024.0);
                        if (readMb > _peakDiskReadMb) _peakDiskReadMb = readMb;
                        PeakDiskReadText = _peakDiskReadMb > 0 ? $"{_peakDiskReadMb:F1} MB/s" : "N/A";
                    }
                }
                catch
                {
                }

                try
                {
                    if (_diskWriteBytes != null)
                    {
                        var writeMb = _diskWriteBytes.NextValue() / (1024.0 * 1024.0);
                        if (writeMb > _peakDiskWriteMb) _peakDiskWriteMb = writeMb;
                        PeakDiskWriteText = _peakDiskWriteMb > 0 ? $"{_peakDiskWriteMb:F1} MB/s" : "N/A";
                    }
                }
                catch
                {
                }

                // GPU utilization
                try
                {
                    if (_gpuEngineUtil.Length > 0)
                    {
                        double sum = 0;
                        foreach (var pc in _gpuEngineUtil)
                        {
                            try { sum += pc.NextValue(); } catch { }
                        }

                        // Sum across engines can exceed 100; clamp for display.
                        var util = Math.Clamp(sum, 0, 100);
                        if (util > _peakGpuUtil) _peakGpuUtil = util;
                        PeakGpuUsageText = _gpuEngineUtil.Length > 0 ? $"{_peakGpuUtil:F0}%" : "N/A";
                    }
                    else
                    {
                        PeakGpuUsageText = "N/A";
                    }
                }
                catch
                {
                    PeakGpuUsageText = "N/A";
                }
            }
            catch
            {
            }
        }

        [RelayCommand]
        private void OpenInsightPopup(InsightItem? insight)
        {
            if (insight == null) return;

            SelectedInsight = insight;
            InsightPopupTitle = $"{insight.ProcessName} - {insight.Category}";
            
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"📊 Impact Score: {insight.ImpactScore:F1}/100");
            sb.AppendLine($"🔮 Predicted CPU (5min): {insight.PredictedCpu:F1}%");
            sb.AppendLine($"🔮 Predicted Memory (5min): {insight.PredictedMemory:F1} MB");
            sb.AppendLine();
            
            if (insight.IsAnomaly)
            {
                sb.AppendLine("⚠️ ANOMALY DETECTED");
                sb.AppendLine("This process shows unusual behavior patterns.");
            }
            
            sb.AppendLine(insight.Detail);
            InsightPopupContent = sb.ToString();
            
            IsInsightPopupOpen = true;
        }

        [RelayCommand]
        private void CloseInsightPopup()
        {
            IsInsightPopupOpen = false;
            SelectedInsight = null;
        }

        [RelayCommand]
        private void OpenSettings() => AppViewStateService.SetView("Settings");

        [RelayCommand]
        private void CleanupOldSessionLogs()
        {
            try
            {
                UsageTrackingService.CleanupOldFiles();
                ActionStatusText = "Cleanup completed";
                Refresh();
            }
            catch (Exception ex)
            {
                ActionStatusText = $"Cleanup failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private void ExportDashboard()
        {
            try
            {
                var date = DateOnly.FromDateTime(DateTime.Today);
                var sessions = UsageTrackingService.GetDailySessions(date, mergeOverlappingSessions: true);
                var totalSeconds = sessions.Sum(s => Math.Max(0, s.durationSeconds));

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"dashboard-export-{DateTime.Now:yyyyMMdd}",
                    DefaultExt = ".txt",
                    Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                };

                bool? result = dialog.ShowDialog();
                if (result != true)
                {
                    return;
                }

                var exportPath = dialog.FileName;
                var lines = new System.Collections.Generic.List<string>
                {
                    $"TaskCore Dashboard Export - {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    $"Total Usage: {TimeSpan.FromSeconds(totalSeconds):hh\\:mm\\:ss}",
                    $"Sessions: {sessions.Count}",
                    $"Top Offender: {TopOffenderName}",
                    $"Resource Peaks - CPU: {PeakCpuText}, RAM: {PeakRamText}, GPU: {PeakGpuUsageText}",
                    ""
                };

                // Add top apps
                var grouped = sessions
                    .GroupBy(s => string.IsNullOrWhiteSpace(s.app) ? "Unknown" : s.app)
                    .Select(g => new { App = g.Key, Seconds = g.Sum(x => Math.Max(0, x.durationSeconds)) })
                    .OrderByDescending(x => x.Seconds)
                    .Take(5);

                lines.Add("Top Apps:");
                foreach (var g in grouped)
                {
                    lines.Add($"  {g.App}: {TimeSpan.FromSeconds(g.Seconds):hh\\:mm\\:ss}");
                }

                File.WriteAllLines(exportPath, lines);
                ActionStatusText = $"Exported to {Path.GetFileName(exportPath)}";
            }
            catch (Exception ex)
            {
                ActionStatusText = $"Export failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private void ViewTopOffenderDetails()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(TopOffenderName) || TopOffenderName == "-")
                {
                    ActionStatusText = "No top offender to view";
                    return;
                }
                // Navigate to Details view
                AppViewStateService.SetView("Details");
                // Note: Filtering by specific app would require additional implementation in DetailsViewModel
            }
            catch (Exception ex)
            {
                ActionStatusText = $"Navigation failed: {ex.Message}";
            }
        }

        private void BuildHeatmapSkeleton()
        {
            HourlyHeatmap.Clear();
            for (int i = 0; i < 24; i++)
            {
                HourlyHeatmap.Add(new HeatmapHourCell
                {
                    Brush = System.Windows.Media.Brushes.Transparent,
                    ToolTip = $"{i:00}:00 - 0 min"
                });
            }
        }

        private static string GetCategoryIcon(BehaviorCategory category)
        {
            return category switch
            {
                BehaviorCategory.Heavy => "🔥",
                BehaviorCategory.Idle => "💤",
                BehaviorCategory.Unstable => "🔄",
                BehaviorCategory.Suspicious => "👁️",
                _ => "✅"
            };
        }

        private static string GetCategoryColor(BehaviorCategory category)
        {
            return category switch
            {
                BehaviorCategory.Heavy => "#FACC15",     // Yellow
                BehaviorCategory.Idle => "#4ADE80",      // Green
                BehaviorCategory.Unstable => "#F87171",   // Red
                BehaviorCategory.Suspicious => "#F87171", // Red
                _ => "#9A4DFF"                            // Purple (Normal)
            };
        }

        private static int CalculateUniqueDurationSeconds(List<UsageTrackingService.SessionRecord> sessions)
        {
            if (sessions == null || sessions.Count == 0) return 0;

            // Filter and collect valid intervals without LINQ overhead
            int validCount = 0;
            var tempIntervals = new (DateTime start, DateTime end)[sessions.Count];
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                if (s.durationSeconds > 0 && s.endTimeUtc > s.startTimeUtc)
                {
                    tempIntervals[validCount++] = (s.startTimeUtc, s.endTimeUtc);
                }
            }

            if (validCount == 0) return 0;

            // Sort in-place using Array.Sort (much faster than LINQ OrderBy + ToList)
            Array.Sort(tempIntervals, 0, validCount, IntervalComparer.Instance);

            // Merge overlapping intervals without allocating a new list
            double totalSeconds = 0;
            var currentStart = tempIntervals[0].start;
            var currentEnd = tempIntervals[0].end;

            for (int i = 1; i < validCount; i++)
            {
                if (tempIntervals[i].start <= currentEnd) // Overlaps or adjacent
                {
                    // Extend current interval if needed
                    if (tempIntervals[i].end > currentEnd)
                        currentEnd = tempIntervals[i].end;
                }
                else
                {
                    totalSeconds += (currentEnd - currentStart).TotalSeconds;
                    currentStart = tempIntervals[i].start;
                    currentEnd = tempIntervals[i].end;
                }
            }
            totalSeconds += (currentEnd - currentStart).TotalSeconds;

            return (int)totalSeconds;
        }

        private sealed class IntervalComparer : IComparer<(DateTime start, DateTime end)>
        {
            public static readonly IntervalComparer Instance = new();
            public int Compare((DateTime start, DateTime end) x, (DateTime start, DateTime end) y)
            {
                return x.start.CompareTo(y.start);
            }
        }

        private void Refresh()
        {
            try
            {
                TodayDateText = SelectedDate.ToString("ddd, dd MMM yyyy");

                var sessions = UsageTrackingService.GetDailySessions(SelectedDate, mergeOverlappingSessions: true);
                
                // Show empty state if no data for selected date
                IsEmpty = sessions.Count == 0;
                OnPropertyChanged(nameof(IsNotEmpty));

                if (sessions.Count > 0)
                {
                    var totalSeconds = CalculateUniqueDurationSeconds(sessions);
                    var activeSeconds = CalculateUniqueDurationSeconds(sessions.Where(s => !s.isIdle).ToList());
                    var idleSeconds = CalculateUniqueDurationSeconds(sessions.Where(s => s.isIdle).ToList());
                    
                    // Show total, but include active/idle breakdown in the subtext
                    TotalUsageText = TimeSpan.FromSeconds(totalSeconds).ToString(@"hh\:mm\:ss");
                    SessionCountText = $"{sessions.Count} session{(sessions.Count == 1 ? "" : "s")}";

                    // Show active vs idle time breakdown
                    var activeTime = TimeSpan.FromSeconds(activeSeconds);
                    var idleTime = TimeSpan.FromSeconds(idleSeconds);
                    var activeText = $"{activeTime.Hours}h {activeTime.Minutes}m active";
                    var idleText = idleSeconds > 0 ? $" | {idleTime.Hours}h {idleTime.Minutes}m idle/locked" : "";
                    TotalUsageVsYesterdayText = activeText + idleText;

                    var top = sessions
                        .GroupBy(s => string.IsNullOrWhiteSpace(s.app) ? "Unknown" : s.app)
                        .Select(g => new { App = g.Key, Seconds = g.Sum(x => Math.Max(0, x.durationSeconds)), PeakCpu = g.Max(x => x.peakCpuPercent), PeakMem = g.Max(x => x.peakMemoryMB) })
                        .OrderByDescending(x => x.Seconds)
                        .FirstOrDefault();

                    if (top != null)
                    {
                        TopOffenderName = top.App;
                        TopOffenderDetail = $"{TimeSpan.FromSeconds(top.Seconds).ToString(@"hh\:mm\:ss")} | CPU {top.PeakCpu:F0}% | RAM {top.PeakMem:F0} MB";

                        // Peaks shown in the Resource Peaks card should reflect the top offender app.
                        PeakCpuText = $"{top.PeakCpu:F1}%";
                        PeakRamText = $"{top.PeakMem:F0} MB";
                    }
                    else
                    {
                        TopOffenderName = "-";
                        TopOffenderDetail = "-";

                        PeakCpuText = "N/A";
                        PeakRamText = "N/A";
                    }

                    BuildTopOffenders(sessions);
                    BuildInsights(sessions);
                    BuildHeatmap(sessions);
                    BuildWeeklyOverview();
                    LoadRecentAlerts();
                    UpdateStabilityIndicators();
                }
                else
                {
                    TotalUsageText = "-";
                    PeakCpuText = "N/A";
                    PeakRamText = "N/A";
                    // keep perf peaks as-is; they are live and not tied to sessions
                    TopOffenderName = "-";
                    TopOffenderDetail = "-";
                    RecentAlerts.Clear();
                    TopOffenders.Clear();
                    Insights.Clear();
                    BuildHeatmapSkeleton();
                    BuildWeeklyOverview();
                    UpdateStabilityIndicators();
                }

                CpuTempText = ReadTempText(() => HardwareMonitorService.Instance.GetCpuPackageTemp());
                GpuTempText = ReadTempText(() => HardwareMonitorService.Instance.GetGpuTempByName(""));
                LastUpdatedText = $"Updated: {DateTime.Now:HH:mm}";

                OnPropertyChanged(nameof(HasNoAlerts));
            }
            catch (Exception ex)
            {
                IsEmpty = true;
                OnPropertyChanged(nameof(IsNotEmpty));
                TotalUsageText = "-";
                PeakCpuText = "N/A";
                PeakRamText = "N/A";
                PeakGpuUsageText = "N/A";
                PeakDiskReadText = "N/A";
                PeakDiskWriteText = "N/A";
                CpuTempText = "N/A";
                GpuTempText = "N/A";
                TopOffenderName = "-";
                TopOffenderDetail = "-";
                RecentAlerts.Clear();
                TopOffenders.Clear();
                Insights.Clear();
                OnPropertyChanged(nameof(HasNoAlerts));
                BuildHeatmapSkeleton();

                try
                {
                    var errorFile = Path.Combine(UsageTrackingService.GetBaseDirectory(), "dashboard-errors.txt");
                    File.AppendAllText(errorFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Dashboard refresh error: {ex.Message}{Environment.NewLine}");
                }
                catch { }
            }
        }

        private static string ReadTempText(Func<float?> getter)
        {
            try
            {
                var t = getter();
                if (!t.HasValue) return "N/A";
                if (t.Value < -50 || t.Value > 150) return "N/A";
                return $"{t.Value:F1}°C";
            }
            catch
            {
                return "N/A";
            }
        }

        private void BuildHeatmap(System.Collections.Generic.List<UsageTrackingService.SessionRecord> sessions)
        {
            // Calculate unique minutes per hour (wall-clock time, not summed durations)
            var minutesPerHour = new double[24];
            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);

            // Pre-allocate a reusable array for intervals (max 1 per session)
            var maxIntervals = sessions.Count;
            var intervals = new (DateTime start, DateTime end)[maxIntervals];

            // Group sessions by hour and calculate unique coverage per hour
            for (int hour = 0; hour < 24; hour++)
            {
                var hourStart = today.AddHours(hour);
                var hourEnd = hourStart.AddHours(1);

                // Find all intervals that intersect with this hour
                int intervalCount = 0;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    var start = s.startTimeUtc.ToLocalTime();
                    var end = s.endTimeUtc.ToLocalTime();

                    // Clamp to today
                    if (start < today) start = today;
                    if (end > tomorrow) end = tomorrow;

                    // Check if intersects with this hour
                    if (end > hourStart && start < hourEnd)
                    {
                        // Clamp interval to this hour
                        if (start < hourStart) start = hourStart;
                        if (end > hourEnd) end = hourEnd;
                        intervals[intervalCount++] = (start, end);
                    }
                }

                if (intervalCount == 0) continue;

                // Sort intervals by start time
                Array.Sort(intervals, 0, intervalCount, IntervalComparer.Instance);

                // Merge overlapping intervals and sum unique minutes
                var currentStart = intervals[0].start;
                var currentEnd = intervals[0].end;
                double uniqueMinutes = 0;

                for (int i = 1; i < intervalCount; i++)
                {
                    if (intervals[i].start <= currentEnd) // Overlaps
                    {
                        if (intervals[i].end > currentEnd)
                            currentEnd = intervals[i].end;
                    }
                    else
                    {
                        uniqueMinutes += (currentEnd - currentStart).TotalMinutes;
                        currentStart = intervals[i].start;
                        currentEnd = intervals[i].end;
                    }
                }
                uniqueMinutes += (currentEnd - currentStart).TotalMinutes;
                minutesPerHour[hour] = Math.Min(60, uniqueMinutes); // Cap at 60 minutes
            }

            var max = minutesPerHour.Max();
            if (HourlyHeatmap.Count != 24) BuildHeatmapSkeleton();

            for (int i = 0; i < 24; i++)
            {
                var ratio = max <= 0 ? 0 : minutesPerHour[i] / max;
                var alpha = (byte)(30 + (ratio * 180));
                HourlyHeatmap[i].Brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 154, 77, 255));
                HourlyHeatmap[i].ToolTip = $"{i:00}:00 - {minutesPerHour[i]:F0} min";
            }
        }

        private void BuildWeeklyOverview()
        {
            WeeklyOverview.Clear();

            // Build 7-day window ending on the selected date
            var days = Enumerable.Range(0, 7)
                .Select(offset => SelectedDate.AddDays(-6 + offset))
                .ToList();

            var dayData = days.Select(d =>
            {
                var sessions = UsageTrackingService.GetDailySessions(d, mergeOverlappingSessions: true);
                var totalSec = CalculateUniqueDurationSeconds(sessions);

                // Find peak app
                var peakApp = sessions
                    .GroupBy(s => string.IsNullOrWhiteSpace(s.app) ? "Unknown" : s.app)
                    .Select(g => new { App = g.Key, Seconds = g.Sum(s => s.durationSeconds) })
                    .OrderByDescending(x => x.Seconds)
                    .FirstOrDefault();

                return new
                {
                    Date = d,
                    TotalSeconds = totalSec,
                    PeakApp = peakApp?.App ?? "-",
                    PeakSeconds = peakApp?.Seconds ?? 0
                };
            }).ToList();

            var max = dayData.Count == 0 ? 0 : dayData.Max(d => d.TotalSeconds);
            var avg = dayData.Count == 0 ? 0 : dayData.Average(d => d.TotalSeconds);
            const double maxWidth = 180;
            var today = DateOnly.FromDateTime(DateTime.Today);

            for (int idx = 0; idx < dayData.Count; idx++)
            {
                var data = dayData[idx];
                var prevData = idx > 0 ? dayData[idx - 1] : null;
                var ratio = max <= 0 ? 0 : (data.TotalSeconds / (double)max);
                var barWidth = Math.Max(2, maxWidth * ratio);

                // Trend arrow vs previous day
                string trend = "";
                if (prevData != null && prevData.TotalSeconds > 0)
                {
                    var change = data.TotalSeconds - prevData.TotalSeconds;
                    var pctChange = Math.Abs(change) / prevData.TotalSeconds;
                    if (pctChange > 0.1)
                        trend = change > 0 ? "↑" : "↓";
                }

                // Color coding based on usage intensity
                string color;
                var hours = data.TotalSeconds / 3600.0;
                if (hours < 4) color = "#4ADE80";      // Green - light (< 4h)
                else if (hours < 9) color = "#FACC15"; // Yellow - medium (4-8h)
                else color = "#F87171";                 // Red - heavy (9h+)

                // Is today
                bool isToday = data.Date == today;

                // Average line position (relative to bar width)
                double avgRatio = max <= 0 ? 0 : (avg / max);
                double avgPosition = Math.Max(2, maxWidth * avgRatio);
                bool showAvg = idx > 0 && Math.Abs(barWidth - avgPosition) > 5;

                WeeklyOverview.Add(new WeeklyOverviewItem
                {
                    DayLabel = $"{data.Date.ToDateTime(TimeOnly.MinValue):ddd dd}",
                    BarWidth = barWidth,
                    UsageText = TimeSpan.FromSeconds(data.TotalSeconds).ToString(@"h\:mm"),
                    IsToday = isToday,
                    TrendArrow = trend,
                    PeakAppName = data.PeakApp,
                    BarColor = isToday ? "#9A4DFF" : color,
                    ShowAverageLine = showAvg,
                    AverageLinePosition = avgPosition
                });
            }
        }

        private void LoadRecentAlerts()
        {
            RecentAlerts.Clear();

            try
            {
                var logDir = Path.Combine(AppSettingsService.BaseDirectory, "RulesLogs");
                var logFile = Path.Combine(logDir, $"{SelectedDate:yyyy-MM-dd}.log");
                if (!File.Exists(logFile))
                {
                    OnPropertyChanged(nameof(HasNoAlerts));
                    return;
                }

                var lines = File.ReadAllLines(logFile);
                foreach (var line in lines.Reverse().Take(8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    RecentAlerts.Add(line);
                }
            }
            catch
            {
            }

            OnPropertyChanged(nameof(HasNoAlerts));
        }

        private void BuildTopOffenders(System.Collections.Generic.List<UsageTrackingService.SessionRecord> sessions)
        {
            TopOffenders.Clear();

            try
            {
                // Debug: log what we're filtering
                var filtered = sessions
                    .Where(s => 
                    {
                        var appName = s.app?.Trim() ?? "Unknown";
                        var isSystem = ProcessFilter.IsSystemAppName(appName);
                        // Log for debugging
                        if (appName.ToLowerInvariant().Contains("lsa") || appName.ToLowerInvariant().Contains("team"))
                        {
                            System.Diagnostics.Debug.WriteLine($"[Filter Debug] App: '{appName}', IsSystem: {isSystem}");
                        }
                        return !isSystem;
                    })
                    .ToList();

                var grouped = filtered
                    .GroupBy(s => string.IsNullOrWhiteSpace(s.app) ? "Unknown" : s.app.Trim())
                    .Select(g => new
                    {
                        App = g.Key,
                        Seconds = g.Sum(x => Math.Max(0, x.durationSeconds)),
                        PeakCpu = g.Max(x => x.peakCpuPercent),
                        PeakMem = g.Max(x => x.peakMemoryMB),
                        AvgCpu = g.Average(x => x.averageCpuPercent),
                        SessionCount = g.Count()
                    })
                    .OrderByDescending(x => x.Seconds)
                    .Take(6)
                    .ToList();

                if (grouped.Count == 0) return;

                for (int i = 0; i < grouped.Count; i++)
                {
                    var g = grouped[i];
                    var rank = i + 1;
                    
                    // Calculate impact score (0-100) based on duration + resource usage
                    var durationScore = Math.Min(g.Seconds / 3600.0 * 20, 40); // 1hr = 20pts, max 40
                    var cpuScore = Math.Min(g.PeakCpu * 0.4, 35); // peak CPU % * 0.4, max 35
                    var memScore = Math.Min(g.PeakMem / 100.0, 25); // 2.5GB = 25pts max
                    var impactScore = Math.Min(durationScore + cpuScore + memScore, 100);

                    // Calculate max impact for bar scaling
                    var maxImpact = Math.Max(grouped.Max(x => 
                    {
                        var ds = Math.Min(x.Seconds / 3600.0 * 20, 40);
                        var cs = Math.Min(x.PeakCpu * 0.4, 35);
                        var ms = Math.Min(x.PeakMem / 100.0, 25);
                        return Math.Min(ds + cs + ms, 100);
                    }), 1); // avoid divide by zero
                    var maxBarWidth = 200; // max pixels

                    // Determine color based on impact
                    string barColor;
                    if (impactScore >= 60) barColor = "#F87171"; // Red - high impact
                    else if (impactScore >= 35) barColor = "#FACC15"; // Yellow - medium
                    else barColor = "#4ADE80"; // Green - low impact

                    // Top rank gets special treatment - use accent color
                    bool isTopRank = rank == 1;
                    if (isTopRank) barColor = "#9A4DFF"; // Purple accent for #1

                    // Create brush from color
                    var barBrush = CreateBrushFromHex(barColor);

                    // Format duration
                    var duration = TimeSpan.FromSeconds(g.Seconds);
                    var durationText = duration.TotalHours >= 1 
                        ? $"{duration.Hours}h {duration.Minutes}m"
                        : $"{duration.Minutes}m {duration.Seconds}s";

                    TopOffenders.Add(new TopOffenderItem
                    {
                        Name = g.App,
                        Detail = $"{g.SessionCount} session{(g.SessionCount == 1 ? "" : "s")}",
                        Rank = rank,
                        Duration = durationText,
                        ImpactScore = impactScore,
                        BarWidth = (impactScore / maxImpact) * maxBarWidth,
                        BarColor = barColor,
                        BarBrush = barBrush,
                        CpuText = $"{g.PeakCpu:F0}%",
                        RamText = $"{g.PeakMem:F0}MB",
                        PeakCpu = g.PeakCpu,
                        PeakRam = g.PeakMem,
                        IsTopRank = isTopRank
                    });
                }
            }
            catch
            {
            }

            OnPropertyChanged(nameof(HasNoTopOffenders));
        }

        private static System.Windows.Media.SolidColorBrush CreateBrushFromHex(string hexColor)
        {
            try
            {
                // Remove # if present
                hexColor = hexColor.Replace("#", "");
                
                // Parse RGB
                byte r = byte.Parse(hexColor.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hexColor.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte b = byte.Parse(hexColor.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                
                return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
            }
            catch
            {
                // Fallback to accent color
                return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(154, 77, 255));
            }
        }

        private void BuildInsights(System.Collections.Generic.List<UsageTrackingService.SessionRecord> sessions)
        {
            // Throttle expensive ML analytics to reduce CPU usage
            var now = DateTime.Now;
            if (now - _lastInsightsBuildTime < _insightsThrottleInterval && Insights.Count > 0)
            {
                // Skip rebuilding insights if we have data and throttle hasn't elapsed
                return;
            }
            _lastInsightsBuildTime = now;

            Insights.Clear();

            try
            {
                // Get real-time process metrics with live historical samples
                var realtimeMetrics = UsageTrackingService.Instance.GetProcessMetricsForAnalytics();
                
                // Get past session data (last 2 days instead of 7) for additional ML context - reduced for performance
                var historicalMetrics = UsageTrackingService.Instance.GetHistoricalProcessMetricsForAnalytics(daysBack: 2);

                // Run ML-powered analytics with both real-time and historical data
                var mlInsights = _analyticsEngine.Analyze(realtimeMetrics, historicalMetrics, DateTime.Now);

                // Add ML-powered insights first (most important)
                foreach (var insight in mlInsights.Take(5))
                {
                    // Determine icon and color based on category and anomaly status
                    string icon = insight.IsAnomaly ? "⚠️" : GetCategoryIcon(insight.BehaviorCategory);
                    string color = insight.IsAnomaly ? "#F87171" : GetCategoryColor(insight.BehaviorCategory);
                    
                    var item = new InsightItem
                    {
                        Title = insight.IsAnomaly ? "Anomaly detected" : insight.BehaviorCategory.ToString(),
                        Detail = string.IsNullOrEmpty(insight.RecommendationText)
                            ? $"{insight.ProcessName}: Impact {insight.ImpactScore:F0}, Predicted CPU {insight.PredictionCpu:F1}%"
                            : insight.RecommendationText,
                        ProcessName = insight.ProcessName,
                        ImpactScore = insight.ImpactScore,
                        Category = insight.BehaviorCategory.ToString(),
                        PredictedCpu = insight.PredictionCpu,
                        PredictedMemory = insight.PredictionMemory,
                        IsAnomaly = insight.IsAnomaly,
                        Icon = icon,
                        ColorBrush = color
                    };

                    Insights.Add(item);
                }

                // Fallback: add traditional session-based insights if no ML insights
                if (Insights.Count == 0)
                {
                    var perApp = sessions
                        .GroupBy(s => string.IsNullOrWhiteSpace(s.app) ? "Unknown" : s.app)
                        .Select(g => new
                        {
                            App = g.Key,
                            TotalSeconds = g.Sum(x => Math.Max(0, x.durationSeconds)),
                            SessionCount = g.Count(),
                            PeakCpu = g.Max(x => x.peakCpuPercent),
                            AvgCpu = g.Average(x => x.averageCpuPercent),
                            PeakMem = g.Max(x => x.peakMemoryMB),
                            AvgMem = g.Average(x => x.averageMemoryMB)
                        })
                        .ToList();

                    var cpuHogs = perApp.Where(x => x.PeakCpu >= 70).OrderByDescending(x => x.PeakCpu).Take(2).ToList();
                    foreach (var x in cpuHogs)
                    {
                        Insights.Add(new InsightItem
                        {
                            Title = "High CPU spikes",
                            Detail = $"{x.App} peaked at {x.PeakCpu:F0}% CPU (avg {x.AvgCpu:F0}%)."
                        });
                    }

                    var memHogs = perApp.Where(x => x.PeakMem >= 1500).OrderByDescending(x => x.PeakMem).Take(2).ToList();
                    foreach (var x in memHogs)
                    {
                        Insights.Add(new InsightItem
                        {
                            Title = "High memory usage",
                            Detail = $"{x.App} peaked at {x.PeakMem:F0} MB RAM (avg {x.AvgMem:F0} MB)."
                        });
                    }
                }

                // Show "All good" if still empty
                if (Insights.Count == 0)
                {
                    Insights.Add(new InsightItem
                    {
                        Title = "All good",
                        Detail = "No unusual CPU/memory spikes detected in today's sessions."
                    });
                }
            }
            catch (Exception ex)
            {
                // Log error and show fallback message
                try
                {
                    var errorFile = Path.Combine(UsageTrackingService.GetBaseDirectory(), "dashboard-errors.txt");
                    File.AppendAllText(errorFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] BuildInsights error: {ex.Message}{Environment.NewLine}");
                }
                catch { }

                Insights.Add(new InsightItem
                {
                    Title = "Analytics unavailable",
                    Detail = "Process analytics engine temporarily unavailable."
                });
            }

            OnPropertyChanged(nameof(HasNoInsights));
        }

        private void UpdateStabilityIndicators()
        {
            try
            {
                var anyBad = RecentAlerts.Any(l => l.Contains("fail", StringComparison.OrdinalIgnoreCase) || l.Contains("error", StringComparison.OrdinalIgnoreCase));
                if (anyBad)
                {
                    StabilityText = "Needs Attention";
                    StabilityDetail = "Some rule actions reported errors";
                }
                else
                {
                    StabilityText = "OK";
                    StabilityDetail = "No issues detected";
                }
            }
            catch
            {
                StabilityText = "OK";
                StabilityDetail = "No issues detected";
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop timer (readonly, can't null it)
            try { _refreshTimer?.Stop(); } catch { }

            // Unsubscribe events
            try { AppViewStateService.ViewChanged -= OnViewChanged; } catch { }
            try
            {
                RecentAlerts.CollectionChanged -= OnRecentAlertsChanged;
                TopOffenders.CollectionChanged -= OnTopOffendersChanged;
                Insights.CollectionChanged -= OnInsightsChanged;
            }
            catch { }

            // Dispose performance counters and null out
            try { _diskReadBytes?.Dispose(); _diskReadBytes = null; } catch { }
            try { _diskWriteBytes?.Dispose(); _diskWriteBytes = null; } catch { }
            foreach (var pc in _gpuEngineUtil)
            {
                try { pc?.Dispose(); } catch { }
            }
            _gpuEngineUtil = Array.Empty<PerformanceCounter>();

            GC.SuppressFinalize(this);
        }
    }
}
