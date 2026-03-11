using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskCore.Models;
using TaskCore.ViewModels;

namespace TaskCore.Views
{
    public partial class ProcessesView : UserControl, IActivatableView
    {
        public ProcessesView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            // ViewModel is created via XAML DataContext - recreate if disposed
            if (DataContext == null)
            {
                DataContext = new ProcessesViewModel();
            }
        }

        public void OnDeactivated()
        {
            // Dispose ViewModel to free resources
            if (DataContext is IDisposable vm)
            {
                DataContext = null;
                vm.Dispose();
            }
        }

        // Expand / collapse process group
        private void GroupHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ProcessGroup group)
                group.IsExpanded = !group.IsExpanded;
        }

        // Handle row selection
        private void ProcessRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ProcessItem item)
            {
                if (DataContext is ProcessesViewModel vm)
                {
                    vm.SelectedProcess = item;
                }
            }
        }
    }
}
