using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PCWatch.Core;

namespace PCWatch.ViewModels
{
    public sealed class CleanupItemViewModel : ObservableObject
    {
        private readonly CleanupCategoryViewModel _category;
        private bool _selected;
        public CleanupItemViewModel(CleanupItem model, CleanupCategoryViewModel category, ICommand actionCommand)
        {
            Model = model;
            _category = category;
            _selected = model.SelectedByDefault;
            ActionCommand = actionCommand;
        }
        public CleanupItem Model { get; private set; }
        public string Name { get { return Model.Name; } }
        public string Detail { get { return Model.Detail; } }
        public string SizeText { get { return RussianFormat.Bytes(Model.Size); } }
        public string PathsText
        {
            get
            {
                if (Model.Paths == null || Model.Paths.Count == 0) return "";
                if (Model.Paths.Count == 1) return Model.Paths[0];
                return Model.Paths[0] + " · ещё " + (Model.Paths.Count - 1);
            }
        }
        public bool CanSelect { get { return !_category.Blocked && (Model.Kind == CleanupKind.Files || Model.Kind == CleanupKind.RecycleBin); } }
        public bool IsSelected
        {
            get { return _selected; }
            set
            {
                if (!CanSelect) value = false;
                if (Set(ref _selected, value, "IsSelected")) _category.SelectionChanged();
            }
        }
        public bool HasAction { get { return Model.Kind == CleanupKind.WindowsOld || Model.Kind == CleanupKind.Hibernation; } }
        public string ActionText { get { return Model.Kind == CleanupKind.Hibernation ? "Отключить гибернацию" : "Открыть Очистку диска"; } }
        public ICommand ActionCommand { get; private set; }
    }

    public sealed class CleanupCategoryViewModel : ObservableObject
    {
        private readonly Action _changed;
        public CleanupCategoryViewModel(CleanupCategory model, Action changed, ICommand windowsOldCommand, ICommand hibernationCommand)
        {
            Model = model;
            _changed = changed;
            Items = new ObservableCollection<CleanupItemViewModel>();
            foreach (CleanupItem item in model.Items)
            {
                ICommand action = item.Kind == CleanupKind.Hibernation ? hibernationCommand : windowsOldCommand;
                Items.Add(new CleanupItemViewModel(item, this, action));
            }
            ToggleAllCommand = new RelayCommand(delegate(object p) { SetAll(!AllSelected); if (_changed != null) _changed(); });
        }
        public CleanupCategory Model { get; private set; }
        public ObservableCollection<CleanupItemViewModel> Items { get; private set; }
        public ICommand ToggleAllCommand { get; private set; }
        public string Title { get { return Model.Title; } }
        public string Note { get { return Model.Note; } }
        public bool Blocked { get { return Model.Blocked; } }
        public string SizeText { get { return RussianFormat.Bytes(Model.Size); } }
        public string SafetyLabel
        {
            get
            {
                if (Model.Blocked) return "Нужен администратор";
                if (Model.Safety == CleanupSafety.Safe) return "Безопасно";
                if (Model.Safety == CleanupSafety.Review) return "По решению";
                return "Только вручную";
            }
        }
        public Brush SafetyBrush
        {
            get
            {
                if (Model.Blocked) return Brushes.IndianRed;
                if (Model.Safety == CleanupSafety.Safe) return Brushes.MediumSeaGreen;
                if (Model.Safety == CleanupSafety.Review) return Brushes.Orange;
                return Brushes.CornflowerBlue;
            }
        }
        public bool HasSelectable { get { return !Blocked && Model.Safety != CleanupSafety.Manual; } }
        public long SelectedSize
        {
            get
            {
                long total = 0;
                foreach (CleanupItemViewModel item in Items) if (item.IsSelected) total += item.Model.Size;
                return total;
            }
        }
        public void SelectionChanged() { Raise("SelectedSize"); if (_changed != null) _changed(); }
        public void SetAll(bool selected)
        {
            foreach (CleanupItemViewModel item in Items) if (item.CanSelect) item.IsSelected = selected;
            SelectionChanged();
        }
        public bool AllSelected
        {
            get
            {
                bool any = false;
                foreach (CleanupItemViewModel item in Items)
                {
                    if (!item.CanSelect) continue;
                    any = true;
                    if (!item.IsSelected) return false;
                }
                return any;
            }
        }
    }

    public sealed class CleanupViewModel : ObservableObject, IDisposable
    {
        private readonly AppSettings _settings;
        private readonly SettingsService _settingsService = new SettingsService();
        private CancellationTokenSource _cancellation;
        private bool _busy;
        private bool _scanning;
        private bool _hasResults;
        private string _status = "Сканирование ещё не запускалось";
        private string _scanDetail = "";
        private double _progress;
        private long _selectedBytes;
        private long _safeBytes;
        private long _reviewBytes;
        private string _lastResult = "";
        private bool _hasLastResult;

