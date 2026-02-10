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

namespace TaskCore.ViewModels.Graphs
{
    public partial class CpuGraphViewModel : ObservableObject
    {
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
        private int processes;
        
        [ObservableProperty]
        private int threads;
        
        [ObservableProperty]
        private int handles;
        
        [ObservableProperty]
        private string upTime;
        
        [ObservableProperty]
        private double baseSpeed;
        
        [ObservableProperty]
        private int sockets;
        
        [ObservableProperty]
        private int cores;
        
        [ObservableProperty]
        private int logicalProcessors;
        
        [ObservableProperty]
        private string virtualization;
        
        [ObservableProperty]
        private string l1Cache;
        
        [ObservableProperty]
        private string l2Cache;
        
        [ObservableProperty]
        private string l3Cache;
        
        [ObservableProperty]
        private string cpuName;

        private readonly DispatcherTimer _timer;
        private readonly PerformanceCounter _cpuCounter;
        private readonly Queue<double> _cpuBuffer = new();
        private readonly Queue<string> _timeBuffer = new();
        private const int MaxDataPoints = 60;

        public CpuGraphViewModel()
        {
            series = new ObservableCollection<ISeries>
            {
                new LineSeries<double>
                {
                    Values = new ObservableCollection<double>(),
                    GeometrySize = 0,
                    Stroke = new SolidColorPaint(DarkTheme.Accent, 2),
                    Fill = null,
                    LineSmoothness = 0,
                    IsVisibleAtLegend = false,
                    Name = "CPU"
                }
            };

            xLabels = new ObservableCollection<string>();

            // Initialize CPU information
            InitializeCpuInfo();

            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue(); // warmup call
            }
            catch
            {
                // fallback: 0% CPU if PerformanceCounter fails (rare)
                _cpuCounter = null;
            }

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) }; // Faster updates for smoothness
            _timer.Tick += Update;
            _timer.Start();
        }

        private void InitializeCpuInfo()
        {
            try
            {
                // Get CPU name using registry (more reliable than WMI)
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (key != null)
                    {
                        CpuName = key.GetValue("ProcessorNameString")?.ToString() ?? "Unknown CPU";
                        var maxSpeed = key.GetValue("~MHz")?.ToString();
                        if (!string.IsNullOrEmpty(maxSpeed))
                            BaseSpeed = Convert.ToDouble(maxSpeed) / 1000.0;
                    }
                }

                // Fallback to WMI if registry fails
                if (CpuName == "Unknown CPU")
                {
                    var cpuInfo = new ManagementObjectSearcher("select * from Win32_Processor").Get().Cast<ManagementObject>().FirstOrDefault();
                    if (cpuInfo != null)
                    {
                        CpuName = cpuInfo["Name"]?.ToString() ?? "Unknown CPU";
                        BaseSpeed = Convert.ToDouble(cpuInfo["MaxClockSpeed"]) / 1000.0; // Convert MHz to GHz
                        Cores = Convert.ToInt32(cpuInfo["NumberOfCores"]);
                        LogicalProcessors = Convert.ToInt32(cpuInfo["NumberOfLogicalProcessors"]);
                        Virtualization = (cpuInfo["VirtualizationFirmwareEnabled"] as bool? ?? false) ? "Enabled" : "Disabled";
                        
                        // Try to get cache information
                        try
                        {
                            var l1Size = cpuInfo["L1CacheSize"];
                            var l2Size = cpuInfo["L2CacheSize"];
                            var l3Size = cpuInfo["L3CacheSize"];
                            
                            if (l1Size != null)
                                L1Cache = $"{Convert.ToInt32(l1Size) / 1024} KB";
                            if (l2Size != null)
                                L2Cache = $"{Convert.ToInt32(l2Size) / 1024} KB";
                            if (l3Size != null)
                                L3Cache = $"{Convert.ToInt32(l3Size) / 1024} KB";
                        }
                        catch
                        {
                            // Cache info not available, will use defaults below
                        }
                    }
                }

                // Get core and logical processor count from Environment (most reliable)
                if (Cores == 0)
                    Cores = Environment.ProcessorCount;
                if (LogicalProcessors == 0)
                    LogicalProcessors = Environment.ProcessorCount;

                // Get system uptime (more reliable method)
                try
                {
                    using (var uptimeCounter = new PerformanceCounter("System", "System Up Time"))
                    {
                        var uptime = TimeSpan.FromSeconds(uptimeCounter.NextValue());
                        UpTime = $"{uptime.Days}:{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
                    }
                }
                catch
                {
                    // Fallback to Environment.TickCount
                    var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                    UpTime = $"{uptime.Days}:{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
                }

                // Set initial CPU speed
                try
                {
                    using (var perfCounter = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total"))
                    {
                        var performance = perfCounter.NextValue();
                        CurrentSpeed = performance > 0 ? BaseSpeed * (performance / 100.0) : BaseSpeed;
                    }
                }
                catch
                {
                    CurrentSpeed = BaseSpeed; // Fallback to base speed
                }

                // Get initial process and thread counts
                Processes = Process.GetProcesses().Length;
                Threads = Process.GetProcesses().Sum(p => 
                {
                    try
                    {
                        return p.Threads.Count;
                    }
                    catch
                    {
                        return 0;
                    }
                });

                Handles = Process.GetProcesses().Sum(p => 
                {
                    try
                    {
                        return p.HandleCount;
                    }
                    catch
                    {
                        return 0;
                    }
                });

                // Set cache sizes to reasonable defaults if still unknown
                if (string.IsNullOrEmpty(L1Cache) || L1Cache == "Unknown")
                    L1Cache = $"{Cores * 64} KB"; // Estimate: ~64KB per core
                if (string.IsNullOrEmpty(L2Cache) || L2Cache == "Unknown")
                    L2Cache = $"{Cores * 256} KB"; // Estimate: ~256KB per core
                if (string.IsNullOrEmpty(L3Cache) || L3Cache == "Unknown")
                    L3Cache = $"{Cores * 2} MB"; // Estimate: ~2MB per core

                // Set virtualization status if still unknown
                if (string.IsNullOrEmpty(Virtualization) || Virtualization == "Unknown")
                    Virtualization = "Enabled"; // Most modern CPUs have virtualization

                // Ensure sockets has a value
                if (Sockets == 0)
                    Sockets = 1; // Most consumer systems have 1 socket
            }
            catch
            {
                // Default values if system info retrieval fails
                CpuName = "Unknown CPU";
                BaseSpeed = 2.5;
                Cores = Environment.ProcessorCount;
                LogicalProcessors = Environment.ProcessorCount;
                Sockets = 1;
                Virtualization = "Enabled";
                L1Cache = $"{Environment.ProcessorCount * 64} KB";
                L2Cache = $"{Environment.ProcessorCount * 256} KB";
                L3Cache = $"{Environment.ProcessorCount * 2} MB";
                UpTime = "0:00:00:00";
                CurrentSpeed = BaseSpeed;
                Processes = Process.GetProcesses().Length;
                Threads = Process.GetProcesses().Sum(p => p.Threads.Count);
                Handles = Process.GetProcesses().Sum(p => p.HandleCount);
            }
        }

        // Dark mode theme properties
        public static class DarkTheme
        {
            public static readonly SKColor Background = new(30, 30, 30); // #1E1E1E
            public static readonly SKColor Grid = new(51, 51, 51);        // #333333
            public static readonly SKColor Text = new(204, 204, 204);    // #CCCCCC
            public static readonly SKColor Accent = new(138, 43, 226);       // Blue Violet Purple Green
        }

        private void Update(object? sender, EventArgs e)
        {
            double cpu = 0;

            if (_cpuCounter != null)
                cpu = Math.Round(_cpuCounter.NextValue(), 1);

            // Update current utilization
            CurrentUtilization = cpu;

            // Update real-time CPU speed
            try
            {
                using (var perfCounter = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total"))
                {
                    var performance = perfCounter.NextValue();
                    if (performance > 0)
                        CurrentSpeed = BaseSpeed * (performance / 100.0);
                }
            }
            catch
            {
                CurrentSpeed = BaseSpeed; // Fallback to base speed
            }

            // Update process and thread counts periodically
            if (DateTime.Now.Second % 5 == 0) // Update every 5 seconds
            {
                Processes = Process.GetProcesses().Length;
                Threads = Process.GetProcesses().Sum(p => 
                {
                    try
                    {
                        return p.Threads.Count;
                    }
                    catch
                    {
                        return 0;
                    }
                });

                Handles = Process.GetProcesses().Sum(p => 
                {
                    try
                    {
                        return p.HandleCount;
                    }
                    catch
                    {
                        return 0;
                    }
                });
            }

            // Use buffers for better performance
            _cpuBuffer.Enqueue(cpu);
            _timeBuffer.Enqueue(DateTime.Now.ToString("HH:mm:ss"));

            // Keep only last MaxDataPoints
            if (_cpuBuffer.Count > MaxDataPoints)
                _cpuBuffer.Dequeue();
            if (_timeBuffer.Count > MaxDataPoints)
                _timeBuffer.Dequeue();

            // Batch update UI for better performance
            var cpuValues = (ObservableCollection<double>)Series[0].Values;
            cpuValues.Clear();
            foreach (var value in _cpuBuffer)
                cpuValues.Add(value);

            XLabels.Clear();
            foreach (var label in _timeBuffer)
                XLabels.Add(label);
        }
    }
}
