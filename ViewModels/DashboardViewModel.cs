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
using TaskCore.Services;

namespace TaskCore.ViewModels
{
    public sealed partial class TopOffenderItem : ObservableObject
    {
        [ObservableProperty] private string name = string.Empty;
        [ObservableProperty] private string detail = string.Empty;
    }

    public sealed partial class InsightItem : ObservableObject
    {
        [ObservableProperty] private string title = string.Empty;
        [ObservableProperty] private string detail = string.Empty;
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

    public partial class DashboardViewModel : ObservableObject
    {
        private readonly DispatcherTimer _refreshTimer;

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
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c taskkill /f /im explorer.exe & start explorer.exe",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                ActionStatusText = "Explorer restarted";
            }
            catch (Exception ex)
            {
                ActionStatusText = $"Restart Explorer failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private void FlushDns()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c ipconfig /flushdns",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                ActionStatusText = "DNS flushed";
            }
            catch (Exception ex)
            {
                ActionStatusText = $"Flush DNS failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private void ClearTemp()
        {
            try
            {
                var temp = Path.GetTempPath();
                if (Directory.Exists(temp))
                {
                    foreach (var file in Directory.GetFiles(temp))
                    {
                        try { File.Delete(file); } catch { }
                    }

                    foreach (var dir in Directory.GetDirectories(temp))
                    {
                        try { Directory.Delete(dir, recursive: true); } catch { }
                    }
                }

                ActionStatusText = "Temp cleared";
            }
            catch (Exception ex)
            {
                ActionStatusText = $"Clear Temp failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private void RestartAudioService()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c net stop audiosrv & net start audiosrv",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                ActionStatusText = "Audio service restarted";
            }
            catch (Exception ex)
            {
                ActionStatusText = $"Restart Audio failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private void RestartGpuDriver()
        {
            try
            {
                // Try PowerShell approach to disable/enable display adapters (requires admin)
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-Command \"Get-PnpDevice -Class DisplayAdapter -Status OK | Disable-PnpDevice -Confirm:$false; Start-Sleep 2; Get-PnpDevice -Class DisplayAdapter | Enable-PnpDevice -Confirm:$false\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                Process.Start(psi);
                ActionStatusText = "GPU driver restart initiated (may need admin)";
            }
            catch (Exception ex)
            {
                ActionStatusText = $"GPU restart failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private void ResetNetworkStack()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c netsh winsock reset & netsh int ip reset & ipconfig /release & ipconfig /renew & ipconfig /flushdns",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                ActionStatusText = "Network stack reset initiated";
            }
            catch (Exception ex)
            {
                ActionStatusText = $"Network reset failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private void RenewIp()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c ipconfig /release & ipconfig /renew",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                ActionStatusText = "IP renewed";
            }
            catch (Exception ex)
            {
                ActionStatusText = $"IP renew failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private void RestartPrintSpooler()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c net stop spooler & net start spooler",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                ActionStatusText = "Print spooler restarted";
            }
            catch (Exception ex)
            {
                ActionStatusText = $"Print spooler restart failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private void RestartWindowsSearch()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c net stop wsearch & net start wsearch",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                ActionStatusText = "Windows Search restarted";
            }
            catch (Exception ex)
            {
                ActionStatusText = $"Windows Search restart failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private void ClearIconCache()
        {
            try
            {
                // Kill explorer, delete icon cache, restart explorer
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c taskkill /f /im explorer.exe & del /f /q %localappdata%\\IconCache.db & start explorer.exe",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(psi);
                ActionStatusText = "Icon cache cleared";
            }
            catch (Exception ex)
            {
                ActionStatusText = $"Clear icon cache failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private void SfcScan()
        {
            try
            {
                // SFC requires admin - will fail silently if not elevated
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c sfc /scannow",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                ActionStatusText = "SFC scan started (may need admin)";
            }
            catch (Exception ex)
            {
                ActionStatusText = $"SFC scan failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private void DismRepair()
        {
            try
            {
                // DISM requires admin
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c DISM /Online /Cleanup-Image /RestoreHealth",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                ActionStatusText = "DISM repair started (may need admin)";
            }
            catch (Exception ex)
            {
                ActionStatusText = $"DISM repair failed: {ex.Message}";
            }
        }

        [ObservableProperty] private string todayDateText = DateTime.Today.ToString("ddd, dd MMM");
        [ObservableProperty] private string totalUsageText = "-";
        [ObservableProperty] private string totalUsageVsYesterdayText = string.Empty;
        [ObservableProperty] private string lastUpdatedText = "Updated: --";
        [ObservableProperty] private string sessionCountText = "0 sessions";

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

        [ObservableProperty] private ObservableCollection<HeatmapHourCell> hourlyHeatmap = new();
        [ObservableProperty] private ObservableCollection<WeeklyOverviewItem> weeklyOverview = new();

        [ObservableProperty] private string actionStatusText = string.Empty;

        public DashboardViewModel()
        {
            BuildHeatmapSkeleton();

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

            Refresh();
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

        private void OnViewChanged(string viewName)
        {
            try
            {
                if (string.Equals(viewName, "Dashboard", StringComparison.OrdinalIgnoreCase))
                {
                    ResetPerfPeaksIfNewDay();
                    EnsurePerfCountersInitialized();

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
        private void OpenRules() => AppViewStateService.SetView("Rules");

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
                var sessions = UsageTrackingService.GetDailySessions(date);
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

        private void Refresh()
        {
            try
            {
                TodayDateText = DateTime.Today.ToString("ddd, dd MMM");

                var date = DateOnly.FromDateTime(DateTime.Today);
                var sessions = UsageTrackingService.GetDailySessions(date);

                IsEmpty = sessions.Count == 0;
                OnPropertyChanged(nameof(IsNotEmpty));

                if (sessions.Count > 0)
                {
                    var totalSeconds = sessions.Sum(s => Math.Max(0, s.durationSeconds));
                    var activeSeconds = sessions.Where(s => !s.isIdle).Sum(s => Math.Max(0, s.durationSeconds));
                    var idleSeconds = sessions.Where(s => s.isIdle).Sum(s => Math.Max(0, s.durationSeconds));
                    
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
            // total runtime per hour (local)
            var minutesPerHour = new double[24];

            foreach (var s in sessions)
            {
                var start = s.startTimeUtc.ToLocalTime();
                var end = s.endTimeUtc.ToLocalTime();

                if (end < start) continue;
                if (start.Date != DateTime.Today.Date && end.Date != DateTime.Today.Date) continue;

                // clamp to today
                var dayStart = DateTime.Today;
                var dayEnd = DateTime.Today.AddDays(1);
                if (start < dayStart) start = dayStart;
                if (end > dayEnd) end = dayEnd;

                var cursor = start;
                while (cursor < end)
                {
                    var hour = cursor.Hour;
                    var nextHour = new DateTime(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, 0, 0).AddHours(1);
                    var sliceEnd = end < nextHour ? end : nextHour;
                    var mins = (sliceEnd - cursor).TotalMinutes;
                    minutesPerHour[hour] += Math.Max(0, mins);
                    cursor = sliceEnd;
                }
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

            var days = Enumerable.Range(0, 7)
                .Select(offset => DateOnly.FromDateTime(DateTime.Today.AddDays(-6 + offset)))
                .ToList();

            var dayData = days.Select(d =>
            {
                var sessions = UsageTrackingService.GetDailySessions(d);
                var totalSec = sessions.Sum(s => Math.Max(0, s.durationSeconds));

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
                var logFile = Path.Combine(logDir, $"{DateTime.Now:yyyy-MM-dd}.log");
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
                var grouped = sessions
                    .GroupBy(s => string.IsNullOrWhiteSpace(s.app) ? "Unknown" : s.app)
                    .Select(g => new
                    {
                        App = g.Key,
                        Seconds = g.Sum(x => Math.Max(0, x.durationSeconds)),
                        PeakCpu = g.Max(x => x.peakCpuPercent),
                        PeakMem = g.Max(x => x.peakMemoryMB)
                    })
                    .OrderByDescending(x => x.Seconds)
                    .Take(6)
                    .ToList();

                foreach (var g in grouped)
                {
                    TopOffenders.Add(new TopOffenderItem
                    {
                        Name = g.App,
                        Detail = $"{TimeSpan.FromSeconds(g.Seconds).ToString(@"hh\:mm")} | CPU {g.PeakCpu:F0}% | RAM {g.PeakMem:F0}MB"
                    });
                }
            }
            catch
            {
            }

            OnPropertyChanged(nameof(HasNoTopOffenders));
        }

        private void BuildInsights(System.Collections.Generic.List<UsageTrackingService.SessionRecord> sessions)
        {
            Insights.Clear();

            try
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

                var frequent = perApp.Where(x => x.SessionCount >= 5).OrderByDescending(x => x.SessionCount).Take(2).ToList();
                foreach (var x in frequent)
                {
                    Insights.Add(new InsightItem
                    {
                        Title = "Frequent restarts",
                        Detail = $"{x.App} started {x.SessionCount} times today."
                    });
                }

                if (Insights.Count == 0)
                {
                    Insights.Add(new InsightItem
                    {
                        Title = "All good",
                        Detail = "No unusual CPU/memory spikes detected in today's sessions."
                    });
                }
            }
            catch
            {
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
    }
}
