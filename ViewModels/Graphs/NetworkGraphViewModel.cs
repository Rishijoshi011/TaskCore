using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.NetworkInformation;

namespace TaskCore.ViewModels.Graphs
{
    public partial class NetworkGraphViewModel : ObservableObject, IDisposable
    {
        [ObservableProperty]
        private ObservableCollection<NetworkAdapterViewModel> adapters;

        public NetworkGraphViewModel()
        {
            Adapters = new ObservableCollection<NetworkAdapterViewModel>();
            InitializeAdapters();
        }

        public void Dispose()
        {
            foreach (var adapter in Adapters)
            {
                try { adapter?.Dispose(); } catch { }
            }
            Adapters.Clear();
        }

        private void InitializeAdapters()
        {
            try
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                             && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                             && n.OperationalStatus == OperationalStatus.Up)
                    .OrderByDescending(n => n.Speed)
                    .ToList();

                foreach (var nic in nics)
                {
                    Adapters.Add(new NetworkAdapterViewModel(nic.Id, nic.Name));
                }

                if (Adapters.Count == 0)
                {
                    // Fallback to all non-loopback network interfaces if none are active/up
                    var fallbackNics = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                        .ToList();

                    foreach (var nic in fallbackNics)
                    {
                        Adapters.Add(new NetworkAdapterViewModel(nic.Id, nic.Name));
                    }
                }

                if (Adapters.Count == 0)
                    Adapters.Add(new NetworkAdapterViewModel(null, null));
            }
            catch
            {
                try
                {
                    Adapters.Clear();
                    Adapters.Add(new NetworkAdapterViewModel(null, null));
                }
                catch
                {
                }
            }
        }
    }
}
