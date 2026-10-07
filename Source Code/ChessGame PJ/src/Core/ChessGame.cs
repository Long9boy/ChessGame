using System;
using System.Collections.Generic;

namespace ChessGame_PJ
{
    public class MoveResult
    {
        public bool Success { get; set; } = false;
        public bool NeedsPromotion { get; set; } = false;
        public bool IsCastle { get; set; } = false;
        public bool IsEnPassant { get; set; } = false;
        public string CapturedPiece { get; set; } = "";
        public string CheckedColor { get; set; } = "";
        public bool IsCheckmate { get; set; } = false;
        public string Winner { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }

    public class ChessGame
    {
        private string[,] boardState = new string[8, 8];
        // Castling check flags
        public bool WhiteKingMoved = false;
        public bool BlackKingMoved = false;
        public bool WhiteRookAMoved = false;
        public bool WhiteRookHMoved = false;
        public bool BlackRookAMoved = false;
        public bool BlackRookHMoved = false;

        // En passant target square (the square skipped by a 2-step pawn advance on the immediately preceding move)
        public (int row, int col)? EnPassantTarget { get; set; } = null;

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
            EnPassantTarget = null;
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

        public static string Opponent(string color) => color == "white" ? "black" : "white";

        public static string PieceType(string piece)
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

        // Raw pseudo-legal moves (ignores check, includes pawn attacks, includes castling squares)
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
                            else if (EnPassantTarget.HasValue && r == EnPassantTarget.Value.row && c == EnPassantTarget.Value.col)
                            {
                                // En passant capture: destination is empty, enemy pawn sits beside at (row, c)
                                if (InBounds(row, c) && !string.IsNullOrEmpty(board[row, c]) && ColorOf(board[row, c]) != color && PieceType(board[row, c]) == "Pawn")
                                {
                                    moves.Add((r, c));
                                }
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

        // Legal moves list (filters out moves that leave king in check, includes castling if legal)
        public List<(int row, int col)> GetLegalMoves(int row, int col)
        {
            var result = new List<(int, int)>();
            string piece = boardState[row, col];
            if (string.IsNullOrEmpty(piece)) return result;
            string color = ColorOf(piece);
            string type = PieceType(piece);

            var pseudo = GetPseudoMoves(row, col, boardState);
            foreach (var (r, c) in pseudo)
            {
                var clone = CloneBoard(boardState);
                bool isEp = type == "Pawn" &&
                            EnPassantTarget.HasValue &&
                            r == EnPassantTarget.Value.row &&
                            c == EnPassantTarget.Value.col &&
                            string.IsNullOrEmpty(boardState[r, c]);

                clone[r, c] = clone[row, col];
                clone[row, col] = "";
                if (isEp)
                {
                    clone[row, c] = "";
                }

                if (!IsInCheck(color, clone))
                    result.Add((r, c));
            }

            // Castling check
            if (PieceType(piece) == "King")
            {
                bool kingMoved = color == "white" ? WhiteKingMoved : BlackKingMoved;
                if (!kingMoved && !IsInCheck(color, boardState))
                {
                    string opp = Opponent(color);

                    bool rookHMoved = color == "white" ? WhiteRookHMoved : BlackRookHMoved;
                    if (!rookHMoved &&
                        string.IsNullOrEmpty(boardState[row, 5]) && string.IsNullOrEmpty(boardState[row, 6]) &&
                        boardState[row, 7] == (color == "white" ? "W_Rook" : "B_Rook") &&
                        !IsSquareAttacked(row, 5, opp, boardState) && !IsSquareAttacked(row, 6, opp, boardState))
                    {
                        result.Add((row, 6));
                    }

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

        // Execute the move. If it is a pawn reaching the last rank and promotionChoice is null,
        // return NeedsPromotion = true and do NOT modify the board—call this function again
        // with the promotionChoice ("Queen"/"Rook"/"Bishop"/"Knight") after the player makes a selection.
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

            bool isCastle = type == "King" && Math.Abs(toCol - fromCol) == 2;
            bool isEnPassant = type == "Pawn" &&
                               EnPassantTarget.HasValue &&
                               toRow == EnPassantTarget.Value.row &&
                               toCol == EnPassantTarget.Value.col &&
                               string.IsNullOrEmpty(boardState[toRow, toCol]);

            string capturedPiece = isEnPassant ? boardState[fromRow, toCol] : boardState[toRow, toCol];

            boardState[toRow, toCol] = piece;
            boardState[fromRow, fromCol] = "";

            if (isEnPassant)
            {
                boardState[fromRow, toCol] = "";
            }

            if (type == "Pawn" && toRow == lastRank && promotionChoice != null)
            {
                boardState[toRow, toCol] = (color == "white" ? "W_" : "B_") + promotionChoice;
            }

            if (isCastle)
            {
                if (toCol == 6)
                {
                    boardState[fromRow, 5] = boardState[fromRow, 7];
                    boardState[fromRow, 7] = "";
                }
                else if (toCol == 2)
                {
                    boardState[fromRow, 3] = boardState[fromRow, 0];
                    boardState[fromRow, 0] = "";
                }
            }

            // Update en passant target for the NEXT turn
            if (type == "Pawn" && Math.Abs(toRow - fromRow) == 2)
            {
                EnPassantTarget = ((fromRow + toRow) / 2, fromCol);
            }
            else
            {
                EnPassantTarget = null;
            }

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
            if (toRow == 0 && toCol == 0) BlackRookAMoved = true;
            if (toRow == 0 && toCol == 7) BlackRookHMoved = true;
            if (toRow == 7 && toCol == 0) WhiteRookAMoved = true;
            if (toRow == 7 && toCol == 7) WhiteRookHMoved = true;

            result.Success = true;
            result.IsCastle = isCastle;
            result.IsEnPassant = isEnPassant;
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

        public ChessGame Clone()
        {
            var clone = new ChessGame();
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    clone.boardState[r, c] = this.boardState[r, c];

            clone.WhiteKingMoved = this.WhiteKingMoved;
            clone.BlackKingMoved = this.BlackKingMoved;
            clone.WhiteRookAMoved = this.WhiteRookAMoved;
            clone.WhiteRookHMoved = this.WhiteRookHMoved;
            clone.BlackRookAMoved = this.BlackRookAMoved;
            clone.BlackRookHMoved = this.BlackRookHMoved;
            clone.EnPassantTarget = this.EnPassantTarget;
            return clone;
        }

        public List<(int fromR, int fromC, int toR, int toC)> GetAllLegalMoves(string color)
        {
            var moves = new List<(int, int, int, int)>();
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    string piece = boardState[r, c];
                    if (!string.IsNullOrEmpty(piece) && ColorOf(piece) == color)
                    {
                        var legal = GetLegalMoves(r, c);
                        foreach (var (tr, tc) in legal)
                        {
                            moves.Add((r, c, tr, tc));
                        }
                    }
                }
            }
            return moves;
        }
    }
}
