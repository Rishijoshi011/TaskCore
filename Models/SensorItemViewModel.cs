using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace TaskCore.Models
{
    public partial class SensorItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private string identifier = string.Empty;

        [ObservableProperty]
        private string hardwareName = string.Empty;

        [ObservableProperty]
        private string hardwareType = string.Empty;

        [ObservableProperty]
        private string sensorType = string.Empty;

        [ObservableProperty]
        private string category = "Other";

        [ObservableProperty]
        private string unit = string.Empty;

        [ObservableProperty]
        private double? currentValue;

        [ObservableProperty]
        private string formattedValue = "Not Available";

        [ObservableProperty]
        private double? minValue;

        [ObservableProperty]
        private string formattedMin = "N/A";

        [ObservableProperty]
        private double? maxValue;

        [ObservableProperty]
        private string formattedMax = "N/A";

        [ObservableProperty]
        private double? avgValue;

        [ObservableProperty]
        private string formattedAvg = "N/A";

        [ObservableProperty]
        private string healthStatus = "N/A";

        [ObservableProperty]
        private string healthStatusBrush = "#888888";

        [ObservableProperty]
        private bool isAvailable;

        [ObservableProperty]
        private string lastUpdated = "N/A";

        private long _sampleCount;
        private double _sumValue;

        public SensorItemViewModel(string name, string identifier, string hardwareName, string hardwareType, string sensorType, string category, string unit)
        {
            Name = name;
            Identifier = identifier;
            HardwareName = hardwareName;
            HardwareType = hardwareType;
            SensorType = sensorType;
            Category = category;
            Unit = unit;
        }

        public void UpdateValue(double? newValue)
        {
            if (!newValue.HasValue || double.IsNaN(newValue.Value) || double.IsInfinity(newValue.Value))
            {
                IsAvailable = false;
                FormattedValue = "Not Available";
                return;
            }

            double val = Math.Round(newValue.Value, 2);
            CurrentValue = val;
            IsAvailable = true;
            LastUpdated = DateTime.Now.ToString("HH:mm:ss");

            // Min tracking
            if (!MinValue.HasValue || val < MinValue.Value)
            {
                MinValue = val;
            }

            // Max tracking
            if (!MaxValue.HasValue || val > MaxValue.Value)
            {
                MaxValue = val;
            }

            // Average tracking
            _sampleCount++;
            _sumValue += val;
            AvgValue = Math.Round(_sumValue / _sampleCount, 2);

            // Format strings
            FormattedValue = FormatWithUnit(val);
            FormattedMin = FormatWithUnit(MinValue.Value);
            FormattedMax = FormatWithUnit(MaxValue.Value);
            FormattedAvg = FormatWithUnit(AvgValue.Value);

            // Evaluate health status
            EvaluateHealthStatus(val);
        }

        public void ResetMinMax()
        {
            _sampleCount = 0;
            _sumValue = 0;
            MinValue = CurrentValue;
            MaxValue = CurrentValue;
            AvgValue = CurrentValue;

            if (CurrentValue.HasValue && MinValue.HasValue && MaxValue.HasValue && AvgValue.HasValue)
            {
                _sampleCount = 1;
                _sumValue = CurrentValue.Value;
                FormattedMin = FormatWithUnit(MinValue.Value);
                FormattedMax = FormatWithUnit(MaxValue.Value);
                FormattedAvg = FormatWithUnit(AvgValue.Value);
            }
            else
            {
                FormattedMin = "N/A";
                FormattedMax = "N/A";
                FormattedAvg = "N/A";
            }
        }

        private string FormatWithUnit(double val)
        {
            if (SensorType == "Temperature")
                return $"{val:F1} {Unit}";
            if (SensorType == "Load" || SensorType == "Control" || SensorType == "Level")
                return $"{val:F1} {Unit}";
            if (SensorType == "Clock" || SensorType == "Frequency")
                return val >= 1000 ? $"{val / 1000.0:F2} GHz" : $"{val:F0} {Unit}";
            if (SensorType == "Power")
                return $"{val:F1} {Unit}";
            if (SensorType == "Voltage")
                return $"{val:F3} {Unit}";
            if (SensorType == "Current")
                return $"{val:F2} {Unit}";
            if (SensorType == "Fan" || SensorType == "Flow")
                return $"{val:F0} {Unit}";
            if (SensorType == "Throughput")
                return val >= 1024 * 1024 ? $"{val / (1024.0 * 1024.0):F1} MB/s" : $"{val / 1024.0:F1} KB/s";
            if (SensorType == "Data" || SensorType == "SmallData")
                return $"{val:F1} {Unit}";

            return $"{val:F1} {Unit}".Trim();
        }

        private void EvaluateHealthStatus(double val)
        {
            if (SensorType == "Temperature")
            {
                if (Category == "Storage")
                {
                    if (val < 45) { HealthStatus = "Excellent"; HealthStatusBrush = "#00FF88"; }
                    else if (val < 55) { HealthStatus = "Good"; HealthStatusBrush = "#00FFFF"; }
                    else if (val < 65) { HealthStatus = "Warm"; HealthStatusBrush = "#FFDC00"; }
                    else if (val < 70) { HealthStatus = "Hot"; HealthStatusBrush = "#FF8800"; }
                    else { HealthStatus = "Critical"; HealthStatusBrush = "#FF4444"; }
                }
                else if (Category == "Battery")
                {
                    if (val < 35) { HealthStatus = "Excellent"; HealthStatusBrush = "#00FF88"; }
                    else if (val < 43) { HealthStatus = "Good"; HealthStatusBrush = "#00FFFF"; }
                    else if (val < 50) { HealthStatus = "Warm"; HealthStatusBrush = "#FFDC00"; }
                    else { HealthStatus = "Critical"; HealthStatusBrush = "#FF4444"; }
                }
                else
                {
                    if (val < 60) { HealthStatus = "Excellent"; HealthStatusBrush = "#00FF88"; }
                    else if (val < 75) { HealthStatus = "Good"; HealthStatusBrush = "#00FFFF"; }
                    else if (val < 85) { HealthStatus = "Warm"; HealthStatusBrush = "#FFDC00"; }
                    else if (val < 95) { HealthStatus = "Hot"; HealthStatusBrush = "#FF8800"; }
                    else { HealthStatus = "Critical"; HealthStatusBrush = "#FF4444"; }
                }
            }
            else if (SensorType == "Load" || SensorType == "Control")
            {
                if (val < 75) { HealthStatus = "Normal"; HealthStatusBrush = "#00FF88"; }
                else if (val < 90) { HealthStatus = "High"; HealthStatusBrush = "#FFDC00"; }
                else { HealthStatus = "Heavy"; HealthStatusBrush = "#FF8800"; }
            }
            else
            {
                HealthStatus = "Normal";
                HealthStatusBrush = "#CCCCCC";
            }
        }
    }
}
