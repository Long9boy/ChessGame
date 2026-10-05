using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ChessGame_PJ.Services;

namespace ChessGame_PJ.Views
{
    public partial class InviteFriendDialog : Window
    {
        private readonly string roomId;

        public InviteFriendDialog(string roomId)
        {
            InitializeComponent();
            this.roomId = roomId;
            lblRoomCode.Text = $"Mã phòng: {roomId}";
            Loaded += async (s, e) => await LoadFriendsAsync();
        }

        private async Task LoadFriendsAsync()
        {
            if (!AuthService.IsLoggedIn || AuthService.CurrentUser == null)
            {
                lblStatus.Text = "Bạn chưa đăng nhập để mời bạn bè.";
                return;
            }

            try
            {
                var friends = await FriendService.GetFriendsAsync(AuthService.CurrentUser.username);
                pnlFriendsList.Children.Clear();

                if (friends.Count == 0)
                {
                    pnlFriendsList.Children.Add(new TextBlock
                    {
                        Text = "Bạn chưa có bạn bè nào trong danh sách.\nHãy thêm bạn tại Sảnh chờ!",
                        Foreground = new SolidColorBrush(Color.FromRgb(139, 136, 170)),
                        FontSize = 12,
                        FontStyle = FontStyles.Italic,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 20, 0, 0)
                    });
                    return;
                }

                foreach (var friend in friends)
                {
                    var card = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(28, 25, 46)),
                        CornerRadius = new CornerRadius(8),
                        Margin = new Thickness(0, 2, 0, 2),
                        Padding = new Thickness(10, 6, 10, 6)
                    };

                    var grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) }); // Avatar
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Info
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); // Invite button

                    // Avatar
                    var avGrid = new Grid { Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Center };
                    var avImg = new Image
                    {
                        Width = 30,
                        Height = 30,
                        Source = ImageResources.GetAvatarImage(friend.avatar),
                        Stretch = Stretch.UniformToFill,
                        Clip = new EllipseGeometry(new Point(15, 15), 14, 14)
                    };
                    var avDot = new Ellipse
                    {
                        Width = 9,
                        Height = 9,
                        Fill = friend.isOnline ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) : new SolidColorBrush(Color.FromRgb(120, 120, 130)),
                        Stroke = new SolidColorBrush(Color.FromRgb(28, 25, 46)),
                        StrokeThickness = 1.5,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom
                    };
                    avGrid.Children.Add(avImg);
                    avGrid.Children.Add(avDot);
                    Grid.SetColumn(avGrid, 0);
                    grid.Children.Add(avGrid);

                    // Name + Status
                    var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                    info.Children.Add(new TextBlock
                    {
                        Text = friend.displayName,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold,
                        FontSize = 13,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                    info.Children.Add(new TextBlock
                    {
                        Text = friend.isOnline ? $"👑 {friend.elo}  •  {friend.StatusDisplayText}" : "Ngoại tuyến",
                        Foreground = friend.isOnline ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(139, 136, 170)),
                        FontSize = 10
                    });
                    Grid.SetColumn(info, 1);
                    grid.Children.Add(info);

                    // Invite button
                    var btnInvite = new Button
                    {
                        Content = "Mời",
                        Height = 28,
                        Style = (Style)FindResource("ModernButtonStyle"),
                        Background = friend.isOnline ? new SolidColorBrush(Color.FromRgb(0, 230, 118)) : new SolidColorBrush(Color.FromRgb(55, 52, 86)),
                        Foreground = friend.isOnline ? new SolidColorBrush(Color.FromRgb(18, 18, 18)) : Brushes.Gray,
                        FontWeight = FontWeights.Bold,
                        FontSize = 12,
                        IsEnabled = friend.isOnline,
                        Cursor = friend.isOnline ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow
                    };

                    string fUser = friend.username;
                    string fName = friend.displayName;
                    btnInvite.Click += async (s, e) =>
                    {
                        btnInvite.IsEnabled = false;
                        btnInvite.Content = "Đang gửi...";
                        bool ok = await FriendService.SendRoomInviteAsync(
                            AuthService.CurrentUser.username,
                            AuthService.CurrentUser.displayName,
                            AuthService.CurrentUser.avatar,
                            fUser,
                            roomId
                        );
                        if (ok)
                        {
                            btnInvite.Content = "✓ Đã mời";
                            btnInvite.Background = new SolidColorBrush(Color.FromRgb(0, 229, 255));
                        }
                        else
                        {
                            btnInvite.Content = "Thử lại";
                            btnInvite.IsEnabled = true;
                        }
                    };
                    Grid.SetColumn(btnInvite, 2);
                    grid.Children.Add(btnInvite);

                    card.Child = grid;
                    pnlFriendsList.Children.Add(card);
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Lỗi: {ex.Message}";
            }
        }

        private void BtnCopyCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(roomId);
                btnCopyCode.Content = "✓ Đã Sao Chép!";
            }
            catch { }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
