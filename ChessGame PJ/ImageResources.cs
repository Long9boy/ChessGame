using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ChessGame_PJ
{
    // Ảnh quân cờ được nhúng thẳng vào file .exe (Embedded Resource),
    // không cần copy thư mục assets đi kèm khi phát hành.
    public static class ImageResources
    {
        private static readonly System.Collections.Generic.Dictionary<string, Image> cache = new();
        private static string[]? allResourceNames = null;

        public static Image? GetPieceImage(string pieceCode)
        {
            string fileName = pieceCode switch
            {
                "B_Pawn" => "pawn_black.png",
                "W_Pawn" => "pawn_white.png",
                "B_Rook" => "rook_black.png",
                "W_Rook" => "rook_white.png",
                "B_Knight" => "knight_black.png",
                "W_Knight" => "knight_white.png",
                "B_Bishop" => "bishop_black.png",
                "W_Bishop" => "bishop_white.png",
                "B_Queen" => "queen_black.png",
                "W_Queen" => "queen_white.png",
                "B_King" => "king_black.png",
                "W_King" => "king_white.png",
                _ => ""
            };
            if (string.IsNullOrEmpty(fileName)) return null;
            return GetImageByFileName(fileName);
        }

        public static Image? GetPromotionImage(string colorPrefix, string pieceType)
        {
            // colorPrefix: "W_" hoặc "B_", pieceType: "Queen","Rook","Bishop","Knight"
            string colorPart = colorPrefix == "W_" ? "white" : "black";
            string fileName = pieceType.ToLower() + "_" + colorPart + ".png";
            return GetImageByFileName(fileName);
        }

        private static Image? GetImageByFileName(string fileName)
        {
            if (cache.TryGetValue(fileName, out Image? cached)) return cached;

            Assembly asm = Assembly.GetExecutingAssembly();

            if (allResourceNames == null)
                allResourceNames = asm.GetManifestResourceNames();

            // Tên resource nhúng thường có dạng "ChessGame_PJ.assets.textures.pawn_white.png"
            string? resourceName = allResourceNames.FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));
            if (resourceName == null) return null;

            using Stream? stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null) return null;

            Image img = Image.FromStream(stream);
            cache[fileName] = img;
            return img;
        }
    }
}
