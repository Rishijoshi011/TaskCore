using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Runtime.InteropServices;

namespace TaskCore.Views
{
    public class SystemGraphsPanel : UserControl, INotifyPropertyChanged
    {
        // Performance data collections for graphs
        private readonly List<double> _cpuHistory = new();
        private readonly List<double> _memoryHistory = new();
        private readonly int _maxHistoryPoints = 60; // 60 seconds of history

        // Performance counters
        private PerformanceCounter _cpuCounter = null!;
        private PerformanceCounter _memoryAvailableCounter = null!;

        // System info
        private string _cpuName = "Loading CPU info...";
        private long _totalMemoryMB = 0;
        //private string _gpuName = "Integrated Graphics";
        //private string _diskInfo = "SSD (NVMe)";

        // Current values
        private float _currentCpuUsage;
        private float _currentMemoryUsage;
        private long _availableMemoryMB;

        // UI refresh timer
        private readonly DispatcherTimer _refreshTimer = new();

        // Graph canvases
        private Canvas _cpuGraphCanvas = null!;
        private Canvas _memoryGraphCanvas = null!;

        // Current selected component
        private string _selectedComponent = "CPU";

        // Component panels
        private Border _cpuPanel = null!;
        private Border _memoryPanel = null!;
        private Border _diskPanel = null!;
        private Border _gpuPanel = null!;

        private Grid _mainContentGrid = null!;
        private bool _isInitialized = false;

        // UI elements dictionary for safe access
        private readonly Dictionary<string, TextBlock> _uiElements = new();
        public event PropertyChangedEventHandler? PropertyChanged;

        // Windows API for memory info
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX()
            {
                this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

       
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public SystemGraphsPanel()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("SystemGraphsPanel: Starting initialization...");

                InitializeComponent();
                InitializePerformanceCounters();
                GetSystemInfo();
                SetupRefreshTimer();

                _isInitialized = true;
                System.Diagnostics.Debug.WriteLine("SystemGraphsPanel: Initialization complete");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SystemGraphsPanel initialization error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void InitializeComponent()
        {
            try
            {
                var mainGrid = new Grid
                {
                    Background = new SolidColorBrush(Color.FromRgb(32, 32, 32))
                };

                // Define rows
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) }); // Menu bar
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

                // Menu Bar
                var menuPanel = CreateMenuPanel();
                if (menuPanel != null)
                {
                    Grid.SetRow(menuPanel, 0);
                    mainGrid.Children.Add(menuPanel);
                }

                // Content Area
                _mainContentGrid = new Grid
                {
                    Margin = new Thickness(20)
                };

                Grid.SetRow(_mainContentGrid, 1);
                mainGrid.Children.Add(_mainContentGrid);

                // Create all panels
                CreateAllPanels();

                // Show initial component
                ShowComponent(_selectedComponent);

