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
            System.Diagnostics.Debug.WriteLine("[ProcessesView] ProcessRow_Click fired!");
            if (sender is FrameworkElement fe && fe.DataContext is ProcessItem item)
            {
                System.Diagnostics.Debug.WriteLine($"[ProcessesView] Row clicked: {item.Name} PID={item.PID}");
                if (DataContext is ProcessesViewModel vm)
                {
                    vm.SelectedProcess = item;
                    System.Diagnostics.Debug.WriteLine($"[ProcessesView] SelectedProcess set to: {vm.SelectedProcess?.Name}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ProcessesView] Row click: sender={sender?.GetType().Name}, DataContext={((sender as FrameworkElement)?.DataContext)?.GetType().Name}");
            }
        }

    }
}
