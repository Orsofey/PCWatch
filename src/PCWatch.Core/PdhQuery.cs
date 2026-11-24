using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PCWatch.Core
{
    internal sealed class PdhQuery : IDisposable
    {
        private const uint PdhFmtDouble = 0x00000200;
        private const int PdhMoreData = unchecked((int)0x800007D2);
        private readonly Dictionary<string, IntPtr> _counters = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
        private IntPtr _query;

        public PdhQuery()
        {
            int status = PdhOpenQuery(null, IntPtr.Zero, out _query);
            if (status != 0) throw new InvalidOperationException("PdhOpenQuery: 0x" + status.ToString("X8"));
        }

        public bool Add(string key, string path)
        {
            IntPtr counter;
            if (_query == IntPtr.Zero) return false;
            int status = PdhAddEnglishCounter(_query, path, IntPtr.Zero, out counter);
            if (status != 0 || counter == IntPtr.Zero) return false;
            _counters[key] = counter;
            return true;
        }

        public bool Collect()
        {
            return _query != IntPtr.Zero && PdhCollectQueryData(_query) == 0;
        }

        public double GetDouble(string key)
        {
            IntPtr counter;
            if (!_counters.TryGetValue(key, out counter)) return double.NaN;
            PdhFmtCounterValue value;
            int status = PdhGetFormattedCounterValue(counter, PdhFmtDouble, IntPtr.Zero, out value);
            if (status != 0 || value.CStatus != 0) return double.NaN;
            return value.DoubleValue;
        }

        public Dictionary<string, double> GetArray(string key)
        {
            Dictionary<string, double> result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            IntPtr counter;
            if (!_counters.TryGetValue(key, out counter)) return result;
            int size = 0;
            int count;
            int status = PdhGetFormattedCounterArray(counter, PdhFmtDouble, ref size, out count, IntPtr.Zero);
            if (status != PdhMoreData || size <= 0) return result;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                status = PdhGetFormattedCounterArray(counter, PdhFmtDouble, ref size, out count, buffer);
                if (status != 0) return result;
                int itemSize = Marshal.SizeOf(typeof(PdhFmtCounterValueItem));
                for (int i = 0; i < count; i++)
                {
                    IntPtr ptr = new IntPtr(buffer.ToInt64() + (long)i * itemSize);
                    PdhFmtCounterValueItem item = (PdhFmtCounterValueItem)Marshal.PtrToStructure(ptr, typeof(PdhFmtCounterValueItem));
                    if (item.CValue.CStatus != 0 || item.Name == IntPtr.Zero) continue;
                    string name = Marshal.PtrToStringUni(item.Name);
                    if (String.IsNullOrEmpty(name)) continue;
                    if (result.ContainsKey(name))
                    {
                        string baseName = name;
                        int suffix = 1;
                        do { name = baseName + "#" + suffix; suffix++; } while (result.ContainsKey(name));
                    }
                    result[name] = item.CValue.DoubleValue;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return result;
        }

        public void Dispose()
        {
            if (_query != IntPtr.Zero)
            {
                PdhCloseQuery(_query);
                _query = IntPtr.Zero;
            }
            _counters.Clear();
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PdhFmtCounterValue
        {
            [FieldOffset(0)] public uint CStatus;
            [FieldOffset(8)] public double DoubleValue;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PdhFmtCounterValueItem
        {
            public IntPtr Name;
            public PdhFmtCounterValue CValue;
        }

        [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW", CharSet = CharSet.Unicode)]
        private static extern int PdhOpenQuery(string dataSource, IntPtr userData, out IntPtr query);
        [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode)]
        private static extern int PdhAddEnglishCounter(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);
        [DllImport("pdh.dll")]
        private static extern int PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")]
        private static extern int PdhCloseQuery(IntPtr query);
        [DllImport("pdh.dll")]
        private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr counterType, out PdhFmtCounterValue value);
        [DllImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW", CharSet = CharSet.Unicode)]
        private static extern int PdhGetFormattedCounterArray(IntPtr counter, uint format, ref int bufferSize, out int itemCount, IntPtr itemBuffer);
    }
}
