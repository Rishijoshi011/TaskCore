using System;
using System.Windows.Controls;
using TaskCore.Views;
using TaskCore.ViewModels.Graphs;

namespace TaskCore.Views.Graphs
{
    public partial class MemoryGraphView : UserControl, IActivatableView, IDisposable
    {
        private bool _disposed;

        public MemoryGraphView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            if (DataContext is not MemoryViewModel)
            {
                DataContext = new MemoryViewModel();
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
