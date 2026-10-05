using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ChessGame_PJ.Views
{
    public partial class PromotionWindow : Window
    {
        public string SelectedPiece { get; private set; } = "Queen";

        public PromotionWindow(string color) // "white" or "black"
        {
            InitializeComponent();
            string prefix = color == "white" ? "W_" : "B_";
            string[] pieces = { "Queen", "Rook", "Bishop", "Knight" };

            foreach (string p in pieces)
            {
                var border = new Border
                {
                    Width = 64,
                    Height = 64,
                    Margin = new Thickness(6),
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(Color.FromRgb(42, 39, 68)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(84, 80, 122)),
                    BorderThickness = new Thickness(1.5),
                    Cursor = Cursors.Hand,
                    Tag = p
                };

                var img = new Image
                {
                    Source = ImageResources.GetPromotionImage(prefix, p),
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(4)
                };
                border.Child = img;

                border.MouseEnter += (s, e) =>
                {
                    if (s is Border b)
                    {
                        b.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 229, 255));
                        b.BorderThickness = new Thickness(2.5);
                    }
                };

                border.MouseLeave += (s, e) =>
                {
                    if (s is Border b)
                    {
                        b.BorderBrush = new SolidColorBrush(Color.FromRgb(84, 80, 122));
                        b.BorderThickness = new Thickness(1.5);
                    }
                };

                border.MouseLeftButtonUp += (s, e) =>
                {
                    if (s is Border b && b.Tag is string chosen)
                    {
                        SelectedPiece = chosen;
                        DialogResult = true;
                        Close();
                    }
                };

                pnlPieces.Children.Add(border);
            }
        }
    }
}