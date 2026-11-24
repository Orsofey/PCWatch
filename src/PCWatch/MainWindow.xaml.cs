using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCWatch.Services;
using PCWatch.ViewModels;

namespace PCWatch
{
    public partial class MainWindow : Window
    {
        private readonly HotkeyService _hotkey = new HotkeyService();

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += delegate
            {
                MainViewModel model = DataContext as MainViewModel;
                _hotkey.Register(this, model == null ? "Ctrl+Alt+F12" : model.Settings.EmergencyHotkey);
                _hotkey.Pressed += delegate { if (model != null) model.EmergencyCommand.Execute(null); };
            };
            StateChanged += delegate { if (WindowState == WindowState.Minimized) ((App)Application.Current).WindowMinimized(); };
            Closed += delegate { _hotkey.Dispose(); ((App)Application.Current).WindowClosed(); };
        }

        private void TitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { ToggleMaximize(); return; }
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void MinimizeClick(object sender, RoutedEventArgs e) { WindowState = WindowState.Minimized; }
        private void MaximizeClick(object sender, RoutedEventArgs e) { ToggleMaximize(); }
        private void CloseClick(object sender, RoutedEventArgs e) { Close(); }
        private void ToggleMaximize() { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }

        private void FindWindowsClick(object sender, RoutedEventArgs e)
        {
            MainViewModel model = DataContext as MainViewModel;
            if (model != null && model.ShowWindowsCommand.CanExecute(null)) model.ShowWindowsCommand.Execute(null);
        }

        private void NavChecked(object sender, RoutedEventArgs e)
        {
            RadioButton button = sender as RadioButton;
            MainViewModel model = DataContext as MainViewModel;
            if (button != null && model != null) model.NavigateCommand.Execute(button.CommandParameter);
        }

        private void ProcessRightClick(object sender, MouseButtonEventArgs e)
        {
            DataGrid grid = sender as DataGrid;
            if (grid == null) return;
            DataGridRow row = ItemsControl.ContainerFromElement(grid, e.OriginalSource as DependencyObject) as DataGridRow;
            if (row != null) grid.SelectedItem = row.Item;
        }

        private void ProcessDoubleClick(object sender, MouseButtonEventArgs e)
        {
            MainViewModel model = DataContext as MainViewModel;
            if (model != null && model.PropertiesCommand.CanExecute(null)) model.PropertiesCommand.Execute(null);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Delete && !(Keyboard.FocusedElement is TextBox))
            {
                MainViewModel model = DataContext as MainViewModel;
                if (model != null && model.TerminateProcessCommand.CanExecute(null)) model.TerminateProcessCommand.Execute(null);
            }
        }
    }
}
