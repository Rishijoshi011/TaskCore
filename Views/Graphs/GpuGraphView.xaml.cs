using System;
using System.IO;
using System.Windows.Controls;
using TaskCore.Services;
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

        private static void Log(string msg) => TaskCoreLogger.Info("gpu-graph", msg);

        public void OnActivated()
        {
            Log("OnActivated BEGIN");
            try
            {
                if (DataContext is not GpuGraphViewModel)
                {
                    Log("Creating new GpuGraphViewModel");
                    DataContext = new GpuGraphViewModel();
                    Log("GpuGraphViewModel created successfully");
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
                TaskCoreLogger.Error("gpu-graph", $"Activation error: {ex}");
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
