using System.Windows.Controls;
using TaskCore.ViewModels;

namespace TaskCore.Views
{
    public partial class DashboardView : UserControl, IActivatableView
    {
        private readonly DashboardViewModel _viewModel;

        public DashboardView()
        {
            InitializeComponent();
            _viewModel = new DashboardViewModel();
            DataContext = _viewModel;
        }

        public void OnActivated()
        {
            _viewModel?.ResumeRefresh();
        }

        public void OnDeactivated()
        {
            _viewModel?.PauseRefresh();
        }
    }
}
