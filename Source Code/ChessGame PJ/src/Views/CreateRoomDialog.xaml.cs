using System.Windows;

namespace ChessGame_PJ.Views
{
    public partial class CreateRoomDialog : Window
    {
        public int SelectedTimeMinutes { get; private set; } = 10;
        public bool EnableMoveHints { get; private set; } = true;
        public bool IsCustomRoom { get; private set; } = false;

        public CreateRoomDialog()
        {
            InitializeComponent();
        }

        private void RbMode_Checked(object sender, RoutedEventArgs e)
        {
            if (pnlTimeOptions == null) return;
            pnlTimeOptions.Visibility = (rbModeCustom?.IsChecked == true) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            IsCustomRoom = rbModeCustom.IsChecked == true;

            if (rb10m.IsChecked == true) SelectedTimeMinutes = 10;
            else if (rb15m.IsChecked == true) SelectedTimeMinutes = 15;
            else if (rb20m.IsChecked == true) SelectedTimeMinutes = 20;

            EnableMoveHints = chkMoveHints.IsChecked == true;

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
