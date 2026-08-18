using System;
using System.Collections.Generic;

namespace ChessGame_PJ
{
    public class MoveResult
    {
        public bool Success { get; set; } = false;
        public bool NeedsPromotion { get; set; } = false; // cần hỏi phong tốt trước khi áp dụng
        public bool IsCastle { get; set; } = false;
        public string CapturedPiece { get; set; } = "";
        public string CheckedColor { get; set; } = "";     // "white"/"black" nếu bên đó đang bị chiếu, "" nếu không
        public bool IsCheckmate { get; set; } = false;
        public string Winner { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }

    internal class ChessGame
    {
        private string[,] boardState = new string[8, 8];

        // Cờ theo dõi quyền nhập thành
        public bool WhiteKingMoved = false;
        public bool BlackKingMoved = false;
        public bool WhiteRookAMoved = false;
        public bool WhiteRookHMoved = false;
        public bool BlackRookAMoved = false;
        public bool BlackRookHMoved = false;

        public ChessGame()
        {
            InitializeBoard();
        }

        public void InitializeBoard()
        {
            for (int row = 0; row < 8; row++)
                for (int col = 0; col < 8; col++)
                    boardState[row, col] = "";

            string[] backRowPieces = { "Rook", "Knight", "Bishop", "Queen", "King", "Bishop", "Knight", "Rook" };

            for (int col = 0; col < 8; col++)
            {
                boardState[0, col] = "B_" + backRowPieces[col];
                boardState[1, col] = "B_Pawn";
            }

            for (int col = 0; col < 8; col++)
            {
                boardState[6, col] = "W_Pawn";
                boardState[7, col] = "W_" + backRowPieces[col];
            }

            WhiteKingMoved = BlackKingMoved = false;
            WhiteRookAMoved = WhiteRookHMoved = BlackRookAMoved = BlackRookHMoved = false;
        }

        public string[,] GetBoardState() => boardState;

        public void SetCell(int row, int col, string pieceName)
        {
            if (row >= 0 && row < 8 && col >= 0 && col < 8)
                boardState[row, col] = pieceName;
        }

        // ==== Helpers ====

        private static bool InBounds(int r, int c) => r >= 0 && r < 8 && c >= 0 && c < 8;

        public static string ColorOf(string piece)
        {
            if (string.IsNullOrEmpty(piece)) return "";
            return piece.StartsWith("W_") ? "white" : "black";
        }

        private static string Opponent(string color) => color == "white" ? "black" : "white";

        private static string PieceType(string piece)
        {
            int idx = piece.IndexOf('_');
            return idx >= 0 ? piece.Substring(idx + 1) : piece;
        }

        public (int row, int col) FindKing(string color, string[,] board)
        {
            string target = (color == "white" ? "W_King" : "B_King");
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    if (board[r, c] == target) return (r, c);
            return (-1, -1);
        }

