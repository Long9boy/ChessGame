using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace ChessGame_PJ.Core.AI
{
    public class StockfishBot : IChessBot, IDisposable
    {
        private Process? process;
        private StreamWriter? inputWriter;
        private StreamReader? outputReader;
        private readonly object lockObj = new object();
        private bool isReady = false;

        public static string? FindStockfishPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string p1 = Path.Combine(baseDir, "assets", "engine", "stockfish.exe");
            if (File.Exists(p1)) return p1;

            string cwd = Directory.GetCurrentDirectory();
            string p2 = Path.Combine(cwd, "assets", "engine", "stockfish.exe");
            if (File.Exists(p2)) return p2;

            string p3 = Path.Combine(cwd, "ChessGame PJ", "assets", "engine", "stockfish.exe");
            if (File.Exists(p3)) return p3;

            // Search parent directories
            var parent = new DirectoryInfo(baseDir).Parent;
            while (parent != null)
            {
                string candidate = Path.Combine(parent.FullName, "assets", "engine", "stockfish.exe");
                if (File.Exists(candidate)) return candidate;
                parent = parent.Parent;
            }

            // Fallback: Tự động giải nén Stockfish nhúng trong Single-File EXE ra LocalAppData nếu chạy độc lập 1 file duy nhất
            return ExtractEmbeddedStockfishIfNeeded();
        }

        private static readonly object _extractLock = new object();

        private static string? ExtractEmbeddedStockfishIfNeeded()
        {
            try
            {
                var asm = typeof(StockfishBot).Assembly;
                string resName = "ChessGame_PJ.assets.engine.stockfish.exe";
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream == null) return null;

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string targetDir = Path.Combine(localAppData, "ChessGamePJ", "engine");
                string targetPath = Path.Combine(targetDir, "stockfish.exe");

                lock (_extractLock)
                {
                    if (File.Exists(targetPath))
                    {
                        var fi = new FileInfo(targetPath);
                        if (fi.Length == stream.Length)
                        {
                            return targetPath;
                        }
                    }

                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    string tempPath = targetPath + ".tmp";
                    using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        stream.CopyTo(fs);
                    }

                    if (File.Exists(targetPath))
                    {
                        try { File.Delete(targetPath); } catch { }
                    }

                    File.Move(tempPath, targetPath);
                    return targetPath;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StockfishBot] Error extracting embedded stockfish: {ex.Message}");
                return null;
            }
        }

        private bool EnsureProcessStarted()
        {
            lock (lockObj)
            {
                if (process != null && !process.HasExited && isReady) return true;

                string? exePath = FindStockfishPath();
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    return false;
                }

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    };

                    process = new Process { StartInfo = psi };
                    process.Start();

                    inputWriter = process.StandardInput;
                    outputReader = process.StandardOutput;

                    SendCommand("uci");
                    string? line;
                    while ((line = outputReader.ReadLine()) != null)
                    {
                        if (line.Trim() == "uciok") break;
                    }

                    SendCommand("isready");
                    while ((line = outputReader.ReadLine()) != null)
                    {
                        if (line.Trim() == "readyok") break;
                    }

                    isReady = true;
                    return true;
                }
                catch
                {
                    Dispose();
                    return false;
                }
            }
        }

        private void SendCommand(string cmd)
        {
            if (inputWriter != null)
            {
                inputWriter.WriteLine(cmd);
                inputWriter.Flush();
            }
        }

        public async Task<(int fromRow, int fromCol, int toRow, int toCol, string? promotion)> GetBestMoveAsync(ChessGame game, string botColor)
        {
            if (!EnsureProcessStarted())
            {
                // Fallback to HardBot if Stockfish executable is not available
                var hardBot = new HardBot();
                return await hardBot.GetBestMoveAsync(game, botColor);
            }

            return await Task.Run(() =>
            {
                lock (lockObj)
                {
                    try
                    {
                        string fen = FenHelper.BoardToFen(game, botColor);
                        SendCommand($"position fen {fen}");
                        SendCommand("go movetime 1000");

                        string? line;
                        while ((line = outputReader?.ReadLine()) != null)
                        {
                            line = line.Trim();
                            if (line.StartsWith("bestmove"))
                            {
                                // Format: bestmove e2e4 ponder e7e5 or bestmove e7e8q
                                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 2)
                                {
                                    string moveUci = parts[1];
                                    if (moveUci == "(none)" || moveUci == "null")
                                    {
                                        break;
                                    }
                                    return FenHelper.ParseUciMove(moveUci);
                                }
                                break;
                            }
                        }
                    }
                    catch
                    {
                        Dispose();
                    }

                    // Fallback if needed
                    var fallback = new HardBot();
                    return fallback.GetBestMoveAsync(game, botColor).GetAwaiter().GetResult();
                }
            });
        }

        public void Dispose()
        {
            lock (lockObj)
            {
                try
                {
                    if (process != null && !process.HasExited)
                    {
                        SendCommand("quit");
                        if (!process.WaitForExit(500))
                        {
                            process.Kill();
                        }
                    }
                }
                catch { }
                finally
                {
                    inputWriter?.Dispose();
                    outputReader?.Dispose();
                    process?.Dispose();
                    inputWriter = null;
                    outputReader = null;
                    process = null;
                    isReady = false;
                }
            }
        }
    }
}
