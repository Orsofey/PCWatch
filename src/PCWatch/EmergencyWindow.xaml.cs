using System.Windows;
using System.Windows.Input;

namespace PCWatch
{
    public partial class EmergencyWindow : Window
    {
        public EmergencyWindow() { InitializeComponent(); }
        private void CloseClick(object sender, RoutedEventArgs e) { Close(); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.Key == Key.Escape) Close(); }
    }
}
