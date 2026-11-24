using System;
using System.Collections.Generic;
using System.Threading;
using PCWatch.Core;

namespace PCWatch.CollectorSmoke
{
    public static class Program
    {
        public static int Main()
        {
            try
            {
                using (WindowsSystemCollector collector = new WindowsSystemCollector())
                {
                    List<SystemSnapshot> history = new List<SystemSnapshot>();
                    history.Add(collector.Sample());
                    Thread.Sleep(1200);
                    SystemSnapshot s = collector.Sample();
                    history.Add(s);
                    Console.WriteLine("CPU: {0:0.0}% / {1:0} MHz", s.CpuUsage, s.CpuClockMhz);
                    Console.WriteLine("RAM: {0:0.0}% / {1:0.0} GB", s.MemoryUsage, s.MemoryTotalGB);
                    Console.WriteLine("Disk: {0:0.0}% / {1:0.0} ms", s.DiskActive, Math.Max(s.DiskReadLatencyMs, s.DiskWriteLatencyMs));
                    Console.WriteLine("GPU: {0:0.0}% / Net: {1:0} B/s", s.GpuUsage, s.NetworkInBytesPerSec + s.NetworkOutBytesPerSec);
                    Console.WriteLine("Processes: {0}; cores: {1}; volumes: {2}", s.Processes.Count, s.CoreUsage.Length, s.Volumes.Count);
                    Console.WriteLine("Power: {0}; temperature: {1}", s.PowerScheme, s.CpuTemperature.HasValue ? s.CpuTemperature.Value.ToString("0.0") : "n/a");
                    if (s.MemoryTotalGB <= 0) throw new Exception("Physical memory was not read.");
                    if (s.Processes.Count == 0) throw new Exception("No processes were read.");
                    if (s.CoreUsage.Length == 0) throw new Exception("No CPU cores were read.");
                    if (String.IsNullOrWhiteSpace(KnownApplications.ResolveExecutablePath("notepad"))) throw new Exception("Known application path resolver failed.");
                    Console.WriteLine("Foreground PID: {0}; windows: {1}", s.ForegroundProcessId, CountWindows(s));
                    RecommendationResult recommendations = RecommendationEngine.Evaluate(history, System.Diagnostics.Process.GetCurrentProcess().Id, null);
                    foreach (RecommendationItem item in recommendations.Items)
                        Console.WriteLine("Recommendation: [{0}] '{1}' / {2}", item.Category, item.Process.Name, item.IsSafeCandidate);
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static int CountWindows(SystemSnapshot snapshot)
        {
            int count = 0;
            foreach (ProcessSample process in snapshot.Processes) count += process.Windows.Count;
            return count;
        }
    }
}
