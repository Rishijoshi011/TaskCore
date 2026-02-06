using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TaskCore.Views
{
    public class SidebarWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isSidebarOpen)
            {
                return isSidebarOpen ? new GridLength(250) : new GridLength(50);
            }
            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}