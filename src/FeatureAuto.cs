using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MobileUiConfig
{
    internal sealed class FeatureCaptureForm : Form
    {
        private readonly Bitmap _screen;
        private readonly Rectangle _screenBounds;
        private Point _start;
        private Point _end;
        private Point _cursor;
        private bool _dragging;
        private bool _hasSelection;
        private bool _saved;

        public Bitmap ResultBitmap;
        public int ResultWidth;
        public int ResultHeight;
        // 裁剪矩形中心的屏幕绝对像素坐标，以及截图边界尺寸；
        // 识别服务据此归一化成锚点（UI 固定位置），只在锚点邻域搜索。
        public int ResultCenterX;
        public int ResultCenterY;
        public int ResultScreenWidth;
        public int ResultScreenHeight;
        public bool CaptureOk { get { return _screen != null; } }

        public FeatureCaptureForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            _screenBounds = SystemInformation.VirtualScreen;
            Bounds = _screenBounds;
            _screen = CaptureScreen(_screenBounds);
        }

        public static Bitmap CaptureScreen(Rectangle bounds)
        {
            try
            {
                Bitmap bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                }
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            Focus();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            if (_screen != null)
            {
                e.Graphics.DrawImageUnscaled(_screen, 0, 0);
            }
            else
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(20, 20, 26))) e.Graphics.FillRectangle(b, ClientRectangle);
            }
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(125, 0, 0, 0)))
            {
                e.Graphics.FillRectangle(dim, ClientRectangle);
            }

            Rectangle sel = SelectionRect();
            if (_hasSelection && sel.Width >= 1 && sel.Height >= 1)
            {
                if (_screen != null)
                {
                    GraphicsState state = e.Graphics.Save();
                    e.Graphics.SetClip(sel);
                    e.Graphics.DrawImageUnscaled(_screen, 0, 0);
                    e.Graphics.Restore(state);
                }
                using (Pen pen = new Pen(Theme.Mint, 2f))
                {
                    e.Graphics.DrawRectangle(pen, sel.X, sel.Y, sel.Width - 1, sel.Height - 1);
                }
                string size = sel.Width + " × " + sel.Height + " px";
                RectangleF label = new RectangleF(sel.X, Math.Max(4, sel.Y - Theme.S(28)), Theme.S(160), Theme.S(24));
                Theme.FillRounded(e.Graphics, label, Theme.S(6), Color.FromArgb(220, 9, 13, 21));
                Theme.DrawText(e.Graphics, size, Theme.UiFont(8.2f, true), Theme.Mint, label,
                    StringAlignment.Center, StringAlignment.Center, true);
            }

            RectangleF top = new RectangleF(0, 0, ClientSize.Width, Theme.S(48));
            using (SolidBrush bar = new SolidBrush(Color.FromArgb(225, 9, 13, 21)))
            {
                e.Graphics.FillRectangle(bar, top);
            }
            Theme.DrawText(e.Graphics, "拖动框选要识别的特征区域 · Esc 取消", Theme.Semibold(10f), Theme.Text,
                new RectangleF(Theme.S(16), 0, ClientSize.Width - Theme.S(32), Theme.S(48)),
                StringAlignment.Near, StringAlignment.Center, true);
            Theme.DrawText(e.Graphics, "建议 60–320 px；截图保存到软件同目录", Theme.UiFont(8.2f, false), Theme.Faint,
                new RectangleF(Theme.S(16), Theme.S(48), ClientSize.Width - Theme.S(32), Theme.S(24)),
                StringAlignment.Near, StringAlignment.Center, true);

            if (!_hasSelection || sel.Width < 2 || sel.Height < 2)
            {
                Point p = _cursor;
                float w = Theme.S(130);
                float h = Theme.S(30);
                float x = Math.Min(ClientSize.Width - w - 4, p.X + Theme.S(14));
                float y = Math.Min(ClientSize.Height - h - 4, p.Y + Theme.S(18));
                Theme.FillRounded(e.Graphics, new RectangleF(x, y, w, h), Theme.S(7), Color.FromArgb(230, 9, 13, 21));
                Theme.DrawText(e.Graphics, "目标：60–320 px", Theme.UiFont(8.2f, false), Theme.Muted,
                    new RectangleF(x, y, w, h), StringAlignment.Center, StringAlignment.Center, true);
            }
        }

        private Rectangle SelectionRect()
        {
            int x1 = Math.Min(_start.X, _end.X);
            int y1 = Math.Min(_start.Y, _end.Y);
            int x2 = Math.Max(_start.X, _end.X);
            int y2 = Math.Max(_start.Y, _end.Y);
            return Rectangle.FromLTRB(x1, y1, x2, y2);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragging = true;
            _start = e.Location;
            _end = e.Location;
            _cursor = e.Location;
            _hasSelection = true;
            Capture = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            _cursor = e.Location;
            if (_dragging) _end = e.Location;
            Invalidate();
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !_dragging) return;
            _dragging = false;
            _end = e.Location;
            Capture = false;
            Rectangle sel = SelectionRect();
            if (sel.Width < 24 || sel.Height < 24)
            {
                _hasSelection = false;
                Invalidate();
                return;
            }
            if (_screen != null)
            {
                Rectangle crop = Rectangle.Intersect(sel, new Rectangle(0, 0, _screen.Width, _screen.Height));
                if (crop.Width < 24 || crop.Height < 24)
                {
                    _hasSelection = false;
                    Invalidate();
                    return;
                }
                ResultBitmap = _screen.Clone(crop, PixelFormat.Format32bppArgb);
                ResultWidth = crop.Width;
                ResultHeight = crop.Height;
                ResultCenterX = (crop.Left + crop.Right) / 2;
                ResultCenterY = (crop.Top + crop.Bottom) / 2;
                ResultScreenWidth = _screenBounds.Width;
                ResultScreenHeight = _screenBounds.Height;
                _saved = true;
                DialogResult = DialogResult.OK;
                Close();
            }
            base.OnMouseUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (!_saved) DialogResult = DialogResult.Cancel;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _screen != null) _screen.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class FeatureFrame
    {
        public int Width;
        public int Height;
        public int SourceWidth;
        public int SourceHeight;
        public float[] Gray;
        public float[] R;
        public float[] G;
        public float[] B;
        public float[] Hh;
        public float[] S;
        public float[] V;
        public double[] Sum;

        public double WindowSum(double[] data, int x0, int y0, int x1, int y1)
        {
            int w1 = Width + 1;
            return data[y1 * w1 + x1] - data[y0 * w1 + x1] - data[y1 * w1 + x0] + data[y0 * w1 + x0];
        }
    }

    internal sealed class MatchResult
    {
        public float Score;
        public float ColorScore;
        public float StructScore;
        public float Consistency;
        public int X;
        public int Y;
        public int W;
        public int H;
        public int FactorIndex;
    }

    // 彩色（HSV 色块命中率）+ 结构（边缘梯度掩码 NCC）双路融合模板匹配。
    // 纯托管 System.Drawing 实现，无第三方原生依赖，适配本地 4K 与串流 1080P：
    // 帧统一降到 1600 宽，模板按裁剪时记录的 CaptureWidth/Height 正确换算基准尺寸，
    // 13 档等比缩放覆盖窗口/分辨率波动；锚点存在时只在 UI 固定位置邻域搜索。
    internal static class FeatureMatcher
    {
        private const int SearchWidth = 1100;

        // 模板预处理缓存（按 Bitmap 引用）。两个扫描服务各自持有模板实例，
        // 字典访问加锁，防止 Timer 线程并发读写。
        private static readonly Dictionary<Bitmap, PreparedTemplate> _tplCache =
            new Dictionary<Bitmap, PreparedTemplate>();

        private sealed class PreparedTemplate
        {
            public int W;
            public int H;
            public float[] Gray;
            public float[] R;
            public float[] G;
            public float[] B;
            public float[] Hh;
            public float[] S;
            public float[] V;
            public byte[] Mask;
            public int Active;
            public int[] DomR;
            public int[] DomG;
            public int[] DomB;
            public float[] DomRatio;
            public int DomCount;
            // 前景亮度极性：1=亮前景，0=暗前景；BgV 为模板背景亮度基线。
            public byte[] Bright;
            public double BgV;
            // 同模板不同缩放档的 ScaleTemplate 缓存，跨帧复用，避免每轮扫描重复重采样/Sobel/HSV。
            public Dictionary<long, ScaleTemplate> ScaleCache;
        }

        private sealed class ScaleTemplate
        {
            public int W;
            public int H;
            public float[] Gray;
            public float[] R;
            public float[] G;
            public float[] B;
            public float[] Hh;
            public float[] S;
            public float[] V;
            public byte[] Mask;
            public int Active;
            public byte[] Bright;
            public double BgV;
            // 从预处理模板复制的主色聚类，用于候选窗颜色一致性否决。
            public int DomCount;
            public int[] DomR;
            public int[] DomG;
            public int[] DomB;
            public float[] DomRatio;
        }

        private struct Region
        {
            public int X;
            public int Y;
            public int W;
            public int H;
        }

        public static void ClearCache()
        {
            lock (_tplCache)
            {
                _tplCache.Clear();
            }
        }

        public static FeatureFrame Prepare(Bitmap screen)
        {
            if (screen == null) return null;
            int sw = Math.Min(SearchWidth, screen.Width);
            int sh = Math.Max(1, (int)Math.Round(screen.Height * (sw / (double)screen.Width)));
            using (Bitmap small = ResizeBitmap(screen, sw, sh))
            {
                FeatureFrame frame = new FeatureFrame();
                frame.Width = sw;
                frame.Height = sh;
                frame.SourceWidth = screen.Width;
                frame.SourceHeight = screen.Height;
                float[] gray;
                float[] r;
                float[] g;
                float[] b;
                ReadRgb(small, out r, out g, out b, out gray);
                frame.R = r;
                frame.G = g;
                frame.B = b;
                frame.Gray = gray;
                // 帧 HSV 一次性预算，候选窗细筛/粗筛直接读取，避免逐点重复换算。
                int npx = sw * sh;
                float[] hh = new float[npx];
                float[] ss = new float[npx];
                float[] vv = new float[npx];
                for (int i = 0; i < npx; i++)
                {
                    float h, s, v;
                    RgbToHsv(r[i], g[i], b[i], out h, out s, out v);
                    hh[i] = h;
                    ss[i] = s;
                    vv[i] = v;
                }
                frame.Hh = hh;
                frame.S = ss;
                frame.V = vv;

                // V 通道积分图：候选窗背景亮度均值 O(1) 查询，避免每个候选窗再扫一遍非前景像素。
                int iw = frame.Width, ih = frame.Height;
                int stride = iw + 1;
                double[] vSum = new double[stride * (ih + 1)];
                for (int y = 0; y < ih; y++)
                {
                    int row = y * iw;
                    int sRow = (y + 1) * stride;
                    int sPrev = y * stride;
                    double rowAcc = 0;
                    for (int x = 0; x < iw; x++)
                    {
                        rowAcc += vv[row + x];
                        vSum[sRow + x + 1] = vSum[sPrev + x + 1] + rowAcc;
                    }
                }
                frame.Sum = vSum;
                return frame;
            }
        }

        public static MatchResult Match(FeatureFrame frame, Bitmap template,
            int captureWidth, int captureHeight, float anchorX, float anchorY)
        {
            MatchResult result = new MatchResult();
            if (frame == null || template == null || template.Width < 4 || template.Height < 4) return result;
            PreparedTemplate prep = GetPrepared(template);
            if (prep == null || prep.Active < 3) return result;

            double wScale = captureWidth > 0
                ? frame.Width / (double)captureWidth
                : frame.Width / (double)frame.SourceWidth;
            double hScale = captureHeight > 0
                ? frame.Height / (double)captureHeight
                : frame.Height / (double)frame.SourceHeight;
            int baseW = (int)Math.Round(template.Width * wScale);
            int baseH = (int)Math.Round(template.Height * hScale);
            if (baseW < 6) baseW = 6;
            if (baseH < 6) baseH = 6;
            if (baseW > frame.Width - 2) baseW = frame.Width - 2;
            if (baseH > frame.Height - 2) baseH = frame.Height - 2;

            float[] factors = new float[]
            {
                0.72f, 0.80f, 0.88f, 0.94f, 1.00f,
                1.06f, 1.13f, 1.22f, 1.33f, 1.46f,
                1.62f, 1.82f, 2.05f
            };
            // 先验顺序：基准档 1.0 与相邻两档先跑，真匹配高分即提前退出；
            // 其余档作为窗口/分辨率波动的兜底。
            int[] order = new int[] { 4, 3, 5, 2, 6, 1, 7, 0, 8, 9, 10, 11, 12 };

            Region region;
            bool hasAnchor = anchorX >= 0f && anchorY >= 0f;
            if (hasAnchor)
            {
                int cx = (int)Math.Round(anchorX * frame.Width);
                int cy = (int)Math.Round(anchorY * frame.Height);
                // 锚点邻域只覆盖图标周边一小圈：界面位置固定后漂移有限，
                // 收紧面积换扫描速度；出界部分由 IntersectRegion 裁到帧内。
                int halfW = Math.Max(baseW + 32, 56);
                int halfH = Math.Max(baseH + 32, 40);
                region = IntersectRegion(frame, cx - halfW, cy - halfH, halfW * 2, halfH * 2);
            }
            else
            {
                region.X = 0;
                region.Y = 0;
                region.W = frame.Width;
                region.H = frame.Height;
            }

            float best = 0f;
            int bestX = 0, bestY = 0, bestTw = 0, bestTh = 0, bestFi = 0;
            float bestColor = 0f, bestStruct = 0f, bestCons = 0f;

            // 第一阶段：只跑基准档（1.00）全屏/邻域搜索；真匹配基准档分数通常 ≥0.60，
            // 命中即返回，避免 13 档全量扫描把单轮耗时拖到秒级。
            ScaleTemplate baseSt = BuildScale(prep,
                (int)Math.Round(baseW * factors[4]), (int)Math.Round(baseH * factors[4]));
            if (baseSt.Active >= 3)
            {
                float score, color, structV, cons;
                int x, y;
                MatchScale(frame, baseSt, region, out score, out x, out y, out color, out structV, out cons);
                if (score > best)
                {
                    best = score; bestX = x; bestY = y;
                    bestTw = baseSt.W; bestTh = baseSt.H; bestFi = 4;
                    bestColor = color; bestStruct = structV; bestCons = cons;
                }
            }
            if (best < 0.60f)
            {
                // 第二阶段：基准档未命中，只跑基准相邻 0.88/0.94/1.06/1.13 四档兜底窗口波动，
                // 极端缩放档在本机分辨率稳定场景下不再全屏扫描，避免单模板兜底耗时暴涨。
                for (int oi = 0; oi < order.Length; oi++)
                {
                    int fi = order[oi];
                    if (fi == 4) continue;
                    if (fi != 3 && fi != 5 && fi != 2 && fi != 6) continue;
                    int tw = (int)Math.Round(baseW * factors[fi]);
                    int th = (int)Math.Round(baseH * factors[fi]);
                    if (tw < 6 || th < 6 || tw > frame.Width || th > frame.Height) continue;
                    ScaleTemplate st = BuildScale(prep, tw, th);
                    if (st.Active < 3) continue;

                    float score, color, structV, cons;
                    int x, y;
                    MatchScale(frame, st, region, out score, out x, out y, out color, out structV, out cons);
                    if (score > best)
                    {
                        best = score;
                        bestX = x;
                        bestY = y;
                        bestTw = st.W;
                        bestTh = st.H;
                        bestFi = fi;
                        bestColor = color;
                        bestStruct = structV;
                        bestCons = cons;
                    }
                    if (best >= 0.65f) break;
                }
            }

            result.Score = best;
            result.ColorScore = bestColor;
            result.StructScore = bestStruct;
            result.Consistency = bestCons;
            result.X = bestX;
            result.Y = bestY;
            result.W = bestTw;
            result.H = bestTh;
            result.FactorIndex = bestFi;
            return result;
        }

        private static PreparedTemplate GetPrepared(Bitmap bmp)
        {
            lock (_tplCache)
            {
                PreparedTemplate cached;
                if (_tplCache.TryGetValue(bmp, out cached)) return cached;
            }
            PreparedTemplate prep = BuildPrepared(bmp);
            if (prep != null)
            {
                lock (_tplCache)
                {
                    _tplCache[bmp] = prep;
                }
            }
            return prep;
        }

        private static PreparedTemplate BuildPrepared(Bitmap bmp)
        {
            PreparedTemplate prep = new PreparedTemplate();
            int w = bmp.Width;
            int h = bmp.Height;
            prep.W = w;
            prep.H = h;
            float[] r, g, b, gray;
            ReadRgb(bmp, out r, out g, out b, out gray);
            prep.R = r;
            prep.G = g;
            prep.B = b;
            prep.Gray = gray;

            prep.Hh = new float[w * h];
            prep.S = new float[w * h];
            prep.V = new float[w * h];
            prep.Mask = new byte[w * h];
            for (int i = 0; i < w * h; i++)
            {
                float hh, ss, vv;
                RgbToHsv(r[i], g[i], b[i], out hh, out ss, out vv);
                prep.Hh[i] = hh;
                prep.S[i] = ss;
                prep.V[i] = vv;
            }

            // 背景色：四边环带 RGB 与 V 均值，用于把模板自带的黑/深色底图排除出前景掩码。
            double bgR = 0, bgG = 0, bgB = 0, bgV = 0;
            int bgCnt = 0;
            for (int x = 0; x < w; x++)
            {
                AccumBorder(ref bgR, ref bgG, ref bgB, ref bgCnt, r, g, b, w, 0, x);
                bgV += prep.V[x];
                AccumBorder(ref bgR, ref bgG, ref bgB, ref bgCnt, r, g, b, w, h - 1, x);
                bgV += prep.V[(h - 1) * w + x];
            }
            for (int y = 1; y < h - 1; y++)
            {
                AccumBorder(ref bgR, ref bgG, ref bgB, ref bgCnt, r, g, b, w, y, 0);
                bgV += prep.V[y * w];
                AccumBorder(ref bgR, ref bgG, ref bgB, ref bgCnt, r, g, b, w, y, w - 1);
                bgV += prep.V[y * w + w - 1];
            }
            if (bgCnt > 0) { bgR /= bgCnt; bgG /= bgCnt; bgB /= bgCnt; bgV /= bgCnt; }

            double mean = 0;
            for (int i = 0; i < w * h; i++) mean += gray[i];
            mean /= (w * h);

            byte[] strict = new byte[w * h];
            int strictCnt = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    double colorDist = ColorEuclidean(r[i], g[i], b[i], bgR, bgG, bgB);
                    // 前景：与背景色距明显不同（彩色图标），或明显亮于背景（白字/亮图标）；
                    // 暗于背景的深色面板/阴影不算前景，避免底色像素混入掩码导致颜色路虚高。
                    bool fg = colorDist > 38.0 || prep.V[i] > bgV * 1.25f;
                    if (fg) { strict[i] = 1; strictCnt++; }
                }
            }

            int need = (int)(w * h * 0.04);
            if (strictCnt >= need)
            {
                prep.Mask = strict;
                prep.Active = strictCnt;
            }
            else
            {
                // 特征像素太少：放宽色距与亮度阈值，仍以「与四边背景不同」为准，
                // 不把底色/边缘梯度像素纳入前景；前景实在过少才整幅参与。
                int looseCnt = 0;
                for (int i = 0; i < w * h; i++)
                {
                    double cd = ColorEuclidean(r[i], g[i], b[i], bgR, bgG, bgB);
                    if (cd > 28.0 || prep.V[i] > bgV * 1.15f)
                    {
                        prep.Mask[i] = 1;
                        looseCnt++;
                    }
                }
                prep.Active = looseCnt;
                if (looseCnt < (int)(w * h * 0.015))
                {
                    for (int i = 0; i < w * h; i++) prep.Mask[i] = 1;
                    prep.Active = w * h;
                }
            }

            // 背景亮度基线（与上面四边 V 均值一致），供候选窗做亮度极性比对。
            prep.BgV = bgCnt > 0 ? bgV : 0.35;
            prep.Bright = new byte[w * h];
            for (int i = 0; i < w * h; i++)
            {
                if (prep.Mask[i] != 0 && prep.V[i] > prep.BgV * 1.12) prep.Bright[i] = 1;
            }

            // 主色聚类（RGB 量化到 32 级），取占比前 3 簇，用于颜色一致性否决。
            Dictionary<int, int> buckets = new Dictionary<int, int>();
            Dictionary<int, int> bucketR = new Dictionary<int, int>();
            Dictionary<int, int> bucketG = new Dictionary<int, int>();
            Dictionary<int, int> bucketB = new Dictionary<int, int>();
            for (int i = 0; i < w * h; i++)
            {
                if (prep.Mask[i] == 0) continue;
                int key = (((int)r[i] >> 3) << 10) | (((int)g[i] >> 3) << 5) | ((int)b[i] >> 3);
                int cur;
                buckets.TryGetValue(key, out cur);
                buckets[key] = cur + 1;
                int rr, gg, bb;
                bucketR.TryGetValue(key, out rr);
                bucketG.TryGetValue(key, out gg);
                bucketB.TryGetValue(key, out bb);
                bucketR[key] = rr + (int)r[i];
                bucketG[key] = gg + (int)g[i];
                bucketB[key] = bb + (int)b[i];
            }
            List<int> keys = new List<int>(buckets.Keys);
            keys.Sort(delegate(int a, int k2) { return buckets[k2].CompareTo(buckets[a]); });

            prep.DomCount = Math.Min(3, keys.Count);
            prep.DomR = new int[3];
            prep.DomG = new int[3];
            prep.DomB = new int[3];
            prep.DomRatio = new float[3];
            for (int k = 0; k < prep.DomCount; k++)
            {
                int key = keys[k];
                int cnt = buckets[key];
                prep.DomR[k] = bucketR[key] / cnt;
                prep.DomG[k] = bucketG[key] / cnt;
                prep.DomB[k] = bucketB[key] / cnt;
                prep.DomRatio[k] = cnt / (float)prep.Active;
            }
            return prep;
        }

        private static ScaleTemplate BuildScale(PreparedTemplate prep, int tw, int th)
        {
            if (prep.ScaleCache == null) prep.ScaleCache = new Dictionary<long, ScaleTemplate>();
            long key = ((long)tw << 32) | (uint)th;
            ScaleTemplate cached;
            if (prep.ScaleCache.TryGetValue(key, out cached)) return cached;

            ScaleTemplate st = new ScaleTemplate();
            st.W = tw;
            st.H = th;
            int n = tw * th;
            st.Gray = new float[n];
            st.R = new float[n];
            st.G = new float[n];
            st.B = new float[n];
            st.Mask = new byte[n];
            st.Bright = new byte[n];
            st.Active = 0;

            for (int y = 0; y < th; y++)
            {
                int sy = (int)((long)y * prep.H / th);
                int srow = sy * prep.W;
                int drow = y * tw;
                for (int x = 0; x < tw; x++)
                {
                    int sx = (int)((long)x * prep.W / tw);
                    int si = srow + sx;
                    int di = drow + x;
                    st.Gray[di] = prep.Gray[si];
                    st.R[di] = prep.R[si];
                    st.G[di] = prep.G[si];
                    st.B[di] = prep.B[si];
                    if (prep.Mask[si] != 0)
                    {
                        st.Mask[di] = 1;
                        st.Bright[di] = prep.Bright[si];
                        st.Active++;
                    }
                }
            }
            st.Hh = new float[n];
            st.S = new float[n];
            st.V = new float[n];
            for (int i = 0; i < n; i++)
            {
                float hh, ss, vv;
                RgbToHsv(st.R[i], st.G[i], st.B[i], out hh, out ss, out vv);
                st.Hh[i] = hh;
                st.S[i] = ss;
                st.V[i] = vv;
            }

            st.DomCount = prep.DomCount;
            st.DomR = prep.DomR;
            st.DomG = prep.DomG;
            st.DomB = prep.DomB;
            st.DomRatio = prep.DomRatio;
            st.BgV = prep.BgV;
            prep.ScaleCache[key] = st;
            return st;
        }

        private static void MatchScale(FeatureFrame frame, ScaleTemplate st, Region region,
            out float score, out int bestX, out int bestY, out float bestColor,
            out float bestStruct, out float bestCons)
        {
            score = 0f;
            bestX = 0;
            bestY = 0;
            bestColor = 0f;
            bestStruct = 0f;
            bestCons = 0f;

            int tw = st.W;
            int th = st.H;
            int rx = region.X;
            int ry = region.Y;
            int rx2 = region.X + region.W - tw;
            int ry2 = region.Y + region.H - th;
            if (rx2 < rx) rx2 = rx;
            if (ry2 < ry) ry2 = ry;

            // 全屏大区域粗筛网格适度放疏，锚点小邻域网格 2px 密扫；候选位置由精筛 ±2 邻域对齐。
            bool fullScreen = region.W > 400;
            int coarseDiv = fullScreen ? 5 : 2;
            int stepX = fullScreen ? Math.Min(tw, Math.Max(4, tw / coarseDiv)) : 2;
            int stepY = fullScreen ? Math.Min(th, Math.Max(4, th / coarseDiv)) : 2;

            List<int> candX = new List<int>();
            List<int> candY = new List<int>();
            List<float> candColor = new List<float>();
            float coarseBest = -1f;
            int cbX = rx, cbY = ry;

            // 颜色漏斗粗筛：单次扫描，前景像素抽样比较 HSV；同时记录全局粗分最高位置兜底，
            // 避免粗筛全灭时二次全扫的重复开销。
            for (int y = ry; y <= ry2; y += stepY)
            {
                for (int x = rx; x <= rx2; x += stepX)
                {
                    float hit = ColorHit(frame, st, x, y, 3);
                    if (hit > coarseBest) { coarseBest = hit; cbX = x; cbY = y; }
                    if (hit >= 0.38f)
                    {
                        candX.Add(x);
                        candY.Add(y);
                        candColor.Add(hit);
                    }
                }
            }
            if (candX.Count == 0 && coarseBest > 0f)
            {
                candX.Add(cbX);
                candY.Add(cbY);
                candColor.Add(coarseBest);
            }
            // 候选过多时只保留粗分最高的前 12 个，控制精筛开销。
            if (candX.Count > 12)
            {
                List<int> order = new List<int>();
                for (int i = 0; i < candX.Count; i++) order.Add(i);
                order.Sort(delegate(int a, int b2) { return candColor[b2].CompareTo(candColor[a]); });
                List<int> nX = new List<int>();
                List<int> nY = new List<int>();
                List<float> nC = new List<float>();
                for (int k = 0; k < 12; k++)
                {
                    int i = order[k];
                    nX.Add(candX[i]);
                    nY.Add(candY[i]);
                    nC.Add(candColor[i]);
                }
                candX = nX;
                candY = nY;
                candColor = nC;
            }

            for (int c = 0; c < candX.Count; c++)
            {
                int sx0 = Math.Max(rx, candX[c] - 2);
                int sx1 = Math.Min(rx2, candX[c] + 2);
                int sy0 = Math.Max(ry, candY[c] - 2);
                int sy1 = Math.Min(ry2, candY[c] + 2);
                for (int y = sy0; y <= sy1; y++)
                {
                    for (int x = sx0; x <= sx1; x++)
                    {
                        float colorHit, structV, cons;
                        EvaluateAt(frame, st, x, y, out colorHit, out structV, out cons);
                        if (colorHit < 0.50f) continue;
                        float struct01 = structV < 0f ? 0f : (structV > 1f ? 1f : structV);
                        // 颜色为主（0.7）、形状为辅（0.3）；形状几乎不匹配（<0.15）时
                        // 否决大部分颜色分，防止深色背景上颜色虚高导致误触。
                        float val = 0.70f * colorHit + 0.30f * struct01;
                        if (struct01 < 0.15f) val = Math.Min(val, colorHit * 0.50f);
                        if (colorHit < 0.50f) val = Math.Min(val, colorHit * 0.60f);
                        if (val > score)
                        {
                            score = val;
                            bestX = x;
                            bestY = y;
                            bestColor = colorHit;
                            bestStruct = structV;
                            bestCons = cons;
                        }
                    }
                }
            }
            score = Math.Max(0f, Math.Min(1f, score));
        }

        // 全量颜色命中率 + 前景亮度极性形状命中率 + 主色一致性：只在掩码前景像素上计算。
        private static void EvaluateAt(FeatureFrame frame, ScaleTemplate st, int x0, int y0,
            out float colorHit, out float structV, out float cons)
        {
            colorHit = 0f;
            structV = 0f;
            cons = 0.8f;
            int tw = st.W;
            int th = st.H;
            int iw = frame.Width;
            int active = st.Active;
            if (active <= 0) return;

            // 候选窗背景亮度基线：V 积分图 O(1) 取窗内整体均值（前景占比小，近似局部背景）。
            int clX0 = Math.Max(0, x0);
            int clY0 = Math.Max(0, y0);
            int clX1 = Math.Min(frame.Width, x0 + tw);
            int clY1 = Math.Min(frame.Height, y0 + th);
            double baseV = 0.35;
            if (clX1 > clX0 && clY1 > clY0 && frame.Sum != null)
            {
                double winV = frame.WindowSum(frame.Sum, clX0, clY0, clX1, clY1);
                baseV = winV / ((double)(clX1 - clX0) * (clY1 - clY0));
            }

            int hit = 0;
            int shapeHit = 0;
            int[] candR = new int[active];
            int[] candG = new int[active];
            int[] candB = new int[active];
            int candCnt = 0;

            for (int y = 0; y < th; y++)
            {
                int irow = (y0 + y) * iw + x0;
                int trow = y * tw;
                for (int x = 0; x < tw; x++)
                {
                    int ti = trow + x;
                    if (st.Mask[ti] == 0) continue;
                    int fi = irow + x;
                    float fv = frame.V[fi];
                    if (ColorMatch(st.Hh[ti], st.S[ti], st.V[ti], frame.Hh[fi], frame.S[fi], fv))
                    {
                        hit++;
                    }
                    // 极性形状：模板前景相对模板背景的亮度差，与候选窗前景相对局部背景的
                    // 亮度差同号且量级达到一半以上，即形状位置对上；不做全局亮/暗二分类，
                    // 适配深色底图标与渐变背景。
                    double td = st.V[ti] - st.BgV;
                    double fd = fv - baseV;
                    if (td >= 0)
                    {
                        if (fd >= 0.05f && fd >= 0.5 * td) shapeHit++;
                    }
                    else
                    {
                        if (fd <= -0.05f && fd <= 0.5 * td) shapeHit++;
                    }
                    candR[candCnt] = (int)frame.R[fi];
                    candG[candCnt] = (int)frame.G[fi];
                    candB[candCnt] = (int)frame.B[fi];
                    candCnt++;
                }
            }
            colorHit = hit / (float)active;
            structV = shapeHit / (float)active;

            if (candCnt >= 8 && st.DomCount > 0)
            {
                double matchRatio = 0;
                for (int k = 0; k < st.DomCount; k++)
                {
                    bool found = false;
                    for (int c = 0; c < candCnt; c++)
                    {
                        if (ColorEuclidean(candR[c], candG[c], candB[c],
                            st.DomR[k], st.DomG[k], st.DomB[k]) <= 32.0)
                        {
                            found = true;
                            break;
                        }
                    }
                    matchRatio += st.DomRatio[k] * (found ? 1.0 : 0.0);
                }
                cons = (float)matchRatio;
            }
        }

        // 候选位置颜色命中率（抽样版，粗筛用）。
        private static float ColorHit(FeatureFrame frame, ScaleTemplate st, int x0, int y0, int every)
        {
            int tw = st.W;
            int th = st.H;
            int iw = frame.Width;
            int hit = 0;
            int cnt = 0;
            // 前景像素逐个比较，不做网格跳步：稀疏文字/小图标跳步后容易整笔漏掉；
            // 前景较多时隔行抽样比较，控制粗筛全屏网格的总开销。
            int sub = st.Active > 48 ? 2 : 1;
            int subIdx = 0;
            for (int y = 0; y < th; y++)
            {
                int irow = (y0 + y) * iw + x0;
                int trow = y * tw;
                for (int x = 0; x < tw; x++)
                {
                    int ti = trow + x;
                    if (st.Mask[ti] == 0) continue;
                    subIdx++;
                    if (subIdx % sub != 0) continue;
                    int fi = irow + x;
                    float fh = frame.Hh[fi];
                    float fs = frame.S[fi];
                    float fv = frame.V[fi];
                    if (ColorMatch(st.Hh[ti], st.S[ti], st.V[ti], fh, fs, fv))
                    {
                        hit++;
                    }
                    cnt++;
                }
            }
            if (cnt <= 0) return 0f;
            return hit / (float)cnt;
        }

        private static Region IntersectRegion(FeatureFrame frame, int x, int y, int w, int h)
        {
            Region r;
            r.X = Math.Max(0, x);
            r.Y = Math.Max(0, y);
            r.W = Math.Min(frame.Width - r.X, w);
            r.H = Math.Min(frame.Height - r.Y, h);
            if (r.W < 16) r.W = 16;
            if (r.H < 16) r.H = 16;
            return r;
        }

        private static Bitmap ResizeBitmap(Bitmap src, int w, int h)
        {
            Bitmap small = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                g.DrawImage(src, new Rectangle(0, 0, w, h),
                    new Rectangle(0, 0, src.Width, src.Height), GraphicsUnit.Pixel);
            }
            return small;
        }

        private static void ReadRgb(Bitmap bmp, out float[] r, out float[] g, out float[] b, out float[] gray)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            Bitmap work = bmp;
            Bitmap converted = null;
            try
            {
                if (bmp.PixelFormat != PixelFormat.Format24bppRgb)
                {
                    converted = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                    using (Graphics gg = Graphics.FromImage(converted))
                    {
                        gg.DrawImage(bmp, new Rectangle(0, 0, w, h));
                    }
                    work = converted;
                }
                Rectangle rect = new Rectangle(0, 0, w, h);
                BitmapData bd = work.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                r = new float[w * h];
                g = new float[w * h];
                b = new float[w * h];
                gray = new float[w * h];
                try
                {
                    byte[] buf = new byte[bd.Stride * bd.Height];
                    Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
                    int idx = 0;
                    for (int y = 0; y < h; y++)
                    {
                        int row = y * bd.Stride;
                        for (int x = 0; x < w; x++)
                        {
                            int i = row + x * 3;
                            b[idx] = buf[i];
                            g[idx] = buf[i + 1];
                            r[idx] = buf[i + 2];
                            gray[idx] = buf[i] * 0.114f + buf[i + 1] * 0.587f + buf[i + 2] * 0.299f;
                            idx++;
                        }
                    }
                }
                finally { work.UnlockBits(bd); }
            }
            finally
            {
                if (converted != null) converted.Dispose();
            }
        }



        private static void AccumBorder(ref double bgR, ref double bgG, ref double bgB, ref int cnt,
            float[] r, float[] g, float[] b, int w, int y, int x)
        {
            int i = y * w + x;
            bgR += r[i];
            bgG += g[i];
            bgB += b[i];
            cnt++;
        }

        private static double ColorEuclidean(float r, float g, float b, double cr, double cg, double cb)
        {
            double dr = r - cr;
            double dg = g - cg;
            double db = b - cb;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        private static double ColorEuclidean(int r, int g, int b, double cr, double cg, double cb)
        {
            double dr = r - cr;
            double dg = g - cg;
            double db = b - cb;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        private static bool ColorMatch(float h1, float s1, float v1, float h2, float s2, float v2)
        {
            if (s1 < 0.18f || s2 < 0.18f)
            {
                return Math.Abs(v1 - v2) <= 0.34f;
            }
            float dh = Math.Abs(h1 - h2);
            dh = Math.Min(dh, 360f - dh);
            return dh <= 26f && Math.Abs(s1 - s2) <= 0.28f && Math.Abs(v1 - v2) <= 0.36f;
        }

        private static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
        {
            float rf = r / 255f;
            float gf = g / 255f;
            float bf = b / 255f;
            float max = Math.Max(rf, Math.Max(gf, bf));
            float min = Math.Min(rf, Math.Min(gf, bf));
            float d = max - min;
            v = max;
            s = max <= 0f ? 0f : d / max;
            if (d < 1e-6f)
            {
                h = 0f;
                return;
            }
            if (max == rf) h = (gf - bf) / d;
            else if (max == gf) h = 2f + (bf - rf) / d;
            else h = 4f + (rf - gf) / d;
            h *= 60f;
            if (h < 0f) h += 360f;
        }
    }

    internal sealed class FeatureAutoSwitchService : IDisposable
    {
        private readonly AppConfig _config;
        private readonly Func<AppConfig> _rootProvider;
        private readonly Func<bool> _canScan;
        private readonly Action<int, string> _switchMode;
        private readonly Action<string> _status;
        private readonly Func<Rectangle> _boundsProvider;
        private readonly System.Threading.Timer _timer;
        private readonly Dictionary<int, Bitmap[]> _templates = new Dictionary<int, Bitmap[]>();
        private readonly Dictionary<int, DateTime[]> _templateTime = new Dictionary<int, DateTime[]>();
        private int _busy;
        private int _candidateMode = -1;
        private int _candidateCount;
        private DateTime _lastSwitch = DateTime.MinValue;
        private DateTime _stickyUntil = DateTime.MinValue;
        // 上一次成功切换到的目标模式：用于切换后巩固窗口内识别反向回切并要求二次确认。
        private int _lastSwitchTarget = -1;
        private int _cachedInterval = 400;
        private string _assetsDir = "";
        private DateTime _lastHeartbeat = DateTime.MinValue;

        public FeatureAutoSwitchService(AppConfig config, Func<AppConfig> rootProvider, Func<bool> canScan, Action<int, string> switchMode,
            Action<string> status, Func<Rectangle> boundsProvider)
        {
            _config = config;
            _rootProvider = rootProvider;
            _canScan = canScan;
            _switchMode = switchMode;
            _status = status;
            _boundsProvider = boundsProvider;
            _timer = new System.Threading.Timer(Tick, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start()
        {
            // 后台暂停恢复后，清掉上一回合的候选计数与巩固窗口状态，避免残留目标
            // 参与反向二次确认判定。
            Reset();
            _lastSwitchTarget = -1;
            _lastSwitch = DateTime.MinValue;
            _stickyUntil = DateTime.MinValue;
            // 用第一个启用项目的 IntervalMs；没有就 800
            int interval = 800;
            AppConfig root = _rootProvider == null ? _config : _rootProvider();
            if (root != null && root.ImageProjects != null)
                foreach (var p in root.ImageProjects)
                    if (p != null && p.Enabled) { interval = p.IntervalMs; break; }
            _timer.Change(900, Math.Max(200, interval));
        }

        public void Stop()
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void Tick(object state)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            try
            {
                DoScan();
            }
            catch (Exception ex)
            {
                Report("自动模式识别异常: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        // 优先截取游戏窗口客户区（本地=游戏主窗口，串流=前台串流窗口）；
        // 取不到或矩形过小（窗口最小化/标题栏异常）时退回整屏兜底。
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

        private void DoScan()
        {
            if (_canScan == null || !_canScan()) return;
            AppConfig root = _rootProvider == null ? _config : _rootProvider();
            if (root == null || root.ImageProjects == null || root.Modes == null)
            {
                Reset();
                return;
            }

            // 粘性保持期：切换成功后短暂锁定，只覆盖界面过渡动画与单帧抖动，
            // 防止特征重叠帧闪回；不做长时间锁定，否则切到新模式后反向特征
            // 要等十几秒才能被识别。
            if (DateTime.UtcNow < _stickyUntil) return;

            // 动态刷新扫描间隔
            int curInterval = 400;
            foreach (var p in root.ImageProjects)
                if (p != null && p.Enabled) { curInterval = p.IntervalMs; break; }
            int expectedInterval = Math.Max(200, curInterval);
            if (expectedInterval != _cachedInterval)
            {
                _cachedInterval = expectedInterval;
                _timer.Change(expectedInterval, expectedInterval);
            }

            // 收集候选项目：已启用、目标模式 ≠ 当前模式、特征图文件存在。
            int currentMode = root.SelectedMode;
            List<int> scanCandidates = new List<int>();
            for (int p = 0; p < root.ImageProjects.Count; p++)
            {
                AutoImageProject project = root.ImageProjects[p];
                if (project == null || !project.Enabled) continue;
                int target = project.TargetMode;
                if (target < 0 || target >= root.Modes.Count || target == currentMode) continue;
                bool any = false;
                for (int i = 0; i < project.Features.Count && i < 3; i++)
                {
                    AutoFeatureConfig f = project.Features[i];
                    if (f == null || !f.Enabled || string.IsNullOrEmpty(f.ImageFile)) continue;
                    if (!File.Exists(ConfigStore.FeaturePath(p, i))) continue;
                    any = true;
                    break;
                }
                if (any) scanCandidates.Add(p);
            }
            if (scanCandidates.Count == 0)
            {
                Reset();
                return;
            }

            // 每 30 秒打一次心跳日志，确认扫描服务在跑、有候选项目
            if ((DateTime.UtcNow - _lastHeartbeat).TotalSeconds > 30)
            {
                _lastHeartbeat = DateTime.UtcNow;
                try
                {
                    ConfigStore.Log(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "[识图] 心跳 当前模式={0} 候选项目={1}个", currentMode + 1, scanCandidates.Count));
                }
                catch { }
            }

            Rectangle bounds = ResolveBounds();
            using (Bitmap screen = FeatureCaptureForm.CaptureScreen(bounds))
            {
                if (screen == null) return;
                FeatureFrame frame = FeatureMatcher.Prepare(screen);
                if (frame == null) return;

                // 双向互验：先复核当前模式对应的旧项目特征是否仍在画面上。
                // 旧界面元素仍命中时，说明画面并没有真正切换，本轮拒绝跨模式
                // 切换，防止两组特征在相似深色 UI 条上互相误配、两个模式来回闪。
                int oldProjectIndex = -1;
                for (int p = 0; p < root.ImageProjects.Count; p++)
                {
                    AutoImageProject q = root.ImageProjects[p];
                    if (q == null || !q.Enabled) continue;
                    if (q.TargetMode == currentMode) { oldProjectIndex = p; break; }
                }
                bool oldStillPresent = false;
                float oldScore = 0f;
                if (oldProjectIndex >= 0)
                {
                    string oldName;
                    oldStillPresent = EvaluateProject(oldProjectIndex,
                        root.ImageProjects[oldProjectIndex], frame, out oldName, out oldScore);
                }

                for (int c = 0; c < scanCandidates.Count; c++)
                {
                    int p = scanCandidates[c];
                    AutoImageProject project = root.ImageProjects[p];
                    string hitName;
                    float candidateScore;
                    if (!EvaluateProject(p, project, frame, out hitName, out candidateScore)) continue;

                    // 双向互验：候选必须明显胜出才认定画面真正切到新模式。
                    // ① 旧特征主导：旧分达到绝对线且不低于候选分 → 画面仍属旧模式，
                    //    拦截；旧分单独达到绝对线但已明显低于候选分属残留误配，不再
                    //    单独锁死切换。
                    // ② 相对领先线：候选分没有比旧分高出 LEAD_MARGIN 以上（两模式特征在
                    //    深色 UI 上分数交织、差距不足）→ 画面归属不明确，否决切换，防止
                    //    两个模式在过渡/混杂帧上每约 0.8 秒来回乒乓、覆盖层反复重建闪烁。
                    float oldLine = oldProjectIndex >= 0
                        ? Math.Max(0.68f, root.ImageProjects[oldProjectIndex].Threshold + 0.06f)
                        : 0.68f;
                    bool oldStrong = oldStillPresent && oldScore >= oldLine && oldScore >= candidateScore;
                    if (ShouldBlockSwitch(oldStillPresent, oldScore, candidateScore, oldLine))
                    {
                        string reason = oldStrong ? "旧模式特征仍在画面上" : "候选分未明显领先旧模式特征";
                        try
                        {
                            ConfigStore.Log(string.Format(
                                System.Globalization.CultureInfo.InvariantCulture,
                                "[识图] 互验拦截：{0}，本轮不切换（候选项目{1}，候选分={2:F3}，旧分={3:F3}）",
                                reason, p + 1, candidateScore, oldScore));
                        }
                        catch { }
                        Reset();
                        return;
                    }

                    int target = project.TargetMode;
                    int hitCount = Math.Max(1, Math.Min(5, project.HitCount));
                    int cooldown = Math.Max(500, project.CooldownMs);

                    // 切换后巩固窗口内的反向回切要求连续命中两次：过渡动画/加载混杂帧上
                    // 反向特征单帧误命中，不再把刚切好的模式立刻拉回去。
                    long sinceLast = (long)(DateTime.UtcNow - _lastSwitch).TotalMilliseconds;
                    bool consolidate = sinceLast < cooldown + 600;
                    bool isReverse = consolidate && _lastSwitchTarget >= 0 && target != _lastSwitchTarget;
                    int requiredHits = isReverse ? Math.Max(hitCount, 2) : hitCount;

                    if (_candidateMode == target) _candidateCount++;
                    else { _candidateMode = target; _candidateCount = 1; }
                    if (_candidateCount >= requiredHits && sinceLast > cooldown)
                    {
                        _lastSwitch = DateTime.UtcNow;
                        _lastSwitchTarget = target;
                        // 锁定期与 cooldown 合并为一道：只覆盖切换过渡动画与单帧抖动，
                        // 不再在 cooldown 之上额外叠加 1.5 秒，避免双向等待时间不对称拉长。
                        _stickyUntil = DateTime.UtcNow.AddMilliseconds(cooldown);
                        try
                        {
                            ConfigStore.Log(string.Format(
                                System.Globalization.CultureInfo.InvariantCulture,
                                "[识图] 已切换到模式{0}（项目{1} {2}，候选分={3:F3}，旧分={4:F3}）",
                                target + 1, p + 1, hitName, candidateScore, oldScore));
                        }
                        catch { }
                        Reset();
                        Action<int, string> h = _switchMode;
                        if (h != null) h(target, hitName);
                    }
                    return;
                }
                Reset();
            }
        }

        // 互验拦截判定（纯函数，便于 harness 用真机日志分数回代回归）：
        // ① 旧特征未命中 → 不拦截；
        // ② 旧特征主导画面：旧分达到绝对线且不低于候选分 → 仍属旧模式，拦截；
        //    （旧分单独达到绝对线、但已明显低于候选分属残留误配，不再单独锁死切换）；
        // ③ 归属不明：候选未比旧分高出 LEAD_MARGIN 以上 → 拦截，避免过渡混杂帧来回乒乓。
        internal static bool ShouldBlockSwitch(bool oldStillPresent, float oldScore, float candidateScore, float oldLine)
        {
            if (!oldStillPresent) return false;
            if (oldScore >= oldLine && oldScore >= candidateScore) return true;
            const float LEAD_MARGIN = 0.08f;
            return oldScore >= candidateScore - LEAD_MARGIN;
        }

        private bool EvaluateProject(int projectIndex, AutoImageProject project, FeatureFrame frame, out string hitName)
        {
            float bestScore;
            bool ok = EvaluateProject(projectIndex, project, frame, out hitName, out bestScore);
            return ok;
        }

        private bool EvaluateProject(int projectIndex, AutoImageProject project, FeatureFrame frame, out string hitName, out float bestScore)
        {
            hitName = null;
            bestScore = 0f;
            bool cross = project.CrossValidation;
            List<int> slots = new List<int>();
            List<AutoFeatureConfig> features = new List<AutoFeatureConfig>();
            for (int i = 0; i < project.Features.Count && i < 3; i++)
            {
                AutoFeatureConfig f = project.Features[i];
                if (f == null || !f.Enabled || string.IsNullOrEmpty(f.ImageFile)) continue;
                if (!File.Exists(ConfigStore.FeaturePath(projectIndex, i))) continue;
                features.Add(f);
                slots.Add(i);
            }
            if (features.Count == 0) return false;

            bool allHit = true;
            int hitCount = 0;
            int bestSlot = -1;
            for (int i = 0; i < features.Count; i++)
            {
                int slot = slots[i];
                AutoFeatureConfig f = features[i];
                Bitmap tpl = GetTemplate(projectIndex, slot);
                if (tpl == null)
                {
                    allHit = false;
                    if (cross) break;
                    continue;
                }
                MatchResult mr = FeatureMatcher.Match(frame, tpl,
                    f.CaptureWidth, f.CaptureHeight, f.AnchorX, f.AnchorY);
                float score = mr.Score;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestSlot = slot;
                }
                // 项目级阈值作为用户偏好；有效下限 0.62 是双路融合分的可信底线。
                // 颜色命中率 < 0.50 直接否决：形状相似但颜色不对（背景误认）不算命中。
                float threshold = project.Threshold <= 0f ? 0.85f : project.Threshold;
                float effectiveThr = Math.Max(threshold, 0.58f);
                bool colorOk = mr.ColorScore >= 0.50f;
                bool featurePassed = score >= effectiveThr && colorOk;
                // 诊断日志：得分 >= 0.3 即输出，便于现场对照颜色/结构两路分数调阈值。
                if (score >= 0.3f || featurePassed)
                {
                    try
                    {
                        string anchor = f.AnchorX >= 0f
                            ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "{0:F2}/{1:F2}", f.AnchorX, f.AnchorY)
                            : "-";
                        ConfigStore.Log(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "[识图] 项目{0} 特征{1} score={2:F3} color={3:F3} struct={4:F3} cons={5:F3} thr={6:F2} hit={7} pos=({8},{9}) {10}x{11} f={12} anchor={13} frame={14}x{15} tpl={16}x{17} cap={18}x{19}",
                            projectIndex + 1, slot + 1, score, mr.ColorScore, mr.StructScore, mr.Consistency,
                            threshold, featurePassed, mr.X, mr.Y, mr.W, mr.H, mr.FactorIndex, anchor,
                            frame.Width, frame.Height, tpl.Width, tpl.Height, f.CaptureWidth, f.CaptureHeight));
                    }
                    catch { }
                }
                if (!featurePassed)
                {
                    allHit = false;
                    if (cross) break;
                    continue;
                }
                hitCount++;
            }

            bool passed = cross ? (allHit && features.Count > 0) : hitCount > 0;
            if (!passed || bestSlot < 0) return false;

            string name = project.Name;
            if (string.IsNullOrEmpty(name)) name = "项目" + ModeConfig.ChineseNumber(projectIndex + 1);
            if (cross && features.Count > 1)
            {
                hitName = "「" + name + "」交叉命中 " + features.Count + " 张特征图";
            }
            else
            {
                hitName = "「" + name + "」特征" + ModeConfig.ChineseNumber(bestSlot + 1) + " (" +
                    Math.Round(bestScore * 100) + "%)";
            }
            return true;
        }

        private void Reset()
        {
            _candidateMode = -1;
            _candidateCount = 0;
        }

        private Bitmap GetTemplate(int projectIndex, int slot)
        {
            try
            {
                EnsureAssetsDir();
                Bitmap[] templates;
                DateTime[] stamps;
                if (!_templates.TryGetValue(projectIndex, out templates))
                {
                    templates = new Bitmap[3];
                    _templates[projectIndex] = templates;
                }
                if (!_templateTime.TryGetValue(projectIndex, out stamps))
                {
                    stamps = new DateTime[3];
                    _templateTime[projectIndex] = stamps;
                }
                string path = ConfigStore.FeaturePath(projectIndex, slot);
                if (!File.Exists(path)) return null;
                DateTime stamp = File.GetLastWriteTimeUtc(path);
                if (templates[slot] != null && stamps[slot] == stamp) return templates[slot];
                if (templates[slot] != null) templates[slot].Dispose();
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (Image source = Image.FromStream(fs))
                {
                    templates[slot] = new Bitmap(source);
                }
                stamps[slot] = stamp;
                return templates[slot];
            }
            catch
            {
                return null;
            }
        }

        // 切换游戏后资源目录变化，丢弃上一个游戏的模板缓存（文件时间戳可能相同）。
        private void EnsureAssetsDir()
        {
            string dir = ConfigStore.AssetsDir;
            if (string.Equals(_assetsDir, dir, StringComparison.OrdinalIgnoreCase)) return;
            _assetsDir = dir;
            foreach (KeyValuePair<int, Bitmap[]> pair in _templates)
            {
                Bitmap[] templates = pair.Value;
                if (templates == null) continue;
                for (int i = 0; i < templates.Length; i++)
                {
                    if (templates[i] != null) templates[i].Dispose();
                    templates[i] = null;
                }
            }
            _templates.Clear();
            _templateTime.Clear();
        }

        private void Report(string message)
        {
            try { ConfigStore.Log(message); } catch { }
            Action<string> h = _status;
            if (h != null) h(message);
        }

        public void Dispose()
        {
            try { _timer.Dispose(); } catch { }
            foreach (KeyValuePair<int, Bitmap[]> pair in _templates)
            {
                Bitmap[] templates = pair.Value;
                if (templates == null) continue;
                for (int i = 0; i < templates.Length; i++)
                {
                    if (templates[i] != null) templates[i].Dispose();
                    templates[i] = null;
                }
            }
            _templates.Clear();
            _templateTime.Clear();
            try { FeatureMatcher.ClearCache(); } catch { }
        }
    }
}
