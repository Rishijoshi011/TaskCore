using System;
using System.Windows.Media.Imaging;
using TaskCore.Services;

namespace TaskCore.ViewModels
{
    public class CurrentlyRunningItem
    {
        public string AppName { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public DateTime StartTimeLocal { get; set; }
        public string DurationDisplay => (DateTime.Now - StartTimeLocal).ToString("hh\\:mm\\:ss");
        public double CurrentCpuPercent { get; set; }
        public double CurrentMemoryMb { get; set; }
        public string ProcessPath { get; set; } = string.Empty;
        public BitmapSource? Icon => IconService.GetIconForPath(ProcessPath);
    }
}
