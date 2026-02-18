using System.Windows;

namespace TaskCore.Helpers
{
    // Freezable proxy to allow binding to DataContext from objects that are not in the visual/logical tree (e.g., DataGridColumn)
    public class BindingProxy : Freezable
    {
        protected override Freezable CreateInstanceCore() => new BindingProxy();

        public object Data
        {
            get { return GetValue(DataProperty); }
            set { SetValue(DataProperty, value); }
        }

        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
            nameof(Data), typeof(object), typeof(BindingProxy), new PropertyMetadata(null));
    }
}
