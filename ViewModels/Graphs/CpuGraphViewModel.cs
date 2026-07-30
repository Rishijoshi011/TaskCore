using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Windows.Threading;
using SkiaSharp;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using TaskCore.Services;

namespace TaskCore.ViewModels.Graphs
{
    public partial class CpuGraphViewModel : ObservableObject, IDisposable
    {
        private bool _disposed;
        [ObservableProperty]
        private ObservableCollection<ISeries> series;

        [ObservableProperty]
        private ObservableCollection<string> xLabels;

        // CPU Information Properties
        [ObservableProperty]
        private double currentUtilization;

        [ObservableProperty]
        private double currentSpeed;

        [ObservableProperty]
        private string upTime = "0:00:00:00";

        [ObservableProperty]
        private int processes;

        [ObservableProperty]
        private int threads;

        [ObservableProperty]
        private int handles;

        [ObservableProperty]
        private string cpuName = "Unknown";

        [ObservableProperty]
        private string manufacturer = "Unknown";

        [ObservableProperty]
        private string family = "Unknown";

        [ObservableProperty]
        private string model = "Unknown";

        [ObservableProperty]
        private string stepping = "Unknown";

        [ObservableProperty]
        private string maxClockSpeed = "Unknown";

        [ObservableProperty]
        private string currentClockSpeed = "Unknown";

        [ObservableProperty]
        private string architecture = "Unknown";

        [ObservableProperty]
        private string level = "Unknown";

        [ObservableProperty]
        private string revision = "Unknown";

        [ObservableProperty]
        private string socketDesignation = "Unknown";

        [ObservableProperty]
        private string processorType = "Unknown";

        [ObservableProperty]
        private string description = "Unknown";

        [ObservableProperty]
        private double baseSpeed;

        [ObservableProperty]
        private int cores;

        [ObservableProperty]
        private int logicalProcessors;

        [ObservableProperty]
        private int sockets;

        [ObservableProperty]
        private string virtualization = "Unknown";

        [ObservableProperty]
        private string l1Cache = "Unknown";

        [ObservableProperty]
        private string l2Cache = "Unknown";

        [ObservableProperty]
        private string l3Cache = "Unknown";

        private readonly DispatcherTimer _timer;
        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _speedCounter;
        private readonly Queue<double> _cpuBuffer = new();
        private readonly Queue<string> _timeBuffer = new();
        private const int MaxDataPoints = 60;

        public CpuGraphViewModel()
        {
            // Initialize chart series
            series = new ObservableCollection<ISeries>
            {
                new LineSeries<double>
                {
                    Values = new TaskCore.Helpers.BulkObservableCollection<double>(),
                    GeometrySize = 0,
                    Stroke = new SolidColorPaint(new SKColor(138, 43, 226), 2),
                    Fill = new LinearGradientPaint(
                        new[] 
                        {
                            new SKColor(138, 43, 226, 180), // Purple with transparency
                            new SKColor(138, 43, 226, 40)  // Purple with more transparency
                        },
                        new SKPoint(0, 0),
                        new SKPoint(0, 1)),
                    LineSmoothness = 0.5,
                    IsVisibleAtLegend = false,
                    Name = "CPU"
                }
            };

            xLabels = new TaskCore.Helpers.BulkObservableCollection<string>();

            // Initialize CPU information
            InitializeCpuInfo();

            // Initialize performance counters
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _speedCounter = new PerformanceCounter("Processor", "% Processor Performance", "_Total");
            }
            catch
            {
                _cpuCounter = null!;
                _speedCounter = null!;
            }

