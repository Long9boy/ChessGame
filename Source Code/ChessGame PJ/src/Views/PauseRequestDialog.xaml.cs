using System.Windows;

namespace ChessGame_PJ.Views
{
    public partial class PauseRequestDialog : Window
    {
        public int SelectedMinutes { get; private set; } = 1;

        public PauseRequestDialog()
        {
            InitializeComponent();
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            if (rb1m.IsChecked == true) SelectedMinutes = 1;
            else if (rb2m.IsChecked == true) SelectedMinutes = 2;
            else if (rb3m.IsChecked == true) SelectedMinutes = 3;
            else if (rb5m.IsChecked == true) SelectedMinutes = 5;

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
