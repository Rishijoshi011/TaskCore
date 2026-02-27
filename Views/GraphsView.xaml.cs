using System;
using System.Windows;
using System.Windows.Controls;

namespace TaskCore.Views
{
    public partial class GraphsView : UserControl, IActivatableView, IDisposable
    {
        private readonly Graphs.CpuGraphView _cpuView = new();
        private readonly Graphs.MemoryGraphView _memoryView = new();
        private readonly Graphs.GpuGraphView _gpuView = new();
        private readonly Graphs.DiskGraphView _diskView = new();
        private readonly Graphs.NetworkGraphView _networkView = new();
        private bool _disposed;

        public GraphsView()
        {
            InitializeComponent();

            // default view
            GraphContent.Content = _cpuView;
        }

        public void OnActivated()
        {
            if (GraphContent.Content is IActivatableView activatable)
                activatable.OnActivated();
        }

        public void OnDeactivated()
        {
            if (GraphContent.Content is IActivatableView activatable)
                activatable.OnDeactivated();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Dispose all child views
            DisposeView(_cpuView);
            DisposeView(_memoryView);
            DisposeView(_gpuView);
            DisposeView(_diskView);
            DisposeView(_networkView);

            GC.SuppressFinalize(this);
        }

        private static void DisposeView(object? view)
        {
            if (view is IDisposable disposable)
            {
                try { disposable.Dispose(); } catch { }
            }
        }

        private void Cpu_Checked(object sender, RoutedEventArgs e)
        {
            if (GraphContent == null) return;
            UncheckOthers(sender);
            GraphContent.Content = _cpuView;
        }

        private void Memory_Checked(object sender, RoutedEventArgs e)
        {
            if (GraphContent == null) return;
            UncheckOthers(sender);
            GraphContent.Content = _memoryView;
        }

        private void Gpu_Checked(object sender, RoutedEventArgs e)
        {
            if (GraphContent == null) return;
            UncheckOthers(sender);
            GraphContent.Content = _gpuView;
        }

        private void Disk_Checked(object sender, RoutedEventArgs e)
        {
            if (GraphContent == null) return;
            UncheckOthers(sender);
            GraphContent.Content = _diskView;
        }

        private void Network_Checked(object sender, RoutedEventArgs e)
        {
            if (GraphContent == null) return;
            UncheckOthers(sender);
            GraphContent.Content = _networkView;
        }

        private void UncheckOthers(object? sender)
        {
            var current = sender as System.Windows.Controls.Primitives.ToggleButton;
            if (current == null) return;
            // Find parent StackPanel
            if (current.Parent is StackPanel parent)
            {
                foreach (var child in parent.Children)
                {
                    if (child is System.Windows.Controls.Primitives.ToggleButton tb && tb != current)
                    {
                        tb.IsChecked = false;
                    }
                }
            }
        }
    }
}
