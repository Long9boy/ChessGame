using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ChessGame_PJ.Services
{
    public class FriendModel
    {
        public string username { get; set; } = "";
        public string displayName { get; set; } = "";
        public string avatar { get; set; } = "avatar1.png";
        public int elo { get; set; } = 1200;
        public string status { get; set; } = "offline";
        public string activity { get; set; } = "Ngoại tuyến";
        public long lastSeen { get; set; } = 0;
        public bool isOnline => status == "online" && (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastSeen < 120_000);
        public string StatusDisplayText
        {
            get
            {
                if (!isOnline) return "Ngoại tuyến";
                return string.IsNullOrWhiteSpace(activity) ? "Trực tuyến" : activity;
            }
        }
    }

    public class FriendRequestModel
    {
        public string fromUsername { get; set; } = "";
        public string fromDisplayName { get; set; } = "";
        public string fromAvatar { get; set; } = "avatar1.png";
        public long timestamp { get; set; } = 0;
    }

    public class RoomInvite
    {
        public string fromUser { get; set; } = "";
        public string fromDisplayName { get; set; } = "";
        public string fromAvatar { get; set; } = "avatar1.png";
        public string roomId { get; set; } = "";
        public long timestamp { get; set; } = 0;
    }

    public static class FriendService
    {
        // Get list of friends with their live status and details
        public static async Task<List<FriendModel>> GetFriendsAsync(string username)
        {
            var result = new List<FriendModel>();
            if (string.IsNullOrWhiteSpace(username)) return result;

            try
            {
                var dict = await FirebaseClient.GetAsync<Dictionary<string, object>>($"friends/{username.ToLowerInvariant().Trim()}");
                if (dict == null || dict.Count == 0) return result;

                foreach (var friendUser in dict.Keys)
                {
                    try
                    {
                        var u = await FirebaseClient.GetAsync<UserData>($"users/{friendUser.ToLowerInvariant()}");
                        if (u != null && !string.IsNullOrEmpty(u.username))
                        {
                            result.Add(new FriendModel
                            {
                                username = u.username,
                                displayName = !string.IsNullOrWhiteSpace(u.displayName) ? u.displayName : u.username,
                                avatar = !string.IsNullOrWhiteSpace(u.avatar) ? u.avatar : "avatar1.png",
                                elo = u.elo,
                                status = u.status ?? "offline",
                                activity = u.activity ?? "Sảnh chờ",
                                lastSeen = u.lastSeen
                            });
                        }
                    }
                    catch { }
                }

                // Sort: Online first, then by ELO descending
                return result.OrderByDescending(f => f.isOnline).ThenByDescending(f => f.elo).ToList();
            }
            catch
            {
                return result;
            }
        }

        // Add friend (mutual)
        public static async Task<(bool Success, string Message)> AddFriendAsync(string myUsername, string targetUsername)
        {
            myUsername = (myUsername ?? "").Trim().ToLowerInvariant();
            targetUsername = (targetUsername ?? "").Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(myUsername) || string.IsNullOrWhiteSpace(targetUsername))
                return (false, "Vui lòng nhập tên người dùng.");

            if (myUsername == targetUsername)
                return (false, "Bạn không thể tự kết bạn với chính mình.");

            try
            {
                var target = await FirebaseClient.GetAsync<UserData>($"users/{targetUsername}");
                if (target == null || string.IsNullOrEmpty(target.username))
                    return (false, $"Không tìm thấy người chơi '{targetUsername}'.");

                // Save mutual friend entries
                await FirebaseClient.PutAsync($"friends/{myUsername}/{targetUsername}", true);
                await FirebaseClient.PutAsync($"friends/{targetUsername}/{myUsername}", true);

                return (true, $"Đã kết bạn thành công với {target.displayName ?? target.username}!");
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi khi kết bạn: {ex.Message}");
            }
        }

        // ── Friend Requests (cần bên kia chấp nhận mới thành bạn bè) ────────────────

        // Gửi lời mời kết bạn (không kết bạn ngay, chỉ tạo yêu cầu chờ chấp nhận)
        public static async Task<(bool Success, string Message)> SendFriendRequestAsync(string myUsername, string targetUsername)
        {
            myUsername = (myUsername ?? "").Trim().ToLowerInvariant();
            targetUsername = (targetUsername ?? "").Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(myUsername) || string.IsNullOrWhiteSpace(targetUsername))
                return (false, "Vui lòng nhập tên người dùng.");

            if (myUsername == targetUsername)
                return (false, "Bạn không thể tự kết bạn với chính mình.");

            try
            {
                var target = await FirebaseClient.GetAsync<UserData>($"users/{targetUsername}");
                if (target == null || string.IsNullOrEmpty(target.username))
                    return (false, $"Không tìm thấy người chơi '{targetUsername}'.");

                bool alreadyFriend = await IsFriendAsync(myUsername, targetUsername);
                if (alreadyFriend)
                    return (false, $"Bạn và {target.displayName ?? target.username} đã là bạn bè.");

                // Nếu đối phương đã từng gửi lời mời cho mình trước đó -> tự động chấp nhận luôn
                var theirRequestToMe = await FirebaseClient.GetAsync<object>($"friendRequests/{myUsername}/{targetUsername}");
                if (theirRequestToMe != null)
                {
                    bool accepted = await AcceptFriendRequestAsync(myUsername, targetUsername);
                    return accepted
                        ? (true, $"Đã chấp nhận lời mời kết bạn của {target.displayName ?? target.username}!")
                        : (false, "Có lỗi xảy ra khi chấp nhận lời mời kết bạn.");
                }

                // Đã gửi lời mời trước đó rồi, đang chờ đối phương chấp nhận
                var myExistingRequest = await FirebaseClient.GetAsync<object>($"friendRequests/{targetUsername}/{myUsername}");
                if (myExistingRequest != null)
                    return (false, $"Bạn đã gửi lời mời kết bạn cho {target.displayName ?? target.username} trước đó, đang chờ chấp nhận.");

                var myInfo = AuthService.CurrentUser;
                var request = new FriendRequestModel
                {
                    fromUsername = myUsername,
                    fromDisplayName = (myInfo != null && !string.IsNullOrWhiteSpace(myInfo.displayName)) ? myInfo.displayName : myUsername,
                    fromAvatar = (myInfo != null && !string.IsNullOrWhiteSpace(myInfo.avatar)) ? myInfo.avatar : "avatar1.png",
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                await FirebaseClient.PutAsync($"friendRequests/{targetUsername}/{myUsername}", request);
                return (true, $"Đã gửi lời mời kết bạn tới {target.displayName ?? target.username}. Chờ họ chấp nhận nhé!");
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi khi gửi lời mời kết bạn: {ex.Message}");
            }
        }

        // Tìm người chơi theo TÊN HIỂN THỊ (không phải tên tài khoản).
        // Có người trùng tên hoàn toàn (không phân biệt hoa/thường) -> chỉ trả về nhóm đó;
        // không có -> trả về những người có tên chứa từ khoá (tối đa 8). Luôn loại bỏ chính mình.
        public static async Task<List<UserData>> SearchUsersByDisplayNameAsync(string query, string myUsername)
        {
            var result = new List<UserData>();
            query = (query ?? "").Trim();
            myUsername = (myUsername ?? "").Trim().ToLowerInvariant();
            if (query.Length == 0) return result;

            try
            {
                var all = await FirebaseClient.GetAsync<Dictionary<string, UserData>>("users");
                if (all == null || all.Count == 0) return result;

                var candidates = all.Values
                    .Where(u => u != null && !string.IsNullOrEmpty(u.username)
                                && !u.username.Equals(myUsername, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var exact = candidates
                    .Where(u => string.Equals((u.displayName ?? "").Trim(), query, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(u => u.elo)
                    .ToList();
                if (exact.Count > 0) return exact;

                return candidates
                    .Where(u => (u.displayName ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(u => u.elo)
                    .Take(8)
                    .ToList();
            }
            catch
            {
                return result;
            }
        }

        // Kiểm tra mình đã gửi lời mời cho người kia và đang chờ họ chấp nhận hay chưa
        public static async Task<bool> HasSentFriendRequestAsync(string myUsername, string targetUsername)
        {
            myUsername = (myUsername ?? "").Trim().ToLowerInvariant();
            targetUsername = (targetUsername ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(myUsername) || string.IsNullOrWhiteSpace(targetUsername)) return false;

            try
            {
                var val = await FirebaseClient.GetAsync<object>($"friendRequests/{targetUsername}/{myUsername}");
                return val != null;
            }
            catch
            {
                return false;
            }
        }

        // Huỷ lời mời kết bạn mình đã gửi (xoá yêu cầu đang chờ ở phía người nhận)
        public static async Task<bool> CancelFriendRequestAsync(string myUsername, string targetUsername)
        {
            myUsername = (myUsername ?? "").Trim().ToLowerInvariant();
            targetUsername = (targetUsername ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(myUsername) || string.IsNullOrWhiteSpace(targetUsername)) return false;

            try
            {
                await FirebaseClient.DeleteAsync($"friendRequests/{targetUsername}/{myUsername}");
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Lấy danh sách lời mời kết bạn đang chờ (người khác gửi tới mình)
        public static async Task<List<FriendRequestModel>> GetPendingFriendRequestsAsync(string username)
        {
            var result = new List<FriendRequestModel>();
            username = (username ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(username)) return result;

            try
            {
                var dict = await FirebaseClient.GetAsync<Dictionary<string, FriendRequestModel>>($"friendRequests/{username}");
                if (dict == null || dict.Count == 0) return result;

                foreach (var kvp in dict)
                {
                    if (kvp.Value == null) continue;
                    if (string.IsNullOrWhiteSpace(kvp.Value.fromUsername))
                        kvp.Value.fromUsername = kvp.Key;
                    result.Add(kvp.Value);
                }

                return result.OrderByDescending(r => r.timestamp).ToList();
            }
            catch
            {
                return result;
            }
        }

        // Chấp nhận lời mời kết bạn -> chính thức trở thành bạn bè (2 chiều)
        public static async Task<bool> AcceptFriendRequestAsync(string myUsername, string fromUsername)
        {
            myUsername = (myUsername ?? "").Trim().ToLowerInvariant();
            fromUsername = (fromUsername ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(myUsername) || string.IsNullOrWhiteSpace(fromUsername)) return false;

            try
            {
                await FirebaseClient.PutAsync($"friends/{myUsername}/{fromUsername}", true);
                await FirebaseClient.PutAsync($"friends/{fromUsername}/{myUsername}", true);
                await FirebaseClient.DeleteAsync($"friendRequests/{myUsername}/{fromUsername}");
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Từ chối lời mời kết bạn -> chỉ xoá yêu cầu, không kết bạn
        public static async Task<bool> DeclineFriendRequestAsync(string myUsername, string fromUsername)
        {
            myUsername = (myUsername ?? "").Trim().ToLowerInvariant();
            fromUsername = (fromUsername ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(myUsername) || string.IsNullOrWhiteSpace(fromUsername)) return false;

            try
            {
                await FirebaseClient.DeleteAsync($"friendRequests/{myUsername}/{fromUsername}");
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Remove friend (mutual)
        public static async Task<bool> RemoveFriendAsync(string myUsername, string targetUsername)
        {
            myUsername = (myUsername ?? "").Trim().ToLowerInvariant();
            targetUsername = (targetUsername ?? "").Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(myUsername) || string.IsNullOrWhiteSpace(targetUsername))
                return false;

            try
            {
                await FirebaseClient.DeleteAsync($"friends/{myUsername}/{targetUsername}");
                await FirebaseClient.DeleteAsync($"friends/{targetUsername}/{myUsername}");
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Check if is friend
        public static async Task<bool> IsFriendAsync(string myUsername, string targetUsername)
        {
            myUsername = (myUsername ?? "").Trim().ToLowerInvariant();
            targetUsername = (targetUsername ?? "").Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(myUsername) || string.IsNullOrWhiteSpace(targetUsername))
                return false;

            try
            {
                var val = await FirebaseClient.GetAsync<object>($"friends/{myUsername}/{targetUsername}");
                return val != null;
            }
            catch
            {
                return false;
            }
        }

        // Update online status & activity with heartbeat timestamp
        public static async Task UpdateStatusAsync(string username, string status, string activity)
        {
            if (string.IsNullOrWhiteSpace(username)) return;
            try
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var patch = new Dictionary<string, object>
                {
                    ["status"] = status,
                    ["activity"] = activity,
                    ["lastSeen"] = now
                };
                await FirebaseClient.PatchAsync($"users/{username.Trim().ToLowerInvariant()}", patch);
            }
            catch { }
        }

        // Send room invite to a friend
        public static async Task<bool> SendRoomInviteAsync(string fromUser, string fromDisplayName, string fromAvatar, string toUser, string roomId)
        {
            if (string.IsNullOrWhiteSpace(fromUser) || string.IsNullOrWhiteSpace(toUser) || string.IsNullOrWhiteSpace(roomId))
                return false;

            try
            {
                var invite = new RoomInvite
                {
                    fromUser = fromUser.Trim().ToLowerInvariant(),
                    fromDisplayName = fromDisplayName,
                    fromAvatar = fromAvatar,
                    roomId = roomId,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                return await FirebaseClient.PutAsync($"invites/{toUser.Trim().ToLowerInvariant()}/{roomId}", invite);
            }
            catch
            {
                return false;
            }
        }

        // Get pending invites for user (within last 3 minutes)
        public static async Task<List<RoomInvite>> GetPendingInvitesAsync(string username)
        {
            var result = new List<RoomInvite>();
            if (string.IsNullOrWhiteSpace(username)) return result;

            try
            {
                var dict = await FirebaseClient.GetAsync<Dictionary<string, RoomInvite>>($"invites/{username.Trim().ToLowerInvariant()}");
                if (dict == null || dict.Count == 0) return result;

                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                foreach (var kvp in dict)
                {
                    if (kvp.Value != null && (now - kvp.Value.timestamp < 180_000)) // valid for 3 min
                    {
                        result.Add(kvp.Value);
                    }
                    else if (kvp.Value != null)
                    {
                        // Clean up expired invite
                        _ = FirebaseClient.DeleteAsync($"invites/{username.Trim().ToLowerInvariant()}/{kvp.Key}");
                    }
                }
                return result;
            }
            catch
            {
                return result;
            }
        }

        // Dismiss invite
        public static async Task DismissInviteAsync(string username, string roomId)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(roomId)) return;
            try
            {
                await FirebaseClient.DeleteAsync($"invites/{username.Trim().ToLowerInvariant()}/{roomId}");
            }
            catch { }
        }
    }
}
