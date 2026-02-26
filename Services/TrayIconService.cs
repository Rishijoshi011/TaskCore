using System;
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;
using TaskCore.Services;

namespace TaskCore.Services
{
    public class TrayIconService : IDisposable
    {
        private static TrayIconService? _instance;
        private static readonly object _lock = new();

        private Forms.NotifyIcon? _notifyIcon;
        private Window? _mainWindow;
        private System.Timers.Timer? _tooltipTimer;
        private bool _isDisposed;

        public bool IsInitialized => _notifyIcon != null;

        public static TrayIconService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new TrayIconService();
                    }
                }
                return _instance;
            }
        }

        private TrayIconService()
        {
        }

        public void Initialize(Window mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));

            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = GetApplicationIcon(),
                Text = "TaskCore - System Monitor",
                Visible = true
            };

            CreateContextMenu();
            SetupTooltip();
            _notifyIcon.DoubleClick += OnNotifyIconDoubleClick;

            // Handle window close to minimize to tray
            _mainWindow.Closing += OnMainWindowClosing;
            _mainWindow.StateChanged += OnMainWindowStateChanged;
        }

        private void CreateContextMenu()
        {
            if (_notifyIcon == null) return;

            var contextMenu = new Forms.ContextMenuStrip();

            // Open menu item
            var openItem = new Forms.ToolStripMenuItem("Open", null, OnOpenClick);
            contextMenu.Items.Add(openItem);

            // Separator
            contextMenu.Items.Add(new Forms.ToolStripSeparator());

            // Exit menu item
            var exitItem = new Forms.ToolStripMenuItem("Exit", null, OnExitClick);
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void SetupTooltip()
        {
            if (_notifyIcon == null) return;

            // Initial tooltip
            UpdateTooltip();

            // Set up a timer to update tooltip every 5 seconds
            _tooltipTimer = new System.Timers.Timer(5000);
            _tooltipTimer.Elapsed += (_, _) => UpdateTooltip();
            _tooltipTimer.AutoReset = true;
            _tooltipTimer.Start();
        }

        private void UpdateTooltip()
        {
            try
            {
                if (_notifyIcon == null) return;

                var running = UsageTrackingService.Instance.GetCurrentlyRunning();
                string tooltip;

                if (running.Count > 0)
                {
                    var topApp = running.OrderByDescending(r => r.avgCpu + r.avgMem).First();
                    tooltip = $"TaskCore\nActive: {running.Count} apps\nTop: {topApp.appName} ({topApp.avgCpu:F1}% CPU)";
                }
                else
                {
                    tooltip = "TaskCore - System Monitor\nNo active apps";
                }

                // Tooltip has max 63 chars in older Windows versions
                if (tooltip.Length > 63)
                {
                    tooltip = tooltip.Substring(0, 60) + "...";
                }

                // Double-check null to avoid race condition with Dispose
                if (_notifyIcon != null)
                {
                    _notifyIcon.Text = tooltip;
                }
            }
            catch
            {
                // Ignore errors when updating tooltip
            }
        }

        private void OnOpenClick(object? sender, EventArgs e)
        {
            ShowMainWindow();
        }

        private void OnExitClick(object? sender, EventArgs e)
        {
            Dispose();
            System.Windows.Application.Current.Shutdown();
        }

        private void OnNotifyIconDoubleClick(object? sender, EventArgs e)
        {
            ShowMainWindow();
        }

        private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Check if minimize to tray is enabled
            var settings = AppSettingsService.Load();
            if (!settings.MinimizeToTray)
            {
                // Setting is disabled, allow normal close
                Dispose();
                return;
            }

            // Cancel the close and minimize to tray instead
            if (!_isDisposed && _mainWindow != null)
            {
                e.Cancel = true;
                _mainWindow.Hide();
            }
        }

        private void OnMainWindowStateChanged(object? sender, EventArgs e)
        {
            // If window is minimized, hide it to tray
            if (_mainWindow?.WindowState == WindowState.Minimized)
            {
                _mainWindow.Hide();
            }
        }

        private void ShowMainWindow()
        {
            if (_mainWindow == null) return;

            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            _mainWindow.Focus();
        }

        private Icon GetApplicationIcon()
        {
            try
            {
                // Try to get the application icon from the assembly
                return System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
            }
            catch
            {
                // Fallback to system information icon
                return SystemIcons.Information;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;

            if (_mainWindow != null)
            {
                _mainWindow.Closing -= OnMainWindowClosing;
                _mainWindow.StateChanged -= OnMainWindowStateChanged;
            }

            if (_tooltipTimer != null)
            {
                _tooltipTimer.Stop();
                _tooltipTimer.Dispose();
                _tooltipTimer = null;
            }

            if (_notifyIcon != null)
            {
                _notifyIcon.DoubleClick -= OnNotifyIconDoubleClick;
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            GC.SuppressFinalize(this);
        }
    }
}