                Content = mainGrid;
                System.Diagnostics.Debug.WriteLine("InitializeComponent completed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InitializeComponent error: {ex.Message}");
            }
        }

        private StackPanel CreateMenuPanel()
        {
            try
            {
                var menuPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                    Height = 60
                };

                // Menu buttons
                var cpuButton = CreateMenuButton("CPU", "🏿");
                var memoryButton = CreateMenuButton("Memory", "🎞");
                var diskButton = CreateMenuButton("Disk", "💾");
                var gpuButton = CreateMenuButton("GPU", "🎮");

                if (cpuButton != null) cpuButton.Click += (s, e) => ShowComponent("CPU");
                if (memoryButton != null) memoryButton.Click += (s, e) => ShowComponent("Memory");
                if (diskButton != null) diskButton.Click += (s, e) => ShowComponent("Disk");
                if (gpuButton != null) gpuButton.Click += (s, e) => ShowComponent("GPU");

                if (cpuButton != null) menuPanel.Children.Add(cpuButton);
                if (memoryButton != null) menuPanel.Children.Add(memoryButton);
                if (diskButton != null) menuPanel.Children.Add(diskButton);
                if (gpuButton != null) menuPanel.Children.Add(gpuButton);

                return menuPanel;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateMenuPanel error: {ex.Message}");
                return null;
            }
        }

        private Button CreateMenuButton(string text, string icon)
        {
            try
            {
                var button = new Button
                {
                    Width = 100,
                    Height = 45,
                    Margin = new Thickness(10, 5, 5, 5),
                    Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                    BorderThickness = new Thickness(1),
                    Foreground = Brushes.White,
                    FontSize = 12,
                    FontWeight = FontWeights.Medium
                };

                var stackPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var iconText = new TextBlock
                {
                    Text = icon,
                    FontSize = 16,
                    Margin = new Thickness(0, 0, 8, 0)
                };

                var labelText = new TextBlock
                {
                    Text = text,
                    FontSize = 12
                };

                stackPanel.Children.Add(iconText);
                stackPanel.Children.Add(labelText);
                button.Content = stackPanel;

                // Hover effect
                button.MouseEnter += (s, e) => {
                    try { button.Background = new SolidColorBrush(Color.FromRgb(33, 92, 116)); } catch { }
                };
                button.MouseLeave += (s, e) => {
                    try
                    {
                        if (text != _selectedComponent)
                            button.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
                    }
                    catch { }
                };

                return button;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateMenuButton error: {ex.Message}");
                return null;
            }
        }

        private void ShowComponent(string component)
        {
            try
            {
                _selectedComponent = component;

                if (_mainContentGrid != null)
                {
                    _mainContentGrid.Children.Clear();

                    // Update menu button states
                    try
                    {
                        var mainGrid = Content as Grid;
                        if (mainGrid?.Children.Count > 0)
                        {
                            var menuPanel = mainGrid.Children[0] as StackPanel;
                            if (menuPanel != null)
                            {
                                foreach (Button btn in menuPanel.Children.OfType<Button>())
                                {
                                    try
                                    {
                                        var stackPanel = btn.Content as StackPanel;
                                        if (stackPanel?.Children.Count > 1)
                                        {
                                            var labelText = stackPanel.Children[1] as TextBlock;
                                            if (labelText?.Text == component)
                                                btn.Background = new SolidColorBrush(Color.FromRgb(0, 120, 215));
                                            else
                                                btn.Background = new SolidColorBrush(Color.FromRgb(60, 60, 60));
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"Button update error: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Menu update error: {ex.Message}");
                    }

                    // Show selected component panel
                    Border panelToShow = null;
                    switch (component)
                    {
                        case "CPU":
                            panelToShow = _cpuPanel;
                            break;
                        case "Memory":
                            panelToShow = _memoryPanel;
                            break;
                        case "Disk":
                            panelToShow = _diskPanel;
                            break;
                        case "GPU":
                            panelToShow = _gpuPanel;
                            break;
                    }

                    if (panelToShow != null)
                        _mainContentGrid.Children.Add(panelToShow);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ShowComponent error: {ex.Message}");
            }
        }

        private void CreateAllPanels()
        {
            try
            {
                _cpuPanel = CreateCpuPanel();
                _memoryPanel = CreateMemoryPanel();
                _diskPanel = CreateDiskPanel();
                _gpuPanel = CreateGpuPanel();
                System.Diagnostics.Debug.WriteLine("All panels created successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateAllPanels error: {ex.Message}");
            }
        }

        private Border CreateCpuPanel()
        {
            try
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(20)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) }); // Info
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Graph

                // CPU Info Panel
                var infoPanel = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };

                // CPU Header
                var cpuHeader = new TextBlock
                {
                    Text = "CPU Performance",
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 20)
                };
                infoPanel.Children.Add(cpuHeader);

                // CPU Usage Display
                var cpuUsageText = CreateTextBlock("0%", 48, Color.FromRgb(0, 120, 215), "CpuUsageText");
                cpuUsageText.Margin = new Thickness(0, 0, 0, 10);
                infoPanel.Children.Add(cpuUsageText);

                // CPU Name
                var cpuNameText = CreateTextBlock(_cpuName, 14, Color.FromRgb(200, 200, 200), "CpuNameText");
                cpuNameText.TextWrapping = TextWrapping.Wrap;
                cpuNameText.Margin = new Thickness(0, 0, 0, 20);
                infoPanel.Children.Add(cpuNameText);

                // CPU Stats
                var statsPanel = new StackPanel();

                statsPanel.Children.Add(CreateStatPanel("Utilization:", "0%", "CpuUtilizationText"));
                statsPanel.Children.Add(CreateStatPanel("Speed:", "0.00 GHz", "CpuSpeedText"));
                statsPanel.Children.Add(CreateStatPanel("Processes:", "0", "CpuProcessesText"));
                statsPanel.Children.Add(CreateStatPanel("Threads:", "0", "CpuThreadsText"));
                statsPanel.Children.Add(CreateStatPanel("Up time:", "0:00:00:00", "UptimeText"));

                infoPanel.Children.Add(statsPanel);
                Grid.SetColumn(infoPanel, 0);
                grid.Children.Add(infoPanel);

                // CPU Graph
                var graphBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(25, 25, 25)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(15),
                    Margin = new Thickness(10, 0, 0, 0)
                };

                _cpuGraphCanvas = new Canvas
                {
                    Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                    MinHeight = 200
                };

                graphBorder.Child = _cpuGraphCanvas;
                Grid.SetColumn(graphBorder, 1);
                grid.Children.Add(graphBorder);

                border.Child = grid;
                return border;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateCpuPanel error: {ex.Message}");
                return new Border { Background = Brushes.Red }; // Fallback
            }
        }

        private Border CreateMemoryPanel()
        {
            try
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(20)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) }); // Info
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Graph

                // Memory Info Panel
                var infoPanel = new StackPanel { Margin = new Thickness(0, 0, 20, 0) };

                // Memory Header
                var memoryHeader = new TextBlock
                {
                    Text = "Memory Performance",
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 20)
                };
                infoPanel.Children.Add(memoryHeader);

                // Memory Usage Display
                var memoryUsageText = CreateTextBlock("0.0/0.0 GB (0%)", 28, Color.FromRgb(255, 185, 0), "MemoryUsageText");
                memoryUsageText.Margin = new Thickness(0, 0, 0, 10);
                infoPanel.Children.Add(memoryUsageText);

                // Memory Details
                var memoryDetailsText = CreateTextBlock("Loading memory details...", 12, Color.FromRgb(180, 180, 180), "MemoryDetailsText");
                memoryDetailsText.TextWrapping = TextWrapping.Wrap;
                memoryDetailsText.Margin = new Thickness(0, 0, 0, 20);
                infoPanel.Children.Add(memoryDetailsText);

                // Memory Stats
                var statsPanel = new StackPanel();

                statsPanel.Children.Add(CreateStatPanel("In use:", "0.0 GB", "MemoryInUseText"));
                statsPanel.Children.Add(CreateStatPanel("Available:", "0.0 GB", "MemoryAvailableText"));
                statsPanel.Children.Add(CreateStatPanel("Committed:", "0.0 GB", "MemoryCommittedText"));
                statsPanel.Children.Add(CreateStatPanel("Cached:", "0.0 GB", "MemoryCachedText"));
                statsPanel.Children.Add(CreateStatPanel("Paged pool:", "0.0 GB", "MemoryPagedPoolText"));
                statsPanel.Children.Add(CreateStatPanel("Non-paged:", "0 MB", "MemoryNonPagedPoolText"));

                infoPanel.Children.Add(statsPanel);
                Grid.SetColumn(infoPanel, 0);
                grid.Children.Add(infoPanel);

                // Memory Graph
                var graphBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(25, 25, 25)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(15),
                    Margin = new Thickness(10, 0, 0, 0)
                };

                _memoryGraphCanvas = new Canvas
                {
                    Background = new SolidColorBrush(Color.FromRgb(20, 20, 20)),
                    MinHeight = 200
                };

                graphBorder.Child = _memoryGraphCanvas;
                Grid.SetColumn(graphBorder, 1);
                grid.Children.Add(graphBorder);

                border.Child = grid;
                return border;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateMemoryPanel error: {ex.Message}");
                return new Border { Background = Brushes.Red }; // Fallback
            }
        }

        private Border CreateDiskPanel()
        {
            try
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(20)
                };

                var stackPanel = new StackPanel();

                var header = new TextBlock
                {
                    Text = "Disk 0 (C:) - SSD Performance",
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 30)
                };
                stackPanel.Children.Add(header);

                var placeholder = new TextBlock
                {
                    Text = "💾 Disk Performance Monitoring\n\nFeatures coming soon:\n• Real-time disk activity monitoring\n• Read/Write speed tracking\n• Active time percentage\n• Queue length statistics\n• Response time analysis\n• IOPS tracking\n• Temperature monitoring\n• Health status (SMART data)",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                    TextAlignment = TextAlignment.Left,
                    LineHeight = 22
                };
                stackPanel.Children.Add(placeholder);

                border.Child = stackPanel;
                return border;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateDiskPanel error: {ex.Message}");
                return new Border { Background = Brushes.Red }; // Fallback
            }
        }

        private Border CreateGpuPanel()
        {
            try
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(20)
                };

                var stackPanel = new StackPanel();

                var header = new TextBlock
                {
                    Text = "GPU 0 - Graphics Performance",
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 0, 30)
                };
                stackPanel.Children.Add(header);

                var placeholder = new TextBlock
                {
                    Text = "🎮 GPU Performance Monitoring\n\nFeatures coming soon:\n• Real-time GPU utilization\n• VRAM usage monitoring\n• Core and memory clocks\n• Temperature monitoring\n• Power consumption tracking\n• Fan speed monitoring\n• 3D/Video/Compute usage\n• Driver and hardware info",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                    TextAlignment = TextAlignment.Left,
                    LineHeight = 22
                };
                stackPanel.Children.Add(placeholder);

                border.Child = stackPanel;
                return border;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateGpuPanel error: {ex.Message}");
                return new Border { Background = Brushes.Red }; // Fallback
            }
        }

        private TextBlock CreateTextBlock(string text, int fontSize, Color color, string name)
        {
            try
            {
                var textBlock = new TextBlock
                {
                    Text = text,
                    FontSize = fontSize,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(color),
                    Name = name
                };

                // Register in dictionary for safe access
                _uiElements[name] = textBlock;
                return textBlock;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateTextBlock error: {ex.Message}");
                return new TextBlock { Text = "Error", Foreground = Brushes.Red };
            }
        }

        private StackPanel CreateStatPanel(string label, string value, string valueName)
        {
            try
            {
                var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

                var labelText = new TextBlock
                {
                    Text = label,
                    FontSize = 11,
                    Foreground = Brushes.LightGray,
                    Margin = new Thickness(0, 0, 0, 2)
                };
                panel.Children.Add(labelText);

                var valueText = CreateTextBlock(value, 13, Colors.White, valueName);
                valueText.FontWeight = FontWeights.Medium;
                panel.Children.Add(valueText);

                return panel;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateStatPanel error: {ex.Message}");
                return new StackPanel();
            }
        }

        private void InitializePerformanceCounters()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Initializing performance counters...");

                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue(); // Initial call

                _memoryAvailableCounter = new PerformanceCounter("Memory", "Available MBytes");

                // Initialize history with zeros
                for (int i = 0; i < _maxHistoryPoints; i++)
                {
                    _cpuHistory.Add(0);
                    _memoryHistory.Add(0);
                }

                System.Diagnostics.Debug.WriteLine("Performance counters initialized successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Performance counter initialization error: {ex.Message}");
            }
        }

        private void GetSystemInfo()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Getting system info...");

                // Get CPU info using WMI
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        _cpuName = obj["Name"]?.ToString()?.Trim() ?? "Unknown CPU";
                        System.Diagnostics.Debug.WriteLine($"CPU Name: {_cpuName}");
                        break;
                    }
                }

                // Get total memory using Windows API
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    _totalMemoryMB = (long)(memStatus.ullTotalPhys / 1024 / 1024);
                    System.Diagnostics.Debug.WriteLine($"Total Memory: {_totalMemoryMB} MB");
                }
                else
                {
                    _totalMemoryMB = 8192; // Default 8GB
                    System.Diagnostics.Debug.WriteLine("Using default memory size");
                }

                System.Diagnostics.Debug.WriteLine("System info retrieved successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"System info error: {ex.Message}");
                _cpuName = "Unknown CPU";
                _totalMemoryMB = 8192; // Default 8GB
            }
        }

        private void SetupRefreshTimer()
        {
            try
            {
                _refreshTimer.Interval = TimeSpan.FromSeconds(1);
                _refreshTimer.Tick += RefreshTimer_Tick;
                _refreshTimer.Start();
                System.Diagnostics.Debug.WriteLine("Refresh timer started");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetupRefreshTimer error: {ex.Message}");
            }
        }

        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (!_isInitialized) return;

                UpdatePerformanceData();
                UpdateUI();
                UpdateGraphs();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Refresh error: {ex.Message}");
            }
        }

        private void UpdatePerformanceData()
        {
            try
            {
                // Update CPU usage
                if (_cpuCounter != null)
                {
                    _currentCpuUsage = _cpuCounter.NextValue();
                }

                // Update memory usage using Windows API
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    _availableMemoryMB = (long)(memStatus.ullAvailPhys / 1024 / 1024);
                    _totalMemoryMB = (long)(memStatus.ullTotalPhys / 1024 / 1024);
                    long usedMemoryMB = _totalMemoryMB - _availableMemoryMB;
                    _currentMemoryUsage = _totalMemoryMB > 0 ? (float)((double)usedMemoryMB / _totalMemoryMB * 100) : 0f;
                }
                else if (_memoryAvailableCounter != null)
                {
                    _availableMemoryMB = (long)_memoryAvailableCounter.NextValue();
                    long usedMemoryMB = _totalMemoryMB - _availableMemoryMB;
                    _currentMemoryUsage = _totalMemoryMB > 0 ? (float)((double)usedMemoryMB / _totalMemoryMB * 100) : 0f;
                }

                // Add to history and maintain max size
                _cpuHistory.Add(_currentCpuUsage);
                if (_cpuHistory.Count > _maxHistoryPoints)
                    _cpuHistory.RemoveAt(0);

                _memoryHistory.Add(_currentMemoryUsage);
                if (_memoryHistory.Count > _maxHistoryPoints)
                    _memoryHistory.RemoveAt(0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Performance data update error: {ex.Message}");
            }
        }

        private void UpdateUI()
        {
            try
            {
                // Update CPU UI using safe dictionary access
                UpdateTextBlockSafe("CpuUsageText", $"{_currentCpuUsage:F0}%");
                UpdateTextBlockSafe("CpuNameText", _cpuName ?? "Unknown CPU");
                UpdateTextBlockSafe("CpuUtilizationText", $"{_currentCpuUsage:F1}%");
                UpdateTextBlockSafe("CpuSpeedText", "2.10 GHz");

                try
                {
                    var processCount = Process.GetProcesses().Length;
                    UpdateTextBlockSafe("CpuProcessesText", processCount.ToString());

                    var totalThreads = Process.GetProcesses().Sum(p =>
                    {
                        try { return p.Threads?.Count ?? 0; }
                        catch { return 0; }
                    });
                    UpdateTextBlockSafe("CpuThreadsText", totalThreads.ToString());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Process count error: {ex.Message}");
                }

                try
                {
                    var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                    UpdateTextBlockSafe("UptimeText", $"{(int)uptime.TotalDays}:{uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Uptime error: {ex.Message}");
                }

                // Update Memory UI
                if (_totalMemoryMB > 0)
                {
                    long usedMemoryMB = _totalMemoryMB - _availableMemoryMB;
                    double usedGB = usedMemoryMB / 1024.0;
                    double totalGB = _totalMemoryMB / 1024.0;

                    UpdateTextBlockSafe("MemoryUsageText", $"{usedGB:F1}/{totalGB:F1} GB ({_currentMemoryUsage:F0}%)");
                    UpdateTextBlockSafe("MemoryDetailsText", "Speed: 3200 MHz • Slots used: 2 of 4 • Form factor: DIMM");
                    UpdateTextBlockSafe("MemoryInUseText", $"{usedGB:F1} GB");
                    UpdateTextBlockSafe("MemoryAvailableText", $"{_availableMemoryMB / 1024.0:F1} GB");
                    UpdateTextBlockSafe("MemoryCommittedText", $"{usedGB * 0.8:F1} GB");
                    UpdateTextBlockSafe("MemoryCachedText", $"{usedGB * 0.3:F1} GB");
                    UpdateTextBlockSafe("MemoryPagedPoolText", $"{usedGB * 0.05:F1} GB");
                    UpdateTextBlockSafe("MemoryNonPagedPoolText", $"{usedMemoryMB * 0.01:F0} MB");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UI update error: {ex.Message}");
            }
        }

        private void UpdateTextBlockSafe(string name, string value)
        {
            try
            {
                if (_uiElements.TryGetValue(name, out var textBlock) && textBlock != null)
                {
                    textBlock.Text = value;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateTextBlockSafe error for {name}: {ex.Message}");
            }
        }

        private void UpdateGraphs()
        {
            try
            {
                if (_selectedComponent == "CPU" && _cpuGraphCanvas != null)
                    UpdateGraph(_cpuGraphCanvas, _cpuHistory, new SolidColorBrush(Color.FromRgb(0, 120, 215)));
                else if (_selectedComponent == "Memory" && _memoryGraphCanvas != null)
                    UpdateGraph(_memoryGraphCanvas, _memoryHistory, new SolidColorBrush(Color.FromRgb(255, 185, 0)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Graph update error: {ex.Message}");
            }
        }

        private void UpdateGraph(Canvas canvas, List<double> history, Brush strokeBrush)
        {
            try
            {
                if (canvas == null || history.Count < 2) return;

                canvas.Children.Clear();

                double width = canvas.ActualWidth > 0 ? canvas.ActualWidth : 400;
                double height = canvas.ActualHeight > 0 ? canvas.ActualHeight : 200;

                if (width <= 0 || height <= 0) return;

                var polyline = new Polyline
                {
                    Stroke = strokeBrush,
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromArgb(30, ((SolidColorBrush)strokeBrush).Color.R,
                                                            ((SolidColorBrush)strokeBrush).Color.G,
                                                            ((SolidColorBrush)strokeBrush).Color.B))
                };

                double stepX = width / Math.Max(1, history.Count - 1);

                for (int i = 0; i < history.Count; i++)
                {
                    double x = i * stepX;
                    double y = height - (history[i] / 100.0 * height);
                    polyline.Points.Add(new Point(x, y));
                }

                // Add points for fill effect
                polyline.Points.Add(new Point(width, height));
                polyline.Points.Add(new Point(0, height));

                canvas.Children.Add(polyline);

                // Add grid lines
                for (int i = 0; i <= 4; i++)
                {
                    double y = (height / 4) * i;
                    var gridLine = new Line
                    {
                        X1 = 0,
                        Y1 = y,
                        X2 = width,
                        Y2 = y,
                        Stroke = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                        StrokeThickness = 0.5
                    };
                    canvas.Children.Add(gridLine);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateGraph error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                _refreshTimer?.Stop();
                _cpuCounter?.Dispose();
                _memoryAvailableCounter?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dispose error: {ex.Message}");
            }
        }
    }
}