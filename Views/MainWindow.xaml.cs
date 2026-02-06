using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using TaskCore.Models;
using TaskCore.Services;

namespace TaskCore
{
    public partial class MainWindow : Window
    {
        private readonly ProcessService _service = new();
        private readonly DispatcherTimer _timer;

        public ObservableCollection<ProcessInfo> Processes { get; } = new();
        public ICollectionView ProcessesView { get; }

        public ProcessInfo? SelectedProcess { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            ProcessesView = CollectionViewSource.GetDefaultView(Processes);

            // Group by Name (like Task Manager)
            ProcessesView.GroupDescriptions.Clear();
            ProcessesView.GroupDescriptions.Add(
                new PropertyGroupDescription(nameof(ProcessInfo.Name))
            );


            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += RefreshProcesses;
            _timer.Start();
        }

        private void RefreshProcesses(object? sender, EventArgs e)
        {
            var latest = _service.GetProcesses();

            // Remove dead processes
            for (int i = Processes.Count - 1; i >= 0; i--)
            {
                if (!latest.Exists(p => p.Id == Processes[i].Id))
                    Processes.RemoveAt(i);
            }

            // Add / update
            foreach (var p in latest)
            {
                var existing = Find(p.Id);
                if (existing == null)
                {
                    Processes.Add(p);
                }
                else
                {
                    existing.Memory = p.Memory;
                    existing.Cpu = p.Cpu;
                }
            }
        }

        private ProcessInfo? Find(int id)
        {
            foreach (var p in Processes)
                if (p.Id == id) return p;
            return null;
        }

        private void EndTask_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProcess == null) return;
            _service.KillProcess(SelectedProcess.Id);
        }
    }
}
