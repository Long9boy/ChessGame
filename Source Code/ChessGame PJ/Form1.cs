using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ChessGame_PJ
{
    public partial class Form1 : Form
    {
        private Button[,] chessBoard = new Button[8, 8];
        private Color[,] originalSquareColor = new Color[8, 8]; // màu ô bàn cờ gốc (so le) lấy từ Designer
        private ChessGame chessGame = new ChessGame();

        // Thông tin phiên chơi
        private string roomId = "";
        private string playerName = "";
        private string myColor = "white"; // "white" = player1 (người tạo phòng), "black" = player2

        private System.Windows.Forms.Timer pollTimer = new System.Windows.Forms.Timer();
        private bool isBusy = false;           // tránh chồng lệnh khi đang gọi Firebase
        private bool isProcessingClick = false;
        private RoomData? lastRoom = null;
        private bool announcedCheckmate = false;

        // Ô đang chọn cục bộ (trước khi xác nhận qua Firebase)
        private int selectedRow = -1;
        private int selectedCol = -1;

        // Dùng cho Designer (không dùng để chạy thật)
        public Form1()
        {
            InitializeComponent();
        }

        // Dùng khi vào phòng thật từ Form2
        public Form1(string roomId, string playerName, bool isCreator) : this()
        {
            this.roomId = roomId;
            this.playerName = playerName;
            this.myColor = isCreator ? "white" : "black";
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            InitializeChessBoard();
            UpdateBoardUI();

            textBox11.Text = myColor == "white" ? playerName : "";
            textBox10.Text = myColor == "black" ? playerName : "";

            Text = $"Phòng: {roomId}  |  Bạn: {playerName} ({(myColor == "white" ? "Trắng" : "Đen")})";

            pollTimer.Interval = 1200;
            pollTimer.Tick += async (s, ev) => await PollRoomAsync();
            pollTimer.Start();

            // Poll ngay lần đầu
            _ = PollRoomAsync();
        }

        private void InitializeChessBoard()
        {
            for (int row = 0; row < 8; row++)
            {
                for (int col = 0; col < 8; col++)
                {
                    int buttonIndex = row * 8 + col + 1;
                    string btnName = $"button{buttonIndex}";

                    Control[] foundControls = this.Controls.Find(btnName, true);

                    if (foundControls.Length > 0 && foundControls[0] is Button btn)
                    {
                        btn.Tag = (row, col);
                        btn.FlatStyle = FlatStyle.Flat;
                        btn.FlatAppearance.BorderSize = 0;
                        btn.TextAlign = ContentAlignment.MiddleCenter;
                        btn.Font = new Font(btn.Font.FontFamily, 20f, FontStyle.Bold);

                        btn.Click -= ChessButton_Click;
                        btn.Click += ChessButton_Click;

                        chessBoard[row, col] = btn;
                        originalSquareColor[row, col] = btn.BackColor; // lưu lại màu ô so le gốc
                    }
                }
            }
        }

        // ================= FIREBASE SYNC =================

        private async System.Threading.Tasks.Task PollRoomAsync()
        {
            if (isBusy || string.IsNullOrEmpty(roomId)) return;
            isBusy = true;
            try
            {
                RoomData? room = await FirebaseClient.GetAsync<RoomData>($"rooms/{roomId}");
                if (room == null) { isBusy = false; return; }

                lastRoom = room;
                ApplyRoomToLocalGame(room);
                UpdateBoardUI();
                RenderSelectionAndDots(room);
                UpdatePlayerLabels(room);

                if (room.status == "checkmate" && !announcedCheckmate)
                {
                    announcedCheckmate = true;
                    pollTimer.Stop();
                    string winnerName = room.winner == "white" ? room.player1 : room.player2;
                    MessageBox.Show($"Chiếu hết! {winnerName} ({(room.winner == "white" ? "Trắng" : "Đen")}) thắng!",
                        "Kết thúc ván đấu", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            finally
            {
                isBusy = false;
            }
        }

        private void UpdatePlayerLabels(RoomData room)
        {
            textBox11.Text = room.player1 ?? "";
            textBox10.Text = room.player2 ?? "";
        }

        private void ApplyRoomToLocalGame(RoomData room)
        {
            var board = chessGame.GetBoardState();
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    string key = $"{r}_{c}";
                    board[r, c] = room.board != null && room.board.ContainsKey(key) ? room.board[key] : "";
                }
            }

            chessGame.WhiteKingMoved = room.wKingMoved;
            chessGame.BlackKingMoved = room.bKingMoved;
            chessGame.WhiteRookAMoved = room.wRookAMoved;
            chessGame.WhiteRookHMoved = room.wRookHMoved;
            chessGame.BlackRookAMoved = room.bRookAMoved;
            chessGame.BlackRookHMoved = room.bRookHMoved;
        }

        private Dictionary<string, string> BoardToDict()
        {
            var dict = new Dictionary<string, string>();
            var board = chessGame.GetBoardState();
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    dict[$"{r}_{c}"] = board[r, c] ?? "";
            return dict;
        }

        // ================= UI RENDER =================

        private void UpdateBoardUI()
        {
            string[,] boardState = chessGame.GetBoardState();

            for (int row = 0; row < 8; row++)
            {
                for (int col = 0; col < 8; col++)
                {
                    string pieceValue = boardState[row, col];
                    Button? btn = chessBoard[row, col];
                    if (btn == null) continue;

                    if (string.IsNullOrEmpty(pieceValue))
                    {
                        btn.BackgroundImage = null;
                    }
                    else
                    {
                        Image? img = ImageResources.GetPieceImage(pieceValue);
                        btn.BackgroundImage = img;
                        if (img != null) btn.BackgroundImageLayout = ImageLayout.Zoom;
                    }
                }
            }
        }

        // Vẽ viền vàng ô đang chọn + chấm tròn gợi ý nước đi + nền đỏ nếu vua đang bị chiếu
        private void RenderSelectionAndDots(RoomData room)
        {
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    Button? btn = chessBoard[r, c];
                    if (btn == null) continue;

                    btn.FlatAppearance.BorderSize = 0;
                    btn.Text = "";
                    btn.BackColor = originalSquareColor[r, c]; // trả lại màu ô so le gốc
                }
            }

            if (room.selRow >= 0 && room.selCol >= 0)
            {
                Button? selBtn = chessBoard[room.selRow, room.selCol];
                if (selBtn != null)
                {
                    selBtn.FlatAppearance.BorderSize = 3;
                    selBtn.FlatAppearance.BorderColor = Color.Gold;
                }

                if (room.legalMoves != null)
                {
                    foreach (string key in room.legalMoves)
                    {
                        string[] parts = key.Split('_');
                        if (parts.Length != 2) continue;
                        if (!int.TryParse(parts[0], out int mr)) continue;
                        if (!int.TryParse(parts[1], out int mc)) continue;

                        Button? mbtn = chessBoard[mr, mc];
                        if (mbtn != null)
                        {
                            mbtn.Text = "●";
                            mbtn.ForeColor = Color.FromArgb(200, 40, 40, 40);
                        }
                    }
                }
            }

            // Nền đỏ dưới quân vua đang bị chiếu
            if (!string.IsNullOrEmpty(room.check))
            {
                var (kr, kc) = chessGame.FindKing(room.check, chessGame.GetBoardState());
                if (kr >= 0)
                {
                    Button? kbtn = chessBoard[kr, kc];
                    if (kbtn != null) kbtn.BackColor = Color.Red;
                }
            }
        }

        // ================= CLICK HANDLING =================

        private async void ChessButton_Click(object? sender, EventArgs e)
        {
            if (isProcessingClick) return;
            if (sender is not Button clickedBtn) return;
            if (clickedBtn.Tag is not ValueTuple<int, int> tag) return;

            int row = tag.Item1;
            int col = tag.Item2;

            RoomData? room = lastRoom;
            if (room == null) return;

            // Phòng chưa đủ 2 người
            if (string.IsNullOrEmpty(room.player2))
            {
                MessageBox.Show("Chưa có ai vào phòng, vui lòng đợi.", "Thông báo",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (room.status == "checkmate") return;

            // Chưa tới lượt
            if (room.turn != myColor)
            {
                MessageBox.Show("Chưa đến lượt của bạn.", "Thông báo",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            isProcessingClick = true;
            try
            {
                string[,] boardState = chessGame.GetBoardState();
                string clickedPiece = boardState[row, col];

                bool hasSelection = room.selRow >= 0 && room.selCol >= 0;

                if (!hasSelection)
                {
                    // Chưa chọn quân nào: click vào ô trống -> không làm gì
                    if (string.IsNullOrEmpty(clickedPiece)) return;

                    // Chỉ được chọn quân của chính mình
                    if (ChessGame.ColorOf(clickedPiece) != myColor) return;

                    var legalMoves = chessGame.GetLegalMoves(row, col);
                    await PushSelection(row, col, legalMoves);
                }
                else
                {
                    // Click lại đúng ô đang chọn -> bỏ chọn
                    if (room.selRow == row && room.selCol == col)
                    {
                        await ClearSelection();
                        return;
                    }

                    var currentLegalMoves = chessGame.GetLegalMoves(room.selRow, room.selCol);
                    bool isLegalTarget = currentLegalMoves.Any(m => m.row == row && m.col == col);

                    if (isLegalTarget)
                    {
                        await ExecuteMove(room.selRow, room.selCol, row, col);
                    }
                    else if (!string.IsNullOrEmpty(clickedPiece) && ChessGame.ColorOf(clickedPiece) == myColor)
                    {
                        // Chọn lại quân khác của mình
                        var legalMoves = chessGame.GetLegalMoves(row, col);
                        await PushSelection(row, col, legalMoves);
                    }
                    // Click ô "" (không hợp lệ, không phải quân mình) -> không làm gì
                }
            }
            finally
            {
                isProcessingClick = false;
            }
        }

        private async System.Threading.Tasks.Task PushSelection(int row, int col, List<(int row, int col)> legalMoves)
        {
            var patch = new Dictionary<string, object>
            {
                ["selRow"] = row,
                ["selCol"] = col,
                ["legalMoves"] = legalMoves.Select(m => $"{m.row}_{m.col}").ToList(),
                ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            await FirebaseClient.PatchAsync($"rooms/{roomId}", patch);
            await PollRoomAsync();
        }

        private async System.Threading.Tasks.Task ClearSelection()
        {
            var patch = new Dictionary<string, object>
            {
                ["selRow"] = -1,
                ["selCol"] = -1,
                ["legalMoves"] = new List<string>(),
                ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            await FirebaseClient.PatchAsync($"rooms/{roomId}", patch);
            await PollRoomAsync();
        }

        private async System.Threading.Tasks.Task ExecuteMove(int fromRow, int fromCol, int toRow, int toCol)
        {
            MoveResult moveResult = chessGame.TryMove(fromRow, fromCol, toRow, toCol);

            if (moveResult.NeedsPromotion)
            {
                using var promoForm = new PromotionForm(myColor);
                DialogResult dr = promoForm.ShowDialog(this);
                if (dr != DialogResult.OK)
                {
                    // Người chơi huỷ chọn phong tốt -> huỷ nước đi
                    return;
                }
                moveResult = chessGame.TryMove(fromRow, fromCol, toRow, toCol, promoForm.SelectedPiece);
            }

            if (!moveResult.Success)
            {
                MessageBox.Show(moveResult.ErrorMessage, "Nước đi không hợp lệ",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string nextTurn = myColor == "white" ? "black" : "white";
            string status = moveResult.IsCheckmate ? "checkmate" : "playing";

            var patch = new Dictionary<string, object>
            {
                ["board"] = BoardToDict(),
                ["turn"] = nextTurn,
                ["status"] = status,
                ["winner"] = moveResult.Winner ?? "",
                ["check"] = moveResult.CheckedColor ?? "",
                ["selRow"] = -1,
                ["selCol"] = -1,
                ["legalMoves"] = new List<string>(),
                ["wKingMoved"] = chessGame.WhiteKingMoved,
                ["bKingMoved"] = chessGame.BlackKingMoved,
                ["wRookAMoved"] = chessGame.WhiteRookAMoved,
                ["wRookHMoved"] = chessGame.WhiteRookHMoved,
                ["bRookAMoved"] = chessGame.BlackRookAMoved,
                ["bRookHMoved"] = chessGame.BlackRookHMoved,
                ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await FirebaseClient.PatchAsync($"rooms/{roomId}", patch);

            if (moveResult.CheckedColor == nextTurn && !moveResult.IsCheckmate)
            {
                MessageBox.Show("Chiếu tướng!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            await PollRoomAsync();
        }

        private void textBox10_TextChanged(object sender, EventArgs e) { }

        private void textBox11_TextChanged(object sender, EventArgs e) { }
    }
}
