namespace ChessGame_PJ.Core
{
    public class MoveHistoryEntry
    {
        public int moveNumber { get; set; } = 1;
        public string whiteMove { get; set; } = "";
        public string blackMove { get; set; } = "";
        public string whitePiece { get; set; } = ""; // e.g. "W_Pawn", "W_Knight"
        public string blackPiece { get; set; } = ""; // e.g. "B_Pawn", "B_Knight"
        public double turnSeconds { get; set; } = 0; // Total duration in seconds for this round

        // Board snapshot after white move and after black move (for replay & rewinding)
        public System.Collections.Generic.Dictionary<string, string>? boardAfterWhite { get; set; }
        public System.Collections.Generic.Dictionary<string, string>? boardAfterBlack { get; set; }

        // Stockfish Evaluation & Move Quality
        public string? whiteEval { get; set; }           // e.g. "+0.3", "-1.2", "M3"
        public string? whiteClassification { get; set; } // e.g. "Best", "Good", "Inaccuracy", "Mistake", "Blunder"
        public string? whiteBadge { get; set; }          // e.g. "★", "✓", "?!", "?", "??"
        public string? whiteColorHex { get; set; }       // e.g. "#00E676"
        public string? whiteBestAlternative { get; set; }
        public string? whiteBestAlternativeReason { get; set; }
        public (int fromRow, int fromCol, int toRow, int toCol, string? promo)? whiteBestAltCoords { get; set; }
        public string? whiteCommentary { get; set; }

        public string? blackEval { get; set; }
        public string? blackClassification { get; set; }
        public string? blackBadge { get; set; }
        public string? blackColorHex { get; set; }
        public string? blackBestAlternative { get; set; }
        public string? blackBestAlternativeReason { get; set; }
        public (int fromRow, int fromCol, int toRow, int toCol, string? promo)? blackBestAltCoords { get; set; }
        public string? blackCommentary { get; set; }

        // Pre-generated TTS WAV file paths & voice clip sequences
        public string? whiteTtsWavPath { get; set; }
        public string? blackTtsWavPath { get; set; }
        public System.Collections.Generic.List<string>? whiteVoiceClips { get; set; }
        public System.Collections.Generic.List<string>? blackVoiceClips { get; set; }
    }
}
