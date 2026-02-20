using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using TaskCore.Views;
using TaskCore.Services;

namespace TaskCore
{
    public partial class MainWindow : Window
    {
        private bool _isSidebarCollapsed = false;

        private readonly ProcessesView _processesView = new ProcessesView();
        private readonly GraphsView _graphsView = new GraphsView();
        private readonly DetailsView _detailsView = new DetailsView();
        private readonly SettingsView _settingsView = new SettingsView();

        public MainWindow()
        {
            InitializeComponent();

            // Default page
            Sidebar.SelectedIndex = 0;
            ContentArea.Content = _processesView;
            AppViewStateService.SetView("Processes");
        }

        private void Sidebar_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            string viewName = Sidebar.SelectedIndex switch
            {
                0 => "Processes",
                1 => "Graphs",
                2 => "Details",
                3 => "Settings",
                _ => "Unknown"
            };

            switch (viewName)
            {
                case "Processes": ContentArea.Content = _processesView; break;
                case "Graphs": ContentArea.Content = _graphsView; break;
                case "Details": ContentArea.Content = _detailsView; break;
                case "Settings": ContentArea.Content = _settingsView; break;
            }

            AppViewStateService.SetView(viewName);
        }

        private void ToggleSidebar(object sender, RoutedEventArgs e)
        {
            if (_isSidebarCollapsed)
            {
                // Expand sidebar
                var sb = TryFindResource("SidebarExpandStoryboard") as Storyboard;
                if (sb != null)
                {
                    sb.Begin();
                }
                else
                {
                    // Fallback to instant expand
                    SidebarColumn.Width = new GridLength(220);
                    SidebarBorder.Visibility = Visibility.Visible;
                }
                _isSidebarCollapsed = false;
            }
            else
            {
                // Collapse sidebar
                var sb = TryFindResource("SidebarCollapseStoryboard") as Storyboard;
                if (sb != null)
                {
                    sb.Begin();
                }
                else
                {
                    // Fallback to instant collapse
                    SidebarColumn.Width = new GridLength(0);
                    SidebarBorder.Visibility = Visibility.Collapsed;
                }
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
