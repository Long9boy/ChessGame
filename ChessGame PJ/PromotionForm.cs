using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ChessGame_PJ
{
    // Dialog hiện 4 lựa chọn quân để phong tốt: Queen, Rook, Bishop, Knight
    public class PromotionForm : Form
    {
        public string SelectedPiece { get; private set; } = "Queen";

        public PromotionForm(string color) // "white" hoặc "black"
        {
            Text = "Phong tốt";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(260, 90);

            string prefix = color == "white" ? "W_" : "B_";
            string[] pieces = { "Queen", "Rook", "Bishop", "Knight" };

            Label lbl = new Label
            {
                Text = "Chọn quân để phong:",
                AutoSize = true,
                Location = new Point(10, 8)
            };
            Controls.Add(lbl);

            int x = 10;
            foreach (string p in pieces)
            {
                Button btn = new Button
                {
                    Size = new Size(56, 56),
                    Location = new Point(x, 28),
                    Tag = p
                };

                Image? img = ImageResources.GetPromotionImage(prefix, p);
                if (img != null)
                {
                    btn.BackgroundImage = img;
                    btn.BackgroundImageLayout = ImageLayout.Zoom;
                }
                else
                {
                    btn.Text = p;
                }

                btn.Click += (s, e) =>
                {
                    SelectedPiece = (string)((Button)s!).Tag!;
                    DialogResult = DialogResult.OK;
                    Close();
                };

                Controls.Add(btn);
                x += 62;
            }
        }
    }
}
