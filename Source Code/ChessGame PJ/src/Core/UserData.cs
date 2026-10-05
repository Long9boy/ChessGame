using System;

namespace ChessGame_PJ
{
    public class UserData
    {
        public string username { get; set; } = "";
        public string passwordHash { get; set; } = "";
        public string salt { get; set; } = "";
        public string displayName { get; set; } = "";
        public string avatar { get; set; } = "avatar1.png";
        public string sessionToken { get; set; } = "";
        public int elo { get; set; } = 1200;
        public int wins { get; set; } = 0;
        public int losses { get; set; } = 0;
        public int draws { get; set; } = 0;
        public long createdAt { get; set; } = 0;

        // Best (fastest) bot win time per difficulty (seconds); 0 = no record
        public int bestBotTimeEasy    { get; set; } = 0;
        public int bestBotTimeMedium  { get; set; } = 0;
        public int bestBotTimeHard    { get; set; } = 0;
        public int bestBotTimeExpert  { get; set; } = 0;
        // Online status and current activity
        public string status { get; set; } = "offline"; // "online" or "offline"
        public string activity { get; set; } = "Sảnh chờ"; // "Sảnh chờ", "Xếp hạng", "Đấu máy", "Giao hữu", "Custom"
        public long lastSeen { get; set; } = 0;
    }
}
