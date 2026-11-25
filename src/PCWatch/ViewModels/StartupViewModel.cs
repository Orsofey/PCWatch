using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using PCWatch.Core;

namespace PCWatch.ViewModels
{
    public sealed class StartupItemViewModel : ObservableObject
    {
        private bool _selected;
        private readonly Action _changed;
        public StartupItemViewModel(StartupItem model, Action changed)
        {
            Model = model;
            _changed = changed;
        }
        public StartupItem Model { get; private set; }
        public string Id { get { return Model.Id; } }
        public string Name { get { return Model.Name; } }
        public string Location { get { return Model.Location; } }
        public string Command { get { return Model.Command; } }
        public string KindText
        {
            get
            {
                switch (Model.Kind)
                {
                    case StartupItemKind.StartupFolder: return "Папка Startup";
                    case StartupItemKind.Service: return "Служба";
                    case StartupItemKind.ScheduledTask: return "Планировщик";
                    default: return "Реестр";
                }
            }
        }
        public string StatusText { get { return Model.Enabled ? "Включено" : "Отключено"; } }
        public string AdminText { get { return Model.RequiresAdmin ? "Админ" : ""; } }
        public string Publisher { get { return Model.Publisher; } }
        public string SignatureStatus { get { return Model.SignatureStatus; } }
        public string ProcessState { get { return Model.ProcessState; } }
        public string ProtectionReason { get { return Model.ProtectionReason; } }
        public bool CanSelect { get { return Model.CanToggle; } }
        public bool IsSelected
        {
            get { return _selected; }
            set { if (Set(ref _selected, value, "IsSelected") && _changed != null) _changed(); }
        }
    }

    public sealed class StartupViewModel : ObservableObject
    {
        private readonly StartupService _service = new StartupService();
        private string _searchText = "";
        private bool _onlyDisabled;
        private bool _busy;
        private string _status = "Сканирование ещё не запускалось";
        private int _selectedCount;
        private StartupItemViewModel _selectedItem;

        public StartupViewModel()
        {
            Items = new ObservableCollection<StartupItemViewModel>();
            ItemsView = CollectionViewSource.GetDefaultView(Items);
            ItemsView.Filter = FilterItem;
            ScanCommand = new RelayCommand(async delegate(object p) { await ScanAsync(); }, delegate { return !_busy; });
            DisableSelectedCommand = new RelayCommand(async delegate(object p) { await ChangeSelectedAsync(false); }, delegate { return !_busy && SelectedCount > 0; });
            EnableSelectedCommand = new RelayCommand(async delegate(object p) { await ChangeSelectedAsync(true); }, delegate { return !_busy && SelectedCount > 0; });
            SelectAllCommand = new RelayCommand(delegate(object p) { SelectAll(); }, delegate { return !_busy; });
            ClearSelectionCommand = new RelayCommand(delegate(object p) { ClearSelection(); }, delegate { return !_busy; });
            OpenLocationCommand = new RelayCommand(delegate(object p) { OpenLocation(); }, delegate { return SelectedItem != null; });
            OpenBackupCommand = new RelayCommand(delegate(object p) { OpenBackup(); });
        }

        public ObservableCollection<StartupItemViewModel> Items { get; private set; }
        public ICollectionView ItemsView { get; private set; }
        public ICommand ScanCommand { get; private set; }
        public ICommand DisableSelectedCommand { get; private set; }
        public ICommand EnableSelectedCommand { get; private set; }
        public ICommand SelectAllCommand { get; private set; }
        public ICommand ClearSelectionCommand { get; private set; }
        public ICommand OpenLocationCommand { get; private set; }
        public ICommand OpenBackupCommand { get; private set; }
        public string SearchText { get { return _searchText; } set { if (Set(ref _searchText, value, "SearchText")) ItemsView.Refresh(); } }
        public bool OnlyDisabled { get { return _onlyDisabled; } set { if (Set(ref _onlyDisabled, value, "OnlyDisabled")) ItemsView.Refresh(); } }
        public bool IsBusy { get { return _busy; } private set { Set(ref _busy, value, "IsBusy"); } }
        public string Status { get { return _status; } private set { Set(ref _status, value, "Status"); } }
        public int SelectedCount { get { return _selectedCount; } private set { Set(ref _selectedCount, value, "SelectedCount"); Raise("SelectionText"); } }
        public string SelectionText { get { return "Выбрано: " + SelectedCount; } }
        public StartupItemViewModel SelectedItem { get { return _selectedItem; } set { Set(ref _selectedItem, value, "SelectedItem"); } }

