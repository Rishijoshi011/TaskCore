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
        partial void OnSearchQueryChanged(string value) => DebouncedApplyFilters();
        partial void OnMemoryFilterChanged(double value) => DebouncedApplyFilters();

        [ObservableProperty]
        private string searchQuery = string.Empty;

        [ObservableProperty]
        private double memoryFilter = 0;

        [ObservableProperty]
        private ProcessItem? selectedProcess;

        [ObservableProperty]
        private ProcessGroup? selectedGroup;

        [RelayCommand]
        private void SelectProcess(ProcessItem? item)
        {
            if (item == null) return;
            SelectedProcess = item;
            SelectedGroup = null; // Clear group selection when individual process selected
        }

        [RelayCommand]
        private void SelectGroup(ProcessGroup? group)
        {
            if (group == null) return;
            SelectedGroup = group;
            SelectedProcess = null; // Clear individual selection
            group.IsExpanded = !group.IsExpanded; // Toggle expand/collapse
        }

        partial void OnSelectedProcessChanged(ProcessItem? value)
        {
            EndTaskCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedGroupChanged(ProcessGroup? value)
        {
            EndTaskCommand.NotifyCanExecuteChanged();
        }

        // 🔥 NEW: LOADING SPINNER BINDING
        [ObservableProperty]
        public bool isLoading = false;

        private bool _hasLoadedOnce = false;

        // Prevent overlapping refreshes
        private bool _isRefreshing = false;

        private CancellationTokenSource? _filterCts;

        public ProcessesViewModel()
        {
            if (IsInDesignMode())
                return;

            // Load instantly without freezing
            _ = LoadProcessListAsync();

            // Setup auto-refresh timer
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            _timer.Tick += async (s, e) => await LoadProcessListAsync();
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
                if (viewName == "Processes")
                    _timer.Start();
                else
                    _timer.Stop();
            }
            catch
            {
            }
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
            if (!_hasLoadedOnce || ProcessGroups.Count == 0)
                IsLoading = true;

            try
            {
                var groups = await Task.Run(() => _service.GetGroupedProcesses());
                _allGroups = groups;
                ApplyFilters();
                _hasLoadedOnce = true;
            }
            finally
            {
                IsLoading = false;
                _isRefreshing = false;
            }
        }

        private void OnRefreshIntervalChanged(int ms)
        {
            try
            {
                var normalized = RefreshIntervalService.Normalize(ms);
                if (normalized < 2000) normalized = 2000;
                _timer.Interval = TimeSpan.FromMilliseconds(normalized);
            }
            catch
            {
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
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                ProcessGroups.Clear();
                foreach (var grp in filtered)
                    ProcessGroups.Add(grp);
            }));

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

        private void DebouncedApplyFilters()
        {
            _filterCts?.Cancel();
            _filterCts = new CancellationTokenSource();
            var token = _filterCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(300, token);
                    if (!token.IsCancellationRequested)
                        ApplyFilters();
                }
                catch { /* ignored */ }
            }, token);
        }


        [RelayCommand]
        private void EndTask()
        {
            // Handle group selection - kill all processes in group
            if (SelectedGroup != null)
            {
                System.Diagnostics.Debug.WriteLine($"[EndTask] Killing all processes in group: {SelectedGroup.GroupName} ({SelectedGroup.Items.Count} processes)");
                int killed = 0;
                foreach (var item in SelectedGroup.Items)
                {
                    try
                    {
                        var proc = System.Diagnostics.Process.GetProcessById(item.PID);
                        if (!proc.HasExited)
                        {
                            proc.Kill();
                            killed++;
                        }
                    }
                    catch { /* ignore failures */ }
                }
                System.Diagnostics.Debug.WriteLine($"[EndTask] Killed {killed}/{SelectedGroup.Items.Count} processes");
                SelectedGroup = null;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    Application.Current.Dispatcher.BeginInvoke(() => _ = LoadProcessListAsync());
                });
                return;
            }

            // Handle individual process selection
            if (SelectedProcess == null)
            {
                System.Diagnostics.Debug.WriteLine("[EndTask] No process or group selected");
                return;
            }

            var pid = SelectedProcess.PID;
            System.Diagnostics.Debug.WriteLine($"[EndTask] Attempting to kill PID {pid} - {SelectedProcess.Name}");

            try
            {
                var proc = System.Diagnostics.Process.GetProcessById(pid);
                if (proc.HasExited)
                {
                    System.Diagnostics.Debug.WriteLine($"[EndTask] Process {pid} already exited");
                    return;
                }

                proc.Kill();
                System.Diagnostics.Debug.WriteLine($"[EndTask] Successfully killed PID {pid}");

                SelectedProcess = null;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    Application.Current.Dispatcher.BeginInvoke(() => _ = LoadProcessListAsync());
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EndTask] Failed to kill PID {pid}: {ex.Message}");
            }
        }

        private bool CanEndTask() => SelectedProcess != null || SelectedGroup != null;
    }
}
