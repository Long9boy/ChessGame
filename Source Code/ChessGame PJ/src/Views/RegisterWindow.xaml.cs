using System;
using System.Windows;

namespace ChessGame_PJ.Views
{
    public partial class RegisterWindow : Window
    {
        public RegisterWindow()
        {
            InitializeComponent();
        }

        private async void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            lblError.Text = "";
            string username = txtUsername.Text.Trim();
            string displayName = txtDisplayName.Text.Trim();
            string password = txtPassword.Password;
            string confirmPassword = txtConfirmPassword.Password;

            if (string.IsNullOrWhiteSpace(username))
            {
                lblError.Text = "Vui lòng nhập tên tài khoản.";
                txtUsername.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                lblError.Text = "Vui lòng nhập mật khẩu.";
                txtPassword.Focus();
                return;
            }

            if (password != confirmPassword)
            {
                lblError.Text = "Mật khẩu nhập lại không khớp!";
                txtConfirmPassword.Focus();
                return;
            }

            btnRegister.IsEnabled = false;
            btnBackToLogin.IsEnabled = false;
            btnRegister.Content = "Đang tạo tài khoản...";

            try
            {
                string defaultAvatar = ImageResources.GetRandomAvatarName();
                var (success, message) = await AuthService.RegisterAsync(username, password, displayName, defaultAvatar);
                if (success)
                {
                    MessageBox.Show("Chúc mừng bạn đã tạo tài khoản thành công!", "Thành công",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    lblError.Text = message;
                }
            }
            finally
            {
                btnRegister.IsEnabled = true;
                btnBackToLogin.IsEnabled = true;
                btnRegister.Content = "Tạo Tài Khoản";
            }
        }

        private void BtnBackToLogin_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}