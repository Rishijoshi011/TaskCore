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
        private readonly bool _watchdogMode;

        // Heavy views - only initialized in normal mode
        private readonly DashboardView? _dashboardView;
        private readonly ProcessesView? _processesView;
        private readonly GraphsView? _graphsView;
        private readonly DetailsView? _detailsView;

        // Light views - always available
        private readonly RulesView _rulesView;
        private readonly SettingsView _settingsView;

        public MainWindow()
        {
            InitializeComponent();

            // Load settings to determine mode
            var settings = AppSettingsService.Load();
            _watchdogMode = settings.WatchdogMode;

            // Initialize views based on mode
            if (!_watchdogMode)
            {
                _dashboardView = new DashboardView();
                _processesView = new ProcessesView();
                _graphsView = new GraphsView();
                _detailsView = new DetailsView();
            }

            _rulesView = new RulesView();
            _settingsView = new SettingsView();

            // Configure sidebar based on mode
            ConfigureSidebarForMode();

            // Set default page
            if (_watchdogMode)
            {
                // In watchdog mode, default to Rules (index 0 in reduced sidebar)
                Sidebar.SelectedIndex = 0;
                ContentArea.Content = _rulesView;
                AppViewStateService.SetView("Rules");
            }
            else
            {
                Sidebar.SelectedIndex = 0;
                ContentArea.Content = _dashboardView;
                AppViewStateService.SetView("Dashboard");
            }
        }

        private void ConfigureSidebarForMode()
        {
            // Clear existing items
            Sidebar.Items.Clear();

            if (_watchdogMode)
            {
                // Watchdog Mode: Only Rules and Settings
                AddSidebarItem("Rules", "pack://application:,,,/TaskCore;component/Resources/Icons/Rules.png");
                AddSidebarItem("Settings", "pack://application:,,,/TaskCore;component/Resources/Icons/Settings.png");
            }
            else
            {
                // Normal Mode: All tabs
                AddSidebarItem("Dashboard", "pack://application:,,,/TaskCore;component/Resources/Icons/Dashboard.png");
                AddSidebarItem("Processes", "pack://application:,,,/TaskCore;component/Resources/Icons/Processes.png");
                AddSidebarItem("Graphs", "pack://application:,,,/TaskCore;component/Resources/Icons/Graphs.png");
                AddSidebarItem("Session Tracker", "pack://application:,,,/TaskCore;component/Resources/Icons/Details.png");
                AddSidebarItem("Rules", "pack://application:,,,/TaskCore;component/Resources/Icons/Rules.png");
                AddSidebarItem("Settings", "pack://application:,,,/TaskCore;component/Resources/Icons/Settings.png");
            }
        }

        private void AddSidebarItem(string text, string iconPath)
        {
            var item = new ListBoxItem();
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconPath)), Width = 20, Height = 20, Margin = new Thickness(0, 0, 14, 0) });
            panel.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.Medium, FontSize = 15 });
            item.Content = panel;
            Sidebar.Items.Add(item);
        }

        private void Sidebar_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_watchdogMode)
            {
                // Watchdog Mode: Only 2 tabs (Rules, Settings)
                string viewName = Sidebar.SelectedIndex switch
                {
                    0 => "Rules",
                    1 => "Settings",
                    _ => "Rules"
                };

                ContentArea.Content = viewName switch
                {
                    "Rules" => _rulesView,
                    "Settings" => _settingsView,
                    _ => _rulesView
                };

                AppViewStateService.SetView(viewName);
            }
            else
            {
                // Normal Mode: All 6 tabs
                string viewName = Sidebar.SelectedIndex switch
                {
                    0 => "Dashboard",
                    1 => "Processes",
                    2 => "Graphs",
                    3 => "Details",
                    4 => "Rules",
                    5 => "Settings",
                    _ => "Dashboard"
                };

                ContentArea.Content = viewName switch
                {
                    "Dashboard" => _dashboardView,
                    "Processes" => _processesView,
                    "Graphs" => _graphsView,
                    "Details" => _detailsView,
                    "Rules" => _rulesView,
                    "Settings" => _settingsView,
                    _ => _dashboardView
                };

                AppViewStateService.SetView(viewName);
            }
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
            var settings = AppSettingsService.Load();
            if (settings.MinimizeToTray)
            {
                // Minimize to tray instead of closing
                Hide();
            }
            else
            {
                // Exit the application
                Application.Current.Shutdown();
            }
        }
    }
}
