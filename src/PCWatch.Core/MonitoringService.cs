using System;
using System.Threading;
using System.Threading.Tasks;

namespace PCWatch.Core
{
    public sealed class MonitoringService : IDisposable
    {
        private readonly object _sync = new object();
        private CancellationTokenSource _cancellation;
        private Task _worker;
        private AppSettings _settings;
        private AlertEngine _alerts;
        private bool _backgroundMode;
        private readonly CsvSnapshotLogger _csvLogger = new CsvSnapshotLogger();
        private bool _disposed;

        public MonitoringService()
        {
            History = new RingBuffer<SystemSnapshot>(600);
            _settings = new AppSettings();
            _alerts = new AlertEngine(_settings);
        }

        public event Action<SystemSnapshot> SnapshotAvailable;
        public event Action<AlertEvent> AlertTriggered;
        public event Action<string> CollectionFailed;
        public RingBuffer<SystemSnapshot> History { get; private set; }
        public string CpuName { get; private set; }
        public string OsName { get; private set; }
        public string GpuNames { get; private set; }
        public bool IsRunning { get { return _worker != null && !_worker.IsCompleted; } }
        public bool BackgroundMode { get { lock (_sync) return _backgroundMode; } set { lock (_sync) _backgroundMode = value; } }

        public void Start(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException("MonitoringService");
                if (IsRunning) return;
                _settings = settings;
                _alerts = new AlertEngine(settings);
                _cancellation = new CancellationTokenSource();
                _worker = Task.Factory.StartNew(Loop, _cancellation.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
        }

        public void UpdateSettings(AppSettings settings)
        {
            if (settings == null) return;
            lock (_sync)
            {
                _settings = settings;
                _alerts = new AlertEngine(settings);
            }
        }

        private void Loop()
        {
            WindowsSystemCollector collector = null;
            try
            {
                collector = new WindowsSystemCollector();
                CpuName = collector.CpuName;
                OsName = collector.OsName;
                GpuNames = collector.GpuNames;
                while (!_cancellation.IsCancellationRequested)
                {
                    DateTime started = DateTime.UtcNow;
                    try
                    {
                        SystemSnapshot snapshot = collector.Sample();
                        History.Add(snapshot);
                        AppSettings settings;
                        AlertEngine alerts;
                        lock (_sync) { settings = _settings; alerts = _alerts; }
                        if (settings.CsvLogging) _csvLogger.Write(snapshot);
                        Action<SystemSnapshot> handler = SnapshotAvailable;
                        if (handler != null) handler(snapshot);
                        if (settings.AlertsEnabled)
                        {
                            foreach (AlertEvent alert in alerts.Evaluate(snapshot))
                            {
                                Action<AlertEvent> alertHandler = AlertTriggered;
                                if (alertHandler != null) alertHandler(alert);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Action<string> error = CollectionFailed;
                        if (error != null) error(ex.Message);
                    }
                    int interval = Math.Max(250, Math.Min(10000, _settings.SampleIntervalMs));
                    if (BackgroundMode) interval = Math.Min(10000, interval * 5);
                    int elapsed = (int)(DateTime.UtcNow - started).TotalMilliseconds;
                    int remaining = Math.Max(50, interval - elapsed);
                    _cancellation.Token.WaitHandle.WaitOne(remaining);
                }
            }
            catch (Exception ex)
            {
                Action<string> error = CollectionFailed;
                if (error != null) error(ex.Message);
            }
            finally
            {
                if (collector != null) collector.Dispose();
            }
        }

        public void Stop()
        {
            Task worker;
            lock (_sync)
            {
                if (_cancellation != null) _cancellation.Cancel();
                worker = _worker;
            }
            if (worker != null)
            {
                try { worker.Wait(2500); }
                catch (AggregateException) { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
            if (_cancellation != null) _cancellation.Dispose();
            _csvLogger.Dispose();
        }
    }
}
