using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using TaskCore.Views;
using TaskCore.Services;
using System.Windows.Media.Imaging;

namespace TaskCore
{
    public partial class MainWindow : Window
    {
        // Track history for back/forward navigation with mouse side buttons
        private readonly List<string> _viewHistory = new();
        private int _historyIndex = -1;
        private bool _isNavigating = false;

        // Window state
        private bool _isSidebarCollapsed = false;
        private readonly bool _watchdogMode;
        private FrameworkElement? _currentView;

        // Heavy views - lazily initialized
        private DashboardView? _dashboardView;
        private ProcessesView? _processesView;
        private GraphsView? _graphsView;
        private DetailsView? _detailsView;

        // Light views - always available
        private RulesView? _rulesView;
        private SettingsView? _settingsView;

        // Windows shutdown notification constants
        private const int WM_QUERYENDSESSION = 0x0011;
        private const int WM_ENDSESSION = 0x0016;
        private const uint ENDSESSION_CLOSEAPP = 0x00000001;
        private const uint ENDSESSION_CRITICAL = 0x40000000;
        private const uint ENDSESSION_LOGOFF = 0x80000000;

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool ShutdownBlockReasonCreate(IntPtr hWnd, [MarshalAs(UnmanagedType.LPWStr)] string pwszReason);

        [DllImport("user32.dll")]
        private static extern bool ShutdownBlockReasonDestroy(IntPtr hWnd);

        private bool _shutdownDetected = false;

        public MainWindow()
        {
            InitializeComponent();

            // Load settings to determine mode
            var settings = AppSettingsService.Load();
            _watchdogMode = settings.WatchdogMode;

            // Initialize views based on mode
            if (!_watchdogMode)
            {
                // Views are lazily initialized - only Dashboard created initially
                _dashboardView = new DashboardView();
            }

            _rulesView = new RulesView();
            _settingsView = new SettingsView();

            // Check if running as administrator and show badge
            if (IsRunningAsAdmin())
            {
                AdminBadge.Visibility = Visibility.Visible;
            }

            // Handle window closing to dispose all resources
            Closing += OnWindowClosing;

            // Hook window messages for shutdown detection
            SourceInitialized += OnSourceInitialized;

            // Configure sidebar based on mode
            ConfigureSidebarForMode();

            // Set default page
            if (_watchdogMode)
            {
                // In watchdog mode, default to Rules (index 0 in reduced sidebar)
                Sidebar.SelectedIndex = 0;
                ContentArea.Content = _rulesView;
                AppViewStateService.SetView("Rules");
                AddToHistory("Rules");
            }
            else
            {
                Sidebar.SelectedIndex = 0;
                ContentArea.Content = _dashboardView;
                AppViewStateService.SetView("Dashboard");
                AddToHistory("Dashboard");
            }

            // Subscribe to mouse side button events for back/forward navigation
            PreviewMouseDown += OnPreviewMouseDown;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProcHook);
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_QUERYENDSESSION:
                    // Windows is asking if we can shut down - save sessions immediately
                    TaskCoreLogger.Info("shutdown", "WM_QUERYENDSESSION received - saving sessions");
                    _shutdownDetected = true;
                    try
                    {
                        // Block shutdown briefly to save data
                        ShutdownBlockReasonCreate(hwnd, "TaskCore is saving session data...");
                        // Save all active sessions synchronously
                        UsageTrackingService.Instance?.SaveRunningSessionsOnShutdown();
                        TaskCoreLogger.Info("shutdown", "Sessions saved for WM_QUERYENDSESSION");
                    }
                    catch (Exception ex)
                    {
                        TaskCoreLogger.Error("shutdown", $"Error saving on WM_QUERYENDSESSION: {ex.Message}");
                    }
                    finally
                    {
                        ShutdownBlockReasonDestroy(hwnd);
                    }
                    // Return TRUE to allow shutdown
                    handled = false;
                    return IntPtr.Zero;

