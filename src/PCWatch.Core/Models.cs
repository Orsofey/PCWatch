using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace PCWatch.Core
{
    public enum HealthStatus
    {
        Unknown,
        Excellent,
        Good,
        Warning,
        Critical
    }

    public enum IssueSeverity
    {
        Info,
        Good,
        Warning,
        Critical
    }

    public sealed class SystemSnapshot
    {
        public DateTime Timestamp { get; set; }
        public double CpuUsage { get; set; }
        public double CpuClockMhz { get; set; }
        public double BaseClockMhz { get; set; }
        public double? CpuTemperature { get; set; }
        public double[] CoreUsage { get; set; }
        public double ProcessorQueue { get; set; }
        public double MemoryUsage { get; set; }
        public double MemoryUsedGB { get; set; }
        public double MemoryTotalGB { get; set; }
        public double CommitUsage { get; set; }
        public double CommitUsedGB { get; set; }
        public double CommitLimitGB { get; set; }
        public double PageFileUsage { get; set; }
        public double PageReadsPerSec { get; set; }
        public double PageFaultsPerSec { get; set; }
        public double DiskActive { get; set; }
        public double DiskReadBytesPerSec { get; set; }
        public double DiskWriteBytesPerSec { get; set; }
        public double DiskReadLatencyMs { get; set; }
        public double DiskWriteLatencyMs { get; set; }
        public double DiskQueueLength { get; set; }
        public double SystemDriveFreePercent { get; set; }
        public double SystemDriveFreeGB { get; set; }
        public bool? SystemDriveIsSsd { get; set; }
        public double GpuUsage { get; set; }
        public double GpuDedicatedUsedGB { get; set; }
        public double GpuSharedUsedGB { get; set; }
        public double NetworkInBytesPerSec { get; set; }
        public double NetworkOutBytesPerSec { get; set; }
        public string PowerScheme { get; set; }
        public bool OnBattery { get; set; }
        public IList<ProcessSample> Processes { get; set; }
        public IList<VolumeSample> Volumes { get; set; }
        public int ForegroundProcessId { get; set; }
        public string CollectionWarning { get; set; }

        public SystemSnapshot()
        {
            Timestamp = DateTime.Now;
            CoreUsage = new double[0];
            Processes = new List<ProcessSample>();
            Volumes = new List<VolumeSample>();
            PowerScheme = "";
            CollectionWarning = "";
            SystemDriveFreePercent = 100;
        }
    }

    public sealed class ProcessSample
    {
        public int Id { get; set; }
        public DateTime StartTime { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public string Path { get; set; }
        public double CpuUsage { get; set; }
        public double MemoryMB { get; set; }
        public double DiskBytesPerSec { get; set; }
        public double PageFaultsPerSec { get; set; }
        public double GpuUsage { get; set; }
        public int ThreadCount { get; set; }
        public int HandleCount { get; set; }
        public bool? Responding { get; set; }
        public bool IsForeground { get; set; }
        public IList<WindowDescriptor> Windows { get; set; }

        public ProcessSample()
        {
            Name = "";
            Title = "";
            Path = "";
            Windows = new List<WindowDescriptor>();
        }
    }

    public sealed class WindowDescriptor
    {
        public IntPtr Handle { get; set; }
        public int ProcessId { get; set; }
        public string Title { get; set; }
        public bool IsMinimized { get; set; }
        public WindowDescriptor()
        {
            Title = "";
        }
    }

    public sealed class RecommendationItem
    {
        public ProcessSample Process { get; set; }
        public string Category { get; set; }
        public string Reason { get; set; }
        public string Impact { get; set; }
        public int Score { get; set; }
        public bool IsSafeCandidate { get; set; }
        public RecommendationItem()
        {
            Category = "";
            Reason = "";
            Impact = "";
        }
    }

    public sealed class RecommendationResult
    {
        public IList<RecommendationItem> Items { get; set; }
        public double PotentialMemoryMB { get; set; }
        public RecommendationResult()
        {
            Items = new List<RecommendationItem>();
        }
    }

    public sealed class VolumeSample
    {
        public string Name { get; set; }
        public string Label { get; set; }
        public string FileSystem { get; set; }
        public double TotalGB { get; set; }
        public double FreeGB { get; set; }
        public double FreePercent { get; set; }
        public bool? IsSsd { get; set; }

        public VolumeSample()
        {
            Name = "";
            Label = "";
            FileSystem = "";
        }
    }

    public sealed class DiagnosticIssue
    {
        public string Code { get; set; }
        public string Title { get; set; }
        public string Evidence { get; set; }
        public string Recommendation { get; set; }
        public IssueSeverity Severity { get; set; }
        public string ProcessName { get; set; }

        public DiagnosticIssue()
        {
            Code = "";
            Title = "";
            Evidence = "";
            Recommendation = "";
            ProcessName = "";
        }
    }

    public sealed class DriverLatencySample
    {
        public string Driver { get; set; }
        public long DpcCount { get; set; }
        public long IsrCount { get; set; }
        public double TotalDpcMs { get; set; }
        public double TotalIsrMs { get; set; }
        public double MaxDpcMs { get; set; }
        public double MaxIsrMs { get; set; }
        public double TotalMs { get { return TotalDpcMs + TotalIsrMs; } }

        public DriverLatencySample()
        {
            Driver = "";
        }
    }

    public sealed class AdvancedAnalysisResult
    {
        public DateTime StartedAt { get; set; }
        public TimeSpan Duration { get; set; }
        public double MaxDpcMs { get; set; }
        public double MaxIsrMs { get; set; }
        public double MaxDispatchLatencyMs { get; set; }
        public long HardPageFaults { get; set; }
        public long LostEvents { get; set; }
        public IList<DriverLatencySample> Drivers { get; set; }
        public string Summary { get; set; }
        public string Warning { get; set; }

        public AdvancedAnalysisResult()
        {
            Drivers = new List<DriverLatencySample>();
            Summary = "";
            Warning = "";
        }
    }

    public enum AlertMetric
    {
        Cpu,
        Memory,
        Disk,
        Temperature,
        SystemDriveFree,
        PageFile
    }

    [DataContract]
    public sealed class AlertRule
    {
        [DataMember] public bool Enabled { get; set; }
        [DataMember] public AlertMetric Metric { get; set; }
        [DataMember] public double Threshold { get; set; }
        [DataMember] public bool TriggerAbove { get; set; }
        [DataMember] public int DurationSeconds { get; set; }
        [DataMember] public int CooldownSeconds { get; set; }

        public AlertRule()
        {
            Enabled = true;
            TriggerAbove = true;
            DurationSeconds = 15;
            CooldownSeconds = 300;
        }
    }

    public sealed class AlertEvent
    {
        public AlertMetric Metric { get; set; }
        public string Title { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public double Value { get; set; }

        public AlertEvent()
        {
            Title = "";
            Message = "";
        }
    }

    [DataContract]
    public sealed class AppSettings
    {
        [DataMember] public int SampleIntervalMs { get; set; }
        [DataMember] public bool MinimizeToTray { get; set; }
        [DataMember] public bool StartWithWindows { get; set; }
        [DataMember] public bool AlertsEnabled { get; set; }
        [DataMember] public bool CsvLogging { get; set; }
        [DataMember] public bool RecycleDownloads { get; set; }
        [DataMember] public string EmergencyHotkey { get; set; }
        [DataMember] public List<AlertRule> Alerts { get; set; }
        [DataMember] public List<string> HiddenRecommendations { get; set; }

        public AppSettings()
        {
            SampleIntervalMs = 1000;
            MinimizeToTray = true;
            StartWithWindows = false;
            AlertsEnabled = true;
            CsvLogging = false;
            RecycleDownloads = true;
            EmergencyHotkey = "Ctrl+Alt+F12";
            HiddenRecommendations = new List<string>();
            Alerts = DefaultAlerts();
        }

        public static List<AlertRule> DefaultAlerts()
        {
            return new List<AlertRule>
            {
                new AlertRule { Metric = AlertMetric.Cpu, Threshold = 95, DurationSeconds = 15 },
                new AlertRule { Metric = AlertMetric.Memory, Threshold = 92, DurationSeconds = 15 },
                new AlertRule { Metric = AlertMetric.Disk, Threshold = 95, DurationSeconds = 20 },
                new AlertRule { Metric = AlertMetric.Temperature, Threshold = 90, DurationSeconds = 5 },
                new AlertRule { Metric = AlertMetric.SystemDriveFree, Threshold = 10, TriggerAbove = false, DurationSeconds = 0 },
                new AlertRule { Metric = AlertMetric.PageFile, Threshold = 80, DurationSeconds = 30 }
            };
        }
    }
}
