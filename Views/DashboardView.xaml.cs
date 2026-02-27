using System;
using System.Windows.Controls;
using TaskCore.ViewModels;

namespace TaskCore.Views
{
    public partial class DashboardView : UserControl, IActivatableView, IDisposable
    {
        private DashboardViewModel? _viewModel;
        private bool _disposed;

        public DashboardView()
        {
            InitializeComponent();
            EnsureViewModel();
        }

        private void EnsureViewModel()
        {
            if (_viewModel == null)
            {
                _viewModel = new DashboardViewModel();
                DataContext = _viewModel;
            }
        }

        public void OnActivated()
        {
            EnsureViewModel();
            _viewModel?.ResumeRefresh();
        }

        public void OnDeactivated()
        {
            _viewModel?.PauseRefresh();
            // Dispose and clear to free memory
            if (_viewModel is IDisposable disposable)
            {
                try { disposable.Dispose(); } catch { }
            }
            _viewModel = null;
            DataContext = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_viewModel is IDisposable disposable)
            {
                disposable.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }
}
