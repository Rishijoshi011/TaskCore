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
            // Do NOT dispose - keep ViewModel cached to maintain PerformanceCounter state and historical data
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
