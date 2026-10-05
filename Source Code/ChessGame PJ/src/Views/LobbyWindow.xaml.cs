using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ChessGame_PJ.Core;
using ChessGame_PJ.Services;

namespace ChessGame_PJ.Views
{
    public partial class LobbyWindow : Window
    {
        private const string IdPrefix = "Nhập ID: ";
        private string selectedAvatar = "avatar1.png";
        private readonly string guestName = $"Player#{Random.Shared.Next(1000, 9999)}";

        // Timer cho hiệu ứng viền xoay lấp lánh Top 1 ELO
        private DispatcherTimer? _rank1AnimTimer;
        private RotateTransform? _rank1RotTransform;

        // Timer cập nhật trạng thái online & polling lời mời phòng
        private DispatcherTimer? _heartbeatTimer;
        private RoomInvite? _currentInvite;

        public LobbyWindow()
        {
            InitializeComponent();
            Loaded += LobbyWindow_Loaded;
            Closed += LobbyWindow_Closed;
        }

        private void LobbyWindow_Closed(object? sender, EventArgs e)
        {
            _heartbeatTimer?.Stop();
            _rank1AnimTimer?.Stop();
        }

        private async void LobbyWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadStaticImages();

            // Default random avatar
            selectedAvatar = ImageResources.GetRandomAvatarName();
            imgAvatar.Source = ImageResources.GetAvatarImage(selectedAvatar);

            // Auto login
            if (!AuthService.IsLoggedIn)
            {
                await AuthService.TryAutoLoginAsync();
            }

            UpdateLoginUI();
            LoadSettings();

            // Load Leaderboards
            _ = LoadEloTop3Async();
            _ = LoadBotRecordsAsync();

            // Hiện ngay huy hiệu lời mời kết bạn đang chờ (không cần chờ heartbeat đầu tiên)
            _ = LoadFriendRequestsAsync();

            StartHeartbeatTimer();
        }

        // ── Load All High Quality Image Assets ─────────────────────────────────────
        private void LoadStaticImages()
        {
            // 1. Ảnh nền tĩnh dự phòng (frame 0 của video)
            try
            {
                var uri = new Uri("pack://application:,,,/assets/textures/lobby_bg.png", UriKind.Absolute);
                var sri = Application.GetResourceStream(uri);
                if (sri?.Stream != null)
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = sri.Stream;
                    bmp.EndInit();
                    bmp.Freeze();
                    imgBackground.Source = bmp;
                }
                else
                {
                    imgBackground.Source = ImageResources.GetImageByFileName("lobby_bg.png");
                }
            }
            catch { }

            // 1b. Khởi tạo Video nền lặp 30s liền mạch
            InitBackgroundVideo();

            // 2. Logo
            imgLogoKing.Source = ImageResources.GetImageByFileName("logo_king.png");