        public CleanupViewModel(AppSettings settings, bool isAdmin)
        {
            _settings = settings;
            IsAdmin = isAdmin;
            Categories = new ObservableCollection<CleanupCategoryViewModel>();
            ScanCommand = new RelayCommand(async delegate(object p) { await ScanAsync(); }, delegate { return !_busy; });
            CleanCommand = new RelayCommand(async delegate(object p) { await CleanAsync(); }, delegate { return !_busy && SelectedBytes > 0; });
            CancelCommand = new RelayCommand(delegate(object p) { Cancel(); }, delegate { return _busy; });
            ToggleHibernationCommand = new RelayCommand(async delegate(object p) { await ToggleHibernationAsync(); }, delegate { return !_busy; });
            OpenWindowsCleanupCommand = new RelayCommand(delegate(object p) { OpenWindowsCleanup(); }, delegate { return !_busy; });
        }

        public ObservableCollection<CleanupCategoryViewModel> Categories { get; private set; }
        public ICommand ScanCommand { get; private set; }
        public ICommand CleanCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
        public ICommand ToggleHibernationCommand { get; private set; }
        public ICommand OpenWindowsCleanupCommand { get; private set; }
        public bool IsAdmin { get; private set; }
        public bool IsBusy { get { return _busy; } private set { Set(ref _busy, value, "IsBusy"); Raise("ScanButtonText"); Raise("CancelVisibility"); } }
        public bool IsScanning { get { return _scanning; } private set { Set(ref _scanning, value, "IsScanning"); } }
        public bool HasResults { get { return _hasResults; } private set { Set(ref _hasResults, value, "HasResults"); } }
        public string Status { get { return _status; } private set { Set(ref _status, value, "Status"); } }
        public string ScanDetail { get { return _scanDetail; } private set { Set(ref _scanDetail, value, "ScanDetail"); } }
        public double Progress { get { return _progress; } private set { Set(ref _progress, value, "Progress"); } }
        public long SelectedBytes { get { return _selectedBytes; } private set { Set(ref _selectedBytes, value, "SelectedBytes"); Raise("SelectedText"); Raise("CleanButtonText"); } }
        public string SelectedText { get { return RussianFormat.Bytes(SelectedBytes); } }
        public string SafeText { get { return RussianFormat.Bytes(_safeBytes); } }
        public string ReviewText { get { return RussianFormat.Bytes(_reviewBytes); } }
        public string CleanButtonText { get { return SelectedBytes > 0 ? "Очистить " + RussianFormat.Bytes(SelectedBytes) : "Ничего не выбрано"; } }
        public string ScanButtonText { get { return IsScanning ? "Сканирование…" : HasResults ? "Сканировать снова" : "Начать безопасное сканирование"; } }
        public string HibernationButtonText { get { return NativeCleanup.HibernationSize() > 0 ? "Отключить гибернацию" : "Включить гибернацию"; } }
        public string HibernationSizeText { get { long size = NativeCleanup.HibernationSize(); return size > 0 ? "hiberfil.sys: " + RussianFormat.Bytes(size) : "Гибернация отключена"; } }
        public string LastResult { get { return _lastResult; } private set { Set(ref _lastResult, value, "LastResult"); } }
        public bool HasLastResult { get { return _hasLastResult; } private set { Set(ref _hasLastResult, value, "HasLastResult"); } }
        public bool RecycleDownloads
        {
            get { return _settings.RecycleDownloads; }
            set
            {
                if (_settings.RecycleDownloads == value) return;
                _settings.RecycleDownloads = value;
                Raise("RecycleDownloads");
                try { _settingsService.Save(_settings); } catch { }
            }
        }
        public Visibility CancelVisibility { get { return IsBusy ? Visibility.Visible : Visibility.Collapsed; } }

        private async Task ScanAsync()
        {
            if (_busy) return;
            _cancellation = new CancellationTokenSource();
            IsBusy = true; IsScanning = true; Progress = 0;
            Status = "Сканирую выбранные безопасные места…";
            Categories.Clear();
            Progress<string> scanProgress = new Progress<string>(delegate(string message)
            {
                Progress = Math.Min(0.9, Progress + 0.02);
                Status = message;
            });
            try
            {
                IList<CleanupCategory> found = await Task.Run(delegate
                {
                    return CleanupScanner.Scan(IsAdmin, _cancellation.Token, ((IProgress<string>)scanProgress).Report);
                });
                Categories.Clear();
                foreach (CleanupCategory category in found)
                {
                    Categories.Add(new CleanupCategoryViewModel(category, Recalc, OpenWindowsCleanupCommand, ToggleHibernationCommand));
                }
                HasResults = Categories.Count > 0;
                Recalc();
                Progress = 1;
                Status = Categories.Count == 0 ? "Очищать нечего: безопасные кандидаты не найдены." : "Скан завершён. Проверьте пути и отметьте нужное.";
                ScanDetail = "Безопасно: " + SafeText + " · по решению: " + ReviewText;
            }
            catch (OperationCanceledException) { Status = "Сканирование отменено."; HasResults = Categories.Count > 0; }
            catch (Exception ex) { Status = "Ошибка сканирования: " + ex.Message; HasResults = Categories.Count > 0; }
            finally
            {
                IsScanning = false; IsBusy = false;
                if (_cancellation != null) { _cancellation.Dispose(); _cancellation = null; }
            }
        }

