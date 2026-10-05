using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ChessGame_PJ.Core.AI
{
    public class HardBot : IChessBot
    {
        private const int MaxDepth = 6;
        private const int MaxQuiescenceDepth = 2;
        private const int CheckmateScore = 1000000;

        // Hard cap on thinking time per move. Iterative deepening stops
        // starting new (deeper) passes once this is exceeded, and always
        // returns the best move found by the last fully-completed pass.
        private const int TimeLimitMs = 800;

        private enum TTFlag : byte { Exact, LowerBound, UpperBound }

        private struct TTEntry
        {
            public int Score;
            public int Depth;
            public TTFlag Flag;
        }

        // Piece values
        private static readonly Dictionary<string, int> PieceValues = new Dictionary<string, int>
        {
            ["Pawn"] = 100,
            ["Knight"] = 320,
            ["Bishop"] = 330,
            ["Rook"] = 500,
            ["Queen"] = 900,
            ["King"] = 20000
        };

        // Positional Piece-Square Tables (PST) from White's perspective (row 0=rank 8, row 7=rank 1)
        private static readonly int[,] PawnPst = {
            {  0,  0,  0,  0,  0,  0,  0,  0 },
            { 50, 50, 50, 50, 50, 50, 50, 50 },
            { 10, 10, 20, 30, 30, 20, 10, 10 },
            {  5,  5, 10, 25, 25, 10,  5,  5 },
            {  0,  0,  0, 20, 20,  0,  0,  0 },
            {  5, -5,-10,  0,  0,-10, -5,  5 },
            {  5, 10, 10,-20,-20, 10, 10,  5 },
            {  0,  0,  0,  0,  0,  0,  0,  0 }
        };

        private static readonly int[,] KnightPst = {
            {-50,-40,-30,-30,-30,-30,-40,-50 },
            {-40,-20,  0,  0,  0,  0,-20,-40 },
            {-30,  0, 10, 15, 15, 10,  0,-30 },
            {-30,  5, 15, 20, 20, 15,  5,-30 },
            {-30,  0, 15, 20, 20, 15,  0,-30 },
            {-30,  5, 10, 15, 15, 10,  5,-30 },
            {-40,-20,  0,  5,  5,  0,-20,-40 },
            {-50,-40,-30,-30,-30,-30,-40,-50 }
        };

        private static readonly int[,] BishopPst = {
            {-20,-10,-10,-10,-10,-10,-10,-20 },
            {-10,  0,  0,  0,  0,  0,  0,-10 },
            {-10,  0,  5, 10, 10,  5,  0,-10 },
            {-10,  5,  5, 10, 10,  5,  5,-10 },
            {-10,  0, 10, 10, 10, 10,  0,-10 },
            {-10, 10, 10, 10, 10, 10, 10,-10 },
            {-10,  5,  0,  0,  0,  0,  5,-10 },
            {-20,-10,-10,-10,-10,-10,-10,-20 }
        };

        private static readonly int[,] RookPst = {
            {  0,  0,  0,  0,  0,  0,  0,  0 },
            {  5, 10, 10, 10, 10, 10, 10,  5 },
            { -5,  0,  0,  0,  0,  0,  0, -5 },
            { -5,  0,  0,  0,  0,  0,  0, -5 },
            { -5,  0,  0,  0,  0,  0,  0, -5 },
            { -5,  0,  0,  0,  0,  0,  0, -5 },
            { -5,  0,  0,  0,  0,  0,  0, -5 },
            {  0,  0,  0,  5,  5,  0,  0,  0 }
        };

        private static readonly int[,] QueenPst = {
            {-20,-10,-10, -5, -5,-10,-10,-20 },
            {-10,  0,  0,  0,  0,  0,  0,-10 },
            {-10,  0,  5,  5,  5,  5,  0,-10 },
            { -5,  0,  5,  5,  5,  5,  0, -5 },
            {  0,  0,  5,  5,  5,  5,  0, -5 },
            {-10,  5,  5,  5,  5,  5,  0,-10 },
            {-10,  0,  5,  0,  0,  0,  0,-10 },
            {-20,-10,-10, -5, -5,-10,-10,-20 }
        };

        private static readonly int[,] KingPst = {
            {-30,-40,-40,-50,-50,-40,-40,-30 },
            {-30,-40,-40,-50,-50,-40,-40,-30 },
            {-30,-40,-40,-50,-50,-40,-40,-30 },
            {-30,-40,-40,-50,-50,-40,-40,-30 },
            {-20,-30,-30,-40,-40,-30,-30,-20 },
            {-10,-20,-20,-20,-20,-20,-20,-10 },
            { 20, 20,  0,  0,  0,  0, 20, 20 },
            { 20, 30, 10,  0,  0, 10, 30, 20 }
        };

        // Endgame king PST: king should move toward the center in the endgame
        private static readonly int[,] KingEndgamePst = {
            {-50,-40,-30,-20,-20,-30,-40,-50 },
            {-30,-20,-10,  0,  0,-10,-20,-30 },
            {-30,-10, 20, 30, 30, 20,-10,-30 },
            {-30,-10, 30, 40, 40, 30,-10,-30 },
            {-30,-10, 30, 40, 40, 30,-10,-30 },
            {-30,-10, 20, 30, 30, 20,-10,-30 },
            {-30,-30,  0,  0,  0,  0,-30,-30 },
            {-50,-30,-30,-30,-30,-30,-30,-50 }
        };

        public async Task<(int fromRow, int fromCol, int toRow, int toCol, string? promotion)> GetBestMoveAsync(ChessGame game, string botColor)
        {
            return await Task.Run(() =>
            {
                var stopwatch = Stopwatch.StartNew();
                var tt = new Dictionary<string, TTEntry>();

                var legalMoves = game.GetAllLegalMoves(botColor);
                if (legalMoves.Count == 0)
                {
                    return (-1, -1, -1, -1, (string?)null);
                }

                var orderedMoves = OrderMoves(legalMoves, game, botColor, null);

                (int fromR, int fromC, int toR, int toC) bestMove = orderedMoves[0];
                int bestScore = int.MinValue;

                // Iterative deepening: search shallow first, then deepen.
                // A time budget bounds total thinking time regardless of MaxDepth -
                // we simply stop starting new passes once we're over budget and
                // return whatever the last *completed* pass found.
                for (int depth = 1; depth <= MaxDepth; depth++)
                {
                    if (stopwatch.ElapsedMilliseconds > TimeLimitMs)
                    {
                        break;
                    }

                    int alpha = int.MinValue + 100;
                    int beta = int.MaxValue - 100;
                    (int fromR, int fromC, int toR, int toC)? currentBest = null;
                    int currentBestScore = int.MinValue;
                    bool passAborted = false;

                    var movesThisPass = PutMoveFirst(orderedMoves, bestMove);

                    foreach (var move in movesThisPass)
                    {
                        // Bail out of a half-finished pass if we've badly overrun
                        // the budget - we still have the previous pass's result.
                        if (stopwatch.ElapsedMilliseconds > TimeLimitMs * 2)
                        {
                            passAborted = true;
                            break;
                        }

                        var clone = game.Clone();
                        var res = clone.TryMove(move.fromR, move.fromC, move.toR, move.toC, "Queen");
                        if (!res.Success) continue;

                        if (res.IsCheckmate)
                        {
                            currentBest = move;
                            currentBestScore = CheckmateScore;
                            break;
                        }

                        string nextTurn = botColor == "white" ? "black" : "white";
                        int score = -Minimax(clone, depth - 1, -beta, -alpha, nextTurn, botColor, stopwatch, tt);

                        if (score > currentBestScore)
                        {
                            currentBestScore = score;
                            currentBest = move;
                        }

                        if (currentBestScore > alpha)
                        {
                            alpha = currentBestScore;
                        }
                        if (alpha >= beta)
                        {
                            break;
                        }
                    }

                    if (!passAborted && currentBest.HasValue)
                    {
                        bestMove = currentBest.Value;
                        bestScore = currentBestScore;
                    }

                    if (bestScore >= CheckmateScore - 100)
                    {
                        break;
                    }
                }

                return (bestMove.fromR, bestMove.fromC, bestMove.toR, bestMove.toC, GetPromotion(game, bestMove, botColor));
            });
        }

        private static List<(int fromR, int fromC, int toR, int toC)> PutMoveFirst(
            List<(int fromR, int fromC, int toR, int toC)> moves,
            (int fromR, int fromC, int toR, int toC) preferred)
        {
            if (!moves.Contains(preferred))
            {
                return moves;
            }
            var result = new List<(int fromR, int fromC, int toR, int toC)>(moves.Count) { preferred };
            result.AddRange(moves.Where(m => m != preferred));
            return result;
        }

        private static string? GetPromotion(ChessGame game, (int fromR, int fromC, int toR, int toC) move, string botColor)
        {
            string piece = game.GetBoardState()[move.fromR, move.fromC];
            int lastRank = botColor == "white" ? 0 : 7;
            if (ChessGame.PieceType(piece) == "Pawn" && move.toR == lastRank)
            {
                return "Queen";
            }
            return null;
        }

        /// <summary>
        /// Negamax with alpha-beta pruning. The returned score is always from
        /// the perspective of <paramref name="currentTurn"/> (the side about to move).
        /// </summary>
        private int Minimax(ChessGame game, int depth, int alpha, int beta, string currentTurn, string botColor, Stopwatch stopwatch, Dictionary<string, TTEntry> tt)
        {
            string oppColor = currentTurn == "white" ? "black" : "white";
            bool inCheck = game.IsInCheck(currentTurn);
            var legalMoves = game.GetAllLegalMoves(currentTurn);

            if (legalMoves.Count == 0)
            {
                if (inCheck)
                {
                    return -CheckmateScore + (MaxDepth - depth);
                }
                return 0; // Stalemate
            }

            if (depth <= 0)
            {
                if (inCheck && depth > -2)
                {
                    depth = 1;
                }
                else
                {
                    return Quiescence(game, alpha, beta, currentTurn, botColor, MaxQuiescenceDepth, stopwatch);
                }
            }

            // Cheap periodic time check to avoid runaway searches on slow hardware.
            if (stopwatch.ElapsedMilliseconds > TimeLimitMs * 3)
            {
                return EvaluateForSide(game, currentTurn, botColor);
            }

            int alphaOrig = alpha;
            string key = GetPositionKey(game, currentTurn);
            if (tt.TryGetValue(key, out var entry) && entry.Depth >= depth)
            {
                if (entry.Flag == TTFlag.Exact)
                {
                    return entry.Score;
                }
                if (entry.Flag == TTFlag.LowerBound && entry.Score > alpha)
                {
                    alpha = entry.Score;
                }
                else if (entry.Flag == TTFlag.UpperBound && entry.Score < beta)
                {
                    beta = entry.Score;
                }
                if (alpha >= beta)
                {
                    return entry.Score;
                }
            }

            var orderedMoves = OrderMoves(legalMoves, game, currentTurn, null);
            var boardBeforeMoves = game.GetBoardState();

            int best = int.MinValue;
            int moveIndex = 0;
            foreach (var move in orderedMoves)
            {
                var clone = game.Clone();
                var res = clone.TryMove(move.fromR, move.fromC, move.toR, move.toC, "Queen");
                if (!res.Success) continue;

                bool isCapture = !string.IsNullOrEmpty(boardBeforeMoves[move.toR, move.toC]);
                bool givesCheck = !string.IsNullOrEmpty(res.CheckedColor);

                int score;

                // Late Move Reduction: moves ordered late (beyond the first few)
                // are, per the move ordering, unlikely to be best. Search them
                // at a shallower depth first with a null window; only pay for a
                // full-depth re-search if they beat alpha, which is rare.
                if (moveIndex >= 3 && depth >= 3 && !isCapture && !givesCheck)
                {
                    int reducedDepth = depth - 2;
                    score = -Minimax(clone, reducedDepth, -alpha - 1, -alpha, oppColor, botColor, stopwatch, tt);
                    if (score > alpha)
                    {
                        score = -Minimax(clone, depth - 1, -beta, -alpha, oppColor, botColor, stopwatch, tt);
                    }
                }
                else
                {
                    score = -Minimax(clone, depth - 1, -beta, -alpha, oppColor, botColor, stopwatch, tt);
                }

                moveIndex++;

                if (score > best)
                {
                    best = score;
                }
                if (best > alpha)
                {
                    alpha = best;
                }
                if (alpha >= beta)
                {
                    break; // Beta cutoff
                }
            }

            var flag = best <= alphaOrig ? TTFlag.UpperBound : (best >= beta ? TTFlag.LowerBound : TTFlag.Exact);
            tt[key] = new TTEntry { Score = best, Depth = depth, Flag = flag };

            return best;
        }

        /// <summary>
        /// Builds a lookup key for the transposition table from piece placement
        /// and side to move. Note: this doesn't encode castling rights or en
        /// passant target, so in rare cases two positions that only differ in
        /// those could share a cache entry - an accepted trade-off for speed.
        /// </summary>
        private static string GetPositionKey(ChessGame game, string turn)
        {
            var board = game.GetBoardState();
            var sb = new System.Text.StringBuilder(80);
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    string p = board[r, c];
                    sb.Append(string.IsNullOrEmpty(p) ? "--" : p);
                }
            }
            sb.Append('|').Append(turn);
            return sb.ToString();
        }

        /// <summary>
        /// Quiescence search: at the search horizon, keep resolving captures
        /// until the position is "quiet", so the static evaluation isn't
        /// fooled by a hanging piece one ply beyond the cutoff.
        /// </summary>
        private int Quiescence(ChessGame game, int alpha, int beta, string currentTurn, string botColor, int depth, Stopwatch stopwatch)
        {
            int standPat = EvaluateForSide(game, currentTurn, botColor);

            if (stopwatch.ElapsedMilliseconds > TimeLimitMs * 3)
            {
                return standPat;
            }

            if (standPat >= beta)
            {
                return beta;
            }
            if (standPat > alpha)
            {
                alpha = standPat;
            }
            if (depth <= 0)
            {
                return alpha;
            }

            string oppColor = currentTurn == "white" ? "black" : "white";
            var board = game.GetBoardState();
            var legalMoves = game.GetAllLegalMoves(currentTurn);

            var captureMoves = legalMoves
                .Where(m =>
                {
                    string target = board[m.toR, m.toC];
                    return !string.IsNullOrEmpty(target) && ChessGame.ColorOf(target) == oppColor;
                })
                .ToList();

            if (captureMoves.Count == 0)
            {
                return alpha;
            }

            var orderedCaptures = OrderMoves(captureMoves, game, currentTurn, null);

            const int DeltaMargin = 200; // safety buffer for positional swings

            foreach (var move in orderedCaptures)
            {
                // Delta pruning: if capturing this piece still can't get us
                // anywhere near alpha even in the best case, don't bother
                // recursing into it.
                string target = board[move.toR, move.toC];
                int captureValue = PieceValues.GetValueOrDefault(ChessGame.PieceType(target), 0);
                if (standPat + captureValue + DeltaMargin < alpha)
                {
                    continue;
                }

                var clone = game.Clone();
                var res = clone.TryMove(move.fromR, move.fromC, move.toR, move.toC, "Queen");
                if (!res.Success) continue;

                int score = -Quiescence(clone, -beta, -alpha, oppColor, botColor, depth - 1, stopwatch);

                if (score >= beta)
                {
                    return beta;
                }
                if (score > alpha)
                {
                    alpha = score;
                }
            }

            return alpha;
        }

        private List<(int fromR, int fromC, int toR, int toC)> OrderMoves(
            List<(int fromR, int fromC, int toR, int toC)> moves,
            ChessGame game,
            string color,
            (int fromR, int fromC, int toR, int toC)? pvMove)
        {
            var board = game.GetBoardState();
            string oppColor = color == "white" ? "black" : "white";
            int lastRank = color == "white" ? 0 : 7;

            // Manual scored list + sort instead of LINQ's OrderByDescending:
            // this method runs at every search node, so avoiding the extra
            // LINQ enumerator/allocation overhead adds up over a full search.
            var scored = new List<((int fromR, int fromC, int toR, int toC) move, int score)>(moves.Count);

            foreach (var m in moves)
            {
                int score = 0;

                if (pvMove.HasValue && m == pvMove.Value)
                {
                    score = 1000000;
                }
                else
                {
                    string target = board[m.toR, m.toC];
                    string moving = board[m.fromR, m.fromC];

                    // MVV-LVA (Most Valuable Victim - Least Valuable Aggressor)
                    if (!string.IsNullOrEmpty(target) && ChessGame.ColorOf(target) == oppColor)
                    {
                        int targetVal = PieceValues.GetValueOrDefault(ChessGame.PieceType(target), 0);
                        int movingVal = PieceValues.GetValueOrDefault(ChessGame.PieceType(moving), 0);
                        score += 10000 + (targetVal * 10 - movingVal);
                    }

                    // Promotions are usually strong.
                    if (ChessGame.PieceType(moving) == "Pawn" && m.toR == lastRank)
                    {
                        score += 9000;
                    }

                    // Center control
                    if ((m.toR == 3 || m.toR == 4) && (m.toC == 3 || m.toC == 4))
                    {
                        score += 30;
                    }
                }

                scored.Add((m, score));
            }

            scored.Sort((a, b) => b.score.CompareTo(a.score));

            var result = new List<(int fromR, int fromC, int toR, int toC)>(scored.Count);
            foreach (var s in scored)
            {
                result.Add(s.move);
            }
            return result;
        }

        /// <summary>
        /// Evaluate the position from the perspective of <paramref name="sideToScore"/>
        /// (positive = good for that side).
        /// </summary>
        private int EvaluateForSide(ChessGame game, string sideToScore, string botColor)
        {
            int botPerspective = EvaluatePosition(game, botColor);
            return sideToScore == botColor ? botPerspective : -botPerspective;
        }

        private int EvaluatePosition(ChessGame game, string botColor)
        {
            // NOTE: deliberately does NOT call GetAllLegalMoves() here.
            // Legal-move generation (with check validation) is the most
            // expensive operation in this engine, and this function runs at
            // every leaf/quiescence node - calling it here was the main
            // reason the bot was slow and CPU-heavy. Material + PST only.
            var board = game.GetBoardState();
            int botScore = 0;
            int oppScore = 0;

            int nonPawnMaterial = 0;
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    string piece = board[r, c];
                    if (string.IsNullOrEmpty(piece)) continue;
                    string pType = ChessGame.PieceType(piece);
                    if (pType != "Pawn" && pType != "King")
                    {
                        nonPawnMaterial += PieceValues.GetValueOrDefault(pType, 0);
                    }
                }
            }
            bool isEndgame = nonPawnMaterial <= 2 * PieceValues["Rook"] + PieceValues["Bishop"];

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    string piece = board[r, c];
                    if (string.IsNullOrEmpty(piece)) continue;

                    string pColor = ChessGame.ColorOf(piece);
                    string pType = ChessGame.PieceType(piece);
                    int matVal = PieceValues.GetValueOrDefault(pType, 0);
                    int pstVal = GetPstValue(pType, r, c, pColor, isEndgame);
                    int totalPieceVal = matVal + pstVal;

                    if (pColor == botColor)
                    {
                        botScore += totalPieceVal;
                    }
                    else
                    {
                        oppScore += totalPieceVal;
                    }
                }
            }

            return botScore - oppScore;
        }

        private int GetPstValue(string pieceType, int r, int c, string color, bool isEndgame)
        {
            int row = color == "white" ? r : (7 - r);
            int col = c;

            if (pieceType == "King" && isEndgame)
            {
                return KingEndgamePst[row, col];
            }

            return pieceType switch
            {
                "Pawn" => PawnPst[row, col],
                "Knight" => KnightPst[row, col],
                "Bishop" => BishopPst[row, col],
                "Rook" => RookPst[row, col],
                "Queen" => QueenPst[row, col],
                "King" => KingPst[row, col],
                _ => 0
            };
        }
    }
}