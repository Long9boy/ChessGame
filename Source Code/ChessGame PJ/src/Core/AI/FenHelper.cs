using System;
using System.Text;

namespace ChessGame_PJ.Core.AI
{
    public static class FenHelper
    {
        public static string BoardToFen(ChessGame game, string turnColor)
        {
            var board = game.GetBoardState();
            var sb = new StringBuilder();

            // 1. Piece placement (from rank 8 down to rank 1, row 0 to 7)
            for (int r = 0; r < 8; r++)
            {
                int emptyCount = 0;
                for (int c = 0; c < 8; c++)
                {
                    string piece = board[r, c];
                    if (string.IsNullOrEmpty(piece))
                    {
                        emptyCount++;
                    }
                    else
                    {
                        if (emptyCount > 0)
                        {
                            sb.Append(emptyCount);
                            emptyCount = 0;
                        }
                        sb.Append(PieceToFenChar(piece));
                    }
                }
                if (emptyCount > 0)
                {
                    sb.Append(emptyCount);
                }
                if (r < 7)
                {
                    sb.Append('/');
                }
            }

            // 2. Active color
            sb.Append(turnColor == "white" ? " w " : " b ");

            // 3. Castling availability
            var castling = new StringBuilder();
            if (!game.WhiteKingMoved)
            {
                if (!game.WhiteRookHMoved && board[7, 7] == "W_Rook") castling.Append('K');
                if (!game.WhiteRookAMoved && board[7, 0] == "W_Rook") castling.Append('Q');
            }
            if (!game.BlackKingMoved)
            {
                if (!game.BlackRookHMoved && board[0, 7] == "B_Rook") castling.Append('k');
                if (!game.BlackRookAMoved && board[0, 0] == "B_Rook") castling.Append('q');
            }
            if (castling.Length == 0) castling.Append('-');
            sb.Append(castling.ToString());

            // 4. En passant, halfmove clock, fullmove counter
            sb.Append(" - 0 1");

            return sb.ToString();
        }

        private static char PieceToFenChar(string piece)
        {
            char c = ChessGame.PieceType(piece) switch
            {
                "Pawn" => 'P',
                "Knight" => 'N',
                "Bishop" => 'B',
                "Rook" => 'R',
                "Queen" => 'Q',
                "King" => 'K',
                _ => 'P'
            };
            return ChessGame.ColorOf(piece) == "white" ? char.ToUpper(c) : char.ToLower(c);
        }

        public static (int fromRow, int fromCol, int toRow, int toCol, string? promotion) ParseUciMove(string uci)
        {
            if (string.IsNullOrWhiteSpace(uci) || uci.Length < 4)
            {
                return (-1, -1, -1, -1, null);
            }

            int fromCol = uci[0] - 'a';
            int fromRow = 8 - (uci[1] - '0');
            int toCol = uci[2] - 'a';
            int toRow = 8 - (uci[3] - '0');

            string? promo = null;
            if (uci.Length >= 5)
            {
                promo = char.ToLower(uci[4]) switch
                {
                    'q' => "Queen",
                    'r' => "Rook",
                    'b' => "Bishop",
                    'n' => "Knight",
                    _ => "Queen"
                };
            }

            return (fromRow, fromCol, toRow, toCol, promo);
        }

        public static string MoveToUci(int fromRow, int fromCol, int toRow, int toCol, string? promo = null)
        {
            char fCol = (char)('a' + fromCol);
            char fRow = (char)('0' + (8 - fromRow));
            char tCol = (char)('a' + toCol);
            char tRow = (char)('0' + (8 - toRow));

            string baseMove = $"{fCol}{fRow}{tCol}{tRow}";
            if (!string.IsNullOrEmpty(promo))
            {
                char p = char.ToLower(promo[0]);
                if (p == 'k') p = 'n'; // knight
                baseMove += p;
            }
            return baseMove;
        }

        /// <summary>
        /// Tạo đối tượng ChessGame từ chuỗi FEN để kiểm tra thế trận (ví dụ chiếu, chiếu hết).
        /// </summary>
        public static ChessGame CreateGameFromFen(string fen)
        {
            var game = new ChessGame();
            var board = game.GetBoardState();
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    board[r, c] = "";
                }
            }

            if (string.IsNullOrWhiteSpace(fen)) return game;
            var parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return game;

            var ranks = parts[0].Split('/');
            for (int r = 0; r < Math.Min(8, ranks.Length); r++)
            {
                int c = 0;
                foreach (char ch in ranks[r])
                {
                    if (c >= 8) break;
                    if (char.IsDigit(ch))
                    {
                        c += (ch - '0');
                    }
                    else
                    {
                        string color = char.IsUpper(ch) ? "W" : "B";
                        string type = char.ToLowerInvariant(ch) switch
                        {
                            'p' => "Pawn",
                            'n' => "Knight",
                            'b' => "Bishop",
                            'r' => "Rook",
                            'q' => "Queen",
                            'k' => "King",
                            _ => "Pawn"
                        };
                        board[r, c] = $"{color}_{type}";
                        c++;
                    }
                }
            }
            return game;
        }
    }
}