        private bool FilterItem(object value)
        {
            StartupItemViewModel item = value as StartupItemViewModel;
            if (item == null) return false;
            if (OnlyDisabled && item.Model.Enabled) return false;
            if (String.IsNullOrWhiteSpace(SearchText)) return true;
            return item.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0
                || item.Command.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0
                || item.Location.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0
                || item.Publisher.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task ScanAsync()
        {
            if (_busy) return;
            IsBusy = true;
            Status = "Сканирование автозапуска…";
            try
            {
                IList<StartupItem> found = await Task.Run(delegate { return _service.Scan(); });
                Items.Clear();
                foreach (StartupItem model in found) Items.Add(new StartupItemViewModel(model, Recalculate));
                Recalculate();
                Status = "Найдено: " + Items.Count + " · включено: " + CountEnabled(true) + " · отключено: " + CountEnabled(false) + ". Процессы не завершаются.";
            }
            catch (Exception ex) { Status = "Ошибка сканирования: " + ex.Message; }
            finally { IsBusy = false; }
        }

        private int CountEnabled(bool enabled)
        {
            int count = 0;
            foreach (StartupItemViewModel item in Items) if (item.Model.Enabled == enabled) count++;
            return count;
        }

        private void Recalculate()
        {
            int count = 0;
            foreach (StartupItemViewModel item in Items) if (item.IsSelected) count++;
            SelectedCount = count;
        }

        private void SelectAll()
        {
            foreach (StartupItemViewModel item in ItemsView)
                if (item.CanSelect) item.IsSelected = true;
            Recalculate();
        }

        private void ClearSelection()
        {
            foreach (StartupItemViewModel item in Items) item.IsSelected = false;
            Recalculate();
        }

        private async Task ChangeSelectedAsync(bool enable)
        {
            if (_busy) return;
            List<StartupItemViewModel> selected = new List<StartupItemViewModel>();
            foreach (StartupItemViewModel item in Items) if (item.IsSelected && item.Model.Enabled != enable) selected.Add(item);
            if (selected.Count == 0) { Status = enable ? "Выберите отключённые записи для восстановления." : "Выберите включённые записи для отключения."; return; }
            string action = enable ? "восстановить" : "отключить";
            MessageBoxResult answer = MessageBox.Show("Подтвердите: " + action + " автозапуск для " + selected.Count + " записей.\n\nТекущие процессы и службы не будут остановлены. Перед изменением создаётся резервная копия, которую можно восстановить.", "PCWatch · автозапуск", MessageBoxButton.YesNo, enable ? MessageBoxImage.Question : MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
            IsBusy = true;
            int changed = 0, failed = 0;
            try
            {
                foreach (StartupItemViewModel item in selected)
                {
                    try
                    {
                        await Task.Run(delegate { if (enable) _service.Enable(item.Model); else _service.Disable(item.Model); });
                        item.Model.Enabled = enable;
                        changed++;
                    }
                    catch { failed++; }
                }
                Status = (enable ? "Восстановлено: " : "Отключено: ") + changed + (failed > 0 ? " · ошибок: " + failed + " (нужны права администратора или элемент защищён)" : "") + ". Сканирование обновлено.";
                IsBusy = false;
                await ScanAsync();
            }
            finally { IsBusy = false; Recalculate(); }
        }

        private void OpenLocation()
        {
            if (SelectedItem == null) return;
            try
            {
                switch (SelectedItem.Model.Kind)
                {
                    case StartupItemKind.RegistryRun: Process.Start("regedit.exe"); break;
                    case StartupItemKind.StartupFolder: Process.Start("explorer.exe", "/select,\"" + SelectedItem.Model.Location + "\""); break;
                    case StartupItemKind.Service: Process.Start("services.msc"); break;
                    case StartupItemKind.ScheduledTask: Process.Start("taskschd.msc"); break;
                }
            }
            catch (Exception ex) { Status = ex.Message; }
        }

        private static void OpenBackup()
        {
            try { Process.Start("explorer.exe", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCWatch")); }
            catch { }
        }
    }
}
