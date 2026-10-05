using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ChessGame_PJ.Core;
using ChessGame_PJ.Core.AI;
using NAudio.Wave;

namespace ChessGame_PJ.Services
{
    /// <summary>
    /// Dịch vụ Text-to-Speech sử dụng các đoạn âm thanh giọng Hải Đăng thu sẵn (assets/sounds/voice/*.wav).
    /// Ghép chuỗi âm thanh siêu tốc trực tiếp trong bộ nhớ RAM:
    /// - 0% CPU khi chơi game (không chạy model AI nặng lúc đang chơi)
    /// - Độ trễ 0ms (phát ngay lập tức sau nước cờ)
    /// - Không bao giờ bị treo hay lỗi mạng
    /// - Tự động bỏ đọc điểm số âm/dương theo yêu cầu
    /// </summary>
    public static class SpeechService
    {
        public static bool IsTtsEnabled { get; set; } = true;
        public static bool IsAutoPlaying { get; set; } = false;

        private static readonly object _lock = new object();
        private static CancellationTokenSource? _currentCts = null;
        private static WaveOut? _currentWaveOut = null;

        // Lưu trữ toàn bộ các clip âm thanh WAV trong RAM
        private static readonly Dictionary<string, byte[]> _voiceClips = new(StringComparer.OrdinalIgnoreCase);
        private static bool _initialized = false;
        private static readonly object _initLock = new object();

        /// <summary>
        /// Tải trước toàn bộ các file âm thanh giọng Hải Đăng vào bộ nhớ RAM khi game khởi động.
        /// Hỗ trợ cả file nhúng trực tiếp trong EXE (Single-File) lẫn thư mục ngoài trên đĩa.
        /// </summary>
        public static void Initialize()
        {
            lock (_initLock)
            {
                if (_initialized) return;
                _initialized = true;
            }

            Task.Run(() =>
            {
                try
                {
                    int loaded = 0;
                    var asm = typeof(SpeechService).Assembly;
                    string prefix = "ChessGame_PJ.assets.sounds.voice.";

                    // Ưu tiên nạp từ EmbeddedResource nhúng sẵn trong EXE
                    foreach (var resName in asm.GetManifestResourceNames())
                    {
                        if (resName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                            resName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                        {
                            string key = resName.Substring(prefix.Length, resName.Length - prefix.Length - 4);
                            try
                            {
                                using var resStream = asm.GetManifestResourceStream(resName);
                                if (resStream != null)
                                {
                                    using var ms = new MemoryStream();
                                    resStream.CopyTo(ms);
                                    lock (_voiceClips)
                                    {
                                        _voiceClips[key] = ms.ToArray();
                                    }
                                    loaded++;
                                }
                            }
                            catch { }
                        }
                    }

                    if (loaded > 0)
                    {
                        Debug.WriteLine($"[SpeechService] Successfully loaded {loaded} embedded voice clips into RAM from EXE.");
                        return;
                    }

                    // Nếu không có EmbeddedResource thì đọc từ thư mục đĩa ngoài
                    string voiceDir = FindVoiceDirectory();
                    if (!Directory.Exists(voiceDir))
                    {
                        Debug.WriteLine($"[SpeechService] Voice directory not found at: {voiceDir}");
                        return;
                    }

                    var files = Directory.GetFiles(voiceDir, "*.wav");
                    foreach (var file in files)
                    {
                        try
                        {
                            string key = Path.GetFileNameWithoutExtension(file);
                            byte[] bytes = File.ReadAllBytes(file);
                            lock (_voiceClips)
                            {
                                _voiceClips[key] = bytes;
                            }
                            loaded++;
                        }
                        catch { }
                    }
                    Debug.WriteLine($"[SpeechService] Successfully loaded {loaded} voice clips into RAM from: {voiceDir}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SpeechService] Initialize error: {ex.Message}");
                }
            });
        }

        public static void StartDaemonAsync()
        {
            // Tương thích ngược: chỉ cần gọi Initialize để nạp các clip vào RAM
            Initialize();
        }

        private static string FindVoiceDirectory()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "assets", "sounds", "voice"),
                Path.Combine(AppContext.BaseDirectory, "voice"),
                Path.Combine(Directory.GetCurrentDirectory(), "assets", "sounds", "voice"),
                Path.Combine(Directory.GetCurrentDirectory(), "ChessGame PJ", "assets", "sounds", "voice"),
                @"C:\Users\ACER\source\repos\ChessGame PJ\ChessGame PJ\assets\sounds\voice"
            };

