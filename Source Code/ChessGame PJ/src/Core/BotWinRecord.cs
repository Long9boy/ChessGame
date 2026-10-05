namespace ChessGame_PJ.Core
{
    public enum BotDifficulty
    {
        Easy,
        Medium,
        Hard,
        Stockfish
    }

    public enum BotWinRecordStatus
    {
        NewRecord,          // New personal best recorded to leaderboard
        KeptExistingRecord, // Won online, but previous personal best is faster or equal
        NotLoggedIn,        // Playing offline / not logged in
        Failed              // Network error or server save failure
    }

    public class BotWinSaveResult
    {
        public BotWinRecordStatus Status { get; set; } = BotWinRecordStatus.Failed;
        public int ExistingBestSeconds { get; set; } = 0;
    }

    public class BotWinRecord
    {
        public string username { get; set; } = "";
        public string displayName { get; set; } = "";
        public string avatar { get; set; } = "avatar1.png";
        public int timeSeconds { get; set; } = 0;
        public long timestamp { get; set; } = 0;
    }
}
