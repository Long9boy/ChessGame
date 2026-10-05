using System.Collections.Generic;
using ChessGame_PJ.Core;

namespace ChessGame_PJ
{
    public class RoomData
    {
        public string player1 { get; set; } = "";     // username (Firebase key)
        public string player2 { get; set; } = "";     // username (Firebase key)
        public string displayName1 { get; set; } = ""; // display name for UI
        public string displayName2 { get; set; } = ""; // display name for UI
        public string avatar1 { get; set; } = "avatar1.png";
        public string avatar2 { get; set; } = "avatar2.png";

        public string turn { get; set; } = "white";
        public string status { get; set; } = "waiting";
        public string winner { get; set; } = "";
        public string check { get; set; } = "";

        public Dictionary<string, string> board { get; set; } = new Dictionary<string, string>();

        public bool wKingMoved { get; set; } = false;
        public bool bKingMoved { get; set; } = false;
        public bool wRookAMoved { get; set; } = false;
        public bool wRookHMoved { get; set; } = false;
        public bool bRookAMoved { get; set; } = false;
        public bool bRookHMoved { get; set; } = false;

        public int selRow { get; set; } = -1;
        public int selCol { get; set; } = -1;
        public List<string> legalMoves { get; set; } = new List<string>();

        // Clock & Turn timing
        public int timeLimitMinutes { get; set; } = 10;
        public double timeLeftWhite { get; set; } = 600;
        public double timeLeftBlack { get; set; } = 600;
        public long turnStartedAt { get; set; } = 0;

        // Move history
        public List<MoveHistoryEntry> moveHistory { get; set; } = new List<MoveHistoryEntry>();

        // Draw offer
        public string drawOffer { get; set; } = ""; // "white", "black", or ""

        // Pause state
        public bool isPaused { get; set; } = false;
        public string pauseRequestedBy { get; set; } = "";
        public int pauseRequestedMinutes { get; set; } = 0;
        public long pauseExpiresAt { get; set; } = 0;

        public long lastHeartbeat1 { get; set; } = 0;
        public long lastHeartbeat2 { get; set; } = 0;

        public bool scoreAwarded { get; set; } = false;
        public bool isMatchmaking { get; set; } = false;

        // Last move highlight coordinates
        public int lastFromRow { get; set; } = -1;
        public int lastFromCol { get; set; } = -1;
        public int lastToRow { get; set; } = -1;
        public int lastToCol { get; set; } = -1;

        // Move hint dots option
        public bool showMoveHints { get; set; } = true;

        public long updatedAt { get; set; } = 0; // Use to determine if the room data has changed since last fetch
    }
}
