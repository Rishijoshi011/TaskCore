using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace TaskCore.Views
{
    public class BlockedProcess : INotifyPropertyChanged
    {
        private string _processName;
        public string ProcessName
        {
            get => _processName;
            set
            {
                if (_processName != value)
                {
                    _processName = value;
                    OnPropertyChanged(nameof(ProcessName));
                }
            }
        }

        private DateTime _dateAdded;
        public DateTime DateAdded
        {
            get => _dateAdded;
            set
            {
                if (_dateAdded != value)
                {
                    _dateAdded = value;
                    OnPropertyChanged(nameof(DateAdded));
                    OnPropertyChanged(nameof(DateAddedString));
                }
            }
        }

        public string DateAddedString => _dateAdded.ToString("MM-dd HH:mm");

        private int _killCount;
        public int KillCount
        {
            get => _killCount;
            set
            {
                if (_killCount != value)
                {
                    _killCount = value;
                    OnPropertyChanged(nameof(KillCount));
                }
            }
        }

        private DateTime _lastKilled;
        public DateTime LastKilled
        {
            get => _lastKilled;
            set
            {
                if (_lastKilled != value)
                {
                    _lastKilled = value;
                    OnPropertyChanged(nameof(LastKilled));
                    OnPropertyChanged(nameof(LastKilledString));
                }
            }
        }

        public string LastKilledString => _lastKilled == DateTime.MinValue ? "Never" : _lastKilled.ToString("HH:mm:ss");

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class WatchDogPanel : UserControl, INotifyPropertyChanged
    {
        // Collections
        private readonly ObservableCollection<BlockedProcess> _blockedProcesses = new();
        public ObservableCollection<BlockedProcess> BlockedProcesses => _blockedProcesses;

        private readonly ObservableCollection<string> _activityLog = new();
        public ObservableCollection<string> ActivityLog => _activityLog;

        // Watchdog monitoring
        private readonly DispatcherTimer _watchdogTimer = new();
        private bool _isWatchdogActive = false;
        private readonly object _lockObject = new object();

        // UI Elements
        private TextBox _processNameTextBox;
        private Button _addProcessButton;
        private Button _browseButton;
        private DataGrid _blockedProcessesGrid;
        private ListBox _activityLogListBox;
        private CheckBox _enableWatchdogCheckBox;
        private TextBlock _statusText;
        private TextBlock _statsText;

        // Statistics
        private int _totalKills = 0;
        private int _sessionKills = 0;
        private DateTime _sessionStartTime;

        // Settings file path
        private readonly string _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskCore", "watchdog_settings.json");

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public WatchDogPanel()
        {
            _sessionStartTime = DateTime.Now;
            InitializeComponent();
            LoadSettings();
            SetupWatchdogTimer();
            LogActivity("🛡️ WatchDog Panel initialized");
        }

        private void InitializeComponent()
        {
            try
            {
                var mainGrid = new Grid
                {
                    Background = new SolidColorBrush(Color.FromRgb(32, 32, 32)),
                    Margin = new Thickness(25)
                };

                // Define rows with better spacing
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(140) }); // Header & Controls
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Main Content
                mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) }); // Activity Log

                // Header Panel
                var headerPanel = CreateHeaderPanel();
                Grid.SetRow(headerPanel, 0);
                mainGrid.Children.Add(headerPanel);

                // Main Content (Blocked Processes Grid)
                var mainContentPanel = CreateMainContentPanel();
                Grid.SetRow(mainContentPanel, 1);
                mainGrid.Children.Add(mainContentPanel);

                // Activity Log Panel
                var activityPanel = CreateActivityLogPanel();
                Grid.SetRow(activityPanel, 2);
                mainGrid.Children.Add(activityPanel);

                Content = mainGrid;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InitializeComponent error: {ex.Message}");
            }
        }

        private Border CreateHeaderPanel()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(25),
                Margin = new Thickness(0, 0, 0, 15)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

            // Left side - Controls
            var leftPanel = new StackPanel();

            // Title with better spacing
            var title = new TextBlock
            {
                Text = "🛡️ Process WatchDog - Personal Firewall",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            leftPanel.Children.Add(title);

            // Add Process Panel with better spacing
            var addPanel = new StackPanel { Orientation = Orientation.Horizontal };

            _processNameTextBox = new TextBox
            {
                Width = 220,
                Height = 30,
                Margin = new Thickness(0, 0, 15, 0),
                Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                BorderThickness = new Thickness(1),
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0, 0, 0, 10)
            };
            _processNameTextBox.KeyDown += ProcessNameTextBox_KeyDown;
            addPanel.Children.Add(_processNameTextBox);

            _addProcessButton = new Button
            {
                Content = "🚫 Block Process",
                Width = 130,
                Height = 35,
                Margin = new Thickness(0, 0, 15, 0),
                Background = new SolidColorBrush(Color.FromRgb(220, 50, 50)),
                Foreground = Brushes.White,
                BorderBrush = Brushes.Transparent,
                FontWeight = FontWeights.Medium,
                FontSize = 12
            };
            _addProcessButton.Click += AddProcessButton_Click;
            addPanel.Children.Add(_addProcessButton);

            _browseButton = new Button
            {
                Content = "📁 Browse",
                Width = 90,
                Height = 35,
                Background = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                Foreground = Brushes.White,
                BorderBrush = Brushes.Transparent,
                FontWeight = FontWeights.Medium,
                FontSize = 12
            };
            _browseButton.Click += BrowseButton_Click;
            addPanel.Children.Add(_browseButton);

            leftPanel.Children.Add(addPanel);
            Grid.SetColumn(leftPanel, 0);
            grid.Children.Add(leftPanel);

            // Right side - Status & Controls with better layout
            var rightPanel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };

            // Enable/Disable Watchdog
            _enableWatchdogCheckBox = new CheckBox
            {
                Content = "🔴 WatchDog Active",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                Margin = new Thickness(0, 0, 0, 15),
                IsChecked = false
            };
            _enableWatchdogCheckBox.Checked += EnableWatchdog_Checked;
            _enableWatchdogCheckBox.Unchecked += EnableWatchdog_Unchecked;
            rightPanel.Children.Add(_enableWatchdogCheckBox);

            // Status
            _statusText = new TextBlock
            {
                Text = "Status: 🔴 Disabled",
                FontSize = 13,
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 0, 0, 8)
            };
            rightPanel.Children.Add(_statusText);

            // Stats
            _statsText = new TextBlock
            {
                Text = "Session Kills: 0\nTotal Kills: 0",
                FontSize = 12,
                Foreground = Brushes.LightGray,
                LineHeight = 18
            };
            rightPanel.Children.Add(_statsText);

            Grid.SetColumn(rightPanel, 1);
            grid.Children.Add(rightPanel);

            border.Child = grid;
            return border;
        }

        private Border CreateMainContentPanel()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 15)
            };

            var stackPanel = new StackPanel();

            // Grid Header
            var headerText = new TextBlock
            {
                Text = "🚫 Blocked Processes",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 20)
            };
            stackPanel.Children.Add(headerText);

            // DataGrid for blocked processes with better column sizing
            _blockedProcessesGrid = new DataGrid
            {
                Background = new SolidColorBrush(Color.FromRgb(35, 35, 35)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(1),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                RowBackground = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                HeadersVisibility = DataGridHeadersVisibility.Column,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                SelectionMode = DataGridSelectionMode.Single,
                ItemsSource = _blockedProcesses,
                MinRowHeight = 35,
                RowHeight = 40,
                FontSize = 12
            };

            // Define columns with proper widths
            _blockedProcessesGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Process Name",
                Binding = new System.Windows.Data.Binding("ProcessName"),
                Width = new DataGridLength(200, DataGridLengthUnitType.Pixel),
                MinWidth = 150
            });

            _blockedProcessesGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Date Added",
                Binding = new System.Windows.Data.Binding("DateAddedString"),
                Width = new DataGridLength(120, DataGridLengthUnitType.Pixel),
                MinWidth = 100
            });

            _blockedProcessesGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Kill Count",
                Binding = new System.Windows.Data.Binding("KillCount"),
                Width = new DataGridLength(100, DataGridLengthUnitType.Pixel),
                MinWidth = 80
            });

            _blockedProcessesGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Last Killed",
                Binding = new System.Windows.Data.Binding("LastKilledString"),
                Width = new DataGridLength(100, DataGridLengthUnitType.Pixel),
                MinWidth = 90
            });

            // Remove button column with better sizing
            var removeColumn = new DataGridTemplateColumn
            {
                Header = "Action",
                Width = new DataGridLength(70, DataGridLengthUnitType.Pixel),
                MinWidth = 60
            };

            var removeButtonTemplate = new DataTemplate();
            var removeButtonFactory = new FrameworkElementFactory(typeof(Button));
            removeButtonFactory.SetValue(Button.ContentProperty, "❌");
            removeButtonFactory.SetValue(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(180, 50, 50)));
            removeButtonFactory.SetValue(Button.ForegroundProperty, Brushes.White);
            removeButtonFactory.SetValue(Button.BorderBrushProperty, Brushes.Transparent);
            removeButtonFactory.SetValue(Button.WidthProperty, 35.0);
            removeButtonFactory.SetValue(Button.HeightProperty, 30.0);
            removeButtonFactory.SetValue(Button.FontSizeProperty, 12.0);
            removeButtonFactory.SetValue(Button.FontWeightProperty, FontWeights.Bold);
            removeButtonFactory.AddHandler(Button.ClickEvent, new RoutedEventHandler(RemoveProcessButton_Click));

            removeButtonTemplate.VisualTree = removeButtonFactory;
            removeColumn.CellTemplate = removeButtonTemplate;
            _blockedProcessesGrid.Columns.Add(removeColumn);

            // Style the DataGrid headers
            var headerStyle = new Style(typeof(DataGridColumnHeader));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BackgroundProperty, new SolidColorBrush(Color.FromRgb(50, 50, 50))));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.ForegroundProperty, Brushes.White));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.FontWeightProperty, FontWeights.Bold));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.PaddingProperty, new Thickness(10)));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(60, 60, 60))));
            headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            _blockedProcessesGrid.ColumnHeaderStyle = headerStyle;

            stackPanel.Children.Add(_blockedProcessesGrid);

            border.Child = stackPanel;
            return border;
        }

        private Border CreateActivityLogPanel()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20)
            };

            var stackPanel = new StackPanel();

            // Log Header
            var headerText = new TextBlock
            {
                Text = "📝 Activity Log",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 15)
            };
            stackPanel.Children.Add(headerText);

            // Activity Log ListBox with better styling
            _activityLogListBox = new ListBox
            {
                Background = new SolidColorBrush(Color.FromRgb(25, 25, 25)),
                Foreground = Brushes.LightGray,
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                ItemsSource = _activityLog,
                Height = 120,
                Padding = new Thickness(10)
            };

            // Style for ListBox items
            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, Brushes.Transparent));
            itemStyle.Setters.Add(new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(2)));
            itemStyle.Setters.Add(new Setter(ListBoxItem.MarginProperty, new Thickness(01)));
            _activityLogListBox.ItemContainerStyle = itemStyle;

            var scrollViewer = new ScrollViewer
            {
                Content = _activityLogListBox,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            stackPanel.Children.Add(scrollViewer);

            border.Child = stackPanel;
            return border;
        }

        private void ProcessNameTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                AddProcess();
            }
        }

        private void AddProcessButton_Click(object sender, RoutedEventArgs e)
        {
            AddProcess();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "Select Executable to Block",
                    Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                    DefaultExt = "exe"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var fileName = Path.GetFileNameWithoutExtension(openFileDialog.FileName);
                    _processNameTextBox.Text = fileName;
                    AddProcess();
                }
            }
            catch (Exception ex)
            {
                LogActivity($"❌ Browse error: {ex.Message}");
            }
        }

        private void AddProcess()
        {
            try
            {
                var processName = _processNameTextBox.Text?.Trim();
                if (string.IsNullOrEmpty(processName))
                {
                    MessageBox.Show("Please enter a process name to block.", "Invalid Input",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Remove .exe extension if present
                if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    processName = processName.Substring(0, processName.Length - 4);

                // Check if already exists
                if (_blockedProcesses.Any(bp => string.Equals(bp.ProcessName, processName, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show($"Process '{processName}' is already blocked.", "Already Blocked",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var blockedProcess = new BlockedProcess
                {
                    ProcessName = processName,
                    DateAdded = DateTime.Now,
                    KillCount = 0,
                    LastKilled = DateTime.MinValue
                };

                _blockedProcesses.Add(blockedProcess);
                _processNameTextBox.Clear();
                SaveSettings();

                LogActivity($"🚫 Added '{processName}' to blocked list");

                // Kill existing instances immediately
                KillExistingInstances(processName);
            }
            catch (Exception ex)
            {
                LogActivity($"❌ Add process error: {ex.Message}");
            }
        }

        private void RemoveProcessButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                var blockedProcess = button?.DataContext as BlockedProcess;

                if (blockedProcess != null)
                {
                    var result = MessageBox.Show($"Remove '{blockedProcess.ProcessName}' from blocked list?",
                        "Confirm Removal", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        _blockedProcesses.Remove(blockedProcess);
                        SaveSettings();
                        LogActivity($"✅ Removed '{blockedProcess.ProcessName}' from blocked list");
                    }
                }
            }
            catch (Exception ex)
            {
                LogActivity($"❌ Remove process error: {ex.Message}");
            }
        }

        private void EnableWatchdog_Checked(object sender, RoutedEventArgs e)
        {
            StartWatchdog();
        }

        private void EnableWatchdog_Unchecked(object sender, RoutedEventArgs e)
        {
            StopWatchdog();
        }

        private void StartWatchdog()
        {
            try
            {
                _isWatchdogActive = true;
                _watchdogTimer.Start();

                _statusText.Text = "Status: 🟢 Active - Monitoring";
                _enableWatchdogCheckBox.Content = "🟢 WatchDog Active";
                _enableWatchdogCheckBox.Foreground = new SolidColorBrush(Color.FromRgb(100, 255, 100));

                LogActivity("🟢 WatchDog activated - Real-time monitoring started");
            }
            catch (Exception ex)
            {
                LogActivity($"❌ Start watchdog error: {ex.Message}");
            }
        }

        private void StopWatchdog()
        {
            try
            {
                _isWatchdogActive = false;
                _watchdogTimer.Stop();

                _statusText.Text = "Status: 🔴 Disabled";
                _enableWatchdogCheckBox.Content = "🔴 WatchDog Disabled";
                _enableWatchdogCheckBox.Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100));

                LogActivity("🔴 WatchDog deactivated - Monitoring stopped");
            }
            catch (Exception ex)
            {
                LogActivity($"❌ Stop watchdog error: {ex.Message}");
            }
        }

        private void SetupWatchdogTimer()
        {
            _watchdogTimer.Interval = TimeSpan.FromSeconds(1); // Check every second
            _watchdogTimer.Tick += WatchdogTimer_Tick;
        }

        private void WatchdogTimer_Tick(object sender, EventArgs e)
        {
            if (!_isWatchdogActive || _blockedProcesses.Count == 0) return;

            try
            {
                lock (_lockObject)
                {
                    var runningProcesses = Process.GetProcesses();
                    var killedAny = false;

                    foreach (var process in runningProcesses)
                    {
                        try
                        {
                            var blockedProcess = _blockedProcesses.FirstOrDefault(bp =>
                                string.Equals(bp.ProcessName, process.ProcessName, StringComparison.OrdinalIgnoreCase));

                            if (blockedProcess != null)
                            {
                                process.Kill();
                                process.WaitForExit(1000);

                                // Update statistics
                                blockedProcess.KillCount++;
                                blockedProcess.LastKilled = DateTime.Now;
                                _sessionKills++;
                                _totalKills++;
                                killedAny = true;

                                LogActivity($"⚡ KILLED: {process.ProcessName} (PID: {process.Id}) - Kill #{blockedProcess.KillCount}");
                            }
                        }
                        catch (Exception ex)
                        {
                            // Process might have already exited
                            System.Diagnostics.Debug.WriteLine($"Kill process error: {ex.Message}");
                        }
                    }

                    if (killedAny)
                    {
                        UpdateStatistics();
                        SaveSettings();
                    }
                }
            }
            catch (Exception ex)
            {
                LogActivity($"❌ Watchdog tick error: {ex.Message}");
            }
        }

        private void KillExistingInstances(string processName)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(1000);
                        LogActivity($"⚡ Immediately killed existing instance: {processName} (PID: {process.Id})");
                    }
                    catch (Exception ex)
                    {
                        LogActivity($"❌ Failed to kill existing instance: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogActivity($"❌ Kill existing instances error: {ex.Message}");
            }
        }

        private void UpdateStatistics()
        {
            try
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    _statsText.Text = $"Session Kills: {_sessionKills}\nTotal Kills: {_totalKills}";
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update statistics error: {ex.Message}");
            }
        }

        private void LogActivity(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var logEntry = $"[{timestamp}] {message}";

                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    _activityLog.Insert(0, logEntry);

                    // Keep only last 100 entries
                    while (_activityLog.Count > 100)
                        _activityLog.RemoveAt(_activityLog.Count - 1);
                });

                System.Diagnostics.Debug.WriteLine(logEntry);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Log activity error: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new
                {
                    BlockedProcesses = _blockedProcesses.Select(bp => new
                    {
                        bp.ProcessName,
                        bp.DateAdded,
                        bp.KillCount,
                        bp.LastKilled
                    }).ToList(),
                    TotalKills = _totalKills,
                    IsWatchdogActive = _isWatchdogActive
                };

                var directory = Path.GetDirectoryName(_settingsPath);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                LogActivity($"❌ Save settings error: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return;

                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<JsonElement>(json);

                if (settings.TryGetProperty("BlockedProcesses", out var blockedProcessesJson))
                {
                    foreach (var item in blockedProcessesJson.EnumerateArray())
                    {
                        var blockedProcess = new BlockedProcess
                        {
                            ProcessName = item.GetProperty("ProcessName").GetString(),
                            DateAdded = item.GetProperty("DateAdded").GetDateTime(),
                            KillCount = item.GetProperty("KillCount").GetInt32(),
                            LastKilled = item.GetProperty("LastKilled").GetDateTime()
                        };
                        _blockedProcesses.Add(blockedProcess);
                    }
                }

                if (settings.TryGetProperty("TotalKills", out var totalKillsJson))
                    _totalKills = totalKillsJson.GetInt32();

                if (settings.TryGetProperty("IsWatchdogActive", out var isActiveJson) && isActiveJson.GetBoolean())
                {
                    _enableWatchdogCheckBox.IsChecked = true;
                }

                UpdateStatistics();
                LogActivity($"📂 Loaded settings: {_blockedProcesses.Count} blocked processes");
            }
            catch (Exception ex)
            {
                LogActivity($"❌ Load settings error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try
            {
                StopWatchdog();
                SaveSettings();
                _watchdogTimer?.Stop();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dispose error: {ex.Message}");
            }
        }
    }
}