using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MobileUiConfig
{
    // 自绘横屏人体工学手柄面板：按 PadCatalog 归一化坐标摆放动作键、一体十字键、肩键、扳机、
    // 系统键与摇杆下压键；双摇杆为装饰（摇杆参数由摇杆卡片负责，不从此处添加）。
    // _showLabels=true 时绘制分组汉字标注（整套布局弹窗）；单键绑定弹窗可关闭以保持简洁。
    internal sealed class PadBodyPanel : Panel
    {
        public event Action<PadSlot> SlotClicked;

        private int _layout;
        private KeyBinding _current;
        private List<PadSlot> _slots = new List<PadSlot>();
        private int _hover = -1;
        private bool _showLabels = true;

        public PadBodyPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            _layout = 0;
            ReloadSlots();
        }

        public void SetLayout(int layout)
        {
            _layout = layout < 0 ? 0 : (layout > 2 ? 2 : layout);
            ReloadSlots();
            Invalidate();
        }

        public void SetCurrentBinding(KeyBinding binding)
        {
            _current = binding;
            Invalidate();
        }

        public void SetShowLabels(bool show)
        {
            _showLabels = show;
            Invalidate();
        }

        private void ReloadSlots()
        {
            _slots = PadCatalog.GetSlots(_layout);
        }

        private RectangleF BodyRect()
        {
            float padX = Width * 0.03f;
            float padY = Height * 0.08f;
            return new RectangleF(padX, padY, Width - padX * 2f, Height - padY * 2f);
        }

        private PointF SlotCenter(PadSlot s, RectangleF body)
        {
            return new PointF(body.X + s.X * body.Width, body.Y + s.Y * body.Height);
        }

        private static bool IsAction(PadSlot s)
        {
            return s.Code == PadButtons.A || s.Code == PadButtons.B ||
                   s.Code == PadButtons.X || s.Code == PadButtons.Y;
        }

        private static bool IsDpad(PadSlot s)
        {
            return s.Code == PadButtons.DPadUp || s.Code == PadButtons.DPadDown ||
                   s.Code == PadButtons.DPadLeft || s.Code == PadButtons.DPadRight;
        }

        private static bool IsTrigger(PadSlot s)
        {
            return s.Modifiers == PadButtons.ModifierLeftTrigger ||
                   s.Modifiers == PadButtons.ModifierRightTrigger;
        }

        private static bool IsShoulder(PadSlot s)
        {
            return s.Code == PadButtons.LeftShoulder || s.Code == PadButtons.RightShoulder;
        }

        private static bool IsThumb(PadSlot s)
        {
            return s.Code == PadButtons.LeftThumb || s.Code == PadButtons.RightThumb;
        }

        private static bool IsSystem(PadSlot s)
        {
            return s.Code == PadButtons.Start || s.Code == PadButtons.Back;
        }

        // 动作键配色对照参考图：Xbox Y黄/X蓝/B红/A绿；Sony △绿/□紫/○红/×蓝；任天堂 X蓝/Y绿/A红/B黄。
        private Color ActionColor(PadSlot s)
        {
            if (_layout == (int)PadLayout.Sony)
            {
                if (s.Code == PadButtons.Y) return Theme.Mint;
                if (s.Code == PadButtons.X) return Theme.Violet;
                if (s.Code == PadButtons.B) return Theme.Danger;
                return Theme.Accent;
            }
            if (_layout == (int)PadLayout.Nintendo)
            {
                if (s.Code == PadButtons.X) return Theme.Accent;
                if (s.Code == PadButtons.Y) return Theme.Mint;
                if (s.Code == PadButtons.A) return Theme.Danger;
                return Theme.Gold;
            }
            if (s.Code == PadButtons.Y) return Theme.Gold;
            if (s.Code == PadButtons.X) return Theme.Accent;
            if (s.Code == PadButtons.B) return Theme.Danger;
            return Theme.Mint;
        }

        // 肩键 / 扳机描边：Xbox 蓝，索尼 / 任天堂紫。
        private Color ShoulderColor()
        {
            return _layout == (int)PadLayout.Xbox ? Theme.Accent : Theme.Violet;
        }

        private RectangleF SlotRect(PadSlot s, RectangleF body, out bool circle)
        {
            float w = body.Width;
            float h = body.Height;
            PointF c = SlotCenter(s, body);
            if (IsAction(s))
            {
                float r = w * 0.045f;
                circle = true;
                return new RectangleF(c.X - r, c.Y - r, r * 2f, r * 2f);
            }
            if (IsDpad(s))
            {
                // 方向键四向共享一体圆盘，命中区域在 HitTest 内按方向切分；此处返回中心圆仅用于悬停兜底。
                float r = w * 0.040f;
                circle = true;
                return new RectangleF(c.X - r, c.Y - r, r * 2f, r * 2f);
            }
            if (IsThumb(s))
            {
                circle = false;
                return new RectangleF(c.X - w * 0.040f, c.Y - h * 0.055f, w * 0.080f, h * 0.11f);
            }
            if (IsSystem(s))
            {
                circle = false;
                return new RectangleF(c.X - w * 0.046f, c.Y - h * 0.055f, w * 0.092f, h * 0.11f);
            }
            if (IsTrigger(s))
            {
                circle = false;
                return new RectangleF(c.X - w * 0.066f, c.Y - h * 0.045f, w * 0.132f, h * 0.09f);
            }
            if (IsShoulder(s))
            {
                circle = false;
                return new RectangleF(c.X - w * 0.066f, c.Y - h * 0.050f, w * 0.132f, h * 0.10f);
            }
            circle = true;
            float rr = w * 0.030f;
            return new RectangleF(c.X - rr, c.Y - rr, rr * 2f, rr * 2f);
        }

        // 一体方向键圆盘（参考图为圆形外轮廓，非方形底板）。
        private RectangleF DpadBoard(RectangleF body)
        {
            PointF c = new PointF(body.X + 0.35f * body.Width, body.Y + 0.58f * body.Height);
            float d = body.Width * 0.150f;
            return new RectangleF(c.X - d / 2f, c.Y - d / 2f, d, d);
        }

        private int HitTest(Point p)
        {
            RectangleF body = BodyRect();
            // 方向键优先：落在一体底板内按主方向切分四向。
            RectangleF board = DpadBoard(body);
            if (board.Contains(p))
            {
                float dx = p.X - (board.X + board.Width / 2f);
                float dy = p.Y - (board.Y + board.Height / 2f);
                int dir;
                if (Math.Abs(dx) > Math.Abs(dy)) dir = dx > 0 ? PadButtons.DPadRight : PadButtons.DPadLeft;
                else dir = dy > 0 ? PadButtons.DPadDown : PadButtons.DPadUp;
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (_slots[i].Code == dir) return i;
                }
            }

            for (int i = 0; i < _slots.Count; i++)
            {
                PadSlot s = _slots[i];
                if (IsDpad(s)) continue;
                RectangleF rect;
                bool circle;
                rect = SlotRect(s, body, out circle);
                if (circle)
                {
                    PointF c = SlotCenter(s, body);
                    float dx = p.X - c.X;
                    float dy = p.Y - c.Y;
                    if (dx * dx + dy * dy <= (rect.Width / 2f) * (rect.Width / 2f)) return i;
                }
                else if (rect.Contains(p))
                {
                    return i;
                }
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = HitTest(e.Location);
            if (idx != _hover)
            {
                _hover = idx;
                Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int idx = HitTest(e.Location);
            if (idx < 0) return;
            Action<PadSlot> h = SlotClicked;
            if (h != null) h(_slots[idx]);
        }

        // 装饰摇杆：左摇杆蓝色圆环（移动）、右摇杆绿色圆环（视角），不可点击。
        private void DrawDecorStick(Graphics g, RectangleF body, float nx, float ny, Color ring)
        {
            PointF c = new PointF(body.X + nx * body.Width, body.Y + ny * body.Height);
            float sr = body.Width * 0.046f;
            RectangleF srRect = new RectangleF(c.X - sr, c.Y - sr, sr * 2f, sr * 2f);
            using (SolidBrush sb = new SolidBrush(Theme.Bg))
            {
                g.FillEllipse(sb, srRect);
            }
            Theme.DrawCircle(g, srRect, ring, 2.4f);
            RectangleF inner = new RectangleF(srRect.X + sr * 0.30f, srRect.Y + sr * 0.30f,
                srRect.Width - sr * 0.60f, srRect.Height - sr * 0.60f);
            using (SolidBrush ib = new SolidBrush(Theme.SurfaceHi))
            {
                g.FillEllipse(ib, inner);
            }
            RectangleF inner2 = new RectangleF(inner.X + inner.Width * 0.18f, inner.Y + inner.Height * 0.18f,
                inner.Width - inner.Width * 0.36f, inner.Height - inner.Height * 0.36f);
            using (SolidBrush ib2 = new SolidBrush(Theme.BgSoft))
            {
                g.FillEllipse(ib2, inner2);
            }
        }

        // 一体方向键圆盘：圆形底板 + 中央十字凹槽 + 四向三角箭头，四向绑定为一个整体。
        private void DrawDpad(Graphics g, RectangleF body)
        {
            RectangleF board = DpadBoard(body);
            using (SolidBrush bb = new SolidBrush(Theme.SurfaceHi))
            {
                g.FillEllipse(bb, board);
            }
            Theme.DrawCircle(g, board, Theme.Stroke, 1.1f);

            PointF c = new PointF(board.X + board.Width / 2f, board.Y + board.Height / 2f);
            float armW = board.Width * 0.60f;
            float armH = board.Width * 0.20f;
            // 横臂 / 竖臂凹槽
            using (SolidBrush arm = new SolidBrush(Theme.BgSoft))
            {
                g.FillRectangle(arm, new RectangleF(c.X - armW / 2f, c.Y - armH / 2f, armW, armH));
                g.FillRectangle(arm, new RectangleF(c.X - armH / 2f, c.Y - armW / 2f, armH, armW));
            }

            // 四向小三角箭头（浅灰，嵌在十字臂端部内侧，不撑满圆盘）。
            Color arrow = Theme.TextSoft;
            float wid = board.Width * 0.085f;
            // 上三角
            using (SolidBrush ab = new SolidBrush(arrow))
            {
                PointF[] up = new PointF[]
                {
                    new PointF(c.X, board.Y + board.Height * 0.30f),
                    new PointF(c.X - wid / 2f, board.Y + board.Height * 0.44f),
                    new PointF(c.X + wid / 2f, board.Y + board.Height * 0.44f)
                };
                g.FillPolygon(ab, up);
                // 下三角
                PointF[] down = new PointF[]
                {
                    new PointF(c.X, board.Y + board.Height * 0.70f),
                    new PointF(c.X - wid / 2f, board.Y + board.Height * 0.56f),
                    new PointF(c.X + wid / 2f, board.Y + board.Height * 0.56f)
                };
                g.FillPolygon(ab, down);
                // 左三角
                PointF[] left = new PointF[]
                {
                    new PointF(board.X + board.Width * 0.30f, c.Y),
                    new PointF(board.X + board.Width * 0.44f, c.Y - wid / 2f),
                    new PointF(board.X + board.Width * 0.44f, c.Y + wid / 2f)
                };
                g.FillPolygon(ab, left);
                // 右三角
                PointF[] right = new PointF[]
                {
                    new PointF(board.X + board.Width * 0.70f, c.Y),
                    new PointF(board.X + board.Width * 0.56f, c.Y - wid / 2f),
                    new PointF(board.X + board.Width * 0.56f, c.Y + wid / 2f)
                };
                g.FillPolygon(ab, right);
            }
        }

        // 索尼动作键几何图形自绘：△ 实心三角、□ 实心方块、○ 圆环、× 交叉线。
        private void DrawSonyGlyph(Graphics g, PadSlot s, RectangleF rect, Color color)
        {
            float pad = rect.Width * 0.22f;
            RectangleF inner = new RectangleF(rect.X + pad, rect.Y + pad, rect.Width - pad * 2f, rect.Height - pad * 2f);
            using (SolidBrush fill = new SolidBrush(color))
            using (Pen pen = new Pen(color, Math.Max(1.6f, rect.Width * 0.08f)))
            {
                if (s.Code == PadButtons.Y) // △
                {
                    PointF[] tri = new PointF[]
                    {
                        new PointF(inner.X + inner.Width / 2f, inner.Y),
                        new PointF(inner.X, inner.Bottom),
                        new PointF(inner.Right, inner.Bottom)
                    };
                    g.FillPolygon(fill, tri);
                }
                else if (s.Code == PadButtons.X) // □
                {
                    float inset = inner.Width * 0.18f;
                    g.FillRectangle(fill, new RectangleF(inner.X + inset, inner.Y + inset,
                        inner.Width - inset * 2f, inner.Height - inset * 2f));
                }
                else if (s.Code == PadButtons.B) // ○（空心圆环）
                {
                    g.DrawEllipse(pen, inner);
                }
                else // A → ×
                {
                    g.DrawLine(pen, inner.X, inner.Y, inner.Right, inner.Bottom);
                    g.DrawLine(pen, inner.Right, inner.Y, inner.X, inner.Bottom);
                }
            }
        }

        // 系统键图标自绘：视图键（双方框）、菜单/选项键（三横线）、−、+、PS 官方标志。
        private void DrawSystemIcon(Graphics g, PadSlot s, RectangleF rect, Color color)
        {
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;
            float u = rect.Height;
            using (Pen pen = new Pen(color, Math.Max(1.3f, u * 0.07f)))
            using (SolidBrush brush = new SolidBrush(color))
            {
                bool menu = s.Code == PadButtons.Start;
                if (_layout == (int)PadLayout.Xbox)
                {
                    if (menu)
                    {
                        // 菜单键：三条横线
                        for (int i = -1; i <= 1; i++)
                        {
                            float y = cy + i * u * 0.18f;
                            g.DrawLine(pen, cx - u * 0.30f, y, cx + u * 0.30f, y);
                        }
                    }
                    else
                    {
                        // 视图键：两个错位方框（前实后空）
                        RectangleF back = new RectangleF(cx - u * 0.24f, cy - u * 0.22f, u * 0.30f, u * 0.30f);
                        RectangleF front = new RectangleF(cx + u * 0.02f, cy - u * 0.02f, u * 0.30f, u * 0.30f);
                        g.DrawRectangle(pen, back.X, back.Y, back.Width, back.Height);
                        g.FillRectangle(brush, front);
                    }
                }
                else if (_layout == (int)PadLayout.Nintendo)
                {
                    float len = u * 0.30f;
                    g.DrawLine(pen, cx - len, cy, cx + len, cy);
                    if (menu) g.DrawLine(pen, cx, cy - len, cx, cy + len);
                }
                else // 索尼
                {
                    if (menu)
                    {
                        // 选项键：三条横线
                        for (int i = -1; i <= 1; i++)
                        {
                            float y = cy + i * u * 0.18f;
                            g.DrawLine(pen, cx - u * 0.30f, y, cx + u * 0.30f, y);
                        }
                    }
                    else
                    {
                        // PS 键：官方 PS 标志（几何自绘，非四色符号堆叠）。
                        float logoSide = u * 0.62f;
                        Theme.DrawPlayStationLogo(g,
                            new RectangleF(cx - logoSide / 2f, cy - logoSide / 2f, logoSide, logoSide), color);
                    }
                }
            }
        }

        private void DrawGroupLabel(Graphics g, RectangleF body, float nx, float ny,
            string text, Color color, float size)
        {
            if (!_showLabels) return;
            PointF c = new PointF(body.X + nx * body.Width, body.Y + ny * body.Height);
            float w = body.Width * 0.22f;
            RectangleF rect = new RectangleF(c.X - w / 2f, c.Y, w, body.Height * 0.08f);
            Theme.DrawText(g, text, Theme.UiFont(size, false), color, rect,
                StringAlignment.Center, StringAlignment.Center, true);
        }

        private void DrawSystemName(Graphics g, RectangleF body, float nx, float ny, string text)
        {
            if (!_showLabels) return;
            PointF c = new PointF(body.X + nx * body.Width, body.Y + ny * body.Height);
            float w = body.Width * 0.16f;
            RectangleF rect = new RectangleF(c.X - w / 2f, c.Y, w, body.Height * 0.07f);
            Theme.DrawText(g, text, Theme.UiFont(7.6f, false), Theme.Muted, rect,
                StringAlignment.Center, StringAlignment.Center, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF body = BodyRect();

            // 机身底板（横长圆角手机轮廓）
            using (SolidBrush bodyBrush = new SolidBrush(Theme.SurfaceAlt))
            {
                Theme.FillRounded(g, body, body.Height * 0.18f, bodyBrush);
            }
            Theme.DrawRounded(g, body, body.Height * 0.18f, Theme.Stroke, 1f);

            // 装饰双摇杆（不可点击）：左摇杆左下、右摇杆右中，与 LS/RS、动作键保持间隙。
            DrawDecorStick(g, body, 0.17f, 0.58f, Theme.Accent);
            DrawDecorStick(g, body, 0.61f, 0.58f, Theme.Mint);

            // 一体方向键底板（先画在按键层之前，作为背景底板）
            DrawDpad(g, body);

            Color shoulder = ShoulderColor();

            for (int i = 0; i < _slots.Count; i++)
            {
                PadSlot s = _slots[i];
                if (IsDpad(s)) continue; // 方向键由一体底板统一绘制，不再逐向画圆。
                RectangleF rect;
                bool circle;
                rect = SlotRect(s, body, out circle);

                bool selected = s.Matches(_current);
                bool hovered = i == _hover;
                Color baseColor;
                Color ringColor;

                if (IsAction(s))
                {
                    Color ac = ActionColor(s);
                    baseColor = Theme.Blend(Color.FromArgb(26, 32, 46), ac, hovered ? 0.34f : 0.18f);
                    ringColor = ac;
                }
                else if (IsTrigger(s))
                {
                    baseColor = Theme.SurfaceAlt;
                    ringColor = shoulder;
                }
                else if (IsShoulder(s))
                {
                    baseColor = Theme.SurfaceHi;
                    ringColor = shoulder;
                }
                else if (IsSystem(s))
                {
                    baseColor = Theme.SurfaceAlt;
                    ringColor = _layout == (int)PadLayout.Xbox ? Theme.Accent : Theme.Muted;
                }
                else if (IsThumb(s))
                {
                    baseColor = Theme.SurfaceHi;
                    ringColor = Theme.Stroke;
                }
                else
                {
                    baseColor = Theme.SurfaceHi;
                    ringColor = Theme.Stroke;
                }

                if (circle)
                {
                    using (SolidBrush b = new SolidBrush(baseColor)) g.FillEllipse(b, rect);
                    Theme.DrawCircle(g, rect, selected || hovered ? Theme.Accent : ringColor, selected ? 2.2f : 1.3f);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(baseColor))
                    {
                        Theme.FillRounded(g, rect, rect.Height * 0.32f, b);
                    }
                    Theme.DrawRounded(g, rect, rect.Height * 0.32f,
                        selected || hovered ? Theme.Accent : ringColor, selected ? 2f : 1.2f);
                }

                // 图形 / 文字内容
                if (IsAction(s))
                {
                    if (_layout == (int)PadLayout.Sony)
                    {
                        DrawSonyGlyph(g, s, rect, ActionColor(s));
                    }
                    else
                    {
                        string letter = PadButtons.VendorSymbol(_layout, s.Code);
                        Font font = Theme.FitFont(g, letter, 11f, true, rect.Width * 0.72f, 6f);
                        Theme.DrawText(g, letter, font, ActionColor(s), rect,
                            StringAlignment.Center, StringAlignment.Center, true);
                    }
                }
                else if (IsSystem(s))
                {
                    DrawSystemIcon(g, s, rect, selected || hovered ? Theme.Text : ringColor);
                }
                else if (IsThumb(s))
                {
                    string label = s.Symbol(_layout);
                    Font font = Theme.FitFont(g, label, 8.2f, true, rect.Width * 0.80f, 5.2f);
                    Theme.DrawText(g, label, font, Theme.TextSoft, rect,
                        StringAlignment.Center, StringAlignment.Center, true);
                }
                else if (!IsDpad(s))
                {
                    string label = s.Symbol(_layout);
                    Font font = Theme.FitFont(g, label, 9.2f, true, rect.Width * 0.74f, 5.2f);
                    Theme.DrawText(g, label, font, Theme.TextSoft, rect,
                        StringAlignment.Center, StringAlignment.Center, true);
                }
            }

            // 分组汉字标注（仅整套布局弹窗显示；加入手机的按键不带这些汉字）
            if (_showLabels)
            {
                DrawGroupLabel(g, body, 0.17f, 0.88f, "左摇杆", Theme.Accent, 8.4f);
                DrawGroupLabel(g, body, 0.35f, 0.88f, "方向键", Theme.Muted, 8.4f);
                DrawGroupLabel(g, body, 0.60f, 0.88f, "右摇杆", Theme.Mint, 8.4f);
                string actionLabel = _layout == (int)PadLayout.Sony ? "动作按键" : "ABXY 按键";
                DrawGroupLabel(g, body, 0.81f, 0.88f, actionLabel, Theme.Gold, 8.4f);
                DrawGroupLabel(g, body, 0.47f, 0.76f, "左摇杆按键", Theme.Muted, 7.4f);
                DrawGroupLabel(g, body, 0.55f, 0.76f, "右摇杆按键", Theme.Muted, 7.4f);

                // 系统键下方小字名称
                if (_layout == (int)PadLayout.Xbox)
                {
                    DrawSystemName(g, body, 0.43f, 0.34f, "视图键");
                    DrawSystemName(g, body, 0.53f, 0.34f, "菜单键");
                }
                else if (_layout == (int)PadLayout.Nintendo)
                {
                    DrawSystemName(g, body, 0.43f, 0.34f, "减号键");
                    DrawSystemName(g, body, 0.53f, 0.34f, "加号键");
                }
                else
                {
                    DrawSystemName(g, body, 0.43f, 0.34f, "PS 键");
                    DrawSystemName(g, body, 0.53f, 0.34f, "选项键");
                }
            }
        }
    }
}
