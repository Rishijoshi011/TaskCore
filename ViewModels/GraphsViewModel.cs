using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace TaskCore.ViewModels
{
    public partial class GraphsViewModel : ObservableObject
    {
        private readonly DispatcherTimer _timer;
        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _ramCounter;

        [ObservableProperty]
        private ObservableCollection<ISeries> cpuSeries;

        [ObservableProperty]
        private ObservableCollection<ISeries> ramSeries;

        [ObservableProperty]
        private ObservableCollection<string> xLabels;

        public GraphsViewModel()
        {
            // CPU Counter
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");

            // RAM Counter
            _ramCounter = new PerformanceCounter("Memory", "Available MBytes");

            // Initialize graph series
            cpuSeries = new ObservableCollection<ISeries>
            {
                new LineSeries<double>
                {
                    Values = new ObservableCollection<double>(),
                    GeometrySize = 0,
                    Stroke = new SolidColorPaint(SKColors.MediumPurple, 3),
                    Fill = null,
                    LineSmoothness = 0.4,
                }
            };

            ramSeries = new ObservableCollection<ISeries>
            {
                new LineSeries<double>
                {
                    Values = new ObservableCollection<double>(),
                    GeometrySize = 0,
                    Stroke = new SolidColorPaint(SKColors.MediumSeaGreen, 3),
                    Fill = null,
                    LineSmoothness = 0.4,
                }
            };

            xLabels = new ObservableCollection<string>();

            // Timer Update
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += UpdateGraphs;
            _timer.Start();
        }

        private void UpdateGraphs(object? sender, EventArgs e)
        {
            double cpuVal = Math.Round(_cpuCounter.NextValue(), 1);

            double freeRam = _ramCounter.NextValue();
            double usedRam = GetTotalMemoryMB() - freeRam;

            // Add values
            ((ObservableCollection<double>)CpuSeries[0].Values).Add(cpuVal);
            ((ObservableCollection<double>)RamSeries[0].Values).Add(usedRam);

            XLabels.Add(DateTime.Now.ToString("HH:mm:ss"));

            // Keep only last 60 points
            Trim<double>((ICollection<double>)CpuSeries[0].Values);
            Trim<double>((ICollection<double>)RamSeries[0].Values);
            Trim<string>(XLabels);
        }

        private double GetTotalMemoryMB()
        {
            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024.0);
        }

        private void Trim<T>(ICollection<T> list)
        {
            if (list.Count > 60)
            {
                // Remove first element
                if (list is ObservableCollection<T> oc)
                    oc.RemoveAt(0);
            }
        }
    }
}