        // Danh sách nước đi "thô" (chưa lọc chiếu) của 1 quân, dùng cho cả tính nước đi và tính ô bị tấn công
        private List<(int r, int c)> GetPseudoMoves(int row, int col, string[,] board, bool attacksOnly = false)
        {
            var moves = new List<(int, int)>();
            string piece = board[row, col];
            if (string.IsNullOrEmpty(piece)) return moves;

            string color = ColorOf(piece);
            string type = PieceType(piece);

            void TryAdd(int r, int c)
            {
                if (!InBounds(r, c)) return;
                string target = board[r, c];
                if (string.IsNullOrEmpty(target) || ColorOf(target) != color)
                    moves.Add((r, c));
            }

            void Slide(int dr, int dc)
            {
                int r = row + dr, c = col + dc;
                while (InBounds(r, c))
                {
                    string target = board[r, c];
                    if (string.IsNullOrEmpty(target))
                    {
                        moves.Add((r, c));
                    }
                    else
                    {
                        if (ColorOf(target) != color) moves.Add((r, c));
                        break;
                    }
                    r += dr; c += dc;
                }
            }

            switch (type)
            {
                case "Pawn":
                    {
                        int dir = color == "white" ? -1 : 1;
                        int startRow = color == "white" ? 6 : 1;

                        if (!attacksOnly)
                        {
                            int oneStep = row + dir;
                            if (InBounds(oneStep, col) && string.IsNullOrEmpty(board[oneStep, col]))
                            {
                                moves.Add((oneStep, col));
                                int twoStep = row + 2 * dir;
                                if (row == startRow && string.IsNullOrEmpty(board[twoStep, col]))
                                    moves.Add((twoStep, col));
                            }
                        }

                        // Ăn chéo (hoặc ô bị tấn công, dùng khi attacksOnly=true)
                        foreach (int dc in new[] { -1, 1 })
                        {
                            int r = row + dir, c = col + dc;
                            if (!InBounds(r, c)) continue;
                            string target = board[r, c];
                            if (attacksOnly)
                            {
                                moves.Add((r, c));
                            }
                            else if (!string.IsNullOrEmpty(target) && ColorOf(target) != color)
                            {
                                moves.Add((r, c));
                            }
                        }
                        break;
                    }
                case "Knight":
                    {
                        int[,] jumps = { { -2, -1 }, { -2, 1 }, { -1, -2 }, { -1, 2 }, { 1, -2 }, { 1, 2 }, { 2, -1 }, { 2, 1 } };
                        for (int i = 0; i < 8; i++)
                            TryAdd(row + jumps[i, 0], col + jumps[i, 1]);
                        break;
                    }
                case "Bishop":
                    Slide(-1, -1); Slide(-1, 1); Slide(1, -1); Slide(1, 1);
                    break;
                case "Rook":
                    Slide(-1, 0); Slide(1, 0); Slide(0, -1); Slide(0, 1);
                    break;
                case "Queen":
                    Slide(-1, -1); Slide(-1, 1); Slide(1, -1); Slide(1, 1);
                    Slide(-1, 0); Slide(1, 0); Slide(0, -1); Slide(0, 1);
                    break;
                case "King":
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                            if (dr != 0 || dc != 0) TryAdd(row + dr, col + dc);
                    break;
            }

            return moves;
        }

        // Ô (r,c) có đang bị 1 quân màu attackerColor tấn công không, trên bàn cờ "board"
        public bool IsSquareAttacked(int row, int col, string attackerColor, string[,] board)
        {
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    string piece = board[r, c];
                    if (string.IsNullOrEmpty(piece) || ColorOf(piece) != attackerColor) continue;

                    var attacks = GetPseudoMoves(r, c, board, attacksOnly: true);
                    foreach (var (ar, ac) in attacks)
                        if (ar == row && ac == col) return true;
                }
            }
            return false;
        }

        public bool IsInCheck(string color, string[,] board)
        {
            var (kr, kc) = FindKing(color, board);
            if (kr < 0) return false;
            return IsSquareAttacked(kr, kc, Opponent(color), board);
        }

        public bool IsInCheck(string color) => IsInCheck(color, boardState);

        private string[,] CloneBoard(string[,] board)
        {
            var clone = new string[8, 8];
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    clone[r, c] = board[r, c];
            return clone;
        }

        // Danh sách nước đi hợp lệ (đã lọc: không để vua mình bị chiếu), bao gồm cả nhập thành
        public List<(int row, int col)> GetLegalMoves(int row, int col)
        {
            var result = new List<(int, int)>();
            string piece = boardState[row, col];
            if (string.IsNullOrEmpty(piece)) return result;
            string color = ColorOf(piece);

            var pseudo = GetPseudoMoves(row, col, boardState);
            foreach (var (r, c) in pseudo)
            {
                var clone = CloneBoard(boardState);
                clone[r, c] = clone[row, col];
                clone[row, col] = "";
                if (!IsInCheck(color, clone))
                    result.Add((r, c));
            }

            // Nhập thành
            if (PieceType(piece) == "King")
            {
                bool kingMoved = color == "white" ? WhiteKingMoved : BlackKingMoved;
                if (!kingMoved && !IsInCheck(color, boardState))
                {
                    string opp = Opponent(color);

                    // Nhập thành ngắn (kingside, xe cột h)
                    bool rookHMoved = color == "white" ? WhiteRookHMoved : BlackRookHMoved;
                    if (!rookHMoved &&
                        string.IsNullOrEmpty(boardState[row, 5]) && string.IsNullOrEmpty(boardState[row, 6]) &&
                        boardState[row, 7] == (color == "white" ? "W_Rook" : "B_Rook") &&
                        !IsSquareAttacked(row, 5, opp, boardState) && !IsSquareAttacked(row, 6, opp, boardState))
                    {
                        result.Add((row, 6));
                    }

                    // Nhập thành dài (queenside, xe cột a)
                    bool rookAMoved = color == "white" ? WhiteRookAMoved : BlackRookAMoved;
                    if (!rookAMoved &&
                        string.IsNullOrEmpty(boardState[row, 1]) && string.IsNullOrEmpty(boardState[row, 2]) && string.IsNullOrEmpty(boardState[row, 3]) &&
                        boardState[row, 0] == (color == "white" ? "W_Rook" : "B_Rook") &&
                        !IsSquareAttacked(row, 2, opp, boardState) && !IsSquareAttacked(row, 3, opp, boardState))
                    {
                        result.Add((row, 2));
                    }
                }
            }

            return result;
        }

