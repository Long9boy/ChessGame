using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using ChessGame_PJ.Core;
using ChessGame_PJ.Core.AI;
using ChessGame_PJ.Services;

namespace ChessGame_PJ.Views
{
    public partial class GameWindow : Window
    {
        [DllImport("user32.dll", EntryPoint = "SetClassLongPtr")]
        private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetClassLong")]
        private static extern int SetClassLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(int crColor);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            if (hwnd != IntPtr.Zero)
            {
                // 1. Enable Windows immersive dark mode for title bar (Windows 10/11)
                int darkMode = 1;
                DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref darkMode, sizeof(int));
                DwmSetWindowAttribute(hwnd, 19 /* DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 */, ref darkMode, sizeof(int));

                // 2. Set Win32 window class background to dark brush #1E1C2E (0x002E1C1E in COLORREF B-G-R)
                IntPtr darkBrush = CreateSolidBrush(0x002E1C1E);
                if (IntPtr.Size == 8)
                    SetClassLongPtr64(hwnd, -10 /* GCLP_HBRBACKGROUND */, darkBrush);
                else
                    SetClassLong32(hwnd, -10 /* GCL_HBRBACKGROUND */, darkBrush.ToInt32());

                // 3. Intercept WM_ERASEBKGND so Win32 never clears the window with default white brush
                var source = HwndSource.FromHwnd(hwnd);
                source?.AddHook(WndProcEraseBackground);
            }
        }

        private IntPtr WndProcEraseBackground(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_ERASEBKGND = 0x0014;
            if (msg == WM_ERASEBKGND)
            {
                handled = true;
                return (IntPtr)1;
            }
            return IntPtr.Zero;
        }

        private const int PerTurnTimeLimitSeconds = 90; // 1'30s max per turn
        private int lastBeepSecond = -1; // For 10s countdown beep

        // Visual elements for 8x8 board
        private readonly Border[,] cellBorders = new Border[8, 8];
        private readonly Image[,] cellImages = new Image[8, 8];
        private readonly Ellipse[,] cellDots = new Ellipse[8, 8];
        private readonly Border[,] cellEvalBadges = new Border[8, 8];
        private readonly TextBlock[,] cellEvalBadgeTexts = new TextBlock[8, 8];
        private readonly List<bool> replayStepIsWhite = new List<bool>();
        private readonly Color[,] originalSquareColors = new Color[8, 8];

        private readonly ChessGame chessGame = new ChessGame();

        // Game state
        private string roomId = "";
        private string playerName = "";

        // Tên hiển thị (displayName) của chính mình — playerName là TÊN TÀI KHOẢN,
        // dùng để lưu dữ liệu (room.player1/2, GameRecord, tra bạn bè...).
        // Gọi hàm này ở bất kỳ đâu trong class để lấy tên hiển thị lên bàn cờ.
        private string GetMyDisplayName()
        {
            return (AuthService.IsLoggedIn && AuthService.CurrentUser != null &&
                !string.IsNullOrWhiteSpace(AuthService.CurrentUser.displayName))
                ? AuthService.CurrentUser.displayName
                : playerName;
        }
        private string myColor = "white";
        private string myAvatar = "avatar1.png";

        // Bot mode state
        private bool isBotMode = false;
        private BotDifficulty botDifficulty = BotDifficulty.Easy;
        private IChessBot? chessBot = null;
        private string botColor = "black";
        private string botName = "Máy";
        private string botAvatar = "avatar2.png";
        private DispatcherTimer? gameDurationTimer;
        private int gameElapsedSeconds = 0;
        private bool isBotGamePaused = false;

        // Matchmaking mode state
        private bool isMatchmaking = false;

        // Timers & Polling
        private DispatcherTimer? pollTimer;
        private DispatcherTimer? turnCountdownTimer;
        private DispatcherTimer? glowAnimationTimer;

        private bool isBusy = false;
        private bool isProcessingClick = false;
        private bool isTimeoutHandling = false;
        private bool isHandlingGameOver = false;
        private RoomData? lastRoom = null;

        // Turn timing for move history
        private DateTime whiteTurnStartTime = DateTime.UtcNow;
        private DateTime blackTurnStartTime = DateTime.UtcNow;
        private double currentRoundSeconds = 0;

        // Turn & Glow
        private string currentTurn = "white";
        private double glowAngle = 0;
        private double glowPulse = 1.0;

        // Selected square
        private int selectedRow = -1;
        private int selectedCol = -1;

        // Last move highlight coordinates
        private int lastFromRow = -1;
        private int lastFromCol = -1;
        private int lastToRow = -1;
        private int lastToCol = -1;

        // Legal move hints (dots)
        private bool showMoveHints = true;

        // Drag & Drop fields
        private bool isDragPending = false;
        private bool isDraggingPiece = false;
        private int dragFromRow = -1;
        private int dragFromCol = -1;
        private Point dragStartPoint;
        private bool wasPieceSelectedOnMouseDown = false;

        // Local move history for bot mode and custom mode
        private readonly List<MoveHistoryEntry> localMoveHistory = new List<MoveHistoryEntry>();

        // Stockfish Evaluation & Real-time Analysis State (Offline / Bot Mode)
        private StockfishEvaluator? stockfishEvaluator = null;
        private PositionEvaluation? lastPositionEval = null;
        private bool isStockfishEvalEnabled = true;
        private System.Threading.Channels.Channel<StockfishEvalTask>? evalChannel = null;
        private System.Threading.CancellationTokenSource? evalWorkerCts = null;
        private Task? evalWorkerTask = null;

        // Stockfish best move hint coordinates
        private int hintFromRow = -1;
        private int hintFromCol = -1;
        private int hintToRow = -1;
        private int hintToCol = -1;

        // Custom Mode & Replay Mode fields
        public bool isCustomMode = false;
        public bool isReplayMode = false;

        /// <summary>
        /// True nếu cửa sổ này đang là một trận đấu thật đang diễn ra (Đấu máy hoặc Online PvP/Ghép trận),
        /// KHÔNG tính chế độ Custom (tự chơi 2 bên) hay Xem lại. Dùng để chặn việc mở lại (Xem lại)
        /// một trận đấu khác trong Lịch sử đấu khi đang thi đấu dở dang.
        /// </summary>
        public bool IsLiveMatch => !isCustomMode && !isReplayMode;
        private GameRecord? replayRecord = null;
        private int currentReplayStep = 0;
        private readonly List<Dictionary<string, string>> replaySnapshots = new List<Dictionary<string, string>>();
        private readonly List<int> replayStepToMoveIndex = new List<int>();
        private DispatcherTimer? replayPlaybackTimer = null;
        private Border? highlightedHistoryRow = null;

        // Threefold repetition tracking (Tam trùng thế cờ / Lặp lại nước đi 3 lần cho mọi chế độ)
        private readonly Dictionary<string, int> positionHistory = new(StringComparer.Ordinal);

        // Custom Mode interactive annotations (Ctrl+click arrows & Alt+click red highlights)
        private readonly List<(int fromR, int fromC, int toR, int toC)> customArrows = new();
        private readonly HashSet<(int r, int c)> customRedHighlightedSquares = new();
        private (int r, int c)? ctrlArrowStartSquare = null;

        // Highlight brushes for Play/Pause buttons in Custom & Replay playback controls
        private static readonly Brush ReplayButtonActiveBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5));   // Xanh dương (đang hoạt động)
        private static readonly Brush ReplayButtonInactiveBrush = new SolidColorBrush(Color.FromRgb(0xEA, 0xE6, 0xDF)); // Mặc định

        // Cập nhật màu nền của nút Play/Pause tùy theo trạng thái đang phát hay đang tạm dừng
        private void SetReplayPlayPauseHighlight(bool isPlaying)
        {
            if (btnReplayPlay != null)
                btnReplayPlay.Background = isPlaying ? ReplayButtonActiveBrush : ReplayButtonInactiveBrush;
            if (btnReplayPause != null)
                btnReplayPause.Background = isPlaying ? ReplayButtonInactiveBrush : ReplayButtonActiveBrush;
        }

        /// <summary>
        /// Xây dựng danh sách các "khung hình" (snapshot bàn cờ) dùng cho chế độ Xem lại,
        /// gồm khung hình ban đầu (bàn cờ khởi tạo) và khung hình sau mỗi nửa nước đi.
        /// </summary>
        private void BuildReplaySnapshots(GameRecord record)
        {
            replaySnapshots.Clear();
            replayStepToMoveIndex.Clear();
            replayStepIsWhite.Clear();

            chessGame.InitializeBoard();
            replaySnapshots.Add(BoardToDict());
            replayStepToMoveIndex.Add(0);
            replayStepIsWhite.Add(true);

            if (record.moves != null)
            {
                for (int i = 0; i < record.moves.Count; i++)
                {
                    var m = record.moves[i];
                    if (m.boardAfterWhite != null && m.boardAfterWhite.Count > 0)
                    {
                        replaySnapshots.Add(m.boardAfterWhite);
                        replayStepToMoveIndex.Add(i);
                        replayStepIsWhite.Add(true);
                    }
                    if (m.boardAfterBlack != null && m.boardAfterBlack.Count > 0)
                    {
                        replaySnapshots.Add(m.boardAfterBlack);
                        replayStepToMoveIndex.Add(i);
                        replayStepIsWhite.Add(false);
                    }
                }
            }
        }

        /// <summary>
        /// Khi một ván đấu (Đấu máy / Online PvP / Ghép trận) kết thúc — dù là do chiếu hết,
        /// hoà cờ, đầu hàng hay mất kết nối — thay vì đóng cửa sổ và quay về Lobby ngay lập tức,
        /// hàm này chuyển cửa sổ hiện tại sang chế độ "Xem lại" ngay tại chỗ: giữ nguyên toàn bộ
        /// bàn cờ + lịch sử nước đi vừa đấu, cho phép người chơi bấm Phát lại từ đầu hoặc dùng
        /// nút Nước đi trước/tiếp để tua qua từng nước, và chỉ rời về Lobby khi họ chủ động bấm nút Đóng.
        /// </summary>
        private void EnterPostGameReviewMode(GameRecord record)
        {
            isReplayMode = true;
            isHandlingGameOver = true;
            replayRecord = record;

            selectedRow = -1;
            selectedCol = -1;

            replayPlaybackTimer?.Stop();
            gameDurationTimer?.Stop();
            glowAnimationTimer?.Stop();
            pollTimer?.Stop();
            turnCountdownTimer?.Stop();

            pnlPvPActionButtons.Visibility = Visibility.Collapsed;
            btnBotExit.Visibility = Visibility.Collapsed;
            pnlReplayCustomControls.Visibility = Visibility.Visible;
            btnCustomStockfishHint.Visibility = Visibility.Collapsed;
            btnLeave.Visibility = Visibility.Visible;
            btnLeave.Content = "🚪 Về Sảnh Chờ";

            lblClockP1.Text = "--:--";
            lblClockP2.Text = "--:--";
            lblRoomInfo.Text = $"📜 Trận đấu đã kết thúc — Xem lại: {record.gameMode}";

            txtName1.Text = !string.IsNullOrWhiteSpace(record.displayNameWhite) ? record.displayNameWhite : record.playerWhite;
            imgAvatar1.Source = ImageResources.GetAvatarImage(record.avatarWhite);
            txtName2.Text = !string.IsNullOrWhiteSpace(record.displayNameBlack) ? record.displayNameBlack : record.playerBlack;
            imgAvatar2.Source = ImageResources.GetAvatarImage(record.avatarBlack);

            // Bật bảng phân tích Stockfish và thanh Eval Bar sau khi kết thúc trận
            isStockfishEvalEnabled = true;
            pnlStockfishAnalysis.Visibility = Visibility.Visible;
            borderLastMoveEval.Visibility = Visibility.Visible;
            pnlEvalBar.Visibility = Visibility.Visible;
            btnStockfishHint.Visibility = Visibility.Collapsed;
            btnToggleEval.Visibility = Visibility.Collapsed;

            if (stockfishEvaluator == null || !stockfishEvaluator.IsAvailable)
            {
                InitializeStockfishEvaluation();
            }

            BuildReplaySnapshots(record);
            RenderMoveHistoryUI(record.moves, showTime: true);

            // Tự động phân tích các nước đi còn thiếu trong lịch sử ván đấu
            _ = EnsureRecordMovesEvaluatedAsync(record);

            // Mặc định hiển thị đúng thế cờ lúc kết thúc trận; người chơi có thể bấm Prev/Next
            // để tua từng nước, hoặc bấm Play để phát lại toàn bộ ván đấu từ đầu.
            currentReplayStep = replaySnapshots.Count - 1;
            ApplyReplayStep(currentReplayStep);
            SetReplayPlayPauseHighlight(isPlaying: false);
            RenderBotModeSelectionAndDots();
        }

        public GameWindow()
        {
            InitializeComponent();
        }

        // Custom Mode Constructor (1 player controls both sides)
        public GameWindow(bool isCustom, string playerName, string myAvatar = "avatar1.png", bool showMoveHints = true) : this()
        {
            this.isCustomMode = true;
            this.playerName = playerName;
            this.myAvatar = myAvatar;
            this.myColor = "white"; // Initially white to move
            this.showMoveHints = showMoveHints;

            Loaded += GameWindow_Loaded;
            Closed += GameWindow_Closed;
        }

        // Replay Mode Factory
        public static GameWindow CreateReplayWindow(GameRecord record)
        {
            var win = new GameWindow();
            win.isReplayMode = true;
            win.replayRecord = record;
            win.playerName = !string.IsNullOrWhiteSpace(record.displayNameWhite) ? record.displayNameWhite : record.playerWhite;
            win.myAvatar = record.avatarWhite;
            win.showMoveHints = false;

            win.Loaded += win.GameWindow_Loaded;
            win.Closed += win.GameWindow_Closed;
            return win;
        }

        // PvP Mode Constructor
        public GameWindow(string roomId, string playerName, bool isCreator, string myAvatar = "avatar1.png", bool isMatchmaking = false, bool showMoveHints = true) : this()
        {
            this.roomId = roomId;
            this.playerName = playerName;
            this.myColor = isCreator ? "white" : "black";
            this.myAvatar = myAvatar;
            this.isMatchmaking = isMatchmaking;
            this.showMoveHints = !isMatchmaking && showMoveHints;

            Loaded += GameWindow_Loaded;
            Closed += GameWindow_Closed;
        }

        // Bot Mode Constructor
        public GameWindow(BotDifficulty difficulty, string playerName, string selectedColor = "white", string myAvatar = "avatar1.png", bool showMoveHints = true) : this()
        {
            this.isBotMode = true;
            this.botDifficulty = difficulty;
            this.playerName = playerName;
            this.myColor = selectedColor.ToLowerInvariant();
            this.botColor = this.myColor == "white" ? "black" : "white";
            this.myAvatar = myAvatar;
            this.showMoveHints = showMoveHints;

            (this.botName, this.botAvatar, this.chessBot) = difficulty switch
            {
                BotDifficulty.Easy => ("Máy (Dễ)", "avatar2.png", (IChessBot)new EasyBot()),
                BotDifficulty.Medium => ("Máy (Thường)", "avatar5.png", (IChessBot)new MediumBot()),
                BotDifficulty.Hard => ("Máy (Khó)", "avatar7.png", (IChessBot)new HardBot()),
                BotDifficulty.Stockfish => ("Stockfish 19", "stockfish.png", (IChessBot)new StockfishBot()),
                _ => ("Máy", "avatar2.png", (IChessBot)new EasyBot())
            };

            Loaded += GameWindow_Loaded;
            Closed += GameWindow_Closed;
        }

        private void GameWindow_Loaded(object sender, RoutedEventArgs e)
        {
            BuildChessBoard();

            // Legal move hints setup: matchmaking always disables hints; otherwise adheres to room setting
            if (isMatchmaking)
            {
                showMoveHints = false;
            }

            // Set king icons for table headers and player cards
            imgHdrWhiteKing.Source = ImageResources.GetPieceImage("W_King");
            imgHdrBlackKing.Source = ImageResources.GetPieceImage("B_King");
            imgCardPiece1.Source = ImageResources.GetPieceImage("W_King");
            imgCardPiece2.Source = ImageResources.GetPieceImage("B_King");

            UpdateBoardUI();

            // Record initial board position for threefold repetition tracking
            if (!isReplayMode)
            {
                string initFen = FenHelper.BoardToFen(chessGame, currentTurn);
                positionHistory[initFen] = 1;
            }

            // Glow animation timer
            glowAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
            glowAnimationTimer.Tick += (s, ev) =>
            {
                glowAngle += 0.08;
                if (glowAngle > Math.PI * 2) glowAngle -= Math.PI * 2;
                glowPulse = 0.80 + 0.20 * Math.Sin(glowAngle);
                UpdateAvatarGlow();
            };
            glowAnimationTimer.Start();

            string myDisplayName = GetMyDisplayName();

            if (isReplayMode)
            {
                pnlPvPActionButtons.Visibility = Visibility.Collapsed;
                btnBotExit.Visibility = Visibility.Collapsed;
                pnlReplayCustomControls.Visibility = Visibility.Visible;
                btnCustomStockfishHint.Visibility = Visibility.Collapsed;
                btnLeave.Visibility = Visibility.Visible;
                btnLeave.Content = "🚪 Đóng";

                lblClockP1.Text = "--:--";
                lblClockP2.Text = "--:--";

                if (replayRecord != null)
                {
                    lblRoomInfo.Text = $"📜 Xem lại: {replayRecord.gameMode}";
                    lblTurnTimer.Text = "Chế độ xem lại trận đấu";

                    txtName1.Text = !string.IsNullOrWhiteSpace(replayRecord.displayNameWhite) ? replayRecord.displayNameWhite : replayRecord.playerWhite;
                    imgAvatar1.Source = ImageResources.GetAvatarImage(replayRecord.avatarWhite);

                    txtName2.Text = !string.IsNullOrWhiteSpace(replayRecord.displayNameBlack) ? replayRecord.displayNameBlack : replayRecord.playerBlack;
                    imgAvatar2.Source = ImageResources.GetAvatarImage(replayRecord.avatarBlack);

                    // Build replay snapshots
                    replaySnapshots.Clear();
                    replayStepToMoveIndex.Clear();

                    // Step 0: Starting board
                    chessGame.InitializeBoard();
                    replaySnapshots.Add(BoardToDict());
                    replayStepToMoveIndex.Add(0);

                    if (replayRecord.moves != null)
                    {
                        for (int i = 0; i < replayRecord.moves.Count; i++)
                        {
                            var m = replayRecord.moves[i];
                            if (m.boardAfterWhite != null && m.boardAfterWhite.Count > 0)
                            {
                                replaySnapshots.Add(m.boardAfterWhite);
                                replayStepToMoveIndex.Add(i);
                            }
                            if (m.boardAfterBlack != null && m.boardAfterBlack.Count > 0)
                            {
                                replaySnapshots.Add(m.boardAfterBlack);
                                replayStepToMoveIndex.Add(i);
                            }
                        }
                        RenderMoveHistoryUI(replayRecord.moves, showTime: true);
                    }

                    // Start at step 0 — chỉ hiển thị bàn cờ ban đầu, KHÔNG tự động phát.
                    // Người dùng phải bấm nút Play để bắt đầu phát từng nước đi (giống hệt Custom).
                    currentReplayStep = 0;
                    ApplyReplayStep(0);
                    SetReplayPlayPauseHighlight(isPlaying: false);
                }
                return;
            }

            if (isCustomMode)
            {
                pnlPvPActionButtons.Visibility = Visibility.Collapsed;
                btnBotExit.Visibility = Visibility.Collapsed;
                pnlReplayCustomControls.Visibility = Visibility.Visible;
                btnCustomStockfishHint.Visibility = Visibility.Visible;
                btnLeave.Visibility = Visibility.Visible;
                btnLeave.Content = "🚪 Thoát Phòng";

                btnReplayPlay.ToolTip = "Phát lại từ đầu (mỗi 1.25s)";
                btnReplayNext.ToolTip = "Nước đi hiện tại";
                SetReplayPlayPauseHighlight(isPlaying: false);

                colHdrTime.Width = new GridLength(0);
                lblHdrTime.Visibility = Visibility.Collapsed;

                lblClockP1.Text = "--:--";
                lblClockP2.Text = "--:--";

                lblRoomInfo.Text = "🛠️ Phòng Custom (1 người chơi - 2 bên)";
                lblTurnTimer.Text = "Lượt quân Trắng";

                txtName1.Text = $"{myDisplayName} (Trắng)";
                imgAvatar1.Source = ImageResources.GetAvatarImage(myAvatar);

                txtName2.Text = $"{myDisplayName} (Đen)";
                imgAvatar2.Source = ImageResources.GetAvatarImage(myAvatar);

                currentTurn = "white";
                UpdateAvatarGlow();
                InitializeStockfishEvaluation();
                return;
            }

            if (isBotMode)
            {
                // Bot Mode: hide PvP action buttons (Xin Hoà, Đầu Hàng, Tạm Dừng), show exit button
                pnlPvPActionButtons.Visibility = Visibility.Collapsed;
                btnBotExit.Visibility = Visibility.Visible;
                btnLeave.Visibility = Visibility.Collapsed;

                // Hide time column in move history when playing with bot
                colHdrTime.Width = new GridLength(0);
                lblHdrTime.Visibility = Visibility.Collapsed;

                lblClockP1.Text = "--:--";
                lblClockP2.Text = "--:--";

                lblRoomInfo.Text = $"🤖 Đấu với {botName}  •  Bạn: {myDisplayName} ({(myColor == "white" ? "Trắng" : "Đen")})";

                if (myColor == "white")
                {
                    txtName1.Text = $"{myDisplayName} (Bạn)";
                    imgAvatar1.Source = ImageResources.GetAvatarImage(myAvatar);

                    txtName2.Text = botName;
                    imgAvatar2.Source = ImageResources.GetAvatarImage(botAvatar);
                }
                else
                {
                    txtName1.Text = botName;
                    imgAvatar1.Source = ImageResources.GetAvatarImage(botAvatar);

                    txtName2.Text = $"{myDisplayName} (Bạn)";
                    imgAvatar2.Source = ImageResources.GetAvatarImage(myAvatar);
                }

                currentTurn = "white";
                UpdateAvatarGlow();

                // Game duration stopwatch
                gameDurationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                gameDurationTimer.Tick += (s, ev) =>
                {
                    if (isHandlingGameOver || isBotGamePaused) return;
                    if (currentTurn == myColor)
                    {
                        gameElapsedSeconds++;
                    }
                    int mins = gameElapsedSeconds / 60;
                    int secs = gameElapsedSeconds % 60;
                    string turnLabel = currentTurn == myColor ? "Lượt của bạn" : $"Lượt của {botName}";
                    lblTurnTimer.Text = $"⏱️ {mins:D2}:{secs:D2}  •  {turnLabel}";
                    lblTurnTimer.Foreground = currentTurn == myColor ? Brushes.Gold : Brushes.LightGray;
                };
                gameDurationTimer.Start();
                lblTurnTimer.Text = $"⏱️ 00:00  •  Lượt: {(currentTurn == myColor ? "Bạn" : botName)}";

                InitializeStockfishEvaluation();

                if (botColor == "white")
                {
                    _ = TriggerBotTurnAsync();
                }
                return;
            }

            // PvP Online Mode
            pnlPvPActionButtons.Visibility = Visibility.Visible;
            btnBotExit.Visibility = Visibility.Collapsed;
            btnLeave.Visibility = Visibility.Visible;
            btnLeave.Content = "🚪 Rời Trận";

            string matchType = isMatchmaking ? "Ghép trận ELO (10 phút)" : "Phòng giao hữu";
            lblRoomInfo.Text = $"Phòng: {roomId} ({matchType})  •  Bạn: {myDisplayName} ({(myColor == "white" ? "Trắng" : "Đen")})";

            if (myColor == "white")
            {
                txtName1.Text = $"{myDisplayName} (Bạn)";
                imgAvatar1.Source = ImageResources.GetAvatarImage(myAvatar);
            }
            else
            {
                txtName2.Text = $"{myDisplayName} (Bạn)";
                imgAvatar2.Source = ImageResources.GetAvatarImage(myAvatar);
            }

            whiteTurnStartTime = DateTime.UtcNow;

            // Polling timer
            pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            pollTimer.Tick += async (s, ev) => await PollRoomAsync();
            pollTimer.Start();

            // Turn Countdown & Bank Clock timer
            turnCountdownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            turnCountdownTimer.Tick += async (s, ev) => await OnTurnCountdownTickAsync();
            turnCountdownTimer.Start();

            _ = PollRoomAsync();
            InitializeStockfishEvaluation();
        }

        // Bàn cờ có nên đảo ngược hay không: chỉ áp dụng khi người chơi đang cầm quân Đen
        // và tùy chọn "Đảo ngược bàn cờ ở quân đen" đang được bật trong Cài đặt.
        private bool IsBoardFlipped => AppSettings.Current.FlipBoardForBlack && myColor == "black";

        // Chuyển tọa độ logic (row, col) của bàn cờ sang tọa độ hiển thị trên Grid,
        // có tính đến việc đảo ngược bàn cờ (nếu có).
        private (int visualRow, int visualCol) ToVisual(int row, int col)
        {
            return IsBoardFlipped ? (7 - row, 7 - col) : (row, col);
        }

        // Chuyển tọa độ hiển thị trên Grid ngược lại thành tọa độ logic của bàn cờ.
        private (int row, int col) FromVisual(int visualRow, int visualCol)
        {
            return IsBoardFlipped ? (7 - visualRow, 7 - visualCol) : (visualRow, visualCol);
        }

        // Cập nhật lại chữ cái cột (A-H) và số hàng (1-8) quanh bàn cờ cho khớp
        // với chiều hiển thị hiện tại (bình thường hoặc đã đảo ngược).
        private void UpdateBoardCoordinateLabels()
        {
            if (gridFileLabels == null || gridRankLabels == null) return;

            bool flipped = IsBoardFlipped;
            for (int i = 0; i < 8; i++)
            {
                char fileChar = (char)('A' + (flipped ? 7 - i : i));
                if (gridFileLabels.Children[i] is TextBlock fileLbl) fileLbl.Text = fileChar.ToString();

                int rankNum = flipped ? 1 + i : 8 - i;
                if (gridRankLabels.Children[i] is TextBlock rankLbl) rankLbl.Text = rankNum.ToString();
            }
        }

        private void BuildChessBoard()
        {
            chessBoardGrid.Children.Clear();
            chessBoardGrid.RowDefinitions.Clear();
            chessBoardGrid.ColumnDefinitions.Clear();

            UpdateBoardCoordinateLabels();

            for (int i = 0; i < 8; i++)
            {
                chessBoardGrid.RowDefinitions.Add(new RowDefinition());
                chessBoardGrid.ColumnDefinitions.Add(new ColumnDefinition());
            }

            Color lightColor = Color.FromRgb(240, 217, 181);
            Color darkColor = Color.FromRgb(181, 136, 99);

            var boardTheme = AppSettings.Current.BoardColor;
            if (boardTheme == BoardColorTheme.BlackWhite)
            {
                lightColor = Color.FromRgb(240, 240, 240);
                darkColor = Color.FromRgb(64, 64, 64);
            }
            else if (boardTheme == BoardColorTheme.BlueWhite)
            {
                lightColor = Color.FromRgb(222, 240, 255);
                darkColor = Color.FromRgb(75, 138, 196);
            }

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    bool isLight = (r + c) % 2 == 0;
                    Color sqColor = isLight ? lightColor : darkColor;
                    originalSquareColors[r, c] = sqColor;

                    var border = new Border
                    {
                        Background = new SolidColorBrush(sqColor),
                        BorderBrush = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Cursor = Cursors.Hand,
                        Tag = (r, c)
                    };

                    var gridCell = new Grid();

                    var img = new Image
                    {
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(0),
                        RenderTransformOrigin = new Point(0.5, 0.5),
                        RenderTransform = new ScaleTransform(1.22, 1.22),
                        IsHitTestVisible = false
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

                    var dot = new Ellipse
                    {
                        Width = 20,
                        Height = 20,
                        Fill = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Visibility = Visibility.Collapsed,
                        IsHitTestVisible = false
                    };

                    var evalBadgeText = new TextBlock
                    {
                        Text = "",
                        FontSize = 9.5,
                        FontWeight = FontWeights.ExtraBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        IsHitTestVisible = false
                    };

                    var evalBadge = new Border
                    {
                        Width = 19,
                        Height = 19,
                        CornerRadius = new CornerRadius(9.5),
                        Background = new SolidColorBrush(Color.FromRgb(0, 230, 118)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                        BorderThickness = new Thickness(1),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, 2, 2, 0),
                        Visibility = Visibility.Collapsed,
                        IsHitTestVisible = false,
                        Child = evalBadgeText
                    };
                    evalBadge.Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 4,
                        ShadowDepth = 1,
                        Opacity = 0.6
                    };

                    gridCell.Children.Add(img);
                    gridCell.Children.Add(dot);
                    gridCell.Children.Add(evalBadge);
                    border.Child = gridCell;

                    border.MouseLeftButtonDown += CellBorder_MouseLeftButtonDown;
                    border.MouseMove += CellBorder_MouseMove;
                    border.MouseLeftButtonUp += CellBorder_MouseLeftButtonUp;
                    border.MouseRightButtonUp += CellBorder_MouseRightButtonUp;
                    border.LostMouseCapture += CellBorder_LostMouseCapture;
                    border.MouseEnter += CellBorder_MouseEnter;
                    border.MouseLeave += CellBorder_MouseLeave;

                    var (visualRow, visualCol) = ToVisual(r, c);
                    Grid.SetRow(border, visualRow);
                    Grid.SetColumn(border, visualCol);
                    chessBoardGrid.Children.Add(border);

                    cellBorders[r, c] = border;
                    cellImages[r, c] = img;
                    cellDots[r, c] = dot;
                    cellEvalBadges[r, c] = evalBadge;
                    cellEvalBadgeTexts[r, c] = evalBadgeText;
                }
            }

            chessBoardGrid.MouseLeave += ChessBoardGrid_MouseLeave;
        }



        private void UpdateAvatarGlow()
        {
            bool isP1 = string.Equals(currentTurn, "white", StringComparison.OrdinalIgnoreCase);
            bool isP2 = string.Equals(currentTurn, "black", StringComparison.OrdinalIgnoreCase);

            byte alpha = (byte)Math.Clamp((int)(240 * glowPulse), 150, 255);
            Color glowColor = Color.FromArgb(alpha, 0, 230, 118);

            if (isP1)
            {
                avatarP1Border.BorderBrush = new SolidColorBrush(glowColor);
                avatarP1Border.BorderThickness = new Thickness(3);
                avatarP1Border.Effect = new DropShadowEffect { Color = Color.FromRgb(0, 230, 118), BlurRadius = 14 * glowPulse, ShadowDepth = 0, Opacity = 0.8 };

                avatarP2Border.BorderBrush = new SolidColorBrush(Color.FromRgb(84, 80, 122));
                avatarP2Border.BorderThickness = new Thickness(1.5);
                avatarP2Border.Effect = null;
            }
            else if (isP2)
            {
                avatarP2Border.BorderBrush = new SolidColorBrush(glowColor);
                avatarP2Border.BorderThickness = new Thickness(3);
                avatarP2Border.Effect = new DropShadowEffect { Color = Color.FromRgb(0, 230, 118), BlurRadius = 14 * glowPulse, ShadowDepth = 0, Opacity = 0.8 };

                avatarP1Border.BorderBrush = new SolidColorBrush(Color.FromRgb(84, 80, 122));
                avatarP1Border.BorderThickness = new Thickness(1.5);
                avatarP1Border.Effect = null;
            }
        }

        #region Move History & Vietnamese Notation

        private static string FormatMoveSquare(int toRow, int toCol, bool isCastle)
        {
            if (isCastle) return toCol == 6 ? "O-O" : "O-O-O";
            char colChar = (char)('a' + toCol);
            int rowNum = 8 - toRow;
            return $"{colChar}{rowNum}";
        }

        private static string FormatMoveName(string piece, int toRow, int toCol, bool isCastle)
        {
            return FormatMoveSquare(toRow, toCol, isCastle);
        }

        private FrameworkElement CreateMoveCell(
            string pieceCode, string moveText, bool isWhite,
            string? eval = null, string? badge = null, string? colorHex = null, string? bestAlt = null,
            string? commentary = null, Action? onCellClick = null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (string.IsNullOrWhiteSpace(moveText) && string.IsNullOrWhiteSpace(pieceCode))
                return sp;

            if (onCellClick != null)
            {
                sp.Cursor = Cursors.Hand;
                sp.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    onCellClick();
                };
            }

            ImageSource? icon = null;
            if (!string.IsNullOrEmpty(pieceCode))
            {
                icon = ImageResources.GetPieceImage(pieceCode);
            }

            string displayText = moveText ?? "";
            string colorPrefix = isWhite ? "W_" : "B_";

            // Fallback parsing if pieceCode is not set (e.g. legacy "Tốt F4", "Mã G5")
            if (icon == null && !string.IsNullOrWhiteSpace(moveText))
            {
                if (moveText.StartsWith("Tốt ", StringComparison.OrdinalIgnoreCase))
                {
                    icon = ImageResources.GetPieceImage(colorPrefix + "Pawn");
                    displayText = moveText.Substring(4).ToLowerInvariant();
                }
                else if (moveText.StartsWith("Mã ", StringComparison.OrdinalIgnoreCase))
                {
                    icon = ImageResources.GetPieceImage(colorPrefix + "Knight");
                    displayText = moveText.Substring(3).ToLowerInvariant();
                }
                else if (moveText.StartsWith("Tượng ", StringComparison.OrdinalIgnoreCase))
                {
                    icon = ImageResources.GetPieceImage(colorPrefix + "Bishop");
                    displayText = moveText.Substring(6).ToLowerInvariant();
                }
                else if (moveText.StartsWith("Xe ", StringComparison.OrdinalIgnoreCase))
                {
                    icon = ImageResources.GetPieceImage(colorPrefix + "Rook");
                    displayText = moveText.Substring(3).ToLowerInvariant();
                }
                else if (moveText.StartsWith("Hậu ", StringComparison.OrdinalIgnoreCase))
                {
                    icon = ImageResources.GetPieceImage(colorPrefix + "Queen");
                    displayText = moveText.Substring(4).ToLowerInvariant();
                }
                else if (moveText.StartsWith("Vua ", StringComparison.OrdinalIgnoreCase))
                {
                    icon = ImageResources.GetPieceImage(colorPrefix + "King");
                    displayText = moveText.Substring(4).ToLowerInvariant();
                }
                else if (moveText.Contains("Nhập thành", StringComparison.OrdinalIgnoreCase))
                {
                    icon = ImageResources.GetPieceImage(colorPrefix + "King");
                    displayText = moveText.Contains("gần", StringComparison.OrdinalIgnoreCase) ? "O-O" : "O-O-O";
                }
                else if (displayText.Length <= 3)
                {
                    icon = ImageResources.GetPieceImage(colorPrefix + "Pawn");
                }
            }

            if (icon != null)
            {
                var img = new Image
                {
                    Source = icon,
                    Width = 32,
                    Height = 32,
                    Margin = new Thickness(0, 0, 7, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                sp.Children.Add(img);
            }

            var txt = new TextBlock
            {
                Text = displayText,
                Foreground = isWhite ? new SolidColorBrush(Color.FromRgb(255, 215, 0)) : Brushes.WhiteSmoke,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center
            };
            sp.Children.Add(txt);

            // Stockfish Move Quality Badge: Chỉ hiển thị trong phòng Custom hoặc sau khi trận kết thúc (Replay/Review)
            bool canShowStockfishInTable = isCustomMode || isReplayMode;
            if (canShowStockfishInTable && !string.IsNullOrEmpty(badge))
            {
                Color badgeColor;
                try { badgeColor = (Color)ColorConverter.ConvertFromString(colorHex ?? "#00E676"); }
                catch { badgeColor = Color.FromRgb(0, 230, 118); }

                var badgeBorder = new Border
                {
                    Background = new SolidColorBrush(badgeColor),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                bool isDark = badge == "??" || badge == "?";
                var badgeTxt = new TextBlock
                {
                    Text = badge,
                    Foreground = isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(18, 18, 18)),
                    FontWeight = FontWeights.ExtraBold,
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                badgeBorder.Child = badgeTxt;

                sp.Children.Add(badgeBorder);
            }

            // Cell ToolTip with rich evaluation and commentary
            string cellTooltip = isWhite ? $"Quân Trắng: {displayText}" : $"Quân Đen: {displayText}";
            if (canShowStockfishInTable)
            {
                if (!string.IsNullOrEmpty(badge)) cellTooltip += $" [{badge}]";
                if (!string.IsNullOrEmpty(eval)) cellTooltip += $" ({eval})";
                if (!string.IsNullOrEmpty(commentary)) cellTooltip += $"\n\n💬 Bình luận Stockfish:\n{commentary}";
                if (!string.IsNullOrEmpty(bestAlt)) cellTooltip += $"\n\n💡 Nước tối ưu của Stockfish: {bestAlt}";
                cellTooltip += "\n(Bấm để xem bình luận ở bảng phân tích)";
            }
            sp.ToolTip = cellTooltip;

            return sp;
        }

        private void RenderMoveHistoryUI(List<MoveHistoryEntry> history, bool showTime)
        {
            pnlMoveHistory.Children.Clear();
            if (history == null) return;

            lblMoveCount.Text = $"{history.Count} lượt";

            foreach (var item in history)
            {
                var rowBorder = new Border
                {
                    Background = (item.moveNumber % 2 == 0)
                        ? new SolidColorBrush(Color.FromArgb(50, 42, 39, 68))
                        : Brushes.Transparent,
                    Padding = new Thickness(6, 4, 6, 4),
                    CornerRadius = new CornerRadius(4),
                    Margin = new Thickness(0, 1, 0, 1)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = showTime ? new GridLength(90) : new GridLength(0) });

                // Move Number
                var lblNum = new TextBlock
                {
                    Text = $"{item.moveNumber}:",
                    Foreground = new SolidColorBrush(Color.FromRgb(165, 162, 194)),
                    FontWeight = FontWeights.Bold,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(lblNum, 0);
                grid.Children.Add(lblNum);

                var rowItem = item;

                // White Move (Piece Icon + Square + Stockfish Eval Badge + Commentary)
                var cellW = CreateMoveCell(
                    item.whitePiece, item.whiteMove, isWhite: true,
                    item.whiteEval, item.whiteBadge, item.whiteColorHex, item.whiteBestAlternative,
                    item.whiteCommentary,
                    onCellClick: () =>
                    {
                        if (isCustomMode || isReplayMode)
                        {
                            JumpToSubMove(rowItem, isWhite: true, rowBorder);
                        }
                    });
                Grid.SetColumn(cellW, 1);
                grid.Children.Add(cellW);

                // Black Move (Piece Icon + Square + Stockfish Eval Badge + Commentary)
                var cellB = CreateMoveCell(
                    item.blackPiece, item.blackMove, isWhite: false,
                    item.blackEval, item.blackBadge, item.blackColorHex, item.blackBestAlternative,
                    item.blackCommentary,
                    onCellClick: () =>
                    {
                        if (isCustomMode || isReplayMode)
                        {
                            JumpToSubMove(rowItem, isWhite: false, rowBorder);
                        }
                    });
                Grid.SetColumn(cellB, 2);
                grid.Children.Add(cellB);

                if (isCustomMode || isReplayMode)
                {
                    rowBorder.Cursor = Cursors.Hand;
                    rowBorder.ToolTip = "Bấm để quay lại bàn cờ và xem bình luận ở lượt này";
                    rowBorder.MouseLeftButtonUp += (s, e) => JumpToHistoryMove(rowItem, rowBorder);
                }
                else
                {
                    rowBorder.Cursor = Cursors.Arrow;
                    rowBorder.ToolTip = null;
                }

                // Time bar chart: proportional fill based on turnSeconds / totalMaxSeconds
                if (showTime && item.turnSeconds > 0)
                {
                    // Max = 2 players × timeLimitMinutes (default 10 min = 600s each → 1200s total)
                    double maxSeconds = 2.0 * (lastRoom?.timeLimitMinutes ?? 10) * 60.0;
                    double ratio = Math.Clamp(item.turnSeconds / maxSeconds, 0.0, 1.0);

                    // Container: bar track + time label stacked vertically
                    var timePanel = new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Orientation = Orientation.Vertical,
                        Margin = new Thickness(4, 0, 0, 0)
                    };

                    // Bar track (background) using Grid for percentage-based fill
                    var trackBorder = new Border
                    {
                        Height = 8,
                        CornerRadius = new CornerRadius(4),
                        Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                        Margin = new Thickness(0, 0, 0, 2),
                        ClipToBounds = true
                    };

                    var barGrid = new Grid();
                    barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
                    barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1.0 - ratio), GridUnitType.Star) });

                    // Color gradient: fast=green (#00E576) → mid=yellow (#FFD700) → slow=red (#FF5252)
                    byte cr = ratio < 0.3 ? (byte)0   : ratio < 0.6 ? (byte)((ratio - 0.3) / 0.3 * 255) : (byte)255;
                    byte cg = ratio < 0.3 ? (byte)229 : ratio < 0.6 ? (byte)(229 - (ratio - 0.3) / 0.3 * 82) : ratio < 0.8 ? (byte)((1.0 - (ratio - 0.6) / 0.2) * 147) : (byte)82;
                    byte cb = (byte)0;

                    var fillRect = new System.Windows.Shapes.Rectangle
                    {
                        Fill = new SolidColorBrush(Color.FromRgb(cr, cg, cb)),
                        RadiusX = 4,
                        RadiusY = 4
                    };
                    Grid.SetColumn(fillRect, 0);
                    barGrid.Children.Add(fillRect);

                    trackBorder.Child = barGrid;
                    timePanel.Children.Add(trackBorder);

                    // Time label right-aligned
                    var lblTime = new TextBlock
                    {
                        Text = $"{item.turnSeconds:F1}s",
                        Foreground = new SolidColorBrush(Color.FromRgb(0, 229, 255)),
                        FontSize = 10,
                        FontFamily = new FontFamily("Consolas, Courier New"),
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    timePanel.Children.Add(lblTime);

                    Grid.SetColumn(timePanel, 3);
                    grid.Children.Add(timePanel);
                }

                rowBorder.Child = grid;
                pnlMoveHistory.Children.Add(rowBorder);
            }

            scrollMoveHistory.ScrollToEnd();
        }

        #endregion

        #region Bot Mode Logic

        private async Task HandleBotModeCellClick(int row, int col)
        {
            if (isHandlingGameOver || isBotGamePaused) return;

            isProcessingClick = true;
            try
            {
                string[,] boardState = chessGame.GetBoardState();
                string clickedPiece = boardState[row, col];
                bool hasSelection = selectedRow >= 0 && selectedCol >= 0;

                if (!hasSelection)
                {
                    if (string.IsNullOrEmpty(clickedPiece)) return;
                    if (ChessGame.ColorOf(clickedPiece) != myColor) return;

                    selectedRow = row;
                    selectedCol = col;
                    RenderBotModeSelectionAndDots();
                }
                else
                {
                    if (selectedRow == row && selectedCol == col)
                    {
                        selectedRow = -1;
                        selectedCol = -1;
                        RenderBotModeSelectionAndDots();
                        return;
                    }

                    var legalMoves = chessGame.GetLegalMoves(selectedRow, selectedCol);
                    bool isLegalTarget = legalMoves.Any(m => m.row == row && m.col == col);

                    if (isLegalTarget)
                    {
                        if (currentTurn != myColor)
                        {
                            // Chưa đến lượt mình: không thực hiện nước đi
                            return;
                        }

                        int fromR = selectedRow;
                        int fromC = selectedCol;
                        selectedRow = -1;
                        selectedCol = -1;
                        RenderBotModeSelectionAndDots();
                        await ExecuteBotModeMove(fromR, fromC, row, col);
                    }
                    else if (!string.IsNullOrEmpty(clickedPiece) && ChessGame.ColorOf(clickedPiece) == myColor)
                    {
                        selectedRow = row;
                        selectedCol = col;
                        RenderBotModeSelectionAndDots();
                    }
                    else
                    {
                        selectedRow = -1;
                        selectedCol = -1;
                        RenderBotModeSelectionAndDots();
                    }
                }
            }
            finally
            {
                isProcessingClick = false;
            }
        }

        private async Task ExecuteBotModeMove(int fromRow, int fromCol, int toRow, int toCol, bool animate = true)
        {
            hintFromRow = hintFromCol = hintToRow = hintToCol = -1;
            string movingPiece = chessGame.GetBoardState()[fromRow, fromCol];
            // Capture piece image BEFORE the move so we can animate it
            ImageSource? pieceImg = ImageResources.GetPieceImage(movingPiece);

            MoveResult moveResult = chessGame.TryMove(fromRow, fromCol, toRow, toCol);

            string? promoPiece = null;
            if (moveResult.NeedsPromotion)
            {
                var promoWindow = new PromotionWindow(myColor) { Owner = this };
                bool? dr = promoWindow.ShowDialog();
                if (dr != true) return;
                promoPiece = promoWindow.SelectedPiece;
                moveResult = chessGame.TryMove(fromRow, fromCol, toRow, toCol, promoPiece);
            }

            if (!moveResult.Success)
            {
                MessageBox.Show(moveResult.ErrorMessage, "Nước đi không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ClearCellEvaluationBadges();

            // Update last move highlight coordinates
            lastFromRow = fromRow;
            lastFromCol = fromCol;
            lastToRow = toRow;
            lastToCol = toCol;

            // Record move in history
            string formattedMove = FormatMoveSquare(toRow, toCol, moveResult.IsCastle);
            var snap = BoardToDict();
            bool isWhiteSubMove = (myColor == "white");
            if (isWhiteSubMove)
            {
                localMoveHistory.Add(new MoveHistoryEntry
                {
                    moveNumber = localMoveHistory.Count + 1,
                    whiteMove = formattedMove,
                    whitePiece = movingPiece,
                    blackMove = "",
                    blackPiece = "",
                    turnSeconds = 0,
                    boardAfterWhite = snap
                });
            }
            else
            {
                if (localMoveHistory.Count > 0)
                {
                    localMoveHistory[^1].blackMove = formattedMove;
                    localMoveHistory[^1].blackPiece = movingPiece;
                    localMoveHistory[^1].boardAfterBlack = snap;
                }
                else
                {
                    localMoveHistory.Add(new MoveHistoryEntry
                    {
                        moveNumber = 1,
                        whiteMove = "",
                        whitePiece = "",
                        blackMove = formattedMove,
                        blackPiece = movingPiece,
                        boardAfterBlack = snap
                    });
                }
            }

            MoveHistoryEntry targetEntry = localMoveHistory[^1];
            RenderMoveHistoryUI(localMoveHistory, showTime: false);

            // Trigger Stockfish Move Evaluation sequentially
            string playedUci = FenHelper.MoveToUci(fromRow, fromCol, toRow, toCol, promoPiece);
            EnqueuePositionEvaluation(
                isInitial: false,
                movingColor: myColor,
                playedMoveUci: playedUci,
                movingPiece: movingPiece,
                fromRow: fromRow,
                fromCol: fromCol,
                toRow: toRow,
                toCol: toCol,
                promoPiece: promoPiece,
                isCapture: !string.IsNullOrEmpty(moveResult.CapturedPiece),
                isCheck: !string.IsNullOrEmpty(moveResult.CheckedColor) || moveResult.IsCheckmate,
                isCheckmate: moveResult.IsCheckmate,
                capturedPiece: moveResult.CapturedPiece,
                targetEntry: targetEntry,
                isWhiteSubMove: isWhiteSubMove);

            // Animate piece movement (click-to-move only, not drag-drop)
            if (animate && pieceImg != null)
                await AnimatePieceMoveAsync(fromRow, fromCol, toRow, toCol, pieceImg);

            UpdateBoardUI();
            RenderBotModeSelectionAndDots();
            ClearTacticalArrows();

            // Play move sound: check -> warning, capture -> replace, normal -> place
            bool isCapture = !string.IsNullOrEmpty(moveResult.CapturedPiece);
            bool isCheck = !string.IsNullOrEmpty(moveResult.CheckedColor) || moveResult.IsCheckmate;

            if (isCheck)
                SoundService.PlayWarning();
            else if (isCapture)
                SoundService.PlayReplace();
            else
                SoundService.PlayPlace();

            if (moveResult.IsCheckmate)
            {
                isHandlingGameOver = true;
                gameDurationTimer?.Stop();
                glowAnimationTimer?.Stop();

                int mins = gameElapsedSeconds / 60;
                int secs = gameElapsedSeconds % 60;
                string timeFormatted = $"{mins:D2}:{secs:D2}";

                // Attempt to record win to Firebase (gracefully handles offline/network errors)
                string diffKey = botDifficulty.ToString().ToLowerInvariant();
                ChessGame_PJ.Core.BotWinSaveResult saveResult;
                try
                {
                    saveResult = await AuthService.RecordBotWinDetailedAsync(diffKey, gameElapsedSeconds);
                }
                catch
                {
                    saveResult = new ChessGame_PJ.Core.BotWinSaveResult { Status = ChessGame_PJ.Core.BotWinRecordStatus.Failed };
                }

                string bxhMsg = saveResult.Status switch
                {
                    ChessGame_PJ.Core.BotWinRecordStatus.NewRecord =>
                        "\n🏆 KỶ LỤC MỚI! Thời gian của bạn đã được cập nhật lên Bảng Xếp Hạng!",
                    ChessGame_PJ.Core.BotWinRecordStatus.KeptExistingRecord =>
                        $"\n✨ Đã ghi nhận chiến thắng! (Kỷ lục tốt nhất hiện tại của bạn: {saveResult.ExistingBestSeconds / 60:D2}:{saveResult.ExistingBestSeconds % 60:D2})",
                    ChessGame_PJ.Core.BotWinRecordStatus.NotLoggedIn =>
                        "\n(Đăng nhập tài khoản để lưu kỷ lục lên BXH)",
                    _ =>
                        "\n(Chưa thể kết nối máy chủ để lưu BXH)"
                };

                // Lưu lịch sử ván đấu với bot
                var rec = BuildBotGameRecord("win", myColor);
                try
                {
                    _ = HistoryService.SaveGameRecordAsync(rec);
                }
                catch { }

                MessageBox.Show($"🏆 CHIẾU HẾT! BẠN ĐÃ CHIẾN THẮNG {botName.ToUpper()}!\n\n⏱️ Thời gian: {timeFormatted}{bxhMsg}",
                    "Chiến Thắng!", MessageBoxButton.OK, MessageBoxImage.Information);

                EnterPostGameReviewMode(rec);
                return;
            }

            currentTurn = botColor;
            UpdateAvatarGlow();
            RenderBotModeSelectionAndDots();

            if (CheckThreefoldRepetition())
            {
                isHandlingGameOver = true;
                gameDurationTimer?.Stop();
                glowAnimationTimer?.Stop();
                SoundService.PlayWarning();
                MessageBox.Show("Ván cờ kết thúc HOÀ do lặp lại nước đi quá 3 lần (Threefold Repetition)!\nCả hai bên không bị cộng hoặc trừ điểm ELO.",
                    "Hoà cờ - Lặp nước đi", MessageBoxButton.OK, MessageBoxImage.Information);
                var drawRec = BuildBotGameRecord("draw", "draw");
                try { _ = HistoryService.SaveGameRecordAsync(drawRec); } catch { }
                EnterPostGameReviewMode(drawRec);
                return;
            }

            _ = TriggerBotTurnAsync();
        }

        private async Task TriggerBotTurnAsync()
        {
            if (isHandlingGameOver || chessBot == null || isBotGamePaused) return;

            lblStatusTip.Text = $"🤖 {botName} đang tính toán nước đi...";

            await Task.Delay(Random.Shared.Next(400, 800));

            try
            {
                var (fR, fC, tR, tC, promo) = await chessBot.GetBestMoveAsync(chessGame, botColor);

                if (fR < 0)
                {
                    isHandlingGameOver = true;
                    gameDurationTimer?.Stop();
                    glowAnimationTimer?.Stop();

                    if (chessGame.IsInCheck(botColor))
                    {
                        int mins = gameElapsedSeconds / 60;
                        int secs = gameElapsedSeconds % 60;
                        string timeFormatted = $"{mins:D2}:{secs:D2}";
                        string diffKey = botDifficulty.ToString().ToLowerInvariant();

                        ChessGame_PJ.Core.BotWinSaveResult saveResult;
                        try
                        {
                            saveResult = await AuthService.RecordBotWinDetailedAsync(diffKey, gameElapsedSeconds);
                        }
                        catch
                        {
                            saveResult = new ChessGame_PJ.Core.BotWinSaveResult { Status = ChessGame_PJ.Core.BotWinRecordStatus.Failed };
                        }

                        string bxhMsg = saveResult.Status switch
                        {
                            ChessGame_PJ.Core.BotWinRecordStatus.NewRecord =>
                                "\n🏆 KỶ LỤC MỚI! Thời gian của bạn đã được cập nhật lên Bảng Xếp Hạng!",
                            ChessGame_PJ.Core.BotWinRecordStatus.KeptExistingRecord =>
                                $"\n✨ Đã ghi nhận chiến thắng! (Kỷ lục tốt nhất hiện tại của bạn: {saveResult.ExistingBestSeconds / 60:D2}:{saveResult.ExistingBestSeconds % 60:D2})",
                            ChessGame_PJ.Core.BotWinRecordStatus.NotLoggedIn =>
                                "\n(Đăng nhập tài khoản để lưu kỷ lục lên BXH)",
                            _ =>
                                "\n(Chưa thể kết nối máy chủ để lưu BXH)"
                        };

                        MessageBox.Show($"🏆 CHIẾU HẾT! BẠN ĐÃ CHIẾN THẮNG {botName.ToUpper()}!\n\n⏱️ Thời gian: {timeFormatted}{bxhMsg}",
                            "Chiến Thắng!", MessageBoxButton.OK, MessageBoxImage.Information);

                        var winRec = BuildBotGameRecord("win", myColor);
                        try { _ = HistoryService.SaveGameRecordAsync(winRec); } catch { }
                        EnterPostGameReviewMode(winRec);
                    }
                    else
                    {
                        MessageBox.Show("Ván cờ kết thúc với kết quả Hòa (Stalemate)!", "Hòa cờ", MessageBoxButton.OK, MessageBoxImage.Information);

                        var drawRec = BuildBotGameRecord("draw", "draw");
                        try { _ = HistoryService.SaveGameRecordAsync(drawRec); } catch { }
                        EnterPostGameReviewMode(drawRec);
                    }

                    return;
                }

                string botMovingPiece = chessGame.GetBoardState()[fR, fC];
                // Capture piece image before TryMove for animation
                ImageSource? botPieceImg = ImageResources.GetPieceImage(botMovingPiece);
                MoveResult bRes = chessGame.TryMove(fR, fC, tR, tC, promo ?? "Queen");
                ClearCellEvaluationBadges();

                // Update last move highlight coordinates for bot move
                lastFromRow = fR;
                lastFromCol = fC;
                lastToRow = tR;
                lastToCol = tC;

                // Animate bot move
                if (botPieceImg != null)
                    await AnimatePieceMoveAsync(fR, fC, tR, tC, botPieceImg);

                UpdateBoardUI();
                ClearTacticalArrows();

                // Play sound for bot move: check -> warning, capture -> replace, normal -> place
                bool botIsCapture = !string.IsNullOrEmpty(bRes.CapturedPiece);
                bool botIsCheck = !string.IsNullOrEmpty(bRes.CheckedColor) || bRes.IsCheckmate || chessGame.IsInCheck(myColor);

                if (botIsCheck)
                    SoundService.PlayWarning();
                else if (botIsCapture)
                    SoundService.PlayReplace();
                else
                    SoundService.PlayPlace();

                // Record bot move in history
                hintFromRow = hintFromCol = hintToRow = hintToCol = -1;
                string botFormattedMove = FormatMoveSquare(tR, tC, bRes.IsCastle);
                var botSnap = BoardToDict();
                bool isWhiteSubMove = (botColor == "white");
                if (isWhiteSubMove)
                {
                    localMoveHistory.Add(new MoveHistoryEntry
                    {
                        moveNumber = localMoveHistory.Count + 1,
                        whiteMove = botFormattedMove,
                        whitePiece = botMovingPiece,
                        blackMove = "",
                        blackPiece = "",
                        turnSeconds = 0,
                        boardAfterWhite = botSnap
                    });
                }
                else
                {
                    if (localMoveHistory.Count > 0)
                    {
                        localMoveHistory[^1].blackMove = botFormattedMove;
                        localMoveHistory[^1].blackPiece = botMovingPiece;
                        localMoveHistory[^1].boardAfterBlack = botSnap;
                    }
                    else
                    {
                        localMoveHistory.Add(new MoveHistoryEntry
                        {
                            moveNumber = 1,
                            whiteMove = "",
                            whitePiece = "",
                            blackMove = botFormattedMove,
                            blackPiece = botMovingPiece,
                            boardAfterBlack = botSnap
                        });
                    }
                }

                MoveHistoryEntry targetEntry = localMoveHistory[^1];
                RenderMoveHistoryUI(localMoveHistory, showTime: false);

                // Trigger Stockfish Move Evaluation for Bot Move sequentially
                string botUci = FenHelper.MoveToUci(fR, fC, tR, tC, promo);
                EnqueuePositionEvaluation(
                    isInitial: false,
                    movingColor: botColor,
                    playedMoveUci: botUci,
                    movingPiece: botMovingPiece,
                    fromRow: fR,
                    fromCol: fC,
                    toRow: tR,
                    toCol: tC,
                    promoPiece: promo,
                    isCapture: !string.IsNullOrEmpty(bRes.CapturedPiece),
                    isCheck: !string.IsNullOrEmpty(bRes.CheckedColor) || bRes.IsCheckmate,
                    isCheckmate: bRes.IsCheckmate,
                    capturedPiece: bRes.CapturedPiece,
                    targetEntry: targetEntry,
                    isWhiteSubMove: isWhiteSubMove);

                if (bRes.IsCheckmate)
                {
                    isHandlingGameOver = true;
                    gameDurationTimer?.Stop();
                    glowAnimationTimer?.Stop();

                    MessageBox.Show($"Chiếu hết! {botName} đã giành chiến thắng.", "Kết Thúc Ván Đấu", MessageBoxButton.OK, MessageBoxImage.Information);

                    var lossRec = BuildBotGameRecord("loss", botColor);
                    try { _ = HistoryService.SaveGameRecordAsync(lossRec); } catch { }
                    EnterPostGameReviewMode(lossRec);
                    return;
                }

                currentTurn = myColor;
                UpdateAvatarGlow();
                RenderBotModeSelectionAndDots();

                if (CheckThreefoldRepetition())
                {
                    isHandlingGameOver = true;
                    gameDurationTimer?.Stop();
                    glowAnimationTimer?.Stop();
                    SoundService.PlayWarning();
                    MessageBox.Show("Ván cờ kết thúc HOÀ do lặp lại nước đi quá 3 lần (Threefold Repetition)!\nCả hai bên không bị cộng hoặc trừ điểm ELO.",
                        "Hoà cờ - Lặp nước đi", MessageBoxButton.OK, MessageBoxImage.Information);
                    var drawRec = BuildBotGameRecord("draw", "draw");
                    try { _ = HistoryService.SaveGameRecordAsync(drawRec); } catch { }
                    EnterPostGameReviewMode(drawRec);
                    return;
                }

                if (bRes.CheckedColor == myColor)
                {
                    lblStatusTip.Text = "⚠️ Bạn đang bị chiếu tướng!";
                }
                else
                {
                    lblStatusTip.Text = "Đến lượt của bạn. Chọn quân để di chuyển.";
                }
            }
            catch (Exception ex)
            {
                lblStatusTip.Text = "Lỗi xử lý nước đi của máy: " + ex.Message;
            }
        }

        private void RenderBotModeSelectionAndDots()
        {
            string[,] boardState = chessGame.GetBoardState();

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    var border = cellBorders[r, c];
                    border.BorderBrush = Brushes.Transparent;
                    border.BorderThickness = new Thickness(0);

                    // Yellow highlight for last move
                    bool isLastMove = (r == lastFromRow && c == lastFromCol) || (r == lastToRow && c == lastToCol);
                    if (isLastMove)
                    {
                        bool isLight = (r + c) % 2 == 0;
                        border.Background = new SolidColorBrush(isLight ? Color.FromRgb(246, 236, 114) : Color.FromRgb(218, 196, 75));
                    }
                    else
                    {
                        border.Background = new SolidColorBrush(originalSquareColors[r, c]);
                    }

                    cellDots[r, c].Visibility = Visibility.Collapsed;
                }
            }

            // Stockfish best move hint highlight
            if (hintFromRow >= 0 && hintFromCol >= 0 && hintToRow >= 0 && hintToCol >= 0)
            {
                var fBorder = cellBorders[hintFromRow, hintFromCol];
                fBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 215, 0));
                fBorder.BorderThickness = new Thickness(3.5);

                var tBorder = cellBorders[hintToRow, hintToCol];
                tBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 229, 255));
                tBorder.BorderThickness = new Thickness(3.5);
                tBorder.Background = new SolidColorBrush(Color.FromArgb(90, 0, 229, 255));
            }

            if (selectedRow >= 0 && selectedCol >= 0)
            {
                var selBorder = cellBorders[selectedRow, selectedCol];
                selBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                selBorder.BorderThickness = new Thickness(3.5);

                if (showMoveHints && (isCustomMode || currentTurn == myColor))
                {
                    var legalMoves = chessGame.GetLegalMoves(selectedRow, selectedCol);
                    foreach (var (mr, mc) in legalMoves)
                    {
                        string targetPiece = boardState[mr, mc];
                        if (string.IsNullOrEmpty(targetPiece))
                        {
                            cellDots[mr, mc].Visibility = Visibility.Visible;
                        }
                        else
                        {
                            cellBorders[mr, mc].BorderBrush = Brushes.Red;
                            cellBorders[mr, mc].BorderThickness = new Thickness(2.5);
                        }
                    }
                }
            }

            // Custom mode Alt-click red highlight
            if (isCustomMode && customRedHighlightedSquares.Count > 0)
            {
                foreach (var (rr, rc) in customRedHighlightedSquares)
                {
                    if (rr >= 0 && rr < 8 && rc >= 0 && rc < 8)
                    {
                        cellBorders[rr, rc].Background = new SolidColorBrush(Color.FromArgb(170, 244, 67, 54));
                    }
                }
            }

            // Custom mode Ctrl-click arrow start piece highlight and legal move dots
            if (isCustomMode && ctrlArrowStartSquare.HasValue)
            {
                var (cr, cc) = ctrlArrowStartSquare.Value;
                var cBorder = cellBorders[cr, cc];
                cBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 140, 0));
                cBorder.BorderThickness = new Thickness(3.5);

                var legalMoves = chessGame.GetLegalMoves(cr, cc);
                foreach (var (mr, mc) in legalMoves)
                {
                    string targetPiece = boardState[mr, mc];
                    if (string.IsNullOrEmpty(targetPiece))
                    {
                        cellDots[mr, mc].Visibility = Visibility.Visible;
                    }
                    else
                    {
                        cellBorders[mr, mc].BorderBrush = new SolidColorBrush(Color.FromRgb(255, 140, 0));
                        cellBorders[mr, mc].BorderThickness = new Thickness(2.5);
                    }
                }
            }

            // King in check highlight
            if (chessGame.IsInCheck(currentTurn))
            {
                var (kr, kc) = chessGame.FindKing(currentTurn, chessGame.GetBoardState());
                if (kr >= 0)
                {
                    cellBorders[kr, kc].Background = new SolidColorBrush(Color.FromRgb(220, 50, 50));
                }
            }

            // Re-apply hover highlight after full re-render
            if (hoverRow >= 0 && hoverCol >= 0)
                ApplyHoverHighlight(hoverRow, hoverCol, true);

            // Redraw custom arrows if any exist
            if (isCustomMode && customArrows.Count > 0)
            {
                RedrawCustomAnnotations();
            }
        }

        #endregion

        #region PvP Online Mode Logic

        private async Task PollRoomAsync()
        {
            if (isBusy || string.IsNullOrEmpty(roomId) || isHandlingGameOver) return;
            isBusy = true;
            try
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var hbPatch = new Dictionary<string, object>
                {
                    [myColor == "white" ? "lastHeartbeat1" : "lastHeartbeat2"] = now
                };
                _ = FirebaseClient.PatchAsync($"rooms/{roomId}", hbPatch);

                RoomData? room = await FirebaseClient.GetAsync<RoomData>($"rooms/{roomId}");
                if (room == null) { isBusy = false; return; }

                bool isOpponentMove = lastRoom != null &&
                                      (room.lastFromRow >= 0 && room.lastFromCol >= 0 && room.lastToRow >= 0 && room.lastToCol >= 0) &&
                                      (room.lastFromRow != lastFromRow || room.lastFromCol != lastFromCol || room.lastToRow != lastToRow || room.lastToCol != lastToCol);

                ImageSource? oppPieceImg = null;
                int oFromR = room.lastFromRow, oFromC = room.lastFromCol, oToR = room.lastToRow, oToC = room.lastToCol;
                if (isOpponentMove)
                {
                    string? pieceCode = chessGame.GetBoardState()[oFromR, oFromC];
                    if (string.IsNullOrEmpty(pieceCode) && room.board != null)
                    {
                        room.board.TryGetValue($"{oToR}_{oToC}", out pieceCode);
                    }
                    if (!string.IsNullOrEmpty(pieceCode))
                    {
                        oppPieceImg = ImageResources.GetPieceImage(pieceCode);
                    }
                }

                bool oppIsCapture = false;
                if (isOpponentMove && oToR >= 0 && oToR < 8 && oToC >= 0 && oToC < 8)
                {
                    oppIsCapture = !string.IsNullOrEmpty(chessGame.GetBoardState()[oToR, oToC]);
                }

                lastRoom = room;
                ApplyRoomToLocalGame(room);

                if (isOpponentMove && oppPieceImg != null)
                {
                    await AnimatePieceMoveAsync(oFromR, oFromC, oToR, oToC, oppPieceImg);
                }

                if (isOpponentMove)
                {
                    ClearTacticalArrows();
                    bool oppIsCheck = room.check == myColor || !string.IsNullOrEmpty(room.check) || room.status == "checkmate";
                    if (oppIsCheck)
                        SoundService.PlayWarning();
                    else if (oppIsCapture)
                        SoundService.PlayReplace();
                    else
                        SoundService.PlayPlace();

                    if (room.status == "playing" && !isHandlingGameOver && CheckThreefoldRepetition(room.turn))
                    {
                        isHandlingGameOver = true;
                        pollTimer?.Stop();
                        turnCountdownTimer?.Stop();
                        await FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object>
                        {
                            ["status"] = "draw",
                            ["updatedAt"] = now
                        });
                        SoundService.PlayWarning();
                        MessageBox.Show("Ván cờ kết thúc HOÀ do lặp lại nước đi quá 3 lần (Threefold Repetition)!\nCả hai bên không bị cộng hoặc trừ điểm ELO.",
                            "Hoà cờ - Lặp nước đi", MessageBoxButton.OK, MessageBoxImage.Information);
                        await ProcessGameDrawAsync(room);
                        return;
                    }
                }

                // Handle Pause State
                if (room.isPaused)
                {
                    pnlPauseOverlay.Visibility = Visibility.Visible;
                    long remainingMs = Math.Max(0, room.pauseExpiresAt - now);
                    int pauseSecs = (int)(remainingMs / 1000);
                    lblPauseCountdown.Text = $"Thời gian tạm dừng còn lại: {pauseSecs / 60:D2}:{pauseSecs % 60:D2}";
                    if (pauseSecs <= 0)
                    {
                        // Auto-resume when pause time expires
                        _ = FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object>
                        {
                            ["isPaused"] = false,
                            ["pauseExpiresAt"] = 0
                        });
                        pnlPauseOverlay.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    pnlPauseOverlay.Visibility = Visibility.Collapsed;
                }

                // Handle Pause Request by Opponent
                if (!string.IsNullOrEmpty(room.pauseRequestedBy) && room.pauseRequestedBy != myColor && room.pauseRequestedBy != "rejected")
                {
                    string oppColorName = room.pauseRequestedBy == "white" ? "Quân Trắng" : "Quân Đen";
                    int mins = room.pauseRequestedMinutes;

                    var confirm = MessageBox.Show($"Đối thủ ({oppColorName}) muốn xin tạm dừng trận đấu trong {mins} phút.\nBạn có đồng ý không?",
                        "Yêu cầu tạm dừng", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (confirm == MessageBoxResult.Yes)
                    {
                        long expires = now + mins * 60 * 1000;
                        await FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object>
                        {
                            ["isPaused"] = true,
                            ["pauseExpiresAt"] = expires,
                            ["pauseRequestedBy"] = ""
                        });
                    }
                    else
                    {
                        await FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object>
                        {
                            ["pauseRequestedBy"] = "rejected"
                        });
                    }
                }
                else if (room.pauseRequestedBy == "rejected")
                {
                    _ = FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object> { ["pauseRequestedBy"] = "" });
                    MessageBox.Show("Đối thủ đã từ chối yêu cầu tạm dừng.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                // Handle Draw Offer by Opponent
                if (!string.IsNullOrEmpty(room.drawOffer) && room.drawOffer != myColor && room.drawOffer != "rejected")
                {
                    string oppColorName = room.drawOffer == "white" ? "Quân Trắng" : "Quân Đen";
                    var confirmDraw = MessageBox.Show($"Đối thủ ({oppColorName}) gửi lời xin hoà ván cờ.\nBạn có đồng ý kết thúc hoà không?\n(Cả 2 bên đều không bị cộng hoặc trừ điểm ELO)",
                        "Lời mời hoà cờ", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (confirmDraw == MessageBoxResult.Yes)
                    {
                        await FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object>
                        {
                            ["status"] = "draw",
                            ["winner"] = "",
                            ["drawOffer"] = "",
                            ["updatedAt"] = now
                        });

                        await ProcessGameDrawAsync(room);
                        return;
                    }
                    else
                    {
                        await FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object>
                        {
                            ["drawOffer"] = "rejected"
                        });
                    }
                }
                else if (room.drawOffer == "rejected")
                {
                    _ = FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object> { ["drawOffer"] = "" });
                    MessageBox.Show("Đối thủ đã từ chối lời xin hoà cờ.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                if (room.turn != myColor)
                {
                    selectedRow = -1;
                    selectedCol = -1;
                }
                else if (selectedRow >= 0 && selectedCol >= 0)
                {
                    string piece = chessGame.GetBoardState()[selectedRow, selectedCol];
                    if (string.IsNullOrEmpty(piece) || ChessGame.ColorOf(piece) != myColor)
                    {
                        selectedRow = -1;
                        selectedCol = -1;
                    }
                }

                UpdateBoardUI();
                RenderSelectionAndDots(room);
                UpdatePlayerLabels(room);
                RenderMoveHistoryUI(room.moveHistory, showTime: true);

                if (!string.IsNullOrEmpty(room.turn))
                {
                    currentTurn = room.turn;
                }

                // Check disconnect
                if (room.status == "playing" && !string.IsNullOrEmpty(room.player2) && !isHandlingGameOver)
                {
                    long opponentHeartbeat = myColor == "white" ? room.lastHeartbeat2 : room.lastHeartbeat1;
                    if (opponentHeartbeat > 0 && (now - opponentHeartbeat) > 10000)
                    {
                        isHandlingGameOver = true;
                        pollTimer?.Stop();
                        turnCountdownTimer?.Stop();

                        string winner = myColor;
                        await FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object>
                        {
                            ["status"] = "opponent_disconnected",
                            ["winner"] = winner,
                            ["updatedAt"] = now
                        });

                        string eloNote = room.isMatchmaking ? " (+10 ELO)" : "";
                        await ProcessGameOverAsync(room, winner, $"Đối thủ đã mất kết nối mạng! Bạn giành chiến thắng{eloNote}!");
                        return;
                    }
                }

                // Check game over
                if ((room.status == "checkmate" || room.status == "player_left" || room.status == "opponent_disconnected" ||
                     room.status == "resigned" || room.status == "timeout_afk" || room.status == "bank_timeout") && !isHandlingGameOver)
                {
                    isHandlingGameOver = true;
                    pollTimer?.Stop();
                    turnCountdownTimer?.Stop();

                    string eloWin = room.isMatchmaking ? " (+10 ELO)" : " (Giao hữu)";
                    string eloLoss = room.isMatchmaking ? " (-10 ELO)" : " (Giao hữu)";

                    string msg = room.status switch
                    {
                        "player_left" => (room.winner == myColor ? $"Đối thủ đã rời trận đấu! Bạn giành chiến thắng{eloWin}!" : $"Bạn đã rời trận đấu{eloLoss}!"),
                        "opponent_disconnected" => (room.winner == myColor ? $"Đối thủ mất kết nối! Bạn giành chiến thắng{eloWin}!" : $"Mất kết nối{eloLoss}!"),
                        "resigned" => (room.winner == myColor ? $"Đối thủ đã đầu hàng! Bạn giành chiến thắng{eloWin}!" : $"Bạn đã đầu hàng{eloLoss}!"),
                        "timeout_afk" => (room.winner == myColor ? $"Đối thủ quá 1'30s không đi nước nào! Bạn giành chiến thắng{eloWin}!" : $"Bạn quá 1'30s không đi nước nào nên bị xử thua{eloLoss}!"),
                        "bank_timeout" => (room.winner == myColor ? $"Đối thủ đã hết tổng thời gian ván đấu! Bạn giành chiến thắng{eloWin}!" : $"Bạn đã hết tổng thời gian ván đấu nên bị xử thua{eloLoss}!"),
                        _ => (room.winner == myColor ? $"Chiếu hết! Bạn đã chiến thắng{eloWin}!" : $"Chiếu hết! Bạn đã thua trận{eloLoss}!")
                    };

                    await ProcessGameOverAsync(room, room.winner, msg);
                    return;
                }
                else if (room.status == "draw" && !isHandlingGameOver)
                {
                    isHandlingGameOver = true;
                    pollTimer?.Stop();
                    turnCountdownTimer?.Stop();
                    await ProcessGameDrawAsync(room);
                    return;
                }
            }
            finally
            {
                isBusy = false;
            }
        }

        /// <summary>
        /// Tạo GameRecord cho một ván Đấu Máy vừa kết thúc (dùng để lưu lịch sử và mở chế độ Xem lại tại chỗ).
        /// </summary>
        private GameRecord BuildBotGameRecord(string result, string winner)
        {
            string diffKey = botDifficulty.ToString().ToLowerInvariant();
            return new GameRecord
            {
                gameId = Guid.NewGuid().ToString("N"),
                playerWhite = (myColor == "white") ? playerName : $"bot_{diffKey}",
                playerBlack = (myColor == "black") ? playerName : $"bot_{diffKey}",
                displayNameWhite = (myColor == "white") ? GetMyDisplayName() : botName,
                displayNameBlack = (myColor == "black") ? GetMyDisplayName() : botName,
                avatarWhite = (myColor == "white") ? myAvatar : "avatar4.png",
                avatarBlack = (myColor == "black") ? myAvatar : "avatar4.png",
                gameMode = "Đấu máy",
                result = result,
                winner = winner,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                moves = new List<MoveHistoryEntry>(localMoveHistory)
            };
        }

        private async Task ProcessGameOverAsync(RoomData room, string winnerColor, string message)
        {
            GameRecord rec = new GameRecord
            {
                gameId = Guid.NewGuid().ToString("N"),
                playerWhite = room.player1,
                playerBlack = room.player2,
                displayNameWhite = !string.IsNullOrWhiteSpace(room.displayName1) ? room.displayName1 : room.player1,
                displayNameBlack = !string.IsNullOrWhiteSpace(room.displayName2) ? room.displayName2 : room.player2,
                avatarWhite = !string.IsNullOrWhiteSpace(room.avatar1) ? room.avatar1 : "avatar1.png",
                avatarBlack = !string.IsNullOrWhiteSpace(room.avatar2) ? room.avatar2 : "avatar1.png",
                gameMode = room.isMatchmaking ? "Xếp hạng" : "Giao hữu",
                result = (winnerColor == myColor) ? "win" : "loss",
                winner = winnerColor,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                moves = room.moveHistory != null ? new List<MoveHistoryEntry>(room.moveHistory) : new List<MoveHistoryEntry>(localMoveHistory)
            };

            try
            {
                if (!room.scoreAwarded)
                {
                    await FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object>
                    {
                        ["scoreAwarded"] = true
                    });

                    string winnerName = winnerColor == "white" ? room.player1 : room.player2;
                    string loserName = winnerColor == "white" ? room.player2 : room.player1;

                    await AuthService.RecordGameResultAsync(winnerName, loserName, isMatchmaking: room.isMatchmaking, points: 10);

                    // Lưu lịch sử ván đấu
                    try
                    {
                        _ = HistoryService.SaveGameRecordAsync(rec);
                    }
                    catch { }
                }
            }
            catch { }

            MessageBox.Show(message, "Kết thúc ván đấu", MessageBoxButton.OK, MessageBoxImage.Information);

            try
            {
                await FirebaseClient.DeleteAsync($"rooms/{roomId}");
            }
            catch { }

            EnterPostGameReviewMode(rec);
        }

        private async Task ProcessGameDrawAsync(RoomData room)
        {
            GameRecord rec = new GameRecord
            {
                gameId = Guid.NewGuid().ToString("N"),
                playerWhite = room.player1,
                playerBlack = room.player2,
                displayNameWhite = !string.IsNullOrWhiteSpace(room.displayName1) ? room.displayName1 : room.player1,
                displayNameBlack = !string.IsNullOrWhiteSpace(room.displayName2) ? room.displayName2 : room.player2,
                avatarWhite = !string.IsNullOrWhiteSpace(room.avatar1) ? room.avatar1 : "avatar1.png",
                avatarBlack = !string.IsNullOrWhiteSpace(room.avatar2) ? room.avatar2 : "avatar1.png",
                gameMode = room.isMatchmaking ? "Xếp hạng" : "Giao hữu",
                result = "draw",
                winner = "draw",
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                moves = room.moveHistory != null ? new List<MoveHistoryEntry>(room.moveHistory) : new List<MoveHistoryEntry>(localMoveHistory)
            };

            try
            {
                if (!room.scoreAwarded)
                {
                    await FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object>
                    {
                        ["scoreAwarded"] = true
                    });

                    await AuthService.RecordDrawResultAsync(room.player1, room.player2);

                    // Lưu lịch sử ván đấu hoà
                    try
                    {
                        _ = HistoryService.SaveGameRecordAsync(rec);
                    }
                    catch { }
                }
            }
            catch { }

            MessageBox.Show("Ván cờ kết thúc với kết quả HOÀ!\nCả hai bên không bị cộng hoặc trừ điểm ELO.",
                "Hoà cờ", MessageBoxButton.OK, MessageBoxImage.Information);

            try
            {
                await FirebaseClient.DeleteAsync($"rooms/{roomId}");
            }
            catch { }

            EnterPostGameReviewMode(rec);
        }

        private async Task OnTurnCountdownTickAsync()
        {
            if (isHandlingGameOver || lastRoom == null) return;

            if (lastRoom.status != "playing" || string.IsNullOrEmpty(lastRoom.player2) || lastRoom.isPaused)
            {
                if (lastRoom != null && !lastRoom.isPaused)
                {
                    lblTurnTimer.Text = "⏳ Đang đợi người chơi...";
                    lblTurnTimer.Foreground = Brushes.LightGray;
                }
                return;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long started = lastRoom.turnStartedAt > 0 ? lastRoom.turnStartedAt : now;
            int elapsedTurn = (int)((now - started) / 1000);
            int turnRemaining = Math.Max(0, PerTurnTimeLimitSeconds - elapsedTurn);

            // Calculate bank clocks
            double wBank = lastRoom.timeLeftWhite;
            double bBank = lastRoom.timeLeftBlack;

            if (lastRoom.turn == "white")
            {
                wBank = Math.Max(0, wBank - elapsedTurn);
            }
            else
            {
                bBank = Math.Max(0, bBank - elapsedTurn);
            }

            int wM = (int)wBank / 60; int wS = (int)wBank % 60;
            int bM = (int)bBank / 60; int bS = (int)bBank % 60;

            lblClockP1.Text = $"{wM:D2}:{wS:D2}";
            lblClockP2.Text = $"{bM:D2}:{bS:D2}";

            bool isMyTurn = lastRoom.turn == myColor;
            lblTurnTimer.Text = isMyTurn ? $"⏱️ Lượt bạn: {turnRemaining}s (Hết 90s tính thua)" : $"⏱️ Lượt đối thủ: {turnRemaining}s";
            lblTurnTimer.Foreground = turnRemaining <= 15 ? Brushes.OrangeRed : Brushes.Gold;

            // Countdown beep when 10 seconds or less remain on player's turn
            if (isMyTurn && !isHandlingGameOver)
            {
                double myBank = myColor == "white" ? wBank : bBank;
                int remainingSecs = Math.Min(turnRemaining, (int)Math.Ceiling(myBank));
                if (remainingSecs <= 10 && remainingSecs > 0)
                {
                    if (lastBeepSecond != remainingSecs)
                    {
                        lastBeepSecond = remainingSecs;
                        SoundService.PlayBeep();
                    }
                }
                else
                {
                    lastBeepSecond = -1;
                }
            }
            else
            {
                lastBeepSecond = -1;
            }

            // Check Bank Clock Expiration (Hết 10/15/20 phút)
            if (isMyTurn && ((myColor == "white" && wBank <= 0) || (myColor == "black" && bBank <= 0)) && !isTimeoutHandling)
            {
                isTimeoutHandling = true;
                string loserColor = myColor;
                string winnerColor = myColor == "white" ? "black" : "white";

                await FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object>
                {
                    ["status"] = "bank_timeout",
                    ["winner"] = winnerColor,
                    ["updatedAt"] = now
                });
                return;
            }

            // Check 90s Turn Inactivity Timeout (Quá 1'30s tính thua luôn)
            if (turnRemaining == 0 && isMyTurn && !isTimeoutHandling)
            {
                isTimeoutHandling = true;
                string loserColor = myColor;
                string winnerColor = myColor == "white" ? "black" : "white";

                await FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object>
                {
                    ["status"] = "timeout_afk",
                    ["winner"] = winnerColor,
                    ["updatedAt"] = now
                });
            }
        }

        private void UpdatePlayerLabels(RoomData room)
        {
            // Show display names in UI (fall back to username if displayName not set)
            string dn1 = !string.IsNullOrEmpty(room.displayName1) ? room.displayName1 : room.player1;
            string dn2 = !string.IsNullOrEmpty(room.displayName2) ? room.displayName2 : room.player2;

            string p1Text = !string.IsNullOrEmpty(dn1) ? dn1 : "Người chơi 1";
            string p2Text = !string.IsNullOrEmpty(dn2) ? dn2 : "Đang đợi...";

            if (myColor == "white")
            {
                p1Text += " (Bạn)";
            }
            else if (myColor == "black" && !string.IsNullOrEmpty(room.player2))
            {
                p2Text += " (Bạn)";
            }

            txtName1.Text = p1Text;
            txtName2.Text = p2Text;

            // Khi chưa có đối thủ vào phòng giao hữu -> gợi ý chuột phải để mời bạn bè
            if (!isBotMode && !isCustomMode && !isReplayMode && !isMatchmaking && string.IsNullOrEmpty(room.player2))
            {
                cardP2.ToolTip = "Nhấp chuột phải để mời bạn bè vào phòng";
            }
            else
            {
                cardP2.ToolTip = "Nhấp chuột phải để xem thông tin người chơi";
            }

            if (!string.IsNullOrEmpty(room.avatar1))
            {
                imgAvatar1.Source = ImageResources.GetAvatarImage(room.avatar1);
            }
            if (!string.IsNullOrEmpty(room.avatar2))
            {
                imgAvatar2.Source = ImageResources.GetAvatarImage(room.avatar2);
            }
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

            if (room.lastFromRow >= 0 && room.lastFromCol >= 0 && room.lastToRow >= 0 && room.lastToCol >= 0)
            {
                lastFromRow = room.lastFromRow;
                lastFromCol = room.lastFromCol;
                lastToRow = room.lastToRow;
                lastToCol = room.lastToCol;
            }

            if (!isMatchmaking)
            {
                showMoveHints = room.showMoveHints;
            }
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

        // Flag to prevent input during piece animation
        private bool isAnimatingMove = false;

        /// <summary>
        /// Animates a piece flying from (fromRow,fromCol) to (toRow,toCol) over 0.25s.
        /// Hides the source cell image during flight, then calls UpdateBoardUI on completion.
        /// Only used for click-to-move and bot/opponent moves (drag-drop stays instant).
        /// </summary>
        private async Task AnimatePieceMoveAsync(int fromRow, int fromCol, int toRow, int toCol, ImageSource pieceImg)
        {
            isAnimatingMove = true;
            try
            {
                double cellW = chessBoardGrid.ActualWidth > 0 ? chessBoardGrid.ActualWidth / 8.0 : 70.0;
                double cellH = chessBoardGrid.ActualHeight > 0 ? chessBoardGrid.ActualHeight / 8.0 : 70.0;

                var (visFromRow, visFromCol) = ToVisual(fromRow, fromCol);
                var (visToRow, visToCol) = ToVisual(toRow, toCol);

                double fromLeft = visFromCol * cellW;
                double fromTop  = visFromRow * cellH;
                double toLeft   = visToCol * cellW;
                double toTop    = visToRow * cellH;

                // Prepare the floating image on the dragDropCanvas
                imgDraggedPiece.Source = pieceImg;
                Canvas.SetLeft(imgDraggedPiece, fromLeft);
                Canvas.SetTop(imgDraggedPiece, fromTop);
                imgDraggedPiece.Visibility = Visibility.Visible;

                // Hide source cell piece during flight
                cellImages[fromRow, fromCol].Opacity = 0.0;

                var tcs = new TaskCompletionSource<bool>();

                var animLeft = new DoubleAnimation(fromLeft, toLeft, new Duration(TimeSpan.FromSeconds(0.25)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var animTop = new DoubleAnimation(fromTop, toTop, new Duration(TimeSpan.FromSeconds(0.25)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                // Resolve once the Left animation finishes (both finish at same time)
                animLeft.Completed += (s, e) => tcs.TrySetResult(true);

                imgDraggedPiece.BeginAnimation(Canvas.LeftProperty, animLeft);
                imgDraggedPiece.BeginAnimation(Canvas.TopProperty, animTop);

                await tcs.Task;

                // Cleanup: hide floating piece, restore source cell opacity
                imgDraggedPiece.Visibility = Visibility.Collapsed;
                imgDraggedPiece.BeginAnimation(Canvas.LeftProperty, null);
                imgDraggedPiece.BeginAnimation(Canvas.TopProperty, null);
                cellImages[fromRow, fromCol].Opacity = 1.0;
            }
            finally
            {
                isAnimatingMove = false;
            }
        }

        private void UpdateBoardUI()
        {
            string[,] boardState = chessGame.GetBoardState();

            for (int row = 0; row < 8; row++)
            {
                for (int col = 0; col < 8; col++)
                {
                    string pieceValue = boardState[row, col];
                    Image img = cellImages[row, col];
                    if (string.IsNullOrEmpty(pieceValue))
                    {
                        img.Source = null;
                    }
                    else
                    {
                        img.Source = ImageResources.GetPieceImage(pieceValue);
                    }
                }
            }

            UpdateCapturedPiecesUI();
        }

        #region Captured Pieces Display

        private static (List<string> whiteCaptured, List<string> blackCaptured) GetCapturedPieces(string[,] board)
        {
            var blackPool = new Dictionary<string, int>
            {
                ["B_Queen"] = 1,
                ["B_Rook"] = 2,
                ["B_Bishop"] = 2,
                ["B_Knight"] = 2,
                ["B_Pawn"] = 8
            };

            var whitePool = new Dictionary<string, int>
            {
                ["W_Queen"] = 1,
                ["W_Rook"] = 2,
                ["W_Bishop"] = 2,
                ["W_Knight"] = 2,
                ["W_Pawn"] = 8
            };

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    string p = board[r, c];
                    if (string.IsNullOrEmpty(p)) continue;
                    if (blackPool.ContainsKey(p)) blackPool[p]--;
                    if (whitePool.ContainsKey(p)) whitePool[p]--;
                }
            }

            var whiteCaptured = new List<string>();
            foreach (var kvp in blackPool)
            {
                int missing = Math.Max(0, kvp.Value);
                for (int i = 0; i < missing; i++) whiteCaptured.Add(kvp.Key);
            }

            var blackCaptured = new List<string>();
            foreach (var kvp in whitePool)
            {
                int missing = Math.Max(0, kvp.Value);
                for (int i = 0; i < missing; i++) blackCaptured.Add(kvp.Key);
            }

            return (whiteCaptured, blackCaptured);
        }

        private void UpdateCapturedPiecesUI()
        {
            try
            {
                var (whiteCaptured, blackCaptured) = GetCapturedPieces(chessGame.GetBoardState());
                // Card 1 is White -> display Black pieces captured by White
                RenderCapturedList(pnlCaptured1, whiteCaptured);
                // Card 2 is Black -> display White pieces captured by Black
                RenderCapturedList(pnlCaptured2, blackCaptured);
            }
            catch { }
        }

        private static void RenderCapturedList(StackPanel targetPanel, List<string> capturedPieces)
        {
            if (targetPanel == null) return;
            targetPanel.Children.Clear();
            if (capturedPieces == null || capturedPieces.Count == 0) return;

            int Priority(string piece) => ChessGame.PieceType(piece) switch
            {
                "Queen" => 5,
                "Rook" => 4,
                "Bishop" => 3,
                "Knight" => 2,
                "Pawn" => 1,
                _ => 0
            };

            var sorted = capturedPieces.OrderByDescending(Priority).ToList();
            int count = sorted.Count;

            // Kích thước mỗi icon quân cờ
            double iconSize = 20.0;
            // Chiều rộng khả dụng tối đa trong hàng captured pieces
            double maxPanelWidth = 245.0;

            // Tính margin phải: nếu ít quân thì cách nhau 2px, nếu nhiều quân thì xếp gối (overlap)
            double marginRight = 2.0;
            if (count > 1)
            {
                double totalWidthWithoutOverlap = count * (iconSize + 2.0);
                if (totalWidthWithoutOverlap > maxPanelWidth)
                {
                    double step = (maxPanelWidth - iconSize) / (count - 1);
                    marginRight = Math.Round(step - iconSize, 1); // Giá trị âm (overlap)
                }
            }

            for (int i = 0; i < count; i++)
            {
                var imgSource = ImageResources.GetPieceImage(sorted[i]);
                if (imgSource != null)
                {
                    string pType = ChessGame.PieceType(sorted[i]);
                    string vnType = pType switch
                    {
                        "Queen" => "Hậu",
                        "Rook" => "Xe",
                        "Bishop" => "Tượng",
                        "Knight" => "Mã",
                        "Pawn" => "Tốt",
                        _ => pType
                    };

                    var img = new Image
                    {
                        Source = imgSource,
                        Width = iconSize,
                        Height = iconSize,
                        Margin = new Thickness(0, 0, i == count - 1 ? 0 : marginRight, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        ToolTip = vnType
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    targetPanel.Children.Add(img);
                }
            }
        }

        #endregion

        private void RenderSelectionAndDots(RoomData room)
        {
            string[,] boardState = chessGame.GetBoardState();

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    var border = cellBorders[r, c];
                    border.BorderBrush = Brushes.Transparent;
                    border.BorderThickness = new Thickness(0);

                    // Yellow highlight for last move
                    bool isLastMove = (r == lastFromRow && c == lastFromCol) || (r == lastToRow && c == lastToCol);
                    if (isLastMove)
                    {
                        bool isLight = (r + c) % 2 == 0;
                        border.Background = new SolidColorBrush(isLight ? Color.FromRgb(246, 236, 114) : Color.FromRgb(218, 196, 75));
                    }
                    else
                    {
                        border.Background = new SolidColorBrush(originalSquareColors[r, c]);
                    }

                    cellDots[r, c].Visibility = Visibility.Collapsed;
                }
            }

            // Selection & Legal move dots
            if (selectedRow >= 0 && selectedCol >= 0)
            {
                var selBorder = cellBorders[selectedRow, selectedCol];
                selBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                selBorder.BorderThickness = new Thickness(3.5);

                if (showMoveHints && !isMatchmaking && room.turn == myColor)
                {
                    var legalMoves = chessGame.GetLegalMoves(selectedRow, selectedCol);
                    foreach (var (mr, mc) in legalMoves)
                    {
                        string targetPiece = boardState[mr, mc];
                        if (string.IsNullOrEmpty(targetPiece))
                        {
                            cellDots[mr, mc].Visibility = Visibility.Visible;
                        }
                        else
                        {
                            cellBorders[mr, mc].BorderBrush = Brushes.Red;
                            cellBorders[mr, mc].BorderThickness = new Thickness(2.5);
                        }
                    }
                }
            }

            // King Check highlight
            if (!string.IsNullOrEmpty(room.check))
            {
                var (kr, kc) = chessGame.FindKing(room.check, chessGame.GetBoardState());
                if (kr >= 0)
                {
                    cellBorders[kr, kc].Background = new SolidColorBrush(Color.FromRgb(220, 50, 50));
                }
            }

            // Re-apply hover highlight after full re-render
            if (hoverRow >= 0 && hoverCol >= 0)
                ApplyHoverHighlight(hoverRow, hoverCol, true);
        }

        private bool IsMyTurnNow()
        {
            if (isReplayMode) return false;
            if (isCustomMode) return !isHandlingGameOver;
            if (isBotMode)
            {
                return !isBotGamePaused && !isHandlingGameOver && currentTurn == myColor;
            }
            else
            {
                return lastRoom != null && lastRoom.status == "playing" && !lastRoom.isPaused && !isHandlingGameOver && lastRoom.turn == myColor;
            }
        }

        #region Drag & Drop and Cell Click Handling

        private void CellBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            hintFromRow = hintFromCol = hintToRow = hintToCol = -1;
            if (isReplayMode) return;
            if (isProcessingClick || isHandlingGameOver || isAnimatingMove) return;
            if (isBotMode && isBotGamePaused) return;
            if (!isBotMode && !isCustomMode && (lastRoom == null || lastRoom.status != "playing" || lastRoom.isPaused)) return;

            if (sender is not Border clickedBorder || clickedBorder.Tag is not ValueTuple<int, int> tag) return;
            int row = tag.Item1;
            int col = tag.Item2;

            string[,] board = chessGame.GetBoardState();
            string piece = board[row, col];

            string activeColor = isCustomMode ? currentTurn : myColor;

            bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0 || Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            bool isAlt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0 || Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);

            if (isCustomMode && (isCtrl || isAlt || ctrlArrowStartSquare.HasValue))
            {
                isDragPending = false;
                isDraggingPiece = false;
                return;
            }

            // If player clicked on their own piece, prepare potential drag
            if (!string.IsNullOrEmpty(piece) && ChessGame.ColorOf(piece) == activeColor)
            {
                wasPieceSelectedOnMouseDown = (selectedRow == row && selectedCol == col);
                isDragPending = true;
                isDraggingPiece = false;
                dragFromRow = row;
                dragFromCol = col;
                dragStartPoint = e.GetPosition(chessBoardGrid);

                // Select this piece immediately (green border + dots)
                selectedRow = row;
                selectedCol = col;
                if (isBotMode || isCustomMode) RenderBotModeSelectionAndDots();
                else if (lastRoom != null) RenderSelectionAndDots(lastRoom);

                clickedBorder.CaptureMouse();
            }
        }

        private void CellBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDragPending && !isDraggingPiece)
            {
                // Normal hovering: make sure current cell is highlighted even if MouseEnter was somehow missed
                if (sender is Border b && b.Tag is ValueTuple<int, int> tag)
                {
                    if (hoverRow != tag.Item1 || hoverCol != tag.Item2)
                    {
                        if (hoverRow >= 0 && hoverCol >= 0)
                            ApplyHoverHighlight(hoverRow, hoverCol, false);
                        hoverRow = tag.Item1;
                        hoverCol = tag.Item2;
                    }
                    ApplyHoverHighlight(hoverRow, hoverCol, true);
                }
                return;
            }

            Point currentPos = e.GetPosition(chessBoardGrid);

            if (isDragPending && !isDraggingPiece)
            {
                Vector diff = currentPos - dragStartPoint;
                if (Math.Abs(diff.X) > 6 || Math.Abs(diff.Y) > 6)
                {
                    isDraggingPiece = true;
                    isDragPending = false;

                    string piece = chessGame.GetBoardState()[dragFromRow, dragFromCol];
                    if (!string.IsNullOrEmpty(piece))
                    {
                        imgDraggedPiece.Source = ImageResources.GetPieceImage(piece);
                        imgDraggedPiece.Visibility = Visibility.Visible;
                        cellImages[dragFromRow, dragFromCol].Opacity = 0.3;
                    }
                }
            }

            if (isDraggingPiece)
            {
                Canvas.SetLeft(imgDraggedPiece, currentPos.X - 35);
                Canvas.SetTop(imgDraggedPiece, currentPos.Y - 35);

                // Determine which cell is under cursor and apply hover highlight during drag
                double boardW = chessBoardGrid.ActualWidth > 0 ? chessBoardGrid.ActualWidth : 560.0;
                double boardH = chessBoardGrid.ActualHeight > 0 ? chessBoardGrid.ActualHeight : 560.0;

                if (currentPos.X >= 0 && currentPos.X < boardW &&
                    currentPos.Y >= 0 && currentPos.Y < boardH)
                {
                    int visDragHoverCol = Math.Clamp((int)(currentPos.X / (boardW / 8.0)), 0, 7);
                    int visDragHoverRow = Math.Clamp((int)(currentPos.Y / (boardH / 8.0)), 0, 7);
                    var (dragHoverRow, dragHoverCol) = FromVisual(visDragHoverRow, visDragHoverCol);

                    if (dragHoverRow != hoverRow || dragHoverCol != hoverCol)
                    {
                        if (hoverRow >= 0 && hoverCol >= 0)
                            ApplyHoverHighlight(hoverRow, hoverCol, false);
                        hoverRow = dragHoverRow;
                        hoverCol = dragHoverCol;
                        ApplyHoverHighlight(hoverRow, hoverCol, true);
                    }
                }
                else
                {
                    if (hoverRow >= 0 && hoverCol >= 0)
                    {
                        ApplyHoverHighlight(hoverRow, hoverCol, false);
                        hoverRow = -1;
                        hoverCol = -1;
                    }
                }
            }
        }

        private async void CellBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isAnimatingMove) return;

            bool wasDragging = isDraggingPiece;
            bool wasPending = isDragPending;
            int fromR = dragFromRow;
            int fC = dragFromCol;

            // Reset drag flags BEFORE releasing mouse capture so LostMouseCapture handler doesn't interfere
            isDraggingPiece = false;
            isDragPending = false;
            imgDraggedPiece.Visibility = Visibility.Collapsed;

            if (fromR >= 0 && fC >= 0)
            {
                cellImages[fromR, fC].Opacity = 1.0;
            }

            if (sender is Border clickedBorder && clickedBorder.IsMouseCaptured)
            {
                clickedBorder.ReleaseMouseCapture();
            }

            if (wasDragging && fromR >= 0 && fC >= 0)
            {
                Point dropPos = e.GetPosition(chessBoardGrid);
                double cellW = chessBoardGrid.ActualWidth > 0 ? chessBoardGrid.ActualWidth / 8.0 : 70.0;
                double cellH = chessBoardGrid.ActualHeight > 0 ? chessBoardGrid.ActualHeight / 8.0 : 70.0;

                if (dropPos.X >= 0 && dropPos.X < chessBoardGrid.ActualWidth &&
                    dropPos.Y >= 0 && dropPos.Y < chessBoardGrid.ActualHeight)
                {
                    int visToCol = Math.Clamp((int)(dropPos.X / cellW), 0, 7);
                    int visToRow = Math.Clamp((int)(dropPos.Y / cellH), 0, 7);
                    var (toRow, toCol) = FromVisual(visToRow, visToCol);

                    if (toRow == fromR && toCol == fC)
                    {
                        return; // Dropped on same square - keep selection active
                    }

                    var legalMoves = chessGame.GetLegalMoves(fromR, fC);
                    bool isLegal = legalMoves.Any(m => m.row == toRow && m.col == toCol);

                    if (isLegal)
                    {
                        if (!IsMyTurnNow())
                        {
                            // Chưa đến lượt mình: không cho phép di chuyển quân cờ!
                            if (isBotMode) RenderBotModeSelectionAndDots();
                            else if (lastRoom != null) RenderSelectionAndDots(lastRoom);
                            return;
                        }

                        selectedRow = -1;
                        selectedCol = -1;
                        isProcessingClick = true;
                        try
                        {
                            if (isCustomMode)
                            {
                                RenderBotModeSelectionAndDots();
                                await ExecuteCustomModeMove(fromR, fC, toRow, toCol, animate: false);
                            }
                            else if (isBotMode)
                            {
                                RenderBotModeSelectionAndDots();
                                await ExecuteBotModeMove(fromR, fC, toRow, toCol, animate: false);
                            }
                            else
                            {
                                if (lastRoom != null) RenderSelectionAndDots(lastRoom);
                                await ExecuteMove(fromR, fC, toRow, toCol, animate: false);
                            }
                        }
                        finally
                        {
                            isProcessingClick = false;
                        }
                        return;
                    }
                    else
                    {
                        string destPiece = chessGame.GetBoardState()[toRow, toCol];
                        string activeCol = isCustomMode ? currentTurn : myColor;
                        if (!string.IsNullOrEmpty(destPiece) && ChessGame.ColorOf(destPiece) == activeCol)
                        {
                            selectedRow = toRow;
                            selectedCol = toCol;
                            if (isBotMode || isCustomMode) RenderBotModeSelectionAndDots();
                            else if (lastRoom != null) RenderSelectionAndDots(lastRoom);
                        }
                        return;
                    }
                }
                return;
            }

            if (wasPending)
            {
                // Click on own piece without dragging
                if (wasPieceSelectedOnMouseDown)
                {
                    // Clicked an already selected piece -> deselect it
                    selectedRow = -1;
                    selectedCol = -1;
                    if (isBotMode || isCustomMode) RenderBotModeSelectionAndDots();
                    else if (lastRoom != null) RenderSelectionAndDots(lastRoom);
                    return;
                }
                // Selected piece stays selected
                return;
            }

            // Clicked on a square where wasPending was false (e.g. empty square or enemy piece to complete a move)
            if (sender is not Border clickedSquareBorder || clickedSquareBorder.Tag is not ValueTuple<int, int> clickedTag) return;
            int clickRow = clickedTag.Item1;
            int clickCol = clickedTag.Item2;

            if (isCustomMode)
            {
                await HandleCustomModeCellClick(clickRow, clickCol);
            }
            else if (isBotMode)
            {
                await HandleBotModeCellClick(clickRow, clickCol);
            }
            else
            {
                await HandlePvPCellClick(clickRow, clickCol);
            }
        }

        private void CellBorder_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (isDraggingPiece)
            {
                isDraggingPiece = false;
                imgDraggedPiece.Visibility = Visibility.Collapsed;
                if (dragFromRow >= 0 && dragFromCol >= 0)
                {
                    cellImages[dragFromRow, dragFromCol].Opacity = 1.0;
                }
            }
            isDragPending = false;
        }

        private void CellBorder_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isDraggingPiece)
            {
                isDraggingPiece = false;
                imgDraggedPiece.Visibility = Visibility.Collapsed;
                if (dragFromRow >= 0 && dragFromCol >= 0)
                {
                    cellImages[dragFromRow, dragFromCol].Opacity = 1.0;
                }
            }
            isDragPending = false;
            selectedRow = -1;
            selectedCol = -1;
            if (isBotMode) RenderBotModeSelectionAndDots();
            else if (lastRoom != null) RenderSelectionAndDots(lastRoom);
        }

        private int hoverRow = -1;
        private int hoverCol = -1;

        private void CellBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            if (isDraggingPiece) return;
            if (sender is not Border b || b.Tag is not ValueTuple<int, int> tag) return;
            if (hoverRow >= 0 && hoverCol >= 0 && (hoverRow != tag.Item1 || hoverCol != tag.Item2))
            {
                ApplyHoverHighlight(hoverRow, hoverCol, false);
            }
            hoverRow = tag.Item1;
            hoverCol = tag.Item2;
            ApplyHoverHighlight(hoverRow, hoverCol, true);
        }

        private void CellBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            if (isDraggingPiece) return;
            if (sender is Border b && b.Tag is ValueTuple<int, int> tag)
            {
                ApplyHoverHighlight(tag.Item1, tag.Item2, false);
                if (hoverRow == tag.Item1 && hoverCol == tag.Item2)
                {
                    hoverRow = -1;
                    hoverCol = -1;
                }
            }
        }

        private void ChessBoardGrid_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!isDraggingPiece)
            {
                if (hoverRow >= 0 && hoverCol >= 0)
                {
                    ApplyHoverHighlight(hoverRow, hoverCol, false);
                }
                hoverRow = -1;
                hoverCol = -1;
            }
        }

        private void ApplyHoverHighlight(int row, int col, bool show)
        {
            if (row < 0 || col < 0 || row > 7 || col > 7) return;
            var border = cellBorders[row, col];
            if (show)
            {
                // Don't override selection (green: 0,230,118) or attack (red) borders
                if (border.BorderBrush is SolidColorBrush existing && border.BorderThickness.Left > 0)
                {
                    var ec = existing.Color;
                    if ((ec.G == 230 && ec.R == 0 && ec.B == 118) || (ec.R == 255 && ec.G == 0))
                        return;
                }
                border.BorderBrush = new SolidColorBrush(Color.FromArgb(220, 255, 215, 0));
                border.BorderThickness = new Thickness(3);
            }
            else
            {
                // Only clear if it's the yellow hover border we set
                if (border.BorderBrush is SolidColorBrush sb)
                {
                    var c = sb.Color;
                    if (c.R == 255 && c.G == 215 && c.B == 0)
                    {
                        border.BorderBrush = Brushes.Transparent;
                        border.BorderThickness = new Thickness(0);
                    }
                }
            }
        }

        private async Task HandlePvPCellClick(int row, int col)
        {
            if (isProcessingClick || isHandlingGameOver) return;

            RoomData? room = lastRoom;
            if (room == null) return;

            if (string.IsNullOrEmpty(room.player2))
            {
                MessageBox.Show("Chưa có ai vào phòng, vui lòng đợi.", "Thông báo",

                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (room.status != "playing" || room.isPaused) return;

            isProcessingClick = true;
            try
            {
                string[,] boardState = chessGame.GetBoardState();
                string clickedPiece = boardState[row, col];

                bool hasSelection = selectedRow >= 0 && selectedCol >= 0;

                if (!hasSelection)
                {
                    if (string.IsNullOrEmpty(clickedPiece)) return;
                    if (ChessGame.ColorOf(clickedPiece) != myColor) return;

                    selectedRow = row;
                    selectedCol = col;
                    RenderSelectionAndDots(room);
                }
                else
                {
                    if (selectedRow == row && selectedCol == col)
                    {
                        selectedRow = -1;
                        selectedCol = -1;
                        RenderSelectionAndDots(room);
                        return;
                    }

                    var currentLegalMoves = chessGame.GetLegalMoves(selectedRow, selectedCol);
                    bool isLegalTarget = currentLegalMoves.Any(m => m.row == row && m.col == col);

                    if (isLegalTarget)
                    {
                        if (room.turn != myColor)
                        {
                            // Chưa đến lượt mình: không cho phép di chuyển quân cờ
                            return;
                        }

                        int fromR = selectedRow;
                        int fromC = selectedCol;
                        selectedRow = -1;
                        selectedCol = -1;
                        RenderSelectionAndDots(room);
                        await ExecuteMove(fromR, fromC, row, col);
                    }
                    else if (!string.IsNullOrEmpty(clickedPiece) && ChessGame.ColorOf(clickedPiece) == myColor)
                    {
                        selectedRow = row;
                        selectedCol = col;
                        RenderSelectionAndDots(room);
                    }
                    else
                    {
                        selectedRow = -1;
                        selectedCol = -1;
                        RenderSelectionAndDots(room);
                    }
                }
            }
            finally
            {
                isProcessingClick = false;
            }
        }

        #endregion

        private async Task ExecuteMove(int fromRow, int fromCol, int toRow, int toCol, bool animate = true)
        {
            string movingPiece = chessGame.GetBoardState()[fromRow, fromCol];
            // Capture piece image for animation before TryMove
            ImageSource? pieceImg = animate ? ImageResources.GetPieceImage(movingPiece) : null;
            MoveResult moveResult = chessGame.TryMove(fromRow, fromCol, toRow, toCol);

            if (moveResult.NeedsPromotion)
            {
                var promoWindow = new PromotionWindow(myColor) { Owner = this };
                bool? dr = promoWindow.ShowDialog();
                if (dr != true)
                {
                    return;
                }
                moveResult = chessGame.TryMove(fromRow, fromCol, toRow, toCol, promoWindow.SelectedPiece);
            }

            if (!moveResult.Success)
            {
                MessageBox.Show(moveResult.ErrorMessage, "Nước đi không hợp lệ",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ClearCellEvaluationBadges();

            // Update last move highlight coordinates
            lastFromRow = fromRow;
            lastFromCol = fromCol;
            lastToRow = toRow;
            lastToCol = toCol;
            ClearTacticalArrows();

            // Animate piece movement (click-to-move only, not drag-drop)
            if (pieceImg != null)
                await AnimatePieceMoveAsync(fromRow, fromCol, toRow, toCol, pieceImg);

            // Play move sound: check -> warning, capture -> replace, normal -> place
            bool pvpIsCapture = !string.IsNullOrEmpty(moveResult.CapturedPiece);
            bool pvpIsCheck = !string.IsNullOrEmpty(moveResult.CheckedColor) || moveResult.IsCheckmate;

            if (pvpIsCheck)
                SoundService.PlayWarning();
            else if (pvpIsCapture)
                SoundService.PlayReplace();
            else
                SoundService.PlayPlace();

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string nextTurn = myColor == "white" ? "black" : "white";
            currentTurn = nextTurn;
            UpdateAvatarGlow();

            // Calculate turn elapsed time and update bank
            long started = (lastRoom != null && lastRoom.turnStartedAt > 0) ? lastRoom.turnStartedAt : now;
            double elapsedTurnSec = Math.Max(0.5, (now - started) / 1000.0);

            double newWhiteBank = lastRoom?.timeLeftWhite ?? 600;
            double newBlackBank = lastRoom?.timeLeftBlack ?? 600;

            if (myColor == "white")
            {
                newWhiteBank = Math.Max(0, newWhiteBank - elapsedTurnSec);
                currentRoundSeconds = elapsedTurnSec;
            }
            else
            {
                newBlackBank = Math.Max(0, newBlackBank - elapsedTurnSec);
                currentRoundSeconds += elapsedTurnSec;
            }

            // Record move in history
            var history = lastRoom?.moveHistory ?? new List<MoveHistoryEntry>();
            string formattedMove = FormatMoveSquare(toRow, toCol, moveResult.IsCastle);

            var snap = BoardToDict();
            if (myColor == "white")
            {
                history.Add(new MoveHistoryEntry
                {
                    moveNumber = history.Count + 1,
                    whiteMove = formattedMove,
                    whitePiece = movingPiece,
                    blackMove = "",
                    blackPiece = "",
                    turnSeconds = currentRoundSeconds,
                    boardAfterWhite = snap
                });
            }
            else
            {
                if (history.Count > 0)
                {
                    history[^1].blackMove = formattedMove;
                    history[^1].blackPiece = movingPiece;
                    history[^1].turnSeconds = currentRoundSeconds;
                    history[^1].boardAfterBlack = snap;
                }
                else
                {
                    history.Add(new MoveHistoryEntry
                    {
                        moveNumber = 1,
                        whiteMove = "",
                        whitePiece = "",
                        blackMove = formattedMove,
                        blackPiece = movingPiece,
                        turnSeconds = currentRoundSeconds,
                        boardAfterBlack = snap
                    });
                }
            }

            bool isThreefold = CheckThreefoldRepetition(nextTurn);
            string status = moveResult.IsCheckmate ? "checkmate" : (isThreefold ? "draw" : "playing");

            var patch = new Dictionary<string, object>
            {
                ["board"] = BoardToDict(),
                ["turn"] = nextTurn,
                ["turnStartedAt"] = now,
                ["timeLeftWhite"] = newWhiteBank,
                ["timeLeftBlack"] = newBlackBank,
                ["moveHistory"] = history,
                ["status"] = status,
                ["winner"] = moveResult.Winner ?? "",
                ["check"] = moveResult.CheckedColor ?? "",
                ["selRow"] = -1,
                ["selCol"] = -1,
                ["legalMoves"] = new List<string>(),
                ["lastFromRow"] = fromRow,
                ["lastFromCol"] = fromCol,
                ["lastToRow"] = toRow,
                ["lastToCol"] = toCol,
                ["wKingMoved"] = chessGame.WhiteKingMoved,
                ["bKingMoved"] = chessGame.BlackKingMoved,
                ["wRookAMoved"] = chessGame.WhiteRookAMoved,
                ["wRookHMoved"] = chessGame.WhiteRookHMoved,
                ["bRookAMoved"] = chessGame.BlackRookAMoved,
                ["bRookHMoved"] = chessGame.BlackRookHMoved,
                ["updatedAt"] = now
            };

            await FirebaseClient.PatchAsync($"rooms/{roomId}", patch);

            if (isThreefold)
            {
                isHandlingGameOver = true;
                pollTimer?.Stop();
                turnCountdownTimer?.Stop();
                SoundService.PlayWarning();
                MessageBox.Show("Ván cờ kết thúc HOÀ do lặp lại nước đi quá 3 lần (Threefold Repetition)!\nCả hai bên không bị cộng hoặc trừ điểm ELO.",
                    "Hoà cờ - Lặp nước đi", MessageBoxButton.OK, MessageBoxImage.Information);
                await ProcessGameDrawAsync(lastRoom ?? new RoomData { moveHistory = history });
                return;
            }

            await PollRoomAsync();
        }

        #endregion

        #region Action Buttons (Xin Hoà, Đầu Hàng, Tạm Dừng)

        private async void BtnOfferDraw_Click(object sender, RoutedEventArgs e)
        {
            if (isBotMode)
            {
                // In bot mode: bot evaluates position
                var confirm = MessageBox.Show("Bạn có muốn đề nghị hòa cờ với máy?",
                    "Xin hòa", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;

                isHandlingGameOver = true;
                gameDurationTimer?.Stop();
                glowAnimationTimer?.Stop();

                MessageBox.Show("Máy đã đồng ý kết thúc ván cờ với kết quả HOÀ!", "Hoà cờ", MessageBoxButton.OK, MessageBoxImage.Information);

                var drawOfferRec = BuildBotGameRecord("draw", "draw");
                try { _ = HistoryService.SaveGameRecordAsync(drawOfferRec); } catch { }
                EnterPostGameReviewMode(drawOfferRec);
                return;
            }

            if (lastRoom == null || lastRoom.status != "playing") return;

            var ask = MessageBox.Show("Bạn có chắc chắn muốn gửi lời xin hoà cờ tới đối thủ không?\n(Nếu đối thủ đồng ý, ván cờ sẽ kết thúc hoà và không ai bị cộng/trừ ELO)",
                "Xin hoà cờ", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ask != MessageBoxResult.Yes) return;

            btnOfferDraw.IsEnabled = false;
            try
            {
                await FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object>
                {
                    ["drawOffer"] = myColor
                });
                lblStatusTip.Text = "🤝 Đã gửi lời xin hoà cờ. Đang chờ đối thủ phản hồi...";
            }
            finally
            {
                btnOfferDraw.IsEnabled = true;
            }
        }

        private async void BtnResign_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show("Bạn có chắc chắn muốn ĐẦU HÀNG ván đấu này không?",
                "Xác nhận đầu hàng", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            if (isBotMode)
            {
                isHandlingGameOver = true;
                gameDurationTimer?.Stop();
                glowAnimationTimer?.Stop();

                MessageBox.Show($"Bạn đã đầu hàng. {botName} giành chiến thắng ván cờ.", "Đầu hàng", MessageBoxButton.OK, MessageBoxImage.Information);

                string botWinColor = myColor == "white" ? "black" : "white";
                var resignRec = BuildBotGameRecord("loss", botWinColor);
                try { _ = HistoryService.SaveGameRecordAsync(resignRec); } catch { }
                EnterPostGameReviewMode(resignRec);
                return;
            }

            if (lastRoom == null) return;
            string otherColor = myColor == "white" ? "black" : "white";

            isHandlingGameOver = true;
            pollTimer?.Stop();
            turnCountdownTimer?.Stop();

            await FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object>
            {
                ["status"] = "resigned",
                ["winner"] = otherColor,
                ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            string eloLoss = isMatchmaking ? " (-10 ELO)" : " (Giao hữu)";
            await ProcessGameOverAsync(lastRoom, otherColor, $"Bạn đã đầu hàng ván đấu{eloLoss}!");
        }

        private async void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            if (isBotMode)
            {
                isBotGamePaused = !isBotGamePaused;
                if (isBotGamePaused)
                {
                    pnlPauseOverlay.Visibility = Visibility.Visible;
                    lblPauseCountdown.Text = "Trận đấu đang tạm dừng (Đấu với máy)";
                }
                else
                {
                    pnlPauseOverlay.Visibility = Visibility.Collapsed;
                }
                return;
            }

            if (lastRoom == null || lastRoom.status != "playing" || lastRoom.isPaused) return;

            var pauseDialog = new PauseRequestDialog { Owner = this };
            if (pauseDialog.ShowDialog() != true) return;

            int selectedMinutes = pauseDialog.SelectedMinutes;

            btnPause.IsEnabled = false;
            try
            {
                await FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object>
                {
                    ["pauseRequestedBy"] = myColor,
                    ["pauseRequestedMinutes"] = selectedMinutes
                });
                lblStatusTip.Text = $"⏸️ Đã gửi yêu cầu tạm dừng {selectedMinutes} phút. Đang chờ đối thủ phản hồi...";
            }
            finally
            {
                btnPause.IsEnabled = true;
            }
        }

        private async void BtnResumeGame_Click(object sender, RoutedEventArgs e)
        {
            if (isBotMode)
            {
                isBotGamePaused = false;
                pnlPauseOverlay.Visibility = Visibility.Collapsed;
                return;
            }

            if (lastRoom == null) return;
            btnResumeGame.IsEnabled = false;
            try
            {
                await FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object>
                {
                    ["isPaused"] = false,
                    ["pauseExpiresAt"] = 0,
                    ["pauseRequestedBy"] = "",
                    ["turnStartedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                pnlPauseOverlay.Visibility = Visibility.Collapsed;
            }
            finally
            {
                btnResumeGame.IsEnabled = true;
            }
        }

        #endregion

        private void BtnLeave_Click(object sender, RoutedEventArgs e)
        {
            if (isReplayMode)
            {
                // Đang ở chế độ Xem lại (kể cả sau khi trận đấu vừa kết thúc) -> không còn gì để mất,
                // đóng luôn không cần xác nhận.
                var reviewLobby = new LobbyWindow();
                reviewLobby.Show();
                this.Close();
                return;
            }

            if (isBotMode)
            {
                var confirmBot = MessageBox.Show("Bạn có chắc chắn muốn thoát ván đấu với máy?",
                    "Thoát ván đấu", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirmBot == MessageBoxResult.Yes)
                {
                    var lobby = new LobbyWindow();
                    lobby.Show();
                    this.Close();
                }
                return;
            }

            string forfeitWarn = isMatchmaking
                ? "Bạn có chắc chắn muốn rời trận đấu? Bạn sẽ bị xử thua (-10 ELO) nếu trận đấu đang diễn ra."
                : "Bạn có chắc chắn muốn rời trận đấu giao hữu này?";

            var confirm = MessageBox.Show(forfeitWarn, "Rời trận", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm == MessageBoxResult.Yes)
            {
                var lobby = new LobbyWindow();
                lobby.Show();
                this.Close();
            }
        }

        #region Custom Mode and Replay Mode Handlers

        private void GameWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                ClearCustomAnnotations();
                e.Handled = true;
            }
            else if (e.Key == Key.H && isCustomMode)
            {
                BtnStockfishHint_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void ClearCustomAnnotations()
        {
            hintFromRow = hintFromCol = hintToRow = hintToCol = -1;
            customArrows.Clear();
            customRedHighlightedSquares.Clear();
            ctrlArrowStartSquare = null;
            ClearTacticalArrows();
            if (isCustomMode || isBotMode)
            {
                RenderBotModeSelectionAndDots();
            }
            else if (lastRoom != null)
            {
                RenderSelectionAndDots(lastRoom);
            }
        }

        private void RedrawCustomAnnotations()
        {
            if (arrowCanvas == null) return;
            ClearTacticalArrows();
            foreach (var (fr, fc, tr, tc) in customArrows)
            {
                var pFrom = GetCellCenter(fr, fc);
                var pTo = GetCellCenter(tr, tc);
                string[,] board = chessGame.GetBoardState();
                string piece = (fr < 8 && fc < 8) ? board[fr, fc] : "";
                bool isKnight = piece.Contains("Knight", StringComparison.OrdinalIgnoreCase);

                var brush = new SolidColorBrush(Color.FromArgb(235, 255, 140, 0));
                brush.Freeze();
                if (isKnight)
                {
                    DrawLShapedArrow(pFrom, pTo, brush, brush, thickness: 5.5);
                }
                else
                {
                    DrawStraightArrow(pFrom, pTo, brush, brush, thickness: 5.0);
                }
            }
        }

        private bool CheckThreefoldRepetition(string? turnColor = null)
        {
            try
            {
                string color = turnColor ?? currentTurn;
                string fen = FenHelper.BoardToFen(chessGame, color);
                if (positionHistory.TryGetValue(fen, out int count))
                {
                    count++;
                    positionHistory[fen] = count;
                    if (count >= 3)
                    {
                        return true;
                    }
                }
                else
                {
                    positionHistory[fen] = 1;
                }
            }
            catch { }
            return false;
        }

        private async Task HandleCustomModeCellClick(int row, int col)
        {
            if (isHandlingGameOver) return;

            bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0 || Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            bool isAlt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0 || Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);

            // Alt + click: toggle red highlight on the clicked square
            if (isAlt)
            {
                if (customRedHighlightedSquares.Contains((row, col)))
                {
                    customRedHighlightedSquares.Remove((row, col));
                }
                else
                {
                    customRedHighlightedSquares.Add((row, col));
                }
                RenderBotModeSelectionAndDots();
                RedrawCustomAnnotations();
                return;
            }

            // If an arrow start square is currently armed (from previous Ctrl + click)
            if (ctrlArrowStartSquare.HasValue)
            {
                var (fromR, fromC) = ctrlArrowStartSquare.Value;
                var legalMoves = chessGame.GetLegalMoves(fromR, fromC);
                bool isLegalDest = legalMoves.Any(m => m.row == row && m.col == col);

                if (isLegalDest)
                {
                    var arrow = (fromR, fromC, row, col);
                    if (customArrows.Contains(arrow))
                    {
                        customArrows.Remove(arrow);
                    }
                    else
                    {
                        customArrows.Add(arrow);
                    }
                    ctrlArrowStartSquare = null;
                    RenderBotModeSelectionAndDots();
                    RedrawCustomAnnotations();
                    return;
                }
                else
                {
                    string targetPiece = chessGame.GetBoardState()[row, col];
                    if (isCtrl && !string.IsNullOrEmpty(targetPiece))
                    {
                        ctrlArrowStartSquare = (row, col);
                        RenderBotModeSelectionAndDots();
                        RedrawCustomAnnotations();
                        return;
                    }

                    ctrlArrowStartSquare = null;
                    RenderBotModeSelectionAndDots();
                    RedrawCustomAnnotations();
                }
            }

            // Ctrl + click on a piece: arm arrow start square
            if (isCtrl)
            {
                string targetPiece = chessGame.GetBoardState()[row, col];
                if (!string.IsNullOrEmpty(targetPiece))
                {
                    ctrlArrowStartSquare = (row, col);
                    selectedRow = -1;
                    selectedCol = -1;
                    RenderBotModeSelectionAndDots();
                    RedrawCustomAnnotations();
                    return;
                }
                return;
            }

            // Normal custom mode click (move selection & execution)
            isProcessingClick = true;
            try
            {
                string[,] boardState = chessGame.GetBoardState();
                string clickedPiece = boardState[row, col];
                bool hasSelection = selectedRow >= 0 && selectedCol >= 0;

                if (!hasSelection)
                {
                    if (string.IsNullOrEmpty(clickedPiece)) return;
                    if (ChessGame.ColorOf(clickedPiece) != currentTurn) return;

                    selectedRow = row;
                    selectedCol = col;
                    RenderBotModeSelectionAndDots();
                }
                else
                {
                    if (selectedRow == row && selectedCol == col)
                    {
                        selectedRow = -1;
                        selectedCol = -1;
                        RenderBotModeSelectionAndDots();
                        return;
                    }

                    var legalMoves = chessGame.GetLegalMoves(selectedRow, selectedCol);
                    bool isLegalTarget = legalMoves.Any(m => m.row == row && m.col == col);

                    if (isLegalTarget)
                    {
                        int fromR = selectedRow;
                        int fromC = selectedCol;
                        selectedRow = -1;
                        selectedCol = -1;
                        RenderBotModeSelectionAndDots();
                        await ExecuteCustomModeMove(fromR, fromC, row, col);
                    }
                    else if (!string.IsNullOrEmpty(clickedPiece) && ChessGame.ColorOf(clickedPiece) == currentTurn)
                    {
                        selectedRow = row;
                        selectedCol = col;
                        RenderBotModeSelectionAndDots();
                    }
                    else
                    {
                        selectedRow = -1;
                        selectedCol = -1;
                        RenderBotModeSelectionAndDots();
                    }
                }
            }
            finally
            {
                isProcessingClick = false;
            }
        }

        private async Task ExecuteCustomModeMove(int fromRow, int fromCol, int toRow, int toCol, bool animate = true)
        {
            ClearCustomAnnotations();
            hintFromRow = hintFromCol = hintToRow = hintToCol = -1;
            string movingPiece = chessGame.GetBoardState()[fromRow, fromCol];
            ImageSource? pieceImg = ImageResources.GetPieceImage(movingPiece);

            string? customPromoPiece = null;
            MoveResult moveResult = chessGame.TryMove(fromRow, fromCol, toRow, toCol);
            if (moveResult.NeedsPromotion)
            {
                var promoWindow = new PromotionWindow(currentTurn) { Owner = this };
                bool? dr = promoWindow.ShowDialog();
                if (dr != true) return;
                customPromoPiece = promoWindow.SelectedPiece;
                moveResult = chessGame.TryMove(fromRow, fromCol, toRow, toCol, customPromoPiece);
            }

            if (!moveResult.Success)
            {
                MessageBox.Show(moveResult.ErrorMessage, "Nước đi không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ClearCellEvaluationBadges();

            lastFromRow = fromRow;
            lastFromCol = fromCol;
            lastToRow = toRow;
            lastToCol = toCol;

            string formattedMove = FormatMoveSquare(toRow, toCol, moveResult.IsCastle);
            var snapshot = BoardToDict();

            bool isWhiteSubMove = (currentTurn == "white");
            if (isWhiteSubMove)
            {
                localMoveHistory.Add(new MoveHistoryEntry
                {
                    moveNumber = localMoveHistory.Count + 1,
                    whiteMove = formattedMove,
                    whitePiece = movingPiece,
                    blackMove = "",
                    blackPiece = "",
                    turnSeconds = 0,
                    boardAfterWhite = snapshot
                });
                currentTurn = "black";
                lblTurnTimer.Text = "Lượt quân Đen";
            }
            else
            {
                if (localMoveHistory.Count > 0)
                {
                    localMoveHistory[^1].blackMove = formattedMove;
                    localMoveHistory[^1].blackPiece = movingPiece;
                    localMoveHistory[^1].boardAfterBlack = snapshot;
                }
                else
                {
                    localMoveHistory.Add(new MoveHistoryEntry
                    {
                        moveNumber = 1,
                        whiteMove = "",
                        whitePiece = "",
                        blackMove = formattedMove,
                        blackPiece = movingPiece,
                        turnSeconds = 0,
                        boardAfterBlack = snapshot
                    });
                }
                currentTurn = "white";
                lblTurnTimer.Text = "Lượt quân Trắng";
            }

            MoveHistoryEntry targetEntry = localMoveHistory[^1];
            RenderMoveHistoryUI(localMoveHistory, showTime: false);

            string customWhoMoved = isWhiteSubMove ? "white" : "black";
            string customUci = FenHelper.MoveToUci(fromRow, fromCol, toRow, toCol, customPromoPiece);
            EnqueuePositionEvaluation(
                isInitial: false,
                movingColor: customWhoMoved,
                playedMoveUci: customUci,
                movingPiece: movingPiece,
                fromRow: fromRow,
                fromCol: fromCol,
                toRow: toRow,
                toCol: toCol,
                promoPiece: customPromoPiece,
                isCapture: !string.IsNullOrEmpty(moveResult.CapturedPiece),
                isCheck: !string.IsNullOrEmpty(moveResult.CheckedColor) || moveResult.IsCheckmate,
                isCheckmate: moveResult.IsCheckmate,
                capturedPiece: moveResult.CapturedPiece,
                targetEntry: targetEntry,
                isWhiteSubMove: isWhiteSubMove);

            if (animate && pieceImg != null)
                await AnimatePieceMoveAsync(fromRow, fromCol, toRow, toCol, pieceImg);

            UpdateBoardUI();
            UpdateAvatarGlow();
            RenderBotModeSelectionAndDots();
            ClearTacticalArrows();

            if (moveResult.IsCheckmate)
            {
                string winColor = currentTurn == "white" ? "Đen" : "Trắng";
                SoundService.PlayWarning();
                MessageBox.Show($"Chiếu hết! Quân {winColor} đã giành chiến thắng!", "Chiếu Hết", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (CheckThreefoldRepetition())
            {
                isHandlingGameOver = true;
                SoundService.PlayWarning();
                MessageBox.Show("Ván cờ kết thúc HOÀ do lặp lại nước đi quá 3 lần (Threefold Repetition)!",
                    "Hoà cờ - Lặp nước đi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (!string.IsNullOrEmpty(moveResult.CheckedColor))
            {
                SoundService.PlayWarning();
            }
            else if (!string.IsNullOrEmpty(moveResult.CapturedPiece))
            {
                SoundService.PlayReplace();
            }
            else
            {
                SoundService.PlayPlace();
            }
        }

        private int FindReplayStepForMove(MoveHistoryEntry targetEntry, bool isWhite)
        {
            var moves = (isReplayMode ? replayRecord?.moves : localMoveHistory) ?? replayRecord?.moves;
            if (moves == null) return -1;
            int moveIdx = moves.IndexOf(targetEntry);
            if (moveIdx < 0) return -1;

            if (replaySnapshots.Count <= 1 || replayStepToMoveIndex.Count != replaySnapshots.Count)
            {
                if (replayRecord != null) BuildReplaySnapshots(replayRecord);
                else BuildLocalReplaySnapshots();
            }

            for (int s = 1; s < replaySnapshots.Count; s++)
            {
                if (s < replayStepToMoveIndex.Count && replayStepToMoveIndex[s] == moveIdx &&
                    s < replayStepIsWhite.Count && replayStepIsWhite[s] == isWhite)
                {
                    return s;
                }
            }
            return -1;
        }

        private void BuildLocalReplaySnapshots()
        {
            replaySnapshots.Clear();
            replayStepToMoveIndex.Clear();
            replayStepIsWhite.Clear();

            chessGame.InitializeBoard();
            replaySnapshots.Add(BoardToDict());
            replayStepToMoveIndex.Add(0);
            replayStepIsWhite.Add(true);

            for (int i = 0; i < localMoveHistory.Count; i++)
            {
                var m = localMoveHistory[i];
                if (m.boardAfterWhite != null && m.boardAfterWhite.Count > 0)
                {
                    replaySnapshots.Add(m.boardAfterWhite);
                    replayStepToMoveIndex.Add(i);
                    replayStepIsWhite.Add(true);
                }
                if (m.boardAfterBlack != null && m.boardAfterBlack.Count > 0)
                {
                    replaySnapshots.Add(m.boardAfterBlack);
                    replayStepToMoveIndex.Add(i);
                    replayStepIsWhite.Add(false);
                }
            }
        }

        private void JumpToHistoryMove(MoveHistoryEntry entry, Border border)
        {
            replayPlaybackTimer?.Stop();
            SpeechService.IsAutoPlaying = false;
            int step = FindReplayStepForMove(entry, isWhite: false);
            if (step < 0) step = FindReplayStepForMove(entry, isWhite: true);
            if (step >= 0)
            {
                ApplyReplayStep(step);
            }
        }

        private void JumpToSubMove(MoveHistoryEntry entry, bool isWhite, Border border)
        {
            replayPlaybackTimer?.Stop();
            SpeechService.IsAutoPlaying = false;
            int step = FindReplayStepForMove(entry, isWhite);
            if (step >= 0)
            {
                ApplyReplayStep(step);
            }
        }

        private void ApplyReplayStep(int step)
        {
            if (step < 0 || step >= replaySnapshots.Count) return;
            ClearCustomAnnotations();
            currentReplayStep = step;
            var dict = replaySnapshots[step];
            ApplyDictToBoard(dict);
            UpdateBoardUI();

            if (step == 0)
            {
                lblTurnTimer.Text = "Bắt đầu ván đấu (Lượt 0)";
                currentTurn = "white";
                UpdateAvatarGlow();
                ClearCellEvaluationBadges();
                UpdateProspectiveOrActiveArrows(0);
                lblLastMoveDetail.Text = "Bắt đầu ván đấu. Bấm vào các nước đi trong bảng hoặc nút Trước / Sau để xem bình luận Stockfish!";
                lblLastMovePlayer.Text = "Bình luận nước đi:";
                lblLastMoveBadge.Text = "Khởi đầu";
                badgeLastMoveEval.Background = new SolidColorBrush(Color.FromRgb(84, 80, 122));
                if (highlightedHistoryRow != null)
                {
                    highlightedHistoryRow.BorderBrush = Brushes.Transparent;
                    highlightedHistoryRow.BorderThickness = new Thickness(0);
                }
            }
            else
            {
                int moveIdx = step < replayStepToMoveIndex.Count ? replayStepToMoveIndex[step] : 0;
                bool isWhite = step < replayStepIsWhite.Count ? replayStepIsWhite[step] : true;
                lblTurnTimer.Text = $"Nước đi {step} / {replaySnapshots.Count - 1}";

                if (isCustomMode)
                {
                    currentTurn = isWhite ? "black" : "white";
                    lblTurnTimer.Text = $"Lượt quân {(currentTurn == "white" ? "Trắng" : "Đen")}";
                    UpdateAvatarGlow();
                }

                // Highlight corresponding row in move history
                if (moveIdx >= 0 && moveIdx < pnlMoveHistory.Children.Count)
                {
                    if (pnlMoveHistory.Children[moveIdx] is Border rowBorder)
                    {
                        if (highlightedHistoryRow != null)
                        {
                            highlightedHistoryRow.BorderBrush = Brushes.Transparent;
                            highlightedHistoryRow.BorderThickness = new Thickness(0);
                        }
                        highlightedHistoryRow = rowBorder;
                        highlightedHistoryRow.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 229, 255));
                        highlightedHistoryRow.BorderThickness = new Thickness(1.5);
                        highlightedHistoryRow.BringIntoView();
                    }
                }

                // Hiển thị bình luận & gắn badge lên bàn cờ cho bước này
                var currentMoves = (isReplayMode ? replayRecord?.moves : localMoveHistory) ?? replayRecord?.moves;
                if (currentMoves != null && moveIdx >= 0 && moveIdx < currentMoves.Count)
                {
                    var entry = currentMoves[moveIdx];
                    ShowHistoricalMoveCommentary(entry, isWhite);
                }
            }
        }

        private void StartReplayPlayback()
        {
            replayPlaybackTimer?.Stop();

            if (currentReplayStep >= replaySnapshots.Count - 1)
            {
                currentReplayStep = 0;
                ApplyReplayStep(0);
            }

            SetReplayPlayPauseHighlight(isPlaying: true);
            SpeechService.IsAutoPlaying = true;

            replayPlaybackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1250) };
            replayPlaybackTimer.Tick += (s, ev) =>
            {
                if (currentReplayStep < replaySnapshots.Count - 1)
                {
                    currentReplayStep++;
                    ApplyReplayStep(currentReplayStep);
                    SoundService.PlayPlace();
                }
                else
                {
                    replayPlaybackTimer?.Stop();
                    SpeechService.IsAutoPlaying = false;
                    SetReplayPlayPauseHighlight(isPlaying: false);
                }
            };
            replayPlaybackTimer.Start();
        }

        private void ApplyDictToBoard(Dictionary<string, string> dict)
        {
            var board = chessGame.GetBoardState();
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    string key = $"{r}_{c}";
                    board[r, c] = dict.ContainsKey(key) ? dict[key] : "";
                }
            }
        }

        // 1. Prev: Quay lại nước đi trước
        private void BtnReplayPrev_Click(object sender, RoutedEventArgs e)
        {
            replayPlaybackTimer?.Stop();
            SpeechService.IsAutoPlaying = false;
            SetReplayPlayPauseHighlight(isPlaying: false);
            ClearCustomAnnotations();
            if (isReplayMode)
            {
                if (currentReplayStep > 0)
                {
                    currentReplayStep--;
                    ApplyReplayStep(currentReplayStep);
                }
            }
            else if (isCustomMode)
            {
                // Undo last move in Custom mode
                if (localMoveHistory.Count == 0) return;
                lastPositionEval = null;

                var lastEntry = localMoveHistory[^1];
                if (!string.IsNullOrEmpty(lastEntry.blackMove))
                {
                    // Undo black move: restore to boardAfterWhite
                    lastEntry.blackMove = "";
                    lastEntry.blackPiece = "";
                    lastEntry.boardAfterBlack = null;
                    if (lastEntry.boardAfterWhite != null)
                    {
                        ApplyDictToBoard(lastEntry.boardAfterWhite);
                    }
                    currentTurn = "black";
                    lblTurnTimer.Text = "Lượt quân Đen";
                }
                else
                {
                    // Undo white move: remove this entry entirely
                    localMoveHistory.RemoveAt(localMoveHistory.Count - 1);
                    if (localMoveHistory.Count > 0)
                    {
                        var prev = localMoveHistory[^1];
                        var prevBoard = prev.boardAfterBlack ?? prev.boardAfterWhite;
                        if (prevBoard != null) ApplyDictToBoard(prevBoard);
                    }
                    else
                    {
                        chessGame.InitializeBoard();
                    }
                    currentTurn = "white";
                    lblTurnTimer.Text = "Lượt quân Trắng";
                }

                RenderMoveHistoryUI(localMoveHistory, showTime: false);
                UpdateBoardUI();
                UpdateAvatarGlow();
                RenderBotModeSelectionAndDots();
            }
        }

        // 2. Play: Tiếp (Replay) / Phát từ đầu (Custom)
        private void BtnReplayPlay_Click(object sender, RoutedEventArgs e)
        {
            ClearCustomAnnotations();
            if (isReplayMode)
            {
                StartReplayPlayback();
            }
            else if (isCustomMode)
            {
                // Replay from beginning in Custom mode
                if (localMoveHistory.Count == 0) return;

                replaySnapshots.Clear();
                replayStepToMoveIndex.Clear();

                chessGame.InitializeBoard();
                replaySnapshots.Add(BoardToDict());
                replayStepToMoveIndex.Add(0);

                for (int i = 0; i < localMoveHistory.Count; i++)
                {
                    var m = localMoveHistory[i];
                    if (m.boardAfterWhite != null)
                    {
                        replaySnapshots.Add(m.boardAfterWhite);
                        replayStepToMoveIndex.Add(i);
                    }
                    if (m.boardAfterBlack != null)
                    {
                        replaySnapshots.Add(m.boardAfterBlack);
                        replayStepToMoveIndex.Add(i);
                    }
                }

                currentReplayStep = 0;
                ApplyReplayStep(0);
                StartReplayPlayback();
            }
        }

        // 3. Pause: Tạm dừng
        private void BtnReplayPause_Click(object sender, RoutedEventArgs e)
        {
            replayPlaybackTimer?.Stop();
            SpeechService.IsAutoPlaying = false;
            SetReplayPlayPauseHighlight(isPlaying: false);
        }

        // 4. Next: Nước đi sau (Replay) / Nước đi hiện tại (Custom)
        private void BtnReplayNext_Click(object sender, RoutedEventArgs e)
        {
            replayPlaybackTimer?.Stop();
            SpeechService.IsAutoPlaying = false;
            SetReplayPlayPauseHighlight(isPlaying: false);
            ClearCustomAnnotations();
            if (isReplayMode)
            {
                if (currentReplayStep < replaySnapshots.Count - 1)
                {
                    currentReplayStep++;
                    ApplyReplayStep(currentReplayStep);
                }
            }
            else if (isCustomMode)
            {
                // Jump to the latest move / current board
                if (localMoveHistory.Count > 0)
                {
                    var last = localMoveHistory[^1];
                    var lastBoard = last.boardAfterBlack ?? last.boardAfterWhite;
                    if (lastBoard != null)
                    {
                        ApplyDictToBoard(lastBoard);
                        currentTurn = (last.boardAfterBlack != null) ? "white" : "black";
                        lblTurnTimer.Text = $"Lượt quân {(currentTurn == "white" ? "Trắng" : "Đen")}";
                        UpdateBoardUI();
                        UpdateAvatarGlow();
                    }
                }
            }
        }

        private void BtnInviteFriendInRoom_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(roomId)) return;
            var dlg = new InviteFriendDialog(roomId) { Owner = this };
            dlg.ShowDialog();
        }

        /// <summary>
        /// Trả về username tương ứng với khung người chơi (P1/P2) để hiện menu chuột phải,
        /// áp dụng đúng cho mọi chế độ: Online (PvP/Ghép trận), Đấu Máy, Custom, Xem lại.
        /// Trả về null/rỗng nếu khung đó là quân của Bot (không cho xem thông tin/lịch sử của Bot).
        /// </summary>
        private string? GetPlayerCardUsername(bool isPlayer1)
        {
            if (isBotMode)
            {
                // Chỉ khung của người chơi thật (bạn) mới có thông tin; khung của Bot thì không.
                bool isHumanCard = isPlayer1 ? (myColor == "white") : (myColor == "black");
                return isHumanCard ? playerName : null;
            }

            if (isCustomMode)
            {
                // Custom: một người chơi tự điều khiển cả 2 bên -> cả 2 khung đều là chính mình.
                return playerName;
            }

            if (isReplayMode)
            {
                string uname = isPlayer1 ? (replayRecord?.playerWhite ?? "") : (replayRecord?.playerBlack ?? "");
                return string.IsNullOrWhiteSpace(uname) ? null : uname;
            }

            // Chế độ Online (Phòng giao hữu / Ghép trận ELO)
            string target = isPlayer1 ? (lastRoom?.player1 ?? playerName) : (lastRoom?.player2 ?? "");
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }

        private async void CardP1_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            string? target = GetPlayerCardUsername(isPlayer1: true);
            if (!string.IsNullOrWhiteSpace(target) && sender is FrameworkElement fe)
            {
                await PlayerContextMenu.ShowAsync(fe, target, this);
            }
        }

        private async void CardP2_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Phòng giao hữu đang chờ đối thủ (chưa có ai vào) -> chuột phải để mời bạn bè vào phòng
            bool isWaitingForOpponent = !isBotMode && !isCustomMode && !isReplayMode && !isMatchmaking
                && !string.IsNullOrEmpty(roomId)
                && (lastRoom == null || string.IsNullOrEmpty(lastRoom.player2));

            if (isWaitingForOpponent)
            {
                var dlg = new InviteFriendDialog(roomId) { Owner = this };
                dlg.ShowDialog();
                return;
            }

            string? target = GetPlayerCardUsername(isPlayer1: false);
            if (!string.IsNullOrWhiteSpace(target) && sender is FrameworkElement fe)
            {
                await PlayerContextMenu.ShowAsync(fe, target, this);
            }
        }

        #endregion

        private void GameWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                replayPlaybackTimer?.Stop();
                pollTimer?.Stop();
                turnCountdownTimer?.Stop();
                glowAnimationTimer?.Stop();
                gameDurationTimer?.Stop();

                if (chessBot is IDisposable disposableBot)
                {
                    disposableBot.Dispose();
                }

                try
                {
                    evalWorkerCts?.Cancel();
                    evalWorkerCts?.Dispose();
                    stockfishEvaluator?.Dispose();
                    stockfishEvaluator = null;
                }
                catch { }

                if (isBotMode || isCustomMode || isReplayMode) return;

                if (lastRoom == null || lastRoom.status == "waiting" || string.IsNullOrEmpty(lastRoom.player2))
                {
                    _ = FirebaseClient.DeleteAsync($"rooms/{roomId}");
                    return;
                }

                if (lastRoom.status == "playing" && !string.IsNullOrEmpty(lastRoom.player2) && !isHandlingGameOver)
                {
                    isHandlingGameOver = true;
                    string otherColor = myColor == "white" ? "black" : "white";
                    _ = FirebaseClient.PatchAsync($"rooms/{roomId}", new Dictionary<string, object>
                    {
                        ["status"] = "player_left",
                        ["winner"] = otherColor,
                        ["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    });

                    string winnerName = otherColor == "white" ? lastRoom.player1 : lastRoom.player2;
                    string loserName = myColor == "white" ? lastRoom.player1 : lastRoom.player2;
                    _ = AuthService.RecordGameResultAsync(winnerName, loserName, isMatchmaking: lastRoom.isMatchmaking, points: 10);
                }
                else
                {
                    _ = FirebaseClient.DeleteAsync($"rooms/{roomId}");
                }
            }
            catch { }
        }

        #region Stockfish Evaluation & Real-Time Move Analysis

        private class StockfishEvalTask
        {
            public bool IsInitial { get; set; }
            public string Fen { get; set; } = "";
            public string TurnColor { get; set; } = "white";
            public string? MovingColor { get; set; }
            public string? PlayedMoveUci { get; set; }
            public string? MovingPiece { get; set; }
            public int FromRow { get; set; } = -1;
            public int FromCol { get; set; } = -1;
            public int ToRow { get; set; } = -1;
            public int ToCol { get; set; } = -1;
            public string? PromoPiece { get; set; }
            public bool IsCapture { get; set; }
            public bool IsCheck { get; set; }
            public bool IsCheckmate { get; set; }
            public string? CapturedPiece { get; set; }
            public MoveHistoryEntry? TargetEntry { get; set; }
            public bool IsWhiteSubMove { get; set; }
            public bool IsCustomMode { get; set; } = false;
            public bool IsMyMove { get; set; } = true;
        }

        private void InitializeStockfishEvaluation()
        {
            try
            {
                if (stockfishEvaluator == null)
                    stockfishEvaluator = new StockfishEvaluator();

                if (stockfishEvaluator.IsAvailable)
                {
                    if (isCustomMode)
                    {
                        // Phòng Custom: hiển thị gợi ý & bình luận ngay trong lúc chơi
                        isStockfishEvalEnabled = true;
                        pnlStockfishAnalysis.Visibility = Visibility.Visible;
                        borderLastMoveEval.Visibility = Visibility.Visible;
                        btnStockfishHint.Visibility = Visibility.Visible;
                        btnToggleEval.Visibility = Visibility.Visible;
                        btnToggleEval.Content = "BẬT";
                        btnToggleEval.Background = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                        btnToggleEval.Foreground = new SolidColorBrush(Color.FromRgb(18, 18, 18));
                        pnlEvalBar.Visibility = showMoveHints ? Visibility.Visible : Visibility.Collapsed;
                    }
                    else if (isReplayMode)
                    {
                        // Chế độ xem lại sau trận: hiển thị bình luận đầy đủ
                        isStockfishEvalEnabled = true;
                        pnlStockfishAnalysis.Visibility = Visibility.Visible;
                        borderLastMoveEval.Visibility = Visibility.Visible;
                        pnlEvalBar.Visibility = Visibility.Visible;
                        btnStockfishHint.Visibility = Visibility.Collapsed;
                        btnToggleEval.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        // Mọi chế độ khác trong lúc đang chơi (Bot, PvP, Ghép trận):
                        // TẮT hoàn toàn gợi ý và bình luận trong trận
                        isStockfishEvalEnabled = false;
                        pnlEvalBar.Visibility = Visibility.Collapsed;
                        pnlStockfishAnalysis.Visibility = Visibility.Collapsed;
                        borderLastMoveEval.Visibility = Visibility.Collapsed;
                        btnStockfishHint.Visibility = Visibility.Collapsed;
                        btnToggleEval.Visibility = Visibility.Collapsed;
                    }

                    if (evalChannel == null)
                    {
                        evalChannel = System.Threading.Channels.Channel.CreateUnbounded<StockfishEvalTask>();
                        evalWorkerCts = new System.Threading.CancellationTokenSource();
                        evalWorkerTask = Task.Run(() => ProcessEvaluationQueueAsync(evalWorkerCts.Token));

                        // Đánh giá thế cờ khởi đầu (bàn cờ ban đầu)
                        string startFen = FenHelper.BoardToFen(chessGame, currentTurn);
                        evalChannel.Writer.TryWrite(new StockfishEvalTask
                        {
                            IsInitial = true,
                            Fen = startFen,
                            TurnColor = currentTurn
                        });
                    }
                }
                else
                {
                    pnlEvalBar.Visibility = Visibility.Collapsed;
                    pnlStockfishAnalysis.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                pnlEvalBar.Visibility = Visibility.Collapsed;
                pnlStockfishAnalysis.Visibility = Visibility.Collapsed;
            }
        }

        private void EnqueuePositionEvaluation(
            bool isInitial,
            string? movingColor = null,
            string? playedMoveUci = null,
            string? movingPiece = null,
            int fromRow = -1,
            int fromCol = -1,
            int toRow = -1,
            int toCol = -1,
            string? promoPiece = null,
            bool isCapture = false,
            bool isCheck = false,
            bool isCheckmate = false,
            string? capturedPiece = null,
            MoveHistoryEntry? targetEntry = null,
            bool isWhiteSubMove = true)
        {
            if (stockfishEvaluator == null || !stockfishEvaluator.IsAvailable || evalChannel == null)
            {
                return;
            }

            string activeTurn = isInitial
                ? (currentTurn ?? "white")
                : (!string.IsNullOrEmpty(movingColor) ? ChessGame.Opponent(movingColor) : (currentTurn ?? "white"));

            string currentFen = FenHelper.BoardToFen(chessGame, activeTurn);
            evalChannel.Writer.TryWrite(new StockfishEvalTask
            {
                IsInitial = isInitial,
                Fen = currentFen,
                TurnColor = activeTurn,
                MovingColor = movingColor,
                PlayedMoveUci = playedMoveUci,
                MovingPiece = movingPiece,
                FromRow = fromRow,
                FromCol = fromCol,
                ToRow = toRow,
                ToCol = toCol,
                PromoPiece = promoPiece,
                IsCapture = isCapture,
                IsCheck = isCheck,
                IsCheckmate = isCheckmate,
                CapturedPiece = capturedPiece,
                TargetEntry = targetEntry,
                IsWhiteSubMove = isWhiteSubMove,
                IsCustomMode = this.isCustomMode,
                IsMyMove = string.Equals(movingColor, myColor, StringComparison.OrdinalIgnoreCase)
            });
        }

        private async Task ProcessEvaluationQueueAsync(System.Threading.CancellationToken ct)
        {
            try
            {
                if (evalChannel == null) return;
                var reader = evalChannel.Reader;

                while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var task))
                    {
                        if (ct.IsCancellationRequested) return;
                        if (stockfishEvaluator == null || !stockfishEvaluator.IsAvailable) continue;

                        var beforeEval = lastPositionEval;
                        var afterEval = await stockfishEvaluator.EvaluatePositionAsync(
                            task.Fen, task.TurnColor, chessGame, 300, ct).ConfigureAwait(false);

                        if (afterEval == null)
                        {
                            if (task.IsCheckmate || (chessGame != null && chessGame.IsCheckmate(task.TurnColor)))
                            {
                                bool isWhiteTurn = string.Equals(task.TurnColor, "white", StringComparison.OrdinalIgnoreCase);
                                afterEval = new PositionEvaluation
                                {
                                    WhiteScore = isWhiteTurn ? -100.0 : 100.0,
                                    MateIn = 0,
                                    TurnColor = task.TurnColor,
                                    Depth = beforeEval?.Depth ?? 1
                                };
                            }
                            else
                            {
                                continue;
                            }
                        }

                        if (ct.IsCancellationRequested) return;

                        lastPositionEval = afterEval;

                        MoveAssessment? assessment = null;
                        if (!task.IsInitial && !string.IsNullOrEmpty(task.MovingColor) && !string.IsNullOrEmpty(task.PlayedMoveUci))
                        {
                            assessment = StockfishEvaluator.ClassifyMove(
                                beforeEval,
                                afterEval,
                                task.MovingColor,
                                task.PlayedMoveUci,
                                task.MovingPiece ?? "",
                                task.FromRow,
                                task.FromCol,
                                task.ToRow,
                                task.ToCol,
                                task.PromoPiece,
                                task.IsCapture,
                                task.IsCheck,
                                task.IsCheckmate,
                                task.CapturedPiece,
                                chessGame,
                                isCustomMode: task.IsCustomMode,
                                isMyMove: task.IsMyMove);
                        }

                        // Cập nhật giao diện trên UI thread
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (isStockfishEvalEnabled)
                            {
                                UpdateEvalBarUI(afterEval);
                                UpdateStockfishAdvantageUI(afterEval);

                                if (assessment != null && !string.IsNullOrEmpty(task.MovingColor))
                                {
                                    UpdateLastMoveAssessmentUI(assessment, task.MovingColor);
                                }
                            }

                            if (assessment != null && task.TargetEntry != null)
                            {
                                bool isNone = assessment.Classification == MoveClassification.None;
                                if (task.IsWhiteSubMove)
                                {
                                    task.TargetEntry.whiteEval = assessment.NewPositionEval?.FormattedScore;
                                    task.TargetEntry.whiteClassification = isNone ? "" : assessment.Classification.ToString();
                                    task.TargetEntry.whiteBadge = isNone ? "" : assessment.Badge;
                                    task.TargetEntry.whiteColorHex = isNone ? "" : assessment.ColorHex;
                                    task.TargetEntry.whiteBestAlternative = isNone ? null : assessment.BestAlternative;
                                    task.TargetEntry.whiteBestAlternativeReason = isNone ? null : assessment.BestAlternativeReason;
                                    task.TargetEntry.whiteBestAltCoords = isNone ? null : assessment.BestAlternativeCoords;
                                    task.TargetEntry.whiteCommentary = isNone ? "" : assessment.Commentary;
                                    task.TargetEntry.whiteVoiceClips = isNone ? null : assessment.VoiceClips;
                                }
                                else
                                {
                                    task.TargetEntry.blackEval = assessment.NewPositionEval?.FormattedScore;
                                    task.TargetEntry.blackClassification = isNone ? "" : assessment.Classification.ToString();
                                    task.TargetEntry.blackBadge = isNone ? "" : assessment.Badge;
                                    task.TargetEntry.blackColorHex = isNone ? "" : assessment.ColorHex;
                                    task.TargetEntry.blackBestAlternative = isNone ? null : assessment.BestAlternative;
                                    task.TargetEntry.blackBestAlternativeReason = isNone ? null : assessment.BestAlternativeReason;
                                    task.TargetEntry.blackBestAltCoords = isNone ? null : assessment.BestAlternativeCoords;
                                    task.TargetEntry.blackCommentary = isNone ? "" : assessment.Commentary;
                                    task.TargetEntry.blackVoiceClips = isNone ? null : assessment.VoiceClips;
                                }

                                RenderMoveHistoryUI(localMoveHistory, showTime: false);
                            }
                        });
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StockfishQueue] {ex.Message}");
            }
        }

        private void UpdateEvalBarUI(PositionEvaluation eval)
        {
            if (pnlEvalBar.Visibility != Visibility.Visible) return;

            double winPercent = eval.WhiteWinPercent;
            bool flipped = IsBoardFlipped;

            // Nếu bàn cờ không bị lật (Trắng ở dưới), cột Trắng dâng từ dưới lên; nếu bị lật (Trắng ở trên), dâng từ trên xuống
            barEvalWhite.VerticalAlignment = !flipped ? VerticalAlignment.Bottom : VerticalAlignment.Top;

            double totalHeight = 560.0;
            double whiteHeight = totalHeight * (winPercent / 100.0);
            whiteHeight = Math.Clamp(whiteHeight, 14.0, totalHeight - 14.0);

            var anim = new DoubleAnimation
            {
                To = whiteHeight,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            barEvalWhite.BeginAnimation(FrameworkElement.HeightProperty, anim);

            lblEvalScore.Text = eval.FormattedScore;

            string advantageText = eval.MateIn.HasValue
                ? (eval.MateIn > 0 ? $"Trắng chiếu hết trong {eval.MateIn} nước" : $"Đen chiếu hết trong {-eval.MateIn} nước")
                : (Math.Abs(eval.WhiteScore) < 0.1 ? "Thế trận cân bằng" : (eval.WhiteScore > 0 ? $"Trắng dẫn: +{eval.WhiteScore:F1}" : $"Đen dẫn: {-eval.WhiteScore:F1}"));

            pnlEvalBar.ToolTip = $"Điểm Stockfish: {eval.FormattedScore} ({advantageText})";
        }

        private void UpdateStockfishAdvantageUI(PositionEvaluation eval)
        {
            lblStockfishDepth.Text = $"Độ sâu: {eval.Depth}";

            if (eval.MateIn.HasValue)
            {
                int m = eval.MateIn.Value;
                lblStockfishPositionEval.Text = m > 0 ? $"⚔️ Trắng chiếu hết trong {m} nước (M{m})" : $"⚔️ Đen chiếu hết trong {-m} nước (-M{-m})";
                lblStockfishPositionEval.Foreground = new SolidColorBrush(m > 0 ? Color.FromRgb(0, 230, 118) : Color.FromRgb(255, 61, 0));
            }
            else
            {
                double s = eval.WhiteScore;
                if (Math.Abs(s) < 0.2)
                {
                    lblStockfishPositionEval.Text = $"⚖️ Thế trận: Cân bằng ({eval.FormattedScore})";
                    lblStockfishPositionEval.Foreground = Brushes.White;
                }
                else if (s >= 2.0)
                {
                    lblStockfishPositionEval.Text = $"⚪ Trắng chiếm ưu thế lớn ({eval.FormattedScore})";
                    lblStockfishPositionEval.Foreground = new SolidColorBrush(Color.FromRgb(0, 229, 255));
                }
                else if (s > 0)
                {
                    lblStockfishPositionEval.Text = $"⚪ Trắng hơi chiếm ưu thế ({eval.FormattedScore})";
                    lblStockfishPositionEval.Foreground = new SolidColorBrush(Color.FromRgb(105, 240, 174));
                }
                else if (s <= -2.0)
                {
                    lblStockfishPositionEval.Text = $"⚫ Đen chiếm ưu thế lớn ({eval.FormattedScore})";
                    lblStockfishPositionEval.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0));
                }
                else
                {
                    lblStockfishPositionEval.Text = $"⚫ Đen hơi chiếm ưu thế ({eval.FormattedScore})";
                    lblStockfishPositionEval.Foreground = new SolidColorBrush(Color.FromRgb(255, 213, 79));
                }
            }
        }

        private (int fromRow, int fromCol, int toRow, int toCol, string? promo)? lastBestAltCoords = null;

        private void PnlBestAltBox_Click(object sender, MouseButtonEventArgs e)
        {
            if (lastBestAltCoords.HasValue)
            {
                var (fR, fC, tR, tC, _) = lastBestAltCoords.Value;
                if (fR >= 0 && fR < 8 && fC >= 0 && fC < 8 && tR >= 0 && tR < 8 && tC >= 0 && tC < 8)
                {
                    hintFromRow = fR;
                    hintFromCol = fC;
                    hintToRow = tR;
                    hintToCol = tC;
                    HighlightStockfishHint(fR, fC, tR, tC);
                    lblStatusTip.Text = $"💡 Gợi ý Stockfish: {lblBestAlternativeMove.Text}";
                }
            }
        }

        private void UpdateLastMoveAssessmentUI(MoveAssessment assessment, string movingColor)
        {
            if (assessment == null || assessment.Classification == MoveClassification.None)
            {
                borderLastMoveEval.Visibility = Visibility.Collapsed;
                pnlBestAltBox.Visibility = Visibility.Collapsed;
                lastBestAltCoords = null;
                return;
            }

            // In-board badge on destination square (luôn hiển thị trên bàn cờ nếu có badge)
            if (lastToRow >= 0 && lastToCol >= 0 && !string.IsNullOrEmpty(assessment.Badge))
            {
                SetCellEvaluationBadge(lastToRow, lastToCol, assessment.Badge, assessment.ColorHex, assessment.Title);
            }

            // Nếu không có bình luận (ví dụ nước đi bình thường / tốt của đối thủ ở chế độ không phải Custom)
            // thì ẩn khung bình luận phía dưới và không phát âm thanh giọng đọc
            if (string.IsNullOrEmpty(assessment.Commentary))
            {
                borderLastMoveEval.Visibility = Visibility.Collapsed;
                pnlBestAltBox.Visibility = Visibility.Collapsed;
                lastBestAltCoords = null;
                return;
            }

            borderLastMoveEval.Visibility = Visibility.Visible;

            string who;
            if (isCustomMode)
            {
                who = string.Equals(movingColor, "white", StringComparison.OrdinalIgnoreCase)
                    ? "Quân Trắng"
                    : "Quân Đen";
            }
            else
            {
                bool isMyMove = string.Equals(movingColor, myColor, StringComparison.OrdinalIgnoreCase);
                who = isMyMove ? "Bạn" : (!string.IsNullOrWhiteSpace(botName) ? botName : "Đối thủ");
            }

            lblLastMovePlayer.Text = $"{who} vừa đi:";
            lblLastMoveBadge.Text = $"{assessment.Badge} {assessment.Title}";

            Color badgeColor;
            try { badgeColor = (Color)ColorConverter.ConvertFromString(assessment.ColorHex); }
            catch { badgeColor = Color.FromRgb(0, 230, 118); }

            badgeLastMoveEval.Background = new SolidColorBrush(badgeColor);

            bool isDarkBg = assessment.Classification == MoveClassification.Blunder || assessment.Classification == MoveClassification.Mistake;
            lblLastMoveBadge.Foreground = isDarkBg ? Brushes.White : new SolidColorBrush(Color.FromRgb(18, 18, 18));
            lblLastMoveDetail.Text = assessment.Commentary;

            // Speak commentary asynchronously with TTS — on each move (only if not auto-playing)
            if (!SpeechService.IsAutoPlaying)
            {
                SpeechService.SpeakAssessment(assessment);
            }

            if (!string.IsNullOrEmpty(assessment.BestAlternative))
            {
                pnlBestAltBox.Visibility = Visibility.Visible;
                lblBestAlternativeMove.Text = assessment.BestAlternative;
                lblBestAlternativeExplain.Text = !string.IsNullOrEmpty(assessment.BestAlternativeReason)
                    ? $"({assessment.BestAlternativeReason})"
                    : "";
                lastBestAltCoords = assessment.BestAlternativeCoords;
            }
            else
            {
                pnlBestAltBox.Visibility = Visibility.Collapsed;
                lastBestAltCoords = null;
            }
        }

        private void ShowHistoricalMoveCommentary(MoveHistoryEntry item, bool isWhite)
        {
            if (item == null) return;

            string who = isWhite ? "Quân Trắng" : "Quân Đen";
            string moveText = isWhite ? item.whiteMove : item.blackMove;
            string? badge = isWhite ? item.whiteBadge : item.blackBadge;
            string? classification = isWhite ? item.whiteClassification : item.blackClassification;
            string? colorHex = isWhite ? item.whiteColorHex : item.blackColorHex;
            string? commentary = isWhite ? item.whiteCommentary : item.blackCommentary;
            string? bestAlt = isWhite ? item.whiteBestAlternative : item.blackBestAlternative;
            string? bestAltReason = isWhite ? item.whiteBestAlternativeReason : item.blackBestAlternativeReason;
            var bestAltCoords = isWhite ? item.whiteBestAltCoords : item.blackBestAltCoords;
            string piece = isWhite ? item.whitePiece : item.blackPiece;

            if (string.IsNullOrEmpty(moveText)) return;

            string titleVi = "";
            if (!string.IsNullOrEmpty(classification) && Enum.TryParse<MoveClassification>(classification, out var parsedClass) && parsedClass != MoveClassification.None)
            {
                titleVi = StockfishEvaluator.GetClassificationTitleVi(parsedClass);
            }

            // In-board evaluation badge & Tactical Arrows
            var (fromR, fromC, toR, toC) = GetMoveCoordinates(item, isWhite);
            if (toR >= 0 && toColValid(toC))
            {
                SetCellEvaluationBadge(toR, toC, badge, colorHex, titleVi);
            }

            // In-board evaluation badge & Tactical Arrows
            UpdateProspectiveOrActiveArrows(currentReplayStep);

            if (string.IsNullOrEmpty(commentary))
            {
                lblLastMovePlayer.Text = $"Nước {item.moveNumber} • {who} ({moveText})";
                lblLastMoveBadge.Text = "";
                badgeLastMoveEval.Background = Brushes.Transparent;
                lblLastMoveDetail.Text = "";
                pnlBestAltBox.Visibility = Visibility.Collapsed;
                lastBestAltCoords = null;
                return;
            }

            lblLastMovePlayer.Text = $"Nước {item.moveNumber} • {who} ({moveText}):";
            lblLastMoveBadge.Text = !string.IsNullOrEmpty(badge) ? $"{badge} {titleVi}" : titleVi;

            Color badgeColor;
            try { badgeColor = (Color)ColorConverter.ConvertFromString(colorHex ?? "#00E676"); }
            catch { badgeColor = Color.FromRgb(0, 230, 118); }

            badgeLastMoveEval.Background = new SolidColorBrush(badgeColor);
            bool isDarkBg = classification == "Blunder" || classification == "Mistake";
            lblLastMoveBadge.Foreground = isDarkBg ? Brushes.White : new SolidColorBrush(Color.FromRgb(18, 18, 18));
            lblLastMoveDetail.Text = commentary;

            // Speak commentary: use pre-generated voice clip sequence if ready (0ms latency), else fallback to text parser
            if (!SpeechService.IsAutoPlaying)
            {
                var clips = isWhite ? item.whiteVoiceClips : item.blackVoiceClips;
                if (clips != null && clips.Count > 0)
                    SpeechService.PlayClipSequence(clips);
                else
                    SpeechService.SpeakAsync(commentary);
            }

            if (!string.IsNullOrEmpty(bestAlt))
            {
                pnlBestAltBox.Visibility = Visibility.Visible;
                lblBestAlternativeMove.Text = bestAlt;
                lblBestAlternativeExplain.Text = !string.IsNullOrEmpty(bestAltReason) ? $"({bestAltReason})" : "";
                lastBestAltCoords = bestAltCoords;
            }
            else
            {
                pnlBestAltBox.Visibility = Visibility.Collapsed;
                lastBestAltCoords = null;
            }
        }

        private void BtnStockfishHint_Click(object sender, RoutedEventArgs e)
        {
            if (!isCustomMode) return;
            if (stockfishEvaluator == null)
                stockfishEvaluator = new StockfishEvaluator();

            if (!stockfishEvaluator.IsAvailable)
            {
                MessageBox.Show("Stockfish chưa sẵn sàng hoặc không tìm thấy file engine.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Chỉ dùng lastPositionEval nếu nó trùng với lượt của người đang đi
            if (lastPositionEval != null && string.Equals(lastPositionEval.TurnColor, currentTurn, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(lastPositionEval.BestMoveUci))
            {
                var (fR, fC, tR, tC, _) = lastPositionEval.BestMoveCoords;
                if (fR >= 0 && fC >= 0 && tR >= 0 && tC >= 0)
                {
                    hintFromRow = fR;
                    hintFromCol = fC;
                    hintToRow = tR;
                    hintToCol = tC;

                    HighlightStockfishHint(fR, fC, tR, tC);

                    string san = lastPositionEval.BestMoveSan;
                    string score = lastPositionEval.FormattedScore;
                    string who = currentTurn == "white" ? "Trắng" : "Đen";
                    lblStatusTip.Text = $"💡 Gợi ý Stockfish ({who}): {san} (Điểm: {score})";
                    return;
                }
            }

            string turnName = currentTurn == "white" ? "Trắng" : "Đen";
            lblStatusTip.Text = $"💡 Stockfish đang tính toán nước đi tốt nhất cho {turnName}, vui lòng đợi giây lát...";
            string targetTurn = currentTurn;
            _ = Task.Run(async () =>
            {
                string fen = FenHelper.BoardToFen(chessGame, targetTurn);
                var eval = await stockfishEvaluator.EvaluatePositionAsync(fen, targetTurn, chessGame, 400);
                if (eval != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (currentTurn != targetTurn) return; // Lượt đi đã thay đổi trong lúc tính toán

                        lastPositionEval = eval;
                        UpdateEvalBarUI(eval);
                        UpdateStockfishAdvantageUI(eval);
                        var (fR, fC, tR, tC, _) = eval.BestMoveCoords;
                        if (fR >= 0 && fC >= 0 && tR >= 0 && tC >= 0)
                        {
                            hintFromRow = fR;
                            hintFromCol = fC;
                            hintToRow = tR;
                            hintToCol = tC;
                            HighlightStockfishHint(fR, fC, tR, tC);
                            string who = targetTurn == "white" ? "Trắng" : "Đen";
                            lblStatusTip.Text = $"💡 Gợi ý Stockfish ({who}): {eval.BestMoveSan} (Điểm: {eval.FormattedScore})";
                        }
                    });
                }
            });
        }

        private void HighlightStockfishHint(int fR, int fC, int tR, int tC)
        {
            RenderBotModeSelectionAndDots();
            ClearTacticalArrows();

            if (fR >= 0 && fR < 8 && fC >= 0 && fC < 8)
            {
                cellBorders[fR, fC].BorderBrush = new SolidColorBrush(Color.FromRgb(255, 215, 0));
                cellBorders[fR, fC].BorderThickness = new Thickness(3.5);
            }

            if (tR >= 0 && tR < 8 && tC >= 0 && tC < 8)
            {
                cellBorders[tR, tC].BorderBrush = new SolidColorBrush(Color.FromRgb(0, 229, 255));
                cellBorders[tR, tC].BorderThickness = new Thickness(3.5);
                cellBorders[tR, tC].Background = new SolidColorBrush(Color.FromArgb(90, 0, 229, 255));
            }

            DrawStockfishArrow(fR, fC, tR, tC, isBestAlternative: true);
        }

        private void BtnToggleEval_Click(object sender, RoutedEventArgs e)
        {
            isStockfishEvalEnabled = !isStockfishEvalEnabled;

            if (isStockfishEvalEnabled)
            {
                btnToggleEval.Content = "BẬT";
                btnToggleEval.Background = new SolidColorBrush(Color.FromRgb(0, 230, 118));
                btnToggleEval.Foreground = new SolidColorBrush(Color.FromRgb(18, 18, 18));
                pnlEvalBar.Visibility = Visibility.Visible;
                borderLastMoveEval.Visibility = Visibility.Visible;
                if (lastPositionEval != null && string.Equals(lastPositionEval.TurnColor, currentTurn, StringComparison.OrdinalIgnoreCase))
                {
                    UpdateEvalBarUI(lastPositionEval);
                    UpdateStockfishAdvantageUI(lastPositionEval);
                }
                else
                {
                    EnqueuePositionEvaluation(isInitial: true);
                }
            }
            else
            {
                btnToggleEval.Content = "TẮT";
                btnToggleEval.Background = new SolidColorBrush(Color.FromRgb(84, 80, 122));
                btnToggleEval.Foreground = Brushes.White;
                pnlEvalBar.Visibility = Visibility.Collapsed;
                borderLastMoveEval.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnToggleTts_Click(object sender, RoutedEventArgs e)
        {
            SpeechService.IsTtsEnabled = !SpeechService.IsTtsEnabled;
            if (SpeechService.IsTtsEnabled)
            {
                btnToggleTts.Content = "🔊";
                btnToggleTts.Foreground = new SolidColorBrush(Color.FromRgb(0, 229, 255));
                btnToggleTts.ToolTip = "Giọng đọc bình luận: ĐANG BẬT (Bấm để tắt)";
                SpeechService.SpeakAsync("Đã bật giọng đọc");
            }
            else
            {
                SpeechService.Stop();
                btnToggleTts.Content = "🔇";
                btnToggleTts.Foreground = new SolidColorBrush(Color.FromRgb(165, 162, 194));
                btnToggleTts.ToolTip = "Giọng đọc bình luận: ĐÃ TẮT (Bấm để bật)";
            }
        }

        private void ClearCellEvaluationBadges()
        {
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    if (cellEvalBadges[r, c] != null)
                    {
                        cellEvalBadges[r, c].Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        private void SetCellEvaluationBadge(int row, int col, string? badge, string? colorHex, string? tooltipText = null)
        {
            ClearCellEvaluationBadges();

            if (row < 0 || row >= 8 || col < 0 || col >= 8 || string.IsNullOrEmpty(badge))
                return;

            Color badgeColor;
            try { badgeColor = (Color)ColorConverter.ConvertFromString(colorHex ?? "#00E676"); }
            catch { badgeColor = Color.FromRgb(0, 230, 118); }

            var border = cellEvalBadges[row, col];
            var txt = cellEvalBadgeTexts[row, col];
            if (border != null && txt != null)
            {
                border.Background = new SolidColorBrush(badgeColor);
                txt.Text = badge;
                bool isDark = badge == "??" || badge == "?";
                txt.Foreground = isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(18, 18, 18));
                if (!string.IsNullOrEmpty(tooltipText))
                {
                    border.ToolTip = tooltipText;
                }
                border.Visibility = Visibility.Visible;
            }
        }

        private Point GetCellCenter(int r, int c)
        {
            var (vRow, vCol) = ToVisual(r, c);
            return new Point(vCol * 70.0 + 35.0, vRow * 70.0 + 35.0);
        }

        private void ClearTacticalArrows()
        {
            arrowCanvas?.Children.Clear();
        }

        private void DrawLShapedArrow(Point pStart, Point pEnd, Brush strokeBrush, Brush fillBrush, double thickness = 5.5)
        {
            if (arrowCanvas == null) return;

            double dx = pEnd.X - pStart.X;
            double dy = pEnd.Y - pStart.Y;
            if (Math.Abs(dx) < 1.0 || Math.Abs(dy) < 1.0)
            {
                DrawStraightArrow(pStart, pEnd, strokeBrush, fillBrush, thickness);
                return;
            }

            Point pCorner;
            double dirX, dirY;

            if (Math.Abs(dy) >= Math.Abs(dx))
            {
                pCorner = new Point(pStart.X, pEnd.Y);
                dirX = Math.Sign(dx);
                dirY = 0;
            }
            else
            {
                pCorner = new Point(pEnd.X, pStart.Y);
                dirX = 0;
                dirY = Math.Sign(dy);
            }

            double tipOffset = 12.0;
            Point pTip = new Point(pEnd.X - dirX * tipOffset, pEnd.Y - dirY * tipOffset);
            Point pBase = new Point(pTip.X - dirX * 16.0, pTip.Y - dirY * 16.0);

            double perpX = -dirY;
            double perpY = dirX;
            Point pLeft = new Point(pBase.X + perpX * 9.0, pBase.Y + perpY * 9.0);
            Point pRight = new Point(pBase.X - perpX * 9.0, pBase.Y - perpY * 9.0);

            var polyline = new Polyline
            {
                Points = new PointCollection { pStart, pCorner, pBase },
                Stroke = strokeBrush,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Flat,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };
            arrowCanvas.Children.Add(polyline);

            var head = new Polygon
            {
                Points = new PointCollection { pTip, pLeft, pRight },
                Fill = fillBrush,
                Stroke = strokeBrush,
                StrokeThickness = 1.0,
                StrokeLineJoin = PenLineJoin.Miter,
                IsHitTestVisible = false
            };
            arrowCanvas.Children.Add(head);
        }

        private void DrawStraightArrow(Point pStart, Point pEnd, Brush strokeBrush, Brush fillBrush, double thickness = 5.0)
        {
            if (arrowCanvas == null) return;

            Vector v = pEnd - pStart;
            double len = v.Length;
            if (len < 15.0) return;

            Vector u = v / len;
            Vector perp = new Vector(-u.Y, u.X);

            double tipOffset = 12.0;
            Point pTip = pEnd - u * tipOffset;
            Point pBase = pTip - u * 16.0;
            Point pLeft = pBase + perp * 9.0;
            Point pRight = pBase - perp * 9.0;

            var line = new Line
            {
                X1 = pStart.X,
                Y1 = pStart.Y,
                X2 = pBase.X,
                Y2 = pBase.Y,
                Stroke = strokeBrush,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Flat,
                IsHitTestVisible = false
            };
            arrowCanvas.Children.Add(line);

            var head = new Polygon
            {
                Points = new PointCollection { pTip, pLeft, pRight },
                Fill = fillBrush,
                Stroke = strokeBrush,
                StrokeThickness = 1.0,
                StrokeLineJoin = PenLineJoin.Miter,
                IsHitTestVisible = false
            };
            arrowCanvas.Children.Add(head);
        }

        private void DrawStockfishArrow(int fromR, int fromC, int toR, int toC, bool isBestAlternative = false)
        {
            if (fromR < 0 || toR < 0 || fromC < 0 || toC < 0) return;
            if (arrowCanvas == null) return;

            var pFrom = GetCellCenter(fromR, fromC);
            var pTo = GetCellCenter(toR, toC);

            string[,] board = chessGame.GetBoardState();
            string piece = (fromR < 8 && fromC < 8) ? board[fromR, fromC] : "";
            bool isKnight = piece.Contains("Knight", StringComparison.OrdinalIgnoreCase);

            Color arrowColor = isBestAlternative ? Color.FromArgb(235, 0, 229, 255) : Color.FromArgb(235, 0, 230, 118);
            var brush = new SolidColorBrush(arrowColor);
            brush.Freeze();

            if (isKnight)
            {
                DrawLShapedArrow(pFrom, pTo, brush, brush, thickness: 5.5);
            }
            else
            {
                DrawStraightArrow(pFrom, pTo, brush, brush, thickness: 5.0);
            }
        }

        private void UpdateProspectiveOrActiveArrows(int step)
        {
            ClearTacticalArrows();
            // Arrows ONLY appear during post-game replay, never during live play
            if (!isReplayMode && !isHandlingGameOver) return;

            var currentMoves = (isReplayMode ? replayRecord?.moves : localMoveHistory) ?? replayRecord?.moves;
            if (currentMoves == null) return;

            // 1. Prospective tactical threat arrow for upcoming move (step + 1)
            int nextStep = step + 1;
            if (nextStep < replaySnapshots.Count && nextStep < replayStepToMoveIndex.Count)
            {
                int nextMoveIdx = replayStepToMoveIndex[nextStep];
                bool nextIsWhite = nextStep < replayStepIsWhite.Count ? replayStepIsWhite[nextStep] : true;
                if (nextMoveIdx >= 0 && nextMoveIdx < currentMoves.Count)
                {
                    var nextEntry = currentMoves[nextMoveIdx];
                    string? classStr = nextIsWhite ? nextEntry.whiteClassification : nextEntry.blackClassification;
                    MoveClassification? parsedClass = null;
                    if (!string.IsNullOrEmpty(classStr) && Enum.TryParse<MoveClassification>(classStr, out var cVal))
                        parsedClass = cVal;

                    string? nextMoveText = nextIsWhite ? nextEntry.whiteMove : nextEntry.blackMove;
                    bool isNextMate = (!string.IsNullOrEmpty(nextMoveText) && nextMoveText.Contains("#")) ||
                                      (nextEntry.whiteEval?.Contains("M0") == true || nextEntry.blackEval?.Contains("M0") == true);

                    // Chiếu hết không hiện mũi tên
                    if (!isNextMate)
                    {
                        // If player made a mistake or blunder, check if Stockfish had a high-impact tactical alternative
                        var bestCoords = nextIsWhite ? nextEntry.whiteBestAltCoords : nextEntry.blackBestAltCoords;
                        if (bestCoords != null && (parsedClass == MoveClassification.Mistake || parsedClass == MoveClassification.Blunder || parsedClass == MoveClassification.Inaccuracy))
                        {
                            var (bFromR, bFromC, bToR, bToC, _) = bestCoords.Value;
                            if (bFromR >= 0 && bToR >= 0)
                            {
                                string boardPiece = chessGame.GetBoardState()[bFromR, bFromC];
                                if (TryDrawHighImpactTacticalArrow(bFromR, bFromC, bToR, bToC, boardPiece, MoveClassification.Best, isProspective: true, isCheckmate: false))
                                {
                                    return;
                                }
                            }
                        }

                        // Or if next move itself was high impact (Best / Brilliant)
                        var (fR, fC, tR, tC) = GetMoveCoordinates(nextEntry, nextIsWhite);
                        string? piece = nextIsWhite ? nextEntry.whitePiece : nextEntry.blackPiece;
                        if (fR >= 0 && tR >= 0 && (parsedClass == MoveClassification.Brilliant || parsedClass == MoveClassification.Best))
                        {
                            if (TryDrawHighImpactTacticalArrow(fR, fC, tR, tC, piece, parsedClass, isProspective: true, isCheckmate: false))
                            {
                                return;
                            }
                        }
                    }
                }
            }

            // 2. Active threat arrows on the piece that just landed at 'step'
            if (step > 0 && step < replayStepToMoveIndex.Count)
            {
                int currMoveIdx = replayStepToMoveIndex[step];
                bool currIsWhite = step < replayStepIsWhite.Count ? replayStepIsWhite[step] : true;
                if (currMoveIdx >= 0 && currMoveIdx < currentMoves.Count)
                {
                    var currEntry = currentMoves[currMoveIdx];
                    string? classStr = currIsWhite ? currEntry.whiteClassification : currEntry.blackClassification;
                    MoveClassification? parsedClass = null;
                    if (!string.IsNullOrEmpty(classStr) && Enum.TryParse<MoveClassification>(classStr, out var cVal))
                        parsedClass = cVal;

                    string? currMoveText = currIsWhite ? currEntry.whiteMove : currEntry.blackMove;
                    bool isCurrMate = (!string.IsNullOrEmpty(currMoveText) && currMoveText.Contains("#")) ||
                                      (currEntry.whiteEval?.Contains("M0") == true || currEntry.blackEval?.Contains("M0") == true);

                    // Chiếu hết không hiện mũi tên; chỉ hiện cho nước đột phá (Brilliant hoặc Best)
                    if (!isCurrMate && (parsedClass == MoveClassification.Brilliant || parsedClass == MoveClassification.Best))
                    {
                        var (fR, fC, tR, tC) = GetMoveCoordinates(currEntry, currIsWhite);
                        string? piece = currIsWhite ? currEntry.whitePiece : currEntry.blackPiece;
                        if (fR >= 0 && tR >= 0)
                        {
                            TryDrawHighImpactTacticalArrow(fR, fC, tR, tC, piece, parsedClass, isProspective: false, isCheckmate: false);
                        }
                    }
                }
            }
        }

        private bool TryDrawHighImpactTacticalArrow(int fromR, int fromC, int toR, int toC, string? piece, MoveClassification? classification, bool isProspective, bool isCheckmate = false)
        {
            // 1. Chiếu hết không bao giờ hiện mũi tên
            if (isCheckmate) return false;
            if (fromR < 0 || toR < 0 || fromC < 0 || toC < 0) return false;
            var board = chessGame.GetBoardState();

            string movingPiece = "";
            if (isProspective && fromR < 8 && fromC < 8)
                movingPiece = board[fromR, fromC];
            else if (!isProspective && toR < 8 && toC < 8)
                movingPiece = board[toR, toC];

            if (string.IsNullOrEmpty(movingPiece) && !string.IsNullOrEmpty(piece))
                movingPiece = piece;
            if (string.IsNullOrEmpty(movingPiece)) return false;

            bool isWhite = movingPiece.StartsWith("W_", StringComparison.OrdinalIgnoreCase);
            string oppColor = isWhite ? "black" : "white";
            if (chessGame.IsCheckmate(oppColor))
            {
                return false;
            }

            var threats = GetThreatenedPiecesFromSquare(toR, toC, movingPiece, board);

            // Filtering for truly breakthrough moves (chỉ những nước đi thật sự đột phá mới hiện mũi tên):
            // 1. Brilliant move
            bool isBrilliant = classification == MoveClassification.Brilliant;

            // 2. Fork attacking >= 2 pieces (có ít nhất 1 mục tiêu giá trị: Hậu, Xe, Tượng, Mã, Vua)
            bool isFork = threats.Count >= 2 && threats.Any(t =>
                t.piece.Contains("King", StringComparison.OrdinalIgnoreCase) ||
                t.piece.Contains("Queen", StringComparison.OrdinalIgnoreCase) ||
                t.piece.Contains("Rook", StringComparison.OrdinalIgnoreCase) ||
                t.piece.Contains("Bishop", StringComparison.OrdinalIgnoreCase) ||
                t.piece.Contains("Knight", StringComparison.OrdinalIgnoreCase));

            // 3. High value threat: đe doạ quân giá trị cao hơn, hoặc chiếu Vua (nhưng không phải chiếu hết), hoặc đe doạ Hậu / Xe
            int movingVal = GetPieceValueForThreat(movingPiece);
            bool isHighValueThreat = threats.Count >= 1 && threats.Any(t =>
            {
                int targetVal = GetPieceValueForThreat(t.piece);
                bool isKing = t.piece.Contains("King", StringComparison.OrdinalIgnoreCase);
                bool isQueen = t.piece.Contains("Queen", StringComparison.OrdinalIgnoreCase);
                bool isRook = t.piece.Contains("Rook", StringComparison.OrdinalIgnoreCase);

                if (targetVal > movingVal) return true; // Đe doạ quân lớn hơn (ví dụ Tốt/Mã đe doạ Xe/Hậu/Tượng)
                if (isKing) return true; // Chiếu Vua
                if (isQueen) return true; // Đe doạ Hậu
                if (isRook && !movingPiece.Contains("Queen", StringComparison.OrdinalIgnoreCase)) return true; // Đe doạ Xe

                return false;
            });

            if (!isFork && !isHighValueThreat && !isBrilliant)
            {
                // Các nước đi bình thường không hiện mũi tên
                return false;
            }

            bool isKnight = movingPiece.Contains("Knight", StringComparison.OrdinalIgnoreCase);
            var pFrom = GetCellCenter(fromR, fromC);
            var pTo = GetCellCenter(toR, toC);

            // Vẽ mũi tên nước đi (Cyan cho Mã, Vàng kim cho các quân khác)
            if (isKnight)
            {
                var moveBrush = new SolidColorBrush(Color.FromArgb(220, 0, 229, 255)); // Vibrant Cyan
                moveBrush.Freeze();
                DrawLShapedArrow(pFrom, pTo, moveBrush, moveBrush, thickness: 5.5);
            }
            else
            {
                var moveBrush = new SolidColorBrush(Color.FromArgb(200, 255, 215, 0)); // Gold
                moveBrush.Freeze();
                DrawStraightArrow(pFrom, pTo, moveBrush, moveBrush, thickness: 5.0);
            }

            // Vẽ mũi tên đỏ đe doạ từ ô đích tới các quân đối phương bị đe doạ
            var threatBrush = new SolidColorBrush(Color.FromArgb(235, 255, 23, 68)); // Bright Red
            threatBrush.Freeze();

            foreach (var threat in threats)
            {
                int tVal = GetPieceValueForThreat(threat.piece);
                bool isSignificant = tVal >= movingVal ||
                    threat.piece.Contains("King", StringComparison.OrdinalIgnoreCase) ||
                    threat.piece.Contains("Queen", StringComparison.OrdinalIgnoreCase) ||
                    threat.piece.Contains("Rook", StringComparison.OrdinalIgnoreCase) ||
                    threats.Count >= 2;

                if (!isSignificant && !isBrilliant) continue;

                var pEnemy = GetCellCenter(threat.r, threat.c);
                if (isKnight)
                {
                    DrawLShapedArrow(pTo, pEnemy, threatBrush, threatBrush, thickness: 4.5);
                }
                else
                {
                    DrawStraightArrow(pTo, pEnemy, threatBrush, threatBrush, thickness: 4.5);
                }
            }

            return true;
        }

        private List<(int r, int c, string piece)> GetThreatenedPiecesFromSquare(int r, int c, string piece, string[,] board)
        {
            var list = new List<(int r, int c, string piece)>();
            if (string.IsNullOrEmpty(piece) || r < 0 || r >= 8 || c < 0 || c >= 8) return list;

            bool isWhite = piece.StartsWith("W_", StringComparison.OrdinalIgnoreCase);
            string oppPrefix = isWhite ? "B_" : "W_";

            if (piece.Contains("Knight", StringComparison.OrdinalIgnoreCase))
            {
                int[] dr = { -2, -2, -1, -1, 1, 1, 2, 2 };
                int[] dc = { -1, 1, -2, 2, -2, 2, -1, 1 };
                for (int i = 0; i < 8; i++)
                {
                    int tr = r + dr[i], tc = c + dc[i];
                    if (tr >= 0 && tr < 8 && tc >= 0 && tc < 8)
                    {
                        string target = board[tr, tc];
                        if (!string.IsNullOrEmpty(target) && target.StartsWith(oppPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            list.Add((tr, tc, target));
                        }
                    }
                }
            }
            else if (piece.Contains("Bishop", StringComparison.OrdinalIgnoreCase) || piece.Contains("Queen", StringComparison.OrdinalIgnoreCase))
            {
                int[] dr = { -1, -1, 1, 1 };
                int[] dc = { -1, 1, -1, 1 };
                for (int i = 0; i < 4; i++)
                {
                    int tr = r + dr[i], tc = c + dc[i];
                    while (tr >= 0 && tr < 8 && tc >= 0 && tc < 8)
                    {
                        string target = board[tr, tc];
                        if (!string.IsNullOrEmpty(target))
                        {
                            if (target.StartsWith(oppPrefix, StringComparison.OrdinalIgnoreCase))
                                list.Add((tr, tc, target));
                            break;
                        }
                        tr += dr[i];
                        tc += dc[i];
                    }
                }
            }

            if (piece.Contains("Rook", StringComparison.OrdinalIgnoreCase) || piece.Contains("Queen", StringComparison.OrdinalIgnoreCase))
            {
                int[] dr = { -1, 1, 0, 0 };
                int[] dc = { 0, 0, -1, 1 };
                for (int i = 0; i < 4; i++)
                {
                    int tr = r + dr[i], tc = c + dc[i];
                    while (tr >= 0 && tr < 8 && tc >= 0 && tc < 8)
                    {
                        string target = board[tr, tc];
                        if (!string.IsNullOrEmpty(target))
                        {
                            if (target.StartsWith(oppPrefix, StringComparison.OrdinalIgnoreCase))
                                list.Add((tr, tc, target));
                            break;
                        }
                        tr += dr[i];
                        tc += dc[i];
                    }
                }
            }
            else if (piece.Contains("Pawn", StringComparison.OrdinalIgnoreCase))
            {
                int pDr = isWhite ? -1 : 1;
                int[] pDc = { -1, 1 };
                for (int i = 0; i < 2; i++)
                {
                    int tr = r + pDr, tc = c + pDc[i];
                    if (tr >= 0 && tr < 8 && tc >= 0 && tc < 8)
                    {
                        string target = board[tr, tc];
                        if (!string.IsNullOrEmpty(target) && target.StartsWith(oppPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            list.Add((tr, tc, target));
                        }
                    }
                }
            }

            return list;
        }

        private static int GetPieceValueForThreat(string piece)
        {
            if (string.IsNullOrEmpty(piece)) return 0;
            if (piece.Contains("King", StringComparison.OrdinalIgnoreCase)) return 100;
            if (piece.Contains("Queen", StringComparison.OrdinalIgnoreCase)) return 9;
            if (piece.Contains("Rook", StringComparison.OrdinalIgnoreCase)) return 5;
            if (piece.Contains("Bishop", StringComparison.OrdinalIgnoreCase) || piece.Contains("Knight", StringComparison.OrdinalIgnoreCase)) return 3;
            if (piece.Contains("Pawn", StringComparison.OrdinalIgnoreCase)) return 1;
            return 0;
        }

        private void ShowTacticalArrowsForMove(int fromR, int fromC, int toR, int toC, string? piece = null, MoveClassification? classification = null, bool isCheckmate = false)
        {
            ClearTacticalArrows();
            if (!isReplayMode && !isHandlingGameOver) return;
            if (isCheckmate) return;
            TryDrawHighImpactTacticalArrow(fromR, fromC, toR, toC, piece, classification, isProspective: false, isCheckmate: isCheckmate);
        }

        private static bool toColValid(int c) => c >= 0 && c < 8;

        private (int fromRow, int fromCol, int toRow, int toCol) GetMoveCoordinates(MoveHistoryEntry item, bool isWhite)
        {
            string moveText = isWhite ? item.whiteMove : item.blackMove;
            if (string.IsNullOrWhiteSpace(moveText))
                return (-1, -1, -1, -1);

            string s = moveText.Trim();
            if (s.Contains("O-O-O", StringComparison.OrdinalIgnoreCase))
            {
                int r = isWhite ? 7 : 0;
                return (r, 4, r, 2);
            }
            if (s.Contains("O-O", StringComparison.OrdinalIgnoreCase))
            {
                int r = isWhite ? 7 : 0;
                return (r, 4, r, 6);
            }

            if (currentReplayStep > 0 && currentReplayStep < replaySnapshots.Count)
            {
                var prev = replaySnapshots[currentReplayStep - 1];
                var curr = replaySnapshots[currentReplayStep];
                string colorPrefix = isWhite ? "W_" : "B_";

                int fR = -1, fC = -1, tR = -1, tC = -1;
                for (int r = 0; r < 8; r++)
                {
                    for (int c = 0; c < 8; c++)
                    {
                        string k = $"{r}_{c}";
                        string pVal = prev.TryGetValue(k, out var pv) ? pv : "";
                        string cVal = curr.TryGetValue(k, out var cv) ? cv : "";

                        if (pVal != cVal)
                        {
                            if (pVal.StartsWith(colorPrefix, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(cVal))
                            {
                                fR = r; fC = c;
                            }
                            else if (cVal.StartsWith(colorPrefix, StringComparison.OrdinalIgnoreCase))
                            {
                                tR = r; tC = c;
                            }
                        }
                    }
                }

                if (fR >= 0 && tR >= 0)
                {
                    return (fR, fC, tR, tC);
                }
            }

            return ParseDestinationSquare(moveText, isWhite);
        }

        private static (int fromRow, int fromCol, int toRow, int toCol) ParseDestinationSquare(string moveText, bool isWhite)
        {
            if (string.IsNullOrWhiteSpace(moveText))
                return (-1, -1, -1, -1);

            string s = moveText.Trim();
            if (s.Contains("O-O-O", StringComparison.OrdinalIgnoreCase))
            {
                int r = isWhite ? 7 : 0;
                return (r, 4, r, 2);
            }
            if (s.Contains("O-O", StringComparison.OrdinalIgnoreCase))
            {
                int r = isWhite ? 7 : 0;
                return (r, 4, r, 6);
            }

            var match = Regex.Match(s, @"([a-hA-H])([1-8])");
            if (match.Success)
            {
                char file = char.ToLowerInvariant(match.Groups[1].Value[0]);
                int rank = match.Groups[2].Value[0] - '0';
                int toCol = file - 'a';
                int toRow = 8 - rank;
                return (-1, -1, toRow, toCol);
            }

            return (-1, -1, -1, -1);
        }

        private static void ApplyDictToGame(ChessGame game, Dictionary<string, string> dict)
        {
            var board = game.GetBoardState();
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    string key = $"{r}_{c}";
                    board[r, c] = dict.TryGetValue(key, out var val) ? val : "";
                }
            }
        }

        private async Task EnsureRecordMovesEvaluatedAsync(GameRecord record)
        {
            if (record?.moves == null || stockfishEvaluator == null || !stockfishEvaluator.IsAvailable)
                return;

            await Task.Run(async () =>
            {
                var simGame = new ChessGame();
                simGame.InitializeBoard();
                PositionEvaluation? prevEval = null;

                try
                {
                    string initialFen = FenHelper.BoardToFen(simGame, "white");
                    prevEval = await stockfishEvaluator.EvaluatePositionAsync(initialFen, "white", simGame, 200).ConfigureAwait(false);
                }
                catch { }

                bool isCustom = string.Equals(record.gameMode, "Custom", StringComparison.OrdinalIgnoreCase);
                string myCol = "white";
                if (!string.IsNullOrEmpty(record.playerBlack) && (record.playerBlack.StartsWith("bot_", StringComparison.OrdinalIgnoreCase) || record.playerBlack.StartsWith("ai", StringComparison.OrdinalIgnoreCase)))
                {
                    myCol = "white";
                }
                else if (!string.IsNullOrEmpty(record.playerWhite) && (record.playerWhite.StartsWith("bot_", StringComparison.OrdinalIgnoreCase) || record.playerWhite.StartsWith("ai", StringComparison.OrdinalIgnoreCase)))
                {
                    myCol = "black";
                }
                else if (!string.IsNullOrEmpty(this.playerName))
                {
                    if (string.Equals(record.playerWhite, this.playerName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(record.displayNameWhite, this.playerName, StringComparison.OrdinalIgnoreCase))
                    {
                        myCol = "white";
                    }
                    else if (string.Equals(record.playerBlack, this.playerName, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(record.displayNameBlack, this.playerName, StringComparison.OrdinalIgnoreCase))
                    {
                        myCol = "black";
                    }
                    else
                    {
                        myCol = this.myColor ?? "white";
                    }
                }
                else
                {
                    myCol = this.myColor ?? "white";
                }

                bool hasNewEval = false;
                for (int i = 0; i < record.moves.Count; i++)
                {
                    var m = record.moves[i];

                    // White move
                    if (!string.IsNullOrWhiteSpace(m.whiteMove))
                    {
                        if (string.IsNullOrEmpty(m.whiteClassification))
                        {
                            try
                            {
                                if (m.boardAfterWhite != null && m.boardAfterWhite.Count > 0)
                                {
                                    ApplyDictToGame(simGame, m.boardAfterWhite);
                                }
                                var coords = ParseDestinationSquare(m.whiteMove, true);
                                string fenAfter = FenHelper.BoardToFen(simGame, "black");
                                var evalAfter = await stockfishEvaluator.EvaluatePositionAsync(fenAfter, "black", simGame, 200).ConfigureAwait(false);

                                bool isCheckmate = simGame.IsCheckmate("black") || m.whiteMove.Contains("#");
                                bool isCheck = isCheckmate || simGame.IsInCheck("black", simGame.GetBoardState()) || m.whiteMove.Contains("+");

                                if (evalAfter == null && isCheckmate)
                                {
                                    evalAfter = new PositionEvaluation
                                    {
                                        WhiteScore = 100.0,
                                        MateIn = 0,
                                        TurnColor = "black",
                                        Depth = prevEval?.Depth ?? 1
                                    };
                                }

                                string uci = (coords.fromRow >= 0 && coords.toRow >= 0) ? FenHelper.MoveToUci(coords.fromRow, coords.fromCol, coords.toRow, coords.toCol) : "";
                                var assessment = StockfishEvaluator.ClassifyMove(
                                    prevEval, evalAfter, "white", uci, m.whitePiece ?? "",
                                    coords.fromRow, coords.fromCol, coords.toRow, coords.toCol,
                                    isCheck: isCheck,
                                    isCheckmate: isCheckmate,
                                    gameContext: simGame,
                                    isCustomMode: isCustom,
                                    isMyMove: (myCol == "white"));

                                if (assessment != null)
                                {
                                    m.whiteEval = assessment.NewPositionEval?.FormattedScore;
                                    m.whiteClassification = assessment.Classification.ToString();
                                    m.whiteBadge = assessment.Badge;
                                    m.whiteColorHex = assessment.ColorHex;
                                    m.whiteBestAlternative = assessment.BestAlternative;
                                    m.whiteBestAlternativeReason = assessment.BestAlternativeReason;
                                    m.whiteBestAltCoords = assessment.BestAlternativeCoords;
                                    m.whiteCommentary = assessment.Commentary;
                                    m.whiteVoiceClips = assessment.VoiceClips;
                                    hasNewEval = true;
                                }
                                prevEval = evalAfter;
                            }
                            catch { }
                        }
                        else if (m.boardAfterWhite != null && m.boardAfterWhite.Count > 0)
                        {
                            ApplyDictToGame(simGame, m.boardAfterWhite);
                        }
                    }

                    // Black move
                    if (!string.IsNullOrWhiteSpace(m.blackMove))
                    {
                        if (string.IsNullOrEmpty(m.blackClassification))
                        {
                            try
                            {
                                if (m.boardAfterBlack != null && m.boardAfterBlack.Count > 0)
                                {
                                    ApplyDictToGame(simGame, m.boardAfterBlack);
                                }
                                var coords = ParseDestinationSquare(m.blackMove, false);
                                string fenAfter = FenHelper.BoardToFen(simGame, "white");
                                var evalAfter = await stockfishEvaluator.EvaluatePositionAsync(fenAfter, "white", simGame, 200).ConfigureAwait(false);

                                bool isCheckmate = simGame.IsCheckmate("white") || m.blackMove.Contains("#");
                                bool isCheck = isCheckmate || simGame.IsInCheck("white", simGame.GetBoardState()) || m.blackMove.Contains("+");

                                if (evalAfter == null && isCheckmate)
                                {
                                    evalAfter = new PositionEvaluation
                                    {
                                        WhiteScore = -100.0,
                                        MateIn = 0,
                                        TurnColor = "white",
                                        Depth = prevEval?.Depth ?? 1
                                    };
                                }

                                string uci = (coords.fromRow >= 0 && coords.toRow >= 0) ? FenHelper.MoveToUci(coords.fromRow, coords.fromCol, coords.toRow, coords.toCol) : "";
                                var assessment = StockfishEvaluator.ClassifyMove(
                                    prevEval, evalAfter, "black", uci, m.blackPiece ?? "",
                                    coords.fromRow, coords.fromCol, coords.toRow, coords.toCol,
                                    isCheck: isCheck,
                                    isCheckmate: isCheckmate,
                                    gameContext: simGame,
                                    isCustomMode: isCustom,
                                    isMyMove: (myCol == "black"));

                                if (assessment != null)
                                {
                                    m.blackEval = assessment.NewPositionEval?.FormattedScore;
                                    m.blackClassification = assessment.Classification.ToString();
                                    m.blackBadge = assessment.Badge;
                                    m.blackColorHex = assessment.ColorHex;
                                    m.blackBestAlternative = assessment.BestAlternative;
                                    m.blackBestAlternativeReason = assessment.BestAlternativeReason;
                                    m.blackBestAltCoords = assessment.BestAlternativeCoords;
                                    m.blackCommentary = assessment.Commentary;
                                    m.blackVoiceClips = assessment.VoiceClips;
                                    hasNewEval = true;
                                }
                                prevEval = evalAfter;
                            }
                            catch { }
                        }
                        else if (m.boardAfterBlack != null && m.boardAfterBlack.Count > 0)
                        {
                            ApplyDictToGame(simGame, m.boardAfterBlack);
                        }
                    }

                    if (hasNewEval)
                    {
                        int currentIdx = i;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            RenderMoveHistoryUI(record.moves, showTime: true);
                            if (currentReplayStep > 0 && currentReplayStep < replaySnapshots.Count)
                            {
                                int moveIdx = currentReplayStep < replayStepToMoveIndex.Count ? replayStepToMoveIndex[currentReplayStep] : 0;
                                bool isWhite = currentReplayStep < replayStepIsWhite.Count ? replayStepIsWhite[currentReplayStep] : true;
                                if (moveIdx == currentIdx && moveIdx < record.moves.Count)
                                {
                                    ShowHistoricalMoveCommentary(record.moves[moveIdx], isWhite);
                                }
                            }
                        });
                    }
                }
            });
        }

        #endregion
    }
}