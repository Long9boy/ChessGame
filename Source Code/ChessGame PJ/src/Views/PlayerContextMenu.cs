using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using ChessGame_PJ.Views;

namespace ChessGame_PJ.Services
{
    public static class PlayerContextMenu
    {
        // Template tối giản: bỏ cột icon màu trắng/xám mặc định của WPF ở bên trái menu
        private const string XmlnsDecl =
            "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
            "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'";

        private static readonly ControlTemplate MenuTemplate = (ControlTemplate)XamlReader.Parse(
            $@"<ControlTemplate {XmlnsDecl} TargetType='ContextMenu'>
                 <Border Background='{{TemplateBinding Background}}'
                         BorderBrush='{{TemplateBinding BorderBrush}}'
                         BorderThickness='{{TemplateBinding BorderThickness}}'
                         Padding='{{TemplateBinding Padding}}'
                         CornerRadius='6'>
                   <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
                 </Border>
               </ControlTemplate>");

        private static readonly ControlTemplate ItemTemplate = (ControlTemplate)XamlReader.Parse(
            $@"<ControlTemplate {XmlnsDecl} TargetType='MenuItem'>
                 <Border x:Name='Bd' Background='Transparent' CornerRadius='4'
                         Padding='{{TemplateBinding Padding}}' SnapsToDevicePixels='True'>
                   <ContentPresenter ContentSource='Header' RecognizesAccessKey='False' VerticalAlignment='Center'/>
                 </Border>
                 <ControlTemplate.Triggers>
                   <Trigger Property='IsHighlighted' Value='True'>
                     <Setter TargetName='Bd' Property='Background' Value='#3D3960'/>
                   </Trigger>
                 </ControlTemplate.Triggers>
               </ControlTemplate>");

        private static ContextMenu CreateDarkMenu()
        {
            var cm = new ContextMenu
            {
                Background = new SolidColorBrush(Color.FromRgb(29, 27, 51)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(61, 57, 96)),
                BorderThickness = new Thickness(1.5),
                Padding = new Thickness(4),
                Template = MenuTemplate
            };

            // Áp template tối cho mọi MenuItem trong menu này
            var itemStyle = new Style(typeof(MenuItem));
            itemStyle.Setters.Add(new Setter(Control.TemplateProperty, ItemTemplate));
            cm.Resources.Add(typeof(MenuItem), itemStyle);
            return cm;
        }

        // Menu chọn người chơi khi tìm theo tên ra nhiều kết quả (trùng tên hoặc khớp một phần)
        public static void ShowPlayerPicker(FrameworkElement targetElement, IEnumerable<UserData> players, Action<UserData> onPick)
        {
            var cm = CreateDarkMenu();
            foreach (var u in players)
            {
                var picked = u;
                var mi = new MenuItem
                {
                    Header = $"{picked.displayName}  ·  ELO {picked.elo}  ·  @{picked.username}",
                    Foreground = Brushes.White,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Padding = new Thickness(8, 6, 8, 6)
                };
                mi.Click += (s, e) => onPick(picked);
                cm.Items.Add(mi);
            }

            cm.PlacementTarget = targetElement;
            cm.Placement = PlacementMode.Bottom;
            cm.IsOpen = true;
        }

        public static async Task ShowAsync(FrameworkElement targetElement, string targetUsername, Window owner, Action? onFriendStateChanged = null)
        {
            if (string.IsNullOrWhiteSpace(targetUsername)) return;
            string cleanUser = targetUsername.Trim();

            var cm = CreateDarkMenu();

            // 1. Option: Xem thông tin & Lịch sử đấu
            var miProfile = new MenuItem
            {
                Header = "👁️ Xem thông tin & Lịch sử đấu",
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(8, 6, 8, 6)
            };
            // Nếu đang mở từ một trận đấu thật đang diễn ra (Đấu máy / Online), không cho phép
            // bấm "Xem lại" nhảy sang trận khác trong Lịch sử đấu, tránh xung đột với trận hiện tại.
            bool blockReplayNavigation = owner is GameWindow ownerGameWindow && ownerGameWindow.IsLiveMatch;

            miProfile.Click += (s, e) =>
            {
                var dlg = new PlayerProfileDialog(cleanUser, blockReplayNavigation) { Owner = owner };
                dlg.ShowDialog();
            };
            cm.Items.Add(miProfile);

            // 2. Option: Kết bạn / Huỷ kết bạn
            if (AuthService.IsLoggedIn && AuthService.CurrentUser != null)
            {
                string myUsername = AuthService.CurrentUser.username;
                bool isSelf = cleanUser.Equals(myUsername, StringComparison.OrdinalIgnoreCase);
                bool isBot = cleanUser.StartsWith("bot_", StringComparison.OrdinalIgnoreCase) ||
                             cleanUser.Contains("máy", StringComparison.OrdinalIgnoreCase) ||
                             cleanUser.Contains("stockfish", StringComparison.OrdinalIgnoreCase);

                if (!isSelf && !isBot)
                {
                    bool isFriend = await FriendService.IsFriendAsync(myUsername, cleanUser);

                    if (isFriend)
                    {
                        var miRemoveFriend = new MenuItem
                        {
                            Header = "✕ Huỷ kết bạn",
                            Foreground = new SolidColorBrush(Color.FromRgb(255, 82, 82)),
                            FontSize = 13,
                            FontWeight = FontWeights.SemiBold,
                            Padding = new Thickness(8, 6, 8, 6)
                        };
                        miRemoveFriend.Click += async (s, e) =>
                        {
                            var confirm = MessageBox.Show($"Bạn có chắc chắn muốn huỷ kết bạn với {cleanUser}?",
                                "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question);
                            if (confirm == MessageBoxResult.Yes)
                            {
                                await FriendService.RemoveFriendAsync(myUsername, cleanUser);
                                MessageBox.Show($"Đã huỷ kết bạn với {cleanUser}.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                                onFriendStateChanged?.Invoke();
                            }
                        };
                        cm.Items.Add(miRemoveFriend);
                    }
                    else
                    {
                        bool requestSent = await FriendService.HasSentFriendRequestAsync(myUsername, cleanUser);

                        if (requestSent)
                        {
                            var miCancelRequest = new MenuItem
                            {
                                Header = "✕ Huỷ yêu cầu",
                                Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                                FontSize = 13,
                                FontWeight = FontWeights.SemiBold,
                                Padding = new Thickness(8, 6, 8, 6)
                            };
                            miCancelRequest.Click += async (s, e) =>
                            {
                                await FriendService.CancelFriendRequestAsync(myUsername, cleanUser);
                                onFriendStateChanged?.Invoke();
                            };
                            cm.Items.Add(miCancelRequest);
                        }
                        else
                        {
                            var miAddFriend = new MenuItem
                            {
                                Header = "+ Gửi lời mời kết bạn",
                                Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118)),
                                FontSize = 13,
                                FontWeight = FontWeights.SemiBold,
                                Padding = new Thickness(8, 6, 8, 6)
                            };
                            miAddFriend.Click += async (s, e) =>
                            {
                                // Gửi thành công thì im lặng, chỉ báo khi có lỗi (không tìm thấy user, v.v.)
                                var (ok, msg) = await FriendService.SendFriendRequestAsync(myUsername, cleanUser);
                                if (ok)
                                    onFriendStateChanged?.Invoke();
                                else
                                    MessageBox.Show(msg, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                            };
                            cm.Items.Add(miAddFriend);
                        }
                    }
                }
            }

            cm.PlacementTarget = targetElement;
            cm.IsOpen = true;
        }
    }
}
