using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using PCWatch.Core;

namespace PCWatch.Services
{
    public sealed class EtwAnalysisService
    {
        private readonly object _sync = new object();
        private TraceEventSession _activeSession;
        private bool _cancelled;

        public bool IsElevated { get { return TraceEventSession.IsElevated() == true; } }

        public Task<AdvancedAnalysisResult> AnalyzeAsync(int seconds, IProgress<int> progress)
        {
            if (seconds < 10 || seconds > 300) throw new ArgumentOutOfRangeException("seconds");
            if (!IsElevated) throw new UnauthorizedAccessException("Расширенный ETW-анализ требует запуска PCWatch от имени администратора.");
            lock (_sync) { _cancelled = false; }
            return Task.Factory.StartNew(delegate { return Capture(seconds, progress); }, TaskCreationOptions.LongRunning);
        }

        private AdvancedAnalysisResult Capture(int seconds, IProgress<int> progress)
        {
            CaptureState state = new CaptureState();
            string name = "PCWatch-Kernel-" + Process.GetCurrentProcess().Id + "-" + Guid.NewGuid().ToString("N");
            using (TraceEventSession session = new TraceEventSession(name, TraceEventSessionOptions.NoRestartOnCreate))
            {
                lock (_sync) { _activeSession = session; }
                try
                {
                    session.BufferSizeMB = 64;
                    KernelTraceEventParser.Keywords flags = KernelTraceEventParser.Keywords.ImageLoad
                        | KernelTraceEventParser.Keywords.DeferedProcedureCalls
                        | KernelTraceEventParser.Keywords.Interrupt
                        | KernelTraceEventParser.Keywords.Process
                        | KernelTraceEventParser.Keywords.Thread
                        | KernelTraceEventParser.Keywords.ContextSwitch
                        | KernelTraceEventParser.Keywords.Dispatcher
                        | KernelTraceEventParser.Keywords.MemoryHardFaults;
                    session.EnableKernelProvider(flags);
                    lock (_sync) { if (_cancelled) session.Stop(); }
                    if (!_cancelled)
                    {
                        KernelTraceEventParser kernel = session.Source.Kernel;
                        kernel.PerfInfoDPC += delegate(DPCTraceData data) { state.AddDpc(data.ElapsedTimeMSec, data.Routine); };
                        kernel.PerfInfoThreadedDPC += delegate(DPCTraceData data) { state.AddDpc(data.ElapsedTimeMSec, data.Routine); };
                        kernel.PerfInfoTimerDPC += delegate(DPCTraceData data) { state.AddDpc(data.ElapsedTimeMSec, data.Routine); };
                        kernel.PerfInfoISR += delegate(ISRTraceData data) { state.AddIsr(data.ElapsedTimeMSec, data.Routine); };
                        kernel.ImageLoadGroup += delegate(ImageLoadTraceData data) { state.AddImage(data.ImageBase, data.ImageSize, data.ProcessID, data.FileName); };
                        kernel.ImageUnloadGroup += delegate(ImageLoadTraceData data) { state.RemoveImage(data.ImageBase, data.ProcessID, data.FileName); };
                        kernel.DispatcherReadyThread += data => state.Ready[data.AwakenedThreadID] = data.TimeStampRelativeMSec;
                        kernel.ThreadCSwitch += delegate(CSwitchTraceData data)
                        {
                            double ready;
                            if (!state.Ready.TryGetValue(data.NewThreadID, out ready)) return;
                            state.Ready.Remove(data.NewThreadID);
                            double latency = data.TimeStampRelativeMSec - ready;
                            if (latency >= 0 && latency < 600000) state.Result.MaxDispatchLatencyMs = Math.Max(state.Result.MaxDispatchLatencyMs, latency);
                        };
                        kernel.MemoryHardFault += data => { state.Result.HardPageFaults++; };
                        DateTime started = DateTime.UtcNow;
                        state.Result.StartedAt = DateTime.Now;
                        using (Timer timer = new Timer(delegate
                        {
                            int elapsed = (int)(DateTime.UtcNow - started).TotalSeconds;
                            if (progress != null) progress.Report(Math.Min(100, elapsed * 100 / seconds));
                            if (elapsed >= seconds)
                            {
                                state.Result.LostEvents = session.EventsLost;
                                try { session.Stop(); } catch { }
                            }
                        }, null, 500, 500))
                        {
                            session.Source.Process();
                        }
                        state.Result.Duration = DateTime.UtcNow - started;
                        if (state.Result.LostEvents == 0) state.Result.LostEvents = session.EventsLost;
                    }
                }
                finally
                {
                    lock (_sync) { _activeSession = null; }
                }
            }
            return state.Complete();
        }

        public void Cancel()
        {
            lock (_sync)
            {
                _cancelled = true;
                if (_activeSession != null)
                {
                    try { _activeSession.Stop(); } catch { }
                }
            }
        }

