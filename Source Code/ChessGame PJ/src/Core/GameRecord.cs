using System.Collections.Generic;

namespace ChessGame_PJ.Core
{
    public class GameRecord
    {
        public string gameId { get; set; } = "";
        public string playerWhite { get; set; } = "";
        public string playerBlack { get; set; } = "";
        public string displayNameWhite { get; set; } = "";
        public string displayNameBlack { get; set; } = "";
        public string avatarWhite { get; set; } = "avatar1.png";
        public string avatarBlack { get; set; } = "avatar1.png";
        public string gameMode { get; set; } = "Giao hữu"; // "Xếp hạng", "Đấu máy", "Giao hữu", "Custom"
        public string result { get; set; } = "win"; // relative: "win", "loss", "draw"
        public string winner { get; set; } = ""; // "white", "black", "draw"
        public long timestamp { get; set; } = 0;
        public List<MoveHistoryEntry> moves { get; set; } = new List<MoveHistoryEntry>();
    }
}
