using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ChessGame_PJ.Core;
using ChessGame_PJ.Services;

namespace ChessGame_PJ.Views
{
    public partial class LeaderboardWindow : Window
    {
        private string currentTab = "elo";

        public LeaderboardWindow()
        {
            InitializeComponent();
            Loaded += async (s, e) => await SwitchTabAsync("elo");
        }

        private async Task SwitchTabAsync(string tab)
        {
            currentTab = tab;
            UpdateTabStyles();
            await LoadLeaderboardAsync();
        }

        private void UpdateTabStyles()
        {
            var inactiveBg = new SolidColorBrush(Color.FromRgb(55, 52, 86));
            var inactiveFg = Brushes.White;

            btnTabElo.Background = inactiveBg; btnTabElo.Foreground = inactiveFg;
            btnTabEasy.Background = inactiveBg; btnTabEasy.Foreground = inactiveFg;
            btnTabMedium.Background = inactiveBg; btnTabMedium.Foreground = inactiveFg;
            btnTabHard.Background = inactiveBg; btnTabHard.Foreground = inactiveFg;
            btnTabStockfish.Background = inactiveBg; btnTabStockfish.Foreground = inactiveFg;

            switch (currentTab)
            {
                case "elo":
                    btnTabElo.Background = new SolidColorBrush(Color.FromRgb(0, 229, 255));
                    btnTabElo.Foreground = new SolidColorBrush(Color.FromRgb(18, 18, 18));
                    lblCategoryDesc.Text = "Top cao thủ xếp hạng ELO qua các trận ghép trận online";
                    lblHeaderMetric.Text = "Điểm ELO  •  Tỉ lệ thắng (Winrate)";
                    break;
                case "easy":
                    btnTabEasy.Background = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                    btnTabEasy.Foreground = new SolidColorBrush(Color.FromRgb(18, 18, 18));
                    lblCategoryDesc.Text = "Top người chơi chiến thắng Máy (Dễ) trong thời gian nhanh nhất";
                    lblHeaderMetric.Text = "Thời gian phá đảo  •  Ngày đạt";
                    break;
                case "medium":
                    btnTabMedium.Background = new SolidColorBrush(Color.FromRgb(255, 215, 0));
                    btnTabMedium.Foreground = new SolidColorBrush(Color.FromRgb(18, 18, 18));
                    lblCategoryDesc.Text = "Top người chơi chiến thắng Máy (Thường) trong thời gian nhanh nhất";
                    lblHeaderMetric.Text = "Thời gian phá đảo  •  Ngày đạt";
                    break;
                case "hard":
                    btnTabHard.Background = new SolidColorBrush(Color.FromRgb(255, 82, 82));
                    btnTabHard.Foreground = new SolidColorBrush(Color.FromRgb(18, 18, 18));
                    lblCategoryDesc.Text = "Top người chơi chiến thắng Máy (Khó - Minimax) trong thời gian nhanh nhất";
                    lblHeaderMetric.Text = "Thời gian phá đảo  •  Ngày đạt";
                    break;
                case "stockfish":
                    btnTabStockfish.Background = new SolidColorBrush(Color.FromRgb(0, 229, 255));
                    btnTabStockfish.Foreground = new SolidColorBrush(Color.FromRgb(18, 18, 18));
                    lblCategoryDesc.Text = "Top người chơi hạ gục Engine Stockfish 19 đẳng cấp thế giới";
                    lblHeaderMetric.Text = "Thời gian phá đảo  •  Ngày đạt";
                    break;
            }
        }

