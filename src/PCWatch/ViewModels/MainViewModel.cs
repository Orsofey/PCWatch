using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PCWatch.Core;
using PCWatch.Services;

namespace PCWatch.ViewModels
{
    public sealed class MainViewModel : ObservableObject, IDisposable
    {
        private readonly MonitoringService _monitor = new MonitoringService();
        private readonly SettingsService _settingsService = new SettingsService();
        private readonly IconService _icons = new IconService();
        private readonly EtwAnalysisService _etw = new EtwAnalysisService();
        private readonly Dictionary<int, ProcessItemViewModel> _processMap = new Dictionary<int, ProcessItemViewModel>();
        private readonly List<double> _cpu = new List<double>();
        private readonly List<double> _ram = new List<double>();
        private readonly List<double> _disk = new List<double>();
        private readonly List<double> _gpu = new List<double>();
        private readonly List<double> _net = new List<double>();
        private readonly List<double> _pageFile = new List<double>();
        private SystemSnapshot _latest = new SystemSnapshot();
        private string _pageTitle = "Обзор";
        private string _pageSubtitle = "Производительность и отзывчивость системы в реальном времени";
        private int _selectedPage;
        private int _sampleCount;
        private string _searchText = "";
        private string _sortMode = "ЦП";
        private ProcessItemViewModel _selectedProcess;
        private RecommendationItemViewModel _selectedRecommendation;
        private WindowDescriptor _selectedWindow;
        private string _recommendationSummary = "Анализ появится после нескольких замеров";
        private string _statusMessage = "Запуск мониторинга…";
        private int _healthScore = 100;
        private string _healthText = "Собираю данные";
        private string _healthSummary = "Первый диагноз появится после нескольких измерений.";
        private Brush _healthBrush = Brushes.MediumSeaGreen;
        private int _processCount;
        private string _cpuValue = "—", _ramValue = "—", _diskValue = "—", _gpuValue = "—", _netValue = "—", _pageValue = "—";
        private string _cpuSubtitle = "Ожидание данных", _ramSubtitle = "", _diskSubtitle = "", _gpuSubtitle = "", _netSubtitle = "", _pageFileSubtitle = "";
        private string _clockStats = "", _temperatureText = "н/д", _lastUpdate = "—", _processInfo = "";
        private double[] _cpuHistory = new double[0], _ramHistory = new double[0], _diskHistory = new double[0], _gpuHistory = new double[0], _netHistory = new double[0], _pageFileHistory = new double[0];
        private double _networkMaximum = 1024;
        private bool _isAnalyzing;
        private int _analysisProgress;
        private string _analysisStatus = "Готов к расширенному анализу";
        private AdvancedAnalysisResult _advancedResult;
        private string _fragmentationResult = "Анализ не запускался. Для SSD дефрагментация не выполняется.";
        private bool _fragmentationAnalyzing;
        private bool _disposed;

        public MainViewModel(AppSettings settings)
        {
            Settings = settings;
            IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            Processes = new ObservableCollection<ProcessItemViewModel>();
            TopProcesses = new ObservableCollection<ProcessItemViewModel>();
            Issues = new ObservableCollection<DiagnosticIssue>();
            Drivers = new ObservableCollection<DriverLatencySample>();
            Volumes = new ObservableCollection<VolumeSample>();
            Notifications = new ObservableCollection<AlertEvent>();
            Recommendations = new ObservableCollection<RecommendationItemViewModel>();
            ProcessesView = CollectionViewSource.GetDefaultView(Processes);
            ProcessesView.Filter = delegate(object item)
            {
                ProcessItemViewModel row = item as ProcessItemViewModel;
                return row != null && row.Matches(SearchText);
            };
            ApplySort();
            NavigateCommand = new RelayCommand(delegate(object p) { Navigate(Convert.ToString(p)); });
            TerminateProcessCommand = new RelayCommand(delegate(object p) { TerminateProcess(); }, delegate { return SelectedProcess != null; });
            OpenLocationCommand = new RelayCommand(delegate(object p) { OpenLocation(); }, delegate { return SelectedProcess != null; });
            PropertiesCommand = new RelayCommand(delegate(object p) { ShowProperties(); }, delegate { return SelectedProcess != null; });
            WebSearchCommand = new RelayCommand(delegate(object p) { WebSearch(); }, delegate { return SelectedProcess != null; });
            RunAnalysisCommand = new RelayCommand(async delegate(object p) { await RunAnalysis(); }, delegate { return !IsAnalyzing; });
            ExportHtmlCommand = new RelayCommand(delegate(object p) { ExportReport(true); });
            ExportTextCommand = new RelayCommand(delegate(object p) { ExportReport(false); });
            CopyReportCommand = new RelayCommand(delegate(object p) { CopyReport(); });
            SaveSettingsCommand = new RelayCommand(delegate(object p) { SaveSettings(); });
            RunAsAdminCommand = new RelayCommand(delegate(object p) { RunAsAdmin(); }, delegate { return !IsAdmin; });
            EmergencyCommand = new RelayCommand(delegate(object p) { ShowEmergency(); });
            OpenToolCommand = new RelayCommand(delegate(object p) { OpenTool(Convert.ToString(p)); });
            AnalyzeFragmentationCommand = new RelayCommand(async delegate(object p) { await AnalyzeFragmentation(); });
            OpenApplicationCommand = new RelayCommand(delegate(object p) { OpenApplication(); }, delegate { return SelectedProcess != null; });
            ShowWindowsCommand = new RelayCommand(delegate(object p) { ShowProcessWindows(); }, delegate { return SelectedProcess != null; });
            ActivateWindowCommand = new RelayCommand(delegate(object p) { ActivateSelectedWindow(); }, delegate { return SelectedWindow != null; });
            SelectRecommendationCommand = new RelayCommand(delegate(object p) { SelectRecommendation(); }, delegate { return SelectedRecommendation != null; });
            CloseRecommendationCommand = new RelayCommand(delegate(object p) { CloseRecommendation(); }, delegate { return SelectedRecommendation != null; });
            HideRecommendationCommand = new RelayCommand(delegate(object p) { HideRecommendation(); }, delegate { return SelectedRecommendation != null; });
            ResetHiddenRecommendationsCommand = new RelayCommand(delegate(object p) { ResetHiddenRecommendations(); });
            _monitor.SnapshotAvailable += OnSnapshot;
            _monitor.AlertTriggered += OnAlert;
            _monitor.CollectionFailed += OnCollectionFailed;
            _icons.IconReady += OnIconReady;
            _monitor.Start(settings);
        }

