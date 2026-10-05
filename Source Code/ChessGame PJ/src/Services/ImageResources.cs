using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ChessGame_PJ
{
    public static class ImageResources
    {
        private static readonly Dictionary<string, ImageSource> cache = new(StringComparer.OrdinalIgnoreCase);
        private static string[]? allResourceNames = null;

        public static string CurrentPieceSet => ChessGame_PJ.Core.AppSettings.Current.PieceStyle;

        public static ImageSource? GetPieceImage(string pieceCode)
        {
            return GetPieceImage(pieceCode, CurrentPieceSet);
        }

        public static ImageSource? GetPieceImage(string pieceCode, string pieceSet)
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
            return GetPieceImageFromSet(fileName, pieceSet);
        }

        public static ImageSource? GetPromotionImage(string colorPrefix, string pieceType)
        {
            string colorPart = colorPrefix == "W_" ? "white" : "black";
            string fileName = pieceType.ToLower() + "_" + colorPart + ".png";
            return GetPieceImageFromSet(fileName, CurrentPieceSet);
        }

        public static ImageSource? GetPieceImageFromSet(string fileName, string pieceSet)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            if (string.IsNullOrEmpty(pieceSet)) pieceSet = "set1";

            string cacheKey = $"cell_{pieceSet}_{fileName}";
            if (cache.TryGetValue(cacheKey, out ImageSource? cached)) return cached;

            // 1. Try WPF pack URI
            string[] packUris = {
                $"pack://application:,,,/assets/textures/cell/{pieceSet}/{fileName}",
                $"pack://application:,,,/assets/textures/cell/set1/{fileName}"
            };

            foreach (string uriStr in packUris)
            {
                try
                {
                    var uri = new Uri(uriStr, UriKind.Absolute);
                    var sri = Application.GetResourceStream(uri);
                    if (sri?.Stream != null)
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = sri.Stream;
                        bmp.EndInit();
                        bmp.Freeze();
                        cache[cacheKey] = bmp;
                        return bmp;
                    }
                }
                catch { }
            }

            // 2. Try Embedded Resource Stream
            Assembly asm = Assembly.GetExecutingAssembly();
            allResourceNames ??= asm.GetManifestResourceNames();
            string searchEnding = $".{pieceSet}.{fileName}";
            string? resourceName = allResourceNames.FirstOrDefault(n => n.EndsWith(searchEnding, StringComparison.OrdinalIgnoreCase));
            if (resourceName != null)
            {
                try
                {
                    using Stream? stream = asm.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = stream;
                        bmp.EndInit();
                        bmp.Freeze();
                        cache[cacheKey] = bmp;
                        return bmp;
                    }
                }
                catch { }
            }

            // 3. Fallback to physical disk path
            string[] searchDirs = {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "textures", "cell", pieceSet),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "textures", "cell", "set1"),
                Path.Combine(Directory.GetCurrentDirectory(), "ChessGame PJ", "assets", "textures", "cell", pieceSet)
            };

            foreach (string dir in searchDirs)
            {
                string path = Path.Combine(dir, fileName);
                if (File.Exists(path))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                        bmp.EndInit();
                        bmp.Freeze();
                        cache[cacheKey] = bmp;
                        return bmp;
                    }
                    catch { }
                }
            }

            // 4. Fallback to general GetImageByFileName
            var fallback = GetImageByFileName(fileName);
            if (fallback != null)
            {
                cache[cacheKey] = fallback;
                return fallback;
            }

            return null;
        }

        public static List<string> GetAllAvatarNames()
        {
            var names = new List<string>();

            // 1. Scan from manifest resources
            Assembly asm = Assembly.GetExecutingAssembly();
            allResourceNames ??= asm.GetManifestResourceNames();

            foreach (string r in allResourceNames)
            {
                if (r.Contains("icon", StringComparison.OrdinalIgnoreCase) && r.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    int iconIdx = r.LastIndexOf('.');
                    int prevIdx = r.LastIndexOf('.', iconIdx - 1);
                    if (prevIdx >= 0)
                    {
                        string fn = r.Substring(prevIdx + 1);
                        if (fn.StartsWith("avatar", StringComparison.OrdinalIgnoreCase) && !names.Contains(fn, StringComparer.OrdinalIgnoreCase))
                            names.Add(fn);
                    }
                }
            }

            // 2. Scan from disk directory
            string iconDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "textures", "icon");
            if (Directory.Exists(iconDir))
            {
                foreach (string file in Directory.GetFiles(iconDir, "avatar*.png"))
                {
                    string fn = Path.GetFileName(file);
                    if (!names.Contains(fn, StringComparer.OrdinalIgnoreCase))
                        names.Add(fn);
                }
            }

            // Fallback default avatars if none found (avatar1.png to avatar9.png)
            if (names.Count == 0)
            {
                for (int i = 1; i <= 9; i++)
                    names.Add($"avatar{i}.png");
            }

            names.Sort();
            return names;
        }

        public static string GetRandomAvatarName()
        {
            var list = GetAllAvatarNames();
            if (list.Count == 0) return "avatar1.png";
            var rnd = new Random();
            return list[rnd.Next(list.Count)];
        }

        public static ImageSource? GetAvatarImage(string? avatarName)
        {
            if (string.IsNullOrWhiteSpace(avatarName))
                avatarName = "avatar1.png";

            if (!avatarName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                avatarName += ".png";

            return GetImageByFileName(avatarName);
        }

        public static ImageSource? GetImageByFileName(string fileName)
        {
            if (cache.TryGetValue(fileName, out ImageSource? cached)) return cached;

            // 1. Try WPF pack URI for cell, icon, textures
            string[] packUris = {
                $"pack://application:,,,/assets/textures/cell/{fileName}",
                $"pack://application:,,,/assets/textures/icon/{fileName}",
                $"pack://application:,,,/assets/textures/{fileName}",
                $"pack://application:,,,/assets/{fileName}"
            };

            foreach (string uriStr in packUris)
            {
                try
                {
                    var uri = new Uri(uriStr, UriKind.Absolute);
                    var sri = Application.GetResourceStream(uri);
                    if (sri != null && sri.Stream != null)
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = sri.Stream;
                        bmp.EndInit();
                        bmp.Freeze();
                        cache[fileName] = bmp;
                        return bmp;
                    }
                }
                catch { }
            }

            // 2. Try Embedded Resource Stream from Assembly
            Assembly asm = Assembly.GetExecutingAssembly();
            allResourceNames ??= asm.GetManifestResourceNames();
            string? resourceName = allResourceNames.FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));
            if (resourceName != null)
            {
                try
                {
                    using Stream? stream = asm.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = stream;
                        bmp.EndInit();
                        bmp.Freeze();
                        cache[fileName] = bmp;
                        return bmp;
                    }
                }
                catch { }
            }

            // 3. Fallback to physical disk path
            string[] searchDirs = {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "textures", "cell"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "textures", "icon"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "textures"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets"),
                AppDomain.CurrentDomain.BaseDirectory
            };

            foreach (string dir in searchDirs)
            {
                string path = Path.Combine(dir, fileName);
                if (File.Exists(path))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.UriSource = new Uri(path, UriKind.Absolute);
                        bmp.EndInit();
                        bmp.Freeze();
                        cache[fileName] = bmp;
                        return bmp;
                    }
                    catch { }
                }
            }

            return null;
        }
    }
}

