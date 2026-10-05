using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ChessGame_PJ.Core;
using ChessGame_PJ.Services;

namespace ChessGame_PJ.Views
{
    public partial class PlayerProfileDialog : Window
    {
        private readonly string targetUsername;
        private readonly bool blockReplayNavigation;

        public PlayerProfileDialog(string username, bool blockReplayNavigation = false)
        {
            InitializeComponent();
            this.targetUsername = (username ?? "").Trim().ToLowerInvariant();
            this.blockReplayNavigation = blockReplayNavigation;
            Loaded += async (s, e) => await LoadProfileDataAsync();
        }

        private async Task LoadProfileDataAsync()
        {
            if (string.IsNullOrEmpty(targetUsername))
            {
                lblEmptyHistory.Text = "Không có thông tin người chơi.";
                return;
            }

            try
            {
                var user = await FirebaseClient.GetAsync<UserData>($"users/{targetUsername}");
                if (user == null)
                {
                    lblEmptyHistory.Text = "Không tìm thấy người chơi.";
                    return;
                }

                // Header
                txtDisplayName.Text = !string.IsNullOrWhiteSpace(user.displayName) ? user.displayName : user.username;
                txtUsername.Text = $"@{user.username}";
                imgProfileAvatar.Source = ImageResources.GetAvatarImage(user.avatar);

                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                bool isOnline = (user.status == "online") && (now - user.lastSeen < 120_000);
                dotStatus.Fill = isOnline
                    ? new SolidColorBrush(Color.FromRgb(76, 175, 80))
                    : new SolidColorBrush(Color.FromRgb(120, 120, 130));

                string act = !string.IsNullOrWhiteSpace(user.activity) ? user.activity : "Sảnh chờ";
                lblStatusText.Text = isOnline ? $"● Trực tuyến  •  {act}" : "● Ngoại tuyến";
                lblStatusText.Foreground = isOnline
                    ? new SolidColorBrush(Color.FromRgb(0, 230, 118))
                    : new SolidColorBrush(Color.FromRgb(165, 162, 194));

                // Career stats
                txtElo.Text = user.elo.ToString();
                txtWins.Text = user.wins.ToString();
                txtLossDraw.Text = $"{user.losses} / {user.draws}";
                int totalGames = user.wins + user.losses + user.draws;
                double winRate = totalGames > 0 ? ((double)user.wins / totalGames) * 100 : 0;
                txtWinRate.Text = $"{winRate:F0}%";

                // Bot records
                txtBotEasy.Text = user.bestBotTimeEasy > 0 ? $"{user.bestBotTimeEasy}s" : "Chưa có";
                txtBotMedium.Text = user.bestBotTimeMedium > 0 ? $"{user.bestBotTimeMedium}s" : "Chưa có";
                txtBotHard.Text = user.bestBotTimeHard > 0 ? $"{user.bestBotTimeHard}s" : "Chưa có";
                txtBotStockfish.Text = user.bestBotTimeExpert > 0 ? $"{user.bestBotTimeExpert}s" : "Chưa có";

                // Match history
                var history = await HistoryService.GetUserHistoryAsync(targetUsername, 10);
                RenderMatchHistory(history);
            }
            catch (Exception ex)
            {
                lblEmptyHistory.Text = $"Lỗi khi tải thông tin: {ex.Message}";
            }
        }

        private void RenderMatchHistory(List<GameRecord> history)
        {
            pnlMatchHistory.Children.Clear();
            if (history == null || history.Count == 0)
            {
                pnlMatchHistory.Children.Add(new TextBlock
                {
                    Text = "Chưa có dữ liệu trận đấu gần đây.",
                    Foreground = new SolidColorBrush(Color.FromRgb(139, 136, 170)),
                    FontSize = 12,
                    FontStyle = FontStyles.Italic,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0)
                });
                return;
            }

            foreach (var match in history)
            {
                // Determine if target was White or Black
                bool isTargetWhite = match.playerWhite.Equals(targetUsername, StringComparison.OrdinalIgnoreCase);
                string oppName = isTargetWhite ? match.displayNameBlack : match.displayNameWhite;
                if (string.IsNullOrEmpty(oppName)) oppName = isTargetWhite ? match.playerBlack : match.playerWhite;
                string oppAvatar = isTargetWhite ? match.avatarBlack : match.avatarWhite;

                string outcome = "draw";
                if (match.winner == "draw") outcome = "draw";
                else if ((isTargetWhite && match.winner == "white") || (!isTargetWhite && match.winner == "black")) outcome = "win";
                else outcome = "loss";

                var cardBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(24, 22, 42)),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(0, 3, 0, 3),
                    Padding = new Thickness(10, 8, 10, 8)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) }); // Result badge
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) }); // Opponent Avatar
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Opponent Info & Mode
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) }); // Replay button

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

                // 2. Opponent Avatar
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

                // 3. Opponent Name & Date & Mode
                var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                var topRow = new TextBlock
                {
                    Text = $"vs {oppName}",
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                infoStack.Children.Add(topRow);

                string dateStr = match.timestamp > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(match.timestamp).ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                    : "--";
                var bottomRow = new TextBlock
                {
                    Text = $"{match.gameMode}  •  {dateStr}",
                    Foreground = new SolidColorBrush(Color.FromRgb(139, 136, 170)),
                    FontSize = 10
                };
                infoStack.Children.Add(bottomRow);
                Grid.SetColumn(infoStack, 2);
                grid.Children.Add(infoStack);

                // 4. Replay Button
                var btnReplay = new Button
                {
                    Content = "👁️ Xem lại",
                    Height = 28,
                    Style = (Style)FindResource("ModernButtonStyle"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 11,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center
                };

                if (blockReplayNavigation)
                {
                    // Đang thi đấu một trận thật (Đấu máy / Online) -> không cho mở trận khác để Xem lại
                    btnReplay.Background = new SolidColorBrush(Color.FromRgb(55, 52, 86));
                    btnReplay.Foreground = new SolidColorBrush(Color.FromRgb(139, 136, 170));
                    btnReplay.ToolTip = "Không thể xem lại trận đấu khác khi đang trong trận. Hãy kết thúc/rời trận hiện tại trước.";
                    btnReplay.Click += (s, e) =>
                    {
                        MessageBox.Show(
                            "Bạn cần kết thúc hoặc rời trận đấu hiện tại trước khi xem lại một trận khác.",
                            "Không thể xem lại", MessageBoxButton.OK, MessageBoxImage.Information);
                    };
                }
                else
                {
                    btnReplay.Background = new SolidColorBrush(Color.FromRgb(0, 229, 255));
                    btnReplay.Foreground = new SolidColorBrush(Color.FromRgb(18, 18, 18));
                    btnReplay.Click += (s, e) =>
                    {
                        // Open match in Replay mode
                        var replayWindow = GameWindow.CreateReplayWindow(match);
                        replayWindow.Show();
                        Close();
                    };
                }

                Grid.SetColumn(btnReplay, 3);
                grid.Children.Add(btnReplay);

                cardBorder.Child = grid;
                pnlMatchHistory.Children.Add(cardBorder);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