        private async void BtnTabElo_Click(object sender, RoutedEventArgs e) => await SwitchTabAsync("elo");
        private async void BtnTabEasy_Click(object sender, RoutedEventArgs e) => await SwitchTabAsync("easy");
        private async void BtnTabMedium_Click(object sender, RoutedEventArgs e) => await SwitchTabAsync("medium");
        private async void BtnTabHard_Click(object sender, RoutedEventArgs e) => await SwitchTabAsync("hard");
        private async void BtnTabStockfish_Click(object sender, RoutedEventArgs e) => await SwitchTabAsync("stockfish");

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async Task LoadLeaderboardAsync()
        {
            lblLoading.Visibility = Visibility.Visible;
            pnlList.Children.Clear();

            try
            {
                if (currentTab == "elo")
                {
                    await LoadEloLeaderboardAsync();
                }
                else
                {
                    await LoadBotLeaderboardAsync(currentTab);
                }
            }
            finally
            {
                lblLoading.Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadEloLeaderboardAsync()
        {
            var topPlayers = await AuthService.GetLeaderboardAsync(10);
            if (topPlayers.Count == 0)
            {
                ShowEmptyNotice("Chưa có dữ liệu người chơi trên hệ thống.");
                return;
            }

            int rank = 1;
            foreach (var player in topPlayers)
            {
                pnlList.Children.Add(CreateEloRankCard(rank, player));
                rank++;
            }
        }

        private async Task LoadBotLeaderboardAsync(string difficulty)
        {
            var topRecords = await AuthService.GetBotLeaderboardAsync(difficulty, 10);
            if (topRecords.Count == 0)
            {
                ShowEmptyNotice($"Chưa có ai chiến thắng và xác lập kỷ lục ở cấp độ này.");
                return;
            }

            int rank = 1;
            foreach (var record in topRecords)
            {
                pnlList.Children.Add(CreateBotWinRankCard(rank, record));
                rank++;
            }
        }

        private void ShowEmptyNotice(string message)
        {
            var emptyText = new TextBlock
            {
                Text = message,
                Foreground = Brushes.Gray,
                FontSize = 14,
                FontStyle = FontStyles.Italic,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0)
            };
            pnlList.Children.Add(emptyText);
        }

        private Border CreateEloRankCard(int rank, UserData player)
        {
            var border = CreateBaseCard(rank, player);
            var grid = (Grid)border.Child;

            // 4. Elo points
            var lblElo = new TextBlock
            {
                Text = $"{player.elo} ELO",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(lblElo, 3);
            grid.Children.Add(lblElo);

            // 5. Win Rate
            int totalGames = player.wins + player.losses + player.draws;
            double winRate = totalGames > 0 ? ((double)player.wins / totalGames) * 100 : 0;
            var lblStats = new TextBlock
            {
                Text = $"{player.wins}W - {player.losses}L ({winRate:F0}%)",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 229, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(lblStats, 4);
            grid.Children.Add(lblStats);

            return border;
        }

        private Border CreateBotWinRankCard(int rank, BotWinRecord record)
        {
            var border = CreateBaseCard(rank, record.displayName, record.username, record.avatar);
            var grid = (Grid)border.Child;

            int mins = record.timeSeconds / 60;
            int secs = record.timeSeconds % 60;

            // 4. Time Metric
            var lblTime = new TextBlock
            {
                Text = $"⏱️ {mins:D2}:{secs:D2}",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(lblTime, 3);
            grid.Children.Add(lblTime);

            // 5. Date Metric
            string dateStr = record.timestamp > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(record.timestamp).ToLocalTime().ToString("dd/MM/yyyy")
                : "--";

            var lblDate = new TextBlock
            {
                Text = dateStr,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(165, 162, 194)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(lblDate, 4);
            grid.Children.Add(lblDate);

            return border;
        }

        private Border CreateBaseCard(int rank, string? displayName = null, string? username = null, string? avatar = null)
        {
            var cardBg = rank switch
            {
                1 => Color.FromRgb(55, 45, 20),
                2 => Color.FromRgb(45, 45, 55),
                3 => Color.FromRgb(50, 35, 25),
                _ => Color.FromRgb(36, 34, 52)
            };

            var border = new Border
            {
                Height = 56,
                Margin = new Thickness(0, 3, 0, 3),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(cardBg),
                Padding = new Thickness(10, 0, 10, 0),
                Cursor = Cursors.Hand,
                ToolTip = "Chuột phải để xem hồ sơ & lịch sử đấu"
            };

            string targetUser = username ?? "";
            border.MouseRightButtonUp += async (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(targetUser))
                {
                    await PlayerContextMenu.ShowAsync(border, targetUser, this);
                }
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });

            // 1. Rank medal/text
            string rankText = rank switch
            {
                1 => "🥇 1",
                2 => "🥈 2",
                3 => "🥉 3",
                _ => $"#{rank}"
            };

            var rankBrush = rank switch
            {
                1 => Brushes.Gold,
                2 => Brushes.Silver,
                3 => new SolidColorBrush(Color.FromRgb(205, 127, 50)),
                _ => Brushes.WhiteSmoke
            };

            var lblRank = new TextBlock
            {
                Text = rankText,
                FontSize = rank <= 3 ? 15 : 13,
                FontWeight = FontWeights.Bold,
                Foreground = rankBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lblRank, 0);
            grid.Children.Add(lblRank);

            // 2. Avatar
            var imgAvatar = new Image
            {
                Source = ImageResources.GetAvatarImage(avatar ?? "avatar1.png"),
                Width = 38,
                Height = 38,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(imgAvatar, 1);
            grid.Children.Add(imgAvatar);

            // 3. Name & Username
            var namePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            string name = !string.IsNullOrWhiteSpace(displayName) ? displayName : (username ?? "Unknown");
            var lblName = new TextBlock
            {
                Text = name,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var lblUsername = new TextBlock
            {
                Text = $"@{username ?? ""}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(165, 162, 194))
            };
            namePanel.Children.Add(lblName);
            namePanel.Children.Add(lblUsername);
            Grid.SetColumn(namePanel, 2);
            grid.Children.Add(namePanel);

            border.Child = grid;
            return border;
        }

        private Border CreateBaseCard(int rank, UserData player)
        {
            return CreateBaseCard(rank, player.displayName, player.username, player.avatar);
        }
    }
}