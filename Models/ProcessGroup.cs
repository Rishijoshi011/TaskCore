using System.Collections.ObjectModel;

namespace TaskCore.Models
{
    public class ProcessGroup
    {
        public required string GroupName { get; init; }
        public double TotalCpu { get; set; }
        public double TotalMemory { get; set; } // MB
        public bool IsExpanded { get; set; } = false;
        public ObservableCollection<ProcessItem> Items { get; init; } = new();
    }
}