        private sealed class CaptureState
        {
            private readonly List<ImageRange> _images = new List<ImageRange>();
            private readonly Dictionary<ulong, string> _routineNames = new Dictionary<ulong, string>();
            private readonly Dictionary<string, DriverLatencySample> _drivers = new Dictionary<string, DriverLatencySample>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<int, double> Ready = new Dictionary<int, double>();
            public readonly AdvancedAnalysisResult Result = new AdvancedAnalysisResult();
            private int _unknown;

            public void AddImage(ulong imageBase, int imageSize, int processId, string fileName)
            {
                if (imageBase == 0 || imageSize <= 0 || String.IsNullOrEmpty(fileName)) return;
                string extension;
                try { extension = Path.GetExtension(fileName); } catch { return; }
                bool kernel = String.Equals(extension, ".sys", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith("ntoskrnl.exe", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith("hal.dll", StringComparison.OrdinalIgnoreCase);
                if (!kernel) return;
                _images.Add(new ImageRange { Base = imageBase, Size = (ulong)imageSize, ProcessId = processId, FileName = fileName });
                _routineNames.Clear();
            }

            public void RemoveImage(ulong imageBase, int processId, string fileName)
            {
                for (int i = _images.Count - 1; i >= 0; i--)
                {
                    ImageRange range = _images[i];
                    if (range.Base == imageBase && range.ProcessId == processId && String.Equals(range.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                        _images.RemoveAt(i);
                }
                _routineNames.Clear();
            }

            public void AddDpc(double milliseconds, ulong routine)
            {
                string name = Resolve(routine);
                DriverLatencySample driver = GetDriver(name);
                driver.DpcCount++;
                driver.TotalDpcMs += milliseconds;
                driver.MaxDpcMs = Math.Max(driver.MaxDpcMs, milliseconds);
                Result.MaxDpcMs = Math.Max(Result.MaxDpcMs, milliseconds);
            }

            public void AddIsr(double milliseconds, ulong routine)
            {
                string name = Resolve(routine);
                DriverLatencySample driver = GetDriver(name);
                driver.IsrCount++;
                driver.TotalIsrMs += milliseconds;
                driver.MaxIsrMs = Math.Max(driver.MaxIsrMs, milliseconds);
                Result.MaxIsrMs = Math.Max(Result.MaxIsrMs, milliseconds);
            }

            private DriverLatencySample GetDriver(string name)
            {
                DriverLatencySample driver;
                if (!_drivers.TryGetValue(name, out driver))
                {
                    driver = new DriverLatencySample { Driver = name };
                    _drivers[name] = driver;
                }
                return driver;
            }

            private string Resolve(ulong routine)
            {
                string name;
                if (_routineNames.TryGetValue(routine, out name)) return name;
                name = "Неизвестно (0x" + routine.ToString("X") + ")";
                for (int i = _images.Count - 1; i >= 0; i--)
                {
                    ImageRange range = _images[i];
                    if (routine < range.Base || routine >= range.Base + range.Size) continue;
                    try { name = Path.GetFileName(range.FileName); } catch { name = range.FileName; }
                    break;
                }
                if (name.StartsWith("Неизвестно", StringComparison.Ordinal)) _unknown++;
                _routineNames[routine] = name;
                return name;
            }

            public AdvancedAnalysisResult Complete()
            {
                List<DriverLatencySample> sorted = new List<DriverLatencySample>(_drivers.Values);
                sorted.Sort(delegate(DriverLatencySample a, DriverLatencySample b) { return b.TotalMs.CompareTo(a.TotalMs); });
                if (sorted.Count > 30) sorted.RemoveRange(30, sorted.Count - 30);
                Result.Drivers = sorted;
                double highest = Math.Max(Result.MaxDpcMs, Result.MaxIsrMs);
                if (highest <= 1) Result.Summary = "Отзывчивость ядра отличная: значительных DPC/ISR-задержек не обнаружено.";
                else if (highest <= 2) Result.Summary = "Отзывчивость ядра хорошая, задержки обычно не мешают работе в реальном времени.";
                else if (highest <= 5) Result.Summary = "Обнаружены заметные DPC/ISR-задержки. Проверьте драйверы с наибольшим временем исполнения.";
                else Result.Summary = "Обнаружены высокие DPC/ISR-задержки, способные влиять на звук, видео и отзывчивость системы.";
                List<string> warnings = new List<string>();
                if (Result.LostEvents > 0) warnings.Add("Потеряно событий ETW: " + Result.LostEvents + ". Результаты могут быть неполными.");
                if (_unknown > 0) warnings.Add("Часть адресов не сопоставлена с образами драйверов; они вынесены в отдельные строки unknown.");
                if (Result.MaxDispatchLatencyMs > 0) warnings.Add("Максимальная оценка задержки планировщика: " + RussianFormat.Milliseconds(Result.MaxDispatchLatencyMs) + ". Это приблизительная метрика по ReadyThread/CSwitch, а не время ответа GUI.");
                Result.Warning = String.Join(" ", warnings.ToArray());
                return Result;
            }
        }

        private sealed class ImageRange
        {
            public ulong Base;
            public ulong Size;
            public int ProcessId;
            public string FileName;
        }
    }
}
