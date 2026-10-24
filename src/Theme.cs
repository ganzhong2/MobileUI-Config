using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MobileUiConfig
{
    public static class Theme
    {
        public static readonly float Scale = DetectScale();

        public static readonly Color Bg = Color.FromArgb(9, 13, 21);
        public static readonly Color BgSoft = Color.FromArgb(12, 18, 28);
        public static readonly Color Surface = Color.FromArgb(17, 24, 37);
        public static readonly Color SurfaceAlt = Color.FromArgb(22, 31, 47);
        public static readonly Color SurfaceHi = Color.FromArgb(29, 41, 61);
        public static readonly Color SurfaceHover = Color.FromArgb(36, 50, 74);
        public static readonly Color Stroke = Color.FromArgb(43, 57, 80);
        public static readonly Color StrokeSoft = Color.FromArgb(31, 42, 60);
        public static readonly Color Text = Color.FromArgb(238, 244, 255);
        public static readonly Color TextSoft = Color.FromArgb(183, 196, 219);
        public static readonly Color Muted = Color.FromArgb(126, 143, 169);
        public static readonly Color Faint = Color.FromArgb(86, 101, 123);
        public static readonly Color Accent = Color.FromArgb(82, 139, 255);
        public static readonly Color AccentDeep = Color.FromArgb(49, 96, 222);
        public static readonly Color Mint = Color.FromArgb(69, 215, 191);
        public static readonly Color MintDeep = Color.FromArgb(31, 155, 142);
        public static readonly Color Gold = Color.FromArgb(255, 190, 105);
        public static readonly Color Danger = Color.FromArgb(255, 104, 122);
        public static readonly Color DangerDeep = Color.FromArgb(202, 63, 84);
        public static readonly Color Violet = Color.FromArgb(172, 132, 255);
        public static readonly Color Pink = Color.FromArgb(255, 133, 179);

        public static readonly Color[] KeyPalette = new Color[]
        {
            Color.FromArgb(82, 139, 255),
            Color.FromArgb(69, 215, 191),
            Color.FromArgb(255, 190, 105),
            Color.FromArgb(172, 132, 255),
            Color.FromArgb(255, 133, 179),
            Color.FromArgb(112, 205, 255)
        };

        private static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();

        private static float DetectScale()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    float scale = g.DpiX / 96f;
                    if (scale < 1f) scale = 1f;
                    if (scale > 3f) scale = 3f;
                    return scale;
                }
            }
            catch
            {
                return 1f;
            }
        }

        public static int S(float value)
        {
            return (int)Math.Round(value * Scale);
        }

        public static float SF(float value)
        {
            return value * Scale;
        }

        public static Font UiFont(float size, bool bold)
        {
            return GetFont("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
        }

        public static Font UiFont(float size)
        {
            return UiFont(size, false);
        }

        public static Font Semibold(float size)
        {
            return GetFont("Microsoft YaHei UI", size, FontStyle.Bold);
        }

        public static Font FitFont(Graphics g, string text, float size, bool bold, float maxWidth, float minSize)
        {
            float pt = size;
            while (pt > minSize)
            {
                Font candidate = GetFont("Microsoft YaHei UI", pt, bold ? FontStyle.Bold : FontStyle.Regular);
                if (g.MeasureString(text == null ? "" : text, candidate).Width <= maxWidth) return candidate;
                pt -= 0.25f;
            }
            return GetFont("Microsoft YaHei UI", minSize, bold ? FontStyle.Bold : FontStyle.Regular);
        }

        public static Font Mono(float size)
        {
            return GetFont("Consolas", size, FontStyle.Regular);
        }

        private static Font GetFont(string family, float size, FontStyle style)
        {
            string key = family + "|" + size.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "|" + style;
            Font f;
            if (FontCache.TryGetValue(key, out f)) return f;
            try
            {
                f = new Font(family, size, style, GraphicsUnit.Point);
            }
            catch
            {
                f = new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
            }
            FontCache[key] = f;
            return f;
        }

        public static Color AccentFor(int index)
        {
            if (index < 0) index = 0;
            return KeyPalette[index % KeyPalette.Length];
        }

        public static Color Blend(Color a, Color b, float amount)
        {
            if (amount < 0f) amount = 0f;
            if (amount > 1f) amount = 1f;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * amount),
                (int)(a.R + (b.R - a.R) * amount),
                (int)(a.G + (b.G - a.G) * amount),
                (int)(a.B + (b.B - a.B) * amount));
        }

        public static Color Alpha(Color color, int alpha)
        {
            if (alpha < 0) alpha = 0;
            if (alpha > 255) alpha = 255;
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        public static GraphicsPath RoundedPath(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (rect.Width <= 0f || rect.Height <= 0f)
            {
                return path;
            }
            float diameter = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
            if (diameter <= 1f)
            {
                path.AddRectangle(rect);
                return path;
            }
            RectangleF arc = new RectangleF(rect.X, rect.Y, diameter, diameter);
            path.AddArc(arc, 180f, 90f);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270f, 90f);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0f, 90f);
            arc.X = rect.X;
            path.AddArc(arc, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, RectangleF rect, float radius, Brush brush)
        {
            using (GraphicsPath path = RoundedPath(rect, radius))
            {
                g.FillPath(brush, path);
            }
        }

        public static void FillRounded(Graphics g, RectangleF rect, float radius, Color color)
        {
            using (SolidBrush brush = new SolidBrush(color))
            {
                FillRounded(g, rect, radius, brush);
            }
        }

        public static void DrawRounded(Graphics g, RectangleF rect, float radius, Color color, float width)
        {
            using (GraphicsPath path = RoundedPath(rect, radius))
            using (Pen pen = new Pen(color, width))
            {
                g.DrawPath(pen, path);
            }
        }

        public static void FillCircle(Graphics g, RectangleF rect, Color color)
        {
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillEllipse(brush, rect);
            }
        }

        public static void DrawCircle(Graphics g, RectangleF rect, Color color, float width)
        {
            using (Pen pen = new Pen(color, width))
            {
                g.DrawEllipse(pen, rect);
            }
        }

        // 官方 PlayStation 标志几何自绘：P 竖笔 + 上肚，S 双弧环绕，底部横条；不依赖外部图片与 Unicode 符号。
        public static void DrawPlayStationLogo(Graphics g, RectangleF box, Color color)
        {
            if (g == null) return;
            float side = Math.Min(box.Width, box.Height);
            if (side <= 1f) return;
            float ox = box.X + (box.Width - side) / 2f;
            float oy = box.Y + (box.Height - side) / 2f;
            Func<float, float> px = delegate(float nx) { return ox + nx * side; };
            Func<float, float> py = delegate(float ny) { return oy + ny * side; };

            using (SolidBrush b = new SolidBrush(color))
            using (Pen pen = new Pen(color, side * 0.13f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                // P 竖笔（贯穿到底，被底横条压住下端）。
                g.FillRectangle(b, px(0.26f), py(0.06f), side * 0.12f, side * 0.84f);
                // P 上肚：右侧圆头闭合。
                RectangleF belly = new RectangleF(px(0.26f), py(0.06f), side * 0.32f, side * 0.40f);
                FillRounded(g, belly, side * 0.16f, b);

                // S 上弧：从 P 右上方向右弯再回中。
                g.DrawBezier(pen, px(0.38f), py(0.18f),
                    px(0.80f), py(0.10f), px(0.84f), py(0.34f), px(0.58f), py(0.48f));
                // S 下弧：从中部向左绕回再向右下收。
                g.DrawBezier(pen, px(0.52f), py(0.52f),
                    px(0.22f), py(0.60f), px(0.22f), py(0.80f), px(0.64f), py(0.86f));

                // 底部水平横条，贯穿 P 与 S 底部。
                g.FillRectangle(b, px(0.26f), py(0.88f), side * 0.54f, side * 0.07f);
            }
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, RectangleF rect,
            StringAlignment align, StringAlignment lineAlign, bool ellipsis)
        {
            if (g == null || font == null) return;
            if (float.IsNaN(rect.Width) || float.IsNaN(rect.Height) ||
                float.IsInfinity(rect.Width) || float.IsInfinity(rect.Height)) return;
            if (rect.Width <= 0.5f || rect.Height <= 0.5f) return;
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = align;
                sf.LineAlignment = lineAlign;
                sf.Trimming = ellipsis ? StringTrimming.EllipsisCharacter : StringTrimming.None;
                sf.FormatFlags = StringFormatFlags.NoWrap;
                using (SolidBrush brush = new SolidBrush(color))
                {
                    g.DrawString(text == null ? "" : text, font, brush, rect, sf);
                }
            }
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, RectangleF rect, StringAlignment align, bool ellipsis)
        {
            DrawText(g, text, font, color, rect, align, StringAlignment.Center, ellipsis);
        }

        public static string Elide(Graphics g, string text, Font font, float maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (g.MeasureString(text, font).Width <= maxWidth) return text;
            string ellipsis = "…";
            int length = text.Length;
            while (length > 0)
            {
                string candidate = text.Substring(0, length) + ellipsis;
                if (g.MeasureString(candidate, font).Width <= maxWidth) return candidate;
                length--;
            }
            return ellipsis;
        }

        public static void SetupGraphics(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static void DrawImageContained(Graphics g, Image image, RectangleF bounds)
        {
            DrawImageContained(g, image, bounds, 1f);
        }

        public static void DrawImageContained(Graphics g, Image image, RectangleF bounds, float opacity)
        {
            if (image == null) return;
            float scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
            float w = image.Width * scale;
            float h = image.Height * scale;
            RectangleF target = new RectangleF(bounds.X + (bounds.Width - w) / 2f,
                bounds.Y + (bounds.Height - h) / 2f, w, h);
            if (opacity >= 0.995f)
            {
                g.DrawImage(image, target);
                return;
            }
            if (opacity < 0f) opacity = 0f;
            ColorMatrix matrix = new ColorMatrix();
            matrix.Matrix33 = opacity;
            using (ImageAttributes attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(matrix);
                g.DrawImage(image, Rectangle.Round(target), 0, 0, image.Width, image.Height,
                    GraphicsUnit.Pixel, attributes);
            }
        }

        public static void DrawDashedRounded(Graphics g, RectangleF rect, float radius, Color color, float width, float dash)
        {
            using (GraphicsPath path = RoundedPath(rect, radius))
            using (Pen pen = new Pen(color, width))
            {
                pen.DashStyle = DashStyle.Custom;
                pen.DashPattern = new float[] { dash, dash };
                g.DrawPath(pen, path);
            }
        }

        public static string ShortTime()
        {
            return DateTime.Now.ToString("HH:mm");
        }
    }

    public enum ModernButtonStyle
    {
        Primary,
        Secondary,
        Ghost,
        Soft,
        Danger,
        Link
    }

    public class ModernButton : Control
    {
        private bool _hover;
        private bool _pressed;
        private ModernButtonStyle _style = ModernButtonStyle.Secondary;
        private string _icon = "";
        private int _radius = 10;
        private DateTime? _lastClickTick;

        public ModernButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            Font = Theme.UiFont(9.2f, false);
            Size = new Size(Theme.S(96), Theme.S(38));
            TabStop = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public ModernButtonStyle Style
        {
            get { return _style; }
            set { _style = value; Invalidate(); }
        }

        public string Icon
        {
            get { return _icon; }
            set { _icon = value == null ? "" : value; Invalidate(); }
        }

        public int Radius
        {
            get { return _radius; }
            set { _radius = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float radius = _radius * Theme.Scale;
            Color fill;
            Color border;
            Color textColor;
            bool drawBorder = true;
            bool gradient = false;

            switch (_style)
            {
                case ModernButtonStyle.Primary:
                    fill = _hover ? Theme.Blend(Theme.Accent, Color.White, 0.10f) : Theme.Accent;
                    border = _hover ? Theme.Blend(Theme.Accent, Color.White, 0.28f) : Theme.Blend(Theme.Accent, Color.White, 0.14f);
                    textColor = Color.White;
                    gradient = true;
                    break;
                case ModernButtonStyle.Soft:
                    fill = _hover ? Theme.SurfaceHover : Theme.Alpha(Theme.Accent, 30);
                    border = Theme.Alpha(Theme.Accent, _hover ? 110 : 70);
                    textColor = Theme.Blend(Theme.Accent, Color.White, 0.55f);
                    break;
                case ModernButtonStyle.Ghost:
                    fill = _hover ? Theme.Alpha(Theme.SurfaceHi, 180) : Color.Transparent;
                    border = _hover ? Theme.Stroke : Color.Transparent;
                    textColor = Theme.TextSoft;
                    drawBorder = _hover;
                    break;
                case ModernButtonStyle.Link:
                    fill = _hover ? Theme.Alpha(Theme.SurfaceHi, 150) : Color.Transparent;
                    border = Color.Transparent;
                    textColor = _hover ? Theme.Text : Theme.Muted;
                    drawBorder = false;
                    break;
                case ModernButtonStyle.Danger:
                    fill = _hover ? Theme.DangerDeep : Theme.Alpha(Theme.Danger, 24);
                    border = Theme.Alpha(Theme.Danger, _hover ? 190 : 90);
                    textColor = _hover ? Color.White : Theme.Danger;
                    break;
                default:
                    fill = _hover ? Theme.SurfaceHover : Theme.SurfaceHi;
                    border = _hover ? Theme.Stroke : Theme.Stroke;
                    textColor = Theme.TextSoft;
                    break;
            }

            if (_pressed)
            {
                fill = Theme.Blend(fill, Color.Black, 0.16f);
            }

            if (fill.A > 0)
            {
                if (gradient)
                {
                    using (LinearGradientBrush brush = new LinearGradientBrush(rect,
                        Theme.Blend(fill, Color.White, 0.10f), Theme.Blend(fill, Color.Black, 0.08f), 90f))
                    {
                        Theme.FillRounded(e.Graphics, rect, radius, brush);
                    }
                }
                else
                {
                    Theme.FillRounded(e.Graphics, rect, radius, fill);
                }
            }
            if (drawBorder)
            {
                Theme.DrawRounded(e.Graphics, rect, radius, border, 1f);
            }

            if (!Enabled)
            {
                textColor = Theme.Faint;
            }

            Font f = Font;
            string caption = Text;
            if (_icon.Length > 0)
            {
                Font iconFont = Theme.UiFont(Font.SizeInPoints * 0.92f, false);
                SizeF iconSize = e.Graphics.MeasureString(_icon, iconFont);
                caption = "  " + Text;
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    sf.FormatFlags = StringFormatFlags.NoWrap;
                    SizeF total = e.Graphics.MeasureString(_icon + caption, iconFont);
                    float left = (Width - total.Width) / 2f + 2f;
                    using (SolidBrush b = new SolidBrush(textColor))
                    {
                        e.Graphics.DrawString(_icon, iconFont, b,
                            new RectangleF(left, 0, iconSize.Width + 2f, Height), sf);
                        sf.Alignment = StringAlignment.Near;
                        e.Graphics.DrawString(Text, Font, b,
                            new RectangleF(left + iconSize.Width + 3f, 0, Width - left, Height), sf);
                    }
                }
                return;
            }

            Theme.DrawText(e.Graphics, caption, f, textColor,
                new RectangleF(6, 0, Width - 12, Height), StringAlignment.Center, StringAlignment.Center, true);

            if (_pressed)
            {
                Theme.DrawRounded(e.Graphics, new RectangleF(2f, 2f, Width - 4f, Height - 4f),
                    Math.Max(4f, radius - 2f), Color.FromArgb(80, 255, 255, 255), 1.5f);
            }

            if (Focused && TabStop)
            {
                Theme.DrawDashedRounded(e.Graphics, new RectangleF(3, 3, Width - 6, Height - 6), Math.Max(4, radius - 3),
                    Theme.Alpha(textColor, 90), 1f, 3f);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Focus();
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                bool wasPressed = _pressed;
                _pressed = false;
                Invalidate();
                if (wasPressed && ClientRectangle.Contains(e.Location))
                {
                    OnClick(EventArgs.Empty);
                }
            }
            base.OnMouseUp(e);
        }

        protected override void OnClick(EventArgs e)
        {
            // OnMouseUp 手动触发一次，base.OnMouseUp 走完框架还会再 raise 一次
            // Click；250ms 内吞掉第二次，杜绝单击被执行两遍（一次添加出两个项目）。
            if (_lastClickTick != null &&
                (DateTime.Now - _lastClickTick.Value).TotalMilliseconds < 250) return;
            _lastClickTick = DateTime.Now;
            base.OnClick(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Enter || keyData == Keys.Space) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                e.SuppressKeyPress = true;
                OnClick(EventArgs.Empty);
            }
            base.OnKeyDown(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    public class CardPanel : Panel
    {
        private int _radius = 16;
        private Color _borderColor = Theme.StrokeSoft;
        private bool _drawBorder = true;

        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Surface;
            DoubleBuffered = true;
        }

        public int Radius
        {
            get { return _radius; }
            set { _radius = value; Invalidate(); ApplyRegion(); }
        }

        public Color BorderColor
        {
            get { return _borderColor; }
            set { _borderColor = value; Invalidate(); }
        }

        public bool DrawBorder
        {
            get { return _drawBorder; }
            set { _drawBorder = value; Invalidate(); }
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            ApplyRegion();
        }

        private void ApplyRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = Theme.RoundedPath(new RectangleF(0, 0, Width, Height), _radius * Theme.Scale))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float radius = _radius * Theme.Scale;
            using (SolidBrush brush = new SolidBrush(BackColor))
            {
                Theme.FillRounded(e.Graphics, rect, radius, brush);
            }
            if (_drawBorder)
            {
                Theme.DrawRounded(e.Graphics, rect, radius, _borderColor, 1f);
            }
            base.OnPaint(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Painted in OnPaint to keep the rounded silhouette crisp.
        }
    }

    public class SectionPanel : Panel
    {
        private string _title = "";
        private string _hint = "";

        public SectionPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.SurfaceAlt;
            Font = Theme.UiFont(9f, false);
        }

        public string Title
        {
            get { return _title; }
            set { _title = value == null ? "" : value; Invalidate(); }
        }

        public string Hint
        {
            get { return _hint; }
            set { _hint = value == null ? "" : value; Invalidate(); }
        }

        // 标题 / 说明文字左缩进（缩放基准）；左侧让位给最左的拖拽把手。
        public float TitleLeftPad = 16f;

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            float s = Theme.Scale;
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRounded(e.Graphics, rect, 14f * s, BackColor);
            Theme.DrawRounded(e.Graphics, rect, 14f * s, Theme.StrokeSoft, 1f);
            bool collapsed = Height < Theme.S(48);
            if (!string.IsNullOrEmpty(_title))
            {
                float titleY = collapsed ? 6f * s : 12f * s;
                Theme.DrawText(e.Graphics, _title, Theme.Semibold(9.6f), Theme.Text,
                    new RectangleF(TitleLeftPad * s, titleY, Width - (TitleLeftPad + 16) * s, 24 * s),
                    StringAlignment.Near, StringAlignment.Center, true);
            }
            if (!collapsed && !string.IsNullOrEmpty(_hint))
            {
                Theme.DrawText(e.Graphics, _hint, Theme.UiFont(7.8f, false), Theme.Muted,
                    new RectangleF(TitleLeftPad * s, 32 * s, Width - (TitleLeftPad + 16) * s, 18 * s),
                    StringAlignment.Near, StringAlignment.Center, true);
            }
            base.OnPaint(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }
    }
}
