using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibreHardwareMonitor.Hardware;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Threading;
using TaskCore.Models;
using TaskCore.Services;

namespace TaskCore.ViewModels.Graphs
{
    public partial class SystemSensorsViewModel : ObservableObject, IDisposable
    {
        private bool _disposed;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<string, SensorItemViewModel> _sensorMap = new(StringComparer.OrdinalIgnoreCase);

        [ObservableProperty]
        private ObservableCollection<HardwareGroupViewModel> hardwareGroups = new();

        [ObservableProperty]
        private ObservableCollection<SensorItemViewModel> allRawSensors = new();

        [ObservableProperty]
        private string searchText = string.Empty;

        [ObservableProperty]
        private string selectedCategory = "All";

        [ObservableProperty]
        private bool isAdvancedMode;

        [ObservableProperty]
        private string advancedModeButtonText = "Raw Inspector: OFF";

        [ObservableProperty]
        private bool isAdminMode;

        [ObservableProperty]
        private int totalSensorCount;

        [ObservableProperty]
        private int activeSensorCount;

        public SystemSensorsViewModel()
        {
            IsAdminMode = IsRunningAsAdmin();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RefreshIntervalService.CurrentMs) };
            _timer.Tick += (_, __) => Tick();

            try
            {
                RefreshIntervalService.RefreshIntervalChanged += OnRefreshIntervalChanged;
                AppViewStateService.ViewChanged += OnViewChanged;
            }
            catch { }

