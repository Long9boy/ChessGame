using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChessGame_PJ.Core.AI
{
    public enum MoveClassification
    {
        None,        // Không có gì nổi bật - không đánh giá
        Brilliant,   // 💎 Xuất sắc / Nước cờ thần thánh (!!)
        Best,        // ⭐ Nước đi tốt (★)
        Excellent,   // ✨ Tuyệt vời (!)
        Good,        // 🟢 Nước đi tốt (✓)
        Inaccuracy,  // 🟡 Chưa chính xác (?!)
        Mistake,     // 🟠 Sai lầm (?)
        Blunder,     // 🔴 Sai lầm nghiêm trọng (??)
        Book         // 📖 Nước đi sách
    }

    public class PositionEvaluation
    {
        /// <summary>
        /// Điểm số thế trận quy đổi về phía quân Trắng (+ = Trắng ưu thế, - = Đen ưu thế, tính theo Pawn).
        /// Ví dụ: +1.5 là Trắng dẫn 1.5 tốt, -2.0 là Đen dẫn 2 tốt.
        /// </summary>
        public double WhiteScore { get; set; } = 0.0;

        /// <summary>
        /// Số nước chiếu hết từ góc nhìn quân Trắng (+N = Trắng chiếu hết trong N nước, -N = Đen chiếu hết trong N nước).
        /// </summary>
        public int? MateIn { get; set; }

        /// <summary>
        /// Nước đi tối ưu nhất theo dạng UCI (ví dụ: "e2e4", "g1f3", "e7e8q").
        /// </summary>
        public string BestMoveUci { get; set; } = "";

        /// <summary>
        /// Tọa độ nước đi tối ưu (fromRow, fromCol, toRow, toCol, promo).
        /// </summary>
        public (int fromRow, int fromCol, int toRow, int toCol, string? promo) BestMoveCoords { get; set; }

        /// <summary>
        /// Ký hiệu nước đi thân thiện (ví dụ: "♘f3", "e4", "O-O").
        /// </summary>
        public string BestMoveSan { get; set; } = "";

        /// <summary>
        /// Độ sâu tìm kiếm (depth) mà Stockfish đã tính toán.
        /// </summary>
        public int Depth { get; set; } = 0;

        /// <summary>
        /// Lượt đi ở vị trí này ("white" hoặc "black").
        /// </summary>
        public string TurnColor { get; set; } = "white";

        /// <summary>
        /// Chuỗi hiển thị điểm gọn (ví dụ: "+1.2", "-0.8", "0.0", "M2", "-M3").
        /// </summary>
        public string FormattedScore => FormatScore(WhiteScore, MateIn);

        /// <summary>
        /// Tỷ lệ phần trăm thắng thế của quân Trắng (0% - 100%), dùng để vẽ thanh Eval Bar.
        /// 50% = Cân bằng tuyệt đối.
        /// </summary>
        public double WhiteWinPercent => ScoreToWinPercent(WhiteScore, MateIn);

        public static string FormatScore(double score, int? mateIn)
        {
            if (mateIn.HasValue)
            {
                if (mateIn.Value == 0)
                {
                    return score >= 0 ? "M0" : "-M0";
                }
                return mateIn.Value > 0 ? $"M{mateIn.Value}" : $"-M{-mateIn.Value}";
            }
            if (Math.Abs(score) < 0.05) return "0.0";
            return score > 0 ? $"+{score:F1}" : $"{score:F1}";
        }

        public static double ScoreToWinPercent(double score, int? mateIn)
        {
            if (mateIn.HasValue)
            {
                if (mateIn.Value == 0) return score >= 0 ? 100.0 : 0.0;
                return mateIn.Value > 0 ? 100.0 : 0.0;
            }
            // Áp dụng hàm Sigmoid tiêu chuẩn tương tự Lichess / Chess.com (cp = pawn * 100)
            double cp = Math.Clamp(score * 100.0, -1500.0, 1500.0);
            double winProb = 1.0 / (1.0 + Math.Exp(-0.004 * cp));
            return Math.Clamp(winProb * 100.0, 1.0, 99.0);
        }
    }

    public class MoveAssessment
    {
        public MoveClassification Classification { get; set; } = MoveClassification.Good;
        public string Badge { get; set; } = "✓";
        public string Title { get; set; } = "Tốt";
        public string Description { get; set; } = "";
        public string Commentary { get; set; } = "";
        public System.Collections.Generic.List<string> VoiceClips { get; set; } = new();
        public string ColorHex { get; set; } = "#A5D6A7";
        public string? BestAlternative { get; set; }
        public string? BestAlternativeReason { get; set; }
        public (int fromRow, int fromCol, int toRow, int toCol, string? promo)? BestAlternativeCoords { get; set; }
        public double CentipawnLoss { get; set; }
        public PositionEvaluation? NewPositionEval { get; set; }
    }

    public class StockfishEvaluator : IDisposable
    {
        private Process? process;
        private StreamWriter? inputWriter;
        private StreamReader? outputReader;
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
        private bool isReady = false;
        private bool isDisposed = false;

        public bool IsAvailable => EnsureStarted();

        private bool EnsureStarted()
        {
            if (isDisposed) return false;
            if (process != null && !process.HasExited && isReady) return true;

            string? exePath = StockfishBot.FindStockfishPath();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                process = new Process { StartInfo = psi };
                process.Start();

                inputWriter = process.StandardInput;
                outputReader = process.StandardOutput;

                SendCommand("uci");
                string? line;
                while ((line = outputReader.ReadLine()) != null)
                {
                    if (line.Trim() == "uciok") break;
                }

                // Cấu hình tối ưu cho đánh giá nhanh: 2 luồng, hash 32MB
                SendCommand("setoption name Threads value 2");
                SendCommand("setoption name Hash value 32");

                SendCommand("isready");
                while ((line = outputReader.ReadLine()) != null)
                {
                    if (line.Trim() == "readyok") break;
                }

                isReady = true;
                return true;
            }
            catch
            {
                Dispose();
                return false;
            }
        }

        private void SendCommand(string cmd)
        {
            if (inputWriter != null)
            {
                inputWriter.WriteLine(cmd);
                inputWriter.Flush();
            }
        }

        /// <summary>
        /// Đánh giá một thế trận FEN bằng Stockfish với movetime xác định (mặc định 300ms).
        /// </summary>
        public async Task<PositionEvaluation?> EvaluatePositionAsync(
            string fen,
            string turnColor,
            ChessGame? gameContext = null,
            int moveTimeMs = 300,
            CancellationToken ct = default)
        {
            if (!EnsureStarted()) return null;

            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (outputReader == null || inputWriter == null || isDisposed) return null;

                SendCommand($"position fen {fen}");
                SendCommand($"go movetime {moveTimeMs}");

                int lastDepth = 0;
                int? rawScoreCp = null;
                int? rawMateMoves = null;
                string? bestMoveUci = null;

                while (true)
                {
                    if (ct.IsCancellationRequested)
                    {
                        await DrainEngineAsync().ConfigureAwait(false);
                        return null;
                    }

                    string? line = await outputReader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line == null) break;

                    line = line.Trim();
                    if (line.StartsWith("info ") && line.Contains("score "))
                    {
                        ParseInfoLine(line, ref lastDepth, ref rawScoreCp, ref rawMateMoves, ref bestMoveUci);
                    }
                    else if (line.StartsWith("bestmove "))
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && parts[1] != "(none)" && parts[1] != "null")
                        {
                            bestMoveUci = parts[1];
                        }
                        break;
                    }
                }

                if (string.IsNullOrEmpty(bestMoveUci) && !rawScoreCp.HasValue && !rawMateMoves.HasValue)
                {
                    // Nếu Stockfish trả về (none) do thế cờ đã bị chiếu hết, tự động phát hiện bằng gameContext hoặc FEN
                    var checkGame = gameContext ?? FenHelper.CreateGameFromFen(fen);
                    if (checkGame != null && checkGame.IsCheckmate(turnColor))
                    {
                        bool isMatedWhite = string.Equals(turnColor, "white", StringComparison.OrdinalIgnoreCase);
                        return new PositionEvaluation
                        {
                            WhiteScore = isMatedWhite ? -100.0 : 100.0,
                            MateIn = 0,
                            TurnColor = turnColor,
                            Depth = Math.Max(lastDepth, 1)
                        };
                    }
                    return null;
                }

                // Chuyển đổi điểm số về góc nhìn quân Trắng:
                // Trong UCI, điểm được tính từ góc nhìn của bên đang đi (turnColor) trong FEN
                bool isWhiteTurn = string.Equals(turnColor, "white", StringComparison.OrdinalIgnoreCase);
                double whiteScore = 0.0;
                int? whiteMate = null;

                if (rawMateMoves.HasValue)
                {
                    if (rawMateMoves.Value == 0)
                    {
                        whiteMate = 0;
                        whiteScore = isWhiteTurn ? -100.0 : 100.0;
                    }
                    else
                    {
                        whiteMate = isWhiteTurn ? rawMateMoves.Value : -rawMateMoves.Value;
                        whiteScore = whiteMate.Value > 0 ? 100.0 : -100.0;
                    }
                }
                else if (rawScoreCp.HasValue)
                {
                    int scoreCp = isWhiteTurn ? rawScoreCp.Value : -rawScoreCp.Value;
                    whiteScore = scoreCp / 100.0;
                }

                var coords = FenHelper.ParseUciMove(bestMoveUci ?? "");
                string bestSan = FormatFriendlySan(coords, gameContext);

                return new PositionEvaluation
                {
                    WhiteScore = whiteScore,
                    MateIn = whiteMate,
                    BestMoveUci = bestMoveUci ?? "",
                    BestMoveCoords = coords,
                    BestMoveSan = bestSan,
                    Depth = lastDepth,
                    TurnColor = turnColor
                };
            }
            catch (OperationCanceledException)
            {
                await DrainEngineAsync().ConfigureAwait(false);
                return null;
            }
            catch
            {
                await DrainEngineAsync().ConfigureAwait(false);
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        }

        private async Task DrainEngineAsync(int timeoutMs = 400)
        {
            if (outputReader == null || inputWriter == null || isDisposed) return;
            try
            {
                SendCommand("stop");
                SendCommand("isready");

                using var cts = new CancellationTokenSource(timeoutMs);
                while (!cts.Token.IsCancellationRequested)
                {
                    string? line = await outputReader.ReadLineAsync(cts.Token).ConfigureAwait(false);
                    if (line == null) break;
                    if (line.Trim() == "readyok") break;
                }
            }
            catch { }
        }

        private static void ParseInfoLine(string line, ref int depth, ref int? scoreCp, ref int? mateMoves, ref string? bestMoveUci)
        {
            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i] == "depth" && i + 1 < tokens.Length && int.TryParse(tokens[i + 1], out int d))
                {
                    depth = d;
                }
                else if (tokens[i] == "score" && i + 2 < tokens.Length)
                {
                    if (tokens[i + 1] == "cp" && int.TryParse(tokens[i + 2], out int cp))
                    {
                        scoreCp = cp;
                        mateMoves = null;
                    }
                    else if (tokens[i + 1] == "mate" && int.TryParse(tokens[i + 2], out int mate))
                    {
                        mateMoves = mate;
                        scoreCp = null;
                    }
                }
                else if (tokens[i] == "pv" && i + 1 < tokens.Length)
                {
                    bestMoveUci = tokens[i + 1];
                }
            }
        }

        /// <summary>
        /// Phân loại chất lượng nước đi vừa thực hiện và đưa ra bình luận chiến thuật/thế trận chi tiết của Stockfish.
        /// </summary>
        public static MoveAssessment ClassifyMove(
            PositionEvaluation? beforeEval,
            PositionEvaluation? afterEval,
            string movingColor,
            string playedMoveUci,
            string movingPiece,
            int fromRow = -1,
            int fromCol = -1,
            int toRow = -1,
            int toCol = -1,
            string? promoPiece = null,
            bool isCapture = false,
            bool isCheck = false,
            bool isCheckmate = false,
            string? capturedPiece = null,
            ChessGame? gameContext = null,
            bool isCustomMode = true,
            bool? isMyMove = null)
        {
            if ((fromRow < 0 || toRow < 0) && !string.IsNullOrEmpty(playedMoveUci))
            {
                var parsed = FenHelper.ParseUciMove(playedMoveUci);
                fromRow = parsed.fromRow;
                fromCol = parsed.fromCol;
                toRow = parsed.toRow;
                toCol = parsed.toCol;
                if (string.IsNullOrEmpty(promoPiece)) promoPiece = parsed.promotion;
            }

            if (string.IsNullOrEmpty(movingPiece) && gameContext != null && toRow >= 0 && toCol >= 0)
            {
                movingPiece = gameContext.GetBoardState()[toRow, toCol];
            }

            string pieceType = !string.IsNullOrEmpty(movingPiece) ? ChessGame.PieceType(movingPiece) : "Pawn";

            string oppColor = ChessGame.Opponent(movingColor);
            if (!isCheckmate && gameContext != null)
            {
                isCheckmate = gameContext.IsCheckmate(oppColor);
            }
            if (!isCheck && gameContext != null)
            {
                isCheck = isCheckmate || gameContext.IsInCheck(oppColor, gameContext.GetBoardState());
            }
            if (isCheckmate)
            {
                isCheck = true;
            }

            bool isWhite = string.Equals(movingColor, "white", StringComparison.OrdinalIgnoreCase);

            if (isCheckmate && afterEval == null)
            {
                afterEval = new PositionEvaluation
                {
                    WhiteScore = isWhite ? 100.0 : -100.0,
                    MateIn = 0,
                    TurnColor = oppColor,
                    Depth = beforeEval?.Depth ?? 1
                };
            }

            double advBefore = 0;
            double advAfter = 0;
            if (beforeEval != null)
            {
                advBefore = isWhite ? beforeEval.WhiteScore : -beforeEval.WhiteScore;
            }
            else
            {
                advBefore = isWhite ? 0.2 : -0.2;
            }
            if (afterEval != null) advAfter = isWhite ? afterEval.WhiteScore : -afterEval.WhiteScore;

            bool hadWinningMate = beforeEval?.MateIn.HasValue == true && (isWhite ? beforeEval.MateIn > 0 : beforeEval.MateIn < 0);
            bool stillHasWinningMate = isCheckmate || (afterEval?.MateIn.HasValue == true && (isWhite ? afterEval.MateIn > 0 : afterEval.MateIn < 0));
            bool opponentNowHasMate = !isCheckmate && afterEval?.MateIn.HasValue == true && (isWhite ? afterEval.MateIn < 0 : afterEval.MateIn > 0);
            bool opponentHadMateBefore = beforeEval?.MateIn.HasValue == true && (isWhite ? beforeEval.MateIn < 0 : beforeEval.MateIn > 0);

            double lossCp;
            if (isCheckmate || stillHasWinningMate)
            {
                lossCp = 0;
            }
            else if (hadWinningMate && !stillHasWinningMate)
            {
                lossCp = 800;
            }
            else if (opponentNowHasMate && !opponentHadMateBefore)
            {
                lossCp = 1000;
            }
            else if (afterEval != null)
            {
                double lossPawns = Math.Max(0.0, advBefore - advAfter);
                lossCp = lossPawns * 100.0;
            }
            else
            {
                lossCp = 0;
            }

            bool isTopMove = beforeEval != null && !string.IsNullOrEmpty(beforeEval.BestMoveUci) &&
                             string.Equals(playedMoveUci, beforeEval.BestMoveUci, StringComparison.OrdinalIgnoreCase);

            string? altMove = (!isTopMove && beforeEval != null && !string.IsNullOrEmpty(beforeEval.BestMoveSan))
                ? beforeEval.BestMoveSan
                : null;

            int movingVal = PieceValue(pieceType);
            int capVal = (!string.IsNullOrEmpty(capturedPiece)) ? PieceValue(capturedPiece) : 0;
            bool isMaterialDeficit = !isCapture || (movingVal > capVal);

            bool isSacrifice = false;
            if (isTopMove && isMaterialDeficit && movingVal >= 3)
            {
                if ((advAfter >= 1.5 || stillHasWinningMate) && toRow >= 0 && toCol >= 0 && gameContext != null)
                {
                    if (gameContext.IsSquareAttacked(toRow, toCol, oppColor, gameContext.GetBoardState()))
                    {
                        isSacrifice = true;
                    }
                }
            }

            MoveClassification classification;
            string badge;
            string title;
            string colorHex;

            // Kiểm tra tình huống bỏ lỡ chiếu hết trong 1 hoặc 2 nước
            bool hadWinningMateIn1or2 = beforeEval?.MateIn.HasValue == true && (isWhite ? (beforeEval.MateIn > 0 && beforeEval.MateIn <= 2) : (beforeEval.MateIn < 0 && beforeEval.MateIn >= -2));
            bool isMissedMate = false;
            if (hadWinningMateIn1or2 && !isCheckmate)
            {
                int absBeforeMate = Math.Abs(beforeEval!.MateIn!.Value);
                if (absBeforeMate == 1)
                {
                    // Đang có nước chiếu hết trong 1 nước nhưng đi nước khác không chiếu hết -> Missed mate!
                    isMissedMate = true;
                }
                else if (absBeforeMate == 2)
                {
                    // Đang có nước chiếu hết trong 2 nước, sau nước đi phải là Mate in 1 về phía mình mới đúng chuẩn
                    bool stillMateIn1 = afterEval?.MateIn.HasValue == true && (isWhite ? afterEval.MateIn.Value == 1 : afterEval.MateIn.Value == -1);
                    if (!stillMateIn1)
                    {
                        isMissedMate = true;
                    }
                }
            }

            if (isCheckmate)
            {
                classification = MoveClassification.Best;
                badge = "★";
                title = "Chiếu hết!";
                colorHex = "#00E676";
                lossCp = 0;
                altMove = null;
            }
            else if (isMissedMate)
            {
                classification = MoveClassification.Blunder;
                badge = "??";
                title = "Bỏ lỡ chiếu hết!";
                colorHex = "#FF3D00";
                lossCp = Math.Max(lossCp, 800);
            }
            else if (isSacrifice)
            {
                classification = MoveClassification.Brilliant;
                badge = "!!";
                title = "Nước đi thiên tài";
                colorHex = "#00E5FF";
                altMove = null;
            }
            else if (isTopMove || lossCp <= 5)
            {
                classification = MoveClassification.Best;
                badge = "★";
                title = "Nước đi tốt";
                colorHex = "#00E676";
                altMove = null;
            }
            else if (lossCp > 300)
            {
                classification = MoveClassification.Blunder;
                badge = "??";
                title = "Sai lầm ngớ ngẩn";
                colorHex = "#FF3D00";
            }
            else if (lossCp > 150)
            {
                classification = MoveClassification.Mistake;
                badge = "?";
                title = "Hơi ngáo ngơ";
                colorHex = "#FF9800";
            }
            else if (lossCp >= 40)
            {
                classification = MoveClassification.Inaccuracy;
                badge = "?!";
                title = "Hơi non tay";
                colorHex = "#FFD54F";
            }
            else
            {
                // Không có gì nổi bật — không đánh giá theo yêu cầu người dùng
                classification = MoveClassification.None;
                badge = "";
                title = "";
                colorHex = "#00000000";
                altMove = null;

                // Ngoại lệ: Cảnh báo xuất Hậu sớm vi phạm nguyên tắc khai cuộc
                var detected = DetectOpening(gameContext, movingColor, pieceType, fromRow, fromCol, toRow, toCol, isCapture);
                if (detected.HasValue && detected.Value.clipKey == "open_queen_early_warn")
                {
                    classification = MoveClassification.Inaccuracy;
                    badge = "?!";
                    title = "Hơi non tay";
                    colorHex = "#FFD54F";
                }
            }

            // Xử lý quy tắc lọc bình luận theo chế độ chơi:
            // Custom mode: bình luận cho cả 2 bên.
            // Các chế độ khác:
            // - Vẫn giữ nguyên icon / badge đánh giá nước đi cho cả 2 bên.
            // - Bình luận cả nước đi thiên tài (Brilliant) của đối thủ.
            // - Bình luận nước đi sai lầm (Blunder / Mistake) của đối thủ.
            // - Đối với nước đi bình thường / tốt / hơi non tay của đối thủ: KHÔNG bình luận (chỉ gắn icon).
            bool isOpponent = isMyMove.HasValue && !isMyMove.Value;
            bool shouldComment = classification != MoveClassification.None;
            bool isOpponentGift = false;
            string giftedPieceName = "";
            string giftedPieceType = "";
            bool isOpponentBlunder = false;

            if (!isCustomMode && isOpponent)
            {
                // Đây là nước đi của đối thủ trong chế độ không phải Custom:
                if (classification == MoveClassification.Brilliant)
                {
                    // Nước đi thiên tài của đối thủ -> Vẫn bình luận!
                    shouldComment = true;
                    title = "Nước đi thiên tài của đối thủ";
                }
                else if (classification == MoveClassification.Blunder || classification == MoveClassification.Mistake)
                {
                    shouldComment = true;

                    // Đối thủ phạm sai lầm: kiểm tra xem có phải hiến tặng quân cờ không
                    string playerColor = ChessGame.Opponent(movingColor);
                    bool pieceHanging = false;

                    if (gameContext != null && toRow >= 0 && toCol >= 0)
                    {
                        var boardState = gameContext.GetBoardState();
                        bool isAttackedByPlayer = gameContext.IsSquareAttacked(toRow, toCol, playerColor, boardState);
                        bool isDefendedByOpponent = gameContext.IsSquareAttacked(toRow, toCol, movingColor, boardState);

                        if (isAttackedByPlayer && movingVal >= 1)
                        {
                            if (!isDefendedByOpponent || lossCp >= 150)
                            {
                                pieceHanging = true;
                            }
                        }
                    }

                    bool playerCapturesTarget = afterEval != null && (
                        (afterEval.BestMoveCoords.toRow == toRow && afterEval.BestMoveCoords.toCol == toCol) ||
                        (afterEval.BestMoveSan.Contains("x") && pieceHanging)
                    );

                    if (pieceHanging || playerCapturesTarget)
                    {
                        isOpponentGift = true;
                        giftedPieceName = PieceNameVi(pieceType);
                        giftedPieceType = pieceType;
                        title = "Đối thủ dâng quân!";
                        badge = "??";
                        colorHex = "#FF3D00";
                    }
                    else
                    {
                        isOpponentBlunder = true;
                        title = "Đối thủ vừa thực hiện một nước đi ngớ ngẩn!";
                        badge = "??";
                        colorHex = "#FF3D00";
                    }
                }
                else
                {
                    // Nước đi bình thường, nước đi tốt, hoặc hơi non tay của đối thủ:
                    // VẪN GIỮ NGUYÊN classification, badge, title, colorHex để gắn icon đánh giá nước đi!
                    // Nhưng KHÔNG bình luận (không text, không voice).
                    shouldComment = false;
                    altMove = null;
                    if (classification == MoveClassification.Best)
                        title = isCheckmate ? "Đối thủ chiếu hết!" : "Nước đi tốt của đối thủ";
                    else if (classification == MoveClassification.Excellent)
                        title = "Nước cờ quá mượt của đối thủ";
                    else if (classification == MoveClassification.Good)
                        title = "Nước đi tạm ổn của đối thủ";
                    else if (classification == MoveClassification.Inaccuracy)
                        title = "Đối thủ hơi non tay";
                }
            }
            else if (isCustomMode && isOpponent)
            {
                // Ở chế độ Custom, thêm chữ "đối thủ" vào tiêu đề nếu là nước đi của đối thủ:
                if (classification == MoveClassification.Brilliant)
                {
                    title = "Nước đi thiên tài của đối thủ";
                }
                else if (classification == MoveClassification.Best)
                {
                    title = isCheckmate ? "Đối thủ chiếu hết!" : "Nước đi tốt của đối thủ";
                }
                else if (classification == MoveClassification.Excellent)
                {
                    title = "Nước cờ quá mượt của đối thủ";
                }
                else if (classification == MoveClassification.Good)
                {
                    title = "Nước đi tạm ổn của đối thủ";
                }
                else if (classification == MoveClassification.Inaccuracy)
                {
                    title = "Đối thủ hơi non tay";
                }
                else if (classification == MoveClassification.Mistake || classification == MoveClassification.Blunder)
                {
                    string playerColor = ChessGame.Opponent(movingColor);
                    bool pieceHanging = false;

                    if (gameContext != null && toRow >= 0 && toCol >= 0)
                    {
                        var boardState = gameContext.GetBoardState();
                        bool isAttackedByPlayer = gameContext.IsSquareAttacked(toRow, toCol, playerColor, boardState);
                        bool isDefendedByOpponent = gameContext.IsSquareAttacked(toRow, toCol, movingColor, boardState);

                        if (isAttackedByPlayer && movingVal >= 1)
                        {
                            if (!isDefendedByOpponent || lossCp >= 150)
                            {
                                pieceHanging = true;
                            }
                        }
                    }

                    bool playerCapturesTarget = afterEval != null && (
                        (afterEval.BestMoveCoords.toRow == toRow && afterEval.BestMoveCoords.toCol == toCol) ||
                        (afterEval.BestMoveSan.Contains("x") && pieceHanging)
                    );

                    if (pieceHanging || playerCapturesTarget)
                    {
                        isOpponentGift = true;
                        giftedPieceName = PieceNameVi(pieceType);
                        giftedPieceType = pieceType;
                        title = "Đối thủ dâng quân!";
                    }
                    else
                    {
                        isOpponentBlunder = true;
                        title = "Đối thủ vừa thực hiện một nước đi ngớ ngẩn!";
                    }
                }
            }

            string actionDesc = GenerateMoveActionDescription(
                pieceType,
                fromRow, fromCol,
                toRow, toCol,
                promoPiece,
                isCapture,
                isCheck,
                isCheckmate,
                capturedPiece,
                movingColor,
                gameContext);

            string altReason = (shouldComment && !string.IsNullOrEmpty(altMove))
                ? GenerateAlternativeReason(altMove, beforeEval?.BestMoveCoords ?? default, gameContext)
                : "";

            var rng = new Random(Environment.TickCount);
            int variantIdx = rng.Next(10);

            string fullCommentary = shouldComment
                ? GenerateFullCommentary(
                    classification,
                    lossCp,
                    actionDesc,
                    altMove,
                    altReason,
                    afterEval,
                    movingColor,
                    isCheckmate,
                    isMissedMate,
                    isOpponentGift,
                    giftedPieceName,
                    isOpponentBlunder,
                    variantIdx,
                    isOpponent)
                : "";

            var voiceClips = shouldComment
                ? BuildVoiceClipSequence(
                    classification,
                    pieceType,
                    fromRow, fromCol,
                    toRow, toCol,
                    promoPiece,
                    isCapture,
                    isCheck,
                    isCheckmate,
                    capturedPiece,
                    movingColor,
                    gameContext,
                    altMove,
                    altReason,
                    beforeEval?.BestMoveCoords ?? default,
                    afterEval,
                    variantIdx,
                    isMissedMate,
                    isOpponentGift,
                    giftedPieceType,
                    isOpponentBlunder,
                    isOpponent)
                : new List<string>();

            return new MoveAssessment
            {
                Classification = classification,
                Badge = badge,
                Title = title,
                Description = actionDesc,
                Commentary = fullCommentary,
                VoiceClips = voiceClips,
                ColorHex = colorHex,
                CentipawnLoss = lossCp,
                BestAlternative = altMove,
                BestAlternativeReason = altReason,
                BestAlternativeCoords = beforeEval?.BestMoveCoords,
                NewPositionEval = afterEval
            };
        }

        public static string PieceNameVi(string? pieceType)
        {
            if (string.IsNullOrWhiteSpace(pieceType)) return "Quân";
            string clean = pieceType.Trim();
            if (clean.StartsWith("W_") || clean.StartsWith("B_"))
            {
                clean = clean.Substring(2);
            }
            return clean.ToLowerInvariant() switch
            {
                "pawn" or "p" => "Tốt",
                "knight" or "n" => "Mã",
                "bishop" or "b" => "Tượng",
                "rook" or "r" => "Xe",
                "queen" or "q" => "Hậu",
                "king" or "k" => "Vua",
                _ => "Quân"
            };
        }

        public static int PieceValue(string? pieceType)
        {
            if (string.IsNullOrWhiteSpace(pieceType)) return 0;
            string clean = pieceType.Trim();
            if (clean.StartsWith("W_") || clean.StartsWith("B_"))
            {
                clean = clean.Substring(2);
            }
            return clean.ToLowerInvariant() switch
            {
                "pawn" or "p" => 1,
                "knight" or "n" => 3,
                "bishop" or "b" => 3,
                "rook" or "r" => 5,
                "queen" or "q" => 9,
                _ => 0
            };
        }

        public static string GetClassificationTitleVi(MoveClassification classification) => classification switch
        {
            MoveClassification.None => "",
            MoveClassification.Brilliant => "Nước đi thiên tài",
            MoveClassification.Best => "Nước đi tốt",
            MoveClassification.Excellent => "Quá mượt",
            MoveClassification.Good => "Tạm ổn",
            MoveClassification.Inaccuracy => "Hơi non tay",
            MoveClassification.Mistake => "Hơi ngáo ngơ",
            MoveClassification.Blunder => "Sai lầm ngớ ngẩn",
            MoveClassification.Book => "Bài tủ",
            _ => ""
        };

        private static bool IsPassedPawn(int row, int col, string movingColor, ChessGame? game)
        {
            if (game == null || row < 0 || row >= 8 || col < 0 || col >= 8) return false;
            var board = game.GetBoardState();
            string oppPawn = movingColor == "white" ? "B_Pawn" : "W_Pawn";

            int startR = movingColor == "white" ? 0 : row + 1;
            int endR = movingColor == "white" ? row - 1 : 7;

            for (int r = startR; r <= endR; r++)
            {
                for (int c = Math.Max(0, col - 1); c <= Math.Min(7, col + 1); c++)
                {
                    if (board[r, c] == oppPawn) return false;
                }
            }
            return true;
        }

        public static string SquareName(int row, int col)
        {
            if (row < 0 || row >= 8 || col < 0 || col >= 8) return "";
            char file = (char)('a' + col);
            int rank = 8 - row;
            return $"{file}{rank}";
        }

        private static int CountValuableOpponentPiecesAttacked(int row, int col, string pieceType, string movingColor, ChessGame game)
        {
            if (game == null || row < 0 || row >= 8 || col < 0 || col >= 8) return 0;
            var board = game.GetBoardState();
            string oppColor = ChessGame.Opponent(movingColor);
            int count = 0;

            if (pieceType == "Knight")
            {
                int[] dr = { -2, -2, -1, -1, 1, 1, 2, 2 };
                int[] dc = { -1, 1, -2, 2, -2, 2, -1, 1 };
                for (int i = 0; i < 8; i++)
                {
                    int r = row + dr[i];
                    int c = col + dc[i];
                    if (r >= 0 && r < 8 && c >= 0 && c < 8)
                    {
                        string target = board[r, c];
                        if (!string.IsNullOrEmpty(target) && ChessGame.ColorOf(target) == oppColor)
                        {
                            string tType = ChessGame.PieceType(target);
                            if (tType == "Queen" || tType == "Rook" || tType == "King" || tType == "Bishop" || tType == "Knight")
                            {
                                count++;
                            }
                        }
                    }
                }
            }
            else if (pieceType == "Pawn")
            {
                int dir = movingColor == "white" ? -1 : 1;
                int r = row + dir;
                int[] cols = { col - 1, col + 1 };
                foreach (int c in cols)
                {
                    if (r >= 0 && r < 8 && c >= 0 && c < 8)
                    {
                        string target = board[r, c];
                        if (!string.IsNullOrEmpty(target) && ChessGame.ColorOf(target) == oppColor)
                        {
                            string tType = ChessGame.PieceType(target);
                            if (tType == "Queen" || tType == "Rook" || tType == "Bishop" || tType == "Knight")
                            {
                                count++;
                            }
                        }
                    }
                }
            }

            return count;
        }

        public static string GenerateAlternativeReason(
            string? bestAltSan,
            (int fromRow, int fromCol, int toRow, int toCol, string? promo) coords,
            ChessGame? gameContext)
        {
            if (string.IsNullOrEmpty(bestAltSan)) return "";

            if (bestAltSan == "O-O" || bestAltSan == "O-O-O")
                return "nhập thành bảo vệ Vua và kết nối Xe";
            if (bestAltSan.Contains("+"))
                return "chiếu Vua đối phương giành thế chủ động";
            if (bestAltSan.Contains("x"))
                return "bắt quân đối phương giành lợi thế chất";
            if (bestAltSan.StartsWith("♘") || bestAltSan.StartsWith("N"))
                return "phát triển Mã kiểm soát trung tâm";
            if (bestAltSan.StartsWith("♗") || bestAltSan.StartsWith("B"))
                return "phát triển Tượng mở đường tấn công";
            if (bestAltSan.StartsWith("♖") || bestAltSan.StartsWith("R"))
                return "đưa Xe kiểm soát cột mở";
            if (bestAltSan.StartsWith("♕") || bestAltSan.StartsWith("Q"))
                return "kích hoạt Hậu tăng sức ép";
            if (bestAltSan.StartsWith("♔") || bestAltSan.StartsWith("K"))
                return "đưa Vua vào vị trí an toàn";

            return "kiểm soát trung tâm và củng cố thế trận";
        }

        public static (string openingName, string desc, string? clipKey)? DetectOpening(
            ChessGame? game,
            string movingColor,
            string pieceType,
            int fromRow, int fromCol,
            int toRow, int toCol,
            bool isCapture)
        {
            string cleanType = pieceType;
            if (cleanType.StartsWith("W_") || cleanType.StartsWith("B_"))
                cleanType = cleanType.Substring(2);

            bool isWhite = string.Equals(movingColor, "white", StringComparison.OrdinalIgnoreCase);
            var board = game?.GetBoardState();

            if (isWhite)
            {
                // Move 1 của Trắng
                if (cleanType == "Pawn" && fromRow == 6)
                {
                    if (fromCol == 4 && toRow == 4 && toCol == 4) // e4
                        return ("Khai cuộc Tốt Vua", "Mở đầu với nước Tốt e4 (Khai cuộc Tốt Vua) - chiếm lĩnh trung tâm và mở đường cho Tượng và Hậu xuất trận.", "open_e4");
                    if (fromCol == 3 && toRow == 4 && toCol == 3) // d4
                        return ("Khai cuộc Tốt Hậu", "Mở đầu với nước Tốt d4 (Khai cuộc Tốt Hậu) - kiểm soát chặt chẽ trung tâm và mở đường cho Tượng ô đen.", "open_d4");
                    if (fromCol == 2 && toRow == 4 && toCol == 2) // c4
                    {
                        if (board != null && board[4, 3] == "W_Pawn")
                            return ("Khai cuộc Gambit Hậu", "Bạn triển khai Khai cuộc Gambit Hậu - chủ động thí tốt c4 để kiểm soát trung tâm.", "open_queens_gambit");
                        return ("Khai cuộc Anh", "Khai cuộc Anh (1. c4) - kiểm soát ô d5 từ cánh và xây dựng thế trận linh hoạt.", "open_c4");
                    }
                    if (fromCol == 1 && toRow == 5 && toCol == 1) // b3
                        return ("Khai cuộc Nimzo-Larsen", "Khai cuộc Nimzo-Larsen (1. b3) - chuẩn bị đưa Tượng lên b2 làm chủ đường chéo dài.", null);
                    if (fromCol == 5 && toRow == 4 && toCol == 5) // f4
                        return ("Khai cuộc Bird", "Khai cuộc Bird (1. f4) - đòn đẩy tốt bất ngờ nhắm thẳng vào quyền kiểm soát ô e5.", null);
                }

                if (cleanType == "Knight" && fromRow == 7 && fromCol == 6 && toRow == 5 && toCol == 5) // Nf3
                {
                    if (board == null || (board[6, 4] == "W_Pawn" && board[6, 3] == "W_Pawn"))
                        return ("Khai cuộc Réti", "Khai cuộc Réti (1. Nf3) - phát triển Mã kiểm soát trung tâm mà không vội vàng đẩy tốt.", "open_nf3");
                }

                // Các nước đi khai cuộc tiếp theo của Trắng khi có bàn cờ
                if (board != null)
                {
                    bool whiteHasE4 = board[4, 4] == "W_Pawn" || (cleanType == "Pawn" && toRow == 4 && toCol == 4);
                    bool blackHasE5 = board[3, 4] == "B_Pawn";

                    if (whiteHasE4 && blackHasE5)
                    {
                        // 1. e4 e5 2. f4 (Gambit Vua)
                        if (cleanType == "Pawn" && fromRow == 6 && fromCol == 5 && toRow == 4 && toCol == 5)
                            return ("Khai cuộc Gambit Vua", "Bạn triển khai Khai cuộc Gambit Vua - phong cách khai cuộc lãng mạn đầy máu lửa!", "open_kings_gambit");

                        // 1. e4 e5 2. Nf3 Nc6 3. Bb5 (Ruy Lopez)
                        if (cleanType == "Bishop" && toRow == 3 && toCol == 1)
                            return ("Khai cuộc Tây Ban Nha", "Bạn lựa chọn Khai cuộc Tây Ban Nha (Ruy Lopez) - ghim Mã b5 tạo sức ép lâu dài lên trung tâm.", "open_ruy_lopez");

                        // 1. e4 e5 2. Nf3 Nc6 3. Bc4 (Italian)
                        if (cleanType == "Bishop" && toRow == 4 && toCol == 2)
                            return ("Khai cuộc Ý", "Bạn đang triển khai thế trận Khai cuộc Ý - phát triển Tượng lên c4 gây sức ép sớm lên điểm yếu f7 của đối thủ.", "open_italian");

                        // 1. e4 e5 2. Nf3 Nc6 3. d4 (Scotch)
                        if (cleanType == "Pawn" && fromRow == 6 && fromCol == 3 && toRow == 4 && toCol == 3)
                            return ("Khai cuộc Scotch", "Bạn triển khai Khai cuộc Scotch - lập tức đẩy tốt d4 công phá trung tâm.", "open_scotch");

                        // 1. e4 e5 2. Nf3 Nc6 3. Nc3 (4 Knights)
                        if (cleanType == "Knight" && toRow == 5 && toCol == 2 && board[2, 2] == "B_Knight")
                            return ("Khai cuộc 4 Mã", "Bạn lựa chọn Khai cuộc 4 Mã - thế trận cân bằng và hài hòa lực lượng.", "open_four_knights");
                    }

                    bool whiteHasD4 = board[4, 3] == "W_Pawn" || (cleanType == "Pawn" && toRow == 4 && toCol == 3);
                    bool blackHasD5 = board[3, 3] == "B_Pawn";

                    if (whiteHasD4)
                    {
                        // 1. d4 d5 2. c4 (Queen's Gambit)
                        if (blackHasD5 && cleanType == "Pawn" && fromRow == 6 && fromCol == 2 && toRow == 4 && toCol == 2)
                            return ("Khai cuộc Gambit Hậu", "Bạn triển khai Khai cuộc Gambit Hậu - chủ động thí tốt c4 để kiểm soát trung tâm.", "open_queens_gambit");

                        // 1. d4 ... 2. Bf4 / 3. Bf4 (London System)
                        if (cleanType == "Bishop" && toRow == 4 && toCol == 5)
                            return ("Khai cuộc Hệ thống London", "Bạn triển khai Khai cuộc Hệ thống London - xây dựng cấu trúc kim tự tháp cực kỳ vững chắc.", "open_london");
                    }
                }
            }
            else // Black is moving
            {
                // Move 1 của Đen
                if (cleanType == "Pawn" && fromRow == 1)
                {
                    if (fromCol == 4 && toRow == 3 && toCol == 4) // e5
                        return ("Đáp trả e5", "Đáp trả đối xứng với e5, tranh chấp quyết liệt ô trung tâm d4.", "open_e5");
                    if (fromCol == 2 && toRow == 3 && toCol == 2) // c5
                        return ("Phòng thủ Sicilian", "Khai cuộc phòng thủ Sicilian (1...c5) - phản kích mạnh mẽ vào cánh và trung tâm của Trắng!", "open_c5");
                    if (fromCol == 3 && toRow == 3 && toCol == 3) // d5
                    {
                        if (board != null && board[4, 4] == "W_Pawn")
                            return ("Phòng thủ Scandinavian", "Bạn triển khai Phòng thủ Scandinavian (1...d5) - lập tức phản kích trung tâm bằng tốt d5.", "open_scandinavian");
                        return ("Đáp trả trung tâm d5", "Đáp trả trung tâm d5, bước vào hệ thống phòng thủ vững chắc.", "open_d5");
                    }
                    if (fromCol == 4 && toRow == 2 && toCol == 4) // e6
                        return ("Phòng thủ Pháp", "Phòng thủ Pháp (1...e6) - chuẩn bị đẩy d5 thiết lập bức tường phòng thủ trung tâm.", "open_french");
                    if (fromCol == 2 && toRow == 2 && toCol == 2) // c6
                        return ("Phòng thủ Caro-Kann", "Phòng thủ Caro-Kann (1...c6) - xây dựng cấu trúc tốt trung tâm chắc chắn.", "open_caro_kann");
                    if (fromCol == 5 && toRow == 3 && toCol == 5) // f5
                        return ("Phòng thủ Hà Lan", "Bạn triển khai Phòng thủ Hà Lan (1...f5) - phản kích trực tiếp vào ô e4, tạo nên thế trận bất đối xứng.", null);
                }

                if (cleanType == "Knight" && fromRow == 0 && fromCol == 6 && toRow == 2 && toCol == 5) // Nf6
                {
                    if (board != null && board[4, 4] == "W_Pawn")
                        return ("Phòng thủ Alekhine", "Bạn chọn Phòng thủ Alekhine (1...Nf6) - khiêu khích đối phương dâng cao tốt trung tâm để sau đó phản kích.", null);
                    return ("Phòng thủ Ấn Độ", "Phát triển Mã lên f6 (Phòng thủ Ấn Độ) - kiểm soát linh hoạt các ô trung tâm e4 và d5.", "open_nf6");
                }

                // Các nước đi tiếp theo của Đen khi có bàn cờ
                if (board != null)
                {
                    bool whiteHasE4 = board[4, 4] == "W_Pawn";
                    bool blackHasE5 = board[3, 4] == "B_Pawn" || (cleanType == "Pawn" && toRow == 3 && toCol == 4);
                    bool whiteHasNf3 = board[5, 5] == "W_Knight";

                    if (whiteHasE4 && blackHasE5 && whiteHasNf3)
                    {
                        if (cleanType == "Knight" && toRow == 2 && toCol == 5) // 2...Nf6 (Petrov)
                            return ("Phòng thủ Petrov", "Bạn chọn Phòng thủ Petrov (Khai cuộc Nga) - phản kích đối xứng hóa giải sức ép của Trắng.", "open_petrov");
                    }

                    bool whiteHasD4 = board[4, 3] == "W_Pawn";
                    bool whiteHasC4 = board[4, 2] == "W_Pawn";
                    bool blackHasD5 = board[3, 3] == "B_Pawn" || (cleanType == "Pawn" && toRow == 3 && toCol == 3);

                    if (whiteHasD4 && whiteHasC4)
                    {
                        if (blackHasD5 && cleanType == "Pawn" && fromRow == 1 && fromCol == 2 && toRow == 2 && toCol == 2) // 2...c6 (Slav)
                            return ("Phòng thủ Slav", "Bạn lựa chọn Phòng thủ Slav - củng cố trung tâm bằng tốt c6 rất kiên cố.", "open_slav");

                        if (cleanType == "Pawn" && fromRow == 1 && fromCol == 6 && toRow == 2 && toCol == 6 && board[2, 5] == "B_Knight") // g6 (KID)
                            return ("Phòng thủ King's Indian", "Bạn chọn Phòng thủ King's Indian - nhường trung tâm để chuẩn bị phản công cánh Vua.", "open_kid");
                    }
                }
            }

            // Cảnh báo xuất Hậu sớm khi các quân nhẹ chưa phát triển
            if (cleanType == "Queen" && !isCapture && board != null)
            {
                int minorCount = 0;
                if (isWhite)
                {
                    if (board[7, 1] == "W_Knight") minorCount++;
                    if (board[7, 2] == "W_Bishop") minorCount++;
                    if (board[7, 5] == "W_Bishop") minorCount++;
                    if (board[7, 6] == "W_Knight") minorCount++;
                }
                else
                {
                    if (board[0, 1] == "B_Knight") minorCount++;
                    if (board[0, 2] == "B_Bishop") minorCount++;
                    if (board[0, 5] == "B_Bishop") minorCount++;
                    if (board[0, 6] == "B_Knight") minorCount++;
                }
                if (minorCount >= 3)
                {
                    return ("Xuất Hậu sớm", "Lưu ý: Xuất Hậu quá sớm khi các quân nhẹ chưa phát triển thường khiến Hậu dễ bị đối phương truy đuổi!", "open_queen_early_warn");
                }
            }

            return null;
        }

        public static string GenerateMoveActionDescription(
            string pieceType,
            int fromRow, int fromCol,
            int toRow, int toCol,
            string? promoPiece,
            bool isCapture,
            bool isCheck,
            bool isCheckmate,
            string? capturedPiece,
            string movingColor,
            ChessGame? gameContext)
        {
            string toSq = SquareName(toRow, toCol);
            string fromSq = SquareName(fromRow, fromCol);
            bool isWhite = string.Equals(movingColor, "white", StringComparison.OrdinalIgnoreCase);

            if (isCheckmate)
            {
                return $"Chiếu hết bằng {PieceNameVi(pieceType)} tại ô {toSq}! Đòn kết liễu trận đấu hoàn hảo.";
            }

            // Castling
            if (pieceType == "King" && Math.Abs(toCol - fromCol) == 2)
            {
                string checkNote = isCheck ? " đồng thời chiếu Vua đối phương!" : "";
                if (toCol > fromCol)
                    return $"Nhập thành gần (O-O) đưa Vua vào vị trí an toàn sau hàng tốt vững chắc, đồng thời kết nối Xe vào trận.{checkNote}";
                else
                    return $"Nhập thành xa (O-O-O) đưa Vua sang cánh Hậu, chuẩn bị cho đợt bão tốt tấn công cánh Vua đối phương.{checkNote}";
            }

            // Promotion
            if (!string.IsNullOrEmpty(promoPiece) || (pieceType == "Pawn" && (toRow == 0 || toRow == 7)))
            {
                string promoName = PieceNameVi(promoPiece ?? "Queen");
                string checkNote = isCheck ? " đồng thời tung đòn chiếu Vua!" : "";
                return $"Phong cấp {promoName} thành công tại ô {toSq}! Gia tăng sức mạnh áp đảo cho lực lượng.{checkNote}";
            }

            // Tactical motifs: Fork (including Royal Fork with check)
            if (gameContext != null && (pieceType == "Knight" || pieceType == "Pawn"))
            {
                int forkCount = CountValuableOpponentPiecesAttacked(toRow, toCol, pieceType, movingColor, gameContext);
                if (forkCount >= 2)
                {
                    string forkCheck = isCheck ? " đồng thời chiếu Vua đối phương!" : "!";
                    return $"Đòn bắt đôi (Fork) hiểm hóc của {PieceNameVi(pieceType)} tại {toSq}{forkCheck} Tấn công cùng lúc nhiều mục tiêu của đối thủ.";
                }
            }

            // Captures
            if (isCapture || !string.IsNullOrEmpty(capturedPiece))
            {
                string capType = !string.IsNullOrEmpty(capturedPiece) ? ChessGame.PieceType(capturedPiece) : "";
                string checkNote = isCheck ? " đồng thời chiếu Vua đối phương!" : "";

                if (capType == "Queen")
                    return $"Dùng {PieceNameVi(pieceType)} bắt Hậu đối phương tại {toSq}! Giáng đòn chí mạng đoạt lấy quân cờ mạnh nhất của đối thủ.{checkNote}";
                if (capType == "Rook")
                    return $"Dùng {PieceNameVi(pieceType)} bắt Xe đối phương tại {toSq}, mang lại ưu thế chất rất lớn.{checkNote}";
                if (capType == "Bishop" || capType == "Knight")
                {
                    if (pieceType == "Pawn")
                        return $"Dùng Tốt ăn {PieceNameVi(capType)} ở {toSq}, giành ưu thế lớn về chất!{checkNote}";
                    return $"Dùng {PieceNameVi(pieceType)} bắt {PieceNameVi(capType)} ở {toSq}, triệt hạ một quân chủ lực của đối phương.{checkNote}";
                }
                if (capType == "Pawn")
                    return $"Ăn tốt đối phương tại ô {toSq}, phá vỡ cấu trúc tốt và kiểm soát thêm không gian.{checkNote}";

                return $"Dùng {PieceNameVi(pieceType)} bắt quân đối phương tại ô {toSq}, gia tăng ưu thế quân số.{checkNote}";
            }

            // Checks (not mate, not fork, not capture)
            if (isCheck)
            {
                return $"Chiếu Vua đối phương bằng {PieceNameVi(pieceType)} từ ô {toSq}! Buộc đối thủ phải lập tức tìm cách ứng phó.";
            }

            // 0. Nhận diện Khai cuộc bài bản
            var opening = DetectOpening(gameContext, movingColor, pieceType, fromRow, fromCol, toRow, toCol, isCapture);
            if (opening.HasValue && !string.IsNullOrEmpty(opening.Value.desc))
            {
                return opening.Value.desc;
            }

            // Opening & Positional logic
            if (pieceType == "Pawn")
            {

                // Center squares
                if ((toRow == 3 || toRow == 4) && (toCol == 3 || toCol == 4))
                    return $"Tiến Tốt {toSq} chiếm lĩnh khu trung tâm trọng yếu và củng cố thế trận.";

                // Wing pawn advance
                if ((toRow == 3 || toRow == 4) && (toCol == 2 || toCol == 5))
                    return $"Đẩy Tốt {toSq} gây sức ép lên trung tâm và mở rộng không gian cánh.";

                // Passed pawn advance
                if ((isWhite && toRow <= 3) || (!isWhite && toRow >= 4))
                {
                    if (IsPassedPawn(toRow, toCol, movingColor, gameContext))
                        return $"Đẩy Tốt thông {toSq} dũng mãnh tiến sát hàng phong cấp, đe dọa trực tiếp phòng tuyến đối phương.";
                    else
                        return $"Đẩy Tốt lên {toSq} áp sát phòng tuyến, gia tăng sức ép và kiểm soát không gian.";
                }

                // Fianchetto preparation
                if (toSq == "g3" || toSq == "b3" || toSq == "g6" || toSq == "b6")
                    return $"Đẩy Tốt {toSq} dọn đường đưa Tượng vào vị trí Fianchetto khống chế đường chéo dài.";

                return $"Tiến Tốt lên {toSq} củng cố cấu trúc thế cờ.";
            }

            if (pieceType == "Knight")
            {
                if (isWhite && fromRow == 7 && fromCol == 6 && toSq == "f3")
                    return "Khai cuộc Réti (1. Nf3) - phát triển Mã kiểm soát trung tâm mà không vội vàng đẩy tốt.";
                if (!isWhite && fromRow == 0 && fromCol == 6 && toSq == "f6")
                    return "Phát triển Mã lên f6 (Phòng thủ Ấn Độ) - kiểm soát linh hoạt các ô trung tâm e4 và d5.";

                if (toSq == "f3" || toSq == "c3" || toSq == "f6" || toSq == "c6")
                    return $"Phát triển Mã lên {toSq} kiểm soát các ô trung tâm trọng yếu và sẵn sàng tham chiến.";
                if (toSq == "d4" || toSq == "e4" || toSq == "d5" || toSq == "e5")
                    return $"Đưa Mã chiếm lĩnh tiền đồn trung tâm {toSq} đầy uy lực.";
                return $"Điều động Mã đến ô {toSq} tăng cường kiểm soát thế trận.";
            }

            if (pieceType == "Bishop")
            {
                if (toSq == "b2" || toSq == "g2" || toSq == "b7" || toSq == "g7")
                    return $"Fianchetto Tượng lên {toSq}, làm chủ đường chéo dài xuyên suốt bàn cờ.";
                if (toSq == "c4" || toSq == "b5" || toSq == "f4" || toSq == "g5" || toSq == "c5" || toSq == "b4" || toSq == "f5" || toSq == "g4")
                    return $"Phát triển Tượng lên {toSq}, chĩa mũi nhọn tấn công vào cánh Vua đối phương.";
                return $"Đưa Tượng lên {toSq} chiếm giữ đường chéo mở quan trọng.";
            }

            if (pieceType == "Rook")
            {
                if ((isWhite && toRow == 1) || (!isWhite && toRow == 6))
                    return $"Đưa Xe thâm nhập hàng ngang thứ {8 - toRow} - vị trí chiến lược uy hiếp toàn bộ hàng tốt của đối thủ!";
                if (toCol == 2 || toCol == 3 || toCol == 4 || toCol == 5)
                    return $"Điều Xe kiểm soát cột {(char)('a' + toCol)}, chuẩn bị cho các đợt công phá trung tâm.";
                return $"Điều Xe đến {toSq} hỗ trợ phòng thủ và kiểm soát mặt trận.";
            }

            if (pieceType == "Queen")
            {
                return $"Hậu xuất kích đến {toSq}, gia tăng sức ép hỏa lực đáng kể lên trận địa đối phương.";
            }

            if (pieceType == "King")
            {
                return $"Di chuyển Vua đến {toSq} để né tránh nguy hiểm và củng cố an toàn.";
            }

            return $"Di chuyển {PieceNameVi(pieceType)} đến ô {toSq}.";
        }

        public static string GenerateFullCommentary(
            MoveClassification classification,
            double lossCp,
            string actionDesc,
            string? bestAltSan,
            string? altReason,
            PositionEvaluation? afterEval,
            string movingColor,
            bool isCheckmate = false,
            bool isMissedMate = false,
            bool isOpponentGift = false,
            string? giftedPieceName = null,
            bool isOpponentBlunder = false,
            int variantIdx = 0,
            bool isOpponent = false)
        {
            string commentary;
            string altText = "";

            if (isMissedMate)
            {
                if (!string.IsNullOrEmpty(bestAltSan))
                    altText = $" 💡 Stockfish: Lẽ ra nên đi {bestAltSan} ({altReason}) để kết liễu trận đấu ngay lập tức!";
                string[] missedMateLines = {
                    $"🔴 Bỏ lỡ chiếu hết! Bạn đang chấp đối thủ hay... bạn thật sự bị suy thoái tư duy vậy?{altText}",
                    $"🔴 Bỏ lỡ chiếu hết! Chiếu hết mười mươi trong tay mà bạn lại bỏ qua? Bạn đang làm từ thiện hay đầu óc đang bay lên vũ trụ thế?{altText}",
                    $"🔴 Bỏ lỡ chiếu hết! Cơ hội kết liễu trận đấu trong chớp mắt trôi qua như một trò đùa... Bạn đang thử lòng kiên nhẫn của đối thủ à?{altText}",
                    $"🔴 Bỏ lỡ chiếu hết! Thần chết đã gõ cửa đối thủ rồi mà bạn lại giật tay lại? Não bộ vừa trải qua một đợt đóng băng toàn phần sao?{altText}",
                    $"🔴 Bỏ lỡ chiếu hết! Chỉ cần một nước nữa là đối thủ lên bảng đếm số, thế mà bạn lại tha cho họ? Đánh cờ hay đang viết tiểu thuyết ngôn tình vậy?{altText}"
                };
                return missedMateLines[variantIdx % missedMateLines.Length];
            }

            if (isOpponentGift)
            {
                string pName = !string.IsNullOrEmpty(giftedPieceName) ? giftedPieceName : "quân cờ";
                string[] oppGiftLines = {
                    $"🔴 Đối thủ dâng quân! Anh ta vừa hiến tặng cho bạn quân {pName} kìa, người tốt như này có được mấy ai đâu!",
                    $"🔴 Đối thủ dâng quân! Đối thủ vừa ship tận tay quân {pName} cho bạn mà không lấy một đồng phí nào!",
                    $"🔴 Đối thủ dâng quân! Một pha từ thiện không hề nhẹ! Đối thủ dâng luôn quân {pName} làm quà tặng tri ân!",
                    $"🔴 Đối thủ dâng quân! Quân {pName} của đối thủ vừa dâng tận miệng! Ăn ngay kẻo nguội bạn ơi!"
                };
                return oppGiftLines[variantIdx % oppGiftLines.Length];
            }

            if (isOpponentBlunder)
            {
                string[] oppBlunderLines = {
                    $"🔴 Đối thủ vừa thực hiện một nước đi ngớ ngẩn! Một pha tự hủy đi vào lòng đất của đối thủ! Thời tới cản không kịp rồi bạn ơi!",
                    $"🔴 Đối thủ vừa thực hiện một nước đi ngớ ngẩn! Nước đi ngớ ngẩn đến từ vị trí đối thủ! Stockfish nhìn pha này cũng phải cạn lời!",
                    $"🔴 Đối thủ vừa thực hiện một nước đi ngớ ngẩn! Đối thủ vừa có pha quăng game thế kỷ! Cơ hội ngàn năm có một để bạn kết liễu trận đấu!",
                    $"🔴 Đối thủ vừa thực hiện một nước đi ngớ ngẩn! Đối thủ hình như đang buồn ngủ hay sao ấy, vừa đi một nước cờ mù mắt thực sự!",
                    $"🔴 Đối thủ vừa thực hiện một nước đi ngớ ngẩn! Một pha 'tự hủy' không thể nào đẹp mắt hơn từ đối thủ! Bạn chỉ việc ngồi rung đùi hưởng thụ thôi!",
                    $"🔴 Đối thủ vừa thực hiện một nước đi ngớ ngẩn! Đối thủ vừa tự đào hố chôn mình rồi! Đừng ngần ngại đẩy họ xuống luôn nhé!"
                };
                return oppBlunderLines[variantIdx % oppBlunderLines.Length];
            }

            switch (classification)
            {
                case MoveClassification.Brilliant:
                    if (isOpponent)
                    {
                        string[] oppBrilliantLines = {
                            $"💎 Nước đi thiên tài của đối thủ! {actionDesc} Một đòn thí quân bất ngờ từ đối phương mang lại lợi thế chiến thuật rõ rệt.",
                            $"💎 Nước đi thiên tài của đối thủ! {actionDesc} Đối thủ vừa có pha phối hợp sắc bén khai thác chính xác sơ hở!",
                            $"💎 Nước đi thiên tài của đối thủ! {actionDesc} Quyết định táo bạo từ đối thủ mang lại hiệu quả rất cao.",
                            $"💎 Nước đi thiên tài của đối thủ! {actionDesc} Đối thủ thí quân khéo léo để mở toang phòng tuyến của bạn!",
                            $"💎 Nước đi thiên tài của đối thủ! {actionDesc} Nước cờ sâu sắc từ đối phương tạo ra thế trận hoàn toàn áp đảo."
                        };
                        commentary = oppBrilliantLines[variantIdx % oppBrilliantLines.Length];
                    }
                    else
                    {
                        string[] brilliantLines = {
                            $"💎 Nước đi thiên tài! {actionDesc} Một đòn thí quân bất ngờ mang lại lợi thế chiến thuật rõ rệt.",
                            $"💎 Nước đi thiên tài! {actionDesc} Đòn phối hợp sắc bén khai thác chính xác sơ hở của đối phương.",
                            $"💎 Nước đi thiên tài! {actionDesc} Quyết định táo bạo nhưng đem lại hiệu quả rất cao.",
                            $"💎 Nước đi thiên tài! {actionDesc} Thí quân khéo léo để mở toang cánh Vua đối thủ.",
                            $"💎 Nước đi thiên tài! {actionDesc} Nước cờ sâu sắc tạo ra thế trận hoàn toàn áp đảo."
                        };
                        commentary = brilliantLines[variantIdx % brilliantLines.Length];
                    }
                    break;

                case MoveClassification.Best:
                    if (isCheckmate)
                    {
                        if (isOpponent)
                        {
                            commentary = $"👑 Đối thủ chiếu hết! {actionDesc} Đối phương đã giáng đòn kết liễu trận đấu.";
                        }
                        else
                        {
                            string[] mateLines = {
                                $"👑 Chiếu hết! {actionDesc} Game over, show's over, đối thủ xách dép về thôi!",
                                $"👑 Chiếu hết! {actionDesc} Cái kết ngọt ngào cho một màn trình diễn đẳng cấp. Pack it up!",
                                $"👑 Chiếu hết! {actionDesc} Đối thủ bị xử đẹp từ đầu đến cuối — không còn gì để cứu vãn!",
                                $"👑 Chiếu hết! {actionDesc} Đóng hòm trận đấu! Tắt máy đi ngủ thôi, đối thủ đã tan thành mây khói!",
                                $"👑 Chiếu hết! {actionDesc} Một kiệt tác trên 64 ô cờ! Đối thủ chỉ biết ngậm ngùi ký biên bản nhận thua!"
                            };
                            commentary = mateLines[variantIdx % mateLines.Length];
                        }
                    }
                    else
                    {
                        if (isOpponent)
                        {
                            string[] oppBestLines = {
                                $"⭐ Nước đi tốt của đối thủ! {actionDesc} Đối phương lựa chọn chuẩn xác, củng cố thế trận.",
                                $"⭐ Nước đi tốt của đối thủ! {actionDesc} Nước cờ chuẩn mực của đối thủ, giữ vững quyền chủ động.",
                                $"⭐ Nước đi tốt của đối thủ! {actionDesc} Đối phương triển khai quân nhịp nhàng và đúng bài bản."
                            };
                            commentary = oppBestLines[variantIdx % oppBestLines.Length];
                        }
                        else
                        {
                            string[] bestLines = {
                                $"⭐ Nước đi tốt! {actionDesc} Lựa chọn hợp lý giúp củng cố thế trận.",
                                $"⭐ Nước đi tốt! {actionDesc} Nước cờ chuẩn xác, giữ vững quyền chủ động.",
                                $"⭐ Nước đi tốt! {actionDesc} Triển khai quân nhịp nhàng và đúng bài bản.",
                                $"⭐ Nước đi tốt! {actionDesc} Nước cờ ổn định, kiểm soát tốt các ô trọng yếu.",
                                $"⭐ Nước đi tốt! {actionDesc} Xử lý tình huống rất gọn gàng và an toàn.",
                                $"⭐ Nước đi tốt! {actionDesc} Lựa chọn tối ưu theo phân tích của Stockfish."
                            };
                            commentary = bestLines[variantIdx % bestLines.Length];
                        }
                    }
                    break;

                case MoveClassification.Excellent:
                    if (isOpponent)
                    {
                        commentary = $"✨ Nước cờ quá mượt của đối thủ! {actionDesc} Đối phương vừa có pha xử lý sắc bén khiến bạn phải căng não suy nghĩ!";
                    }
                    else
                    {
                        string[] excellentLines = {
                            $"✨ Quá mượt! {actionDesc} Nước cờ sắc bén khiến đối thủ toát mồ hôi hột.",
                            $"✨ Quá mượt! {actionDesc} Không hoàn hảo tuyệt đối nhưng cũng gần lắm rồi — đối thủ khó chịu lắm đây.",
                            $"✨ Quá mượt! {actionDesc} Stockfish chỉ biết gật đầu dù vẫn còn cách tốt hơn tí xíu.",
                            $"✨ Quá mượt! {actionDesc} Nước đi đầy tính đe dọa, ép đối thủ phải căng não suy nghĩ!",
                            $"✨ Quá mượt! {actionDesc} Kiểm soát không gian tuyệt vời, thế cờ càng lúc càng mở rộng!"
                        };
                        commentary = excellentLines[variantIdx % excellentLines.Length];
                    }
                    break;

                case MoveClassification.Good:
                    if (isOpponent)
                    {
                        commentary = $"🟢 Nước đi tạm ổn của đối thủ! {actionDesc} Nước đi an toàn của đối phương, giữ vững thế trận.";
                    }
                    else
                    {
                        string[] goodLines = {
                            $"🟢 Tạm ổn! {actionDesc} Không rực rỡ lắm nhưng ít nhất chưa làm hỏng trận.",
                            $"🟢 Tạm ổn! {actionDesc} Nước đi an toàn, không bị mất gì — nhưng táo bạo hơn thì hay hơn nhỉ?",
                            $"🟢 Tạm ổn! {actionDesc} Giữ vững phong độ — chưa xuất sắc nhưng cũng không làm khán giả ngáp.",
                            $"🟢 Tạm ổn! {actionDesc} Nước này tuy không đột biến nhưng giữ vững được sự an toàn tối thiểu.",
                            $"🟢 Tạm ổn! {actionDesc} Chậm mà chắc, kiểu đánh ru ngủ đối phương chờ thời cơ bùng nổ!"
                        };
                        commentary = goodLines[variantIdx % goodLines.Length];
                    }
                    break;

                case MoveClassification.Inaccuracy:
                    if (!string.IsNullOrEmpty(bestAltSan))
                        altText = $" 💡 Stockfish khuyến nghị: Lẽ ra nên đi {bestAltSan} ({altReason}) thì ngon hơn nhiều.";
                    if (isOpponent)
                    {
                        commentary = $"🟡 Đối thủ hơi non tay (-{(lossCp / 100.0):F1} điểm). {actionDesc} Đối phương vừa có nước đi thiếu chính xác, tạo cơ hội cho bạn khai thác!{altText}";
                    }
                    else
                    {
                        string[] inaccLines = {
                            $"🟡 Hơi non tay (-{(lossCp / 100.0):F1} điểm). {actionDesc} Không sai hẳn nhưng cũng không đúng lắm — như ăn cơm mà quên muối vậy.{altText}",
                            $"🟡 Hơi non tay (-{(lossCp / 100.0):F1} điểm). {actionDesc} Đối thủ vừa thở phào nhẹ nhõm — bạn vô tình tặng họ không khí rồi đó.{altText}",
                            $"🟡 Hơi non tay (-{(lossCp / 100.0):F1} điểm). {actionDesc} Nước này làm giảm áp lực, đối thủ mừng thầm đấy — tiếc thật!{altText}",
                            $"🟡 Hơi non tay (-{(lossCp / 100.0):F1} điểm). {actionDesc} Đánh hơi cấn nha! Tự nhiên chậm lại một nhịp cho đối phương kịp hoàn hồn.{altText}",
                            $"🟡 Hơi non tay (-{(lossCp / 100.0):F1} điểm). {actionDesc} Hơi thiếu quyết đoán một chút! Lẽ ra có thể bóp nghẹt đối thủ chặt hơn.{altText}",
                            $"🟡 Hơi non tay (-{(lossCp / 100.0):F1} điểm). {actionDesc} Đang có đà tấn công ngon trớn tự dưng lại đạp phanh! Hơi phí đấy nhé.{altText}"
                        };
                        commentary = inaccLines[variantIdx % inaccLines.Length];
                    }
                    break;

                case MoveClassification.Mistake:
                    if (!string.IsNullOrEmpty(bestAltSan))
                        altText = $" 💡 Stockfish đề xuất: Bạn nên đi {bestAltSan} ({altReason}) để giữ thế trận.";
                    if (isOpponent)
                    {
                        commentary = $"🟠 Đối thủ hơi ngáo ngơ (-{(lossCp / 100.0):F1} điểm)! {actionDesc} Đối thủ vừa có pha xử lý chệch nhịp, bạn hãy nắm bắt cơ hội này ngay!{altText}";
                    }
                    else
                    {
                        string[] mistakeLines = {
                            $"🟠 Hơi ngáo ngơ (-{(lossCp / 100.0):F1} điểm)! {actionDesc} Tự dưng dâng thế trận cho đối phương — bạn đang chơi cờ hay đang làm từ thiện?{altText}",
                            $"🟠 Hơi ngáo ngơ (-{(lossCp / 100.0):F1} điểm)! {actionDesc} Đối thủ không xin mà bạn cũng cho — quá hào phóng! Đây không phải phong trào thiện nguyện đâu nhé.{altText}",
                            $"🟠 Hơi ngáo ngơ (-{(lossCp / 100.0):F1} điểm)! {actionDesc} Stockfish nhìn nước này mà thở dài. Kỳ thủ 5 tuổi cũng biết cần thận trọng hơn chứ!{altText}",
                            $"🟠 Hơi ngáo ngơ (-{(lossCp / 100.0):F1} điểm)! {actionDesc} Nước cờ đi thẳng vào bẫy, đối phương không cười hơi phí đấy!{altText}",
                            $"🟠 Hơi ngáo ngơ (-{(lossCp / 100.0):F1} điểm)! {actionDesc} Một pha tính toán hơi lệch nhịp, đối thủ đã chớp lấy được lợi thế rồi!{altText}",
                            $"🟠 Hơi ngáo ngơ (-{(lossCp / 100.0):F1} điểm)! {actionDesc} Não bộ vừa có dấu hiệu quá tải chăng? Nước đi này thực sự khiến người xem thót tim!{altText}"
                        };
                        commentary = mistakeLines[variantIdx % mistakeLines.Length];
                    }
                    break;

                case MoveClassification.Blunder:
                default:
                    if (!string.IsNullOrEmpty(bestAltSan))
                        altText = $" 💡 Lẽ ra phải đi {bestAltSan} ({altReason}) mới chuẩn bài!";
                    if (isOpponent)
                    {
                        commentary = $"🔴 Đối thủ vừa thực hiện một nước đi ngớ ngẩn! {actionDesc} Pha xử lý sai lầm nghiêm trọng của đối phương mở ra bước ngoặt lớn cho bạn!{altText}";
                    }
                    else
                    {
                        string[] blunderLines = {
                            $"🔴 Sai lầm ngớ ngẩn (-{(lossCp / 100.0):F1} điểm)! {actionDesc} Mù cờ hay sao mà đi nước này bạn ơi? Một pha tự hủy đi vào lòng đất!{altText}",
                            $"🔴 Sai lầm ngớ ngẩn (-{(lossCp / 100.0):F1} điểm)! {actionDesc} Ông/bà vừa biếu quân không công — đối thủ không cần bẫy, bạn tự chui vào rồi! Kỳ thủ 5 tuổi cũng không làm vậy đâu nhé.{altText}",
                            $"🔴 Sai lầm ngớ ngẩn (-{(lossCp / 100.0):F1} điểm)! {actionDesc} Nước đi này thậm chí không cần Stockfish để biết là tệ — cứ nhìn là thấy muốn khóc thay rồi!{altText}",
                            $"🔴 Sai lầm ngớ ngẩn (-{(lossCp / 100.0):F1} điểm)! {actionDesc} Đánh cờ bằng niềm tin hay sao vậy? Thầy dạy cờ mà thấy nước này chắc ngất ngay tại chỗ!{altText}",
                            $"🔴 Sai lầm ngớ ngẩn (-{(lossCp / 100.0):F1} điểm)! {actionDesc} Vừa đặt tay xuống bàn là thấy cả tương lai u tối! Một pha quăng game cực kỳ mãn nhãn!{altText}",
                            $"🔴 Sai lầm ngớ ngẩn (-{(lossCp / 100.0):F1} điểm)! {actionDesc} Chuyện gì vừa xảy ra vậy? Bạn đang cố gắng tạo thử thách khó cho bản thân à?{altText}",
                            $"🔴 Sai lầm ngớ ngẩn (-{(lossCp / 100.0):F1} điểm)! {actionDesc} Nước đi này xứng đáng đi vào lịch sử những pha xử lý thảm họa nhất năm!{altText}"
                        };
                        commentary = blunderLines[variantIdx % blunderLines.Length];
                    }
                    break;
            }

            if (!isCheckmate && afterEval?.MateIn != null && afterEval.MateIn.Value != 0)
            {
                int mate = afterEval.MateIn.Value;
                bool isWhite = string.Equals(movingColor, "white", StringComparison.OrdinalIgnoreCase);
                bool myMate = isWhite ? mate > 0 : mate < 0;
                if (myMate)
                {
                    commentary += $" ⚔️ Chiếu hết không thể cản phá trong {Math.Abs(mate)} nước! Chuẩn bị ăn mừng thôi — đối thủ xong phim rồi!";
                }
                else
                {
                    commentary += $" ⚠️ Toang rồi! Đối thủ có thể chiếu hết trong {Math.Abs(mate)} nước — phòng thủ gấp hoặc chuẩn bị... xách dép!";
                }
            }

            return commentary;
        }

        private static string GetPieceClipKey(string? pieceType)
        {
            if (string.IsNullOrWhiteSpace(pieceType)) return "piece_general";
            string clean = pieceType.Trim();
            if (clean.StartsWith("W_") || clean.StartsWith("B_")) clean = clean.Substring(2);
            return clean.ToLowerInvariant() switch
            {
                "pawn" or "p" => "piece_pawn",
                "knight" or "n" => "piece_knight",
                "bishop" or "b" => "piece_bishop",
                "rook" or "r" => "piece_rook",
                "queen" or "q" => "piece_queen",
                "king" or "k" => "piece_king",
                _ => "piece_general"
            };
        }

        private static string GetSquareClipKey(int r, int c)
        {
            if (r < 0 || r >= 8 || c < 0 || c >= 8) return "";
            char file = (char)('a' + c);
            int rank = 8 - r;
            return $"sq_{file}{rank}";
        }

        public static List<string> BuildVoiceClipSequence(
            MoveClassification classification,
            string pieceType,
            int fromRow, int fromCol,
            int toRow, int toCol,
            string? promoPiece,
            bool isCapture,
            bool isCheck,
            bool isCheckmate,
            string? capturedPiece,
            string movingColor,
            ChessGame? gameContext,
            string? bestAltSan,
            string? altReason,
            (int fromRow, int fromCol, int toRow, int toCol, string? promo) bestAltCoords,
            PositionEvaluation? afterEval,
            int variantIdx = 0,
            bool isMissedMate = false,
            bool isOpponentGift = false,
            string? giftedPieceType = null,
            bool isOpponentBlunder = false,
            bool isOpponent = false)
        {
            var clips = new List<string>();
            if (classification == MoveClassification.None) return clips;

            if (isMissedMate)
            {
                clips.Add("title_missed_mate");
                clips.Add($"react_missed_mate_{(variantIdx % 5) + 1}");
                if (!string.IsNullOrEmpty(bestAltSan))
                {
                    clips.Add("alt_pref_blunder");
                    string altSqClip = GetSquareClipKey(bestAltCoords.toRow, bestAltCoords.toCol);
                    if (!string.IsNullOrEmpty(altSqClip)) clips.Add(altSqClip);
                    clips.Add("alt_suf_blunder");
                }
                return clips;
            }

            if (isOpponentGift)
            {
                clips.Add("title_opp_gift");
                int leadIdx = (variantIdx % 4) + 1;
                clips.Add($"opp_gift_lead_{leadIdx}");
                clips.Add(GetPieceClipKey(giftedPieceType ?? pieceType));
                clips.Add($"opp_gift_tail_{leadIdx}");
                return clips;
            }

            if (isOpponentBlunder)
            {
                clips.Add("title_opp_blunder_move");
                clips.Add($"opp_blunder_{(variantIdx % 6) + 1}");
                return clips;
            }

            // 1. Tiêu đề phân loại (KHÔNG đọc điểm số theo yêu cầu)
            string titleClip = isOpponent
                ? classification switch
                {
                    MoveClassification.Brilliant => "title_opp_brilliant",
                    MoveClassification.Best => isCheckmate ? "title_mate" : "title_opp_best",
                    MoveClassification.Excellent => "title_excellent",
                    MoveClassification.Good => "title_good",
                    MoveClassification.Inaccuracy => "title_inaccuracy",
                    MoveClassification.Mistake => "title_mistake",
                    _ => "title_opp_blunder_move"
                }
                : classification switch
                {
                    MoveClassification.Brilliant => "title_brilliant",
                    MoveClassification.Best => isCheckmate ? "title_mate" : "title_best",
                    MoveClassification.Excellent => "title_excellent",
                    MoveClassification.Good => "title_good",
                    MoveClassification.Inaccuracy => "title_inaccuracy",
                    MoveClassification.Mistake => "title_mistake",
                    _ => "title_blunder"
                };
            clips.Add(titleClip);

            string pClip = GetPieceClipKey(pieceType);
            string toSqClip = GetSquareClipKey(toRow, toCol);
            bool isWhite = string.Equals(movingColor, "white", StringComparison.OrdinalIgnoreCase);

            // 2. Hành động nước đi
            if (isCheckmate)
            {
                clips.Add("act_checkmate_lead");
                clips.Add(pClip);
                clips.Add("conn_at_sq");
                if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                clips.Add("act_checkmate_end");
            }
            else if (pieceType == "King" && Math.Abs(toCol - fromCol) == 2)
            {
                if (toCol > fromCol) clips.Add("act_castle_near");
                else clips.Add("act_castle_far");
                if (isCheck) clips.Add("act_check_note");
            }
            else if (!string.IsNullOrEmpty(promoPiece) || (pieceType == "Pawn" && (toRow == 0 || toRow == 7)))
            {
                clips.Add("act_promo_lead");
                clips.Add(GetPieceClipKey(promoPiece ?? "Queen"));
                clips.Add("act_promo_mid");
                if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                clips.Add("act_promo_end");
                if (isCheck) clips.Add("act_check_note");
            }
            else if (gameContext != null && (pieceType == "Knight" || pieceType == "Pawn") &&
                     CountValuableOpponentPiecesAttacked(toRow, toCol, pieceType, movingColor, gameContext) >= 2)
            {
                clips.Add("act_fork_lead");
                clips.Add(pClip);
                clips.Add("conn_at");
                if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                clips.Add("act_fork_end");
                if (isCheck) clips.Add("act_check_note");
            }
            else if (isCapture || !string.IsNullOrEmpty(capturedPiece))
            {
                string capType = !string.IsNullOrEmpty(capturedPiece) ? ChessGame.PieceType(capturedPiece) : "";
                if (capType == "Queen")
                {
                    clips.Add("act_cap_use");
                    clips.Add(pClip);
                    clips.Add("act_cap_queen_mid");
                    if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                    clips.Add("act_cap_queen_end");
                }
                else if (capType == "Rook")
                {
                    clips.Add("act_cap_use");
                    clips.Add(pClip);
                    clips.Add("act_cap_rook_mid");
                    if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                    clips.Add("act_cap_rook_end");
                }
                else if (capType == "Bishop")
                {
                    clips.Add("act_cap_use");
                    clips.Add(pClip);
                    clips.Add("act_cap_bishop_mid");
                    if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                    clips.Add("act_cap_minor_end");
                }
                else if (capType == "Knight")
                {
                    clips.Add("act_cap_use");
                    clips.Add(pClip);
                    clips.Add("act_cap_knight_mid");
                    if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                    clips.Add("act_cap_minor_end");
                }
                else if (capType == "Pawn")
                {
                    clips.Add("act_eat_pawn_lead");
                    if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                    clips.Add("act_eat_pawn_end");
                }
                else
                {
                    clips.Add("act_cap_use");
                    clips.Add(pClip);
                    clips.Add("act_cap_gen_mid");
                    if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                    clips.Add("act_cap_gen_end");
                }
                if (isCheck) clips.Add("act_check_note");
            }
            else if (isCheck)
            {
                clips.Add("act_check_normal_lead");
                clips.Add(pClip);
                clips.Add("act_check_normal_from");
                if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                clips.Add("act_check_normal_end");
            }
            else
            {
                // Khai cuộc và nước đi thế trận
                var opening = DetectOpening(gameContext, movingColor, pieceType, fromRow, fromCol, toRow, toCol, isCapture);
                if (opening.HasValue && !string.IsNullOrEmpty(opening.Value.clipKey))
                {
                    clips.Add(opening.Value.clipKey);
                }
                else if (pieceType == "Pawn" && (isWhite ? fromRow == 6 : fromRow == 1))
                {
                    if (isWhite && fromRow == 6 && fromCol == 4 && toRow == 4 && toCol == 4) clips.Add("open_e4");
                    else if (isWhite && fromRow == 6 && fromCol == 3 && toRow == 4 && toCol == 3) clips.Add("open_d4");
                    else if (isWhite && fromRow == 6 && fromCol == 2 && toRow == 4 && toCol == 2) clips.Add("open_c4");
                    else if (!isWhite && fromRow == 1 && fromCol == 4 && toRow == 3 && toCol == 4) clips.Add("open_e5");
                    else if (!isWhite && fromRow == 1 && fromCol == 2 && toRow == 3 && toCol == 2) clips.Add("open_c5");
                    else if (!isWhite && fromRow == 1 && fromCol == 3 && toRow == 3 && toCol == 3) clips.Add("open_d5");
                    else if (!isWhite && fromRow == 1 && fromCol == 4 && toRow == 2 && toCol == 4) clips.Add("open_e6");
                    else if (!isWhite && fromRow == 1 && fromCol == 2 && toRow == 2 && toCol == 2) clips.Add("open_c6");
                    else
                    {
                        clips.Add("act_pawn_advance");
                        if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                        clips.Add("act_pawn_center_end");
                    }
                }
                else if (pieceType == "Pawn")
                {
                    if ((toRow == 3 || toRow == 4) && (toCol == 3 || toCol == 4))
                    {
                        clips.Add("act_pawn_advance");
                        if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                        clips.Add("act_pawn_center_end");
                    }
                    else if ((toRow == 3 || toRow == 4) && (toCol == 2 || toCol == 5))
                    {
                        clips.Add("act_pawn_push");
                        if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                        clips.Add("act_pawn_wing_end");
                    }
                    else if ((isWhite && toRow <= 3) || (!isWhite && toRow >= 4))
                    {
                        if (IsPassedPawn(toRow, toCol, movingColor, gameContext))
                        {
                            clips.Add("act_pawn_passed_lead");
                            if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                            clips.Add("act_pawn_passed_end");
                        }
                        else
                        {
                            clips.Add("act_pawn_push_line");
                            if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                            clips.Add("act_pawn_line_end");
                        }
                    }
                    else
                    {
                        clips.Add("act_pawn_advance");
                        if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                        clips.Add("act_pawn_solid_end");
                    }
                }
                else if (pieceType == "Knight")
                {
                    if (isWhite && fromRow == 7 && fromCol == 6 && toRow == 5 && toCol == 5) clips.Add("open_nf3");
                    else if (!isWhite && fromRow == 0 && fromCol == 6 && toRow == 2 && toCol == 5) clips.Add("open_nf6");
                    else if ((toRow == 5 && toCol == 5) || (toRow == 5 && toCol == 2) || (toRow == 2 && toCol == 5) || (toRow == 2 && toCol == 2))
                    {
                        clips.Add("act_knight_dev");
                        if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                        clips.Add("act_knight_center_end");
                    }
                    else if ((toRow == 3 || toRow == 4) && (toCol == 3 || toCol == 4))
                    {
                        clips.Add("act_knight_outpost_lead");
                        if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                        clips.Add("act_knight_outpost_end");
                    }
                    else
                    {
                        clips.Add("act_knight_maneuver_lead");
                        if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                        clips.Add("act_knight_maneuver_end");
                    }
                }
                else if (pieceType == "Bishop")
                {
                    string toSq = SquareName(toRow, toCol);
                    if (toSq == "b2" || toSq == "g2" || toSq == "b7" || toSq == "g7")
                    {
                        clips.Add("act_bishop_fianchetto");
                        if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                        clips.Add("act_bishop_fianchetto_end");
                    }
                    else if (toSq == "c4" || toSq == "b5" || toSq == "f4" || toSq == "g5" || toSq == "c5" || toSq == "b4" || toSq == "f5" || toSq == "g4")
                    {
                        clips.Add("act_bishop_dev");
                        if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                        clips.Add("act_bishop_dev_end");
                    }
                    else
                    {
                        clips.Add("act_bishop_open_lead");
                        if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                        clips.Add("act_bishop_open_end");
                    }
                }
                else if (pieceType == "Rook")
                {
                    if ((isWhite && toRow == 1) || (!isWhite && toRow == 6))
                    {
                        clips.Add("act_rook_7th");
                    }
                    else if (toCol >= 2 && toCol <= 5)
                    {
                        clips.Add("act_rook_file_lead");
                        clips.Add($"file_{(char)('a' + toCol)}");
                        clips.Add("act_rook_file_end");
                    }
                    else
                    {
                        clips.Add("act_rook_move_lead");
                        if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                        clips.Add("act_rook_move_end");
                    }
                }
                else if (pieceType == "Queen")
                {
                    clips.Add("act_queen_move_lead");
                    if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                    clips.Add("act_queen_move_end");
                }
                else if (pieceType == "King")
                {
                    clips.Add("act_king_move_lead");
                    if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                    clips.Add("act_king_move_end");
                }
                else
                {
                    clips.Add("act_gen_move_lead");
                    clips.Add(pClip);
                    clips.Add("act_gen_move_to");
                    if (!string.IsNullOrEmpty(toSqClip)) clips.Add(toSqClip);
                }
            }

            // 3. Câu phản hồi cợt nhả
            switch (classification)
            {
                case MoveClassification.Brilliant:
                    clips.Add($"react_brilliant_{(variantIdx % 5) + 1}");
                    break;
                case MoveClassification.Best:
                    if (isCheckmate) clips.Add($"react_mate_{(variantIdx % 5) + 1}");
                    else clips.Add($"react_best_{(variantIdx % 6) + 1}");
                    break;
                case MoveClassification.Excellent:
                    clips.Add($"react_excellent_{(variantIdx % 5) + 1}");
                    break;
                case MoveClassification.Good:
                    clips.Add($"react_good_{(variantIdx % 5) + 1}");
                    break;
                case MoveClassification.Inaccuracy:
                    clips.Add($"react_inacc_{(variantIdx % 6) + 1}");
                    break;
                case MoveClassification.Mistake:
                    clips.Add($"react_mistake_{(variantIdx % 6) + 1}");
                    break;
                case MoveClassification.Blunder:
                default:
                    clips.Add($"react_blunder_{(variantIdx % 7) + 1}");
                    break;
            }

            // 4. Gợi ý nước đi thay thế nếu có
            if (!string.IsNullOrEmpty(bestAltSan))
            {
                if (classification == MoveClassification.Inaccuracy) clips.Add("alt_pref_inacc");
                else if (classification == MoveClassification.Mistake) clips.Add("alt_pref_mistake");
                else if (classification == MoveClassification.Blunder) clips.Add("alt_pref_blunder");

                string altSqClip = GetSquareClipKey(bestAltCoords.toRow, bestAltCoords.toCol);
                if (!string.IsNullOrEmpty(altSqClip)) clips.Add(altSqClip);

                if (!string.IsNullOrEmpty(altReason))
                {
                    if (altReason.Contains("nhập thành")) clips.Add("alt_reason_castle");
                    else if (altReason.Contains("chiếu Vua")) clips.Add("alt_reason_check");
                    else if (altReason.Contains("bắt quân")) clips.Add("alt_reason_capture");
                    else if (altReason.Contains("Mã")) clips.Add("alt_reason_knight");
                    else if (altReason.Contains("Tượng")) clips.Add("alt_reason_bishop");
                    else if (altReason.Contains("Xe")) clips.Add("alt_reason_rook");
                    else if (altReason.Contains("Hậu")) clips.Add("alt_reason_queen");
                    else if (altReason.Contains("Vua")) clips.Add("alt_reason_king");
                    else clips.Add("alt_reason_center");
                }

                if (classification == MoveClassification.Inaccuracy) clips.Add("alt_suf_inacc");
                else if (classification == MoveClassification.Mistake) clips.Add("alt_suf_mistake");
                else if (classification == MoveClassification.Blunder) clips.Add("alt_suf_blunder");
            }

            // 5. Cảnh báo chiếu hết
            if (!isCheckmate && afterEval?.MateIn != null && afterEval.MateIn.Value != 0)
            {
                int mate = afterEval.MateIn.Value;
                bool myMate = isWhite ? mate > 0 : mate < 0;
                int absMate = Math.Abs(mate);
                if (myMate)
                {
                    if (absMate == 1) clips.Add("mate_win_1");
                    else if (absMate == 2) clips.Add("mate_win_2");
                    else if (absMate == 3) clips.Add("mate_win_3");
                    else clips.Add("mate_win_few");
                }
                else
                {
                    if (absMate == 1) clips.Add("mate_lose_1");
                    else if (absMate == 2) clips.Add("mate_lose_2");
                    else if (absMate == 3) clips.Add("mate_lose_3");
                    else clips.Add("mate_lose_few");
                }
            }

            return clips;
        }

        public static string FormatFriendlySan((int fR, int fC, int tR, int tC, string? promo) coords, ChessGame? game)
        {
            if (coords.fR < 0 || coords.fC < 0 || coords.tR < 0 || coords.tC < 0) return "";

            char toFile = (char)('a' + coords.tC);
            int toRank = 8 - coords.tR;
            string toSquare = $"{toFile}{toRank}";

            string promoSuffix = "";
            if (!string.IsNullOrEmpty(coords.promo))
            {
                char pChar = char.ToUpper(coords.promo[0]);
                if (pChar == 'K') pChar = 'N'; // Knight
                promoSuffix = $"={pChar}";
            }

            if (game != null)
            {
                var board = game.GetBoardState();
                string piece = board[coords.fR, coords.fC];
                string targetPiece = board[coords.tR, coords.tC];
                bool isCapture = !string.IsNullOrEmpty(targetPiece);

                string type = ChessGame.PieceType(piece);
                string prefix = type switch
                {
                    "Knight" => "♘",
                    "Bishop" => "♗",
                    "Rook" => "♖",
                    "Queen" => "♕",
                    "King" => "♔",
                    _ => ""
                };

                // Nhập thành
                if (type == "King" && Math.Abs(coords.tC - coords.fC) == 2)
                {
                    return coords.tC > coords.fC ? "O-O" : "O-O-O";
                }

                if (string.IsNullOrEmpty(prefix))
                {
                    // Tốt đi
                    if (coords.fC != coords.tC)
                    {
                        char fromFile = (char)('a' + coords.fC);
                        return $"{fromFile}x{toSquare}{promoSuffix}";
                    }
                    return $"{toSquare}{promoSuffix}";
                }

                string cap = isCapture ? "x" : "";
                return $"{prefix}{cap}{toSquare}{promoSuffix}";
            }

            char fF = (char)('a' + coords.fC);
            int fR = 8 - coords.fR;
            return $"{fF}{fR}➔{toSquare}{promoSuffix}";
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            try
            {
                if (process != null && !process.HasExited)
                {
                    SendCommand("quit");
                    if (!process.WaitForExit(400))
                    {
                        process.Kill();
                    }
                }
            }
            catch { }
            finally
            {
                inputWriter?.Dispose();
                outputReader?.Dispose();
                process?.Dispose();
                semaphore.Dispose();
                inputWriter = null;
                outputReader = null;
                process = null;
                isReady = false;
            }
        }
    }
}
