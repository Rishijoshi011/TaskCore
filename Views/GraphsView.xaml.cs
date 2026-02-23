using System.Windows;
using System.Windows.Controls;

namespace TaskCore.Views
{
    public partial class GraphsView : UserControl
    {
        public GraphsView()
        {
            InitializeComponent();

            // default view
            GraphContent.Content = new Graphs.CpuGraphView();
        }

        private void Cpu_Checked(object sender, RoutedEventArgs e)
        {
            if (GraphContent == null) return;
            UncheckOthers(sender);
            GraphContent.Content = new Graphs.CpuGraphView();
        }

        private void Memory_Checked(object sender, RoutedEventArgs e)
        {
            if (GraphContent == null) return;
            UncheckOthers(sender);
            GraphContent.Content = new Graphs.MemoryGraphView();
        }

        private void Gpu_Checked(object sender, RoutedEventArgs e)
        {
            if (GraphContent == null) return;
            UncheckOthers(sender);
            GraphContent.Content = new Graphs.GpuGraphView();
        }

        private void Disk_Checked(object sender, RoutedEventArgs e)
        {
            if (GraphContent == null) return;
            UncheckOthers(sender);
            GraphContent.Content = new Graphs.DiskGraphView();
        }

        private void Network_Checked(object sender, RoutedEventArgs e)
        {
            if (GraphContent == null) return;
            UncheckOthers(sender);
            GraphContent.Content = new Graphs.NetworkGraphView();
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
