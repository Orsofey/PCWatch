using System.Windows;
using System.Windows.Input;
using PCWatch.ViewModels;

namespace PCWatch
{
    public partial class ProcessWindowsWindow : Window
    {
        public ProcessWindowsWindow() { InitializeComponent(); }
        private void CloseClick(object sender, RoutedEventArgs e) { Close(); }
        private void ActivateClick(object sender, RoutedEventArgs e) { ActivateSelected(); }
        private void ActivateDoubleClick(object sender, MouseButtonEventArgs e) { ActivateSelected(); }
        private void ActivateSelected()
        {
            MainViewModel model = DataContext as MainViewModel;
            if (model == null) return;
            if (model.SelectedWindow != null) model.ActivateWindowCommand.Execute(null);
            else if (model.OpenApplicationCommand.CanExecute(null)) model.OpenApplicationCommand.Execute(null);
            Close();
        }
    }
}