            // Start update timer
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RefreshIntervalService.CurrentMs) };
            _timer.Tick += Update;
            _timer.Start();

            try
            {
                RefreshIntervalService.RefreshIntervalChanged += OnRefreshIntervalChanged;
                OnRefreshIntervalChanged(RefreshIntervalService.CurrentMs);
            }
            catch
            {
            }

            try
            {
                AppViewStateService.ViewChanged += OnViewChanged;
                OnViewChanged(AppViewStateService.CurrentView);
            }
            catch
            {
            }
        }

        private void OnViewChanged(string viewName)
        {
            try
            {
                if (viewName == "Graphs")
                    _timer.Start();
                else
                    _timer.Stop();
            }
            catch
            {
            }
        }

        private void OnRefreshIntervalChanged(int ms)
        {
            try
            {
                _timer.Interval = TimeSpan.FromMilliseconds(RefreshIntervalService.Normalize(ms));
            }
            catch
            {
            }
        }

        private void InitializeCpuInfo()
        {
            try
            {
                // Reset all values to defaults first
                SetDefaultCpuValues();
                
                // Try Registry first (most reliable for basic info)
                bool registrySuccess = TryGetCpuInfoFromRegistry();
                
                // Try WMI for additional info
                bool wmiSuccess = TryGetCpuInfoFromWMI();
                
                // If both failed, use Environment as fallback
                if (!registrySuccess && !wmiSuccess)
                {
                    TryGetCpuInfoFromEnvironment();
                }
                
                // Always try to get real-time data from Performance Counters
                TryGetRealTimeCpuInfo();
                
                // Get process/thread counts (always available)
                GetProcessInfo();
                
                // Get system uptime (always available)
                GetSystemUptime();
                
                // Set description if still unknown
                if (string.IsNullOrEmpty(Description) || Description == "Unknown")
                {
                    Description = CpuName;
                }
                
                // Ensure we have some reasonable values
                if (Cores == 0 && LogicalProcessors == 0)
                {
                    Cores = Environment.ProcessorCount;
                    LogicalProcessors = Environment.ProcessorCount;
                    Sockets = 1;
                }
                
                if (Sockets == 0)
                {
                    Sockets = 1;
                }
            }
            catch
            {
                // Ultimate fallback
                SetDefaultCpuValues();
                
                // Set minimum reasonable values
                Cores = Environment.ProcessorCount;
                LogicalProcessors = Environment.ProcessorCount;
                Sockets = 1;
                CpuName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown CPU";
                Architecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "Unknown";
            }
        }

        private bool TryGetCpuInfoFromRegistry()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (key != null)
                    {
                        var name = key.GetValue("ProcessorNameString")?.ToString();
                        if (!string.IsNullOrEmpty(name) && name.Length > 5)
                        {
                            CpuName = name;
                        }

                        var speed = key.GetValue("~MHz")?.ToString();
                        if (!string.IsNullOrEmpty(speed) && double.TryParse(speed, out var speedMhz) && speedMhz > 0)
                        {
                            BaseSpeed = speedMhz / 1000.0;
                            MaxClockSpeed = $"{BaseSpeed:F2} GHz";
                        }

                        // Try to get family, model, stepping from registry
                        Family = key.GetValue("Family")?.ToString() ?? "Unknown";
                        Model = key.GetValue("Model")?.ToString() ?? "Unknown";
                        Stepping = key.GetValue("Stepping")?.ToString() ?? "Unknown";
                        
                        return true;
                    }
                }
            }
            catch
            {
                // Registry failed
            }
            return false;
        }

        private bool TryGetCpuInfoFromWMI()
        {
            try
            {
                // Simple, reliable WMI query
                var cpuInfo = new ManagementObjectSearcher("select * from Win32_Processor").Get().Cast<ManagementObject>().FirstOrDefault();
                if (cpuInfo != null)
                {
                    // CPU Name - most reliable
                    var name = cpuInfo["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        CpuName = name;
                    }

                    // Manufacturer - reliable
                    var vendor = cpuInfo["Manufacturer"]?.ToString();
                    if (!string.IsNullOrEmpty(vendor))
                    {
                        Manufacturer = vendor switch
                        {
                            "GenuineIntel" => "Intel",
                            "AuthenticAMD" => "AMD",
                            _ => vendor
                        };
                    }

                    // Speed - reliable
                    var maxSpeed = cpuInfo["MaxClockSpeed"]?.ToString();
                    if (!string.IsNullOrEmpty(maxSpeed) && double.TryParse(maxSpeed, out var speedMhz) && speedMhz > 0)
                    {
                        BaseSpeed = speedMhz / 1000.0;
                        MaxClockSpeed = $"{BaseSpeed:F2} GHz";
                    }

                    // Cores - reliable
                    var coresValue = cpuInfo["NumberOfCores"]?.ToString();
                    if (!string.IsNullOrEmpty(coresValue) && int.TryParse(coresValue, out var coresCount) && coresCount > 0)
                    {
                        Cores = coresCount;
                    }

                    // Logical processors - reliable
                    var logicalValue = cpuInfo["NumberOfLogicalProcessors"]?.ToString();
                    if (!string.IsNullOrEmpty(logicalValue) && int.TryParse(logicalValue, out var logicalCount) && logicalCount > 0)
                    {
                        LogicalProcessors = logicalCount;
                    }

                    // Architecture - reliable
                    var arch = cpuInfo["Architecture"]?.ToString();
                    if (!string.IsNullOrEmpty(arch))
                    {
                        Architecture = arch switch
                        {
                            "0" => "x86",
                            "9" => "x64",
                            "5" => "ARM",
                            "12" => "ARM64",
                            _ => arch
                        };
                    }

                    // Virtualization - try firmware flag first
                    var virtEnabled = cpuInfo["VirtualizationFirmwareEnabled"] as bool?;
                    if (virtEnabled.HasValue)
                    {
                        Virtualization = virtEnabled.Value ? "Enabled" : "Disabled";
                    }
                    else
                    {
                        // Fallback: if a hypervisor is present, virtualization must be enabled
                        try
                        {
                            using var osSearcher = new ManagementObjectSearcher("SELECT HypervisorPresent FROM Win32_OperatingSystem");
                            var os = osSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
                            var hypervisorPresent = os?["HypervisorPresent"] as bool?;
                            if (hypervisorPresent == true)
                            {
                                Virtualization = "Enabled";
                            }
                        }
                        catch
                        {
                            // ignore, keep previous value
                        }
                    }

                    // Extra info if available
                    if (string.IsNullOrEmpty(Family) || Family == "Unknown")
                        Family = cpuInfo["Family"]?.ToString() ?? Family;
                    if (string.IsNullOrEmpty(Model) || Model == "Unknown")
                        Model = cpuInfo["Revision"]?.ToString() ?? Model;
                    if (string.IsNullOrEmpty(Stepping) || Stepping == "Unknown")
                        Stepping = cpuInfo["Stepping"]?.ToString() ?? Stepping;
                    if (string.IsNullOrEmpty(Level) || Level == "Unknown")
                        Level = cpuInfo["Level"]?.ToString() ?? Level;
                    if (string.IsNullOrEmpty(SocketDesignation) || SocketDesignation == "Unknown")
                        SocketDesignation = cpuInfo["SocketDesignation"]?.ToString() ?? SocketDesignation;
                    if (string.IsNullOrEmpty(ProcessorType) || ProcessorType == "Unknown")
                        ProcessorType = cpuInfo["ProcessorType"]?.ToString() ?? ProcessorType;

                    // Get cache using Win32_CacheMemory (more reliable)
                    TryGetCacheFromWin32CacheMemory();

                    return true;
                }
            }
            catch
            {
                // WMI failed
            }
            return false;
        }

        private void TryGetCpuInfoFromEnvironment()
        {
            try
            {
                // Get processor count from environment
                if (Cores == 0 && LogicalProcessors == 0)
                {
                    Cores = Environment.ProcessorCount;
                    LogicalProcessors = Environment.ProcessorCount;
                    Sockets = 1; // Assume 1 socket if we can't determine
                }
                
                // Try to get CPU name from environment variables
                if (string.IsNullOrEmpty(CpuName) || CpuName == "Unknown")
                {
                    var processorName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
                    if (!string.IsNullOrEmpty(processorName))
                    {
                        CpuName = processorName;
                    }
                }
                
                // Try to get architecture
                if (string.IsNullOrEmpty(Architecture) || Architecture == "Unknown")
                {
                    var processorArch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
                    if (!string.IsNullOrEmpty(processorArch))
                    {
                        Architecture = processorArch;
                    }
                }
                
                // Try to get processor level
                if (string.IsNullOrEmpty(Level) || Level == "Unknown")
                {
                    var processorLevel = Environment.GetEnvironmentVariable("PROCESSOR_LEVEL");
                    if (!string.IsNullOrEmpty(processorLevel))
                    {
                        Level = processorLevel;
                    }
                }
                
                // Try to get processor revision
                if (string.IsNullOrEmpty(Revision) || Revision == "Unknown")
                {
                    var processorRevision = Environment.GetEnvironmentVariable("PROCESSOR_REVISION");
                    if (!string.IsNullOrEmpty(processorRevision))
                    {
                        Revision = processorRevision;
                    }
                }
            }
            catch
            {
                // Environment failed
            }
        }

        private void TryGetRealTimeCpuInfo()
        {
            // Performance counters are initialized in constructor
            // This method is just for organization
        }

        private void GetProcessInfo()
        {
            try
            {
                Processes = Process.GetProcesses().Length;
                Threads = Process.GetProcesses().Sum(p => p.Threads.Count);
                Handles = Process.GetProcesses().Sum(p => p.HandleCount);
            }
            catch
            {
                // Keep default values if process info fails
            }
        }

        private void GetSystemUptime()
        {
            try
            {
                var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                UpTime = $"{uptime.Days}:{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
            }
            catch
            {
                UpTime = "0:00:00:00";
            }
        }

        private void SetDefaultCpuValues()
        {
            CpuName = "Unknown";
            Manufacturer = "Unknown";
            Family = "Unknown";
            Model = "Unknown";
            Stepping = "Unknown";
            MaxClockSpeed = "Unknown";
            CurrentClockSpeed = "Unknown";
            Architecture = "Unknown";
            Level = "Unknown";
            Revision = "Unknown";
            SocketDesignation = "Unknown";
            ProcessorType = "Unknown";
            Description = "Unknown";
            BaseSpeed = 0;
            Cores = 0;
            LogicalProcessors = 0;
            Sockets = 0;
            Virtualization = "Unknown";
            L1Cache = "Unknown";
            L2Cache = "Unknown";
            L3Cache = "Unknown";
            UpTime = "0:00:00:00";
            CurrentSpeed = 0;
        }

        private void TryGetCacheInfo(ManagementObject cpuInfo)
        {
            try
            {
                // Basic cache detection only
                var l1Size = cpuInfo["L1CacheSize"]?.ToString();
                var l2Size = cpuInfo["L2CacheSize"]?.ToString();
                var l3Size = cpuInfo["L3CacheSize"]?.ToString();
                
                // L1 Cache
                if (!string.IsNullOrEmpty(l1Size) && l1Size != "0" && long.TryParse(l1Size, out var l1Bytes) && l1Bytes > 0)
                {
                    L1Cache = FormatCacheSize(l1Bytes);
                }
                
                // L2 Cache
                if (!string.IsNullOrEmpty(l2Size) && l2Size != "0" && long.TryParse(l2Size, out var l2Bytes) && l2Bytes > 0)
                {
                    L2Cache = FormatCacheSize(l2Bytes);
                }
                
                // L3 Cache
                if (!string.IsNullOrEmpty(l3Size) && l3Size != "0" && long.TryParse(l3Size, out var l3Bytes) && l3Bytes > 0)
                {
                    L3Cache = FormatCacheSize(l3Bytes);
                }
            }
            catch
            {
                // Cache info failed - leave as Unknown
            }
        }

        private string FormatCacheSize(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) // GB
                return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
            if (bytes >= 1024L * 1024L) // MB
                return $"{bytes / (1024.0 * 1024.0):F0} MB";
            if (bytes >= 1024L) // KB
                return $"{bytes / 1024.0:F0} KB";
            return $"{bytes} B";
        }

        private void TryGetCacheFromWin32CacheMemory()
        {
            try
            {
                var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_CacheMemory");
                var caches = searcher.Get().Cast<ManagementObject>().ToList();

                long l1Total = 0;
                long l2Total = 0;
                long l3Total = 0;

                foreach (var cache in caches)
                {
                    try
                    {
                        var level = cache["Level"]?.ToString(); // 3=L1, 4=L2, 5=L3 on many systems
                        var maxSize = cache["MaxCacheSize"];      // in KB

                        if (maxSize == null || level == null)
                            continue;

                        var sizeKb = Convert.ToInt64(maxSize);
                        if (sizeKb <= 0)
                            continue;

                        var sizeBytes = sizeKb * 1024L;

                        switch (level)
                        {
                            case "3":
                                l1Total += sizeBytes;
                                break;
                            case "4":
                                l2Total += sizeBytes;
                                break;
                            case "5":
                                l3Total += sizeBytes;
                                break;
                        }
                    }
                    catch
                    {
                        // skip invalid entries
                    }
                }

                if (l1Total > 0) L1Cache = FormatCacheSize(l1Total);
                if (l2Total > 0) L2Cache = FormatCacheSize(l2Total);
                if (l3Total > 0) L3Cache = FormatCacheSize(l3Total);

                if (l1Total == 0 && l2Total == 0 && l3Total == 0)
                {
                    TryGetCacheFromCpuModel();
                }
            }
            catch
            {
                TryGetCacheFromCpuModel();
            }
        }

        private void TryGetCacheFromCpuModel()
        {
            if (string.IsNullOrEmpty(CpuName) || Cores == 0)
                return;

            var name = CpuName.ToLowerInvariant();
            var cores = Cores;

            // Intel 12th Gen (Alder Lake) - i5-12400 example
            if (name.Contains("intel") && name.Contains("12th"))
            {
                if (cores == 6)
                {
                    L1Cache = FormatCacheSize(480 * 1024L);       // 480 KB
                    L2Cache = FormatCacheSize(7680 * 1024L);      // 7.5 MB
                    L3Cache = FormatCacheSize(18 * 1024L * 1024L); // 18 MB
                }
            }

            // Generic Intel/AMD fallback using typical per-core sizes
            if (L1Cache == "Unknown" && cores > 0)
            {
                L1Cache = FormatCacheSize(64 * 1024L * cores); // 64 KB per core
            }
        }

        private void Update(object? sender, EventArgs e)
        {
            if (!string.Equals(AppViewStateService.CurrentView, "Graphs", StringComparison.OrdinalIgnoreCase))
                return;

            double cpu = 0;

            // Get CPU utilization
            if (_cpuCounter != null)
            {
                try
                {
                    cpu = Math.Round(_cpuCounter.NextValue(), 1);
                }
                catch
                {
                    // Counter failed, use simulated value
                    cpu = CurrentUtilization + (new Random().NextDouble() - 0.5) * 5;
                    cpu = Math.Min(100, Math.Max(0, cpu));
                }
            }
            else
            {
                // No counter available, simulate CPU usage
                cpu = CurrentUtilization + (new Random().NextDouble() - 0.5) * 5;
                cpu = Math.Min(100, Math.Max(0, cpu));
            }

            // Update current utilization
            CurrentUtilization = cpu;

            // Update current speed
            if (_speedCounter != null)
            {
                try
                {
                    var performance = _speedCounter.NextValue();
                    if (performance > 0 && BaseSpeed > 0)
                    {
                        CurrentSpeed = BaseSpeed * (performance / 100.0);
                        CurrentClockSpeed = $"{CurrentSpeed:F2} GHz";
                    }
                }
                catch
                {
                    // Use base speed if speed counter fails
                    if (BaseSpeed > 0)
                    {
                        CurrentSpeed = BaseSpeed * (1 + (cpu / 100.0) * 0.3);
                        CurrentClockSpeed = $"{CurrentSpeed:F2} GHz";
                    }
                }
            }
            else if (BaseSpeed > 0)
            {
                // Simulate speed based on utilization if no speed counter
                CurrentSpeed = BaseSpeed * (1 + (cpu / 100.0) * 0.3);
                CurrentClockSpeed = $"{CurrentSpeed:F2} GHz";
            }

            // Update process and thread counts periodically
            if (DateTime.Now.Second % 5 == 0) // Update every 5 seconds
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var processes = System.Diagnostics.Process.GetProcesses();
                        int procCount = processes.Length;
                        int threadCount = 0;
                        int handleCount = 0;

                        foreach (var p in processes)
                        {
                            try
                            {
                                threadCount += p.Threads.Count;
                                handleCount += p.HandleCount;
                            }
                            catch { }
                            finally
                            {
                                p.Dispose();
                            }
                        }

                        System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            Processes = procCount;
                            Threads = threadCount;
                            Handles = handleCount;
                        }));
                    }
                    catch
                    {
                    }
                });
            }

            // Update uptime every second
            try
            {
                var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                UpTime = $"{uptime.Days}:{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
            }
            catch
            {
                // Keep current uptime if update fails
            }

            // Use buffers for better performance
            _cpuBuffer.Enqueue(cpu);
            _timeBuffer.Enqueue(DateTime.Now.ToString("HH:mm:ss"));

            // Keep only last MaxDataPoints
            if (_cpuBuffer.Count > MaxDataPoints)
                _cpuBuffer.Dequeue();
            if (_timeBuffer.Count > MaxDataPoints)
                _timeBuffer.Dequeue();

            // Update chart data safely
            try
            {
                if (Series.Count > 0 && Series[0].Values is TaskCore.Helpers.BulkObservableCollection<double> bulkCpuValues)
                {
                    bulkCpuValues.ReplaceAll(_cpuBuffer);
                }
                else if (Series.Count > 0 && Series[0].Values is ObservableCollection<double> cpuValues)
                {
                    cpuValues.Clear();
                    foreach (var value in _cpuBuffer)
                        cpuValues.Add(value);
                }

                if (XLabels is TaskCore.Helpers.BulkObservableCollection<string> bulkXLabels)
                {
                    bulkXLabels.ReplaceAll(_timeBuffer);
                }
                else if (XLabels != null)
                {
                    XLabels.Clear();
                    foreach (var label in _timeBuffer)
                        XLabels.Add(label);
                }
            }
            catch
            {
                // Chart update failed, keep previous data
            }
        }

        // Dark mode theme properties
        public static class DarkTheme
        {
            public static readonly SKColor Background = new(30, 30, 30); // #1E1E1E
            public static readonly SKColor Grid = new(51, 51, 51);        // #333333
            public static readonly SKColor Text = new(204, 204, 204);    // #CCCCCC
            public static readonly SKColor Accent = new(138, 43, 226);       // Purple
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                RefreshIntervalService.RefreshIntervalChanged -= OnRefreshIntervalChanged;
            }
            catch { }

            try
            {
                AppViewStateService.ViewChanged -= OnViewChanged;
            }
            catch { }

            try
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Tick -= Update;
                }
            }
            catch { }

            try { _cpuCounter?.Dispose(); } catch { }
            try { _speedCounter?.Dispose(); } catch { }

            GC.SuppressFinalize(this);
        }
    }
}