        // Còn nước đi hợp lệ nào cho bên "color" không? (dùng để xác định chiếu hết / hết nước)
        public bool HasAnyLegalMove(string color)
        {
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    if (!string.IsNullOrEmpty(boardState[r, c]) && ColorOf(boardState[r, c]) == color)
                        if (GetLegalMoves(r, c).Count > 0) return true;
            return false;
        }

        public bool IsCheckmate(string color)
        {
            return IsInCheck(color) && !HasAnyLegalMove(color);
        }

        // Thực hiện nước đi. Nếu là tốt đến hàng cuối và promotionChoice == null,
        // trả về NeedsPromotion = true và KHÔNG thay đổi bàn cờ - gọi lại hàm này
        // với promotionChoice ("Queen"/"Rook"/"Bishop"/"Knight") sau khi người chơi chọn.
        public MoveResult TryMove(int fromRow, int fromCol, int toRow, int toCol, string? promotionChoice = null)
        {
            var result = new MoveResult();
            string piece = boardState[fromRow, fromCol];
            if (string.IsNullOrEmpty(piece))
            {
                result.ErrorMessage = "Ô trống";
                return result;
            }

            string color = ColorOf(piece);
            string type = PieceType(piece);

            var legalMoves = GetLegalMoves(fromRow, fromCol);
            bool isLegal = legalMoves.Exists(m => m.row == toRow && m.col == toCol);
            if (!isLegal)
            {
                result.ErrorMessage = "Nước đi không hợp lệ";
                return result;
            }

            int lastRank = color == "white" ? 0 : 7;
            if (type == "Pawn" && toRow == lastRank && promotionChoice == null)
            {
                result.NeedsPromotion = true;
                result.Success = false;
                return result;
            }

            // Nhập thành?
            bool isCastle = type == "King" && Math.Abs(toCol - fromCol) == 2;

            string capturedPiece = boardState[toRow, toCol];

            boardState[toRow, toCol] = piece;
            boardState[fromRow, fromCol] = "";

            if (type == "Pawn" && toRow == lastRank && promotionChoice != null)
            {
                boardState[toRow, toCol] = (color == "white" ? "W_" : "B_") + promotionChoice;
            }

            if (isCastle)
            {
                if (toCol == 6) // kingside
                {
                    boardState[fromRow, 5] = boardState[fromRow, 7];
                    boardState[fromRow, 7] = "";
                }
                else if (toCol == 2) // queenside
                {
                    boardState[fromRow, 3] = boardState[fromRow, 0];
                    boardState[fromRow, 0] = "";
                }
            }

            // Cập nhật cờ nhập thành
            if (type == "King")
            {
                if (color == "white") WhiteKingMoved = true; else BlackKingMoved = true;
            }
            if (type == "Rook")
            {
                if (color == "white")
                {
                    if (fromCol == 0) WhiteRookAMoved = true;
                    if (fromCol == 7) WhiteRookHMoved = true;
                }
                else
                {
                    if (fromCol == 0) BlackRookAMoved = true;
                    if (fromCol == 7) BlackRookHMoved = true;
                }
            }
            // Nếu xe bị ăn ngay tại vị trí gốc, cũng mất quyền nhập thành phía đó
            if (toRow == 0 && toCol == 0) BlackRookAMoved = true;
            if (toRow == 0 && toCol == 7) BlackRookHMoved = true;
            if (toRow == 7 && toCol == 0) WhiteRookAMoved = true;
            if (toRow == 7 && toCol == 7) WhiteRookHMoved = true;

            result.Success = true;
            result.IsCastle = isCastle;
            result.CapturedPiece = capturedPiece ?? "";

            string opponentColor = Opponent(color);
            bool oppInCheck = IsInCheck(opponentColor);
            if (oppInCheck)
            {
                bool oppCheckmate = IsCheckmate(opponentColor);
                result.CheckedColor = opponentColor;
                result.IsCheckmate = oppCheckmate;
                if (oppCheckmate) result.Winner = color;
            }

            return result;
        }
    }
}
