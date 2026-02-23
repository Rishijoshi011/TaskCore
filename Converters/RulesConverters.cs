using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TaskCore.Models;

namespace TaskCore.Converters
{
    /// <summary>
    /// Converts a count to Visibility (Visible if count is 0, Collapsed if > 0).
    /// Used for empty state messages.
    /// </summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var count = value switch
            {
                int i => i,
                _ => 0
            };
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts a boolean to a brush color.
    /// Parameter: "Enabled" returns green for true, gray for false.
    /// Without parameter: returns green for true, red/gray for false.
    /// </summary>
    public class BoolToBrushConverter : IValueConverter
    {
        private static readonly Brush EnabledBrush = new SolidColorBrush(Color.FromRgb(46, 204, 113)); // Green
        private static readonly Brush DisabledBrush = new SolidColorBrush(Color.FromRgb(231, 76, 60)); // Red
        private static readonly Brush GrayBrush = new SolidColorBrush(Color.FromRgb(149, 165, 166)); // Gray

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isEnabled = value is bool b && b;
            var param = parameter as string;

            if (param == "Enabled")
                return isEnabled ? EnabledBrush : GrayBrush;

            // Default: true = green, false = red
            return isEnabled ? EnabledBrush : DisabledBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts a boolean to a status string.
    /// </summary>
    public class BoolToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is true ? "ON" : "OFF";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts null to a fallback text value.
    /// Parameter format: "Edit Rule|New Rule" - first part if not null, second if null.
    /// </summary>
    public class NullToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var parts = (parameter as string)?.Split('|') ?? new[] { "Edit", "New" };
            var notNullText = parts.Length > 0 ? parts[0] : "Edit";
            var nullText = parts.Length > 1 ? parts[1] : "New";

            return value != null ? notNullText : nullText;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts enum values to human-readable descriptions.
    /// </summary>
    public class EnumToDescriptionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch
            {
                RuleConditionType.CpuAbove => "CPU Usage Above",
                RuleConditionType.MemoryAbove => "Memory Usage Above",
                RuleConditionType.ProcessNameContains => "Process Name Contains",
                RuleConditionType.ProcessNameExact => "Process Name Is",
                RuleConditionType.ProcessRunning => "Process Running",
                RuleConditionType.ProcessNotRunning => "Process Not Running",

                RuleActionType.Notify => "Show Notification",
                RuleActionType.Log => "Log to File",
                RuleActionType.KillProcess => "Kill Process",
                RuleActionType.SuspendProcess => "Suspend Process",
                RuleActionType.SetPriority => "Set Priority",

                RuleNotificationMode.Silent => "Silent (No Alert)",
                RuleNotificationMode.ToastOnly => "Toast Notification",
                RuleNotificationMode.SoundOnly => "Sound Only",
                RuleNotificationMode.ToastAndSound => "Toast + Sound",

                _ => value?.ToString() ?? string.Empty
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts condition type to visibility for showing/hiding threshold input.
    /// </summary>
    public class ConditionToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is RuleConditionType.CpuAbove or RuleConditionType.MemoryAbove
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Converts action type to visibility for showing/hiding action parameter input.
    /// </summary>
    public class ActionToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is RuleActionType.SetPriority
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
