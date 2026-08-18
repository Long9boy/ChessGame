using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace ChessGame_PJ
{
    public partial class Form2 : Form
    {
        private const string NamePrefix = "Nhập tên:";
        private const string IdPrefix = "Nhập ID:";

        public Form2()
        {
            InitializeComponent();
        }

        private string GetPlayerName()
        {
            string text = textBox2.Text ?? "";
            if (text.StartsWith(NamePrefix)) text = text.Substring(NamePrefix.Length);
            return text.Trim();
        }

        private string GetRoomIdInput()
        {
            string text = textBox1.Text ?? "";
            if (text.StartsWith(IdPrefix)) text = text.Substring(IdPrefix.Length);
            return text.Trim();
        }

        private static string GenerateRoomId()
        {
            Random rnd = new Random();
            return rnd.Next(100000, 999999).ToString();
        }

        private RoomData CreateInitialRoom(string creatorName)
        {
            var game = new ChessGame();
            var board = game.GetBoardState();
            var boardDict = new Dictionary<string, string>();
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    boardDict[$"{r}_{c}"] = board[r, c] ?? "";

            return new RoomData
            {
                player1 = creatorName,
                player2 = "",
                turn = "white",
                status = "waiting",
                winner = "",
                check = "",
                board = boardDict,
                selRow = -1,
                selCol = -1,
                legalMoves = new List<string>(),
                updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }

        // Nút "Tạo phòng"
        private async void button2_Click(object sender, EventArgs e)
        {
            string name = GetPlayerName();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Vui lòng nhập tên trước khi tạo phòng.", "Thiếu tên",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            button2.Enabled = false;
            button1.Enabled = false;
            try
            {
                string newRoomId = GenerateRoomId();
                RoomData room = CreateInitialRoom(name);

                bool ok = await FirebaseClient.PutAsync($"rooms/{newRoomId}", room);
                if (!ok)
                {
                    MessageBox.Show("Không thể tạo phòng, vui lòng kiểm tra kết nối mạng và thử lại.",
                        "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                MessageBox.Show($"Đã tạo phòng thành công!\nMã phòng: {newRoomId}\nHãy gửi mã này cho đối thủ.",
                    "Tạo phòng thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);

                var form1 = new Form1(newRoomId, name, isCreator: true);
                form1.FormClosed += (s, ev) => Application.Exit();
                form1.Show();
                this.Hide();
            }
            finally
            {
                button2.Enabled = true;
                button1.Enabled = true;
            }
        }

        // Nút "vào phòng"
        private async void button1_Click(object sender, EventArgs e)
        {
            string name = GetPlayerName();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Vui lòng nhập tên trước khi vào phòng.", "Thiếu tên",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string roomId = GetRoomIdInput();
            if (string.IsNullOrEmpty(roomId))
            {
                MessageBox.Show("Vui lòng nhập ID phòng.", "Thiếu ID phòng",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            button2.Enabled = false;
            button1.Enabled = false;
            try
            {
                RoomData? room = await FirebaseClient.GetAsync<RoomData>($"rooms/{roomId}");
                if (room == null)
                {
                    MessageBox.Show("Không tìm thấy phòng với ID này.", "Lỗi",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (!string.IsNullOrEmpty(room.player2))
                {
                    MessageBox.Show("Phòng này đã đủ người chơi.", "Phòng đầy",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var patch = new Dictionary<string, object>
                {
                    ["player2"] = name,
                    ["status"] = "playing",
                    ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                bool ok = await FirebaseClient.PatchAsync($"rooms/{roomId}", patch);
                if (!ok)
                {
                    MessageBox.Show("Không thể vào phòng, vui lòng thử lại.", "Lỗi",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var form1 = new Form1(roomId, name, isCreator: false);
                form1.FormClosed += (s, ev) => Application.Exit();
                form1.Show();
                this.Hide();
            }
            finally
            {
                button2.Enabled = true;
                button1.Enabled = true;
            }
        }

        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete) && textBox1.SelectionStart <= IdPrefix.Length)
            {
                e.SuppressKeyPress = true;
            }
        }

        private void textBox1_MouseDown(object sender, MouseEventArgs e)
        {
            if (textBox1.SelectionStart < IdPrefix.Length)
            {
                textBox1.SelectionStart = textBox1.Text.Length;
            }
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            if (!textBox1.Text.StartsWith(IdPrefix))
            {
                textBox1.Text = IdPrefix;
                textBox1.SelectionStart = textBox1.Text.Length;
            }
        }

        private void textBox2_KeyDown(object sender, KeyEventArgs e)
        {
            if ((e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete) && textBox2.SelectionStart <= NamePrefix.Length)
            {
                e.SuppressKeyPress = true;
            }
        }

        private void textBox2_MouseDown(object sender, MouseEventArgs e)
        {
            if (textBox2.SelectionStart < NamePrefix.Length)
            {
                textBox2.SelectionStart = textBox2.Text.Length;
            }
        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {
            if (!textBox2.Text.StartsWith(NamePrefix))
            {
                textBox2.Text = NamePrefix;
                textBox2.SelectionStart = textBox2.Text.Length;
            }
        }
    }
}
