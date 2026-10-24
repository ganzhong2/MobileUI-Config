using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MobileUiConfig
{
    // Sequential story-skip clicker. Slot 1 clicks, then slot 2, then slot 3.
    // Random mode picks any remaining target; cross mode keeps the 1 -> 2 -> 3 order.
    internal sealed class StorySkipService : IDisposable
    {
        private readonly AppConfig _config;
        private readonly Func<AppConfig> _rootProvider;
        private readonly Func<bool> _canScan;
        private readonly Action<string> _notify;
        private readonly Func<Rectangle> _boundsProvider;
        private readonly System.Threading.Timer _timer;
        private readonly Random _random = new Random();
        private readonly Bitmap[,] _templates = new Bitmap[3, 3];
        private readonly DateTime[,] _templateTime = new DateTime[3, 3];
        private readonly List<int> _remaining = new List<int>();
        private readonly object _gate = new object();
        private int _currentSlot = -1;
        private int _candidateCount;
        private int _busy;
        private DateTime _pausedUntil = DateTime.MinValue;
        private int _cachedInterval = 600;
        private string _assetsDir = "";

        public StorySkipService(AppConfig config, Func<AppConfig> rootProvider, Func<bool> canScan, Action<string> notify,
            Func<Rectangle> boundsProvider)
        {
            _config = config;
            _rootProvider = rootProvider;
            _canScan = canScan;
            _notify = notify;
            _boundsProvider = boundsProvider;
            AppConfig root0 = rootProvider == null ? config : rootProvider();
            int startInterval = root0 != null && root0.StorySkip != null
                ? Math.Max(200, root0.StorySkip.IntervalMs) : 600;
            _cachedInterval = startInterval;
            _timer = new System.Threading.Timer(Tick, null, startInterval, startInterval);
        }

        private Rectangle ResolveBounds()
        {
            Rectangle fallback = SystemInformation.VirtualScreen;
            if (_boundsProvider == null) return fallback;
            try
            {
                Rectangle r = _boundsProvider();
                if (r.Width > 100 && r.Height > 100) return r;
            }
            catch { }
            return fallback;
        }

        public void Apply()
        {
            lock (_gate)
            {
                _currentSlot = -1;
                _candidateCount = 0;
                _remaining.Clear();
            }
        }

        private void Tick(object state)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            try
            {
                AppConfig root = _rootProvider == null ? _config : _rootProvider();
                StorySkipConfig cfg = root == null ? null : root.StorySkip;
                if (cfg == null || !cfg.Enabled || _canScan == null || !_canScan())
                {
                    _candidateCount = 0;
                    return;
                }
                if (DateTime.UtcNow < _pausedUntil) return;

                // 动态刷新扫描间隔
                int wantInterval = Math.Max(200, cfg.IntervalMs);
                if (wantInterval != _cachedInterval)
                {
                    _cachedInterval = wantInterval;
                    _timer.Change(wantInterval, wantInterval);
                }

                int needHits = Math.Max(1, Math.Min(5, cfg.HitCount));
                lock (_gate)
                {
                    if (_remaining.Count == 0) BuildRemaining(cfg);
                    if (_remaining.Count == 0) return;
                    if (_currentSlot < 0 || !_remaining.Contains(_currentSlot))
                        _currentSlot = cfg.CrossValidation ? _remaining[0] : _remaining[_random.Next(_remaining.Count)];
                }
                int slot = _currentSlot;
                StorySkipFeatureConfig feature = cfg.Features[slot];
                if (feature == null || feature.Images == null) return;
                Rectangle bounds = ResolveBounds();
                using (Bitmap screen = FeatureCaptureForm.CaptureScreen(bounds))
                {
                    if (screen == null) return;
                    FeatureFrame frame = FeatureMatcher.Prepare(screen);
                    if (frame == null) return;
                    float best = 0f;
                    int bestTw = 0, bestTh = 0, bestX = 0, bestY = 0;
                    for (int imageIndex = 0; imageIndex < feature.Images.Count && imageIndex < 3; imageIndex++)
                    {
                        StorySkipImageConfig image = feature.Images[imageIndex];
                        if (image == null || string.IsNullOrEmpty(image.ImageFile)) continue;
                        Bitmap template = GetTemplate(slot, imageIndex);
                        if (template == null) continue;
                        MatchResult mr = FeatureMatcher.Match(frame, template,
                            image.CaptureWidth, image.CaptureHeight, image.AnchorX, image.AnchorY);
                        float threshold = cfg.Threshold <= 0f ? 0.85f : cfg.Threshold;
                        float effectiveThr = Math.Max(threshold, 0.58f);
                        bool colorOk = mr.ColorScore >= 0.50f;
                        bool passed = mr.Score >= effectiveThr && colorOk;
                        if (mr.Score >= 0.3f || passed)
                        {
                            try
                            {
                                ConfigStore.Log(string.Format(
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    "[跳剧情] 目标{0} 图{1} score={2:F3} color={3:F3} struct={4:F3} cons={5:F3} thr={6:F2} hit={7} pos=({8},{9}) {10}x{11} frame={12}x{13}",
                                    slot + 1, imageIndex + 1, mr.Score, mr.ColorScore, mr.StructScore, mr.Consistency,
                                    threshold, passed, mr.X, mr.Y, mr.W, mr.H, frame.Width, frame.Height));
                            }
                            catch { }
                        }
                        if (passed && mr.Score > best)
                        {
                            best = mr.Score;
                            bestTw = mr.W;
                            bestTh = mr.H;
                            bestX = mr.X;
                            bestY = mr.Y;
                        }
                    }
                    if (best <= 0f)
                    {
                        _candidateCount = 0;
                        return;
                    }
                    _candidateCount++;
                    if (_candidateCount < needHits) return;
                    _candidateCount = 0;
                    float sx = frame.SourceWidth > 0 ? (float)frame.SourceWidth / frame.Width : 1f;
                    float sy = frame.SourceHeight > 0 ? (float)frame.SourceHeight / frame.Height : 1f;
                    int clickX = bounds.Left + (int)Math.Round((bestX + bestTw / 2f) * sx);
                    int clickY = bounds.Top + (int)Math.Round((bestY + bestTh / 2f) * sy);
                    ClickAt(clickX, clickY);
                    lock (_gate)
                    {
                        _remaining.Remove(slot);
                        _currentSlot = -1;
                        if (_remaining.Count == 0) _pausedUntil = DateTime.UtcNow.AddMilliseconds(1500);
                    }
                    Log("自动跳过剧情：点击目标 " + (slot + 1) + " (" + clickX + "," + clickY + ")");
                }
            }
            catch (Exception ex)
            {
                ConfigStore.Log("自动跳过剧情异常: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        private void BuildRemaining(StorySkipConfig cfg)
        {
            _remaining.Clear();
            _currentSlot = -1;
            for (int i = 0; i < cfg.Features.Count && i < 3; i++)
            {
                StorySkipFeatureConfig f = cfg.Features[i];
                if (f == null || !f.Enabled || f.Images == null) continue;
                bool hasImage = false;
                for (int j = 0; j < f.Images.Count && j < 3; j++)
                {
                    StorySkipImageConfig image = f.Images[j];
                    if (image == null || string.IsNullOrEmpty(image.ImageFile)) continue;
                    if (File.Exists(ConfigStore.StorySkipPath(i, j))) { hasImage = true; break; }
                }
                if (hasImage) _remaining.Add(i);
            }
        }

        private Bitmap GetTemplate(int target, int image)
        {
            try
            {
                EnsureAssetsDir();
                string path = ConfigStore.StorySkipPath(target, image);
                if (!File.Exists(path)) return null;
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                if (_templates[target, image] != null && _templateTime[target, image] == stamp)
                    return _templates[target, image];
                using (Bitmap source = new Bitmap(path))
                {
                    if (_templates[target, image] != null) _templates[target, image].Dispose();
                    _templates[target, image] = new Bitmap(source);
                }
                _templateTime[target, image] = stamp;
                return _templates[target, image];
            }
            catch { return null; }
        }

        // 切换游戏后资源目录变化，丢弃上一个游戏的模板缓存（文件时间戳可能相同）。
        private void EnsureAssetsDir()
        {
            string dir = ConfigStore.AssetsDir;
            if (string.Equals(_assetsDir, dir, StringComparison.OrdinalIgnoreCase)) return;
            _assetsDir = dir;
            for (int target = 0; target < 3; target++)
            {
                for (int image = 0; image < 3; image++)
                {
                    if (_templates[target, image] != null) _templates[target, image].Dispose();
                    _templates[target, image] = null;
                    _templateTime[target, image] = DateTime.MinValue;
                }
            }
        }

        private void ClickAt(int x, int y)
        {
            try
            {
                SetCursorPos(x, y);
                mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(35);
                mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            }
            catch (Exception ex)
            {
                ConfigStore.Log("自动跳过点击失败: " + ex.Message);
            }
        }

        private void Log(string text)
        {
            try { ConfigStore.Log(text); } catch { }
            Action<string> h = _notify;
            if (h != null) h(text);
        }

        public void Dispose()
        {
            try { _timer.Dispose(); } catch { }
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    if (_templates[i, j] != null) _templates[i, j].Dispose();
                }
            }
            try { FeatureMatcher.ClearCache(); } catch { }
        }

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extraInfo);
    }
}
