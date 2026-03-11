using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using TaskCore.Services;

namespace TaskCore.ViewModels.Graphs
{
    public partial class GpuAdapterViewModel : ObservableObject, IDisposable
    {
        private bool _disposed;
        private static readonly object _wmiLock = new();
        private static int _nextWmiIndex;

        [ObservableProperty]
        private ObservableCollection<ISeries> series;

        [ObservableProperty]
        private string gpuName = "Unknown";

        [ObservableProperty]
        private double currentUtilization;

        [ObservableProperty]
        private string currentTemperatureText = "N/A";

        [ObservableProperty]
        private string currentMemoryUsageText = "N/A";

        [ObservableProperty]
        private string totalMemoryText = "N/A";

        [ObservableProperty]
        private string manufacturer = "Unknown";

        [ObservableProperty]
        private string driverVersion = "Unknown";

        [ObservableProperty]
        private string driverDate = "Unknown";

        [ObservableProperty]
        private string adapterCompatibility = "Unknown";

        [ObservableProperty]
        private string videoProcessor = "Unknown";

        [ObservableProperty]
        private string status = "Unknown";

        [ObservableProperty]
        private string currentResolution = "Unknown";

        [ObservableProperty]
        private string currentRefreshRate = "Unknown";

        [ObservableProperty]
        private string videoModeDescription = "Unknown";

        [ObservableProperty]
        private string adapterDacType = "Unknown";

        [ObservableProperty]
        private string pnpDeviceId = "Unknown";

        [ObservableProperty]
        private string infFilename = "Unknown";

        [ObservableProperty]
        private string infSection = "Unknown";

        private readonly ObservableCollection<double> _values = new();
        private readonly DispatcherTimer _timer;
        private const int MaxPoints = 60;

        private readonly List<PerformanceCounter> _gpu3dCounters = new();
        private readonly List<PerformanceCounter> _gpuAnyCounters = new();
        private PerformanceCounter? _dedicatedUsage;
        private PerformanceCounter? _dedicatedLimit;
        private PerformanceCounter? _sharedUsage;
        private PerformanceCounter? _sharedLimit;

        private readonly object _wmiCacheLock = new();
        private DateTime _lastWmiUtilReadUtc = DateTime.MinValue;
        private DateTime _lastWmiVramReadUtc = DateTime.MinValue;
        private double? _cachedWmiUtil;
        private string _cachedWmiUtilSource = string.Empty;
        private (double? usedGb, double? totalGb)? _cachedWmiVram;
        private int _wmiUtilTaskRunning;
        private int _wmiVramTaskRunning;

        private double? _wmiAdapterRamTotalGb;

        private readonly string? _adapterMemoryInstance;
        private readonly string? _luidToken;

