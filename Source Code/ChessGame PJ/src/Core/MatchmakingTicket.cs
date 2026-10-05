namespace ChessGame_PJ.Core
{
    public class MatchmakingTicket
    {
        public string username { get; set; } = "";
        public string displayName { get; set; } = "";
        public string avatar { get; set; } = "avatar1.png";
        public int elo { get; set; } = 1200;
        public string status { get; set; } = "waiting"; // "waiting", "matched", "cancelled"
        public string roomId { get; set; } = "";
        public string matchedWith { get; set; } = "";
        public long createdAt { get; set; } = 0;
        public long lastHeartbeat { get; set; } = 0;
    }
}
