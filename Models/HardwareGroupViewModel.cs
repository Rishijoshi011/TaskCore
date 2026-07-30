using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace TaskCore.Models
{
    public partial class HardwareGroupViewModel : ObservableObject
    {
        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private string hardwareType = string.Empty;

        [ObservableProperty]
        private string category = "Other";

        [ObservableProperty]
        private string categoryColorBrush = "#8A2BE2";

        [ObservableProperty]
        private ObservableCollection<SensorItemViewModel> sensors = new();

        public HardwareGroupViewModel(string name, string hardwareType, string category)
        {
            Name = name;
            HardwareType = hardwareType;
            Category = category;
            CategoryColorBrush = GetCategoryColor(category);
        }

        private static string GetCategoryColor(string category)
        {
            return category switch
            {
                "CPU" => "#8A2BE2",        // BlueViolet / Purple
                "GPU" => "#FFDC00",        // Gold / Yellow
                "Memory" => "#00FFFF",     // Cyan
                "Storage" => "#00FF88",    // Mint Green
                "Motherboard" => "#FF6464", // Coral / Light Red
                "Cooling" => "#0099FF",    // Sky Blue
                "Power" => "#FF9900",      // Orange
                "Battery" => "#FF55AA",    // Pink
                "Network" => "#33CCFF",    // Light Cyan
                _ => "#AAAAAA"
            };
        }
    }
}
