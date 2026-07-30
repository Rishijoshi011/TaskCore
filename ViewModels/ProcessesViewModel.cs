using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskCore.Helpers;
using TaskCore.Models;
using TaskCore.Services;

namespace TaskCore.ViewModels
{
    public partial class ProcessesViewModel : ObservableObject, IDisposable
    {
        private bool _disposed;
        private readonly ProcessService _service = new();
        private readonly DispatcherTimer _timer;

        private readonly AppSettings _settings = new();

        private enum SortField
        {
            Name,
            Cpu,
            Memory,
            Pid
        }

        // RAW UNFILTERED LIST
        private List<ProcessGroup> _allGroups = new();

        // FILTERED, UI-BOUND LIST
        [ObservableProperty]
        private BulkObservableCollection<ProcessGroup> processGroups = new();

        // FILTER PROPERTIES
        partial void OnSearchQueryChanged(string value) => DebouncedApplyFilters();
        partial void OnMemoryFilterChanged(double value) => DebouncedApplyFilters();

        [ObservableProperty]
        private string searchQuery = string.Empty;

        [ObservableProperty]
        private double memoryFilter = 0;

        private SortField _sortField = SortField.Cpu;
        private bool _sortDescending = true;

        public string NameSortIndicator => _sortField == SortField.Name ? (_sortDescending ? "▼" : "▲") : string.Empty;
        public string CpuSortIndicator => _sortField == SortField.Cpu ? (_sortDescending ? "▼" : "▲") : string.Empty;
        public string MemorySortIndicator => _sortField == SortField.Memory ? (_sortDescending ? "▼" : "▲") : string.Empty;
        public string PidSortIndicator => _sortField == SortField.Pid ? (_sortDescending ? "▼" : "▲") : string.Empty;

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

        [RelayCommand]
        private void SortBy(string? column)
        {
            if (string.IsNullOrWhiteSpace(column)) return;

            if (!Enum.TryParse<SortField>(column, ignoreCase: true, out var requested))
                return;

            if (_sortField == requested)
                _sortDescending = !_sortDescending;
            else
            {
                _sortField = requested;
                _sortDescending = true;
            }

            OnPropertyChanged(nameof(NameSortIndicator));
            OnPropertyChanged(nameof(CpuSortIndicator));
            OnPropertyChanged(nameof(MemorySortIndicator));
            OnPropertyChanged(nameof(PidSortIndicator));

            try
            {
                _settings.ProcessesSortField = _sortField.ToString();
                _settings.ProcessesSortDescending = _sortDescending;
                AppSettingsService.Save(_settings);
            }
            catch
            {
            }

            ApplyFilters();
        }

        partial void OnSelectedProcessChanged(ProcessItem? value)
        {
            EndTaskCommand.NotifyCanExecuteChanged();
        }

        partial void OnSelectedGroupChanged(ProcessGroup? value)
        {
            EndTaskCommand.NotifyCanExecuteChanged();
        }

        [ObservableProperty]
        public bool isLoading = false;

        private bool _hasLoadedOnce = false;

        private CancellationTokenSource? _filterCts;

#pragma warning disable CS8618 // Non-nullable field is initialized in constructor after design mode check
        public ProcessesViewModel()
#pragma warning restore CS8618
        {
            if (IsInDesignMode())
                return;

            var loadedSettings = AppSettingsService.Load();
            _settings.AutoStartWithWindows = loadedSettings.AutoStartWithWindows;
            _settings.PreferAdminForProtectedActions = loadedSettings.PreferAdminForProtectedActions;
            _settings.RefreshIntervalMs = loadedSettings.RefreshIntervalMs;
            _settings.MinimizeToTray = loadedSettings.MinimizeToTray;
            _settings.ProcessesSortField = loadedSettings.ProcessesSortField;
            _settings.ProcessesSortDescending = loadedSettings.ProcessesSortDescending;

            _settings.CustomAlertSoundPath = loadedSettings.CustomAlertSoundPath;
            _settings.SessionTrackerSummaryColumns = loadedSettings.SessionTrackerSummaryColumns;
            _settings.SessionTrackerSessionColumns = loadedSettings.SessionTrackerSessionColumns;
            _settings.ProcessFilterMode = loadedSettings.ProcessFilterMode;
            _settings.SelectedProcesses = loadedSettings.SelectedProcesses;

            try
            {
                if (!string.IsNullOrWhiteSpace(_settings.ProcessesSortField) &&
                    Enum.TryParse<SortField>(_settings.ProcessesSortField, ignoreCase: true, out var sf))
                {
                    _sortField = sf;
                }

                _sortDescending = _settings.ProcessesSortDescending;

                OnPropertyChanged(nameof(NameSortIndicator));
                OnPropertyChanged(nameof(CpuSortIndicator));
                OnPropertyChanged(nameof(MemorySortIndicator));
                OnPropertyChanged(nameof(PidSortIndicator));
            }
            catch
            {
            }

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

        private List<ProcessGroup> ApplySorting(IEnumerable<ProcessGroup> groups)
        {
            IEnumerable<ProcessGroup> orderedGroups = _sortField switch
            {
                SortField.Name => _sortDescending
                    ? groups.OrderByDescending(g => g.GroupName)
                    : groups.OrderBy(g => g.GroupName),
                SortField.Cpu => _sortDescending
                    ? groups.OrderByDescending(g => g.TotalCpu)
                    : groups.OrderBy(g => g.TotalCpu),
                SortField.Memory => _sortDescending
                    ? groups.OrderByDescending(g => g.TotalMemory)
                    : groups.OrderBy(g => g.TotalMemory),
                SortField.Pid => _sortDescending
                    ? groups.OrderByDescending(g => g.Items.Count > 0 ? g.Items[0].PID : 0)
                    : groups.OrderBy(g => g.Items.Count > 0 ? g.Items[0].PID : 0),
                _ => groups
            };

            var result = orderedGroups.ToList();

            foreach (var g in result)
            {
                try
                {
                    IEnumerable<ProcessItem> orderedItems = _sortField switch
                    {
                        SortField.Name => _sortDescending
                            ? g.Items.OrderByDescending(p => p.Name)
                            : g.Items.OrderBy(p => p.Name),
                        SortField.Cpu => _sortDescending
                            ? g.Items.OrderByDescending(p => p.CpuUsage)
                            : g.Items.OrderBy(p => p.CpuUsage),
                        SortField.Memory => _sortDescending
                            ? g.Items.OrderByDescending(p => p.MemoryMB)
                            : g.Items.OrderBy(p => p.MemoryMB),
                        SortField.Pid => _sortDescending
                            ? g.Items.OrderByDescending(p => p.PID)
                            : g.Items.OrderBy(p => p.PID),
                        _ => g.Items.AsEnumerable()
                    };

                    g.Items.ReplaceAll(orderedItems);
                }
                catch
                {
                }
            }

            return result;
        }

        private void OnViewChanged(string viewName)
        {
            try
            {
                if (viewName == "Processes")
                {
                    if (!_timer.IsEnabled)
                        _timer.Start();
                }
                else
                {
                    if (_timer.IsEnabled)
                        _timer.Stop();
                }
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

        private int _isRefreshingFlag;

        private async Task LoadProcessListAsync()
        {
            if (System.Threading.Interlocked.Exchange(ref _isRefreshingFlag, 1) == 1) return;

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
                System.Threading.Interlocked.Exchange(ref _isRefreshingFlag, 0);
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

        private void ApplyFilters()
        {
            // preserve old selection BEFORE filtering
            var oldSelected = SelectedProcess;

            // preserve expand state BEFORE refresh/filter
            var expandState = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var g in ProcessGroups)
                    expandState[g.GroupName] = g.IsExpanded;
            }
            catch
            {
            }

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

            // ↕ Sorting (groups + items)
            var sortedList = ApplySorting(filtered);

            // Update UI list safely using BulkObservableCollection ReplaceAll (1 Reset event)
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => UpdateGroupsUi(sortedList, expandState, oldSelected)));
            }
            else
            {
                UpdateGroupsUi(sortedList, expandState, oldSelected);
            }
        }

