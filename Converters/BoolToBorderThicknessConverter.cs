using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TaskCore.Converters
{
    /// <summary>
    /// Converts boolean to border thickness (2 for current hour, 0 otherwise)
    /// </summary>
    public class BoolToBorderThicknessConverter : IValueConverter
    {
        private static readonly Thickness CurrentHourThickness = new Thickness(2);
        private static readonly Thickness NormalThickness = new Thickness(0);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isCurrentHour && isCurrentHour)
                return CurrentHourThickness;
            return NormalThickness;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