        public event Action<string, string> NotificationRequested;
        public AppSettings Settings { get; private set; }
        public ObservableCollection<ProcessItemViewModel> Processes { get; private set; }
        public ObservableCollection<ProcessItemViewModel> TopProcesses { get; private set; }
        public ObservableCollection<DiagnosticIssue> Issues { get; private set; }
        public ObservableCollection<DriverLatencySample> Drivers { get; private set; }
        public ObservableCollection<VolumeSample> Volumes { get; private set; }
        public ObservableCollection<AlertEvent> Notifications { get; private set; }
        public ObservableCollection<RecommendationItemViewModel> Recommendations { get; private set; }
        public ICollectionView ProcessesView { get; private set; }
        public ICommand NavigateCommand { get; private set; }
        public ICommand TerminateProcessCommand { get; private set; }
        public ICommand OpenLocationCommand { get; private set; }
        public ICommand PropertiesCommand { get; private set; }
        public ICommand WebSearchCommand { get; private set; }
        public ICommand RunAnalysisCommand { get; private set; }
        public ICommand ExportHtmlCommand { get; private set; }
        public ICommand ExportTextCommand { get; private set; }
        public ICommand CopyReportCommand { get; private set; }
        public ICommand SaveSettingsCommand { get; private set; }
        public ICommand RunAsAdminCommand { get; private set; }
        public ICommand EmergencyCommand { get; private set; }
        public ICommand OpenToolCommand { get; private set; }
        public ICommand AnalyzeFragmentationCommand { get; private set; }
        public ICommand OpenApplicationCommand { get; private set; }
        public ICommand ShowWindowsCommand { get; private set; }
        public ICommand ActivateWindowCommand { get; private set; }
        public ICommand SelectRecommendationCommand { get; private set; }
        public ICommand CloseRecommendationCommand { get; private set; }
        public ICommand HideRecommendationCommand { get; private set; }
        public ICommand ResetHiddenRecommendationsCommand { get; private set; }
        public bool IsAdmin { get; private set; }
        public string AdminModeText { get { return IsAdmin ? "Администратор" : "Обычный режим"; } }
        public string CpuName { get { return String.IsNullOrWhiteSpace(_monitor.CpuName) ? "Определение процессора…" : _monitor.CpuName; } }
        public string OsName { get { return _monitor.OsName ?? ""; } }
        public string GpuNames { get { return _monitor.GpuNames ?? ""; } }
        public SystemSnapshot Latest { get { return _latest; } private set { Set(ref _latest, value, "Latest"); } }
        public string PageTitle { get { return _pageTitle; } private set { Set(ref _pageTitle, value, "PageTitle"); } }
        public string PageSubtitle { get { return _pageSubtitle; } private set { Set(ref _pageSubtitle, value, "PageSubtitle"); } }
        public int SelectedPage { get { return _selectedPage; } private set { Set(ref _selectedPage, value, "SelectedPage"); Raise("DashboardVisible"); Raise("ProcessesVisible"); Raise("ResourcesVisible"); Raise("DiagnosticsVisible"); Raise("ReportsVisible"); Raise("SettingsVisible"); Raise("RecommendationsVisible"); } }
        public Visibility DashboardVisible { get { return SelectedPage == 0 ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility ProcessesVisible { get { return SelectedPage == 1 ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility ResourcesVisible { get { return SelectedPage == 2 ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility DiagnosticsVisible { get { return SelectedPage == 3 ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility ReportsVisible { get { return SelectedPage == 4 ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility SettingsVisible { get { return SelectedPage == 5 ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility RecommendationsVisible { get { return SelectedPage == 6 ? Visibility.Visible : Visibility.Collapsed; } }
        public string SearchText { get { return _searchText; } set { if (Set(ref _searchText, value, "SearchText")) ProcessesView.Refresh(); } }
        public string SortMode { get { return _sortMode; } set { if (Set(ref _sortMode, value, "SortMode")) ApplySort(); } }
        public ProcessItemViewModel SelectedProcess { get { return _selectedProcess; } set { Set(ref _selectedProcess, value, "SelectedProcess"); } }
        public RecommendationItemViewModel SelectedRecommendation { get { return _selectedRecommendation; } set { Set(ref _selectedRecommendation, value, "SelectedRecommendation"); } }
        public WindowDescriptor SelectedWindow { get { return _selectedWindow; } set { Set(ref _selectedWindow, value, "SelectedWindow"); } }
        public string RecommendationSummary { get { return _recommendationSummary; } private set { Set(ref _recommendationSummary, value, "RecommendationSummary"); } }
        public string StatusMessage { get { return _statusMessage; } private set { Set(ref _statusMessage, value, "StatusMessage"); } }
        public int HealthScore { get { return _healthScore; } private set { Set(ref _healthScore, value, "HealthScore"); } }
        public string HealthText { get { return _healthText; } private set { Set(ref _healthText, value, "HealthText"); } }
        public string HealthSummary { get { return _healthSummary; } private set { Set(ref _healthSummary, value, "HealthSummary"); } }
        public Brush HealthBrush { get { return _healthBrush; } private set { Set(ref _healthBrush, value, "HealthBrush"); } }
        public int ProcessCount { get { return _processCount; } private set { Set(ref _processCount, value, "ProcessCount"); } }
        public string CpuValue { get { return _cpuValue; } private set { Set(ref _cpuValue, value, "CpuValue"); } }
        public string RamValue { get { return _ramValue; } private set { Set(ref _ramValue, value, "RamValue"); } }
        public string DiskValue { get { return _diskValue; } private set { Set(ref _diskValue, value, "DiskValue"); } }
        public string GpuValue { get { return _gpuValue; } private set { Set(ref _gpuValue, value, "GpuValue"); } }
        public string NetValue { get { return _netValue; } private set { Set(ref _netValue, value, "NetValue"); } }
        public string PageValue { get { return _pageValue; } private set { Set(ref _pageValue, value, "PageValue"); } }
        public string CpuSubtitle { get { return _cpuSubtitle; } private set { Set(ref _cpuSubtitle, value, "CpuSubtitle"); } }
        public string RamSubtitle { get { return _ramSubtitle; } private set { Set(ref _ramSubtitle, value, "RamSubtitle"); } }
        public string DiskSubtitle { get { return _diskSubtitle; } private set { Set(ref _diskSubtitle, value, "DiskSubtitle"); } }
        public string GpuSubtitle { get { return _gpuSubtitle; } private set { Set(ref _gpuSubtitle, value, "GpuSubtitle"); } }
        public string NetSubtitle { get { return _netSubtitle; } private set { Set(ref _netSubtitle, value, "NetSubtitle"); } }
        public string PageFileSubtitle { get { return _pageFileSubtitle; } private set { Set(ref _pageFileSubtitle, value, "PageFileSubtitle"); } }
        public string ClockStats { get { return _clockStats; } private set { Set(ref _clockStats, value, "ClockStats"); } }
        public string TemperatureText { get { return _temperatureText; } private set { Set(ref _temperatureText, value, "TemperatureText"); } }
        public string LastUpdate { get { return _lastUpdate; } private set { Set(ref _lastUpdate, value, "LastUpdate"); } }
        public string ProcessInfo { get { return _processInfo; } private set { Set(ref _processInfo, value, "ProcessInfo"); } }
        public double[] CpuHistory { get { return _cpuHistory; } private set { Set(ref _cpuHistory, value, "CpuHistory"); } }
        public double[] RamHistory { get { return _ramHistory; } private set { Set(ref _ramHistory, value, "RamHistory"); } }
        public double[] DiskHistory { get { return _diskHistory; } private set { Set(ref _diskHistory, value, "DiskHistory"); } }
        public double[] GpuHistory { get { return _gpuHistory; } private set { Set(ref _gpuHistory, value, "GpuHistory"); } }
        public double[] NetHistory { get { return _netHistory; } private set { Set(ref _netHistory, value, "NetHistory"); } }
        public double[] PageFileHistory { get { return _pageFileHistory; } private set { Set(ref _pageFileHistory, value, "PageFileHistory"); } }
        public double NetworkMaximum { get { return _networkMaximum; } private set { Set(ref _networkMaximum, value, "NetworkMaximum"); } }
        public bool IsAnalyzing { get { return _isAnalyzing; } private set { Set(ref _isAnalyzing, value, "IsAnalyzing"); Raise("AnalysisButtonText"); } }
        public int AnalysisProgress { get { return _analysisProgress; } private set { Set(ref _analysisProgress, value, "AnalysisProgress"); } }
        public string AnalysisStatus { get { return _analysisStatus; } private set { Set(ref _analysisStatus, value, "AnalysisStatus"); } }
        public string AnalysisButtonText { get { return IsAnalyzing ? "Анализ выполняется…" : "Запустить ETW-анализ · 60 с"; } }
        public AdvancedAnalysisResult AdvancedResult { get { return _advancedResult; } private set { Set(ref _advancedResult, value, "AdvancedResult"); Raise("AdvancedVisibility"); Raise("MaxDpcText"); Raise("MaxIsrText"); Raise("DispatchText"); Raise("HardFaultText"); } }
        public Visibility AdvancedVisibility { get { return AdvancedResult == null ? Visibility.Collapsed : Visibility.Visible; } }
        public string MaxDpcText { get { return AdvancedResult == null ? "—" : RussianFormat.Milliseconds(AdvancedResult.MaxDpcMs); } }
        public string MaxIsrText { get { return AdvancedResult == null ? "—" : RussianFormat.Milliseconds(AdvancedResult.MaxIsrMs); } }
        public string DispatchText { get { return AdvancedResult == null ? "—" : RussianFormat.Milliseconds(AdvancedResult.MaxDispatchLatencyMs); } }
        public string HardFaultText { get { return AdvancedResult == null ? "—" : AdvancedResult.HardPageFaults.ToString("N0", RussianFormat.Culture); } }
        public string FragmentationResult { get { return _fragmentationResult; } private set { Set(ref _fragmentationResult, value, "FragmentationResult"); } }

        private void Navigate(string page)
        {
            switch ((page ?? "").ToLowerInvariant())
            {
                case "processes": SelectedPage = 1; PageTitle = "Процессы"; PageSubtitle = "Нагрузка приложений и безопасное управление процессами"; break;
                case "recommendations": SelectedPage = 6; PageTitle = "Оптимизация"; PageSubtitle = "Обоснованные кандидаты на закрытие и способы освободить ресурсы"; break;
                case "resources": SelectedPage = 2; PageTitle = "Ресурсы"; PageSubtitle = "Процессор, память, диски, видеокарта и сеть"; break;
                case "diagnostics": SelectedPage = 3; PageTitle = "Диагностика"; PageSubtitle = "DPC, ISR, драйверы и отзывчивость ядра через ETW"; break;
                case "reports": SelectedPage = 4; PageTitle = "Отчёты"; PageSubtitle = "Заключение, доказательства и рекомендации по системе"; break;
                case "settings": SelectedPage = 5; PageTitle = "Настройки"; PageSubtitle = "Мониторинг, уведомления, журнал и автозапуск"; break;
                default: SelectedPage = 0; PageTitle = "Обзор"; PageSubtitle = "Производительность и отзывчивость системы в реальном времени"; break;
            }
        }

        private void OnSnapshot(SystemSnapshot snapshot)
        {
            Dispatch(delegate
            {
                Latest = snapshot;
                AddHistory(_cpu, snapshot.CpuUsage); AddHistory(_ram, snapshot.MemoryUsage); AddHistory(_disk, snapshot.DiskActive);
                AddHistory(_gpu, snapshot.GpuUsage); AddHistory(_net, snapshot.NetworkInBytesPerSec + snapshot.NetworkOutBytesPerSec);
                AddHistory(_pageFile, snapshot.PageFileUsage);
                CpuHistory = _cpu.ToArray(); RamHistory = _ram.ToArray(); DiskHistory = _disk.ToArray(); GpuHistory = _gpu.ToArray(); NetHistory = _net.ToArray(); PageFileHistory = _pageFile.ToArray();
                double maxNetwork = 1024;
                foreach (double value in _net) maxNetwork = Math.Max(maxNetwork, value);
                NetworkMaximum = maxNetwork * 1.15;
                CpuValue = RussianFormat.Percent(snapshot.CpuUsage);
                RamValue = RussianFormat.Percent(snapshot.MemoryUsage);
                DiskValue = RussianFormat.Percent(snapshot.DiskActive);
                GpuValue = RussianFormat.Percent(snapshot.GpuUsage);
                NetValue = RussianFormat.BytesPerSec(snapshot.NetworkInBytesPerSec + snapshot.NetworkOutBytesPerSec);
                PageValue = RussianFormat.Percent(snapshot.PageFileUsage);
                TemperatureText = snapshot.CpuTemperature.HasValue ? RussianFormat.Number(snapshot.CpuTemperature.Value) + " °C" : "н/д";
                CpuSubtitle = RussianFormat.MegaHertz(snapshot.CpuClockMhz) + " · " + TemperatureText;
                RamSubtitle = RussianFormat.GigaBytes(snapshot.MemoryUsedGB) + " из " + RussianFormat.GigaBytes(snapshot.MemoryTotalGB);
                DiskSubtitle = RussianFormat.BytesPerSec(snapshot.DiskReadBytesPerSec + snapshot.DiskWriteBytesPerSec) + " · " + RussianFormat.Milliseconds(Math.Max(snapshot.DiskReadLatencyMs, snapshot.DiskWriteLatencyMs));
                GpuSubtitle = RussianFormat.GigaBytes(snapshot.GpuDedicatedUsedGB) + " выделенной памяти";
                NetSubtitle = "↓ " + RussianFormat.BytesPerSec(snapshot.NetworkInBytesPerSec) + "  ↑ " + RussianFormat.BytesPerSec(snapshot.NetworkOutBytesPerSec);
                PageFileSubtitle = "Commit " + RussianFormat.Percent(snapshot.CommitUsage) + " · страницы " + RussianFormat.Rate(snapshot.PageReadsPerSec);
                LastUpdate = snapshot.Timestamp.ToString("HH:mm:ss");
                ProcessInfo = "Ядер: " + snapshot.CoreUsage.Length + " · Очередь ЦП: " + RussianFormat.Number(snapshot.ProcessorQueue) + " · Питание: " + (String.IsNullOrWhiteSpace(snapshot.PowerScheme) ? "н/д" : snapshot.PowerScheme);
                StatusMessage = String.IsNullOrWhiteSpace(snapshot.CollectionWarning) ? "Мониторинг активен" : snapshot.CollectionWarning;
                _sampleCount++;
                if (_sampleCount % 2 == 1) UpdateProcesses(snapshot.Processes);
                if (_sampleCount % 10 == 1 || Volumes.Count == 0) { Volumes.Clear(); foreach (VolumeSample volume in snapshot.Volumes) Volumes.Add(volume); }
                if (_sampleCount % 6 == 0) ProcessesView.Refresh();
                if (_sampleCount % 5 == 0 || Issues.Count == 0) UpdateDiagnostics();
                if (_sampleCount % 10 == 0) UpdateRecommendations();
                if (_sampleCount <= 2) { Raise("CpuName"); Raise("OsName"); Raise("GpuNames"); }
            });
        }

        private void UpdateProcesses(IList<ProcessSample> samples)
        {
            HashSet<int> active = new HashSet<int>();
            foreach (ProcessSample sample in samples)
            {
                active.Add(sample.Id);
                ProcessItemViewModel row;
                if (_processMap.TryGetValue(sample.Id, out row)) row.Update(sample);
                else
                {
                    row = new ProcessItemViewModel(sample);
                    row.Icon = _icons.GetCached(sample.Path, sample.Name);
                    _processMap.Add(sample.Id, row);
                    Processes.Add(row);
                }
                if (row.Icon == null) _icons.Request(sample.Path, sample.Name);
            }
            for (int i = Processes.Count - 1; i >= 0; i--)
            {
                int pid = Processes[i].Id;
                if (active.Contains(pid)) continue;
                _processMap.Remove(pid);
                if (Object.ReferenceEquals(SelectedProcess, Processes[i])) SelectedProcess = null;
                Processes.RemoveAt(i);
            }
            ProcessCount = samples.Count;
            List<ProcessSample> top = new List<ProcessSample>(samples);
            top.Sort(delegate(ProcessSample a, ProcessSample b) { return b.CpuUsage.CompareTo(a.CpuUsage); });
            TopProcesses.Clear();
            for (int i = 0; i < Math.Min(6, top.Count); i++)
            {
                ProcessItemViewModel row;
                if (_processMap.TryGetValue(top[i].Id, out row)) TopProcesses.Add(row);
            }
        }

        private void UpdateRecommendations()
        {
            SystemSnapshot[] all = _monitor.History.Snapshot();
            int start = Math.Max(0, all.Length - 30);
            List<SystemSnapshot> window = new List<SystemSnapshot>(all.Length - start);
            for (int i = start; i < all.Length; i++) window.Add(all[i]);
            RecommendationResult result = RecommendationEngine.Evaluate(window, Process.GetCurrentProcess().Id, Settings.HiddenRecommendations);
            Recommendations.Clear();
            int safe = 0;
            foreach (RecommendationItem item in result.Items)
            {
                ProcessItemViewModel row;
                _processMap.TryGetValue(item.Process.Id, out row);
                Recommendations.Add(new RecommendationItemViewModel(item, row));
                if (item.IsSafeCandidate) safe++;
            }
            RecommendationSummary = result.Items.Count == 0
                ? "Явных кандидатов не найдено. Активные окна и важные фоновые задачи исключены."
                : safe + " безопасных кандидатов · потенциально до " + RussianFormat.Number(result.PotentialMemoryMB) + " МБ памяти · всего предложений: " + result.Items.Count;
        }

        private void UpdateDiagnostics()
        {
            SystemSnapshot[] all = _monitor.History.Snapshot();
            int start = Math.Max(0, all.Length - 60);
            List<SystemSnapshot> window = new List<SystemSnapshot>(all.Length - start);
            for (int i = start; i < all.Length; i++) window.Add(all[i]);
            IList<DiagnosticIssue> result = DiagnosticsEngine.Analyze(window);
            Issues.Clear(); foreach (DiagnosticIssue issue in result) Issues.Add(issue);
            HealthScore = DiagnosticsEngine.HealthScore(result);
            HealthStatus status = DiagnosticsEngine.WorstStatus(result);
            if (status == HealthStatus.Critical) { HealthText = "Критическое состояние"; HealthBrush = new SolidColorBrush(Color.FromRgb(255, 113, 135)); }
            else if (status == HealthStatus.Warning) { HealthText = "Есть проблемы"; HealthBrush = new SolidColorBrush(Color.FromRgb(255, 185, 104)); }
            else if (status == HealthStatus.Good) { HealthText = "Хорошее состояние"; HealthBrush = new SolidColorBrush(Color.FromRgb(106, 143, 255)); }
            else { HealthText = "Система работает стабильно"; HealthBrush = new SolidColorBrush(Color.FromRgb(77, 219, 160)); }
            HealthSummary = result.Count > 0 ? result[0].Title + ". " + result[0].Evidence : "Данных пока недостаточно.";
            if (window.Count > 0)
            {
                double min = Double.MaxValue, max = 0, sum = 0;
                int validClockSamples = 0;
                foreach (SystemSnapshot item in window)
                {
                    if (item.CpuClockMhz <= 0) continue;
                    min = Math.Min(min, item.CpuClockMhz); max = Math.Max(max, item.CpuClockMhz); sum += item.CpuClockMhz; validClockSamples++;
                }
                ClockStats = validClockSamples == 0 ? "Частота: н/д" : "Частота min " + RussianFormat.MegaHertz(min) + " · max " + RussianFormat.MegaHertz(max) + " · средняя " + RussianFormat.MegaHertz(sum / validClockSamples);
            }
        }

        private void OnAlert(AlertEvent alert)
        {
            Dispatch(delegate
            {
                Notifications.Insert(0, alert);
                while (Notifications.Count > 30) Notifications.RemoveAt(Notifications.Count - 1);
            });
            Action<string, string> handler = NotificationRequested;
            if (handler != null) handler(alert.Title, alert.Message);
        }

        private void OnCollectionFailed(string message) { Dispatch(delegate { StatusMessage = "Ошибка сбора: " + message; }); }

        private void OnIconReady(string key, ImageSource icon)
        {
            Dispatch(delegate
            {
                foreach (ProcessItemViewModel row in Processes)
                    if (String.Equals(row.IconKey, key, StringComparison.OrdinalIgnoreCase)) row.Icon = icon;
                foreach (RecommendationItemViewModel recommendation in Recommendations) recommendation.RefreshIcon();
            });
        }

        private void ApplySort()
        {
            if (ProcessesView == null) return;
            ProcessesView.SortDescriptions.Clear();
            string property = SortMode == "Память" ? "Sample.MemoryMB" : SortMode == "Диск" ? "Sample.DiskBytesPerSec" : SortMode == "Имя" ? "Title" : "Sample.CpuUsage";
            ProcessesView.SortDescriptions.Add(new SortDescription(property, SortMode == "Имя" ? ListSortDirection.Ascending : ListSortDirection.Descending));
            ProcessesView.Refresh();
        }

        private void TerminateProcess()
        {
            ProcessItemViewModel row = SelectedProcess;
            if (row == null) return;
            string reason;
            if (!ProcessSafety.CanTerminate(row.Sample, Process.GetCurrentProcess().Id, out reason))
            {
                MessageBox.Show(reason, "PCWatch · защита процессов", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Process target = null;
            try
            {
                target = Process.GetProcessById(row.Id);
                if (target.HasExited || !ProcessSafety.IsCurrentIdentity(target, row.Sample))
                {
                    MessageBox.Show("Процесс изменился или PID был повторно использован. Обновите список и выберите приложение снова.", "PCWatch · защита процессов", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                MessageBoxResult answer = MessageBox.Show("Завершить «" + row.Title + "» (PID " + row.Id + ")?\n\nНесохранённые данные будут потеряны.", "PCWatch · подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;
                if (target.HasExited || !ProcessSafety.IsCurrentIdentity(target, row.Sample))
                {
                    MessageBox.Show("Процесс изменился во время подтверждения. Действие отменено.", "PCWatch · защита процессов", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                target.Kill();
                StatusMessage = "Процесс " + row.Name + " завершён";
            }
            catch (Exception ex) { MessageBox.Show("Не удалось завершить процесс: " + ex.Message + (IsAdmin ? "" : "\nПопробуйте режим администратора."), "PCWatch", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally
            {
                if (target != null) target.Dispose();
            }
        }

        private void OpenApplication()
        {
            if (SelectedProcess == null) return;
            if (SelectedProcess.Sample.Windows != null && SelectedProcess.Sample.Windows.Count > 0)
            {
                WindowDescriptor window = SelectedProcess.Sample.Windows[0];
                bool activated = WindowActivationService.Activate(window.Handle);
                StatusMessage = activated ? "Окно приложения активировано" : "Windows заблокировала смену окна — оно подсвечено в панели задач";
                return;
            }
            string path = SelectedProcess.ResolvedPath;
            if (String.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show("Исполняемый файл приложения не найден. Проверьте свойства процесса или откройте его вручную.", "PCWatch", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                StatusMessage = "Запущено: " + SelectedProcess.Title;
            }
            catch (Exception ex) { MessageBox.Show("Не удалось открыть приложение: " + ex.Message, "PCWatch", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void ShowProcessWindows()
        {
            if (SelectedProcess == null) return;
            StatusMessage = "Окон у процесса: " + SelectedProcess.WindowCount;
            ProcessWindowsWindow window = new ProcessWindowsWindow();
            window.DataContext = this;
            window.Owner = Application.Current.MainWindow;
            window.Show();
            window.Activate();
        }

        private void ActivateSelectedWindow()
        {
            if (SelectedWindow == null) return;
            bool activated = WindowActivationService.Activate(SelectedWindow.Handle);
            StatusMessage = activated ? "Окно активировано" : "Windows заблокировала смену окна — оно подсвечено в панели задач";
        }

        private void SelectRecommendation()
        {
            if (SelectedRecommendation == null) return;
            SelectedProcess = SelectedRecommendation.ProcessRow;
            Navigate("processes");
        }

        private void CloseRecommendation()
        {
            if (SelectedRecommendation == null) return;
            SelectedProcess = SelectedRecommendation.ProcessRow;
            TerminateProcess();
        }

        private void HideRecommendation()
        {
            if (SelectedRecommendation == null) return;
            string name = KnownApplications.Normalize(SelectedRecommendation.Name);
            if (!Settings.HiddenRecommendations.Contains(name)) Settings.HiddenRecommendations.Add(name);
            _settingsService.Save(Settings);
            UpdateRecommendations();
        }

        private void ResetHiddenRecommendations()
        {
            Settings.HiddenRecommendations.Clear();
            _settingsService.Save(Settings);
            UpdateRecommendations();
            StatusMessage = "Скрытые рекомендации восстановлены";
        }

        private void OpenLocation()
        {
            if (SelectedProcess == null) return;
            string path = SelectedProcess.ResolvedPath;
            if (String.IsNullOrWhiteSpace(path)) { MessageBox.Show("Путь к исполняемому файлу не найден.", "PCWatch", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            Process.Start("explorer.exe", "/select,\"" + path + "\"");
        }

        private void ShowProperties()
        {
            if (SelectedProcess == null) return;
            string path = SelectedProcess.ResolvedPath;
            if (String.IsNullOrWhiteSpace(path)) { MessageBox.Show("Путь к исполняемому файлу не найден.", "PCWatch", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            Process.Start(new ProcessStartInfo { FileName = path, Verb = "properties", UseShellExecute = true });
        }

        private void WebSearch()
        {
            if (SelectedProcess == null) return;
            Process.Start("https://www.google.com/search?q=" + Uri.EscapeDataString(SelectedProcess.Name + ".exe что это за процесс"));
        }

        private async Task RunAnalysis()
        {
            if (!IsAdmin)
            {
                MessageBoxResult result = MessageBox.Show("ETW-анализ DPC/ISR требует права администратора. Перезапустить PCWatch?", "PCWatch · расширенная диагностика", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes) RunAsAdmin();
                return;
            }
            if (IsAnalyzing) return;
            IsAnalyzing = true; AnalysisProgress = 0; AnalysisStatus = "Запуск отдельной ETW-сессии…";
            Drivers.Clear(); AdvancedResult = null;
            try
            {
                Progress<int> progress = new Progress<int>(delegate(int value) { AnalysisProgress = value; AnalysisStatus = "Сбор DPC, ISR и событий планировщика: " + value + "%"; });
                AdvancedAnalysisResult result = await _etw.AnalyzeAsync(60, progress);
                AdvancedResult = result;
                foreach (DriverLatencySample driver in result.Drivers) Drivers.Add(driver);
                AnalysisStatus = result.Summary + (String.IsNullOrWhiteSpace(result.Warning) ? "" : " " + result.Warning);
                AnalysisProgress = 100;
            }
            catch (Exception ex) { AnalysisStatus = "Не удалось выполнить ETW-анализ: " + ex.Message + ". Закройте другие программы kernel-трассировки и повторите."; }
            finally { IsAnalyzing = false; }
        }

        private void ExportReport(bool html)
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = html ? "HTML-отчёт|*.html" : "Текстовый отчёт|*.txt";
            dialog.FileName = "PCWatch-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + (html ? ".html" : ".txt");
            if (dialog.ShowDialog() != true) return;
            string content = BuildReport(html);
            File.WriteAllText(dialog.FileName, content, new UTF8Encoding(true));
            StatusMessage = "Отчёт сохранён: " + dialog.FileName;
            try { Process.Start("explorer.exe", "/select,\"" + dialog.FileName + "\""); } catch { }
        }

        private string BuildReport(bool html)
        {
            SystemSnapshot[] all = _monitor.History.Snapshot();
            List<SystemSnapshot> history = new List<SystemSnapshot>(all);
            List<DiagnosticIssue> issues = new List<DiagnosticIssue>(Issues);
            return html ? ReportBuilder.BuildHtml(history, issues, AdvancedResult, CpuName, OsName, GpuNames) : ReportBuilder.BuildText(history, issues, AdvancedResult, CpuName, OsName, GpuNames);
        }

        private void CopyReport()
        {
            Clipboard.SetText(BuildReport(false));
            StatusMessage = "Текстовый отчёт скопирован в буфер обмена";
        }

        private void SaveSettings()
        {
            try
            {
                Settings.SampleIntervalMs = Math.Max(250, Math.Min(10000, Settings.SampleIntervalMs));
                _settingsService.Save(Settings);
                ApplyAutostart();
                _monitor.UpdateSettings(Settings);
                StatusMessage = "Настройки сохранены";
            }
            catch (Exception ex) { MessageBox.Show("Не удалось сохранить настройки: " + ex.Message, "PCWatch", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void ApplyAutostart()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (key == null) return;
                if (Settings.StartWithWindows) key.SetValue("PCWatch", "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"");
                else key.DeleteValue("PCWatch", false);
            }
        }

        private void RunAsAdmin()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = Process.GetCurrentProcess().MainModule.FileName, Verb = "runas", UseShellExecute = true });
                Application.Current.Shutdown();
            }
            catch { }
        }

        private void ShowEmergency()
        {
            EmergencyWindow window = new EmergencyWindow();
            window.DataContext = this;
            window.Show(); window.Activate();
        }

        private static void OpenTool(string tool)
        {
            try
            {
                switch (tool)
                {
                    case "taskmgr": Process.Start("taskmgr.exe"); break;
                    case "events": Process.Start("eventvwr.exe"); break;
                    case "power": Process.Start("control.exe", "powercfg.cpl"); break;
                    case "programs": Process.Start("appwiz.cpl"); break;
                    case "disks": Process.Start("dfrgui.exe"); break;
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "PCWatch", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private async Task AnalyzeFragmentation()
        {
            if (_fragmentationAnalyzing) return;
            if (Latest.SystemDriveIsSsd == true)
            {
                FragmentationResult = "Системный диск является SSD. Анализ фрагментации пропущен: дефрагментация SSD не нужна.";
                return;
            }
            MessageBoxResult confirm = MessageBox.Show("Запустить только чтение и анализ системного диска через defrag /A?\nОптимизация и перемещение файлов выполняться не будут.", "PCWatch · анализ диска", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            _fragmentationAnalyzing = true;
            FragmentationResult = "Анализ диска выполняется…";
            try
            {
                string output = await Task.Factory.StartNew(delegate
                {
                    ProcessStartInfo info = new ProcessStartInfo("defrag.exe", Environment.SystemDirectory.Substring(0, 2) + " /A /V /H");
                    info.UseShellExecute = false; info.CreateNoWindow = true; info.RedirectStandardOutput = true; info.RedirectStandardError = true;
                    using (Process process = Process.Start(info))
                    {
                        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                        Task<string> stderr = process.StandardError.ReadToEndAsync();
                        process.WaitForExit();
                        Task.WaitAll(stdout, stderr);
                        return stdout.Result + stderr.Result;
                    }
                });
                FragmentationResult = String.IsNullOrWhiteSpace(output) ? "Windows не вернула отчёт о фрагментации." : output.Trim();
            }
            catch (Exception ex) { FragmentationResult = "Анализ недоступен: " + ex.Message; }
            finally { _fragmentationAnalyzing = false; }
        }

        public void SetBackgroundMode(bool isBackground)
        {
            _monitor.BackgroundMode = isBackground;
        }

        public void ShowStatus(string message)
        {
            StatusMessage = message;
        }

        private static void AddHistory(List<double> values, double value)
        {
            values.Add(value);
            if (values.Count > 120) values.RemoveAt(0);
        }

        private static void Dispatch(Action action)
        {
            if (Application.Current == null) return;
            if (Application.Current.Dispatcher.CheckAccess()) action();
            else
            {
                try { Application.Current.Dispatcher.BeginInvoke(action); } catch (TaskCanceledException) { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _monitor.SnapshotAvailable -= OnSnapshot;
            _monitor.AlertTriggered -= OnAlert;
            _monitor.CollectionFailed -= OnCollectionFailed;
            _icons.IconReady -= OnIconReady;
            _etw.Cancel();
            _monitor.Dispose();
            _icons.Dispose();
        }
    }
}
