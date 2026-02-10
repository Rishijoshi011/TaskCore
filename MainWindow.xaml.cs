using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using TaskCore.Views;

namespace TaskCore
{
    public partial class MainWindow : Window
    {
        private bool _isSidebarCollapsed = false;

        public MainWindow()
        {
            InitializeComponent();

            // Default page
            Sidebar.SelectedIndex = 0;
            ContentArea.Content = new ProcessesView();
        }

        private void Sidebar_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            switch (Sidebar.SelectedIndex)
            {
                case 0:
                    ContentArea.Content = new ProcessesView();
                    break;
                case 1:
                    ContentArea.Content = new GraphsView();
                    break;
                case 2:
                    ContentArea.Content = new DetailsView();
                    break;
                case 3:
                    ContentArea.Content = new SettingsView();
                    break;
            }
        }

        private void ToggleSidebar(object sender, RoutedEventArgs e)
        {
            if (_isSidebarCollapsed)
            {
                // Expand sidebar
                SidebarColumn.Width = new GridLength(220);
                SidebarBorder.Visibility = Visibility.Visible;
                _isSidebarCollapsed = false;
            }
            else
            {
                // Collapse sidebar
                SidebarColumn.Width = new GridLength(0);
                SidebarBorder.Visibility = Visibility.Collapsed;
                _isSidebarCollapsed = true;
            }
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
