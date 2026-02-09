using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskCore.Models;
using TaskCore.Services;

namespace TaskCore.ViewModels
{
    public partial class ProcessesViewModel : ObservableObject
    {
        private readonly ProcessService _service = new();
        private readonly DispatcherTimer _timer;

        // RAW UNFILTERED LIST
        private List<ProcessGroup> _allGroups = new();

        // FILTERED, UI-BOUND LIST
        [ObservableProperty]
        private ObservableCollection<ProcessGroup> processGroups = new();

        // FILTER PROPERTIES
        partial void OnSearchQueryChanged(string value) => ApplyFilters();
        partial void OnMemoryFilterChanged(double value) => ApplyFilters();

        [ObservableProperty]
        private string searchQuery = string.Empty;

        [ObservableProperty]
        private double memoryFilter = 0;

        [ObservableProperty]
        private ProcessItem? selectedProcess;

        // 🔥 NEW: LOADING SPINNER BINDING
        [ObservableProperty]
        public bool isLoading = false;

        // Prevent overlapping refreshes
        private bool _isRefreshing = false;

        public ProcessesViewModel()
        {
            if (IsInDesignMode())
                return;

            // Load instantly without freezing
            _ = LoadProcessListAsync();

            // Setup auto-refresh timer
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(7)
            };
            _timer.Tick += async (s, e) => await LoadProcessListAsync();
            _timer.Start();
        }

        private static bool IsInDesignMode()
        {
            return System.ComponentModel.DesignerProperties
                .GetIsInDesignMode(new DependencyObject());
        }

        // 🔥 MAIN REFRESH LOGIC (with loading + async task-run)
        private async Task LoadProcessListAsync()
        {
            if (_isRefreshing) return;

            _isRefreshing = true;
            IsLoading = true;     // show spinner

            try
            {
                var groups = await Task.Run(() => _service.GetGroupedProcesses());
                _allGroups = groups;
                ApplyFilters();
            }
            finally
            {
                IsLoading = false; // hide spinner
                _isRefreshing = false;
            }
        }

        // FILTER ONLY — does NOT call process service
        private void ApplyFilters()
        {
            // preserve old selection BEFORE filtering
            var oldSelected = SelectedProcess;

            var filtered = _allGroups.AsEnumerable();

            // 🔍 Search
            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                filtered = filtered.Where(g =>
                    g.GroupName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
            }

            // 🧠 Memory filter
            if (MemoryFilter > 0)
            {
                filtered = filtered.Where(g => g.TotalMemory >= MemoryFilter);
            }

            // Update UI list safely
            UpdateObservableCollection(ProcessGroups, filtered);

            // 🔄 Restore selected process if it still exists
            if (oldSelected != null)
            {
                foreach (var g in ProcessGroups)
                {
                    var match = g.Items.FirstOrDefault(p => p.PID == oldSelected.PID);
                    if (match != null)
                    {
                        SelectedProcess = match;
                        break;
                    }
                }
            }
        }


        // Safe UI update
        private void UpdateObservableCollection<T>(
            ObservableCollection<T> target,
            IEnumerable<T> source)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                target.Clear();
                foreach (var item in source)
                    target.Add(item);
            }));
        }

        [RelayCommand]
        private void EndTask()
        {
            var proc = SelectedProcess?.UnderlyingProcess;

            if (proc == null || proc.HasExited)
                return;

            try { proc.Kill(); }
            catch { /* access denied / system process */ }
        }
    }
}
