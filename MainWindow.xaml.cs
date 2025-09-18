using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using TaskCore.Views; 

namespace TaskCore
{
    public class ProcessInfo : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public int Id { get; set; }
        public float Memory { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly ObservableCollection<ProcessInfo> _processes = new();

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

        public ObservableCollection<ProcessInfo> Processes => _processes;

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
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            LoadProcesses();

            CurrentPanelView = new ProcessListPanel();
        }

        public void LoadProcesses()
        {
            _processes.Clear();
            var processList = Process.GetProcesses()
                .Select(p => new ProcessInfo
                {
                    Name = p.ProcessName,
                    Id = p.Id,
                    Memory = p.WorkingSet64 / 1024f / 1024f
                })
                .OrderBy(p => p.Name)
                .ToList();

            foreach (var p in processList)
            {
                _processes.Add(p);
            }
        }

        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            IsSidebarOpen = !IsSidebarOpen;
        }

        public void EndTaskButton_Click(object sender, RoutedEventArgs e)
        {
            // Get the current ProcessListPanel
            if (CurrentPanelView is TaskCore.Views.ProcessListPanel processListPanel)
            {
                // Get the selected process from the TreeView in the panel
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
                        MessageBox.Show($"Unable to end process: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
    }
}

namespace TaskCore.Views
{
    //public class ProcessListPanel : UserControl
    //{
        
    //}

    public class SystemGraphsPanel : UserControl
    {
        public SystemGraphsPanel()
        {
            Content = new TextBlock
            {
                Text = "This is graph panel",
                FontSize = 24,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.White
            };
        }
    }
}