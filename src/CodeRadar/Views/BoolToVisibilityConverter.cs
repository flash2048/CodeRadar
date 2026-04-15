using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CodeRadar.Views
{
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool v = value is bool b && b;
            if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
                v = !v;
            return v ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
