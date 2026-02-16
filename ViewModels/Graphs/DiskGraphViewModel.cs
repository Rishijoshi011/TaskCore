using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace TaskCore.ViewModels.Graphs
{
    public partial class DiskGraphViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<DiskAdapterViewModel> disks;

        public DiskGraphViewModel()
        {
            Disks = new ObservableCollection<DiskAdapterViewModel>();
            InitializeDisks();
        }

        private void InitializeDisks()
        {
            try
            {
                string[] instances;
                try
                {
                    instances = new PerformanceCounterCategory("PhysicalDisk").GetInstanceNames();
                }
                catch
                {
                    instances = Array.Empty<string>();
                }

                foreach (var inst in instances)
                {
                    if (string.IsNullOrWhiteSpace(inst)) continue;
                    if (string.Equals(inst, "_Total", StringComparison.OrdinalIgnoreCase)) continue;

                    Disks.Add(new DiskAdapterViewModel(inst));
                }

                if (Disks.Count == 0)
                    Disks.Add(new DiskAdapterViewModel(null));
            }
            catch
            {
                try
                {
                    Disks.Clear();
                    Disks.Add(new DiskAdapterViewModel(null));
                }
                catch
                {
                }
            }
        }
    }
}
