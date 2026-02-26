using System;
using System.Threading;
using System.Windows;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using TaskCore.Services;

namespace TaskCore
{
    public partial class App : Application
    {
        private static Mutex? _instanceMutex;
        private static bool _ownsMutex;
        private const string MutexName = "TaskCore_SingleInstance_Mutex";

        protected override void OnStartup(StartupEventArgs e)
        {
            // Check if watchdog mode is requested via command line or settings
            var settings = AppSettingsService.Load();
            var watchdogMode = settings.WatchdogMode || e.Args.Contains("--watchdog");
            
            // Use different mutex for watchdog mode to allow it running alongside regular mode
            var mutexName = watchdogMode ? "TaskCore_Watchdog_Instance" : MutexName;
            
            _instanceMutex = new Mutex(true, mutexName, out bool createdNew);
            
            if (!createdNew && !watchdogMode)
            {
                // Only block regular mode duplicates. Watchdog mode can have multiple instances
                try { _instanceMutex.Close(); } catch { }
                _instanceMutex = null;
                _ownsMutex = false;
                
                // Bring existing window to foreground
                try
                {
                    var existingProcess = System.Diagnostics.Process.GetProcessesByName("TaskCore")
                        .FirstOrDefault(p => p.Id != System.Diagnostics.Process.GetCurrentProcess().Id);
                    
                    if (existingProcess != null)
                    {
                        NativeMethods.SetForegroundWindow(existingProcess.MainWindowHandle);
                    }
                }
                catch { }
                
                Shutdown();
                return;
            }

            // We own the mutex
            _ownsMutex = true;

            base.OnStartup(e);

            // Initialize theme with saved accent color
            ThemeService.ApplyAccentColor(settings.AccentColor ?? "#FF9A4DFF");

            // LiveCharts/SkiaSharp initialization - skip in watchdog mode (no graphs shown)
            // This saves ~30-50MB memory in minimal mode
            if (!watchdogMode)
            {
                LiveCharts.Configure(config =>
                    config.AddSkiaSharp()
                );
            }

            // UsageTrackingService runs in ALL modes - records session data for dashboard
            // even when in watchdog mode, so data is available when viewing dashboard later
            _ = UsageTrackingService.Instance;

            if (settings.AutomationWatchdogEnabled)
            {
                try
                {
                    _ = AutomationWatchdogService.Instance;
                }
                catch
                {
                }
            }

            // RulesService initializes itself via lazy singleton when needed
            // No need to force init - it only runs when rules exist
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            // Initialize tray icon once when window is activated
            if (MainWindow is not null && TrayIconService.Instance is { IsInitialized: false })
            {
                TrayIconService.Instance.Initialize(MainWindow);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Only release mutex if we actually acquired it
            if (_ownsMutex && _instanceMutex != null)
            {
                try
                {
                    _instanceMutex.ReleaseMutex();
                }
                catch { }
                
                try
                {
                    _instanceMutex.Close();
                }
                catch { }
                
                _instanceMutex = null;
                _ownsMutex = false;
            }
            
            base.OnExit(e);
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool SetForegroundWindow(IntPtr hWnd);
        }
    }
}
