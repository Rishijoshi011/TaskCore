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

        private void Cpu_Click(object sender, System.Windows.RoutedEventArgs e)
            => GraphContent.Content = new Graphs.CpuGraphView();

        private void Memory_Click(object sender, System.Windows.RoutedEventArgs e)
            => GraphContent.Content = new Graphs.MemoryGraphView();

        private void Gpu_Click(object sender, System.Windows.RoutedEventArgs e)
            => GraphContent.Content = new Graphs.GpuGraphView();

        private void Disk_Click(object sender, System.Windows.RoutedEventArgs e)
            => GraphContent.Content = new Graphs.DiskGraphView();

        private void Network_Click(object sender, System.Windows.RoutedEventArgs e)
            => GraphContent.Content = new Graphs.NetworkGraphView();
    }
}
