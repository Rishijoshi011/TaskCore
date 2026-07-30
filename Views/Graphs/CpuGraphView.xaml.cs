using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using TaskCore.Views;
using TaskCore.ViewModels.Graphs;

namespace TaskCore.Views.Graphs
{
    /// <summary>
    /// Interaction logic for CpuGraphView.xaml
    /// </summary>
    public partial class CpuGraphView : UserControl, IActivatableView, IDisposable
    {
        private bool _disposed;

        public CpuGraphView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            // Recreate ViewModel if needed
            if (DataContext is not CpuGraphViewModel)
            {
                DataContext = new CpuGraphViewModel();
            }
        }

        public void OnDeactivated()
        {
            // Do NOT dispose - keep ViewModel cached to maintain PerformanceCounter state and historical data
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