        public GpuAdapterViewModel(string? adapterMemoryInstance, string? gpuNameHint)
        {
            try
            {
                _adapterMemoryInstance = adapterMemoryInstance;
                _luidToken = ExtractLuid(adapterMemoryInstance);

                Log("Creating Series collection");
                Series = new ObservableCollection<ISeries>
                {
                    new LineSeries<double>
                    {
                        Values = _values,
                        GeometrySize = 0,
                        Stroke = new SolidColorPaint(new SKColor(255, 220, 0), 3),
                        Fill = new SolidColorPaint(new SKColor(255, 220, 0, 40)),
                        LineSmoothness = 0,
                        IsVisibleAtLegend = false,
                        Name = "GPU"
                    }
                };
                Log("Series created successfully");

                Log("Initializing GPU info");
                InitializeGpuInfo(gpuNameHint);
                Log("GPU info initialized");

                Log("Initializing counters");
                TryInitializeCounters();
                Log("Counters initialized");

                for (int i = 0; i < 10; i++)
                    _values.Add(0);

                Log("Creating timer");
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RefreshIntervalService.CurrentMs) };
                _timer.Tick += (_, __) => Tick();
                _timer.Start();
                Log("Timer started");

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
                Log("Constructor complete");
            }
            catch (Exception ex)
            {
                Log($"FATAL ERROR in constructor: {ex.GetType().Name}: {ex.Message}");
                Log($"StackTrace: {ex.StackTrace}");
                TaskCoreLogger.Error("gpu-adapter", $"Constructor error: {ex}");
                throw;
            }
        }

        private static void Log(string msg) => TaskCoreLogger.Info("gpu-adapter", msg);

        private void OnViewChanged(string viewName)
        {
            try
            {
                if (viewName == "Graphs")
                    _timer.Start();
                else
                    _timer.Stop();
            }
            catch
            {
            }
        }

        private void OnRefreshIntervalChanged(int ms)
        {
            try
            {
                _timer.Interval = TimeSpan.FromMilliseconds(RefreshIntervalService.Normalize(ms));
            }
            catch
            {
            }
        }

        private void InitializeGpuInfo(string? gpuNameHint)
        {
            try
            {
                var gpus = new ManagementObjectSearcher("select * from Win32_VideoController").Get().Cast<ManagementObject>().ToList();
                ManagementObject? match = null;

                if (!string.IsNullOrWhiteSpace(gpuNameHint))
                    match = gpus.FirstOrDefault(g => (g["Name"]?.ToString() ?? string.Empty).Contains(gpuNameHint, StringComparison.OrdinalIgnoreCase));

                if (match == null && gpus.Count > 0)
                {
                    lock (_wmiLock)
                    {
                        if (_nextWmiIndex < 0) _nextWmiIndex = 0;
                        match = gpus.ElementAtOrDefault(_nextWmiIndex);
                        _nextWmiIndex++;
                    }
                }

                match ??= gpus.FirstOrDefault();

                var name = match?["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                    GpuName = name;

                try
                {
                    var adapterRamObj = match?["AdapterRAM"];
                    if (adapterRamObj != null)
                    {
                        var bytes = Convert.ToDouble(adapterRamObj);
                        if (bytes > 0)
                        {
                            _wmiAdapterRamTotalGb = bytes / (1024.0 * 1024.0 * 1024.0);
                            // If we don't later get a better "Dedicated Limit" value, at least show something.
                            TotalMemoryText = $"{_wmiAdapterRamTotalGb.Value:F1} GB";
                        }
                    }
                }
                catch
                {
                }

                if (!string.IsNullOrWhiteSpace(GpuName))
                {
                    if (GpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || GpuName.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
                        Manufacturer = "NVIDIA";
                    else if (GpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) || GpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || GpuName.Contains("ATI", StringComparison.OrdinalIgnoreCase))
                        Manufacturer = "AMD";
                    else if (GpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase) || GpuName.Contains("UHD", StringComparison.OrdinalIgnoreCase) || GpuName.Contains("Iris", StringComparison.OrdinalIgnoreCase))
                        Manufacturer = "Intel";
                }

                var compat = match?["AdapterCompatibility"]?.ToString();
                if (!string.IsNullOrWhiteSpace(compat))
                    AdapterCompatibility = compat;

                var vp = match?["VideoProcessor"]?.ToString();
                if (!string.IsNullOrWhiteSpace(vp))
                    VideoProcessor = vp;

                var st = match?["Status"]?.ToString();
                if (!string.IsNullOrWhiteSpace(st))
                    Status = st;

                var pnp = match?["PNPDeviceID"]?.ToString();
                if (!string.IsNullOrWhiteSpace(pnp))
                    PnpDeviceId = pnp;

                var infFile = match?["InfFilename"]?.ToString();
                if (!string.IsNullOrWhiteSpace(infFile))
                    InfFilename = infFile;

                var infSec = match?["InfSection"]?.ToString();
                if (!string.IsNullOrWhiteSpace(infSec))
                    InfSection = infSec;

                var dac = match?["AdapterDACType"]?.ToString();
                if (!string.IsNullOrWhiteSpace(dac))
                    AdapterDacType = dac;

                var vm = match?["VideoModeDescription"]?.ToString();
                if (!string.IsNullOrWhiteSpace(vm))
                    VideoModeDescription = vm;

                var drv = match?["DriverVersion"]?.ToString();
                if (!string.IsNullOrWhiteSpace(drv))
                    DriverVersion = drv;

                var dd = match?["DriverDate"]?.ToString();
                if (!string.IsNullOrWhiteSpace(dd))
                {
                    try
                    {
                        var dt = ManagementDateTimeConverter.ToDateTime(dd);
                        DriverDate = dt.ToString("yyyy-MM-dd");
                    }
                    catch
                    {
                        DriverDate = dd;
                    }
                }

                try
                {
                    var w = match?["CurrentHorizontalResolution"];
                    var h = match?["CurrentVerticalResolution"];
                    if (w != null && h != null)
                    {
                        var ws = Convert.ToInt32(w);
                        var hs = Convert.ToInt32(h);
                        if (ws > 0 && hs > 0)
                            CurrentResolution = $"{ws}x{hs}";
                    }
                }
                catch
                {
                }

                try
                {
                    var rr = match?["CurrentRefreshRate"];
                    if (rr != null)
                    {
                        var hz = Convert.ToInt32(rr);
                        if (hz > 0)
                            CurrentRefreshRate = $"{hz} Hz";
                    }
                }
                catch
                {
                }
            }
            catch
            {
            }
        }

        private void ScheduleWmiVramRefresh()
        {
            // throttle: at most once every 5 seconds
            if ((DateTime.UtcNow - _lastWmiVramReadUtc) < TimeSpan.FromSeconds(5)) return;
            if (Interlocked.Exchange(ref _wmiVramTaskRunning, 1) == 1) return;

            _lastWmiVramReadUtc = DateTime.UtcNow;

            _ = Task.Run(() =>
            {
                try
                {
                    if (TryGetVramFromWmi(out var usedGb, out var totalGb))
                    {
                        lock (_wmiCacheLock)
                        {
                            _cachedWmiVram = (usedGb, totalGb);
                        }
                    }
                }
                catch
                {
                }
                finally
                {
                    Interlocked.Exchange(ref _wmiVramTaskRunning, 0);
                }
            });
        }

        private bool TryGetVramFromWmi(out double? usedGb, out double? totalGb)
        {
            usedGb = null;
            totalGb = null;

            // Best-effort: query WMI perf formatted adapter memory class if present.
            // Not all Windows builds expose this class.
            try
            {
                var className = "Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory";
                var searcher = new ManagementObjectSearcher($"SELECT Name,DedicatedUsage,DedicatedLimit,SharedUsage,SharedLimit FROM {className}");
                var items = searcher.Get().Cast<ManagementObject>().ToList();
                if (items.Count == 0) return false;

                // Try LUID match first.
                IEnumerable<ManagementObject> filtered = items;
                if (!string.IsNullOrWhiteSpace(_luidToken))
                    filtered = filtered.Where(m => (m["Name"]?.ToString() ?? string.Empty).Contains(_luidToken, StringComparison.OrdinalIgnoreCase));

                var match = filtered.FirstOrDefault() ?? items.FirstOrDefault();
                if (match == null) return false;

                double du = TryGetDouble(match, "DedicatedUsage");
                double dl = TryGetDouble(match, "DedicatedLimit");
                double su = TryGetDouble(match, "SharedUsage");
                double sl = TryGetDouble(match, "SharedLimit");

                var totalBytes = dl > 0 ? dl : (sl > 0 ? sl : 0);
                var usedBytes = dl > 0 ? du : (sl > 0 ? su : 0);
                if (totalBytes <= 0) return false;

                totalGb = totalBytes / (1024.0 * 1024.0 * 1024.0);
                usedGb = usedBytes / (1024.0 * 1024.0 * 1024.0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static double TryGetDouble(ManagementObject obj, string property)
        {
            try
            {
                var v = obj[property];
                if (v == null) return 0;
                if (double.TryParse(v.ToString(), out var d)) return d;
                return Convert.ToDouble(v);
            }
            catch
            {
                return 0;
            }
        }

        private void TryAddGpuAnyEngineCounters(bool requireLuidMatch)
        {
            try
            {
                var cat = new PerformanceCounterCategory("GPU Engine");
                foreach (var instance in cat.GetInstanceNames())
                {
                    if (!instance.Contains("engtype_", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (requireLuidMatch && !string.IsNullOrWhiteSpace(_luidToken) && !instance.Contains(_luidToken, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, true);
                        _ = c.NextValue();
                        _gpuAnyCounters.Add(c);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private void TryInitializeCounters()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_adapterMemoryInstance))
                {
                    try
                    {
                        _dedicatedUsage = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", _adapterMemoryInstance, true);
                        _dedicatedLimit = new PerformanceCounter("GPU Adapter Memory", "Dedicated Limit", _adapterMemoryInstance, true);
                        _sharedUsage = new PerformanceCounter("GPU Adapter Memory", "Shared Usage", _adapterMemoryInstance, true);
                        _sharedLimit = new PerformanceCounter("GPU Adapter Memory", "Shared Limit", _adapterMemoryInstance, true);

                        _ = _dedicatedUsage.NextValue();
                        _ = _dedicatedLimit.NextValue();
                        _ = _sharedUsage.NextValue();
                        _ = _sharedLimit.NextValue();
                    }
                    catch
                    {
                        _dedicatedUsage = null;
                        _dedicatedLimit = null;
                        _sharedUsage = null;
                        _sharedLimit = null;
                    }
                }

                TryAddGpuEngineCounters(requireLuidMatch: true);
                if (_gpu3dCounters.Count == 0)
                    TryAddGpuEngineCounters(requireLuidMatch: false);

                // If 3D engine counters aren't available/working on this machine, fall back to any engine type.
                if (_gpu3dCounters.Count == 0)
                {
                    TryAddGpuAnyEngineCounters(requireLuidMatch: true);
                    if (_gpuAnyCounters.Count == 0)
                        TryAddGpuAnyEngineCounters(requireLuidMatch: false);
                }
            }
            catch
            {
            }
        }

        private void TryAddGpuEngineCounters(bool requireLuidMatch)
        {
            try
            {
                var cat = new PerformanceCounterCategory("GPU Engine");
                foreach (var instance in cat.GetInstanceNames())
                {
                    if (!instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (requireLuidMatch && !string.IsNullOrWhiteSpace(_luidToken) && !instance.Contains(_luidToken, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, true);
                        _ = c.NextValue();
                        _gpu3dCounters.Add(c);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private void Tick()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[GpuGraph] Tick fired - CurrentUtil: {CurrentUtilization:F1}%");
                UpdateUtilization();
                UpdateVram();
                UpdateTemperature();
                System.Diagnostics.Debug.WriteLine($"[GpuGraph] After update - CurrentUtil: {CurrentUtilization:F1}%");
                AppendPoint(CurrentUtilization);
            }
            catch (Exception ex)
            {
                TaskCoreLogger.Error("gpu-graph", $"Tick error: {ex}");
            }
        }

        private void UpdateUtilization()
        {
            double gpu;

            if (_gpu3dCounters.Count > 0)
            {
                try
                {
                    float max = 0;
                    foreach (var c in _gpu3dCounters)
                    {
                        var v = c.NextValue();
                        if (v > max) max = v;
                    }

                    gpu = Math.Round(Math.Clamp(max, 0, 100), 1);
                }
                catch (Exception ex)
                {
                    TaskCoreLogger.Error("gpu-graph", $"3D counter error: {ex.Message}");
                    gpu = CurrentUtilization;
                }
            }
            else if (_gpuAnyCounters.Count > 0)
            {
                try
                {
                    float max = 0;
                    foreach (var c in _gpuAnyCounters)
                    {
                        var v = c.NextValue();
                        if (v > max) max = v;
                    }

                    gpu = Math.Round(Math.Clamp(max, 0, 100), 1);
                }
                catch (Exception ex)
                {
                    TaskCoreLogger.Error("gpu-graph", $"AnyEngine counter error: {ex.Message}");
                    gpu = CurrentUtilization;
                }
            }
            else
            {
                ScheduleWmiUtilRefresh();
                lock (_wmiCacheLock)
                {
                    if (_cachedWmiUtil.HasValue)
                    {
                        gpu = _cachedWmiUtil.Value;
                    }
                    else
                    {
                        gpu = CurrentUtilization;
                    }
                }
            }

            CurrentUtilization = gpu;
        }

        private void ScheduleWmiUtilRefresh()
        {
            // throttle: at most once every 2 seconds
            if ((DateTime.UtcNow - _lastWmiUtilReadUtc) < TimeSpan.FromSeconds(2)) return;
            if (Interlocked.Exchange(ref _wmiUtilTaskRunning, 1) == 1) return;

            _lastWmiUtilReadUtc = DateTime.UtcNow;

            _ = Task.Run(() =>
            {
                try
                {
                    if (TryGetUtilizationFromWmi(out var util, out var src))
                    {
                        lock (_wmiCacheLock)
                        {
                            _cachedWmiUtil = util;
                            _cachedWmiUtilSource = src;
                        }
                    }
                }
                catch
                {
                }
                finally
                {
                    Interlocked.Exchange(ref _wmiUtilTaskRunning, 0);
                }
            });
        }

        private bool TryGetUtilizationFromWmi(out double utilization, out string source)
        {
            utilization = 0;
            source = string.Empty;

            // Some machines don't expose GPU perf counters through PerformanceCounterCategory,
            // but WMI perf classes can still provide formatted values.
            try
            {
                // 1) Try GPUAdapter UtilizationPercentage (best case).
                if (TryReadWmiMax(
                        className: "Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapter",
                        valueProperty: "UtilizationPercentage",
                        out utilization))
                {
                    source = "WMI-Adapter";
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                // 2) Fall back to GPUEngine UtilizationPercentage filtered to 3D engines when possible.
                if (TryReadWmiMax(
                        className: "Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine",
                        valueProperty: "UtilizationPercentage",
                        out utilization,
                        require3DEngine: true))
                {
                    source = "WMI-Engine3D";
                    return true;
                }

                if (TryReadWmiMax(
                        className: "Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine",
                        valueProperty: "UtilizationPercentage",
                        out utilization,
                        require3DEngine: false))
                {
                    source = "WMI-Engine";
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryReadWmiMax(string className, string valueProperty, out double maxValue, bool require3DEngine = false)
        {
            maxValue = 0;
            var found = false;

            try
            {
                var searcher = new ManagementObjectSearcher($"SELECT Name,{valueProperty} FROM {className}");
                foreach (var obj in searcher.Get().Cast<ManagementObject>())
                {
                    var name = obj["Name"]?.ToString() ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(_luidToken) && !name.Contains(_luidToken, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (require3DEngine && !name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var valObj = obj[valueProperty];
                    if (valObj == null) continue;

                    if (!double.TryParse(valObj.ToString(), out var v))
                        continue;

                    if (v > maxValue) maxValue = v;
                    found = true;
                }

                if (!found && !string.IsNullOrWhiteSpace(_luidToken))
                {
                    // If LUID filtering was too strict, retry without it.
                    var retry = new ManagementObjectSearcher($"SELECT Name,{valueProperty} FROM {className}");
                    foreach (var obj in retry.Get().Cast<ManagementObject>())
                    {
                        var name = obj["Name"]?.ToString() ?? string.Empty;
                        if (require3DEngine && !name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var valObj = obj[valueProperty];
                        if (valObj == null) continue;

                        if (!double.TryParse(valObj.ToString(), out var v))
                            continue;

                        if (v > maxValue) maxValue = v;
                        found = true;
                    }
                }
            }
            catch
            {
                return false;
            }

            if (!found) return false;
            maxValue = Math.Round(Math.Clamp(maxValue, 0, 100), 1);
            return true;
        }

        private void UpdateVram()
        {
            try
            {
                double dedicatedUsed = 0;
                double dedicatedLimit = 0;
                double sharedUsed = 0;
                double sharedLimit = 0;

                if (_dedicatedUsage != null) dedicatedUsed = _dedicatedUsage.NextValue();
                if (_dedicatedLimit != null) dedicatedLimit = _dedicatedLimit.NextValue();
                if (_sharedUsage != null) sharedUsed = _sharedUsage.NextValue();
                if (_sharedLimit != null) sharedLimit = _sharedLimit.NextValue();

                var hasDedicated = dedicatedLimit > 0;
                var hasShared = sharedLimit > 0;

                var totalBytes = hasDedicated ? dedicatedLimit : (hasShared ? sharedLimit : 0);
                var usedBytes = hasDedicated ? dedicatedUsed : (hasShared ? sharedUsed : 0);

                if (totalBytes <= 0)
                {
                    // WMI can be slow and will hang the UI if called in this timer.
                    // Use cached values that are refreshed in the background.
                    ScheduleWmiVramRefresh();
                    lock (_wmiCacheLock)
                    {
                        if (_cachedWmiVram.HasValue)
                        {
                            var usedGb = _cachedWmiVram.Value.usedGb;
                            var totalGb = _cachedWmiVram.Value.totalGb;
                            TotalMemoryText = totalGb.HasValue ? $"{totalGb.Value:F1} GB" : "N/A";
                            CurrentMemoryUsageText = usedGb.HasValue ? $"{usedGb.Value:F1} GB" : "N/A";
                        }
                        else
                        {
                            // Still show used bytes if we can read it even without a known total.
                            if (usedBytes > 0)
                                CurrentMemoryUsageText = $"{usedBytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
                            else
                                CurrentMemoryUsageText = "N/A";

                            if (_wmiAdapterRamTotalGb.HasValue)
                                TotalMemoryText = $"{_wmiAdapterRamTotalGb.Value:F1} GB";
                            else
                                TotalMemoryText = "N/A";
                        }
                    }
                    return;
                }

                TotalMemoryText = $"{totalBytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
                CurrentMemoryUsageText = $"{usedBytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
            }
            catch
            {
            }
        }

        private void UpdateTemperature()
        {
            try
            {
                var hw = HardwareMonitorService.Instance;
                float? t = null;

                try
                {
                    t = hw.GetGpuTempByName(GpuName);
                }
                catch
                {
                    t = null;
                }

                if (!t.HasValue)
                {
                    try
                    {
                        t = hw.GetCpuPackageTemp();
                    }
                    catch
                    {
                        t = null;
                    }
                }

                CurrentTemperatureText = (t.HasValue && t.Value > -50 && t.Value < 150)
                    ? $"{t.Value:F1} °C"
                    : "N/A";
            }
            catch
            {
                CurrentTemperatureText = "N/A";
            }
        }

        private void AppendPoint(double value)
        {
            try
            {
                _values.Add(value);
                while (_values.Count > MaxPoints)
                    _values.RemoveAt(0);
                TaskCoreLogger.Debug("gpu-graph", $"Added point: {value:F1}% (total points: {_values.Count})");
            }
            catch (Exception ex)
            {
                TaskCoreLogger.Error("gpu-graph", $"Error adding point: {ex.Message}");
            }
        }

        private static string? ExtractLuid(string? instance)
        {
            if (string.IsNullOrWhiteSpace(instance)) return null;

            var idx = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;

            var end = instance.IndexOf('_', idx + 5);
            if (end < 0) return instance[idx..];

            return instance[idx..end];
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                RefreshIntervalService.RefreshIntervalChanged -= OnRefreshIntervalChanged;
            }
            catch { }

            try
            {
                AppViewStateService.ViewChanged -= OnViewChanged;
            }
            catch { }

            try
            {
                if (_timer != null)
                {
                    _timer.Stop();
                    _timer.Tick -= (_, __) => Tick();
                }
            }
            catch { }

            foreach (var c in _gpu3dCounters) { try { c?.Dispose(); } catch { } }
            _gpu3dCounters.Clear();
            foreach (var c in _gpuAnyCounters) { try { c?.Dispose(); } catch { } }
            _gpuAnyCounters.Clear();
            try { _dedicatedUsage?.Dispose(); _dedicatedUsage = null; } catch { }
            try { _dedicatedLimit?.Dispose(); _dedicatedLimit = null; } catch { }
            try { _sharedUsage?.Dispose(); _sharedUsage = null; } catch { }
            try { _sharedLimit?.Dispose(); _sharedLimit = null; } catch { }

            GC.SuppressFinalize(this);
        }
    }
}
