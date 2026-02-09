using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskCore.Models;
using TaskCore.ViewModels;

namespace TaskCore.Views
{
    public partial class ProcessesView : UserControl
    {
        public ProcessesView()
        {
            InitializeComponent();
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
                    vm.SelectedProcess = item;
            }
        }

    }
}
