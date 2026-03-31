using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TaskCore.Converters
{
    /// <summary>
    /// Converts boolean to border brush (accent color for current hour, transparent otherwise)
    /// </summary>
    public class BoolToBorderBrushConverter : IValueConverter
    {
        private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly Brush TransparentBrush = Brushes.Transparent;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isCurrentHour && isCurrentHour)
                return AccentBrush;
            return TransparentBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
