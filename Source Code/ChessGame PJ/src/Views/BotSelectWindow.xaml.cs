using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ChessGame_PJ.Core;

namespace ChessGame_PJ.Views
{
    public partial class BotSelectWindow : Window
    {
        public BotDifficulty SelectedDifficulty { get; private set; } = BotDifficulty.Easy;
        public string SelectedColor { get; private set; } = "white";
        public bool EnableMoveHints { get; private set; } = true;

        public BotSelectWindow()
        {
            InitializeComponent();
            SelectDifficulty(BotDifficulty.Easy);
            imgStockfishAvatar.Source = ImageResources.GetImageByFileName("stockfish.png");
        }

        private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.Tag is string tag)
            {
                if (Enum.TryParse<BotDifficulty>(tag, out var diff))
                {
                    SelectDifficulty(diff);
                }
            }
        }

        private void SelectDifficulty(BotDifficulty diff)
        {
            SelectedDifficulty = diff;
            UpdateCardState(cardEasy, badgeEasy, diff == BotDifficulty.Easy, Color.FromRgb(0, 230, 118));
            UpdateCardState(cardMedium, badgeMedium, diff == BotDifficulty.Medium, Color.FromRgb(255, 215, 0));
            UpdateCardState(cardHard, badgeHard, diff == BotDifficulty.Hard, Color.FromRgb(255, 82, 82));
            UpdateCardState(cardStockfish, badgeStockfish, diff == BotDifficulty.Stockfish, Color.FromRgb(0, 229, 255));
        }

        private static void UpdateCardState(Border card, FrameworkElement badge, bool isSelected, Color accent)
        {
            if (isSelected)
            {
                card.BorderBrush = new SolidColorBrush(accent);
                card.BorderThickness = new Thickness(2);
                card.Background = new SolidColorBrush(Color.FromRgb(47, 42, 74));
                card.Effect = new DropShadowEffect
                {
                    Color = accent,
                    BlurRadius = 14,
                    ShadowDepth = 0,
                    Opacity = 0.5
                };
                if (badge != null) badge.Visibility = Visibility.Visible;
            }
            else
            {
                card.BorderBrush = new SolidColorBrush(Color.FromRgb(61, 56, 94));
                card.BorderThickness = new Thickness(1);
                card.Background = new SolidColorBrush(Color.FromRgb(35, 32, 53));
                card.Effect = null;
                if (badge != null) badge.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            SelectedColor = rbWhite.IsChecked == true ? "white" : "black";
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
