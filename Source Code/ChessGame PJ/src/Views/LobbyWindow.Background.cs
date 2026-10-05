using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ChessGame_PJ.Views
{
    // Video nền Sảnh chờ lặp 30s liền mạch (Seamless Looping qua Double-Buffering 2 MediaElements).
    // - Tối ưu GPU tối đa:
    //   1. Standby player khi ở trạng thái nghỉ được đặt Opacity = 0.0 để GPU bỏ qua hoàn toàn, không render thừa.
    //   2. Timer kiểm tra vị trí đặt chu kỳ 100ms với DispatcherPriority.Background thay vì spam GPU mỗi 25ms.
    //   3. Ẩn ảnh nền tĩnh khi video đã bắt đầu phát để tránh lãng phí GPU fill rate.
    // - Pre-roll: Player dưới được kích hoạt phát ngầm từ 0.0s TRƯỚC khi video chính kết thúc 1000ms.
    // - Cross-fade: Khi còn 500ms, player trên mờ dần trong 500ms, để lộ player dưới đã chạy ổn định.
    public partial class LobbyWindow
    {
        private const string BgVideoFileName = "lobby_loop.mp4";
        private const string BgVideoResourceName = "ChessGame_PJ.assets.videos.lobby_loop.mp4";

        private bool _bgVideoReady;
        private MediaElement? _playerTop;
        private MediaElement? _playerBottom;
        private DispatcherTimer? _videoLoopTimer;
        private TimeSpan _videoDuration = TimeSpan.FromSeconds(30.0);
        private bool _isBottomStarted;
        private bool _isTransitioning;

        private void InitBackgroundVideo()
        {
            try
            {
                string? path = FindOrExtractBackgroundVideo();
                if (path == null) return;

                var videoUri = new Uri(path, UriKind.Absolute);

                _playerTop = meBackgroundA;
                _playerBottom = meBackgroundB;

                Panel.SetZIndex(_playerTop, 2);
                Panel.SetZIndex(_playerBottom, 1);

                _playerTop.Opacity = 1.0;
                _playerBottom.Opacity = 0.0; // Standby player đặt 0.0 để GPU bỏ qua không render

                _playerTop.Visibility = Visibility.Visible;
                _playerBottom.Visibility = Visibility.Visible;

                ConfigurePlayer(_playerTop, videoUri);
                ConfigurePlayer(_playerBottom, videoUri);

                // Timer 100ms với Background priority tiết kiệm 95% CPU/GPU
                _videoLoopTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                _videoLoopTimer.Tick += VideoLoopTimer_Tick;

                StateChanged += (_, _) => UpdateBackgroundVideoPlayback();
                IsVisibleChanged += (_, _) => UpdateBackgroundVideoPlayback();
                Closed += (_, _) =>
                {
                    try
                    {
                        _videoLoopTimer?.Stop();
                        meBackgroundA.Stop(); meBackgroundA.Close();
                        meBackgroundB.Stop(); meBackgroundB.Close();
                    }
                    catch { }
                };

                // Kích hoạt nạp đồng thời CẢ 2 PLAYER:
                _playerTop.Play();
                _playerBottom.Play();
                _playerBottom.Pause();
                _playerBottom.Position = TimeSpan.Zero;
            }
            catch { }
        }

        private void ConfigurePlayer(MediaElement player, Uri source)
        {
            player.Source = source;
            player.IsMuted = true;
            player.Volume = 0;
            player.LoadedBehavior = MediaState.Manual;
            player.UnloadedBehavior = MediaState.Manual;

            player.MediaOpened += (s, _) =>
            {
                _bgVideoReady = true;
                if (s is MediaElement me && me.NaturalDuration.HasTimeSpan && me.NaturalDuration.TimeSpan.TotalSeconds > 1)
                {
                    _videoDuration = me.NaturalDuration.TimeSpan;
                }

                // Khi video đã mở thành công, ẩn ảnh tĩnh để GPU không phải vẽ 2 lớp nền
                try { imgBackground.Visibility = Visibility.Collapsed; } catch { }

                _videoLoopTimer?.Start();
            };

            player.MediaEnded += (s, _) =>
            {
                if (s == _playerTop && !_isTransitioning)
                {
                    PerformSafeSwap();
                }
            };

            player.MediaFailed += (_, _) => { };
        }

        private void VideoLoopTimer_Tick(object? sender, EventArgs e)
        {
            if (!_bgVideoReady || _playerTop == null || _playerBottom == null || _isTransitioning) return;

            try
            {
                TimeSpan pos = _playerTop.Position;

                // Giai đoạn 1: Kích hoạt player dưới chạy ngầm từ 0.0s trước khi video kết thúc 1000ms
                TimeSpan cueThreshold = _videoDuration - TimeSpan.FromMilliseconds(1000);
                if (cueThreshold > TimeSpan.FromSeconds(1) && pos >= cueThreshold && !_isBottomStarted)
                {
                    _isBottomStarted = true;
                    _playerBottom.Opacity = 1.0; // Bật hiển thị cho player dưới chạy ngầm
                    _playerBottom.Position = TimeSpan.Zero;
                    _playerBottom.Play();
                }

                // Giai đoạn 2: Khi còn 500ms (player dưới đã chạy ngầm được 500ms ổn định), bắt đầu cross-fade mờ dần player trên
                TimeSpan fadeThreshold = _videoDuration - TimeSpan.FromMilliseconds(500);
                if (fadeThreshold > TimeSpan.FromSeconds(1) && pos >= fadeThreshold)
                {
                    PerformCrossFade();
                }
            }
            catch { }
        }

        private void PerformCrossFade()
        {
            if (_isTransitioning || _playerTop == null || _playerBottom == null) return;
            _isTransitioning = true;

            try
            {
                var curTop = _playerTop;
                var curBottom = _playerBottom;

                if (!_isBottomStarted)
                {
                    curBottom.Opacity = 1.0;
                    curBottom.Position = TimeSpan.Zero;
                    curBottom.Play();
                }

                Panel.SetZIndex(curBottom, 1);
                Panel.SetZIndex(curTop, 2);

                var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(500));
                fadeOut.Completed += (_, _) =>
                {
                    try
                    {
                        Panel.SetZIndex(curBottom, 2);
                        Panel.SetZIndex(curTop, 1);

                        curTop.Pause();
                        curTop.Position = TimeSpan.Zero;
                        curTop.BeginAnimation(UIElement.OpacityProperty, null);
                        curTop.Opacity = 0.0; // Tắt hoàn toàn opacity cho player nghỉ để GPU không phải vẽ

                        _playerTop = curBottom;
                        _playerBottom = curTop;
                    }
                    catch { }
                    finally
                    {
                        _isBottomStarted = false;
                        _isTransitioning = false;
                    }
                };

                curTop.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
            catch
            {
                _isBottomStarted = false;
                _isTransitioning = false;
            }
        }

        private void PerformSafeSwap()
        {
            if (_playerTop == null || _playerBottom == null) return;
            try
            {
                var curTop = _playerTop;
                var curBottom = _playerBottom;

                curBottom.Opacity = 1.0;
                curBottom.Position = TimeSpan.Zero;
                curBottom.Play();

                Panel.SetZIndex(curBottom, 2);
                Panel.SetZIndex(curTop, 1);

                curTop.Pause();
                curTop.Position = TimeSpan.Zero;
                curTop.BeginAnimation(UIElement.OpacityProperty, null);
                curTop.Opacity = 0.0;

                _playerTop = curBottom;
                _playerBottom = curTop;
                _isBottomStarted = false;
                _isTransitioning = false;
            }
            catch { }
        }

        private void UpdateBackgroundVideoPlayback()
        {
            if (!_bgVideoReady || _playerTop == null) return;
            try
            {
                if (WindowState == WindowState.Minimized || !IsVisible)
                {
                    _videoLoopTimer?.Stop();
                    meBackgroundA.Pause();
                    meBackgroundB.Pause();
                }
                else
                {
                    _playerTop.Play();
                    _videoLoopTimer?.Start();
                }
            }
            catch { }
        }

        private static string? FindOrExtractBackgroundVideo()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string p1 = Path.Combine(baseDir, "assets", "videos", BgVideoFileName);
            if (File.Exists(p1)) return p1;

            var parent = new DirectoryInfo(baseDir).Parent;
            while (parent != null)
            {
                string candidate = Path.Combine(parent.FullName, "assets", "videos", BgVideoFileName);
                if (File.Exists(candidate)) return candidate;
                parent = parent.Parent;
            }

            try
            {
                var asm = typeof(LobbyWindow).Assembly;
                using var stream = asm.GetManifestResourceStream(BgVideoResourceName);
                if (stream == null) return null;

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string targetDir = Path.Combine(localAppData, "ChessGamePJ", "video");
                string targetPath = Path.Combine(targetDir, BgVideoFileName);

                if (File.Exists(targetPath) && new FileInfo(targetPath).Length == stream.Length)
                    return targetPath;

                Directory.CreateDirectory(targetDir);
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
            catch
            {
                return null;
            }
        }
    }
}
