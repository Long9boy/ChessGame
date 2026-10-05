using System;
using System.IO;
using System.Text.Json;

namespace ChessGame_PJ.Core
{
    public enum BoardColorTheme
    {
        BrownGold = 0,   // Nâu + Vàng nhạt (mặc định)
        BlackWhite = 1,  // Trắng + Đen
        BlueWhite = 2    // Xanh + Trắng
    }

    public class AppSettings
    {
        public int Volume { get; set; } = 70;
        public BoardColorTheme BoardColor { get; set; } = BoardColorTheme.BrownGold;

        // Khi bật: người chơi cầm quân Đen sẽ thấy bàn cờ được đảo ngược
        // (quân của mình hiển thị ở phía dưới màn hình) để dễ nhìn hơn.
        public bool FlipBoardForBlack { get; set; } = true;
        
        public const string PieceStyleClassic = "set1";
        public const string PieceStyleRoyal = "set2";
        public const string PieceStyleAngleVsDemon = "set3";
        public const string PieceStyleAngleVsDemon2 = "set4";
        public const string PieceStyleDragonAge = "set5";

        private string _pieceStyle = PieceStyleClassic;
        public string PieceStyle
        {
            get => _pieceStyle switch
            {
                "set2" or "royal" => PieceStyleRoyal,
                "set3" or "angle_vs_demon" => PieceStyleAngleVsDemon,
                "set4" or "angle_vs_demon_2" => PieceStyleAngleVsDemon2,
                "set5" or "dragon_age" or "long_toc" => PieceStyleDragonAge,
                _ => PieceStyleClassic
            };
            set => _pieceStyle = value;
        }

        // ── Board color palettes ──────────────────────────────────────
        // Returns (lightSquare, darkSquare) as hex strings
        public static (string light, string dark) GetBoardColors(BoardColorTheme theme) => theme switch
        {
            BoardColorTheme.BlackWhite => ("#F0F0F0", "#404040"),
            BoardColorTheme.BlueWhite  => ("#DEF0FF", "#4B8AC4"),
            _                          => ("#F0D9B5", "#B58863")  // BrownGold default
        };

        // ── Persistence ───────────────────────────────────────────────
        private static readonly string SettingsDir  = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CovuaVipPro");
        private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

        private static AppSettings? _instance;
        public static AppSettings Current => _instance ??= Load();

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null) return _instance = s;
                }
            }
            catch { }
            return _instance = new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }
    }
}
