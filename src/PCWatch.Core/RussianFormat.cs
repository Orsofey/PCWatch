using System;
using System.Collections.Generic;
using System.Globalization;

namespace PCWatch.Core
{
    /// <summary>
    /// Форматирование величин в русской локали (запятая как разделитель дробной части).
    /// </summary>
    public static class RussianFormat
    {
        private const double Kb = 1024.0;
        private const double Mb = 1024.0 * 1024.0;
        private const double Gb = 1024.0 * 1024.0 * 1024.0;

        public static CultureInfo Culture
        {
            get { return CultureInfo.GetCultureInfo("ru-RU"); }
        }

        public static string Number(double value)
        {
            return value.ToString("N1", Culture);
        }

        public static string Bytes(double bytes)
        {
            double abs = Math.Abs(bytes);
            if (abs >= Gb)
            {
                return (bytes / Gb).ToString("N2", Culture) + " ГБ";
            }
            if (abs >= Mb)
            {
                return (bytes / Mb).ToString("N1", Culture) + " МБ";
            }
            if (abs >= Kb)
            {
                return (bytes / Kb).ToString("N1", Culture) + " КБ";
            }
            return bytes.ToString("N0", Culture) + " Б";
        }

        public static string BytesPerSec(double bytesPerSec)
        {
            return Bytes(bytesPerSec) + "/с";
        }

        public static string GigaBytes(double gigaBytes)
        {
            return gigaBytes.ToString("N1", Culture) + " ГБ";
        }

        public static string Percent(double value)
        {
            return value.ToString("N1", Culture) + " %";
        }

        public static string MegaHertz(double value)
        {
            return value.ToString("N0", Culture) + " МГц";
        }

        public static string Milliseconds(double value)
        {
            return value.ToString("N1", Culture) + " мс";
        }

        public static string Rate(double valuePerSec)
        {
            return valuePerSec.ToString("N1", Culture) + " /с";
        }
    }
}
