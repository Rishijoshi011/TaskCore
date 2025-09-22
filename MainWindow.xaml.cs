using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TaskCore.Views;

namespace TaskCore
{
    public class ProcessInfo : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public int Id { get; set; }
        public float Memory { get; set; }

        private float _cpu;
        public float Cpu
        {
            get => _cpu;
            set
            {
                if (Math.Abs(_cpu - value) > 0.1f)
                {
                    _cpu = value;
                    OnPropertyChanged(nameof(Cpu));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly ObservableCollection<ProcessInfo> _processes = new();
        public ObservableCollection<ProcessInfo> Processes => _processes;
        public List<ProcessInfo> _processList = new();

        private bool _isSidebarOpen = true;
        public bool IsSidebarOpen
        {
            get => _isSidebarOpen;
            set
            {
                if (_isSidebarOpen != value)
                {
                    _isSidebarOpen = value;
                    OnPropertyChanged(nameof(IsSidebarOpen));
                }
            }
        }

        private object _currentPanelView;
        public object CurrentPanelView
        {
            get => _currentPanelView;
            set
            {
                if (_currentPanelView != value)
                {
                    _currentPanelView = value;
                    OnPropertyChanged(nameof(CurrentPanelView));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            LoadProcesses();
            StartAutoRefresh();

            CurrentPanelView = new ProcessListPanel();
        }

        public void LoadProcesses()
        {
            _processes.Clear();

            _processList = Process.GetProcesses()
                .Select(p => new ProcessInfo
                {
                    Name = p.ProcessName,
                    Id = p.Id,
                    Memory = p.WorkingSet64 / 1024f / 1024f,
                    Cpu = 0
                })
                .OrderBy(p => p.Name)
                .ToList();

            foreach (var p in _processList)
            {
                _processes.Add(p);
            }
        }

        private void StartAutoRefresh()
        {
            new Thread(() =>
            {
                var cpuCounters = new Dictionary<int, PerformanceCounter>();

                while (true)
                {
                    var processes = Process.GetProcesses();
                    var infos = new List<ProcessInfo>();

                    foreach (var p in processes)
                    {
                        float cpu = 0;
                        try
                        {
                            if (!cpuCounters.ContainsKey(p.Id))
                            {
                                cpuCounters[p.Id] = new PerformanceCounter("Process", "% Processor Time", p.ProcessName, true);
                                cpuCounters[p.Id].NextValue(); // warmup
                            }
                            cpu = cpuCounters[p.Id].NextValue() / Environment.ProcessorCount;
                        }
                        catch
                        {
                            cpu = 0;
                        }

                        infos.Add(new ProcessInfo
                        {
                            Name = p.ProcessName,
                            Id = p.Id,
                            Memory = p.WorkingSet64 / 1024f / 1024f,
                            Cpu = (float)Math.Round(cpu, 1)
                        });
                    }

                    // Remove counters for exited processes
                    var currentIds = infos.Select(i => i.Id).ToHashSet();
                    var exitedIds = cpuCounters.Keys.Where(id => !currentIds.Contains(id)).ToList();
                    foreach (var id in exitedIds)
                    {
                        cpuCounters[id].Dispose();
                        cpuCounters.Remove(id);
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _processes.Clear();
                        foreach (var info in infos.OrderBy(i => i.Name))
                        {
                            _processes.Add(info);
                        }
                        _processList = infos;
                    });

                    Thread.Sleep(1000);
                }
            })
            { IsBackground = true }.Start();
        }

        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            IsSidebarOpen = !IsSidebarOpen;
        }

        public void EndTaskButton_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentPanelView is TaskCore.Views.ProcessListPanel processListPanel)
            {
                if (processListPanel.processTreeView.SelectedItem is ProcessInfo selectedProcess)
                {
                    try
                    {
                        var process = Process.GetProcessById(selectedProcess.Id);
                        process.Kill();
                        LoadProcesses();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Unable to end process: {ex.Message}",
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void ProcessListButton_Click(object sender, RoutedEventArgs e)
        {
            CurrentPanelView = new ProcessListPanel();
        }

        private void SystemGraphsButton_Click(object sender, RoutedEventArgs e)
        {
            CurrentPanelView = new SystemGraphsPanel();
        }

        private void WatchDogButton_Click(object sender, RoutedEventArgs e)
        {
            CurrentPanelView = new WatchDogPanel();
        }

    }
}

