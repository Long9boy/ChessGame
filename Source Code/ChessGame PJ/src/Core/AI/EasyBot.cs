using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ChessGame_PJ.Core.AI
{
    public class EasyBot : IChessBot
    {
        private static readonly Random random = new Random();

        public Task<(int fromRow, int fromCol, int toRow, int toCol, string? promotion)> GetBestMoveAsync(ChessGame game, string botColor)
        {
            var legalMoves = game.GetAllLegalMoves(botColor);
            if (legalMoves.Count == 0)
            {
                return Task.FromResult((-1, -1, -1, -1, (string?)null));
            }

            var board = game.GetBoardState();
            var captureMoves = new List<(int fromR, int fromC, int toR, int toC)>();

            foreach (var m in legalMoves)
            {
                string targetPiece = board[m.toR, m.toC];
                if (!string.IsNullOrEmpty(targetPiece) && ChessGame.ColorOf(targetPiece) != botColor)
                {
                    captureMoves.Add(m);
                }
            }

            (int fR, int fC, int tR, int tC) chosenMove;
            if (captureMoves.Count > 0)
            {
                chosenMove = captureMoves[random.Next(captureMoves.Count)];
            }
            else
            {
                chosenMove = legalMoves[random.Next(legalMoves.Count)];
            }

            string? promo = null;
            string movingPiece = board[chosenMove.fR, chosenMove.fC];
            int lastRank = botColor == "white" ? 0 : 7;
            if (ChessGame.PieceType(movingPiece) == "Pawn" && chosenMove.tR == lastRank)
            {
                promo = "Queen";
            }

            return Task.FromResult((chosenMove.fR, chosenMove.fC, chosenMove.tR, chosenMove.tC, promo));
        }
    }
}
