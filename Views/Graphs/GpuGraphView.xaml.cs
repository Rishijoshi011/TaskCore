using System;
using System.Windows.Controls;
using TaskCore.Views;
using TaskCore.ViewModels.Graphs;

namespace TaskCore.Views.Graphs
{
    public partial class GpuGraphView : UserControl, IActivatableView, IDisposable
    {
        private bool _disposed;

        public GpuGraphView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            if (DataContext is not GpuAdapterViewModel)
            {
                DataContext = new GpuAdapterViewModel(null, null);
            }
        }

        public void OnDeactivated()
        {
            if (DataContext is IDisposable disposable)
            {
                try { disposable.Dispose(); } catch { }
            }
            DataContext = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            OnDeactivated();
            GC.SuppressFinalize(this);
        }
    }
}
