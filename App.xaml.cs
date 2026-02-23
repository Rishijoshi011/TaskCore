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

            // REQUIRED for LiveChartsCore — loads Skia backend
            LiveCharts.Configure(config =>
                config.AddSkiaSharp()
            );

            // WatchdogService removed - merged into RulesService
            // _ = AppSessionTrackerService.Instance; // DISABLED - using UsageTrackingService instead
            _ = UsageTrackingService.Instance;
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
