using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using TaskCore.Services;

namespace TaskCore.ViewModels.Graphs
{
    public partial class NetworkAdapterViewModel : ObservableObject, IDisposable
    {
        private bool _disposed;
        [ObservableProperty]
        private ObservableCollection<ISeries> downloadSeries;

        [ObservableProperty]
        private ObservableCollection<ISeries> uploadSeries;

        [ObservableProperty]
        private string adapterName = "Unknown";

        [ObservableProperty]
        private string description = "Unknown";

        [ObservableProperty]
        private string status = "Unknown";

        [ObservableProperty]
        private string macAddress = "Unknown";

        [ObservableProperty]
        private string linkSpeedText = "N/A";

        [ObservableProperty]
        private string ipv4Text = "N/A";

        [ObservableProperty]
        private string ipv6Text = "N/A";

        [ObservableProperty]
        private string gatewayText = "N/A";

        [ObservableProperty]
        private string dnsText = "N/A";

        [ObservableProperty]
        private string dhcpText = "N/A";

        [ObservableProperty]
        private string currentDownloadText = "0 Mbps";

        [ObservableProperty]
        private string currentUploadText = "0 Mbps";

        private readonly ObservableCollection<double> _downValues = new();
        private readonly ObservableCollection<double> _upValues = new();
        private const int MaxPoints = 60;

        private readonly DispatcherTimer _timer;
        private readonly string? _nicId;

        private long _lastRx;
        private long _lastTx;
        private DateTime _lastSampleUtc;

        private DateTime _lastInfoUtc = DateTime.MinValue;
        private int _infoTaskRunning;

        public NetworkAdapterViewModel(string? nicId, string? fallbackName)
        {
            _nicId = nicId;
            AdapterName = fallbackName ?? "Network";

            DownloadSeries = new ObservableCollection<ISeries>
            {
                new LineSeries<double>
                {
                    Values = _downValues,
                    GeometrySize = 0,
                    Stroke = new SolidColorPaint(new SKColor(0, 255, 255), 2),
                    Fill = new SolidColorPaint(new SKColor(0, 255, 255, 30)),
                    LineSmoothness = 0,
                    IsVisibleAtLegend = false,
                    Name = "Download"
                }
            };

            UploadSeries = new ObservableCollection<ISeries>
            {
                new LineSeries<double>
                {
                    Values = _upValues,
                    GeometrySize = 0,
                    Stroke = new SolidColorPaint(new SKColor(255, 100, 100), 2),
                    Fill = new SolidColorPaint(new SKColor(255, 100, 100, 25)),
                    LineSmoothness = 0,
                    IsVisibleAtLegend = false,
                    Name = "Upload"
                }
            };

            for (int i = 0; i < 10; i++)
            {
                _downValues.Add(0);
                _upValues.Add(0);
            }

            InitializeSampleState();
            ScheduleInfoRefresh();

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

        private void InitializeSampleState()
        {
            try
            {
                var nic = ResolveNic();
                if (nic == null) return;

                var stats = nic.GetIPv4Statistics();
                _lastRx = stats.BytesReceived;
                _lastTx = stats.BytesSent;
                _lastSampleUtc = DateTime.UtcNow;
            }
            catch
            {
            }
        }

        private void Tick()
        {
            if (!string.Equals(AppViewStateService.CurrentView, "Graphs", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                var nic = ResolveNic();
                if (nic == null)
                {
                    Append(_downValues, 0);
                    Append(_upValues, 0);
                    CurrentDownloadText = "0 Mbps";
                    CurrentUploadText = "0 Mbps";
                    return;
                }

                var now = DateTime.UtcNow;
                var dt = (now - _lastSampleUtc).TotalSeconds;
                if (dt <= 0) dt = 0.5;

                var stats = nic.GetIPv4Statistics();
                var rx = stats.BytesReceived;
                var tx = stats.BytesSent;

                var rxBps = (rx - _lastRx) / dt;
                var txBps = (tx - _lastTx) / dt;

                _lastRx = rx;
                _lastTx = tx;
                _lastSampleUtc = now;

                var rxMbps = (rxBps * 8.0) / 1_000_000.0;
                var txMbps = (txBps * 8.0) / 1_000_000.0;

                if (rxMbps < 0) rxMbps = 0;
                if (txMbps < 0) txMbps = 0;

                CurrentDownloadText = $"{rxMbps:F1} Mbps";
                CurrentUploadText = $"{txMbps:F1} Mbps";

                Append(_downValues, rxMbps);
                Append(_upValues, txMbps);

                ScheduleInfoRefresh();
            }
            catch
            {
            }
        }

        private NetworkInterface? ResolveNic()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_nicId))
                    return NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault();

                return NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n => string.Equals(n.Id, _nicId, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
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

        private void ScheduleInfoRefresh()
        {
            if ((DateTime.UtcNow - _lastInfoUtc) < TimeSpan.FromSeconds(10)) return;
            if (Interlocked.Exchange(ref _infoTaskRunning, 1) == 1) return;

            _lastInfoUtc = DateTime.UtcNow;
            _ = Task.Run(() =>
            {
                try
                {
                    RefreshInfo();
                }
                catch
                {
                }
                finally
                {
                    Interlocked.Exchange(ref _infoTaskRunning, 0);
                }
            });
        }

        private void RefreshInfo()
        {
            var nic = ResolveNic();
            if (nic == null) return;

            AdapterName = nic.Name;
            Description = nic.Description;
            Status = nic.OperationalStatus.ToString();

            try
            {
                var mac = nic.GetPhysicalAddress()?.ToString();
                if (!string.IsNullOrWhiteSpace(mac))
                    MacAddress = string.Join(":", Enumerable.Range(0, mac.Length / 2).Select(i => mac.Substring(i * 2, 2)));
            }
            catch
            {
            }

            try
            {
                LinkSpeedText = nic.Speed > 0 ? $"{nic.Speed / 1_000_000} Mbps" : "N/A";
            }
            catch
            {
            }

            try
            {
                var props = nic.GetIPProperties();

                var ipv4 = props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString())
                    .ToList();
                Ipv4Text = ipv4.Count > 0 ? string.Join(", ", ipv4) : "N/A";

                var ipv6 = props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                    .Select(a => a.Address.ToString())
                    .ToList();
                Ipv6Text = ipv6.Count > 0 ? string.Join(", ", ipv6) : "N/A";

                var gw = props.GatewayAddresses.Select(g => g.Address.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                GatewayText = gw.Count > 0 ? string.Join(", ", gw) : "N/A";

                var dns = props.DnsAddresses.Select(d => d.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                DnsText = dns.Count > 0 ? string.Join(", ", dns) : "N/A";

                try
                {
                    var isDhcp = props.GetIPv4Properties()?.IsDhcpEnabled;
                    DhcpText = isDhcp.HasValue ? (isDhcp.Value ? "Enabled" : "Disabled") : "N/A";
                }
                catch
                {
                    DhcpText = "N/A";
                }
            }
            catch
            {
            }
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

            GC.SuppressFinalize(this);
        }
    }
}
