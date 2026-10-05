using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ChessGame_PJ
{
    public static class AuthService
    {
        public static UserData? CurrentUser { get; private set; }
        public static bool IsLoggedIn => CurrentUser != null;

        private static readonly string SessionDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChessGamePJ"
        );
        private static readonly string SessionFilePath = Path.Combine(SessionDirectory, "session.json");

        private class LocalSession
        {
            public string Username { get; set; } = "";
            public string SessionToken { get; set; } = "";
        }

        // Generate a random salt
        public static string GenerateSalt()
        {
            byte[] saltBytes = new byte[16];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(saltBytes);
            return Convert.ToBase64String(saltBytes);
        }

        // Hash password with salt using PBKDF2 (SHA-256)
        public static string HashPassword(string password, string salt)
        {
            byte[] saltBytes = Convert.FromBase64String(salt);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                saltBytes,
                iterations: 10000,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: 32
            );
            return Convert.ToBase64String(hash);
        }

        public static bool VerifyPassword(string password, string salt, string expectedHash)
        {
            try
            {
                string computed = HashPassword(password, salt);
                return string.Equals(computed, expectedHash);
            }
            catch
            {
                return false;
            }
        }

        // Sign up / Register
        public static async Task<(bool Success, string Message)> RegisterAsync(string username, string password, string displayName, string avatar)
        {
            username = (username ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
                return (false, "Tên tài khoản phải có ít nhất 3 ký tự.");

            foreach (char c in username)
            {
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return (false, "Tên tài khoản chỉ được chứa chữ cái, số và dấu gạch dưới (_).");
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
                return (false, "Mật khẩu phải có ít nhất 4 ký tự.");

            if (string.IsNullOrWhiteSpace(displayName))
                displayName = username;

            if (string.IsNullOrWhiteSpace(avatar))
                avatar = "avatar1.png";

            var existing = await FirebaseClient.GetAsync<UserData>($"users/{username}");
            if (existing != null && !string.IsNullOrEmpty(existing.username))
                return (false, "Tên tài khoản này đã được sử dụng. Vui lòng chọn tên khác.");

            string salt = GenerateSalt();
            string passwordHash = HashPassword(password, salt);
            string sessionToken = Guid.NewGuid().ToString("N");

            var newUser = new UserData
            {
                username = username,
                passwordHash = passwordHash,
                salt = salt,
                displayName = displayName.Trim(),
                avatar = avatar,
                sessionToken = sessionToken,
                elo = 1200,
                wins = 0,
                losses = 0,
                draws = 0,
                createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            bool saved = await FirebaseClient.PutAsync($"users/{username}", newUser);
            if (!saved)
                return (false, "Không thể kết nối đến máy chủ. Vui lòng kiểm tra mạng và thử lại.");

            CurrentUser = newUser;
            SaveLocalSession(username, sessionToken);
            return (true, "Đăng ký tài khoản thành công!");
        }

        // Login
        public static async Task<(bool Success, string Message)> LoginAsync(string username, string password, bool rememberMe = true)
        {
            username = (username ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return (false, "Vui lòng nhập đầy đủ tên tài khoản và mật khẩu.");

            var user = await FirebaseClient.GetAsync<UserData>($"users/{username}");
            if (user == null || string.IsNullOrEmpty(user.username))
                return (false, "Tài khoản không tồn tại.");

            if (!VerifyPassword(password, user.salt, user.passwordHash))
                return (false, "Mật khẩu không chính xác.");

            string sessionToken = Guid.NewGuid().ToString("N");
            user.sessionToken = sessionToken;
            await FirebaseClient.PatchAsync($"users/{username}", new { sessionToken = sessionToken });

            CurrentUser = user;
            if (rememberMe)
            {
                SaveLocalSession(username, sessionToken);
            }
            else
            {
                ClearLocalSession();
            }

            return (true, "Đăng nhập thành công!");
        }

        // Try auto-login from local session
        public static async Task<bool> TryAutoLoginAsync()
        {
            try
            {
                if (!File.Exists(SessionFilePath)) return false;

                string json = await File.ReadAllTextAsync(SessionFilePath);
                var session = JsonSerializer.Deserialize<LocalSession>(json);
                if (session == null || string.IsNullOrWhiteSpace(session.Username) || string.IsNullOrWhiteSpace(session.SessionToken))
                    return false;

                var user = await FirebaseClient.GetAsync<UserData>($"users/{session.Username.ToLowerInvariant()}");
                if (user == null || string.IsNullOrEmpty(user.username))
                    return false;

                if (user.sessionToken == session.SessionToken)
                {
                    CurrentUser = user;
                    return true;
                }
            }
            catch
            {
                // ignore any errors during auto-login
            }
            return false;
        }

        // Logout
        public static void Logout()
        {
            CurrentUser = null;
            ClearLocalSession();
        }

        // Update Avatar
        public static async Task<bool> UpdateAvatarAsync(string newAvatar)
        {
            if (CurrentUser == null) return false;
            CurrentUser.avatar = newAvatar;
            return await FirebaseClient.PatchAsync($"users/{CurrentUser.username}", new { avatar = newAvatar });
        }

        // Get Leaderboard
        public static async Task<List<UserData>> GetLeaderboardAsync(int top = 10)
        {
            try
            {
                var allUsersDict = await FirebaseClient.GetAsync<Dictionary<string, UserData>>("users");
                if (allUsersDict == null || allUsersDict.Count == 0)
                    return new List<UserData>();

                var list = allUsersDict.Values
                    .Where(u => u != null && !string.IsNullOrEmpty(u.username))
                    .OrderByDescending(u => u.elo)
                    .ThenByDescending(u => u.wins)
                    .Take(top)
                    .ToList();

                return list;
            }
            catch
            {
                return new List<UserData>();
            }
        }

        // Record game result: Winner gets +points, Loser gets -points (ONLY if isMatchmaking == true)
        public static async Task RecordGameResultAsync(string winnerName, string loserName, bool isMatchmaking = false, int points = 10)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(winnerName) || string.IsNullOrWhiteSpace(loserName) ||
                    string.Equals(winnerName.Trim(), loserName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(winnerName))
                {
                    string wUser = winnerName.Trim().ToLowerInvariant();
                    var userW = await FirebaseClient.GetAsync<UserData>($"users/{wUser}");
                    if (userW != null && !string.IsNullOrEmpty(userW.username))
                    {
                        int newWins = userW.wins + 1;
                        int newElo = isMatchmaking ? (userW.elo + points) : userW.elo;

                        var patchObj = isMatchmaking
                            ? (object)new { elo = newElo, wins = newWins }
                            : (object)new { wins = newWins };

                        await FirebaseClient.PatchAsync($"users/{wUser}", patchObj);

                        if (CurrentUser != null && CurrentUser.username.Equals(wUser, StringComparison.OrdinalIgnoreCase))
                        {
                            CurrentUser.elo = newElo;
                            CurrentUser.wins = newWins;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(loserName))
                {
                    string lUser = loserName.Trim().ToLowerInvariant();
                    var userL = await FirebaseClient.GetAsync<UserData>($"users/{lUser}");
                    if (userL != null && !string.IsNullOrEmpty(userL.username))
                    {
                        int newLosses = userL.losses + 1;
                        int newElo = isMatchmaking ? Math.Max(0, userL.elo - points) : userL.elo;

                        var patchObj = isMatchmaking
                            ? (object)new { elo = newElo, losses = newLosses }
                            : (object)new { losses = newLosses };

                        await FirebaseClient.PatchAsync($"users/{lUser}", patchObj);

                        if (CurrentUser != null && CurrentUser.username.Equals(lUser, StringComparison.OrdinalIgnoreCase))
                        {
                            CurrentUser.elo = newElo;
                            CurrentUser.losses = newLosses;
                        }
                    }
                }
            }
            catch { }
        }

        // Record draw result: neither player gains or loses Elo, both get draws + 1
        public static async Task RecordDrawResultAsync(string player1, string player2)
        {
            try
            {
                foreach (var name in new[] { player1, player2 })
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string u = name.Trim().ToLowerInvariant();
                    var user = await FirebaseClient.GetAsync<UserData>($"users/{u}");
                    if (user != null && !string.IsNullOrEmpty(user.username))
                    {
                        int newDraws = user.draws + 1;
                        await FirebaseClient.PatchAsync($"users/{u}", new { draws = newDraws });

                        if (CurrentUser != null && CurrentUser.username.Equals(u, StringComparison.OrdinalIgnoreCase))
                        {
                            CurrentUser.draws = newDraws;
                        }
                    }
                }
            }
            catch { }
        }

        // Record bot win time in seconds. Returns detailed status result.
        public static async Task<ChessGame_PJ.Core.BotWinSaveResult> RecordBotWinDetailedAsync(string difficulty, int timeSeconds)
        {
            try
            {
                if (CurrentUser == null || string.IsNullOrEmpty(CurrentUser.username))
                {
                    return new ChessGame_PJ.Core.BotWinSaveResult { Status = ChessGame_PJ.Core.BotWinRecordStatus.NotLoggedIn };
                }

                string key = difficulty.ToLowerInvariant().Trim();
                string path = $"ai_leaderboards/{key}/{CurrentUser.username}";

                var existing = await FirebaseClient.GetAsync<ChessGame_PJ.Core.BotWinRecord>(path);
                if (existing != null && existing.timeSeconds > 0 && existing.timeSeconds <= timeSeconds)
                {
                    // Existing record is faster or equal
                    return new ChessGame_PJ.Core.BotWinSaveResult
                    {
                        Status = ChessGame_PJ.Core.BotWinRecordStatus.KeptExistingRecord,
                        ExistingBestSeconds = existing.timeSeconds
                    };
                }

                var record = new ChessGame_PJ.Core.BotWinRecord
                {
                    username = CurrentUser.username,
                    displayName = !string.IsNullOrWhiteSpace(CurrentUser.displayName) ? CurrentUser.displayName : CurrentUser.username,
                    avatar = CurrentUser.avatar,
                    timeSeconds = timeSeconds,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                bool saved = await FirebaseClient.PutAsync(path, record);
                if (saved)
                {
                    // Also update user's personal best time field
                    string timeField = key switch
                    {
                        "easy"   => "bestBotTimeEasy",
                        "medium" => "bestBotTimeMedium",
                        "hard"   => "bestBotTimeHard",
                        "expert" => "bestBotTimeExpert",
                        _        => ""
                    };
                    if (!string.IsNullOrEmpty(timeField))
                    {
                        _ = FirebaseClient.PatchAsync($"users/{CurrentUser.username}", new Dictionary<string, object>
                        {
                            [timeField] = timeSeconds
                        });
                        // Update local cache
                        if (CurrentUser != null)
                        {
                            if (key == "easy")   CurrentUser.bestBotTimeEasy   = timeSeconds;
                            if (key == "medium") CurrentUser.bestBotTimeMedium  = timeSeconds;
                            if (key == "hard")   CurrentUser.bestBotTimeHard    = timeSeconds;
                            if (key == "expert") CurrentUser.bestBotTimeExpert  = timeSeconds;
                        }
                    }

                    return new ChessGame_PJ.Core.BotWinSaveResult
                    {
                        Status = ChessGame_PJ.Core.BotWinRecordStatus.NewRecord,
                        ExistingBestSeconds = timeSeconds
                    };
                }
                else
                {
                    return new ChessGame_PJ.Core.BotWinSaveResult { Status = ChessGame_PJ.Core.BotWinRecordStatus.Failed };
                }
            }
            catch
            {
                return new ChessGame_PJ.Core.BotWinSaveResult { Status = ChessGame_PJ.Core.BotWinRecordStatus.Failed };
            }
        }

        // Record bot win time in seconds. Saves personal best (lowest time).
        public static async Task<bool> RecordBotWinAsync(string difficulty, int timeSeconds)
        {
            var res = await RecordBotWinDetailedAsync(difficulty, timeSeconds);
            return res.Status == ChessGame_PJ.Core.BotWinRecordStatus.NewRecord;
        }

        // Get Bot Leaderboard sorted by fastest time (ascending)
        public static async Task<List<ChessGame_PJ.Core.BotWinRecord>> GetBotLeaderboardAsync(string difficulty, int top = 10)
        {
            try
            {
                string key = difficulty.ToLowerInvariant().Trim();
                var dict = await FirebaseClient.GetAsync<Dictionary<string, ChessGame_PJ.Core.BotWinRecord>>($"ai_leaderboards/{key}");
                if (dict == null || dict.Count == 0)
                {
                    return new List<ChessGame_PJ.Core.BotWinRecord>();
                }

                var result = dict.Values
                    .Where(r => r != null && !string.IsNullOrEmpty(r.username) && r.timeSeconds > 0)
                    .OrderBy(r => r.timeSeconds)
                    .ThenBy(r => r.timestamp)
                    .Take(top)
                    .ToList();

                // Các bản ghi kỷ lục chỉ lưu avatar/tên hiển thị tại thời điểm đạt kỷ lục,
                // nên nếu người chơi đổi avatar/tên sau đó thì dữ liệu cũ sẽ bị lỗi thời.
                // Lấy dữ liệu người dùng hiện tại để hiển thị avatar/tên mới nhất trên bảng xếp hạng.
                try
                {
                    var allUsers = await FirebaseClient.GetAsync<Dictionary<string, UserData>>("users");
                    if (allUsers != null)
                    {
                        foreach (var r in result)
                        {
                            var liveUser = allUsers.Values.FirstOrDefault(u =>
                                u != null && !string.IsNullOrEmpty(u.username) &&
                                u.username.Equals(r.username, StringComparison.OrdinalIgnoreCase));
                            if (liveUser != null)
                            {
                                if (!string.IsNullOrWhiteSpace(liveUser.avatar))
                                    r.avatar = liveUser.avatar;
                                if (!string.IsNullOrWhiteSpace(liveUser.displayName))
                                    r.displayName = liveUser.displayName;
                            }
                        }
                    }
                }
                catch { /* Nếu không lấy được dữ liệu người dùng mới nhất thì vẫn giữ dữ liệu cũ trong record */ }

                return result;
            }
            catch
            {
                return new List<ChessGame_PJ.Core.BotWinRecord>();
            }
        }

        private static void SaveLocalSession(string username, string sessionToken)
        {
            try
            {
                if (!Directory.Exists(SessionDirectory))
                    Directory.CreateDirectory(SessionDirectory);

                var session = new LocalSession { Username = username, SessionToken = sessionToken };
                string json = JsonSerializer.Serialize(session);
                File.WriteAllText(SessionFilePath, json);
            }
            catch { }
        }

        private static void ClearLocalSession()
        {
            try
            {
                if (File.Exists(SessionFilePath))
                    File.Delete(SessionFilePath);
            }
            catch { }
        }
    }
}
