using System.Diagnostics;

using System.Windows.Media;

namespace TaskCore.Models
{
    public class ProcessItem
    {
        public required string Name { get; init; }
        public required int PID { get; init; }
        public string Path { get; init; } = string.Empty;
        public double CpuUsage { get; set; }     // %
        public double MemoryMB { get; set; }     // MB
        public ImageSource? Icon { get; set; }
    }
}
