using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Vorbis;
using NAudio.Wave;
using ChessGame_PJ.Core;

namespace ChessGame_PJ.Services
{
    /// <summary>
    /// Phát âm thanh OGG nhúng trực tiếp trong EXE — không cần thư mục assets bên ngoài.
    /// Toàn bộ sound OGG được giải mã sẵn thành chuẩn 16-bit PCM WAV trong RAM khi khởi động.
    /// Mỗi lần phát được chạy nền trong Task để đảm bảo không lag UI và không bị GC thu hồi sớm.
    /// </summary>
    public static class SoundService
    {
        private static bool _initialized = false;
        private static readonly object _initLock = new();

        // Lưu WAV bytes (chuẩn 16-bit PCM RIFF) đã giải mã sẵn: tên sound → byte[] wav
        private static readonly Dictionary<string, byte[]> _decodedWav =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] SoundNames = { "place", "replace", "warning", "beep", "interact" };

        public static void Initialize()
        {
            if (_initialized) return;
            lock (_initLock)
            {
                if (_initialized) return;

                var asm = typeof(SoundService).Assembly;

                foreach (var name in SoundNames)
                {
                    // EmbeddedResource naming: {RootNamespace}.{path with . separators}.{filename}
                    // assets/sounds/place.ogg → ChessGame_PJ.assets.sounds.place.ogg
                    string resourceName = $"ChessGame_PJ.assets.sounds.{name}.ogg";
                    try
                    {
                        using var resStream = asm.GetManifestResourceStream(resourceName);
                        if (resStream == null) continue;

                        using var ms = new MemoryStream();
                        resStream.CopyTo(ms);
                        ms.Position = 0;

                        // Giải mã OGG sang Vorbis
                        using var vorbis = new VorbisWaveReader(ms);

                        // Chuyển đổi Vorbis (32-bit IEEE Float) sang chuẩn 16-bit PCM WAV trong RAM
                        var pcm16 = new WaveFloatTo16Provider(vorbis);
                        using var wavMs = new MemoryStream();
                        using (var writer = new WaveFileWriter(wavMs, pcm16.WaveFormat))
                        {
                            byte[] buf = new byte[4096];
                            int bytesRead;
                            while ((bytesRead = pcm16.Read(buf.AsSpan())) > 0)
                            {
                                writer.Write(buf, 0, bytesRead);
                            }
                        }

                        _decodedWav[name] = wavMs.ToArray();
                    }
                    catch
                    {
                        // Bỏ qua lỗi nếu có file hỏng để không crash app
                    }
                }

                _initialized = true;
            }
        }

        private static float GetEffectiveVolume()
        {
            try
            {
                return (float)Math.Clamp(AppSettings.Current.Volume / 100.0, 0.0, 1.0);
            }
            catch
            {
                return 0.7f;
            }
        }

        public static void PlaySound(string soundName)
        {
            try
            {
                if (!_initialized) Initialize();

                float volume = GetEffectiveVolume();
                if (volume <= 0.001f) return;

                if (!_decodedWav.TryGetValue(soundName, out var wavBytes)) return;

                // Phát âm thanh trong background thread để không chặn UI và giữ player sống đến khi phát xong
                Task.Run(() =>
                {
                    try
                    {
                        using var ms = new MemoryStream(wavBytes);
                        using var reader = new WaveFileReader(ms);
                        using var waveOut = new WaveOut();
                        waveOut.Init(reader);
                        waveOut.Volume = volume;
                        waveOut.Play();

                        while (waveOut.PlaybackState == PlaybackState.Playing)
                        {
                            Thread.Sleep(10);
                        }
                    }
                    catch
                    {
                    }
                });
            }
            catch { }
        }

        public static void PlayPlace()    => PlaySound("place");
        public static void PlayReplace()  => PlaySound("replace");
        public static void PlayWarning()  => PlaySound("warning");
        public static void PlayBeep()     => PlaySound("beep");
        public static void PlayInteract() => PlaySound("interact");
    }
}