        private void ToggleCategory(CleanupCategoryViewModel category)
        {
            if (category == null) return;
            category.SetAll(!category.AllSelected);
            Recalc();
        }

        private void Recalc()
        {
            long selected = 0, safe = 0, review = 0;
            foreach (CleanupCategoryViewModel category in Categories)
            {
                selected += category.SelectedSize;
                if (category.Model.Safety == CleanupSafety.Safe && !category.Blocked) safe += category.Model.Size;
                else if (!category.Blocked && category.Model.Safety != CleanupSafety.Manual) review += category.Model.Size;
            }
            SelectedBytes = selected; _safeBytes = safe; _reviewBytes = review;
            Raise("SafeText"); Raise("ReviewText"); Raise("ScanDetail");
            Raise("HibernationButtonText"); Raise("HibernationSizeText");
        }

        private async Task CleanAsync()
        {
            if (_busy) return;
            List<CleanupItem> selected = new List<CleanupItem>();
            foreach (CleanupCategoryViewModel category in Categories)
                foreach (CleanupItemViewModel item in category.Items)
                    if (item.IsSelected) selected.Add(item.Model);
            if (selected.Count == 0) return;

            bool recycle = RecycleDownloads;
            bool emptyBin = false;
            foreach (CleanupItem item in selected) if (item.Kind == CleanupKind.RecycleBin) emptyBin = true;
            string warning = "Будут удалены только отмеченные пути. Кэши удаляются безвозвратно; старые загрузки " + (recycle ? "будут перемещены в Корзину." : "будут удалены безвозвратно.") + "\n\nЗанятые файлы будут пропущены. Продолжить?";
            if (emptyBin) warning += "\n\nВнимание: текущее содержимое Корзины также будет удалено безвозвратно.";
            MessageBoxResult answer = MessageBox.Show("Очистить " + RussianFormat.Bytes(SelectedBytes) + "?\n\n" + warning, "PCWatch · подтверждение очистки", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            _cancellation = new CancellationTokenSource();
            IsBusy = true; Progress = 0; Status = "Очистка…";
            Progress<CleanupProgress> cleanProgress = new Progress<CleanupProgress>(delegate(CleanupProgress value)
            {
                Progress = value.Fraction;
                Status = value.Message;
            });
            try
            {
                CleanupResult result = await Task.Run(delegate
                {
                    return CleanupService.Clean(selected, recycle, _cancellation.Token, cleanProgress);
                });
                LastResult = "Освобождено " + RussianFormat.Bytes(result.FreedBytes) + (result.RecycledBytes > 0 ? " · в Корзину: " + RussianFormat.Bytes(result.RecycledBytes) : "") + " · удалено файлов: " + result.DeletedFiles + (result.SkippedFiles > 0 ? " · пропущено: " + result.SkippedFiles : "");
                HasLastResult = true;
                Status = "Очистка завершена.";
                IsBusy = false;
                await ScanAsync();
            }
            catch (OperationCanceledException) { Status = "Очистка остановлена."; }
            catch (Exception ex) { Status = "Очистка прервана: " + ex.Message; }
            finally
            {
                IsBusy = false; IsScanning = false;
                if (_cancellation != null) { _cancellation.Dispose(); _cancellation = null; }
            }
        }

        private async Task ToggleHibernationAsync()
        {
            long size = NativeCleanup.HibernationSize();
            bool enabled = size > 0;
            string action = enabled ? "Отключить" : "Включить";
            MessageBoxResult answer = MessageBox.Show(action + " гибернацию Windows?" + (enabled ? "\n\nФайл hiberfil.sys (" + RussianFormat.Bytes(size) + ") будет удалён, быстрый запуск также отключится. Вернуть можно этой же кнопкой." : "\n\nWindows снова создаст hiberfil.sys и включит быстрый запуск."), "PCWatch · гибернация", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
            IsBusy = true; Status = action + " гибернацию…";
            try
            {
                bool ok = await Task.Run(delegate { return NativeCleanup.SetHibernation(!enabled); });
                Status = ok ? "Гибернация: " + (enabled ? "отключена" : "включена") : "Windows не изменила настройку гибернации.";
                IsBusy = false;
                if (ok) await ScanAsync();
            }
            catch (Exception ex) { Status = "Не удалось изменить гибернацию: " + ex.Message; }
            finally { IsBusy = false; }
        }

        private static void OpenWindowsCleanup()
        {
            try { System.Diagnostics.Process.Start("cleanmgr.exe"); }
            catch (Exception ex) { MessageBox.Show("Не удалось открыть Очистку диска: " + ex.Message, "PCWatch", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        public void Cancel()
        {
            if (_cancellation != null) { try { _cancellation.Cancel(); } catch { } }
        }

        public void Dispose()
        {
            Cancel();
        }
    }
}
