using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.NetworkInformation;

namespace TaskCore.ViewModels.Graphs
{
    public partial class NetworkGraphViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<NetworkAdapterViewModel> adapters;

        public NetworkGraphViewModel()
        {
            Adapters = new ObservableCollection<NetworkAdapterViewModel>();
            InitializeAdapters();
        }

        private void InitializeAdapters()
        {
            try
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .ToList();

                foreach (var nic in nics)
                {
                    Adapters.Add(new NetworkAdapterViewModel(nic.Id, nic.Name));
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