            foreach (var path in candidates)
            {
                if (Directory.Exists(path) && Directory.GetFiles(path, "*.wav").Length > 0)
                {
                    return path;
                }
            }

            return candidates[0];
        }

        public static void Stop()
        {
            try
            {
                lock (_lock)
                {
                    _currentCts?.Cancel();
                    _currentCts = null;
                    _currentWaveOut?.Stop();
                    _currentWaveOut = null;
                }
            }
            catch { }
        }

        public static void StopDaemon()
        {
            Stop();
        }

        // ── Public Playback API ──────────────────────────────────────────────

        /// <summary>
        /// Phát chuỗi clip âm thanh đã chuẩn bị sẵn theo thứ tự (không đọc điểm số, phát siêu nhanh).
        /// </summary>
        public static void PlayClipSequence(IEnumerable<string>? clipKeys)
        {
            if (!IsTtsEnabled || IsAutoPlaying || clipKeys == null) return;

            int vol = GetVolume();
            if (vol <= 0) return;

            if (!_initialized) Initialize();

            var keysList = new List<string>(clipKeys);
            if (keysList.Count == 0) return;

            CancellationTokenSource newCts;
            lock (_lock)
            {
                _currentCts?.Cancel();
                try { _currentWaveOut?.Stop(); } catch { }
                _currentWaveOut = null;
                newCts = new CancellationTokenSource();
                _currentCts = newCts;
            }

            _ = Task.Run(() => PlayClipsInternal(keysList, vol, newCts.Token));
        }

        /// <summary>
        /// Phát đánh giá nước cờ: ưu tiên sử dụng danh sách VoiceClips đã dựng sẵn.
        /// </summary>
        public static void SpeakAssessment(MoveAssessment? assessment)
        {
            if (assessment == null) return;

            if (assessment.VoiceClips != null && assessment.VoiceClips.Count > 0)
            {
                PlayClipSequence(assessment.VoiceClips);
            }
            else if (!string.IsNullOrEmpty(assessment.Commentary))
            {
                SpeakAsync(assessment.Commentary);
            }
        }

        /// <summary>
        /// Tương thích ngược: nhận văn bản bình luận, phân tách thành các đoạn clip tương ứng để ghép lại.
        /// </summary>
        public static void SpeakAsync(string? commentary)
        {
            if (!IsTtsEnabled || IsAutoPlaying || string.IsNullOrWhiteSpace(commentary)) return;

            int vol = GetVolume();
            if (vol <= 0) return;

            if (!_initialized) Initialize();

            // Nhận diện nhanh các câu thông báo hệ thống
            if (commentary.Contains("bật giọng đọc", StringComparison.OrdinalIgnoreCase))
            {
                PlayClipSequence(new[] { "sys_tts_on" });
                return;
            }
            if (commentary.Contains("tắt giọng đọc", StringComparison.OrdinalIgnoreCase))
            {
                PlayClipSequence(new[] { "sys_tts_off" });
                return;
            }

            // Phân tích văn bản bình luận thành chuỗi các key clip tương ứng
            var clips = ParseCommentaryToClips(commentary);
            if (clips.Count > 0)
            {
                PlayClipSequence(clips);
            }
        }

        public static void PlayPreGeneratedWav(string? wavPath)
        {
            // Tương thích ngược: nếu file tồn tại thì phát
            if (!IsTtsEnabled || IsAutoPlaying || string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath)) return;

            int vol = GetVolume();
            if (vol <= 0) return;

            CancellationTokenSource newCts;
            lock (_lock)
            {
                _currentCts?.Cancel();
                try { _currentWaveOut?.Stop(); } catch { }
                _currentWaveOut = null;
                newCts = new CancellationTokenSource();
                _currentCts = newCts;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(wavPath);
                    PlayWavBytes(bytes, vol, newCts.Token);
                }
                catch { }
            });
        }

        public static Task<string?> PreGenerateWavAsync(string? commentary, CancellationToken ct = default)
        {
            // Hệ thống nối file mới hoạt động trong <1ms, không cần pre-generate ra file tạm nữa
            return Task.FromResult<string?>(null);
        }

        // ── Internal Concatenation & Audio Output ────────────────────────────

        private static void PlayClipsInternal(List<string> clipKeys, int volume, CancellationToken ct)
        {
            try
            {
                if (ct.IsCancellationRequested) return;

                // Lấy mảng byte các clip WAV tương ứng
                var wavs = new List<byte[]>();
                lock (_voiceClips)
                {
                    foreach (var key in clipKeys)
                    {
                        if (_voiceClips.TryGetValue(key, out var bytes) && bytes != null && bytes.Length > 44)
                        {
                            wavs.Add(bytes);
                        }
                    }
                }

                if (wavs.Count == 0 || ct.IsCancellationRequested) return;

                // Ghép nối các file WAV trực tiếp trong RAM (khoảng lặng 50ms giữa các cụm)
                byte[] combinedWav = ConcatenateWavClips(wavs, silenceBetweenMs: 50);
                if (combinedWav.Length <= 44 || ct.IsCancellationRequested) return;

                PlayWavBytes(combinedWav, volume, ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SpeechService] PlayClips error: {ex.Message}");
            }
        }

        private static void PlayWavBytes(byte[] wavBytes, int volume, CancellationToken ct)
        {
            WaveOut? waveOut = null;
            WaveFileReader? reader = null;
            MemoryStream? ms = null;
            try
            {
                if (ct.IsCancellationRequested || wavBytes == null || wavBytes.Length <= 44) return;

                ms = new MemoryStream(wavBytes);
                reader = new WaveFileReader(ms);
                waveOut = new WaveOut();
                waveOut.Volume = Math.Clamp(volume / 100.0f, 0f, 1f);
                waveOut.Init(reader);

                lock (_lock) { _currentWaveOut = waveOut; }

                waveOut.Play();

                while (waveOut.PlaybackState == PlaybackState.Playing && !ct.IsCancellationRequested)
                {
                    Thread.Sleep(30);
                }

                if (ct.IsCancellationRequested)
                {
                    waveOut.Stop();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SpeechService] Audio play error: {ex.Message}");
            }
            finally
            {
                lock (_lock) { if (_currentWaveOut == waveOut) _currentWaveOut = null; }
                try { waveOut?.Dispose(); } catch { }
                try { reader?.Dispose(); } catch { }
                try { ms?.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// Ghép nhiều file WAV 48kHz Mono 16-bit PCM thành 1 file WAV duy nhất với khoảng nghỉ tự nhiên.
        /// </summary>
        public static byte[] ConcatenateWavClips(IEnumerable<byte[]> wavFiles, int silenceBetweenMs = 50)
        {
            using var pcmStream = new MemoryStream();
            // 48000 mẫu/giây * 1 kênh * 2 byte/mẫu = 96000 byte/giây
            int silenceByteCount = (48000 * 2 * silenceBetweenMs) / 1000;
            // Đảm bảo chia hết cho 2 (16-bit block align)
            silenceByteCount = (silenceByteCount / 2) * 2;
            byte[] silenceBytes = new byte[silenceByteCount];

            bool isFirst = true;
            foreach (var wav in wavFiles)
            {
                if (wav == null || wav.Length <= 44) continue;

                if (!isFirst && silenceBytes.Length > 0)
                {
                    pcmStream.Write(silenceBytes, 0, silenceBytes.Length);
                }
                isFirst = false;

                // Dữ liệu PCM bắt đầu từ byte 44 (tiêu chuẩn WAV header 44 byte)
                pcmStream.Write(wav, 44, wav.Length - 44);
            }

            if (pcmStream.Length == 0) return Array.Empty<byte>();

            byte[] pcmData = pcmStream.ToArray();
            int dataSize = pcmData.Length;

            using var output = new MemoryStream(44 + dataSize);
            using var writer = new BinaryWriter(output);

            // Ghi tiêu chuẩn WAV Header 44-byte (RIFF/WAVE PCM 48000Hz 16-bit Mono)
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);             // Chunk size
            writer.Write((short)1);        // PCM format
            writer.Write((short)1);        // Channels: Mono
            writer.Write(48000);           // Sample rate
            writer.Write(96000);           // Byte rate
            writer.Write((short)2);        // Block align
            writer.Write((short)16);       // Bits per sample
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);
            writer.Write(pcmData);

            return output.ToArray();
        }

        // ── Text Parsing Helper (Fallback) ───────────────────────────────────

        /// <summary>
        /// <summary>
        /// Phân tích văn bản bình luận thành chuỗi các mã clip âm thanh tương ứng.
        /// Bỏ qua các phần điểm số như (-10.0 điểm).
        /// </summary>
        public static List<string> ParseCommentaryToClips(string text)
        {
            var clips = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return clips;

            // 1. Phân loại tiêu đề
            if (text.Contains("Bỏ lỡ chiếu hết", StringComparison.OrdinalIgnoreCase)) clips.Add("title_missed_mate");
            else if (text.Contains("Đối thủ dâng quân", StringComparison.OrdinalIgnoreCase)) clips.Add("title_opp_gift");
            else if (text.Contains("nước đi ngớ ngẩn", StringComparison.OrdinalIgnoreCase) && text.Contains("Đối thủ", StringComparison.OrdinalIgnoreCase)) clips.Add("title_opp_blunder_move");
            else if (text.Contains("Đối thủ tự hủy", StringComparison.OrdinalIgnoreCase)) clips.Add("title_opp_blunder");
            else if (text.Contains("thiên tài của đối thủ", StringComparison.OrdinalIgnoreCase)) clips.Add("title_opp_brilliant");
            else if (text.Contains("thiên tài", StringComparison.OrdinalIgnoreCase)) clips.Add("title_brilliant");
            else if (text.Contains("Chiếu hết!", StringComparison.OrdinalIgnoreCase)) clips.Add("title_mate");
            else if (text.Contains("nước đi tốt của đối thủ", StringComparison.OrdinalIgnoreCase)) clips.Add("title_opp_best");
            else if (text.Contains("Nước đi tốt", StringComparison.OrdinalIgnoreCase) || text.Contains("đỉnh chóp", StringComparison.OrdinalIgnoreCase) || text.Contains("tốt nhất", StringComparison.OrdinalIgnoreCase)) clips.Add("title_best");
            else if (text.Contains("Quá mượt", StringComparison.OrdinalIgnoreCase)) clips.Add("title_excellent");
            else if (text.Contains("Tạm ổn", StringComparison.OrdinalIgnoreCase)) clips.Add("title_good");
            else if (text.Contains("non tay", StringComparison.OrdinalIgnoreCase)) clips.Add("title_inaccuracy");
            else if (text.Contains("ngáo ngơ", StringComparison.OrdinalIgnoreCase)) clips.Add("title_mistake");
            else if (text.Contains("ngớ ngẩn", StringComparison.OrdinalIgnoreCase)) clips.Add("title_blunder");

            // 2. Kiểm tra các câu mở đầu khai cuộc
            if (text.Contains("Tốt e4 (Khai cuộc Tốt Vua)")) clips.Add("open_e4");
            else if (text.Contains("Tốt d4 (Khai cuộc Tốt Hậu)")) clips.Add("open_d4");
            else if (text.Contains("Khai cuộc Anh")) clips.Add("open_c4");
            else if (text.Contains("đối xứng với e5")) clips.Add("open_e5");
            else if (text.Contains("phòng thủ Sicilian")) clips.Add("open_c5");
            else if (text.Contains("Phòng thủ Scandinavian")) clips.Add("open_scandinavian");
            else if (text.Contains("trung tâm d5")) clips.Add("open_d5");
            else if (text.Contains("Phòng thủ Pháp")) clips.Add("open_french");
            else if (text.Contains("Phòng thủ Caro-Kann")) clips.Add("open_caro_kann");
            else if (text.Contains("Khai cuộc Réti")) clips.Add("open_nf3");
            else if (text.Contains("Phát triển Mã lên f6")) clips.Add("open_nf6");
            else if (text.Contains("Khai cuộc Ý")) clips.Add("open_italian");
            else if (text.Contains("Khai cuộc Tây Ban Nha") || text.Contains("Ruy Lopez")) clips.Add("open_ruy_lopez");
            else if (text.Contains("Gambit Hậu")) clips.Add("open_queens_gambit");
            else if (text.Contains("Hệ thống London")) clips.Add("open_london");
            else if (text.Contains("Gambit Vua")) clips.Add("open_kings_gambit");
            else if (text.Contains("Khai cuộc Scotch")) clips.Add("open_scotch");
            else if (text.Contains("Khai cuộc 4 Mã")) clips.Add("open_four_knights");
            else if (text.Contains("Phòng thủ Petrov")) clips.Add("open_petrov");
            else if (text.Contains("Phòng thủ King's Indian")) clips.Add("open_kid");
            else if (text.Contains("Phòng thủ Slav")) clips.Add("open_slav");
            else if (text.Contains("Cách khai cuộc rất bài bản")) clips.Add("open_dev_principles");
            else if (text.Contains("hoàn tất việc nhập thành sớm")) clips.Add("open_castle_early");
            else if (text.Contains("Xuất Hậu quá sớm")) clips.Add("open_queen_early_warn");
            else if (text.Contains("Di chuyển một quân nhiều lần")) clips.Add("open_same_piece_warn");

            // 3. Kiểm tra các hành động nhập thành
            if (text.Contains("Nhập thành gần")) clips.Add("act_castle_near");
            else if (text.Contains("Nhập thành xa")) clips.Add("act_castle_far");

            // 4. Tìm các câu cợt nhả đặc trưng
            // Bỏ lỡ chiếu hết (Missed mate)
            if (text.Contains("suy thoái tư duy")) clips.Add("react_missed_mate_1");
            else if (text.Contains("bay lên vũ trụ")) clips.Add("react_missed_mate_2");
            else if (text.Contains("thử lòng kiên nhẫn")) clips.Add("react_missed_mate_3");
            else if (text.Contains("đóng băng toàn phần")) clips.Add("react_missed_mate_4");
            else if (text.Contains("tiểu thuyết ngôn tình")) clips.Add("react_missed_mate_5");

            // Địch hiến tặng quân
            else if (text.Contains("hiến tặng cho bạn quân") || text.Contains("người tốt như này"))
            {
                clips.Add("opp_gift_lead_1");
                ExtractPieceClip(text, clips);
                clips.Add("opp_gift_tail_1");
            }
            else if (text.Contains("ship tận tay quân"))
            {
                clips.Add("opp_gift_lead_2");
                ExtractPieceClip(text, clips);
                clips.Add("opp_gift_tail_2");
            }
            else if (text.Contains("từ thiện không hề nhẹ"))
            {
                clips.Add("opp_gift_lead_3");
                ExtractPieceClip(text, clips);
                clips.Add("opp_gift_tail_3");
            }
            else if (text.Contains("dâng tận miệng"))
            {
                clips.Add("opp_gift_lead_4");
                ExtractPieceClip(text, clips);
                clips.Add("opp_gift_tail_4");
            }

            // Địch tự hủy / blunder
            else if (text.Contains("tự hủy đi vào lòng đất")) clips.Add("opp_blunder_1");
            else if (text.Contains("cạn lời")) clips.Add("opp_blunder_2");
            else if (text.Contains("quăng game thế kỷ")) clips.Add("opp_blunder_3");
            else if (text.Contains("nước cờ mù mắt")) clips.Add("opp_blunder_4");
            else if (text.Contains("ngồi rung đùi hưởng thụ")) clips.Add("opp_blunder_5");
            else if (text.Contains("tự đào hố chôn mình")) clips.Add("opp_blunder_6");

            // Các câu thông thường
            else if (text.Contains("Mù cờ hay sao")) clips.Add("react_blunder_1");
            else if (text.Contains("biếu quân không công")) clips.Add("react_blunder_2");
            else if (text.Contains("thậm chí không cần Stockfish")) clips.Add("react_blunder_3");
            else if (text.Contains("Đánh cờ bằng niềm tin")) clips.Add("react_blunder_4");
            else if (text.Contains("thấy cả tương lai u tối")) clips.Add("react_blunder_5");
            else if (text.Contains("thử thách khó cho bản thân")) clips.Add("react_blunder_6");
            else if (text.Contains("thảm họa nhất năm")) clips.Add("react_blunder_7");

            else if (text.Contains("làm từ thiện")) clips.Add("react_mistake_1");
            else if (text.Contains("quá hào phóng")) clips.Add("react_mistake_2");
            else if (text.Contains("Kỳ thủ 5 tuổi cũng biết")) clips.Add("react_mistake_3");
            else if (text.Contains("đi thẳng vào bẫy")) clips.Add("react_mistake_4");
            else if (text.Contains("lệch nhịp")) clips.Add("react_mistake_5");
            else if (text.Contains("não bộ vừa có dấu hiệu quá tải", StringComparison.OrdinalIgnoreCase)) clips.Add("react_mistake_6");

            else if (text.Contains("quên muối")) clips.Add("react_inacc_1");
            else if (text.Contains("thở phào nhẹ nhõm")) clips.Add("react_inacc_2");
            else if (text.Contains("giảm áp lực")) clips.Add("react_inacc_3");
            else if (text.Contains("hơi cấn")) clips.Add("react_inacc_4");
            else if (text.Contains("bóp nghẹt đối thủ")) clips.Add("react_inacc_5");
            else if (text.Contains("đang có đà tấn công", StringComparison.OrdinalIgnoreCase)) clips.Add("react_inacc_6");

            else if (text.Contains("củng cố thế trận") || text.Contains("chuẩn bài như sách giáo khoa")) clips.Add("react_best_1");
            else if (text.Contains("giữ vững quyền chủ động") || text.Contains("Mượt như đổ dầu")) clips.Add("react_best_2");
            else if (text.Contains("nhịp nhàng và đúng bài bản") || text.Contains("từng pixel")) clips.Add("react_best_3");
            else if (text.Contains("kiểm soát tốt các ô trọng yếu") || text.Contains("Phong thái đại kiện tướng")) clips.Add("react_best_4");
            else if (text.Contains("gọn gàng và an toàn") || text.Contains("sắc như dao cạo")) clips.Add("react_best_5");
            else if (text.Contains("phân tích của Stockfish") || text.Contains("nghiêng mình chào thua")) clips.Add("react_best_6");

            else if (text.Contains("toát mồ hôi hột")) clips.Add("react_excellent_1");
            else if (text.Contains("không hoàn hảo tuyệt đối", StringComparison.OrdinalIgnoreCase)) clips.Add("react_excellent_2");
            else if (text.Contains("còn cách tốt hơn tí xíu")) clips.Add("react_excellent_3");
            else if (text.Contains("căng não suy nghĩ")) clips.Add("react_excellent_4");
            else if (text.Contains("càng lúc càng mở rộng")) clips.Add("react_excellent_5");

            else if (text.Contains("chưa làm hỏng trận")) clips.Add("react_good_1");
            else if (text.Contains("không bị mất gì")) clips.Add("react_good_2");
            else if (text.Contains("không làm khán giả ngáp")) clips.Add("react_good_3");
            else if (text.Contains("an toàn tối thiểu")) clips.Add("react_good_4");
            else if (text.Contains("ru ngủ đối phương")) clips.Add("react_good_5");

            else if (text.Contains("lợi thế chiến thuật") || text.Contains("tàn nhẫn như cướp biển")) clips.Add("react_brilliant_1");
            else if (text.Contains("khai thác chính xác sơ hở") || text.Contains("biểu diễn nghệ thuật")) clips.Add("react_brilliant_2");
            else if (text.Contains("Quyết định táo bạo") || text.Contains("toát mồ hôi lạnh")) clips.Add("react_brilliant_3");
            else if (text.Contains("mở toang cánh Vua") || text.Contains("thuốc độc bọc đường")) clips.Add("react_brilliant_4");
            else if (text.Contains("hoàn toàn áp đảo") || text.Contains("chip lượng tử")) clips.Add("react_brilliant_5");

            else if (text.Contains("xách dép về thôi")) clips.Add("react_mate_1");
            else if (text.Contains("Pack it up")) clips.Add("react_mate_2");
            else if (text.Contains("xử đẹp từ đầu đến cuối")) clips.Add("react_mate_3");
            else if (text.Contains("Đóng hòm trận đấu")) clips.Add("react_mate_4");
            else if (text.Contains("Một kiệt tác trên 64 ô cờ")) clips.Add("react_mate_5");

            // 5. Cảnh báo chiếu hết
            if (text.Contains("Toang rồi! Đối thủ có thể chiếu hết trong 1 nước")) clips.Add("mate_lose_1");
            else if (text.Contains("Toang rồi! Đối thủ có thể chiếu hết trong 2 nước")) clips.Add("mate_lose_2");
            else if (text.Contains("Toang rồi! Đối thủ có thể chiếu hết trong 3 nước")) clips.Add("mate_lose_3");
            else if (text.Contains("Chiếu hết không thể cản phá trong 1 nước")) clips.Add("mate_win_1");
            else if (text.Contains("Chiếu hết không thể cản phá trong 2 nước")) clips.Add("mate_win_2");

            return clips;
        }

        private static void ExtractPieceClip(string text, List<string> clips)
        {
            if (text.Contains("quân Hậu")) clips.Add("piece_queen");
            else if (text.Contains("quân Xe")) clips.Add("piece_rook");
            else if (text.Contains("quân Tượng")) clips.Add("piece_bishop");
            else if (text.Contains("quân Mã")) clips.Add("piece_knight");
            else if (text.Contains("quân Tốt")) clips.Add("piece_pawn");
            else clips.Add("piece_general");
        }

        private static int GetVolume()
        {
            try { return (int)Math.Clamp(AppSettings.Current.Volume, 0, 100); }
            catch { return 70; }
        }

        /// <summary>
        /// Làm sạch văn bản bình luận (giữ nguyên chữ ký cho các bài kiểm tra tự động).
        /// </summary>
        public static string CleanCommentaryForSpeech(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            string clean = text;

            clean = clean.Replace("O-O-O", "nhập thành xa")
                         .Replace("O-O", "nhập thành gần")
                         .Replace("♘", " Mã ")
                         .Replace("♗", " Tượng ")
                         .Replace("♖", " Xe ")
                         .Replace("♕", " Hậu ")
                         .Replace("♔", " Vua ")
                         .Replace("➔", " sang ")
                         .Replace("1...", "")
                         .Replace("★", "")
                         .Replace("✓", "")
                         .Replace("?!", "")
                         .Replace("??", "")
                         .Replace("!!", "")
                         .Replace("?", "")
                         .Replace("!", "");

            // Xử lý điểm số âm/dương cho giọng đọc (chuẩn hoá N.0 thành N cho tự nhiên)
            clean = Regex.Replace(clean, @"\+([0-9]+)\.0\s*điểm", "dẫn $1 điểm");
            clean = Regex.Replace(clean, @"\-([0-9]+)\.0\s*điểm", "mất $1 điểm");
            clean = Regex.Replace(clean, @"\+([0-9]+(?:\.[0-9]+)?)\s*điểm", "dẫn $1 điểm");
            clean = Regex.Replace(clean, @"\-([0-9]+(?:\.[0-9]+)?)\s*điểm", "mất $1 điểm");

            clean = clean.Replace("(", ", ").Replace(")", ", ");
            clean = Regex.Replace(clean, @"[\uD800-\uDBFF][\uDC00-\uDFFF]", "");
            clean = Regex.Replace(clean, @"[\u2600-\u27BF\uFE00-\uFE0F]", "");
            clean = Regex.Replace(clean, @"[💎⭐👑✨🟢🟡🟠🔴💡⚔️⚠️]", "");

            clean = Regex.Replace(clean, @",\s*,+", ",");
            clean = Regex.Replace(clean, @"\s+,", ",");
            clean = Regex.Replace(clean, @"\s+", " ").Trim(' ', ',');

            return clean;
        }
    }
}