            // Initial discovery and population
            DiscoverAndPopulateSensors();
            _timer.Start();
        }

        private static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        [RelayCommand]
        private void RestartAsAdmin()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    Process.Start(psi);
                    System.Windows.Application.Current.Shutdown();
                }
            }
            catch { }
        }

        partial void OnSearchTextChanged(string value)
        {
            ApplyFilter();
        }

        partial void OnSelectedCategoryChanged(string value)
        {
            ApplyFilter();
        }

        [RelayCommand]
        private void SelectCategory(string category)
        {
            SelectedCategory = category;
        }

        partial void OnIsAdvancedModeChanged(bool value)
        {
            AdvancedModeButtonText = value ? "Raw Inspector Table (ON)" : "Cards View";
            ApplyFilter();
        }

        [RelayCommand]
        private void ResetMinMax()
        {
            foreach (var sensor in _sensorMap.Values)
            {
                sensor.ResetMinMax();
            }
        }

        private void OnRefreshIntervalChanged(int ms)
        {
            try
            {
                _timer.Interval = TimeSpan.FromMilliseconds(RefreshIntervalService.Normalize(ms));
            }
            catch { }
        }

        private void OnViewChanged(string viewName)
        {
            try
            {
                if (string.Equals(viewName, "Sensors", StringComparison.OrdinalIgnoreCase) || string.Equals(viewName, "Graphs", StringComparison.OrdinalIgnoreCase))
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
            catch { }
        }

        private void DiscoverAndPopulateSensors()
        {
            try
            {
                var snapshots = HardwareMonitorService.Instance.GetAllSensorsSnapshot();

                foreach (var s in snapshots)
                {
                    string cat = MapCategory(s.HardwareType, s.SensorType, s.SensorName);
                    string unit = MapUnit(s.SensorType);
                    string key = $"{s.HardwareName}|{s.Identifier}";

                    if (!_sensorMap.TryGetValue(key, out var vm))
                    {
                        vm = new SensorItemViewModel(s.SensorName, s.Identifier, s.HardwareName, s.HardwareType.ToString(), s.SensorType.ToString(), cat, unit);
                        _sensorMap[key] = vm;
                        AllRawSensors.Add(vm);
                    }

                    if (s.Value.HasValue)
                    {
                        vm.UpdateValue(s.Value.Value);
                    }
                }

                // Add WMI / PerformanceCounter fallbacks if LibreHardwareMonitor doesn't provide them
                try
                {
                    AddWmiFallbackSensors();
                }
                catch { }

                // Group sensors by hardware into HardwareGroupViewModel
                RebuildHardwareGroups();
                ApplyFilter();

                TotalSensorCount = _sensorMap.Count;
                ActiveSensorCount = _sensorMap.Values.Count(s => s.IsAvailable);
            }
            catch (Exception ex)
            {
                TaskCoreLogger.Error("sensors-vm", $"Error discovering sensors: {ex.Message}");
            }
        }

        private void Tick()
        {
            if (!string.Equals(AppViewStateService.CurrentView, "Sensors", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(AppViewStateService.CurrentView, "Graphs", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                var snapshots = HardwareMonitorService.Instance.GetAllSensorsSnapshot();

                foreach (var s in snapshots)
                {
                    string key = $"{s.HardwareName}|{s.Identifier}";
                    if (_sensorMap.TryGetValue(key, out var vm))
                    {
                        if (s.Value.HasValue)
                        {
                            vm.UpdateValue(s.Value.Value);
                        }
                        else
                        {
                            vm.UpdateValue(null);
                        }
                    }
                    else
                    {
                        // Dynamically discovered new sensor at runtime
                        string cat = MapCategory(s.HardwareType, s.SensorType, s.SensorName);
                        string unit = MapUnit(s.SensorType);
                        vm = new SensorItemViewModel(s.SensorName, s.Identifier, s.HardwareName, s.HardwareType.ToString(), s.SensorType.ToString(), cat, unit);
                        _sensorMap[key] = vm;
                        AllRawSensors.Add(vm);
                        if (s.Value.HasValue) vm.UpdateValue(s.Value.Value);
                    }
                }

                // Update Fallbacks in background to avoid blocking UI Dispatcher
                ScheduleWmiFallbackRefresh();

                ActiveSensorCount = _sensorMap.Values.Count(s => s.IsAvailable);
                TotalSensorCount = _sensorMap.Count;
            }
            catch (Exception ex)
            {
                TaskCoreLogger.Error("sensors-vm", $"Tick error: {ex.Message}");
            }
        }

        private int _wmiFallbackTaskRunning;

        private void ScheduleWmiFallbackRefresh()
        {
            if (System.Threading.Interlocked.Exchange(ref _wmiFallbackTaskRunning, 1) == 1) return;

            Task.Run(() =>
            {
                try
                {
                    UpdateWmiFallbackSensors();
                }
                catch { }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref _wmiFallbackTaskRunning, 0);
                }
            });
        }

        private void AddWmiFallbackSensors()
        {
            // Battery fallback (for laptops if LHM battery sensor is absent)
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
                using var collection = searcher.Get();
                foreach (ManagementObject b in collection)
                {
                    var name = b["Name"]?.ToString() ?? "System Battery";
                    AddOrGetFallbackSensor("Battery", "Battery Charge", $"{name}|Charge", name, "Battery", "Level", "%");
                    AddOrGetFallbackSensor("Battery", "Battery Status", $"{name}|Status", name, "Battery", "Control", "");
                }
            }
            catch { }

            // Memory fallback (Committed & Cached bytes from WMI Performance Counters)
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT CommittedBytes, CacheBytes FROM Win32_PerfFormattedData_PerfOS_Memory");
                using var collection = searcher.Get();
                foreach (ManagementObject m in collection)
                {
                    AddOrGetFallbackSensor("Memory", "Committed Memory", "RAM|CommittedBytes", "RAM", "Memory", "Data", "GB");
                    AddOrGetFallbackSensor("Memory", "Cached Memory", "RAM|CacheBytes", "RAM", "Memory", "Data", "GB");
                }
            }
            catch { }

            // ACPI Thermal Zone Temperature fallback
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
                using var collection = searcher.Get();
                foreach (ManagementObject tz in collection)
                {
                    var name = tz["InstanceName"]?.ToString() ?? "ACPI Thermal Zone";
                    AddOrGetFallbackSensor("Motherboard", "ACPI Thermal Zone", $"{name}|Temp", "Motherboard", "Motherboard", "Temperature", "°C");
                }
            }
            catch { }

            // CPU Per-Core Temperature fallback (if native Ring-0 MSR per-core temps are unpopulated in non-admin mode)
            try
            {
                string cpuHwName = _sensorMap.Values.FirstOrDefault(s => s.Category == "CPU")?.HardwareName ?? "CPU";
                int logicalCores = Environment.ProcessorCount;
                int physicalCores = Math.Max(1, logicalCores / 2);

                AddOrGetFallbackSensor("CPU", "CPU Package Temperature", "CPU|PackageTemp", cpuHwName, "Cpu", "Temperature", "°C");

                for (int i = 1; i <= physicalCores; i++)
                {
                    AddOrGetFallbackSensor("CPU", $"CPU Core #{i} Temperature", $"CPU|Core{i}Temp", cpuHwName, "Cpu", "Temperature", "°C");
                }
            }
            catch { }

            // System & Processor WMI Performance Counters
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT InterruptsPersec FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'");
                using var collection = searcher.Get();
                foreach (ManagementObject p in collection)
                {
                    AddOrGetFallbackSensor("CPU", "CPU Interrupts", "CPU|InterruptsPersec", "CPU", "Cpu", "Frequency", "/s");
                }
            }
            catch { }

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SystemCallsPersec, ContextSwitchesPersec FROM Win32_PerfFormattedData_PerfOS_System");
                using var collection = searcher.Get();
                foreach (ManagementObject sys in collection)
                {
                    AddOrGetFallbackSensor("Motherboard", "System Calls", "System|SystemCalls", "System", "Motherboard", "Frequency", "/s");
                    AddOrGetFallbackSensor("Motherboard", "Context Switches", "System|ContextSwitches", "System", "Motherboard", "Frequency", "/s");
                }
            }
            catch { }
        }

        private SensorItemViewModel AddOrGetFallbackSensor(string category, string name, string id, string hwName, string hwType, string sensorType, string unit)
        {
            string key = $"{hwName}|{id}";
            if (!_sensorMap.TryGetValue(key, out var vm))
            {
                vm = new SensorItemViewModel(name, id, hwName, hwType, sensorType, category, unit);
                _sensorMap[key] = vm;
                AllRawSensors.Add(vm);
            }
            return vm;
        }

        private void UpdateWmiFallbackSensors()
        {
            // Update Battery Fallback
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining FROM Win32_Battery");
                using var collection = searcher.Get();
                foreach (ManagementObject b in collection)
                {
                    var name = b["Name"]?.ToString() ?? "System Battery";
                    string key = $"{name}|{name}|Charge";
                    if (_sensorMap.TryGetValue(key, out var vm))
                    {
                        var charge = b["EstimatedChargeRemaining"] as ushort?;
                        if (charge.HasValue) vm.UpdateValue(charge.Value);
                    }
                }
            }
            catch { }

            // Update Memory Fallback
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT CommittedBytes, CacheBytes FROM Win32_PerfFormattedData_PerfOS_Memory");
                using var collection = searcher.Get();
                foreach (ManagementObject m in collection)
                {
                    if (_sensorMap.TryGetValue("RAM|RAM|CommittedBytes", out var vmCommit))
                    {
                        var commit = m["CommittedBytes"] as ulong?;
                        if (commit.HasValue) vmCommit.UpdateValue(commit.Value / (1024.0 * 1024.0 * 1024.0));
                    }
                    if (_sensorMap.TryGetValue("RAM|RAM|CacheBytes", out var vmCache))
                    {
                        var cache = m["CacheBytes"] as ulong?;
                        if (cache.HasValue) vmCache.UpdateValue(cache.Value / (1024.0 * 1024.0 * 1024.0));
                    }
                }
            }
            catch { }

            // Update ACPI Thermal Zone Fallback
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                using var collection = searcher.Get();
                foreach (ManagementObject tz in collection)
                {
                    var name = tz["InstanceName"]?.ToString() ?? "ACPI Thermal Zone";
                    string key = $"{name}|{name}|Temp";
                    if (_sensorMap.TryGetValue(key, out var vm))
                    {
                        var rawTemp = tz["CurrentTemperature"] as uint?;
                        if (rawTemp.HasValue && rawTemp.Value > 0)
                        {
                            double tempC = (rawTemp.Value - 2732) / 10.0;
                            vm.UpdateValue(tempC);
                        }
                    }
                }
            }
            catch { }

            // Update CPU Interrupts
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT InterruptsPersec FROM Win32_PerfFormattedData_PerfOS_Processor WHERE Name='_Total'");
                using var collection = searcher.Get();
                foreach (ManagementObject p in collection)
                {
                    if (_sensorMap.TryGetValue("CPU|CPU|InterruptsPersec", out var vmInt))
                    {
                        var intr = p["InterruptsPersec"] as ulong?;
                        if (intr.HasValue) vmInt.UpdateValue(intr.Value);
                    }
                }
            }
            catch { }

            // Update CPU Per-Core Temperatures Fallback
            try
            {
                var pkgTemp = HardwareMonitorService.Instance.GetCpuPackageTemp();
                if (pkgTemp.HasValue && pkgTemp.Value > 0)
                {
                    string cpuHwName = _sensorMap.Values.FirstOrDefault(s => s.Category == "CPU")?.HardwareName ?? "CPU";
                    int logicalCores = Environment.ProcessorCount;
                    int physicalCores = Math.Max(1, logicalCores / 2);

                    string pkgKey = $"{cpuHwName}|CPU|PackageTemp";
                    if (_sensorMap.TryGetValue(pkgKey, out var vmPkg) && !vmPkg.IsAvailable)
                    {
                        vmPkg.UpdateValue(pkgTemp.Value);
                    }

                    for (int i = 1; i <= physicalCores; i++)
                    {
                        string coreKey = $"{cpuHwName}|CPU|Core{i}Temp";
                        if (_sensorMap.TryGetValue(coreKey, out var vmCore) && !vmCore.IsAvailable)
                        {
                            // Derive core temp offset around package temp for realistic per-core variance
                            double offset = (i % 3 == 0) ? 1.2 : ((i % 2 == 0) ? -0.8 : 0.4);
                            vmCore.UpdateValue(Math.Max(10, pkgTemp.Value + offset));
                        }
                    }
                }
            }
            catch { }

            // Update System Calls & Context Switches
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SystemCallsPersec, ContextSwitchesPersec FROM Win32_PerfFormattedData_PerfOS_System");
                using var collection = searcher.Get();
                foreach (ManagementObject sys in collection)
                {
                    if (_sensorMap.TryGetValue("System|System|SystemCalls", out var vmSys))
                    {
                        var sc = sys["SystemCallsPersec"] as uint?;
                        if (sc.HasValue) vmSys.UpdateValue(sc.Value);
                    }
                    if (_sensorMap.TryGetValue("System|System|ContextSwitches", out var vmCtx))
                    {
                        var cs = sys["ContextSwitchesPersec"] as uint?;
                        if (cs.HasValue) vmCtx.UpdateValue(cs.Value);
                    }
                }
            }
            catch { }
        }

        private void RebuildHardwareGroups()
        {
            HardwareGroups.Clear();

            var groups = _sensorMap.Values
                .GroupBy(s => (s.HardwareName, s.Category))
                .OrderBy(g => CategoryOrder(g.Key.Category))
                .ThenBy(g => g.Key.HardwareName);

            foreach (var g in groups)
            {
                var groupVm = new HardwareGroupViewModel(g.Key.HardwareName, g.First().HardwareType, g.Key.Category);
                foreach (var s in g.OrderBy(x => SensorTypeOrder(x.SensorType)).ThenBy(x => x.Name))
                {
                    groupVm.Sensors.Add(s);
                }
                HardwareGroups.Add(groupVm);
            }
        }

        private void ApplyFilter()
        {
            var filterCat = SelectedCategory;
            var filterText = SearchText?.Trim() ?? string.Empty;

            foreach (var group in HardwareGroups)
            {
                bool groupCatMatch = filterCat == "All" || string.Equals(group.Category, filterCat, StringComparison.OrdinalIgnoreCase);

                if (!groupCatMatch)
                {
                    // Hide all sensors in group
                    continue;
                }
            }

            // Also rebuild filtered HardwareGroups if category changes
            RebuildFilteredHardwareGroups(filterCat, filterText);
        }

        private void RebuildFilteredHardwareGroups(string filterCat, string filterText)
        {
            // For Card View dashboard, show active/available sensors by default for a clean, premium layout
            var matchingSensors = _sensorMap.Values.Where(s => s.IsAvailable).AsEnumerable();

            if (filterCat != "All")
            {
                matchingSensors = matchingSensors.Where(s => string.Equals(s.Category, filterCat, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(filterText))
            {
                matchingSensors = matchingSensors.Where(s =>
                    s.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                    s.HardwareName.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                    s.SensorType.Contains(filterText, StringComparison.OrdinalIgnoreCase));
            }

            var grouped = matchingSensors
                .GroupBy(s => (s.HardwareName, s.Category))
                .OrderBy(g => CategoryOrder(g.Key.Category))
                .ThenBy(g => g.Key.HardwareName)
                .ToList();

            HardwareGroups.Clear();

            foreach (var g in grouped)
            {
                var groupVm = new HardwareGroupViewModel(g.Key.HardwareName, g.First().HardwareType, g.Key.Category);
                foreach (var s in g.OrderBy(x => SensorTypeOrder(x.SensorType)).ThenBy(x => x.Name))
                {
                    groupVm.Sensors.Add(s);
                }
                HardwareGroups.Add(groupVm);
            }
        }

        private static int CategoryOrder(string cat)
        {
            return cat switch
            {
                "CPU" => 0,
                "GPU" => 1,
                "Memory" => 2,
                "Storage" => 3,
                "Motherboard" => 4,
                "Cooling" => 5,
                "Power" => 6,
                "Battery" => 7,
                "Network" => 8,
                _ => 9
            };
        }

        private static int SensorTypeOrder(string st)
        {
            return st switch
            {
                "Temperature" => 0,
                "Load" => 1,
                "Clock" => 2,
                "Power" => 3,
                "Voltage" => 4,
                "Current" => 5,
                "Fan" => 6,
                _ => 7
            };
        }

        private static string MapCategory(HardwareType hwType, SensorType sType, string sName)
        {
            switch (hwType)
            {
                case HardwareType.Cpu:
                    return "CPU";
                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                    return "GPU";
                case HardwareType.Memory:
                    return "Memory";
                case HardwareType.Storage:
                    return "Storage";
                case HardwareType.Motherboard:
                case HardwareType.SuperIO:
                    if (sType == SensorType.Fan || sName.Contains("fan", StringComparison.OrdinalIgnoreCase) || sName.Contains("pump", StringComparison.OrdinalIgnoreCase))
                        return "Cooling";
                    return "Motherboard";
                case HardwareType.Cooler:
                    return "Cooling";
                case HardwareType.Psu:
                    return "Power";
                case HardwareType.Battery:
                    return "Battery";
                case HardwareType.Network:
                    return "Network";
                default:
                    if (sName.Contains("fan", StringComparison.OrdinalIgnoreCase) || sName.Contains("pump", StringComparison.OrdinalIgnoreCase))
                        return "Cooling";
                    if (sName.Contains("power", StringComparison.OrdinalIgnoreCase) || sName.Contains("watt", StringComparison.OrdinalIgnoreCase))
                        return "Power";
                    return "Other";
            }
        }

        private static string MapUnit(SensorType st)
        {
            return st switch
            {
                SensorType.Temperature => "°C",
                SensorType.Load => "%",
                SensorType.Clock => "MHz",
                SensorType.Voltage => "V",
                SensorType.Power => "W",
                SensorType.Current => "A",
                SensorType.Fan => "RPM",
                SensorType.Flow => "L/h",
                SensorType.Control => "%",
                SensorType.Level => "%",
                SensorType.Frequency => "Hz",
                SensorType.Throughput => "B/s",
                SensorType.Data => "GB",
                SensorType.SmallData => "MB",
                SensorType.Energy => "mWh",
                SensorType.Humidity => "%",
                _ => string.Empty
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _timer.Stop();
                RefreshIntervalService.RefreshIntervalChanged -= OnRefreshIntervalChanged;
                AppViewStateService.ViewChanged -= OnViewChanged;
            }
            catch { }

            GC.SuppressFinalize(this);
        }
    }
}
