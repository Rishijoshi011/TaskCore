using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
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
    public partial class DiskAdapterViewModel : ObservableObject, IDisposable
    {
        private bool _disposed;
        [ObservableProperty]
        private ObservableCollection<ISeries> readSeries;

        [ObservableProperty]
        private ObservableCollection<ISeries> writeSeries;

        [ObservableProperty]
        private string diskName = "Unknown";

        [ObservableProperty]
        private string model = "Unknown";

        [ObservableProperty]
        private string interfaceType = "Unknown";

        [ObservableProperty]
        private string mediaType = "Unknown";

        [ObservableProperty]
        private string serialNumber = "Unknown";

        [ObservableProperty]
        private string busType = "Unknown";

        [ObservableProperty]
        private string healthStatus = "Unknown";

        [ObservableProperty]
        private string operationalStatus = "Unknown";

        [ObservableProperty]
        private string friendlyName = "Unknown";

        [ObservableProperty]
        private string uniqueId = "Unknown";

        [ObservableProperty]
        private string status = "Unknown";

        [ObservableProperty]
        private string sizeText = "N/A";

        [ObservableProperty]
        private string partitionsText = "N/A";

        [ObservableProperty]
        private string currentReadSpeedText = "0 MB/s";

        [ObservableProperty]
        private string currentWriteSpeedText = "0 MB/s";

        [ObservableProperty]
        private string currentActiveTimeText = "0%";

        [ObservableProperty]
        private string currentQueueLengthText = "0";

        [ObservableProperty]
        private string currentTemperatureText = "N/A";

        private readonly ObservableCollection<double> _readValues = new();
        private readonly ObservableCollection<double> _writeValues = new();

        private readonly DispatcherTimer _timer;
        private const int MaxPoints = 60;

        private readonly string? _instance;
        private PerformanceCounter? _readBytes;
        private PerformanceCounter? _writeBytes;
        private PerformanceCounter? _activeTime;
        private PerformanceCounter? _queueLen;

        private readonly object _wmiLock = new();
        private DateTime _lastWmiInfoUtc = DateTime.MinValue;
        private int _wmiTaskRunning;

        public DiskAdapterViewModel(string? physicalDiskInstance)
        {
            _instance = physicalDiskInstance;

            ReadSeries = new ObservableCollection<ISeries>
            {
                new LineSeries<double>
                {
                    Values = _readValues,
                    GeometrySize = 0,
                    Stroke = new SolidColorPaint(new SKColor(0, 255, 255), 2),
                    Fill = new SolidColorPaint(new SKColor(0, 255, 255, 30)),
                    LineSmoothness = 0,
                    IsVisibleAtLegend = false,
                    Name = "Read"
                }
            };

            WriteSeries = new ObservableCollection<ISeries>
            {
                new LineSeries<double>
                {
                    Values = _writeValues,
                    GeometrySize = 0,
                    Stroke = new SolidColorPaint(new SKColor(255, 100, 100), 2),
                    Fill = new SolidColorPaint(new SKColor(255, 100, 100, 25)),
                    LineSmoothness = 0,
                    IsVisibleAtLegend = false,
                    Name = "Write"
                }
            };

            for (int i = 0; i < 10; i++)
            {
                _readValues.Add(0);
                _writeValues.Add(0);
            }

            DiskName = string.IsNullOrWhiteSpace(_instance) ? "Disk" : _instance;

            TryInitCounters();
            ScheduleWmiInfoRefresh();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RefreshIntervalService.CurrentMs) };
            _timer.Tick += (_, __) => Tick();
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

        private void TryInitCounters()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_instance)) return;

                _readBytes = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", _instance, true);
                _writeBytes = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", _instance, true);
                _activeTime = new PerformanceCounter("PhysicalDisk", "% Disk Time", _instance, true);
                _queueLen = new PerformanceCounter("PhysicalDisk", "Current Disk Queue Length", _instance, true);

                _ = _readBytes.NextValue();
                _ = _writeBytes.NextValue();
                _ = _activeTime.NextValue();
                _ = _queueLen.NextValue();
            }
            catch
            {
                _readBytes = null;
                _writeBytes = null;
                _activeTime = null;
                _queueLen = null;
            }
        }

        private void Tick()
        {
            double readBps = 0;
            double writeBps = 0;
            double active = 0;
            double q = 0;

            try { if (_readBytes != null) readBps = _readBytes.NextValue(); } catch { }
            try { if (_writeBytes != null) writeBps = _writeBytes.NextValue(); } catch { }
            try { if (_activeTime != null) active = _activeTime.NextValue(); } catch { }
            try { if (_queueLen != null) q = _queueLen.NextValue(); } catch { }

            var readMb = readBps / (1024.0 * 1024.0);
            var writeMb = writeBps / (1024.0 * 1024.0);

            CurrentReadSpeedText = $"{readMb:F1} MB/s";
            CurrentWriteSpeedText = $"{writeMb:F1} MB/s";
            CurrentActiveTimeText = $"{Math.Clamp(active, 0, 100):F0}%";
            CurrentQueueLengthText = $"{q:F0}";

            Append(_readValues, readMb);
            Append(_writeValues, writeMb);

            UpdateTemperature();
            ScheduleWmiInfoRefresh();
        }

        private void UpdateTemperature()
        {
            try
            {
                var t = HardwareMonitorService.Instance.GetStorageTempByName(Model);
                CurrentTemperatureText = t.HasValue ? $"{t.Value:F1}°C" : "N/A";
            }
            catch
            {
                CurrentTemperatureText = "N/A";
            }
        }

        private void Append(ObservableCollection<double> values, double v)
        {
            try
            {
                values.Add(v);
                while (values.Count > MaxPoints)
                    values.RemoveAt(0);
            }
            catch
            {
            }
        }

        private void ScheduleWmiInfoRefresh()
        {
            if ((DateTime.UtcNow - _lastWmiInfoUtc) < TimeSpan.FromSeconds(15)) return;
            if (Interlocked.Exchange(ref _wmiTaskRunning, 1) == 1) return;

            _lastWmiInfoUtc = DateTime.UtcNow;

            _ = Task.Run(() =>
            {
                try
                {
                    RefreshWmiInfo();
                }
                catch
                {
                }
                finally
                {
                    Interlocked.Exchange(ref _wmiTaskRunning, 0);
                }
            });
        }

        private void RefreshWmiInfo()
        {
            // Best-effort mapping: use Win32_DiskDrive index if instance starts with "<n> ".
            try
            {
                int? index = null;
                if (!string.IsNullOrWhiteSpace(_instance))
                {
                    var parts = _instance.Split(' ');
                    if (parts.Length > 0 && int.TryParse(parts[0], out var i))
                        index = i;
                }

                var drives = new ManagementObjectSearcher("select * from Win32_DiskDrive").Get().Cast<ManagementObject>().ToList();
                ManagementObject? drive = null;

                if (index.HasValue)
                    drive = drives.FirstOrDefault(d => Convert.ToInt32(d["Index"]) == index.Value);

                drive ??= drives.FirstOrDefault();
                if (drive == null) return;

                // MSFT_PhysicalDisk provides better MediaType (SSD/HDD) and health on modern Windows.
                TryPopulateMsftPhysicalDisk(drive);

                var m = drive["Model"]?.ToString();
                if (!string.IsNullOrWhiteSpace(m))
                {
                    Model = m;
                    DiskName = m;
                }

                var iface = drive["InterfaceType"]?.ToString();
                if (!string.IsNullOrWhiteSpace(iface))
                    InterfaceType = iface;

                var mt = drive["MediaType"]?.ToString();
                if (!string.IsNullOrWhiteSpace(mt))
                    MediaType = mt;

                var sn = drive["SerialNumber"]?.ToString();
                if (!string.IsNullOrWhiteSpace(sn))
                    SerialNumber = sn.Trim();

                var st = drive["Status"]?.ToString();
                if (!string.IsNullOrWhiteSpace(st))
                    Status = st;

                try
                {
                    var sizeObj = drive["Size"];
                    if (sizeObj != null)
                    {
                        var bytes = Convert.ToDouble(sizeObj);
                        if (bytes > 0)
                            SizeText = $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
                    }
                }
                catch
                {
                }

                try
                {
                    var p = drive["Partitions"];
                    if (p != null)
                        PartitionsText = p.ToString() ?? "N/A";
                }
                catch
                {
                }
            }
            catch
            {
            }
        }

        private void TryPopulateMsftPhysicalDisk(ManagementObject win32DiskDrive)
        {
            try
            {
                // Attempt to map by serial number / model. Not perfect but works in many cases.
                var wmiSerial = win32DiskDrive["SerialNumber"]?.ToString()?.Trim();
                var wmiModel = win32DiskDrive["Model"]?.ToString();

                var scope = new ManagementScope("\\\\.\\root\\Microsoft\\Windows\\Storage");
                scope.Connect();

                var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT FriendlyName,UniqueId,SerialNumber,MediaType,BusType,HealthStatus,OperationalStatus,Size FROM MSFT_PhysicalDisk"));
                var list = searcher.Get().Cast<ManagementObject>().ToList();
                if (list.Count == 0) return;

                ManagementObject? match = null;

                if (!string.IsNullOrWhiteSpace(wmiSerial))
                {
                    match = list.FirstOrDefault(d => string.Equals((d["SerialNumber"]?.ToString() ?? string.Empty).Trim(), wmiSerial, StringComparison.OrdinalIgnoreCase));
                }

                if (match == null && !string.IsNullOrWhiteSpace(wmiModel))
                {
                    match = list.FirstOrDefault(d => (d["FriendlyName"]?.ToString() ?? string.Empty).Contains(wmiModel, StringComparison.OrdinalIgnoreCase));
                }

                match ??= list.FirstOrDefault();
                if (match == null) return;

                var fn = match["FriendlyName"]?.ToString();
                if (!string.IsNullOrWhiteSpace(fn))
                    FriendlyName = fn;

                var uid = match["UniqueId"]?.ToString();
                if (!string.IsNullOrWhiteSpace(uid))
                    UniqueId = uid;

                var bus = match["BusType"];
                if (bus != null)
                    BusType = BusTypeToString(Convert.ToUInt16(bus));

                var media = match["MediaType"];
                if (media != null)
                    MediaType = MediaTypeToString(Convert.ToUInt16(media));

                var hs = match["HealthStatus"];
                if (hs != null)
                    HealthStatus = HealthStatusToString(Convert.ToUInt16(hs));

                try
                {
                    var ops = match["OperationalStatus"] as ushort[];
                    if (ops != null && ops.Length > 0)
                        OperationalStatus = string.Join(", ", ops.Select(OperationalStatusToString));
                }
                catch
                {
                }
            }
            catch
            {
            }
        }

        private static string MediaTypeToString(ushort v)
        {
            return v switch
            {
                0 => "Unspecified",
                3 => "HDD",
                4 => "SSD",
                5 => "SCM",
                _ => v.ToString()
            };
        }

        private static string BusTypeToString(ushort v)
        {
            return v switch
            {
                0 => "Unknown",
                1 => "SCSI",
                2 => "ATAPI",
                3 => "ATA",
                4 => "IEEE 1394",
                5 => "SSA",
                6 => "Fibre Channel",
                7 => "USB",
                8 => "RAID",
                9 => "iSCSI",
                10 => "SAS",
                11 => "SATA",
                12 => "SD",
                13 => "MMC",
                14 => "Virtual",
                15 => "File-Backed Virtual",
                16 => "Spaces",
                17 => "NVMe",
                _ => v.ToString()
            };
        }

        private static string HealthStatusToString(ushort v)
        {
            return v switch
            {
                0 => "Healthy",
                1 => "Warning",
                2 => "Unhealthy",
                5 => "Unknown",
                _ => v.ToString()
            };
        }

        private static string OperationalStatusToString(ushort v)
        {
            return v switch
            {
                0 => "Unknown",
                1 => "Other",
                2 => "OK",
                3 => "Degraded",
                4 => "Stressed",
                5 => "Predictive Failure",
                6 => "Error",
                7 => "Non-Recoverable Error",
                8 => "Starting",
                9 => "Stopping",
                10 => "Stopped",
                11 => "In Service",
                12 => "No Contact",
                13 => "Lost Communication",
                14 => "Aborted",
                15 => "Dormant",
                16 => "Supporting Entity in Error",
                17 => "Completed",
                18 => "Power Mode",
                _ => v.ToString()
            };
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

            try { _readBytes?.Dispose(); _readBytes = null; } catch { }
            try { _writeBytes?.Dispose(); _writeBytes = null; } catch { }
            try { _activeTime?.Dispose(); _activeTime = null; } catch { }
            try { _queueLen?.Dispose(); _queueLen = null; } catch { }

            GC.SuppressFinalize(this);
        }
    }
}
