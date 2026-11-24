using System;
using System.Globalization;
using System.Windows.Data;
using PCWatch.Core;

namespace PCWatch.Converters
{
    public sealed class AlertMetricConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is AlertMetric)) return value;
            switch ((AlertMetric)value)
            {
                case AlertMetric.Cpu: return "Процессор";
                case AlertMetric.Memory: return "Оперативная память";
                case AlertMetric.Disk: return "Активность диска";
                case AlertMetric.Temperature: return "Температура ЦП";
                case AlertMetric.SystemDriveFree: return "Свободно на системном диске";
                case AlertMetric.PageFile: return "Файл подкачки";
                default: return value.ToString();
            }
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }
}