                case WM_ENDSESSION:
                    // Windows is ending the session - save if not already saved
                    if (wParam.ToInt32() != 0 && !_shutdownDetected)
                    {
                        TaskCoreLogger.Info("shutdown", "WM_ENDSESSION received - emergency save");
                        try
                        {
                            UsageTrackingService.Instance?.SaveRunningSessionsOnShutdown();
                        }
                        catch { }
                    }
                    handled = false;
                    return IntPtr.Zero;
            }
            return IntPtr.Zero;
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
                AddSidebarItem("Session Tracker", "pack://application:,,,/TaskCore;component/Resources/Icons/Session.png");
                AddSidebarItem("Rules", "pack://application:,,,/TaskCore;component/Resources/Icons/Rules.png");
                AddSidebarItem("Settings", "pack://application:,,,/TaskCore;component/Resources/Icons/Settings.png");
            }
        }

        private void AddSidebarItem(string text, string iconPath)
        {
            var item = new ListBoxItem();
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new Image { Source = GetCachedIcon(iconPath), Width = 20, Height = 20, Margin = new Thickness(0, 0, 14, 0) });
            panel.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.Medium, FontSize = 15 });
            item.Content = panel;
            Sidebar.Items.Add(item);
        }

        private static readonly Dictionary<string, BitmapImage> _iconCache = new();

        private static BitmapImage GetCachedIcon(string iconPath)
        {
            if (!_iconCache.TryGetValue(iconPath, out var image))
            {
                image = new BitmapImage(new Uri(iconPath));
                image.Freeze(); // Make thread-safe and reduce memory
                _iconCache[iconPath] = image;
            }
            return image;
        }

        // View getters - create new each time to ensure proper disposal
        private DashboardView GetDashboardView()
        {
            DisposeView(_dashboardView);
            _dashboardView = null;
            return new DashboardView();
        }
        private ProcessesView GetProcessesView()
        {
            DisposeView(_processesView);
            _processesView = null;
            return new ProcessesView();
        }
        private GraphsView GetGraphsView()
        {
            DisposeView(_graphsView);
            _graphsView = null;
            return new GraphsView();
        }
        private DetailsView GetDetailsView()
        {
            DisposeView(_detailsView);
            _detailsView = null;
            return new DetailsView();
        }
        private RulesView GetRulesView()
        {
            DisposeView(_rulesView);
            _rulesView = null;
            return new RulesView();
        }
        private SettingsView GetSettingsView()
        {
            DisposeView(_settingsView);
            _settingsView = null;
            return new SettingsView();
        }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Dispose all cached views to free resources
            DisposeView(_dashboardView);
            DisposeView(_processesView);
            DisposeView(_graphsView);
            DisposeView(_detailsView);
            DisposeView(_rulesView);
            DisposeView(_settingsView);
        }

        private static void DisposeView(object? view)
        {
            if (view is IDisposable disposable)
            {
                try { disposable.Dispose(); } catch { }
            }
        }

        private void DisposeCurrentViewModel()
        {
            if (_currentView?.DataContext is IDisposable disposable)
            {
                try { disposable.Dispose(); } catch { }
                _currentView.DataContext = null;
            }
        }

        private void Sidebar_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Dispose current view's ViewModel when switching to free memory
            DisposeCurrentViewModel();

            // Deactivate current view before switching
            if (_currentView is IActivatableView activatable)
            {
                activatable.OnDeactivated();
            }

            if (_watchdogMode)
            {
                // Watchdog Mode: Only 2 tabs (Rules, Settings)
                string viewName = Sidebar.SelectedIndex switch
                {
                    0 => "Rules",
                    1 => "Settings",
                    _ => "Rules"
                };

                _currentView = viewName switch
                {
                    "Rules" => GetRulesView(),
                    "Settings" => GetSettingsView(),
                    _ => GetRulesView()
                };

                ContentArea.Content = _currentView;
                AppViewStateService.SetView(viewName);
                AddToHistory(viewName);
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

                _currentView = viewName switch
                {
                    "Dashboard" => GetDashboardView(),
                    "Processes" => GetProcessesView(),
                    "Graphs" => GetGraphsView(),
                    "Details" => GetDetailsView(),
                    "Rules" => GetRulesView(),
                    "Settings" => GetSettingsView(),
                    _ => GetDashboardView()
                };

                ContentArea.Content = _currentView;
                AppViewStateService.SetView(viewName);
                AddToHistory(viewName);
            }

            // Activate new view after switching
            if (_currentView is IActivatableView newActivatable)
            {
                newActivatable.OnActivated();
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
            if (e.ClickCount == 2 && e.LeftButton == MouseButtonState.Pressed)
            {
                // Double-click on title bar toggles maximize/restore
                Maximize_Click(sender, e);
                return;
            }
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

        private static bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Handle mouse side buttons (MB4/XButton1 = Back, MB5/XButton2 = Forward)
            if (e.XButton1 == MouseButtonState.Pressed)
            {
                e.Handled = true;
                NavigateBack();
            }
            else if (e.XButton2 == MouseButtonState.Pressed)
            {
                e.Handled = true;
                NavigateForward();
            }
        }

        private void AddToHistory(string viewName)
        {
            if (_isNavigating) return;

            // Remove any forward history if we're adding a new entry
            if (_historyIndex < _viewHistory.Count - 1)
            {
                _viewHistory.RemoveRange(_historyIndex + 1, _viewHistory.Count - _historyIndex - 1);
            }

            _viewHistory.Add(viewName);
            _historyIndex = _viewHistory.Count - 1;

            // Limit history size
            if (_viewHistory.Count > 50)
            {
                _viewHistory.RemoveAt(0);
                _historyIndex--;
            }
        }

        private void NavigateBack()
        {
            if (_historyIndex > 0)
            {
                _isNavigating = true;
                _historyIndex--;
                var viewName = _viewHistory[_historyIndex];
                SwitchToView(viewName);
                _isNavigating = false;
            }
        }

        private void NavigateForward()
        {
            if (_historyIndex < _viewHistory.Count - 1)
            {
                _isNavigating = true;
                _historyIndex++;
                var viewName = _viewHistory[_historyIndex];
                SwitchToView(viewName);
                _isNavigating = false;
            }
        }

        private void SwitchToView(string viewName)
        {
            // Update sidebar selection without triggering navigation
            int targetIndex = viewName switch
            {
                "Dashboard" => 0,
                "Processes" => 1,
                "Graphs" => 2,
                "Session Tracker" or "Details" => 3,
                "Rules" => _watchdogMode ? 0 : 4,
                "Settings" => _watchdogMode ? 1 : 5,
                _ => 0
            };

            if (targetIndex >= 0 && targetIndex < Sidebar.Items.Count)
            {
                Sidebar.SelectedIndex = targetIndex;
                // Sidebar_SelectionChanged will handle the actual view switch
            }
        }
    }
}
