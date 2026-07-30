using System.Windows.Controls;
using TaskCore.ViewModels;

namespace TaskCore.Views
{
    public partial class RulesView : UserControl, IActivatableView
    {
        public RulesView()
        {
            InitializeComponent();
        }

        public void OnActivated()
        {
            // Recreate ViewModel if it was disposed
            if (DataContext == null)
            {
                DataContext = new RulesViewModel();
            }
        }

        public void OnDeactivated()
        {
            // Do NOT dispose - keep ViewModel cached for fast tab switching
        }
    }
}
