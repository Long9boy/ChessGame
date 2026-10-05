using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ChessGame_PJ.Core;

namespace ChessGame_PJ.Views
{
    public partial class MatchmakingDialog : Window
    {
        private readonly string myUsername;
        private readonly string myDisplayName;
        private readonly string myAvatar;
        private readonly int myElo;

        private DispatcherTimer? queueTimer;
        private int elapsedSeconds = 0;
        private bool isExiting = false;

        public string MatchedRoomId { get; private set; } = "";
        public bool IsCreator { get; private set; } = false;
        public bool IsMatched { get; private set; } = false;

        public MatchmakingDialog(string username, string displayName, string avatar, int elo)
        {
            InitializeComponent();
            myUsername = username.ToLowerInvariant().Trim();
            myDisplayName = displayName;
            myAvatar = avatar;
            myElo = elo;

            Loaded += MatchmakingDialog_Loaded;
            Closing += MatchmakingDialog_Closing;
        }

        private async void MatchmakingDialog_Loaded(object sender, RoutedEventArgs e)
        {
            await StartMatchmakingAsync();
        }

        private async Task StartMatchmakingAsync()
        {
            lblStatus.Text = "Đang kiểm tra người chơi trong hàng đợi...";

            try
            {
                // 1. Check if an active waiting opponent exists
                var allTickets = await FirebaseClient.GetAsync<Dictionary<string, MatchmakingTicket>>("matchmaking_queue");
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                MatchmakingTicket? waitingOpponent = null;
                if (allTickets != null)
                {
                    foreach (var kvp in allTickets)
                    {
                        var ticket = kvp.Value;
                        if (ticket != null &&
                            !string.IsNullOrEmpty(ticket.username) &&
                            ticket.username != myUsername &&
                            ticket.status == "waiting" &&
                            (now - ticket.lastHeartbeat) < 15000)
                        {
                            waitingOpponent = ticket;
                            break;
                        }
                    }
                }

                if (waitingOpponent != null)
                {
                    // Match found! Create game room with isMatchmaking = true
                    lblStatus.Text = "Đã tìm thấy đối thủ! Đang khởi tạo trận đấu...";
                    string roomId = "mm_" + Random.Shared.Next(100000, 999999);

                    var game = new ChessGame();
                    var board = game.GetBoardState();
                    var boardDict = new Dictionary<string, string>();
                    for (int r = 0; r < 8; r++)
                        for (int c = 0; c < 8; c++)
                            boardDict[$"{r}_{c}"] = board[r, c] ?? "";

                    var newRoom = new RoomData
                    {
                        player1 = waitingOpponent.username,      // username for Firebase key
                        displayName1 = waitingOpponent.displayName, // display name for UI
                        player2 = myUsername,                    // username for Firebase key
                        displayName2 = myDisplayName,            // display name for UI
                        avatar1 = waitingOpponent.avatar,
                        avatar2 = myAvatar,
                        turn = "white",
                        status = "playing",
                        winner = "",
                        check = "",
                        board = boardDict,
                        isMatchmaking = true,
                        turnStartedAt = now,
                        lastHeartbeat1 = now,
                        lastHeartbeat2 = now,
                        updatedAt = now
                    };

                    bool created = await FirebaseClient.PutAsync($"rooms/{roomId}", newRoom);
                    if (created)
                    {
                        // Notify waiting opponent that they have been matched
                        await FirebaseClient.PatchAsync($"matchmaking_queue/{waitingOpponent.username}", new Dictionary<string, object>
                        {
                            ["status"] = "matched",
                            ["roomId"] = roomId,
                            ["matchedWith"] = myDisplayName
                        });

                        MatchedRoomId = roomId;
                        IsCreator = false; // Player 2 (Black)
                        IsMatched = true;

                        DialogResult = true;
                        Close();
                        return;
                    }
                }

                // 2. If no active opponent found, put current user into queue
                lblStatus.Text = "Đang tìm đối thủ phù hợp...";
                var myTicket = new MatchmakingTicket
                {
                    username = myUsername,
                    displayName = myDisplayName,
                    avatar = myAvatar,
                    elo = myElo,
                    status = "waiting",
                    roomId = "",
                    createdAt = now,
                    lastHeartbeat = now
                };

                await FirebaseClient.PutAsync($"matchmaking_queue/{myUsername}", myTicket);

                // Start timer to pulse heartbeat and check if someone matched with us
                queueTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                queueTimer.Tick += async (s, ev) => await OnQueueTickAsync();
                queueTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi kết nối khi tìm trận: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
        }

        private async Task OnQueueTickAsync()
        {
            if (isExiting) return;

            elapsedSeconds++;
            int mins = elapsedSeconds / 60;
            int secs = elapsedSeconds % 60;
            lblTimer.Text = $"Đang tìm trận: {mins:D2}:{secs:D2}";

            try
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _ = FirebaseClient.PatchAsync($"matchmaking_queue/{myUsername}", new Dictionary<string, object>
                {
                    ["lastHeartbeat"] = now
                });

                var ticket = await FirebaseClient.GetAsync<MatchmakingTicket>($"matchmaking_queue/{myUsername}");
                if (ticket != null && ticket.status == "matched" && !string.IsNullOrEmpty(ticket.roomId))
                {
                    queueTimer?.Stop();
                    lblStatus.Text = $"Đã ghép trận thành công! Vào phòng {ticket.roomId}...";

                    MatchedRoomId = ticket.roomId;
                    IsCreator = true; // Player 1 (White)
                    IsMatched = true;

                    // Clean up my ticket
                    _ = FirebaseClient.DeleteAsync($"matchmaking_queue/{myUsername}");

                    await Task.Delay(400);
                    DialogResult = true;
                    Close();
                }
            }
            catch { }
        }

        private async void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            await CancelQueueAsync();
            DialogResult = false;
            Close();
        }

        private async void MatchmakingDialog_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!IsMatched)
            {
                await CancelQueueAsync();
            }
        }

        private async Task CancelQueueAsync()
        {
            if (isExiting) return;
            isExiting = true;
            queueTimer?.Stop();

            try
            {
                await FirebaseClient.DeleteAsync($"matchmaking_queue/{myUsername}");
            }
            catch { }
        }
    }
}
