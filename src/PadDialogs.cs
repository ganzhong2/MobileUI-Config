using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MobileUiConfig
{
    // 单键选择弹窗：在当前厂商人体工学布局上挑选一个手柄按键作为绑定；可清空为未绑定。
    internal sealed class PadBindingPickerDialog : Form
    {
        private readonly int _layout;
        private readonly KeyBinding _current;
        private KeyBinding _result;
        private PadBodyPanel _body;
        private ModernButton _noneButton;

        private PadBindingPickerDialog(int layout, KeyBinding current)
        {
            _layout = layout;
            _current = current == null ? new KeyBinding() : current;
            _result = null;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(9.4f, false);
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(500), Theme.S(486));
            DoubleBuffered = true;
            KeyPreview = true;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;

            _noneButton = new ModernButton();
            _noneButton.Text = "未绑定";
            _noneButton.Style = ModernButtonStyle.Secondary;
            _noneButton.Radius = 9;
            _noneButton.Click += delegate
            {
                _result = new KeyBinding();
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(_noneButton);

            _body = new PadBodyPanel();
            _body.SetLayout(_layout);
            _body.SetCurrentBinding(_current);
            _body.SetShowLabels(false);
            _body.SlotClicked += delegate(PadSlot slot)
            {
                _result = slot.ToBinding();
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(_body);

            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };
            MouseDown += DialogOnMouseDown;
            Resize += delegate { LayoutBody(); };
            Shown += delegate { LayoutBody(); };
        }

        public static KeyBinding Show(IWin32Window owner, int layout, KeyBinding current)
        {
            using (PadBindingPickerDialog dlg = new PadBindingPickerDialog(layout, current))
            {
                if (dlg.ShowDialog(owner) == DialogResult.OK) return dlg._result;
            }
            return null;
        }

        private void LayoutBody()
        {
            float s = Theme.Scale;
            int pad = Theme.S(22);
            int bodyTop = Theme.S(92);
            int bodyH = ClientSize.Height - bodyTop - Theme.S(64);
            _body.SetBounds(pad, bodyTop, ClientSize.Width - pad * 2, bodyH);

            int btnW = Theme.S(140);
            int btnH = Theme.S(38);
            int bottomY = ClientSize.Height - Theme.S(16) - btnH;
            _noneButton.SetBounds(pad, bottomY, btnW, btnH);
        }

        private void DialogOnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y < Theme.S(56))
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, new IntPtr(NativeMethods.HTCAPTION), IntPtr.Zero);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            float s = Theme.Scale;
            RectangleF rect = new RectangleF(0.5f, 0.5f, ClientSize.Width - 1f, ClientSize.Height - 1f);
            using (SolidBrush brush = new SolidBrush(Theme.Surface))
            {
                Theme.FillRounded(e.Graphics, rect, 14f * s, brush);
            }
            Theme.DrawRounded(e.Graphics, rect, 14f * s, Theme.Stroke, 1f);
            Theme.FillRounded(e.Graphics, new RectangleF(22 * s, 22 * s, 4 * s, 24 * s), 2f * s, Theme.Accent);
            Theme.DrawText(e.Graphics, "选择手柄按键", Theme.Semibold(13f), Theme.Text,
                new RectangleF(38 * s, 16 * s, ClientSize.Width - 70 * s, 34 * s), StringAlignment.Near, StringAlignment.Center, true);
            Theme.DrawText(e.Graphics, "点击一个按键完成绑定，选择「未绑定」可清空当前绑定。", Theme.UiFont(8.8f, false), Theme.Muted,
                new RectangleF(22 * s, 54 * s, ClientSize.Width - 44 * s, 20 * s), StringAlignment.Near, StringAlignment.Center, true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int preference = 2;
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, 4);
            }
            catch { }
        }
    }

    // 新增手柄布局弹窗：长方形三栏（左品牌标识 / 中人体工学手柄轮廓 / 右按键图例说明），
    // 三厂商互斥切换、单键添加、整套添加、清空当前布局。
    internal sealed class PadLayoutPickerDialog : Form
    {
        public event Action<PadSlot> SlotClicked;
        public event Action ClearClicked;
        public event Action<int> PresetClicked;

        private int _layout;
        private readonly List<ModernButton> _vendorButtons = new List<ModernButton>();
        private PadBodyPanel _body;
        private ModernButton _clearButton;
        private ModernButton _presetButton;
        private ModernButton _closeButton;

        private static readonly string[] VendorNames = new string[] { "微软 Xbox", "索尼 PlayStation", "任天堂 Switch" };

        private PadLayoutPickerDialog(int layout)
        {
            _layout = layout < 0 ? 0 : (layout > 2 ? 2 : layout);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(9.4f, false);
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(1080), Theme.S(500));
            DoubleBuffered = true;
            KeyPreview = true;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;

            for (int i = 0; i < VendorNames.Length; i++)
            {
                int idx = i;
                ModernButton b = new ModernButton();
                b.Text = VendorNames[i];
                b.Radius = 9;
                b.Click += delegate { SelectVendor(idx); };
                Controls.Add(b);
                _vendorButtons.Add(b);
            }

            _body = new PadBodyPanel();
            _body.SetLayout(_layout);
            _body.SetShowLabels(true);
            _body.SlotClicked += delegate(PadSlot slot)
            {
                Action<PadSlot> h = SlotClicked;
                if (h != null) h(slot);
            };
            Controls.Add(_body);

            _clearButton = new ModernButton();
            _clearButton.Text = "清空当前布局";
            _clearButton.Style = ModernButtonStyle.Danger;
            _clearButton.Radius = 9;
            _clearButton.Click += delegate
            {
                Action h = ClearClicked;
                if (h != null) h();
            };
            Controls.Add(_clearButton);

            _presetButton = new ModernButton();
            _presetButton.Text = "整套添加";
            _presetButton.Style = ModernButtonStyle.Primary;
            _presetButton.Radius = 9;
            _presetButton.Click += delegate
            {
                Action<int> h = PresetClicked;
                if (h != null) h(_layout);
            };
            Controls.Add(_presetButton);

            _closeButton = new ModernButton();
            _closeButton.Text = "关闭";
            _closeButton.Style = ModernButtonStyle.Secondary;
            _closeButton.Radius = 9;
            _closeButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_closeButton);

            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    e.SuppressKeyPress = true;
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };
            MouseDown += DialogOnMouseDown;
            Resize += delegate { LayoutDialog(); };
            Shown += delegate { LayoutDialog(); };
            RefreshVendorStyles();
        }

        public static void Show(IWin32Window owner, int layout, Action<PadSlot> onSlot, Action onClear, Action<int> onPreset)
        {
            using (PadLayoutPickerDialog dlg = new PadLayoutPickerDialog(layout))
            {
                if (onSlot != null) dlg.SlotClicked += onSlot;
                if (onClear != null) dlg.ClearClicked += onClear;
                if (onPreset != null) dlg.PresetClicked += onPreset;
                dlg.ShowDialog(owner);
            }
        }

        private void SelectVendor(int layout)
        {
            _layout = layout;
            _body.SetLayout(_layout);
            RefreshVendorStyles();
            LayoutDialog();
            Invalidate();
        }

        private void RefreshVendorStyles()
        {
            for (int i = 0; i < _vendorButtons.Count; i++)
            {
                _vendorButtons[i].Style = i == _layout ? ModernButtonStyle.Primary : ModernButtonStyle.Secondary;
            }
        }

        private void LayoutDialog()
        {
            float s = Theme.Scale;
            int pad = Theme.S(22);
            int gap = Theme.S(10);
            int vendorW = (ClientSize.Width - pad * 2 - gap * 2) / 3;
            int vendorY = Theme.S(58);
            for (int i = 0; i < _vendorButtons.Count; i++)
            {
                _vendorButtons[i].SetBounds(pad + i * (vendorW + gap), vendorY, vendorW, Theme.S(40));
            }

            int contentTop = Theme.S(104);
            int contentH = ClientSize.Height - contentTop - Theme.S(68);
            int brandW = Theme.S(172);
            int legendW = Theme.S(172);
            int bodyX = pad + brandW + gap;
            int bodyW = ClientSize.Width - pad * 2 - brandW - legendW - gap * 2;
            _body.SetBounds(bodyX, contentTop, bodyW, contentH);

            int bottomY = ClientSize.Height - Theme.S(16) - Theme.S(40);
            int btnW = Theme.S(128);
            int btnH = Theme.S(40);
            _closeButton.SetBounds(pad, bottomY, btnW, btnH);
            _presetButton.SetBounds(ClientSize.Width - pad - btnW, bottomY, btnW, btnH);
            _clearButton.SetBounds(_presetButton.Left - gap - btnW, bottomY, btnW, btnH);
        }

        private void DialogOnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y < Theme.S(56))
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, new IntPtr(NativeMethods.HTCAPTION), IntPtr.Zero);
            }
        }

        // 左侧品牌标识区：品牌图形 + 英文品牌名 + 中文布局名。
        private void DrawBrand(Graphics g, RectangleF rect)
        {
            float s = Theme.Scale;
            // 品牌图形标识（几何近似，不依赖外部图片资源）
            float logoSize = Theme.S(64);
            float logoY = rect.Y + Theme.S(18);
            float logoX = rect.X + (rect.Width - logoSize) / 2f;
            RectangleF logoRect = new RectangleF(logoX, logoY, logoSize, logoSize);

            if (_layout == 0)
            {
                // Xbox：绿色圆底 + 白色 X
                using (SolidBrush b = new SolidBrush(Color.FromArgb(16, 124, 16)))
                {
                    g.FillEllipse(b, logoRect);
                }
                using (Pen pen = new Pen(Color.White, 3.4f * s))
                {
                    g.DrawLine(pen, logoRect.X + logoSize * 0.30f, logoRect.Y + logoSize * 0.30f,
                        logoRect.Right - logoSize * 0.30f, logoRect.Bottom - logoSize * 0.30f);
                    g.DrawLine(pen, logoRect.Right - logoSize * 0.30f, logoRect.Y + logoSize * 0.30f,
                        logoRect.X + logoSize * 0.30f, logoRect.Bottom - logoSize * 0.30f);
                }
                Theme.DrawText(g, "Microsoft", Theme.Semibold(15f), Theme.Text,
                    new RectangleF(rect.X, logoRect.Bottom + Theme.S(14), rect.Width, Theme.S(26)),
                    StringAlignment.Center, StringAlignment.Center, true);
                Theme.DrawText(g, "微软 Xbox 布局", Theme.UiFont(10f, false), Theme.TextSoft,
                    new RectangleF(rect.X, logoRect.Bottom + Theme.S(44), rect.Width, Theme.S(22)),
                    StringAlignment.Center, StringAlignment.Center, true);
                Theme.DrawText(g, "（手机横屏）", Theme.UiFont(9f, false), Theme.Muted,
                    new RectangleF(rect.X, logoRect.Bottom + Theme.S(68), rect.Width, Theme.S(20)),
                    StringAlignment.Center, StringAlignment.Center, true);
            }
            else if (_layout == 1)
            {
                // PlayStation：官方 PS 标志（几何自绘，非四色符号横排）。
                RectangleF logoBox = new RectangleF(logoRect.X, logoRect.Y + logoSize * 0.08f,
                    logoSize, logoSize * 0.84f);
                Theme.DrawPlayStationLogo(g, logoBox, Theme.Text);
                Theme.DrawText(g, "PlayStation", Theme.Semibold(15f), Theme.Text,
                    new RectangleF(rect.X, logoRect.Bottom + Theme.S(14), rect.Width, Theme.S(26)),
                    StringAlignment.Center, StringAlignment.Center, true);
                Theme.DrawText(g, "索尼 PlayStation 布局", Theme.UiFont(10f, false), Theme.TextSoft,
                    new RectangleF(rect.X, logoRect.Bottom + Theme.S(44), rect.Width, Theme.S(22)),
                    StringAlignment.Center, StringAlignment.Center, true);
                Theme.DrawText(g, "（手机横屏）", Theme.UiFont(9f, false), Theme.Muted,
                    new RectangleF(rect.X, logoRect.Bottom + Theme.S(68), rect.Width, Theme.S(20)),
                    StringAlignment.Center, StringAlignment.Center, true);
            }
            else
            {
                // Nintendo：红色圆角椭圆 + 白色 Nintendo 字样
                RectangleF pill = new RectangleF(logoRect.X, logoRect.Y + logoSize * 0.28f, logoSize, logoSize * 0.44f);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(222, 28, 36)))
                {
                    Theme.FillRounded(g, pill, pill.Height * 0.5f, b);
                }
                Theme.DrawText(g, "Nintendo", Theme.Semibold(10.5f), Color.White, pill,
                    StringAlignment.Center, StringAlignment.Center, true);
                Theme.DrawText(g, "任天堂 Switch 布局", Theme.UiFont(10f, false), Theme.TextSoft,
                    new RectangleF(rect.X, logoRect.Bottom + Theme.S(20), rect.Width, Theme.S(22)),
                    StringAlignment.Center, StringAlignment.Center, true);
                Theme.DrawText(g, "（手机横屏）", Theme.UiFont(9f, false), Theme.Muted,
                    new RectangleF(rect.X, logoRect.Bottom + Theme.S(44), rect.Width, Theme.S(20)),
                    StringAlignment.Center, StringAlignment.Center, true);
            }
        }

        // 右侧按键图例说明区：彩色圆点 + 分组说明，文案随厂商切换。
        private void DrawLegend(Graphics g, RectangleF rect)
        {
            float s = Theme.Scale;
            string shoulder;
            string system;
            string action;
            if (_layout == 1)
            {
                shoulder = "肩键 / 扳机（L/R/L1/R1/L2/R2）";
                system = "系统按键（PS / 选项）";
                action = "动作按键（△ ○ × □）";
            }
            else if (_layout == 2)
            {
                shoulder = "肩键 / 扳机（L/R/ZL/ZR）";
                system = "系统按键（− / +）";
                action = "ABXY 按键（动作）";
            }
            else
            {
                shoulder = "肩键 / 扳机（L/R）";
                system = "系统按键（视图 / 菜单）";
                action = "ABXY 按键（动作）";
            }

            Color[] dots = new Color[] { Theme.Accent, Theme.Mint, Theme.Gold, Theme.Muted, Theme.Violet, Theme.Accent };
            string[] rows = new string[]
            {
                "左摇杆（移动）",
                "右摇杆（视角）",
                action,
                "方向键（切换 / 菜单）",
                shoulder,
                system
            };

            float rowH = Theme.S(46);
            float dotR = Theme.S(6);
            for (int i = 0; i < rows.Length; i++)
            {
                float y = rect.Y + Theme.S(8) + i * rowH;
                RectangleF dot = new RectangleF(rect.X, y + Theme.S(2), dotR * 2f, dotR * 2f);
                using (SolidBrush b = new SolidBrush(dots[i]))
                {
                    g.FillEllipse(b, dot);
                }
                RectangleF textRect = new RectangleF(rect.X + dotR * 2f + Theme.S(12), y,
                    rect.Width - dotR * 2f - Theme.S(12), rowH);
                Theme.DrawText(g, rows[i], Theme.UiFont(9.2f, false), Theme.TextSoft, textRect,
                    StringAlignment.Near, StringAlignment.Center, true);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            float s = Theme.Scale;
            RectangleF rect = new RectangleF(0.5f, 0.5f, ClientSize.Width - 1f, ClientSize.Height - 1f);
            using (SolidBrush brush = new SolidBrush(Theme.Surface))
            {
                Theme.FillRounded(e.Graphics, rect, 14f * s, brush);
            }
            Theme.DrawRounded(e.Graphics, rect, 14f * s, Theme.Stroke, 1f);
            Theme.FillRounded(e.Graphics, new RectangleF(22 * s, 22 * s, 4 * s, 24 * s), 2f * s, Theme.Accent);
            Theme.DrawText(e.Graphics, "选择手柄布局", Theme.Semibold(13f), Theme.Text,
                new RectangleF(38 * s, 16 * s, ClientSize.Width - 70 * s, 34 * s), StringAlignment.Near, StringAlignment.Center, true);
            Theme.DrawText(e.Graphics, "点击单个按键加入布局，或切换厂商后整套添加。", Theme.UiFont(8.8f, false), Theme.Muted,
                new RectangleF(22 * s, 54 * s, ClientSize.Width - 44 * s, 20 * s), StringAlignment.Near, StringAlignment.Center, true);

            int pad = Theme.S(22);
            int gap = Theme.S(10);
            int contentTop = Theme.S(104);
            int contentH = ClientSize.Height - contentTop - Theme.S(68);
            int brandW = Theme.S(172);
            int legendW = Theme.S(172);
            DrawBrand(e.Graphics, new RectangleF(pad, contentTop, brandW, contentH));
            DrawLegend(e.Graphics, new RectangleF(ClientSize.Width - pad - legendW, contentTop, legendW, contentH));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int preference = 2;
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, 4);
            }
            catch { }
        }
    }
}
