using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace TaskCore.Views
{
    public partial class GraphsView : UserControl, IActivatableView, IDisposable
    {
        private Graphs.CpuGraphView? _cpuView;
        private Graphs.MemoryGraphView? _memoryView;
        private Graphs.GpuGraphView? _gpuView;
        private Graphs.DiskGraphView? _diskView;
        private Graphs.NetworkGraphView? _networkView;
        private bool _disposed;

        private static void Log(string msg)
        {
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TaskCore", "Logs");
                Directory.CreateDirectory(logDir);
                var logFile = Path.Combine(logDir, "graphsview.txt");
                File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
            }
            catch { }
        }

        public GraphsView()
        {
            Log("Constructor BEGIN");
            try
            {
                InitializeComponent();
                Log("InitializeComponent complete");

                // default view - lazy init with error handling
                try
                {
                    _cpuView = new Graphs.CpuGraphView();
                    GraphContent.Content = _cpuView;
                    Log("CPU view created and set as default");
                }
                catch (Exception ex)
                {
                    Log($"ERROR creating CPU view: {ex.GetType().Name}: {ex.Message}");
                    GraphContent.Content = new TextBlock { Text = "Error loading CPU graph", Foreground = System.Windows.Media.Brushes.Red };
                }
            }
            catch (Exception ex)
            {
                Log($"FATAL in constructor: {ex.GetType().Name}: {ex.Message}");
                Log($"StackTrace: {ex.StackTrace}");
            }
            Log("Constructor END");
        }

        public void OnActivated()
        {
            Log("OnActivated");
            if (GraphContent.Content is IActivatableView activatable)
            {
                try { activatable.OnActivated(); } catch (Exception ex) { Log($"OnActivated error: {ex.Message}"); }
            }
        }

        public void OnDeactivated()
        {
            Log("OnDeactivated");
            if (GraphContent.Content is IActivatableView activatable)
            {
                try { activatable.OnDeactivated(); } catch (Exception ex) { Log($"OnDeactivated error: {ex.Message}"); }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

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
            try
            {
                _cpuView ??= new Graphs.CpuGraphView();
                GraphContent.Content = _cpuView;
                if (_cpuView is IActivatableView av) av.OnActivated();
            }
            catch (Exception ex)
            {
                Log($"ERROR switching to CPU: {ex.Message}");
            }
        }

        private void Memory_Checked(object sender, RoutedEventArgs e)
        {
            if (GraphContent == null) return;
            UncheckOthers(sender);
            try
            {
                _memoryView ??= new Graphs.MemoryGraphView();
                GraphContent.Content = _memoryView;
                if (_memoryView is IActivatableView av) av.OnActivated();
            }
            catch (Exception ex)
            {
                Log($"ERROR switching to Memory: {ex.Message}");
            }
        }

        private void Gpu_Checked(object sender, RoutedEventArgs e)
        {
            Log("Gpu_Checked BEGIN");
            if (GraphContent == null) return;
            UncheckOthers(sender);
            try
            {
                _gpuView ??= new Graphs.GpuGraphView();
                Log("GpuGraphView created");
                GraphContent.Content = _gpuView;
                Log("Content set to GpuGraphView");
                if (_gpuView is IActivatableView av)
                {
                    Log("Calling OnActivated");
                    av.OnActivated();
                }
            }
            catch (Exception ex)
            {
                Log($"ERROR switching to GPU: {ex.GetType().Name}: {ex.Message}");
                Log($"StackTrace: {ex.StackTrace}");
                GraphContent.Content = new TextBlock { Text = $"GPU Graph Error: {ex.Message}", Foreground = System.Windows.Media.Brushes.Red, TextWrapping = TextWrapping.Wrap };
            }
            Log("Gpu_Checked END");
        }

        private void Disk_Checked(object sender, RoutedEventArgs e)
        {
            if (GraphContent == null) return;
            UncheckOthers(sender);
            try
            {
                _diskView ??= new Graphs.DiskGraphView();
                GraphContent.Content = _diskView;
                if (_diskView is IActivatableView av) av.OnActivated();
            }
            catch (Exception ex)
            {
                Log($"ERROR switching to Disk: {ex.Message}");
            }
        }

        private void Network_Checked(object sender, RoutedEventArgs e)
        {
            if (GraphContent == null) return;
            UncheckOthers(sender);
            try
            {
                _networkView ??= new Graphs.NetworkGraphView();
                GraphContent.Content = _networkView;
                if (_networkView is IActivatableView av) av.OnActivated();
            }
            catch (Exception ex)
            {
                Log($"ERROR switching to Network: {ex.Message}");
            }
        }

        private void UncheckOthers(object? sender)
        {
            var current = sender as System.Windows.Controls.Primitives.ToggleButton;
            if (current == null) return;
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
