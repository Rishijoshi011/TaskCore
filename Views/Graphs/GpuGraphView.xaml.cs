using System;
using System.Diagnostics;
using System.IO;
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

        private static void Log(string msg)
        {
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TaskCore", "Logs");
                Directory.CreateDirectory(logDir);
                var logFile = Path.Combine(logDir, "gpugraph.txt");
                File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
            }
            catch { }
        }

        public void OnActivated()
        {
            Log("OnActivated BEGIN");
            try
            {
                if (DataContext is not GpuAdapterViewModel)
                {
                    Log("Creating new GpuAdapterViewModel");
                    DataContext = new GpuAdapterViewModel(null, null);
                    Log("GpuAdapterViewModel created successfully");
                }
                else
                {
                    Log("DataContext already set");
                }
            }
            catch (Exception ex)
            {
                Log($"ERROR in OnActivated: {ex.GetType().Name}: {ex.Message}");
                Log($"StackTrace: {ex.StackTrace}");
                Debug.WriteLine($"[GpuGraphView] Activation error: {ex}");
            }
            Log("OnActivated END");
        }

        public void OnDeactivated()
        {
            Log("OnDeactivated");
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
