using System.Windows;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using TaskCore.Services;

namespace TaskCore
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Check if watchdog mode is enabled
            var settings = AppSettingsService.Load();
            var watchdogMode = settings.WatchdogMode;

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
    }
}
