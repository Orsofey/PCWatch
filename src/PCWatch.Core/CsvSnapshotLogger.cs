using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace PCWatch.Core
{
    public sealed class CsvSnapshotLogger : IDisposable
    {
        private readonly object _sync = new object();
        private StreamWriter _writer;
        private DateTime _lastFlush;
        private int _writes;

        public string FilePath { get; private set; }

        public void Write(SystemSnapshot s)
        {
            lock (_sync)
            {
                try
                {
                    if (_writer == null)
                    {
                        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCWatch", "Logs");
                        Directory.CreateDirectory(folder);
                        FilePath = Path.Combine(folder, "pcwatch-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv");
                        _writer = new StreamWriter(FilePath, false, new UTF8Encoding(true));
                        _writer.WriteLine("time;cpu;clock_mhz;ram;disk;gpu;net_kbs;page_reads;top_process");
                    }
                    string top = "";
                    double cpu = -1;
                    foreach (ProcessSample p in s.Processes)
                        if (p.CpuUsage > cpu) { cpu = p.CpuUsage; top = p.Name; }
                    _writer.WriteLine(String.Join(";", new string[]
                    {
                        s.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        s.CpuUsage.ToString("0.0", CultureInfo.InvariantCulture),
                        s.CpuClockMhz.ToString("0", CultureInfo.InvariantCulture),
                        s.MemoryUsage.ToString("0.0", CultureInfo.InvariantCulture),
                        s.DiskActive.ToString("0.0", CultureInfo.InvariantCulture),
                        s.GpuUsage.ToString("0.0", CultureInfo.InvariantCulture),
                        ((s.NetworkInBytesPerSec + s.NetworkOutBytesPerSec) / 1024).ToString("0.0", CultureInfo.InvariantCulture),
                        s.PageReadsPerSec.ToString("0.0", CultureInfo.InvariantCulture),
                        top.Replace(';', ',')
                    }));
                    _writes++;
                    if (_writes >= 5 || DateTime.Now - _lastFlush > TimeSpan.FromSeconds(10))
                    {
                        _writer.Flush();
                        _writes = 0;
                        _lastFlush = DateTime.Now;
                    }
                }
                catch (IOException)
                {
                    StopWriter();
                }
                catch (UnauthorizedAccessException)
                {
                    StopWriter();
                }
            }
        }

        private void StopWriter()
        {
            if (_writer == null) return;
            try { _writer.Dispose(); } catch { }
            _writer = null;
        }

        public void Dispose()
        {
            lock (_sync) StopWriter();
        }
    }
}
