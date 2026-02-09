using System.Windows;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

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
        }
    }
}
