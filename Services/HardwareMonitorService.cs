using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;

namespace TaskCore.Services
{
    public sealed class HardwareMonitorService : IDisposable
    {
        private static readonly Lazy<HardwareMonitorService> _instance = new(() => new HardwareMonitorService());
        public static HardwareMonitorService Instance => _instance.Value;

        private readonly Computer _computer;
        private readonly DispatcherTimer _timer;
        private readonly UpdateVisitor _updateVisitor = new();

        private readonly object _lock = new();
        private readonly Dictionary<string, float> _gpuTempsByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _storageTempsByName = new(StringComparer.OrdinalIgnoreCase);
        private float? _cpuPackageTemp;
        private float? _memoryTemp;
        private float? _anyTemp;
        private DateTime _lastDebugLogUtc = DateTime.MinValue;

        private HardwareMonitorService()
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true
            };

            _computer.Open();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RefreshIntervalService.CurrentMs) };
            _timer.Tick += (_, __) => Refresh();
            _timer.Start();

            try
            {
                RefreshIntervalService.RefreshIntervalChanged += OnRefreshIntervalChanged;
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

            Refresh();
        }

        private void OnViewChanged(string viewName)
        {
            try
            {
                // Only refresh temps when these views are visible.
                if (string.Equals(viewName, "Dashboard", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(viewName, "Graphs", StringComparison.OrdinalIgnoreCase))
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

        private void Refresh()
        {
            try
            {
                lock (_lock)
                {
                    try
                    {
                        _computer.Accept(_updateVisitor);
                    }
                    catch
                    {
                    }

                    _gpuTempsByName.Clear();
                    _storageTempsByName.Clear();
                    _cpuPackageTemp = null;
                    _memoryTemp = null;
                    _anyTemp = null;

                    foreach (var hw in _computer.Hardware)
                    {
                        ProcessHardwareRecursive(hw);
                    }

                    TryDebugLogTempSensors();
                }
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

        private void TryDebugLogTempSensors()
        {
            // Log at most once every 15 seconds to avoid spamming.
            var now = DateTime.UtcNow;
            if ((now - _lastDebugLogUtc) < TimeSpan.FromSeconds(15)) return;
            _lastDebugLogUtc = now;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("[LibreHardwareMonitor] Temperature scan");

                if (_cpuPackageTemp.HasValue) sb.AppendLine($"CPU temp: {_cpuPackageTemp.Value:F1} C");
                else sb.AppendLine("CPU temp: (none)");

                if (_gpuTempsByName.Count > 0)
                {
                    sb.AppendLine("GPU temps:");
                    foreach (var kv in _gpuTempsByName)
                        sb.AppendLine($"  {kv.Key}: {kv.Value:F1} C");
                }
                else
                {
                    sb.AppendLine("GPU temps: (none)");
                }

                if (_memoryTemp.HasValue) sb.AppendLine($"Memory temp: {_memoryTemp.Value:F1} C");
                else sb.AppendLine("Memory temp: (none)");

                if (_anyTemp.HasValue) sb.AppendLine($"Any temp fallback: {_anyTemp.Value:F1} C");
                else sb.AppendLine("Any temp fallback: (none) -> likely no exposed temp sensors on this machine");

                sb.AppendLine("-- Raw temperature sensors --");
                foreach (var hw in _computer.Hardware)
                    AppendTempsRecursive(sb, hw, indent: "");

                Debug.WriteLine(sb.ToString());
            }
            catch
            {
            }
        }

        private static void AppendTempsRecursive(StringBuilder sb, IHardware hw, string indent)
        {
            try
            {
                sb.AppendLine($"{indent}{hw.HardwareType}: {hw.Name}");

                foreach (var s in hw.Sensors)
                {
                    if (s.SensorType != SensorType.Temperature) continue;
                    var val = s.Value.HasValue ? $"{s.Value.Value:F1} C" : "(null)";
                    sb.AppendLine($"{indent}  - {s.Name}: {val}");
                }

                foreach (var sub in hw.SubHardware)
                    AppendTempsRecursive(sb, sub, indent + "  ");
            }
            catch
            {
            }
        }

        private sealed class UpdateVisitor : IVisitor
        {
            public void VisitComputer(IComputer computer)
            {
                foreach (var hardware in computer.Hardware)
                    hardware.Accept(this);
            }

            public void VisitHardware(IHardware hardware)
            {
                try
                {
                    hardware.Update();
                }
                catch
                {
                }

                try
                {
                    foreach (var subHardware in hardware.SubHardware)
                        subHardware.Accept(this);
                }
                catch
                {
                }
            }

            public void VisitSensor(ISensor sensor)
            {
            }

            public void VisitParameter(IParameter parameter)
            {
            }
        }

        private void ProcessHardwareRecursive(IHardware hw)
        {
            try
            {
                hw.Update();
            }
            catch
            {
            }

            try
            {
                // Track any temperature sensor we can find as a diagnostic fallback.
                if (!_anyTemp.HasValue)
                {
                    var any = ReadTemperature(hw, preferredNames: ["package", "cpu", "gpu", "core", "hot", "junction", "memory", "ram", "dimm", "temperature"]);
                    if (any.HasValue)
                        _anyTemp = any;
                }

                if (hw.HardwareType == HardwareType.Cpu)
                {
                    var temp = ReadTemperature(hw, preferredNames: ["package", "cpu package", "tctl", "tdie", "core", "cpu", "die", "processor"]);
                    if (temp.HasValue)
                        _cpuPackageTemp = temp;
                }
                else if (hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuAmd || hw.HardwareType == HardwareType.GpuIntel)
                {
                    var temp = ReadTemperature(hw, preferredNames: ["gpu core", "gpu", "core", "hot spot", "hotspot", "junction", "temperature", "graphics", "gt", "intel"]);
                    if (temp.HasValue)
                        _gpuTempsByName[hw.Name] = temp.Value;
                }
                else if (hw.HardwareType == HardwareType.Memory)
                {
                    var temp = ReadTemperature(hw, preferredNames: ["memory", "ram", "dimm", "dram", "module"]);
                    if (temp.HasValue)
                        _memoryTemp = temp;
                }
                else if (hw.HardwareType == HardwareType.Storage)
                {
                    var temp = ReadTemperature(hw, preferredNames: ["temperature", "drive", "hdd", "ssd", "nvme", "composite"]);
                    if (temp.HasValue)
                        _storageTempsByName[hw.Name] = temp.Value;
                }
            }
            catch
            {
            }

            try
            {
                foreach (var sub in hw.SubHardware)
                    ProcessHardwareRecursive(sub);
            }
            catch
            {
            }
        }

        private static float? ReadTemperature(IHardware hw, string[] preferredNames)
        {
            var sensors = hw.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue).ToList();
            if (sensors.Count == 0) return null;

            foreach (var name in preferredNames)
            {
                var match = sensors.FirstOrDefault(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match.Value;
            }

            return sensors.First().Value;
        }

        public float? GetCpuPackageTemp()
        {
            lock (_lock) return _cpuPackageTemp ?? _anyTemp;
        }

        public float? GetMemoryTemp()
        {
            lock (_lock) return _memoryTemp ?? _anyTemp;
        }

        public float? GetGpuTempByName(string gpuName)
        {
            lock (_lock)
            {
                if (_gpuTempsByName.TryGetValue(gpuName, out var v)) return v;

                var key = _gpuTempsByName.Keys.FirstOrDefault(k => k.Contains(gpuName, StringComparison.OrdinalIgnoreCase) || gpuName.Contains(k, StringComparison.OrdinalIgnoreCase));
                if (key != null) return _gpuTempsByName[key];

                // Fallback for iGPU / naming mismatches: return any GPU temperature if available.
                if (_gpuTempsByName.Count > 0) return _gpuTempsByName.Values.First();

                return _anyTemp;
            }
        }

        public float? GetStorageTempByName(string? diskModelOrName)
        {
            if (string.IsNullOrWhiteSpace(diskModelOrName))
            {
                lock (_lock)
                    return _storageTempsByName.Count > 0 ? _storageTempsByName.Values.FirstOrDefault() : _anyTemp;
            }

            lock (_lock)
            {
                if (_storageTempsByName.TryGetValue(diskModelOrName, out var v)) return v;

                var key = _storageTempsByName.Keys.FirstOrDefault(k => k.Contains(diskModelOrName, StringComparison.OrdinalIgnoreCase) || diskModelOrName.Contains(k, StringComparison.OrdinalIgnoreCase));
                if (key != null) return _storageTempsByName[key];

                return _storageTempsByName.Count > 0 ? _storageTempsByName.Values.FirstOrDefault() : _anyTemp;
            }
        }

        public void Dispose()
        {
            try
            {
                _timer.Stop();
            }
            catch
            {
            }

            try
            {
                _computer.Close();
            }
            catch
            {
            }
        }
    }
}
