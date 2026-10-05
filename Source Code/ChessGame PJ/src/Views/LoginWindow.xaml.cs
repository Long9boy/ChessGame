using System;
using System.Windows;
using System.Windows.Input;

namespace ChessGame_PJ.Views
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => txtUsername.Focus();
        }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            lblError.Text = "";
            string username = txtUsername.Text.Trim();
            string password = txtPassword.Password;

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

            btnLogin.IsEnabled = false;
            btnRegister.IsEnabled = false;
            btnLogin.Content = "Đang đăng nhập...";

            try
            {
                var (success, message) = await AuthService.LoginAsync(username, password, chkRememberMe.IsChecked ?? true);
                if (success)
                {
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
                btnLogin.IsEnabled = true;
                btnRegister.IsEnabled = true;
                btnLogin.Content = "Đăng Nhập";
            }
        }

        private void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            var registerWindow = new RegisterWindow { Owner = this };
            if (registerWindow.ShowDialog() == true)
            {
                DialogResult = true;
                Close();
            }
        }

        private void Txt_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnLogin_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}