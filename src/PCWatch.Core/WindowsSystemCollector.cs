using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace PCWatch.Core
{
    public sealed class WindowsSystemCollector : IDisposable
    {
        private readonly PdhQuery _pdh = new PdhQuery();
        private readonly Dictionary<int, ProcessMetadata> _processMetadata = new Dictionary<int, ProcessMetadata>();
        private readonly string _systemRoot;
        private DateTime _temperatureAt = DateTime.MinValue;
        private DateTime _powerAt = DateTime.MinValue;
        private double? _temperature;
        private string _powerScheme = "";
        private bool? _systemDriveIsSsd;
        private bool _disposed;

        public string CpuName { get; private set; }
        public string OsName { get; private set; }
        public string GpuNames { get; private set; }
        public double BaseClockMhz { get; private set; }

        public WindowsSystemCollector()
        {
            CpuName = "Процессор";
            OsName = Environment.OSVersion.VersionString;
            GpuNames = "";
            _systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            AddCounters();
            ReadStaticInformation();
            _systemDriveIsSsd = DetectSystemDriveSsd();
        }

        private void AddCounters()
        {
            _pdh.Add("cpu", @"\Processor(_Total)\% Processor Time");
            _pdh.Add("cores", @"\Processor(*)\% Processor Time");
            _pdh.Add("cpuPerf", @"\Processor Information(_Total)\% Processor Performance");
            _pdh.Add("queue", @"\System\Processor Queue Length");
            _pdh.Add("commit", @"\Memory\% Committed Bytes In Use");
            _pdh.Add("pageReads", @"\Memory\Page Reads/sec");
            _pdh.Add("pageFaults", @"\Memory\Page Faults/sec");
            _pdh.Add("pageFile", @"\Paging File(_Total)\% Usage");
            _pdh.Add("diskIdle", @"\PhysicalDisk(_Total)\% Idle Time");
            _pdh.Add("diskRead", @"\PhysicalDisk(_Total)\Disk Read Bytes/sec");
            _pdh.Add("diskWrite", @"\PhysicalDisk(_Total)\Disk Write Bytes/sec");
            _pdh.Add("diskReadLatency", @"\PhysicalDisk(_Total)\Avg. Disk sec/Read");
            _pdh.Add("diskWriteLatency", @"\PhysicalDisk(_Total)\Avg. Disk sec/Write");
            _pdh.Add("diskQueue", @"\PhysicalDisk(_Total)\Current Disk Queue Length");
            _pdh.Add("gpuEngines", @"\GPU Engine(*)\Utilization Percentage");
            _pdh.Add("gpuDedicated", @"\GPU Adapter Memory(*)\Dedicated Usage");
            _pdh.Add("gpuShared", @"\GPU Adapter Memory(*)\Shared Usage");
            _pdh.Add("netIn", @"\Network Interface(*)\Bytes Received/sec");
            _pdh.Add("netOut", @"\Network Interface(*)\Bytes Sent/sec");
            _pdh.Add("procIds", @"\Process(*)\ID Process");
            _pdh.Add("procCpu", @"\Process(*)\% Processor Time");
            _pdh.Add("procMemory", @"\Process(*)\Working Set - Private");
            _pdh.Add("procDisk", @"\Process(*)\IO Data Bytes/sec");
            _pdh.Add("procFaults", @"\Process(*)\Page Faults/sec");
            _pdh.Add("procThreads", @"\Process(*)\Thread Count");
            _pdh.Add("procHandles", @"\Process(*)\Handle Count");
        }

        public SystemSnapshot Sample()
        {
            if (_disposed) throw new ObjectDisposedException("WindowsSystemCollector");
            SystemSnapshot s = new SystemSnapshot();
            s.BaseClockMhz = BaseClockMhz;
            if (!_pdh.Collect())
            {
                s.CollectionWarning = "Windows не вернула данные счётчиков PDH.";
                return s;
            }

            s.CpuUsage = Clamp(Value(_pdh.GetDouble("cpu")), 0, 100);
            s.ProcessorQueue = Math.Max(0, Value(_pdh.GetDouble("queue")));
            double performance = Value(_pdh.GetDouble("cpuPerf"), 100);
            s.CpuClockMhz = BaseClockMhz > 0 ? Clamp(BaseClockMhz * performance / 100.0, 0, BaseClockMhz * 3) : 0;
            s.CoreUsage = ReadCores();

            MemoryStatusEx memory;
            if (MemoryStatusEx.TryRead(out memory))
            {
                s.MemoryTotalGB = memory.TotalPhysical / 1073741824.0;
                s.MemoryUsedGB = (memory.TotalPhysical - memory.AvailablePhysical) / 1073741824.0;
                s.MemoryUsage = memory.MemoryLoad;
                s.CommitLimitGB = memory.TotalPageFile / 1073741824.0;
                s.CommitUsedGB = (memory.TotalPageFile - memory.AvailablePageFile) / 1073741824.0;
            }
            s.CommitUsage = Clamp(Value(_pdh.GetDouble("commit")), 0, 100);
            s.PageFileUsage = Clamp(Value(_pdh.GetDouble("pageFile")), 0, 100);
            s.PageReadsPerSec = Math.Max(0, Value(_pdh.GetDouble("pageReads")));
            s.PageFaultsPerSec = Math.Max(0, Value(_pdh.GetDouble("pageFaults")));

            s.DiskActive = Clamp(100 - Value(_pdh.GetDouble("diskIdle"), 100), 0, 100);
            s.DiskReadBytesPerSec = Math.Max(0, Value(_pdh.GetDouble("diskRead")));
            s.DiskWriteBytesPerSec = Math.Max(0, Value(_pdh.GetDouble("diskWrite")));
            s.DiskReadLatencyMs = Math.Max(0, Value(_pdh.GetDouble("diskReadLatency")) * 1000);
            s.DiskWriteLatencyMs = Math.Max(0, Value(_pdh.GetDouble("diskWriteLatency")) * 1000);
            s.DiskQueueLength = Math.Max(0, Value(_pdh.GetDouble("diskQueue")));
            ReadNetwork(s);
            Dictionary<int, double> processGpu;
            ReadGpu(s, out processGpu);
            Dictionary<int, List<WindowDescriptor>> windows;
            int foregroundPid;
            ReadWindows(out windows, out foregroundPid);
            s.ForegroundProcessId = foregroundPid;
            s.Processes = ReadProcesses(processGpu, windows, foregroundPid);
            ReadVolumes(s);
            s.SystemDriveIsSsd = _systemDriveIsSsd;
            RefreshSlowValues(s);
            return s;
        }

        private double[] ReadCores()
        {
            Dictionary<string, double> values = _pdh.GetArray("cores");
            List<KeyValuePair<int, double>> cores = new List<KeyValuePair<int, double>>();
            foreach (KeyValuePair<string, double> pair in values)
            {
                int index;
                if (Int32.TryParse(pair.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                    cores.Add(new KeyValuePair<int, double>(index, Clamp(pair.Value, 0, 100)));
            }
            cores.Sort(delegate(KeyValuePair<int, double> a, KeyValuePair<int, double> b) { return a.Key.CompareTo(b.Key); });
            double[] result = new double[cores.Count];
            for (int i = 0; i < cores.Count; i++) result[i] = cores[i].Value;
            return result;
        }

        private void ReadNetwork(SystemSnapshot s)
        {
            s.NetworkInBytesPerSec = SumNetwork(_pdh.GetArray("netIn"));
            s.NetworkOutBytesPerSec = SumNetwork(_pdh.GetArray("netOut"));
        }

        private static double SumNetwork(Dictionary<string, double> values)
        {
            double sum = 0;
            foreach (KeyValuePair<string, double> pair in values)
            {
                string n = pair.Key.ToLowerInvariant();
                if (n.Contains("loopback") || n.Contains("isatap") || n.Contains("teredo") || n.Contains("tunnel")) continue;
                if (pair.Value > 0 && !Double.IsNaN(pair.Value) && !Double.IsInfinity(pair.Value)) sum += pair.Value;
            }
            return sum;
        }

        private void ReadGpu(SystemSnapshot s, out Dictionary<int, double> processGpu)
        {
            processGpu = new Dictionary<int, double>();
            Dictionary<string, double> engines = _pdh.GetArray("gpuEngines");
            Dictionary<string, double> adapters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, double> pair in engines)
            {
                double value = pair.Value > 0 && pair.Value < 1000 ? pair.Value : 0;
                string adapter = GetBetween(pair.Key, "luid_", "_eng_");
                if (adapter.Length == 0) adapter = "default";
                if (!adapters.ContainsKey(adapter)) adapters[adapter] = 0;
                adapters[adapter] += value;
                int pid = GetProcessIdFromGpuInstance(pair.Key);
                if (pid > 0)
                {
                    if (!processGpu.ContainsKey(pid)) processGpu[pid] = 0;
                    processGpu[pid] += value;
                }
            }
            double max = 0;
            foreach (double value in adapters.Values) max = Math.Max(max, Clamp(value, 0, 100));
            s.GpuUsage = max;
            s.GpuDedicatedUsedGB = Sum(_pdh.GetArray("gpuDedicated")) / 1073741824.0;
            s.GpuSharedUsedGB = Sum(_pdh.GetArray("gpuShared")) / 1073741824.0;
        }

        private IList<ProcessSample> ReadProcesses(Dictionary<int, double> processGpu, Dictionary<int, List<WindowDescriptor>> windows, int foregroundPid)
        {
            Dictionary<string, double> ids = _pdh.GetArray("procIds");
            Dictionary<string, double> cpu = _pdh.GetArray("procCpu");
            Dictionary<string, double> memory = _pdh.GetArray("procMemory");
            Dictionary<string, double> disk = _pdh.GetArray("procDisk");
            Dictionary<string, double> faults = _pdh.GetArray("procFaults");
            Dictionary<string, double> threads = _pdh.GetArray("procThreads");
            Dictionary<string, double> handles = _pdh.GetArray("procHandles");
            List<ProcessSample> result = new List<ProcessSample>();
            HashSet<int> seen = new HashSet<int>();
            int processors = Math.Max(1, Environment.ProcessorCount);
            foreach (KeyValuePair<string, double> pair in ids)
            {
                int pid = (int)Math.Round(pair.Value);
                if (pid <= 0 || !seen.Add(pid)) continue;
                ProcessMetadata metadata = GetProcessMetadata(pid, pair.Key, windows.ContainsKey(pid));
                ProcessSample p = new ProcessSample();
                p.Id = pid;
                p.StartTime = metadata.StartTime;
                p.Name = metadata.Name;
                p.Path = metadata.Path;
                p.Title = metadata.Title;
                p.CpuUsage = Clamp(Get(cpu, pair.Key) / processors, 0, 100);
                p.MemoryMB = Math.Max(0, Get(memory, pair.Key) / 1048576.0);
                p.DiskBytesPerSec = Math.Max(0, Get(disk, pair.Key));
                p.PageFaultsPerSec = Math.Max(0, Get(faults, pair.Key));
                p.ThreadCount = (int)Math.Max(0, Get(threads, pair.Key));
                p.HandleCount = (int)Math.Max(0, Get(handles, pair.Key));
                p.Responding = metadata.Responding;
                double gpu;
                if (processGpu.TryGetValue(pid, out gpu)) p.GpuUsage = Clamp(gpu, 0, 100);
                result.Add(p);
            }
            if (result.Count == 0) result.AddRange(ReadProcessesFallback(processGpu));
            foreach (ProcessSample process in result)
            {
                List<WindowDescriptor> processWindows;
                if (windows.TryGetValue(process.Id, out processWindows)) process.Windows = processWindows;
                process.IsForeground = process.Id == foregroundPid;
            }
            if (_processMetadata.Count > 600)
            {
                List<int> stale = new List<int>();
                foreach (int pid in _processMetadata.Keys) if (!seen.Contains(pid)) stale.Add(pid);
                foreach (int pid in stale) _processMetadata.Remove(pid);
            }
            result.Sort(delegate(ProcessSample a, ProcessSample b) { return b.CpuUsage.CompareTo(a.CpuUsage); });
            return result;
        }

        private IEnumerable<ProcessSample> ReadProcessesFallback(Dictionary<int, double> processGpu)
        {
            List<ProcessSample> result = new List<ProcessSample>();
            Process[] processes = Process.GetProcesses();
            foreach (Process process in processes)
            {
                using (process)
                {
                    try
                    {
                        ProcessMetadata metadata = GetProcessMetadata(process.Id, process.ProcessName, process.MainWindowHandle != IntPtr.Zero);
                        ProcessSample p = new ProcessSample();
                        p.Id = process.Id;
                        p.StartTime = metadata.StartTime;
                        p.Name = metadata.Name;
                        p.Title = metadata.Title;
                        p.Path = metadata.Path;
                        p.MemoryMB = process.WorkingSet64 / 1048576.0;
                        p.ThreadCount = process.Threads.Count;
                        p.HandleCount = process.HandleCount;
                        p.Responding = metadata.Responding;
                        double gpu;
                        if (processGpu.TryGetValue(process.Id, out gpu)) p.GpuUsage = Clamp(gpu, 0, 100);
                        result.Add(p);
                    }
                    catch { }
                }
            }
            return result;
        }

        private ProcessMetadata GetProcessMetadata(int pid, string instanceName, bool hasWindow)
        {
            ProcessMetadata cached;
            if (_processMetadata.TryGetValue(pid, out cached))
            {
                string currentName = instanceName;
                int nameHash = currentName.IndexOf('#');
                if (nameHash > 0) currentName = currentName.Substring(0, nameHash);
                if (!String.Equals(cached.Name, currentName, StringComparison.OrdinalIgnoreCase))
                {
                    _processMetadata.Remove(pid);
                    return GetProcessMetadata(pid, instanceName, hasWindow);
                }
                int identityIntervalSeconds = cached.StartTime == DateTime.MinValue ? 5 : 60;
                if (DateTime.UtcNow - cached.LastStartCheck > TimeSpan.FromSeconds(identityIntervalSeconds))
                {
                    if (IsProcessReused(pid, cached.StartTime))
                    {
                        _processMetadata.Remove(pid);
                        return GetProcessMetadata(pid, instanceName, hasWindow);
                    }
                    cached.LastStartCheck = DateTime.UtcNow;
                }
                if (!hasWindow) cached.Responding = null;
                else if (DateTime.UtcNow - cached.LastCheck > TimeSpan.FromSeconds(5))
                {
                    cached.Responding = null;
                    RefreshResponding(pid, cached);
                    cached.LastCheck = DateTime.UtcNow;
                }
                return cached;
            }
            string name = instanceName;
            int hash = name.IndexOf('#');
            if (hash > 0) name = name.Substring(0, hash);
            cached = new ProcessMetadata();
            cached.Name = name;
            cached.Title = name;
            cached.Path = "";
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    cached.Name = process.ProcessName;
                    cached.Title = process.ProcessName;
                    try { cached.StartTime = process.StartTime; } catch { }
                    string processPath;
                    if (TryReadProcessPath(pid, out processPath)) cached.Path = processPath;
                    else { try { cached.Path = process.MainModule.FileName; } catch { } }
                    if (hasWindow && process.MainWindowHandle != IntPtr.Zero) cached.Responding = process.Responding;
                }
            }
            catch { }
            if (cached.Path.Length > 0)
            {
                try
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(cached.Path);
                    if (!String.IsNullOrWhiteSpace(info.FileDescription)) cached.Title = info.FileDescription.Trim();
                }
                catch { }
            }
            _processMetadata[pid] = cached;
            cached.LastCheck = DateTime.UtcNow;
            cached.LastStartCheck = DateTime.UtcNow;
            return cached;
        }

        private static bool IsProcessReused(int pid, DateTime expectedStart)
        {
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    DateTime actualStart;
                    try { actualStart = process.StartTime; }
                    catch { return false; }
                    return expectedStart == DateTime.MinValue || actualStart != expectedStart;
                }
            }
            catch { return false; }
        }

        private static void RefreshResponding(int pid, ProcessMetadata metadata)
        {
            try
            {
                using (Process process = Process.GetProcessById(pid))
                    if (process.MainWindowHandle != IntPtr.Zero) metadata.Responding = process.Responding;
            }
            catch { }
        }

        private static bool TryReadProcessPath(int pid, out string path)
        {
            path = "";
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = OpenProcess(0x1000, false, pid);
                if (handle == IntPtr.Zero) return false;
                StringBuilder buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                if (!QueryFullProcessImageName(handle, 0, buffer, ref size)) return false;
                path = buffer.ToString();
                return path.Length > 0;
            }
            catch { return false; }
            finally { if (handle != IntPtr.Zero) CloseHandle(handle); }
        }

        private static void ReadWindows(out Dictionary<int, List<WindowDescriptor>> windows, out int foregroundPid)
        {
            Dictionary<int, List<WindowDescriptor>> collected = new Dictionary<int, List<WindowDescriptor>>();
            uint foreground;
            GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
            EnumWindows(delegate(IntPtr handle, IntPtr parameter)
            {
                if (!IsWindowVisible(handle)) return true;
                uint pid;
                GetWindowThreadProcessId(handle, out pid);
                if (pid == 0) return true;
                int length = GetWindowTextLength(handle);
                if (length <= 0 && (int)pid != (int)foreground) return true;
                StringBuilder title = new StringBuilder(Math.Min(1025, Math.Max(256, length + 2)));
                GetWindowText(handle, title, title.Capacity);
                List<WindowDescriptor> list;
                if (!collected.TryGetValue((int)pid, out list))
                {
                    list = new List<WindowDescriptor>();
                    collected[(int)pid] = list;
                }
                list.Add(new WindowDescriptor { Handle = handle, ProcessId = (int)pid, Title = title.Length > 0 ? title.ToString() : "Окно без названия", IsMinimized = IsIconic(handle) });
                return true;
            }, IntPtr.Zero);
            windows = collected;
            foregroundPid = (int)foreground;
        }

        private void ReadVolumes(SystemSnapshot s)
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    VolumeSample v = new VolumeSample();
                    v.Name = drive.Name;
                    v.Label = drive.VolumeLabel;
                    v.FileSystem = drive.DriveFormat;
                    v.TotalGB = drive.TotalSize / 1073741824.0;
                    v.FreeGB = drive.AvailableFreeSpace / 1073741824.0;
                    v.FreePercent = drive.TotalSize > 0 ? drive.AvailableFreeSpace * 100.0 / drive.TotalSize : 0;
                    s.Volumes.Add(v);
                    if (String.Equals(drive.Name, _systemRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        s.SystemDriveFreeGB = v.FreeGB;
                        s.SystemDriveFreePercent = v.FreePercent;
                        v.IsSsd = _systemDriveIsSsd;
                    }
                }
                catch { }
            }
        }

        private void RefreshSlowValues(SystemSnapshot s)
        {
            DateTime now = DateTime.Now;
            if (now - _temperatureAt > TimeSpan.FromSeconds(20))
            {
                _temperatureAt = now;
                _temperature = ReadTemperature();
            }
            s.CpuTemperature = _temperature;
            if (now - _powerAt > TimeSpan.FromSeconds(30))
            {
                _powerAt = now;
                _powerScheme = ReadPowerScheme();
            }
            s.PowerScheme = _powerScheme;
            s.OnBattery = ReadOnBattery();
        }

        private void ReadStaticInformation()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    if (key != null)
                    {
                        object name = key.GetValue("ProcessorNameString");
                        if (name != null) CpuName = Convert.ToString(name, CultureInfo.InvariantCulture).Trim();
                        object mhz = key.GetValue("~MHz");
                        if (mhz != null) BaseClockMhz = Convert.ToDouble(mhz, CultureInfo.InvariantCulture);
                    }
            }
            catch { }
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    if (key != null)
                    {
                        string product = Convert.ToString(key.GetValue("ProductName"), CultureInfo.InvariantCulture);
                        string build = Convert.ToString(key.GetValue("CurrentBuild"), CultureInfo.InvariantCulture);
                        string edition = Convert.ToString(key.GetValue("DisplayVersion"), CultureInfo.InvariantCulture);
                        OsName = product + " " + edition + " (сборка " + build + ")";
                    }
            }
            catch { }
            try
            {
                List<string> names = new List<string>();
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                using (ManagementObjectCollection items = searcher.Get())
                    foreach (ManagementObject item in items)
                    {
                        string name = Convert.ToString(item["Name"], CultureInfo.InvariantCulture);
                        if (!String.IsNullOrWhiteSpace(name)) names.Add(name.Trim());
                    }
                GpuNames = String.Join(", ", names.ToArray());
            }
            catch { }
        }

        private static double? ReadTemperature()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\wmi", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                using (ManagementObjectCollection items = searcher.Get())
                    foreach (ManagementObject item in items)
                    {
                        double celsius = Convert.ToDouble(item["CurrentTemperature"], CultureInfo.InvariantCulture) / 10.0 - 273.15;
                        if (celsius > -30 && celsius < 150) return celsius;
                    }
            }
            catch { }
            return null;
        }

        private bool? DetectSystemDriveSsd()
        {
            try
            {
                string diskNumber = null;
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\Microsoft\\Windows\\Storage", "SELECT DiskNumber, DriveLetter FROM MSFT_Partition"))
                using (ManagementObjectCollection items = searcher.Get())
                    foreach (ManagementObject item in items)
                    {
                        string letter = Convert.ToString(item["DriveLetter"], CultureInfo.InvariantCulture).TrimEnd('\\', ':');
                        if (String.Equals(letter, _systemRoot.TrimEnd('\\', ':'), StringComparison.OrdinalIgnoreCase))
                        {
                            diskNumber = Convert.ToString(item["DiskNumber"], CultureInfo.InvariantCulture);
                            break;
                        }
                    }
                if (diskNumber == null) return null;
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("root\\Microsoft\\Windows\\Storage", "SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk"))
                using (ManagementObjectCollection items = searcher.Get())
                    foreach (ManagementObject item in items)
                    {
                        if (!String.Equals(Convert.ToString(item["DeviceId"], CultureInfo.InvariantCulture), diskNumber, StringComparison.OrdinalIgnoreCase)) continue;
                        int type = Convert.ToInt32(item["MediaType"], CultureInfo.InvariantCulture);
                        return type == 4 || type == 5;
                    }
            }
            catch { }
            return null;
        }

        private static string ReadPowerScheme()
        {
            IntPtr scheme = IntPtr.Zero;
            try
            {
                if (PowerGetActiveScheme(IntPtr.Zero, out scheme) != 0 || scheme == IntPtr.Zero) return "";
                Guid guid = (Guid)Marshal.PtrToStructure(scheme, typeof(Guid));
                StringBuilder name = new StringBuilder(256);
                uint size = (uint)(name.Capacity * 2);
                if (PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, name, ref size) == 0)
                    return name.ToString();
            }
            catch { }
            finally { if (scheme != IntPtr.Zero) LocalFree(scheme); }
            return "";
        }

        private static bool ReadOnBattery()
        {
            try
            {
                SystemPowerStatus status;
                return GetSystemPowerStatus(out status) && status.ACLineStatus == 0;
            }
            catch { return false; }
        }

        private static double Get(Dictionary<string, double> values, string key)
        {
            double value;
            return values.TryGetValue(key, out value) ? Value(value) : 0;
        }

        private static double Sum(Dictionary<string, double> values)
        {
            double sum = 0;
            foreach (double value in values.Values) if (value > 0 && !Double.IsNaN(value) && !Double.IsInfinity(value)) sum += value;
            return sum;
        }

        private static double Value(double value, double fallback = 0)
        {
            return Double.IsNaN(value) || Double.IsInfinity(value) ? fallback : value;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value)) return min;
            return Math.Max(min, Math.Min(max, value));
        }

        private static int GetProcessIdFromGpuInstance(string value)
        {
            string text = GetBetween(value, "pid_", "_luid");
            int pid;
            return Int32.TryParse(text, out pid) ? pid : 0;
        }

        private static string GetBetween(string value, string start, string end)
        {
            int a = value.IndexOf(start, StringComparison.OrdinalIgnoreCase);
            if (a < 0) return "";
            a += start.Length;
            int b = value.IndexOf(end, a, StringComparison.OrdinalIgnoreCase);
            return b > a ? value.Substring(a, b - a) : "";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _pdh.Dispose();
            _processMetadata.Clear();
        }

        private sealed class ProcessMetadata
        {
            public string Name;
            public string Title;
            public string Path;
            public bool? Responding;
            public DateTime StartTime;
            public DateTime LastCheck;
            public DateTime LastStartCheck;
        }

        private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr handle);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr handle);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximumCount);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
            public static bool TryRead(out MemoryStatusEx status)
            {
                status = new MemoryStatusEx();
                status.Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
                return GlobalMemoryStatusEx(ref status);
            }
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte Reserved1;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr activePolicyGuid);
        [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
        private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subGroup, IntPtr powerSetting, StringBuilder buffer, ref uint bufferSize);
        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
