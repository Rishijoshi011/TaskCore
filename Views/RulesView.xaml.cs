using System.Windows.Controls;
using TaskCore.ViewModels;

namespace TaskCore.Views
{
    public partial class RulesView : UserControl
    {
        public RulesView()
        {
            InitializeComponent();
            DataContext = new RulesViewModel();
        }
    }
}
