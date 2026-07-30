using System;
using System.Windows.Controls;
using TaskCore.ViewModels.Graphs;

namespace TaskCore.Views.Graphs
{
    public partial class SystemSensorsView : UserControl, IActivatableView, IDisposable
    {
        private bool _disposed;

        public SystemSensorsView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            if (DataContext is not SystemSensorsViewModel)
            {
                DataContext = new SystemSensorsViewModel();
            }
        }

        public void OnDeactivated()
        {
            // Keep DataContext cached for instant tab switching without lost min/max history
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (DataContext is IDisposable disposable)
            {
                try { disposable.Dispose(); } catch { }
            }

            GC.SuppressFinalize(this);
        }
    }
}
