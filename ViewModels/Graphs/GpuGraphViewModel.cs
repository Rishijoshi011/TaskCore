using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace TaskCore.ViewModels.Graphs
{
    public partial class GpuGraphViewModel : ObservableObject, IDisposable
    {
        [ObservableProperty]
        private ObservableCollection<GpuAdapterViewModel> gpus;

        public GpuGraphViewModel()
        {
            Gpus = new ObservableCollection<GpuAdapterViewModel>();
            InitializeAdapters();
        }

        public void Dispose()
        {
            foreach (var gpu in Gpus)
            {
                try { gpu?.Dispose(); } catch { }
            }
            Gpus.Clear();
        }

        private void InitializeAdapters()
        {
            try
            {
                string[] instances;
                try
                {
                    instances = new PerformanceCounterCategory("GPU Adapter Memory").GetInstanceNames();
                }
                catch
                {
                    instances = Array.Empty<string>();
                }

                if (instances.Length == 0)
                {
                    Gpus.Add(new GpuAdapterViewModel(adapterMemoryInstance: null, gpuNameHint: null, wmiIndex: 0));
                    return;
                }

                int idx = 0;
                foreach (var inst in instances)
                {
                    if (string.IsNullOrWhiteSpace(inst)) continue;

                    var vm = new GpuAdapterViewModel(adapterMemoryInstance: inst, gpuNameHint: null, wmiIndex: idx);
                    Gpus.Add(vm);
                    idx++;
                }
            }
            catch
            {
                try
                {
                    Gpus.Clear();
                    Gpus.Add(new GpuAdapterViewModel(adapterMemoryInstance: null, gpuNameHint: null));
                }
                catch
                {
                }
            }
        }
    }
}