        private void UpdateGroupsUi(List<ProcessGroup> sortedList, Dictionary<string, bool> expandState, ProcessItem? oldSelected)
        {
            foreach (var grp in sortedList)
            {
                if (expandState.TryGetValue(grp.GroupName, out var isExpanded))
                    grp.IsExpanded = isExpanded;
            }

            ProcessGroups.ReplaceAll(sortedList);

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
                    {
                        var dispatcher = Application.Current?.Dispatcher;
                        if (dispatcher != null)
                            dispatcher.BeginInvoke(new Action(ApplyFilters));
                    }
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
                TaskCoreLogger.Info("endtask", $"Killing all processes in group: {SelectedGroup.GroupName} ({SelectedGroup.Items.Count} processes)");
                int killed = 0;
                foreach (var item in SelectedGroup.Items)
                {
                    try
                    {
                        using var proc = System.Diagnostics.Process.GetProcessById(item.PID);
                        if (!proc.HasExited)
                        {
                            proc.Kill();
                            killed++;
                        }
                    }
                    catch { /* ignore failures */ }
                }
                TaskCoreLogger.Info("endtask", $"Killed {killed}/{SelectedGroup.Items.Count} processes");
                SelectedGroup = null;
#pragma warning disable CS4014 // Intentionally fire-and-forget
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    Application.Current?.Dispatcher?.BeginInvoke(() => _ = LoadProcessListAsync());
                });
#pragma warning restore CS4014
                return;
            }

            // Handle individual process selection
            if (SelectedProcess == null)
            {
                TaskCoreLogger.Warn("endtask", "No process or group selected");
                return;
            }

            var pid = SelectedProcess.PID;
            TaskCoreLogger.Info("endtask", $"Attempting to kill PID {pid} - {SelectedProcess.Name}");

            try
            {
                using var proc = System.Diagnostics.Process.GetProcessById(pid);
                if (proc.HasExited)
                {
                    TaskCoreLogger.Warn("endtask", $"Process {pid} already exited");
                    return;
                }

                proc.Kill();
                TaskCoreLogger.Info("endtask", $"Successfully killed PID {pid}");

                SelectedProcess = null;
#pragma warning disable CS4014 // Intentionally fire-and-forget
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    Application.Current?.Dispatcher?.BeginInvoke(() => _ = LoadProcessListAsync());
                });
#pragma warning restore CS4014
            }
            catch (Exception ex)
            {
                TaskCoreLogger.Error("endtask", $"Failed to kill PID {pid}: {ex.Message}");
            }
        }

        private bool CanEndTask() => SelectedProcess != null || SelectedGroup != null;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop timer and null out
            try { _timer?.Stop(); } catch { }

            // Cancel any pending filter task
            try { _filterCts?.Cancel(); } catch { }
            try { _filterCts?.Dispose(); } catch { }

            // Unsubscribe events
            try { AppViewStateService.ViewChanged -= OnViewChanged; } catch { }
            try { RefreshIntervalService.RefreshIntervalChanged -= OnRefreshIntervalChanged; } catch { }

            GC.SuppressFinalize(this);
        }
    }
}
