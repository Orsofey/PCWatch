using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows;
using PCWatch.Core;
using PCWatch.ViewModels;
using WinForms = System.Windows.Forms;

namespace PCWatch
{
    public partial class App : Application
    {
        private MainViewModel _viewModel;
        private MainWindow _window;
        private WinForms.NotifyIcon _tray;
        private Mutex _mutex;
        private bool _exiting;
        private bool _trayHintShown;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args)
            {
                LogError(args.Exception);
                args.Handled = true;
                MessageBox.Show("Ошибка интерфейса PCWatch:\n" + args.Exception.Message, "PCWatch", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            bool created;
            _mutex = new Mutex(true, @"Local\PCWatch-2B77B27C-Orsofey", out created);
            if (!created)
            {
                MessageBox.Show("PCWatch уже запущен. Откройте его через значок в системном трее.", "PCWatch", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
            try
            {
                AppSettings settings = new SettingsService().Load();
                _viewModel = new MainViewModel(settings);
                _viewModel.NotificationRequested += OnNotification;
                _window = new MainWindow { DataContext = _viewModel };
                MainWindow = _window;
                CreateTray();
                _window.Show();
                OpenWindowsFromArguments(Environment.GetCommandLineArgs());
            }
            catch (Exception ex)
            {
                LogError(ex);
                MessageBox.Show("PCWatch не запустился:\n" + ex, "PCWatch", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private static void LogError(Exception error)
        {
            try
            {
                string folder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCWatch");
                System.IO.Directory.CreateDirectory(folder);
                System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "startup-error.log"), DateTime.Now.ToString("O") + Environment.NewLine + error);
            }
            catch { }
        }

        private async void OpenWindowsFromArguments(string[] args)
        {
            try
            {
                int pid = 0;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--windows" && i + 1 < args.Length) Int32.TryParse(args[i + 1], out pid);
                    else if (args[i].StartsWith("--windows=", StringComparison.OrdinalIgnoreCase)) Int32.TryParse(args[i].Substring(10), out pid);
                }
                if (pid <= 0) return;
                _viewModel.ShowStatus("Ожидаю список окон процесса " + pid + "…");
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    await System.Threading.Tasks.Task.Delay(500);
                    foreach (ProcessItemViewModel process in _viewModel.Processes)
                    {
                        if (process.Id != pid) continue;
                        _viewModel.SelectedProcess = process;
                        _viewModel.ShowWindowsCommand.Execute(null);
                        return;
                    }
                }
                _viewModel.ShowStatus("Процесс с PID " + pid + " не найден");
            }
            catch (Exception ex) { LogError(ex); }
        }

        private void CreateTray()
        {
            _tray = new WinForms.NotifyIcon();
            _tray.Text = "PCWatch — мониторинг активен";
            try { _tray.Icon = Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName); }
            catch { _tray.Icon = SystemIcons.Application; }
            WinForms.ContextMenuStrip menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Открыть PCWatch", null, delegate { Dispatcher.BeginInvoke(new Action(ShowMainWindow)); });
            menu.Items.Add("Emergency Mode", null, delegate { Dispatcher.BeginInvoke(new Action(delegate { if (_viewModel != null) _viewModel.EmergencyCommand.Execute(null); })); });
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Выход", null, delegate { Dispatcher.BeginInvoke(new Action(ExitApplication)); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { Dispatcher.BeginInvoke(new Action(ShowMainWindow)); };
            _tray.Visible = true;
        }

        private void ShowMainWindow()
        {
            if (_window == null) return;
            _viewModel.SetBackgroundMode(false);
            _window.Show();
            _window.WindowState = WindowState.Normal;
            _window.Activate();
        }

        public void WindowMinimized()
        {
            if (_viewModel == null || !_viewModel.Settings.MinimizeToTray || _tray == null) return;
            _viewModel.SetBackgroundMode(true);
            _window.Hide();
            if (!_trayHintShown)
            {
                _tray.ShowBalloonTip(2500, "PCWatch продолжает работу", "Двойной щелчок по значку откроет окно. Мониторинг остаётся активным.", WinForms.ToolTipIcon.Info);
                _trayHintShown = true;
            }
        }

        public void WindowClosed()
        {
            if (!_exiting) Shutdown();
        }

        private void OnNotification(string title, string message)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (_tray != null) _tray.ShowBalloonTip(5000, title, message, WinForms.ToolTipIcon.Warning);
            }));
        }

        private void ExitApplication()
        {
            _exiting = true;
            if (_window != null) _window.Close();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _exiting = true;
            if (_viewModel != null)
            {
                _viewModel.NotificationRequested -= OnNotification;
                _viewModel.Dispose();
            }
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            if (_mutex != null)
            {
                try { _mutex.ReleaseMutex(); } catch { }
                _mutex.Dispose();
            }
            base.OnExit(e);
        }
    }
}
