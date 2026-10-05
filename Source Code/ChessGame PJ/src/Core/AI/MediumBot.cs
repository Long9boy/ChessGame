using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChessGame_PJ.Core.AI
{
    public class MediumBot : IChessBot
    {
        private static readonly Random random = new Random();

        public static int GetPieceValue(string piece)
        {
            if (string.IsNullOrEmpty(piece)) return 0;
            return ChessGame.PieceType(piece) switch
            {
                "Pawn" => 100,
                "Knight" => 320,
                "Bishop" => 330,
                "Rook" => 500,
                "Queen" => 900,
                "King" => 20000,
                _ => 0
            };
        }

        public Task<(int fromRow, int fromCol, int toRow, int toCol, string? promotion)> GetBestMoveAsync(ChessGame game, string botColor)
        {
            var legalMoves = game.GetAllLegalMoves(botColor);
            if (legalMoves.Count == 0)
            {
                return Task.FromResult((-1, -1, -1, -1, (string?)null));
            }

            string oppColor = botColor == "white" ? "black" : "white";
            var currentBoard = game.GetBoardState();

            var scoredMoves = new List<((int fR, int fC, int tR, int tC) move, int score)>();

            foreach (var move in legalMoves)
            {
                int score = 0;
                string movingPiece = currentBoard[move.fromR, move.fromC];
                string targetPiece = currentBoard[move.toR, move.toC];
                int movingVal = GetPieceValue(movingPiece);
                int targetVal = GetPieceValue(targetPiece);

                // 1. Capture profit
                if (!string.IsNullOrEmpty(targetPiece) && ChessGame.ColorOf(targetPiece) == oppColor)
                {
                    score += targetVal;
                }

                // 2. Was moving piece currently in danger?
                bool wasAttacked = game.IsSquareAttacked(move.fromR, move.fromC, oppColor, currentBoard);

                // Simulate move
                var clone = game.Clone();
                var moveRes = clone.TryMove(move.fromR, move.fromC, move.toR, move.toC, "Queen");

                if (moveRes.Success)
                {
                    if (moveRes.IsCheckmate)
                    {
                        score += 50000;
                    }
                    else if (!string.IsNullOrEmpty(moveRes.CheckedColor))
                    {
                        score += 40;
                    }

                    var newBoard = clone.GetBoardState();
                    bool destinationAttacked = clone.IsSquareAttacked(move.toR, move.toC, oppColor, newBoard);

                    // Destination is under fire
                    if (destinationAttacked)
                    {
                        // Blunder penalty if we lost our piece or traded down
                        score -= movingVal;
                    }
                    else if (wasAttacked)
                    {
                        // Successfully escaped danger to a safe square!
                        score += (int)(movingVal * 0.85);
                    }
                }

                // 3. Small bonus for moving towards center
                int distCenter = Math.Abs(move.toR - 3) + Math.Abs(move.toR - 4) + Math.Abs(move.toC - 3) + Math.Abs(move.toC - 4);
                score += (14 - distCenter);

                scoredMoves.Add((move, score));
            }

            // Find best score
            int maxScore = scoredMoves.Max(x => x.score);
            // Select among top moves within 20 points of best score to add variety
            var bestMoves = scoredMoves.Where(x => x.score >= maxScore - 20).Select(x => x.move).ToList();

            var chosen = bestMoves[random.Next(bestMoves.Count)];

            string? promo = null;
            string p = currentBoard[chosen.fR, chosen.fC];
            int lastRank = botColor == "white" ? 0 : 7;
            if (ChessGame.PieceType(p) == "Pawn" && chosen.tR == lastRank)
            {
                promo = "Queen";
            }

            return Task.FromResult((chosen.fR, chosen.fC, chosen.tR, chosen.tC, promo));
        }
    }
}
