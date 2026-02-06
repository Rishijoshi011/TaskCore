using System.Collections.ObjectModel;

namespace TaskCore.Models
{
    public class ProcessGroup
    {
        public string Name { get; set; } = "";
        public ObservableCollection<ProcessInfo> Children { get; } = new();

        public bool IsSingle => Children.Count <= 1;
        public int Count => Children.Count;
    }
}
