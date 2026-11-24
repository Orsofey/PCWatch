using System;
using System.Globalization;
using System.Windows.Data;

namespace PCWatch.Converters
{
    public sealed class WindowStateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return value is bool && (bool)value ? "Свёрнуто" : "Открыто"; }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }
}