            // 3. Game Mode Button Images
            imgBtnTimTran.Source     = ImageResources.GetImageByFileName("btn_tim_tran.png");
            imgBtnChoiOffline.Source = ImageResources.GetImageByFileName("btn_choi_offline.png");
            imgBtnTaoPhong.Source    = ImageResources.GetImageByFileName("btn_tao_phong.png");
            imgBtnThamGia.Source     = ImageResources.GetImageByFileName("btn_tham_gia.png");
            imgBtnBxh.Source         = ImageResources.GetImageByFileName("btn_bxh.png");
        }


        // ── Settings ──────────────────────────────────────────────────────────────
        private bool _isSettingsLoaded = false;

        private void LoadSettings()
        {
            _isSettingsLoaded = false;
            try
            {
                // Tải lại cài đặt mới nhất từ file để đảm bảo không bị ghi đè bởi giá trị mặc định của control
                AppSettings.Load();
                var s = AppSettings.Current;
                sliderVolume.Value = s.Volume;
                lblVolume.Text = $"{s.Volume}%";

                chkFlipBoardForBlack.IsChecked = s.FlipBoardForBlack;

                rdoBoardBrownGold.IsChecked  = s.BoardColor == BoardColorTheme.BrownGold;
                rdoBoardBlackWhite.IsChecked = s.BoardColor == BoardColorTheme.BlackWhite;
                rdoBoardBlueWhite.IsChecked  = s.BoardColor == BoardColorTheme.BlueWhite;

                // Load piece preview images
                imgPrevClassic1.Source = ImageResources.GetPieceImageFromSet("king_white.png", "set1");
                imgPrevClassic2.Source = ImageResources.GetPieceImageFromSet("queen_white.png", "set1");
                imgPrevClassic3.Source = ImageResources.GetPieceImageFromSet("knight_black.png", "set1");

                imgPrevRoyal1.Source = ImageResources.GetPieceImageFromSet("king_white.png", "set2");
                imgPrevRoyal2.Source = ImageResources.GetPieceImageFromSet("queen_white.png", "set2");
                imgPrevRoyal3.Source = ImageResources.GetPieceImageFromSet("knight_black.png", "set2");

                imgPrevAngle1.Source = ImageResources.GetPieceImageFromSet("king_white.png", "set3");
                imgPrevAngle2.Source = ImageResources.GetPieceImageFromSet("queen_white.png", "set3");
                imgPrevAngle3.Source = ImageResources.GetPieceImageFromSet("knight_black.png", "set3");

                imgPrevAngle2_1.Source = ImageResources.GetPieceImageFromSet("king_white.png", "set4");
                imgPrevAngle2_2.Source = ImageResources.GetPieceImageFromSet("queen_white.png", "set4");
                imgPrevAngle2_3.Source = ImageResources.GetPieceImageFromSet("knight_black.png", "set4");

                imgPrevDragon1.Source = ImageResources.GetPieceImageFromSet("king_white.png", "set5");
                imgPrevDragon2.Source = ImageResources.GetPieceImageFromSet("queen_white.png", "set5");
                imgPrevDragon3.Source = ImageResources.GetPieceImageFromSet("knight_black.png", "set5");

                // Piece set selection
                string ps = s.PieceStyle;
                rdoPieceClassic.IsChecked     = ps == AppSettings.PieceStyleClassic;
                rdoPieceRoyal.IsChecked       = ps == AppSettings.PieceStyleRoyal;
                rdoPieceAngleDemon.IsChecked  = ps == AppSettings.PieceStyleAngleVsDemon;
                rdoPieceAngleDemon2.IsChecked = ps == AppSettings.PieceStyleAngleVsDemon2;
                rdoPieceDragonAge.IsChecked   = ps == AppSettings.PieceStyleDragonAge;
            }
            finally
            {
                _isSettingsLoaded = true;
            }
        }

        private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isSettingsLoaded) return;
            int v = (int)sliderVolume.Value;
            if (lblVolume != null) lblVolume.Text = $"{v}%";
            AppSettings.Current.Volume = v;
            AppSettings.Current.Save();
        }

        private void ChkFlipBoardForBlack_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsLoaded) return;
            AppSettings.Current.FlipBoardForBlack = chkFlipBoardForBlack.IsChecked == true;
            AppSettings.Current.Save();
        }

        // Cho phép bấm vào cả khung (không chỉ riêng ô checkbox nhỏ) để bật/tắt.
        private void FlipBoardRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            chkFlipBoardForBlack.IsChecked = !(chkFlipBoardForBlack.IsChecked == true);
        }

        private void RdoBoardColor_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsLoaded || rdoBoardBrownGold == null) return;
            if (rdoBoardBrownGold.IsChecked  == true) AppSettings.Current.BoardColor = BoardColorTheme.BrownGold;
            if (rdoBoardBlackWhite.IsChecked == true) AppSettings.Current.BoardColor = BoardColorTheme.BlackWhite;
            if (rdoBoardBlueWhite.IsChecked  == true) AppSettings.Current.BoardColor = BoardColorTheme.BlueWhite;
            AppSettings.Current.Save();
        }

        private void RdoPieceStyle_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsLoaded || rdoPieceClassic == null) return;
            if (rdoPieceClassic.IsChecked == true) AppSettings.Current.PieceStyle = AppSettings.PieceStyleClassic;
            else if (rdoPieceRoyal.IsChecked == true) AppSettings.Current.PieceStyle = AppSettings.PieceStyleRoyal;
            else if (rdoPieceAngleDemon.IsChecked == true) AppSettings.Current.PieceStyle = AppSettings.PieceStyleAngleVsDemon;
            else if (rdoPieceAngleDemon2.IsChecked == true) AppSettings.Current.PieceStyle = AppSettings.PieceStyleAngleVsDemon2;
            else if (rdoPieceDragonAge?.IsChecked == true) AppSettings.Current.PieceStyle = AppSettings.PieceStyleDragonAge;
            AppSettings.Current.Save();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            bool show = pnlSettings.Visibility != Visibility.Visible;
            CloseAllPopups();
            if (show)
            {
                pnlSettings.Visibility = Visibility.Visible;
                popupOverlay.Visibility = Visibility.Visible;
            }
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e) => CloseAllPopups();

        // ── Profile Area & Popup ──────────────────────────────────────
        public void UpdateLoginUI()
        {
            if (AuthService.IsLoggedIn && AuthService.CurrentUser != null)
            {
                // Show Logout button, hide Login button
                txtBtnAuth.Text = "Login";
                btnAuth.Visibility = Visibility.Collapsed;
                btnLogout.Visibility = Visibility.Visible;

                string accountName = !string.IsNullOrWhiteSpace(AuthService.CurrentUser.displayName)
                    ? AuthService.CurrentUser.displayName
                    : AuthService.CurrentUser.username;
                txtPlayerName.Text = (accountName ?? "").Trim();

                lblElo.Text = $"ELO {AuthService.CurrentUser.elo}";

                if (!string.IsNullOrEmpty(AuthService.CurrentUser.avatar))
                {
                    selectedAvatar = AuthService.CurrentUser.avatar;
                    imgAvatar.Source = ImageResources.GetAvatarImage(selectedAvatar);
                }
            }
            else
            {
                // Show Login button, hide Logout button
                txtBtnAuth.Text = "Login";
                btnAuth.Visibility = Visibility.Visible;
                btnLogout.Visibility = Visibility.Collapsed;

                txtPlayerName.Text = guestName;
                lblElo.Text = "ELO 1200";
            }
        }

        private void BtnAuth_Click(object sender, RoutedEventArgs e)
        {
            CloseAllPopups();
            // btnAuth is the Login button
            var loginWindow = new LoginWindow { Owner = this };
            if (loginWindow.ShowDialog() == true)
            {
                UpdateLoginUI();
            }
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            CloseAllPopups();
            var confirm = MessageBox.Show("Bạn có chắc chắn muốn đăng xuất khỏi tài khoản?", "Đăng xuất",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                AuthService.Logout();
                selectedAvatar = ImageResources.GetRandomAvatarName();
                imgAvatar.Source = ImageResources.GetAvatarImage(selectedAvatar);
                UpdateLoginUI();
            }
        }

        private void BorderAvatar_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => OpenProfilePopup();
        private void BtnViewProfile_Click(object sender, RoutedEventArgs e) => OpenProfilePopup();

        private void OpenProfilePopup()
        {
            CloseAllPopups();
            RefreshProfilePopup();
            pnlProfilePopup.Visibility = Visibility.Visible;
            popupOverlay.Visibility = Visibility.Visible;
            _ = LoadProfileMatchHistoryAsync();
        }

        private void RefreshProfilePopup()
        {
            imgProfileAvatarLarge.Source = ImageResources.GetAvatarImage(selectedAvatar);

            if (AuthService.IsLoggedIn && AuthService.CurrentUser != null)
            {
                var u = AuthService.CurrentUser;
                lblProfileName.Text = !string.IsNullOrWhiteSpace(u.displayName) ? u.displayName : u.username;
                lblProfileUsername.Text = $"@{u.username}";

                int total = u.wins + u.losses + u.draws;
                double wr = total > 0 ? (u.wins * 100.0 / total) : 0;

                lblProfileElo.Text     = u.elo.ToString();
                lblProfileWins.Text    = u.wins.ToString();
                lblProfileDraws.Text   = u.draws.ToString();
                lblProfileLosses.Text  = u.losses.ToString();
                lblProfileWinrate.Text = $"{wr:F1}%";

                // Bot records
                var botRecs = new List<string>();
                if (u.bestBotTimeEasy > 0)   botRecs.Add($"Dễ: {FormatTime(u.bestBotTimeEasy)}");
                if (u.bestBotTimeMedium > 0) botRecs.Add($"TB: {FormatTime(u.bestBotTimeMedium)}");
                if (u.bestBotTimeHard > 0)   botRecs.Add($"Khó: {FormatTime(u.bestBotTimeHard)}");
                if (u.bestBotTimeExpert > 0) botRecs.Add($"Siêu khó: {FormatTime(u.bestBotTimeExpert)}");

                if (botRecs.Count > 0)
                {
                    lblProfileBotRecord.Text = string.Join("   |   ", botRecs);
                    pnlProfileBotRecord.Visibility = Visibility.Visible;
                }
                else
                {
                    pnlProfileBotRecord.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                lblProfileName.Text = guestName;
                lblProfileUsername.Text = "(Khách - Chưa đăng nhập)";
                lblProfileElo.Text = "1200";
                lblProfileWins.Text = "0";
                lblProfileDraws.Text = "0";
                lblProfileLosses.Text = "0";
                lblProfileWinrate.Text = "0%";
                pnlProfileBotRecord.Visibility = Visibility.Collapsed;
            }

            // Populate avatar selection grid
            PopulateAvatarPickerGrid();
        }

        private void PopulateAvatarPickerGrid()
        {
            pnlAvatarPickerGrid.Children.Clear();
            var allAvatars = ImageResources.GetAllAvatarNames();

            foreach (var avName in allAvatars)
            {
                bool isSelected = string.Equals(avName, selectedAvatar, StringComparison.OrdinalIgnoreCase);

                var avBorder = new Border
                {
                    Width = 46,
                    Height = 46,
                    CornerRadius = new CornerRadius(23),
                    BorderThickness = new Thickness(isSelected ? 2.5 : 1.5),
                    BorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(255, 215, 0)) : new SolidColorBrush(Color.FromRgb(55, 65, 95)),
                    Background = new SolidColorBrush(Color.FromRgb(28, 35, 60)),
                    Margin = new Thickness(4),
                    Cursor = Cursors.Hand,
                    Tag = avName
                };

                var avImg = new Image
                {
                    Source = ImageResources.GetAvatarImage(avName),
                    Width = 42,
                    Height = 42,
                    Stretch = Stretch.UniformToFill
                };
                avImg.Clip = new EllipseGeometry(new Point(21, 21), 20, 20);

                avBorder.Child = avImg;
                avBorder.MouseLeftButtonUp += async (s, e) =>
                {
                    if (s is Border b && b.Tag is string chosen)
                    {
                        selectedAvatar = chosen;
                        imgAvatar.Source = ImageResources.GetAvatarImage(selectedAvatar);
                        imgProfileAvatarLarge.Source = ImageResources.GetAvatarImage(selectedAvatar);

                        if (AuthService.IsLoggedIn)
                        {
                            await AuthService.UpdateAvatarAsync(selectedAvatar);
                        }

                        // Re-render highlight
                        PopulateAvatarPickerGrid();
                    }
                };

                pnlAvatarPickerGrid.Children.Add(avBorder);
            }
        }

        // ── Match History (Profile Popup) ───────────────────────────────────────────
        private async Task LoadProfileMatchHistoryAsync()
        {
            string username = AuthService.IsLoggedIn && AuthService.CurrentUser != null
                ? AuthService.CurrentUser.username
                : "";

            if (string.IsNullOrWhiteSpace(username))
            {
                pnlProfileMatchHistory.Children.Clear();
                pnlProfileMatchHistory.Children.Add(new TextBlock
                {
                    Text = "Đăng nhập để xem lịch sử đấu.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x9A, 0xA8)),
                    FontSize = 12,
                    FontStyle = FontStyles.Italic,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 16, 0, 0)
                });
                return;
            }

            try
            {
                var history = await HistoryService.GetUserHistoryAsync(username, 10);

                // Nếu người dùng đã đóng popup hoặc mở lại (avatar/username đổi) trong lúc chờ tải thì bỏ qua kết quả cũ
                if (pnlProfilePopup.Visibility != Visibility.Visible) return;

                RenderProfileMatchHistory(username, history);
            }
            catch (Exception ex)
            {
                pnlProfileMatchHistory.Children.Clear();
                pnlProfileMatchHistory.Children.Add(new TextBlock
                {
                    Text = $"Lỗi khi tải lịch sử đấu: {ex.Message}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52)),
                    FontSize = 12,
                    FontStyle = FontStyles.Italic,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 16, 0, 0)
                });
            }
        }

        private void RenderProfileMatchHistory(string targetUsername, List<GameRecord> history)
        {
            pnlProfileMatchHistory.Children.Clear();

            if (history == null || history.Count == 0)
            {
                pnlProfileMatchHistory.Children.Add(new TextBlock
                {
                    Text = "Chưa có dữ liệu trận đấu gần đây.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x9A, 0xA8)),
                    FontSize = 12,
                    FontStyle = FontStyles.Italic,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 16, 0, 0)
                });
                return;
            }

            foreach (var match in history)
            {
                bool isTargetWhite = match.playerWhite.Equals(targetUsername, StringComparison.OrdinalIgnoreCase);
                string oppName = isTargetWhite ? match.displayNameBlack : match.displayNameWhite;
                if (string.IsNullOrEmpty(oppName)) oppName = isTargetWhite ? match.playerBlack : match.playerWhite;
                string oppAvatar = isTargetWhite ? match.avatarBlack : match.avatarWhite;

                string outcome;
                if (match.winner == "draw") outcome = "draw";
                else if ((isTargetWhite && match.winner == "white") || (!isTargetWhite && match.winner == "black")) outcome = "win";
                else outcome = "loss";

                var cardBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x14, 0x19, 0x2D)),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 3, 0, 3),
                    Padding = new Thickness(10, 8, 10, 8)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });

                // 1. Result badge
                var badge = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(6, 2, 6, 2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = outcome switch
                    {
                        "win" => new SolidColorBrush(Color.FromArgb(50, 0, 230, 118)),
                        "loss" => new SolidColorBrush(Color.FromArgb(50, 255, 82, 82)),
                        _ => new SolidColorBrush(Color.FromArgb(50, 255, 215, 0))
                    }
                };
                var lblBadge = new TextBlock
                {
                    Text = outcome switch { "win" => "THẮNG", "loss" => "THUA", _ => "HOÀ" },
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Foreground = outcome switch
                    {
                        "win" => new SolidColorBrush(Color.FromRgb(0, 230, 118)),
                        "loss" => new SolidColorBrush(Color.FromRgb(255, 82, 82)),
                        _ => new SolidColorBrush(Color.FromRgb(255, 215, 0))
                    }
                };
                badge.Child = lblBadge;
                Grid.SetColumn(badge, 0);
                grid.Children.Add(badge);

                // 2. Opponent avatar
                var oppImg = new Image
                {
                    Width = 26,
                    Height = 26,
                    Source = ImageResources.GetAvatarImage(oppAvatar),
                    Stretch = Stretch.UniformToFill,
                    VerticalAlignment = VerticalAlignment.Center,
                    Clip = new EllipseGeometry(new Point(13, 13), 13, 13)
                };
                Grid.SetColumn(oppImg, 1);
                grid.Children.Add(oppImg);

                // 3. Opponent name & date & mode
                var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                infoStack.Children.Add(new TextBlock
                {
                    Text = $"vs {oppName}",
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                string dateStr = match.timestamp > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(match.timestamp).ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                    : "--";
                infoStack.Children.Add(new TextBlock
                {
                    Text = $"{match.gameMode}  •  {dateStr}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x9A, 0xA8)),
                    FontSize = 10
                });
                Grid.SetColumn(infoStack, 2);
                grid.Children.Add(infoStack);

                // 4. Replay button — mở lại trận đấu giống chế độ phát của Custom
                var btnReplay = new Button
                {
                    Content = "👁️ Xem lại",
                    Height = 28,
                    Style = (Style)FindResource("ModernButtonStyle"),
                    Background = new SolidColorBrush(Color.FromRgb(0, 229, 255)),
                    Foreground = new SolidColorBrush(Color.FromRgb(18, 18, 18)),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center
                };
                btnReplay.Click += (s, e) =>
                {
                    CloseAllPopups();
                    var replayWindow = GameWindow.CreateReplayWindow(match);
                    replayWindow.Owner = this;
                    replayWindow.Show();
                };
                Grid.SetColumn(btnReplay, 3);
                grid.Children.Add(btnReplay);

                cardBorder.Child = grid;
                pnlProfileMatchHistory.Children.Add(cardBorder);
            }
        }

        private void BtnCloseProfile_Click(object sender, RoutedEventArgs e) => CloseAllPopups();

        // ── Join Room Popup ───────────────────────────────────────────────────────
        private void BtnJoinRoom_Click(object sender, RoutedEventArgs e)
        {
            bool show = pnlJoinPopup.Visibility != Visibility.Visible;
            CloseAllPopups();
            if (show)
            {
                pnlJoinPopup.Visibility = Visibility.Visible;
                popupOverlay.Visibility = Visibility.Visible;
                txtRoomId.Text = IdPrefix;
                txtRoomId.SelectionStart = IdPrefix.Length;
                txtRoomId.Focus();
            }
        }

        private void BtnCloseJoin_Click(object sender, RoutedEventArgs e) => CloseAllPopups();

        private void PopupOverlay_Click(object sender, MouseButtonEventArgs e) => CloseAllPopups();

        private void CloseAllPopups()
        {
            pnlProfilePopup.Visibility = Visibility.Collapsed;
            pnlSettings.Visibility     = Visibility.Collapsed;
            pnlJoinPopup.Visibility    = Visibility.Collapsed;
            pnlFriendsDropdown.Visibility = Visibility.Collapsed;
            popupOverlay.Visibility    = Visibility.Collapsed;
        }

        private async void ProfileArea_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            string username = GetPlayerName();
            if (!string.IsNullOrWhiteSpace(username) && sender is FrameworkElement fe)
            {
                await Services.PlayerContextMenu.ShowAsync(fe, username, this, async () => await LoadFriendsListAsync());
            }
        }

        // ── Friends Dropdown & Invites ───────────────────────────────────────────
        private async void BtnFriendsDropdown_Click(object sender, RoutedEventArgs e)
        {
            bool show = pnlFriendsDropdown.Visibility != Visibility.Visible;
            CloseAllPopups();
            if (show)
            {
                pnlFriendsDropdown.Visibility = Visibility.Visible;
                popupOverlay.Visibility = Visibility.Visible;
                await LoadFriendsListAsync();
                await LoadFriendRequestsAsync();
            }
        }

        private void BtnCloseFriends_Click(object sender, RoutedEventArgs e) => CloseAllPopups();

        private async void BtnAddFriend_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthService.IsLoggedIn || AuthService.CurrentUser == null)
            {
                MessageBox.Show("Vui lòng đăng nhập để sử dụng tính năng kết bạn.", "Yêu cầu đăng nhập", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Tìm theo TÊN NGƯỜI CHƠI (displayName), không phải tên tài khoản
            string query = txtAddFriendUsername.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(query))
            {
                MessageBox.Show("Vui lòng nhập tên người chơi cần kết bạn.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnAddFriend.IsEnabled = false;
            try
            {
                var matches = await FriendService.SearchUsersByDisplayNameAsync(query, AuthService.CurrentUser.username);

                if (matches.Count == 0)
                {
                    MessageBox.Show($"Không tìm thấy người chơi nào có tên '{query}'.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Đúng 1 người trùng tên hoàn toàn -> gửi lời mời luôn
                bool exactSingle = matches.Count == 1 &&
                    string.Equals(matches[0].displayName?.Trim(), query, StringComparison.OrdinalIgnoreCase);
                if (exactSingle)
                {
                    await SendFriendRequestToAsync(matches[0].username);
                    return;
                }

                // Nhiều người trùng tên, hoặc chỉ khớp một phần -> cho chọn đúng người (kèm @tài khoản để phân biệt)
                Services.PlayerContextMenu.ShowPlayerPicker(txtAddFriendUsername, matches,
                    async picked => await SendFriendRequestToAsync(picked.username));
            }
            finally
            {
                btnAddFriend.IsEnabled = true;
            }
        }

        private async Task SendFriendRequestToAsync(string targetUsername)
        {
            if (AuthService.CurrentUser == null) return;

            // Chỉ gửi YÊU CẦU kết bạn — chỉ khi đối phương chấp nhận thì 2 bên mới chính thức là bạn bè
            var (ok, msg) = await FriendService.SendFriendRequestAsync(AuthService.CurrentUser.username, targetUsername);
            if (!ok)
            {
                MessageBox.Show(msg, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            txtAddFriendUsername.Clear();
            await LoadFriendsListAsync();
            await LoadFriendRequestsAsync();
        }

        // ── Pending Friend Requests (lời mời kết bạn đang chờ) ──────────────────────
        private async Task LoadFriendRequestsAsync()
        {
            if (!AuthService.IsLoggedIn || AuthService.CurrentUser == null)
            {
                pnlFriendRequestsContainer.Children.Clear();
                pnlFriendRequestsSection.Visibility = Visibility.Collapsed;
                UpdateFriendRequestBadge(0);
                return;
            }

            string currentUsername = AuthService.CurrentUser.username;
            var requests = await FriendService.GetPendingFriendRequestsAsync(currentUsername);

            pnlFriendRequestsContainer.Children.Clear();

            if (requests == null || requests.Count == 0)
            {
                pnlFriendRequestsSection.Visibility = Visibility.Collapsed;
                UpdateFriendRequestBadge(0);
                return;
            }

            pnlFriendRequestsSection.Visibility = Visibility.Visible;
            lblFriendRequestsCount.Text = $"({requests.Count})";
            UpdateFriendRequestBadge(requests.Count);

            foreach (var req in requests)
            {
                pnlFriendRequestsContainer.Children.Add(CreateFriendRequestRow(req));
            }
        }

        private void UpdateFriendRequestBadge(int count)
        {
            if (count > 0)
            {
                lblFriendRequestBadge.Text = count > 9 ? "9+" : count.ToString();
                dotFriendRequestBadge.Visibility = Visibility.Visible;
            }
            else
            {
                dotFriendRequestBadge.Visibility = Visibility.Collapsed;
            }
        }

        private Border CreateFriendRequestRow(FriendRequestModel req)
        {
            var rowBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(36, 32, 20)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                BorderThickness = new Thickness(1)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Avatar
            var avImg = new Image
            {
                Source = ImageResources.GetAvatarImage(req.fromAvatar),
                Width = 32,
                Height = 32,
                Stretch = Stretch.UniformToFill,
                VerticalAlignment = VerticalAlignment.Center
            };
            avImg.Clip = new EllipseGeometry(new Point(16, 16), 16, 16);
            Grid.SetColumn(avImg, 0);
            grid.Children.Add(avImg);

            // Name
            var nameBlock = new TextBlock
            {
                Text = req.fromDisplayName,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 6, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 130
            };
            Grid.SetColumn(nameBlock, 1);
            grid.Children.Add(nameBlock);

            // Accept / Decline buttons
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var btnAccept = new Button
            {
                Content = "✓",
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 4, 0),
                Style = (Style)FindResource("ModernButtonStyle"),
                Background = new SolidColorBrush(Color.FromRgb(0, 230, 118)),
                Foreground = new SolidColorBrush(Color.FromRgb(18, 18, 18)),
                FontWeight = FontWeights.Bold,
                ToolTip = "Chấp nhận kết bạn"
            };
            btnAccept.Click += async (s, e) =>
            {
                if (AuthService.CurrentUser == null) return;
                btnAccept.IsEnabled = false;
                bool ok = await FriendService.AcceptFriendRequestAsync(AuthService.CurrentUser.username, req.fromUsername);
                if (ok)
                {
                    await LoadFriendRequestsAsync();
                    await LoadFriendsListAsync();
                }
                else
                {
                    btnAccept.IsEnabled = true;
                }
            };
            btnPanel.Children.Add(btnAccept);

            var btnDecline = new Button
            {
                Content = "✕",
                Width = 28,
                Height = 28,
                Style = (Style)FindResource("ModernButtonStyle"),
                Background = new SolidColorBrush(Color.FromRgb(54, 50, 78)),
                Foreground = new SolidColorBrush(Color.FromRgb(255, 82, 82)),
                FontWeight = FontWeights.Bold,
                ToolTip = "Từ chối"
            };
            btnDecline.Click += async (s, e) =>
            {
                if (AuthService.CurrentUser == null) return;
                btnDecline.IsEnabled = false;
                await FriendService.DeclineFriendRequestAsync(AuthService.CurrentUser.username, req.fromUsername);
                await LoadFriendRequestsAsync();
            };
            btnPanel.Children.Add(btnDecline);

            Grid.SetColumn(btnPanel, 2);
            grid.Children.Add(btnPanel);

            rowBorder.Child = grid;
            return rowBorder;
        }

        private void TxtAddFriendUsername_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnAddFriend_Click(sender, e);
            }
        }

        private async Task LoadFriendsListAsync()
        {
            pnlFriendsContainer.Children.Clear();

            if (!AuthService.IsLoggedIn || AuthService.CurrentUser == null)
            {
                lblFriendsEmpty.Text = "Vui lòng đăng nhập để xem danh sách bạn bè.";
                lblFriendsEmpty.Visibility = Visibility.Visible;
                pnlFriendsContainer.Children.Add(lblFriendsEmpty);
                lblFriendsCount.Text = "";
                return;
            }

            lblFriendsEmpty.Text = "Đang tải danh sách bạn bè...";
            lblFriendsEmpty.Visibility = Visibility.Visible;
            pnlFriendsContainer.Children.Add(lblFriendsEmpty);

            string currentUsername = AuthService.CurrentUser.username;
            var friends = await FriendService.GetFriendsAsync(currentUsername);

            pnlFriendsContainer.Children.Clear();
            int onlineCount = friends.Count(f => f.isOnline);
            lblFriendsCount.Text = $" ({onlineCount}/{friends.Count} online)";

            if (friends.Count == 0)
            {
                lblFriendsEmpty.Text = "Bạn chưa có bạn bè nào.\nNhập tên tài khoản ở trên để kết bạn!";
                lblFriendsEmpty.Visibility = Visibility.Visible;
                pnlFriendsContainer.Children.Add(lblFriendsEmpty);
                return;
            }

            foreach (var friend in friends)
            {
                pnlFriendsContainer.Children.Add(CreateFriendRow(friend));
            }
        }

        private Border CreateFriendRow(FriendModel friend)
        {
            var rowBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(28, 25, 48)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                ToolTip = "Chuột phải để xem hồ sơ & lịch sử đấu"
            };

            string friendUser = friend.username;
            rowBorder.MouseRightButtonUp += async (s, e) =>
            {
                await Services.PlayerContextMenu.ShowAsync(rowBorder, friendUser, this, async () => await LoadFriendsListAsync());
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Avatar + Online Indicator
            var avGrid = new Grid { Width = 36, Height = 36, HorizontalAlignment = HorizontalAlignment.Left };
            var avImg = new Image
            {
                Source = ImageResources.GetAvatarImage(friend.avatar),
                Width = 34,
                Height = 34,
                Stretch = Stretch.UniformToFill
            };
            avImg.Clip = new EllipseGeometry(new Point(17, 17), 17, 17);
            avGrid.Children.Add(avImg);

            var dotColor = friend.isOnline ? Color.FromRgb(0, 230, 118) : Color.FromRgb(120, 120, 120);
            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(dotColor),
                Stroke = new SolidColorBrush(Color.FromRgb(24, 22, 40)),
                StrokeThickness = 2,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            avGrid.Children.Add(dot);
            Grid.SetColumn(avGrid, 0);
            grid.Children.Add(avGrid);

            // Text Info
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };

            var nameLine = new StackPanel { Orientation = Orientation.Horizontal };
            nameLine.Children.Add(new TextBlock
            {
                Text = friend.displayName,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 160
            });
            nameLine.Children.Add(new TextBlock
            {
                Text = $" @{friend.username}",
                Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 165)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            });
            stack.Children.Add(nameLine);

            var statusLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            statusLine.Children.Add(new TextBlock
            {
                Text = $"👑 {friend.elo} ELO  •  ",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            });

            var statusColor = friend.isOnline ? (friend.activity == "Sảnh chờ" ? Color.FromRgb(0, 229, 255) : Color.FromRgb(0, 230, 118)) : Color.FromRgb(140, 140, 155);
            statusLine.Children.Add(new TextBlock
            {
                Text = friend.StatusDisplayText,
                Foreground = new SolidColorBrush(statusColor),
                FontSize = 11,
                FontWeight = FontWeights.Medium
            });
            stack.Children.Add(statusLine);

            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);

            rowBorder.Child = grid;
            return rowBorder;
        }

        private void StartHeartbeatTimer()
        {
            _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _heartbeatTimer.Tick += async (s, e) =>
            {
                if (AuthService.IsLoggedIn && AuthService.CurrentUser != null)
                {
                    string username = AuthService.CurrentUser.username;
                    _ = FriendService.UpdateStatusAsync(username, "online", "Sảnh chờ");

                    // Cập nhật số huy hiệu lời mời kết bạn đang chờ (kể cả khi dropdown đang đóng)
                    var pendingFriendRequests = await FriendService.GetPendingFriendRequestsAsync(username);
                    UpdateFriendRequestBadge(pendingFriendRequests?.Count ?? 0);
                    if (pnlFriendsDropdown.Visibility == Visibility.Visible)
                    {
                        await LoadFriendRequestsAsync();
                    }

                    var invites = await FriendService.GetPendingInvitesAsync(username);
                    if (invites != null && invites.Count > 0)
                    {
                        var latest = invites.Last();
                        if (_currentInvite == null || _currentInvite.roomId != latest.roomId)
                        {
                            _currentInvite = latest;
                            imgInviteFromAvatar.Source = ImageResources.GetAvatarImage(latest.fromAvatar);
                            lblInviteFromUser.Text = $"{latest.fromDisplayName} đã mời bạn vào phòng!";
                            lblInviteRoomId.Text = $"Mã phòng: {latest.roomId}";
                            pnlInvitePopup.Visibility = Visibility.Visible;
                        }
                    }
                }
            };
            _heartbeatTimer.Start();
        }

        private async void BtnAcceptInvite_Click(object sender, RoutedEventArgs e)
        {
            if (_currentInvite != null)
            {
                string roomId = _currentInvite.roomId;
                pnlInvitePopup.Visibility = Visibility.Collapsed;
                if (AuthService.IsLoggedIn && AuthService.CurrentUser != null)
                {
                    _ = FriendService.DismissInviteAsync(AuthService.CurrentUser.username, roomId);
                }
                await JoinRoomByIdAsync(roomId);
            }
        }

        private void BtnDeclineInvite_Click(object sender, RoutedEventArgs e)
        {
            if (_currentInvite != null)
            {
                if (AuthService.IsLoggedIn && AuthService.CurrentUser != null)
                {
                    _ = FriendService.DismissInviteAsync(AuthService.CurrentUser.username, _currentInvite.roomId);
                }
                _currentInvite = null;
            }
            pnlInvitePopup.Visibility = Visibility.Collapsed;
        }

        // ── Right Panel: Leaderboards ─────────────────────────────────────────────
        private async Task LoadEloTop3Async()
        {
            try
            {
                var top3 = await AuthService.GetLeaderboardAsync(3);
                Dispatcher.Invoke(() =>
                {
                    // Dừng animation cũ nếu có
                    _rank1AnimTimer?.Stop();
                    _rank1AnimTimer = null;

                    pnlTop3Elo.Children.Clear();
                    if (top3 == null || top3.Count == 0)
                    {
                        pnlTop3Elo.Children.Add(MakeEmptyLabel("Chưa có người chơi"));
                        return;
                    }

                    string[] rankImages = { "rank1.png", "rank2.png", "rank3.png" };
                    for (int i = 0; i < top3.Count && i < 3; i++)
                    {
                        var u = top3[i];
                        string rankImg = i < rankImages.Length ? rankImages[i] : "rank3.png";
                        var row = MakeEloLeaderboardRow(rankImg, u.displayName ?? u.username, u.username, u.elo, u.avatar, isRank1: i == 0);
                        pnlTop3Elo.Children.Add(row);

                        // Thêm đường thẳng ngăn cách giữa hai người chơi
                        if (i < Math.Min(top3.Count, 3) - 1)
                        {
                            pnlTop3Elo.Children.Add(MakeSeparator());
                        }
                    }
                });
            }
            catch { }
        }

        private static FrameworkElement MakeSeparator()
        {
            var grad = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0)
            };
            grad.GradientStops.Add(new GradientStop(Color.FromArgb(0, 45, 65, 105), 0.0));
            grad.GradientStops.Add(new GradientStop(Color.FromArgb(140, 90, 130, 200), 0.5));
            grad.GradientStops.Add(new GradientStop(Color.FromArgb(0, 45, 65, 105), 1.0));

            return new Border
            {
                Height = 1,
                Background = grad,
                Margin = new Thickness(6, 4, 6, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
        }

        private FrameworkElement MakeEloLeaderboardRow(string rankImageName, string name, string username, int elo, string? avatar, bool isRank1 = false)
        {
            var grid = new Grid { Margin = new Thickness(isRank1 ? 4 : 2, 2, isRank1 ? 4 : 2, 2) };
            grid.Cursor = Cursors.Hand;
            grid.ToolTip = "Chuột phải để xem hồ sơ & lịch sử đấu";
            string targetUser = !string.IsNullOrWhiteSpace(username) ? username : name;
            grid.MouseRightButtonUp += async (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(targetUser))
                {
                    await Services.PlayerContextMenu.ShowAsync(grid, targetUser, this, async () => await LoadFriendsListAsync());
                }
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); // Medal
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); // Avatar
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Name & ELO

            // 1. Rank Medallion
            var imgMedal = new Image
            {
                Source = ImageResources.GetImageByFileName(rankImageName),
                Width = 24,
                Height = 24,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(imgMedal, 0);
            grid.Children.Add(imgMedal);

            // 2. Round Avatar
            var avatarGrid = new Grid
            {
                Width = 32,
                Height = 32,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var avCircle = new Ellipse
            {
                Width = 32,
                Height = 32,
                Fill = new SolidColorBrush(Color.FromRgb(35, 45, 75)),
                Stroke = new SolidColorBrush(isRank1 ? Color.FromRgb(255, 215, 0) : Color.FromRgb(55, 65, 95)),
                StrokeThickness = 1.5
            };
            var avImg = new Image
            {
                Source = ImageResources.GetAvatarImage(avatar),
                Width = 30,
                Height = 30,
                Stretch = Stretch.UniformToFill
            };
            avImg.Clip = new EllipseGeometry(new Point(15, 15), 14, 14);
            avatarGrid.Children.Add(avCircle);
            avatarGrid.Children.Add(avImg);
            Grid.SetColumn(avatarGrid, 1);
            grid.Children.Add(avatarGrid);

            // 3. Name + ELO
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            stack.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var eloStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
            eloStack.Children.Add(new TextBlock { Text = "👑 ", Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)), FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            eloStack.Children.Add(new TextBlock { Text = $"ELO {elo}", Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)), FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(eloStack);

            Grid.SetColumn(stack, 2);
            grid.Children.Add(stack);

            // ── Không phải hạng 1 → trả về row thông thường ─────────────────
            if (!isRank1) return grid;

            // ── Hạng 1: thêm viền gradient xoay lấp lánh ────────────────────
            var gradBrush = new LinearGradientBrush();
            gradBrush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 215,   0), 0.00)); // Gold
            gradBrush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 255, 200), 0.20)); // Light yellow
            gradBrush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 255, 255), 0.40)); // White sparkle
            gradBrush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 180,   0), 0.60)); // Amber
            gradBrush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 255, 255), 0.80)); // White sparkle
            gradBrush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 215,   0), 1.00)); // Gold

            _rank1RotTransform = new RotateTransform(0, 0.5, 0.5);
            gradBrush.RelativeTransform = _rank1RotTransform;

            var outerBorder = new Border
            {
                CornerRadius  = new CornerRadius(10),
                Background    = gradBrush,
                Padding       = new Thickness(1.8),
                Margin        = new Thickness(0, 1, 0, 2),
                Effect        = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color       = Color.FromRgb(255, 215, 0),
                    BlurRadius  = 8,
                    ShadowDepth = 0,
                    Opacity     = 0.55
                }
            };
            var innerBorder = new Border
            {
                Background   = new SolidColorBrush(Color.FromRgb(18, 25, 45)),
                CornerRadius = new CornerRadius(8),
                Padding      = new Thickness(6, 3, 6, 3),
                Child        = grid
            };
            outerBorder.Child = innerBorder;

            // Bắt đầu timer quay ~60fps
            double angle = 0;
            _rank1AnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _rank1AnimTimer.Tick += (s, e) =>
            {
                angle = (angle + 2.5) % 360;
                _rank1RotTransform.Angle = angle;
            };
            _rank1AnimTimer.Start();

            return outerBorder;
        }

        // ── Bot Records: Top 1 mỗi cấp độ, mỗi khung riêng ──────────────────────
        private static readonly string[] Difficulties  = { "easy",  "medium", "hard",   "expert"   };
        private static readonly string[] DiffNames     = { "Dễ",    "TB",     "Khó",    "Siêu khó" };
        private static readonly Color[]  DiffColors    =
        {
            Color.FromRgb(  0, 200, 100),  // Dễ     – xanh lá
            Color.FromRgb(255, 215,   0),  // TB      – vàng
            Color.FromRgb(255, 130,   0),  // Khó     – cam
            Color.FromRgb(220,  50,  50),  // Siêu khó – đỏ
        };

        private async Task LoadBotRecordsAsync()
        {
            try
            {
                for (int d = 0; d < Difficulties.Length; d++)
                {
                    string diff      = Difficulties[d];
                    string diffName  = DiffNames[d];
                    Color  diffColor = DiffColors[d];

                    var records = await AuthService.GetBotLeaderboardAsync(diff, 1);

                    int localD = d;
                    Dispatcher.Invoke(() =>
                    {
                        // Xoá placeholder lần đầu
                        if (localD == 0) pnlBotRecords.Children.Clear();

                        // ── Mini-card mỗi cấp độ ─────────────────────────────
                        var card = new Border
                        {
                            CornerRadius    = new CornerRadius(8),
                            Background      = new SolidColorBrush(Color.FromArgb(70, 20, 30, 60)),
                            BorderBrush     = new SolidColorBrush(Color.FromArgb(90,
                                                diffColor.R, diffColor.G, diffColor.B)),
                            BorderThickness = new Thickness(1),
                            Margin          = new Thickness(0, 0, 0, 5),
                            Padding         = new Thickness(8, 5, 8, 5)
                        };

                        var row = new Grid();
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });    // badge
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); // avatar
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });    // time

                        // 1. Badge cấp độ
                        var badge = new Border
                        {
                            Background      = new SolidColorBrush(Color.FromArgb(210,
                                                diffColor.R, diffColor.G, diffColor.B)),
                            CornerRadius    = new CornerRadius(5),
                            Padding         = new Thickness(6, 2, 6, 2),
                            Margin          = new Thickness(0, 0, 6, 0),
                            MinWidth        = 38,
                            VerticalAlignment   = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            Child = new TextBlock
                            {
                                Text       = diffName,
                                Foreground = Brushes.Black,
                                FontSize   = 10,
                                FontWeight = FontWeights.Bold,
                                HorizontalAlignment = HorizontalAlignment.Center
                            }
                        };
                        Grid.SetColumn(badge, 0);
                        row.Children.Add(badge);

                        if (records != null && records.Count > 0)
                        {
                            var r = records[0];
                            card.Cursor = Cursors.Hand;
                            card.ToolTip = "Chuột phải để xem hồ sơ & lịch sử đấu";
                            string targetBotUser = r.username;
                            card.MouseRightButtonUp += async (s, e) =>
                            {
                                if (!string.IsNullOrWhiteSpace(targetBotUser))
                                {
                                    await Services.PlayerContextMenu.ShowAsync(card, targetBotUser, this, async () => await LoadFriendsListAsync());
                                }
                            };

                            // 2. Avatar
                            var avImg = new Image
                            {
                                Source              = ImageResources.GetAvatarImage(r.avatar),
                                Width               = 24,
                                Height              = 24,
                                Stretch             = Stretch.UniformToFill,
                                VerticalAlignment   = VerticalAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Left
                            };
                            avImg.Clip = new EllipseGeometry(new Point(12, 12), 11, 11);
                            Grid.SetColumn(avImg, 1);
                            row.Children.Add(avImg);

                            // 3. Tên
                            var lblName = new TextBlock
                            {
                                Text              = !string.IsNullOrWhiteSpace(r.displayName) ? r.displayName : r.username,
                                Foreground        = Brushes.White,
                                FontSize          = 12,
                                FontWeight        = FontWeights.SemiBold,
                                VerticalAlignment = VerticalAlignment.Center,
                                TextTrimming      = TextTrimming.CharacterEllipsis,
                                Margin            = new Thickness(5, 0, 4, 0)
                            };
                            Grid.SetColumn(lblName, 2);
                            row.Children.Add(lblName);

                            // 4. Thời gian
                            var timePanel = new StackPanel
                            {
                                Orientation       = Orientation.Horizontal,
                                VerticalAlignment = VerticalAlignment.Center
                            };
                            timePanel.Children.Add(new TextBlock
                            {
                                Text              = "⏱ ",
                                Foreground        = new SolidColorBrush(Color.FromRgb(0, 229, 255)),
                                FontSize          = 10,
                                VerticalAlignment = VerticalAlignment.Center
                            });
                            timePanel.Children.Add(new TextBlock
                            {
                                Text              = FormatTime(r.timeSeconds),
                                Foreground        = new SolidColorBrush(Color.FromRgb(0, 229, 255)),
                                FontSize          = 11,
                                FontWeight        = FontWeights.Bold,
                                VerticalAlignment = VerticalAlignment.Center
                            });
                            Grid.SetColumn(timePanel, 3);
                            row.Children.Add(timePanel);
                        }
                        else
                        {
                            // Chưa có kỷ lục
                            var lblEmpty = new TextBlock
                            {
                                Text              = "--:--",
                                Foreground        = new SolidColorBrush(Color.FromRgb(100, 110, 140)),
                                FontSize          = 11,
                                VerticalAlignment = VerticalAlignment.Center,
                                Margin            = new Thickness(5, 0, 0, 0)
                            };
                            Grid.SetColumn(lblEmpty, 1);
                            lblEmpty.SetValue(Grid.ColumnSpanProperty, 3);
                            row.Children.Add(lblEmpty);
                        }

                        card.Child = row;
                        pnlBotRecords.Children.Add(card);
                    });
                }
            }
            catch { }
        }

        private static TextBlock MakeEmptyLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 150, 175)),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            };
        }

        private static string FormatTime(int seconds)
        {
            if (seconds <= 0) return "--:--";
            int m = seconds / 60, s = seconds % 60;
            return m > 0 ? $"{m}:{s:D2}" : $"{s}s";
        }

        // ── Game Navigation Logic ──────────────────────────────────────────────────
        private void BtnLeaderboard_Click(object sender, RoutedEventArgs e)
        {
            var lbWindow = new LeaderboardWindow { Owner = this };
            lbWindow.ShowDialog();
        }

        private async void BtnMatchmaking_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthService.IsLoggedIn || AuthService.CurrentUser == null)
            {
                var ask = MessageBox.Show(
                    "Tính năng Ghép Trận yêu cầu đăng nhập tài khoản để tích luỹ ELO.\nBạn có muốn đăng nhập ngay bây giờ không?",
                    "Yêu cầu đăng nhập", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ask == MessageBoxResult.Yes)
                {
                    var lw = new LoginWindow { Owner = this };
                    if (lw.ShowDialog() == true)
                    {
                        UpdateLoginUI();
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    return;
                }
            }

            if (AuthService.CurrentUser == null) return;

            string username = AuthService.CurrentUser.username;
            string displayName = GetDisplayNameForUI();
            int elo = AuthService.CurrentUser.elo;

            var mmDialog = new MatchmakingDialog(username, displayName, selectedAvatar, elo) { Owner = this };
            if (mmDialog.ShowDialog() == true && mmDialog.IsMatched)
            {
                var gameWindow = new GameWindow(mmDialog.MatchedRoomId, displayName,
                    isCreator: mmDialog.IsCreator, myAvatar: selectedAvatar, isMatchmaking: true);
                gameWindow.Show();
                this.Close();
            }
        }

        private void BtnPlayBot_Click(object sender, RoutedEventArgs e)
        {
            var botDialog = new BotSelectWindow { Owner = this };
            if (botDialog.ShowDialog() == true)
            {
                string name = GetPlayerName();
                var gameWindow = new GameWindow(botDialog.SelectedDifficulty, name,
                    selectedColor: botDialog.SelectedColor, myAvatar: selectedAvatar,
                    showMoveHints: botDialog.EnableMoveHints);
                gameWindow.Show();
                this.Close();
            }
        }

        private async void BtnCreateRoom_Click(object sender, RoutedEventArgs e)
        {
            var createDialog = new CreateRoomDialog { Owner = this };
            if (createDialog.ShowDialog() != true) return;

            string name = GetPlayerName();
            string displayName = GetDisplayNameForUI();

            if (createDialog.IsCustomRoom)
            {
                var gameWindow = new GameWindow(isCustom: true, name, selectedAvatar, createDialog.EnableMoveHints);
                gameWindow.Show();
                this.Close();
                return;
            }

            int timeMinutes = createDialog.SelectedTimeMinutes;
            btnCreateRoom.IsEnabled = false;
            try
            {
                string newRoomId = GenerateRoomId();
                RoomData room = CreateInitialRoom(name, displayName, timeMinutes, createDialog.EnableMoveHints);

                bool ok = await FirebaseClient.PutAsync($"rooms/{newRoomId}", room);
                if (!ok)
                {
                    MessageBox.Show("Không thể tạo phòng, vui lòng kiểm tra kết nối mạng và thử lại.",
                        "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var gameWindow = new GameWindow(newRoomId, name, isCreator: true,
                    myAvatar: selectedAvatar, isMatchmaking: false,
                    showMoveHints: createDialog.EnableMoveHints);
                gameWindow.Show();
                this.Close();
            }
            finally
            {
                btnCreateRoom.IsEnabled = true;
            }
        }

        private async void BtnConfirmJoin_Click(object sender, RoutedEventArgs e)
        {
            string roomId = GetRoomIdInput();
            if (string.IsNullOrEmpty(roomId))
            {
                MessageBox.Show("Vui lòng nhập ID phòng.", "Thiếu ID", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            btnConfirmJoin.IsEnabled = false;
            try
            {
                await JoinRoomByIdAsync(roomId);
            }
            finally
            {
                btnConfirmJoin.IsEnabled = true;
            }
        }

        public async Task JoinRoomByIdAsync(string roomId)
        {
            if (string.IsNullOrWhiteSpace(roomId)) return;
            string name = GetPlayerName();
            string displayName = GetDisplayNameForUI();

            try
            {
                RoomData? room = await FirebaseClient.GetAsync<RoomData>($"rooms/{roomId}");
                if (room == null)
                {
                    MessageBox.Show("Không tìm thấy phòng với ID này.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (string.Equals(room.player1, name, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Bạn không thể tự vào phòng do chính mình tạo.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!string.IsNullOrEmpty(room.player2))
                {
                    MessageBox.Show("Phòng này đã đủ người chơi.", "Phòng đầy", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (room.lastHeartbeat1 > 0 && (now - room.lastHeartbeat1) > 15000)
                {
                    _ = FirebaseClient.DeleteAsync($"rooms/{roomId}");
                    MessageBox.Show("Phòng đã bị huỷ do chủ phòng không còn trực tuyến.", "Phòng không tồn tại",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var patch = new Dictionary<string, object>
                {
                    ["player2"]      = name,
                    ["displayName2"] = displayName,
                    ["avatar2"]      = selectedAvatar,
                    ["status"]       = "playing",
                    ["updatedAt"]    = now
                };
                bool ok = await FirebaseClient.PatchAsync($"rooms/{roomId}", patch);
                if (!ok)
                {
                    MessageBox.Show("Không thể vào phòng, vui lòng thử lại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                CloseAllPopups();
                var gameWindow = new GameWindow(roomId, name, isCreator: false,
                    myAvatar: selectedAvatar, isMatchmaking: false,
                    showMoveHints: room.showMoveHints);
                gameWindow.Show();
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi kết nối phòng: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Helper Methods ────────────────────────────────────────────────────────
        private string GetPlayerName()
        {
            if (AuthService.IsLoggedIn && AuthService.CurrentUser != null)
                return AuthService.CurrentUser.username.Trim();
            return guestName;
        }

        private string GetDisplayNameForUI()
        {
            if (AuthService.IsLoggedIn && AuthService.CurrentUser != null)
            {
                string dn = AuthService.CurrentUser.displayName;
                return !string.IsNullOrWhiteSpace(dn) ? dn.Trim() : AuthService.CurrentUser.username.Trim();
            }
            return guestName;
        }

        private string GetRoomIdInput()
        {
            string text = txtRoomId.Text ?? "";
            if (text.StartsWith(IdPrefix)) text = text[IdPrefix.Length..];
            return text.Trim();
        }

        private static string GenerateRoomId() => new Random().Next(100000, 999999).ToString();

        private RoomData CreateInitialRoom(string creatorUsername, string creatorDisplayName,
            int timeLimitMinutes = 10, bool showMoveHints = true)
        {
            var game = new ChessGame();
            var board = game.GetBoardState();
            var boardDict = new Dictionary<string, string>();
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    boardDict[$"{r}_{c}"] = board[r, c] ?? "";

            double seconds = timeLimitMinutes * 60.0;
            return new RoomData
            {
                player1 = creatorUsername,
                displayName1 = creatorDisplayName,
                player2 = "",
                displayName2 = "",
                avatar1 = selectedAvatar,
                avatar2 = "",
                turn = "white",
                status = "waiting",
                winner = "",
                check = "",
                board = boardDict,
                selRow = -1,
                selCol = -1,
                legalMoves = new List<string>(),
                timeLimitMinutes = timeLimitMinutes,
                timeLeftWhite = seconds,
                timeLeftBlack = seconds,
                turnStartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                showMoveHints = showMoveHints,
                updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        private void TxtRoomId_KeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key == Key.Back || e.Key == Key.Delete) && txtRoomId.SelectionStart <= IdPrefix.Length)
                e.Handled = true;
        }

        private void TxtRoomId_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (txtRoomId.SelectionStart < IdPrefix.Length)
                txtRoomId.SelectionStart = txtRoomId.Text.Length;
        }

        private void TxtRoomId_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!txtRoomId.Text.StartsWith(IdPrefix))
            {
                txtRoomId.Text = IdPrefix;
                txtRoomId.SelectionStart = txtRoomId.Text.Length;
            }
        }
    }
}