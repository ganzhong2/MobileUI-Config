using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace MobileUiConfig
{
    // Project header shown directly below the real Windows title bar.
    public class TitleBar : Control
    {
        private readonly TextBox _projectBox;
        private readonly StatusPill _envPill;
        private readonly StatusPill _localPill;
        private readonly StatusPill _streamPill;
        private readonly ToggleSwitch _localToggle;
        private readonly ToggleSwitch _streamToggle;
        private readonly StatusPill _gamePill;
        private readonly Label _padKeyLabel;
        private readonly ToggleSwitch _padKeyToggle;
        private readonly Label _padPadLabel;
        private readonly ToggleSwitch _padPadToggle;
        private string _saveText = "已自动保存";
        private Color _saveColor = Theme.Mint;
        private bool _titleFocused;
        private Bitmap _iconBitmap;
        private bool _bindingUi;
        private bool _bindingPage;

        public event EventHandler ProjectNameChanged;
        public event EventHandler LocalUiChanged;
        public event EventHandler StreamUiChanged;
        public event EventHandler PageSideChanged;

        public TitleBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Bg;
            Height = Theme.S(62);
            Font = Theme.UiFont(9f, false);

            _projectBox = new TextBox();
            _projectBox.BorderStyle = BorderStyle.None;
            _projectBox.BackColor = Theme.Bg;
            _projectBox.ForeColor = Theme.Text;
            _projectBox.Font = Theme.Semibold(14.5f);
            _projectBox.MaxLength = 60;
            _projectBox.Text = "移动端UI";
            _projectBox.TextChanged += new EventHandler(ProjectBoxOnTextChanged);
            _projectBox.GotFocus += delegate { _titleFocused = true; Invalidate(); };
            _projectBox.LostFocus += delegate
            {
                _titleFocused = false;
                if (_projectBox.Text.Trim().Length == 0) _projectBox.Text = "移动端UI";
                Invalidate();
            };
            _projectBox.KeyDown += new KeyEventHandler(ProjectBoxOnKeyDown);
            Controls.Add(_projectBox);

            _localPill = new StatusPill();
            _localPill.SetStatus("本地模式", Theme.Accent);
            Controls.Add(_localPill);

            _localToggle = new ToggleSwitch();
            _localToggle.CheckedChanged += delegate
            {
                if (_bindingUi) return;
                EventHandler h = LocalUiChanged;
                if (h != null) h(this, EventArgs.Empty);
            };
            Controls.Add(_localToggle);

            _streamPill = new StatusPill();
            _streamPill.SetStatus("串流模式", Theme.Muted);
            Controls.Add(_streamPill);

            _streamToggle = new ToggleSwitch();
            _streamToggle.CheckedChanged += delegate
            {
                if (_bindingUi) return;
                EventHandler h = StreamUiChanged;
                if (h != null) h(this, EventArgs.Empty);
            };
            Controls.Add(_streamToggle);

            _gamePill = new StatusPill();
            _gamePill.SetStatus("未保存游戏", Theme.Violet);
            Controls.Add(_gamePill);

            _envPill = new StatusPill();
            _envPill.SetStatus("当前：检测中", Theme.Gold);
            Controls.Add(_envPill);

            _padKeyLabel = MakePadLabel("虚拟按键");
            _padKeyToggle = new ToggleSwitch();
            _padKeyToggle.CheckedChanged += delegate
            {
                if (_bindingPage) return;
                _bindingPage = true;
                try { _padPadToggle.SetCheckedSilent(false); }
                finally { _bindingPage = false; }
                EventHandler h = PageSideChanged;
                if (h != null) h(this, EventArgs.Empty);
            };
            Controls.Add(_padKeyLabel);
            Controls.Add(_padKeyToggle);

            _padPadLabel = MakePadLabel("虚拟手柄");
            _padPadToggle = new ToggleSwitch();
            _padPadToggle.CheckedChanged += delegate
            {
                if (_bindingPage) return;
                _bindingPage = true;
                try { _padKeyToggle.SetCheckedSilent(false); }
                finally { _bindingPage = false; }
                EventHandler h = PageSideChanged;
                if (h != null) h(this, EventArgs.Empty);
            };
            Controls.Add(_padPadLabel);
            Controls.Add(_padPadToggle);

            LoadHeaderIcon();
        }

        private void LoadHeaderIcon()
        {
            try
            {
                _iconBitmap = AppIcon.LoadBitmap();
            }
            catch
            {
                _iconBitmap = null;
            }
        }

        public string GameName
        {
            get { return _gamePill.StatusText; }
            set { _gamePill.SetStatus(string.IsNullOrEmpty(value) ? "未保存游戏" : value, Theme.Violet); }
        }

        public string ProjectName
        {
            get { return _projectBox.Text; }
            set
            {
                string text = value == null ? "" : value;
                if (_projectBox.Text == text) return;
                _projectBox.Text = text;
            }
        }

        public StatusPill EnvironmentPill
        {
            get { return _localPill; }
        }

        public bool LocalUiChecked
        {
            get { return _localToggle.Checked; }
        }

        public bool StreamUiChecked
        {
            get { return _streamToggle.Checked; }
        }

        public void SetUiToggles(bool local, bool stream)
        {
            _bindingUi = true;
            try
            {
                _localToggle.SetCheckedSilent(local);
                _streamToggle.SetCheckedSilent(stream);
            }
            finally
            {
                _bindingUi = false;
            }
        }

        public int PageSide
        {
            get { return _padPadToggle.Checked ? 1 : 0; }
        }

        public void SetPageSideSilent(int side)
        {
            _bindingPage = true;
            try
            {
                _padKeyToggle.SetCheckedSilent(side != 1);
                _padPadToggle.SetCheckedSilent(side == 1);
            }
            finally
            {
                _bindingPage = false;
            }
        }

        private Label MakePadLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.BackColor = Theme.Bg;
            label.ForeColor = Theme.TextSoft;
            label.Font = Theme.UiFont(8.4f, false);
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        public void SetEnvironmentMode(bool stream)
        {
            _localPill.SetStatus("本地模式", stream ? Theme.Muted : Theme.Accent);
            _streamPill.SetStatus("串流模式", stream ? Theme.Mint : Theme.Muted);
            _envPill.SetStatus(stream ? "当前：串流模式" : "当前：本地模式", stream ? Theme.Mint : Theme.Accent);
        }

        public void SetEnvironmentChecking()
        {
            _localPill.SetStatus("本地模式", Theme.Gold);
            _streamPill.SetStatus("串流模式", Theme.Gold);
            _envPill.SetStatus("当前：检测中", Theme.Gold);
        }

        public void SetProjectNameSilent(string value)
        {
            _projectBox.TextChanged -= ProjectBoxOnTextChanged;
            _projectBox.Text = value == null ? "" : value;
            _projectBox.TextChanged += ProjectBoxOnTextChanged;
        }

        public void SetSaveState(string text, Color color)
        {
            _saveText = text == null ? "" : text;
            _saveColor = color;
            Invalidate();
        }

        public void FocusProjectName()
        {
            _projectBox.Focus();
            _projectBox.SelectAll();
        }

        private void ProjectBoxOnTextChanged(object sender, EventArgs e)
        {
            EventHandler h = ProjectNameChanged;
            if (h != null) h(this, EventArgs.Empty);
            Invalidate();
        }

        private void ProjectBoxOnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                Parent.Focus();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_projectBox == null || _localPill == null) return;
            float s = Theme.Scale;
            int left = Theme.S(70);
            int envW = Theme.S(118);
            int envH = Theme.S(26);
            int toggleW = Theme.S(42);
            int saveW = Theme.S(168);
            int right = Width - Theme.S(22);
            int saveLeft = right - saveW;

            int localRight = saveLeft - Theme.S(12);
            _streamToggle.SetBounds(localRight - toggleW, (Height - envH) / 2, toggleW, envH);
            _streamPill.SetBounds(_streamToggle.Left - Theme.S(8) - envW, (Height - envH) / 2, envW, envH);

            int streamRight = _streamPill.Left - Theme.S(12);
            _localToggle.SetBounds(streamRight - toggleW, (Height - envH) / 2, toggleW, envH);
            _localPill.SetBounds(_localToggle.Left - Theme.S(8) - envW, (Height - envH) / 2, envW, envH);

            int gameW = Theme.S(168);
            int gameLeft = _localPill.Left - Theme.S(12) - gameW;
            _gamePill.SetBounds(gameLeft, (Height - envH) / 2, gameW, envH);
            int envPillW = Theme.S(128);
            int envPillLeft = gameLeft - Theme.S(8) - envPillW;
            _envPill.SetBounds(envPillLeft, (Height - envH) / 2, envPillW, envH);

            int cy = (Height - envH) / 2;
            int padToggleW = Theme.S(42);
            int padLabelW = Theme.S(64);
            int padGap = Theme.S(8);
            // 虚拟手柄组（挨着环境标识左侧）
            int padPadToggleLeft = envPillLeft - Theme.S(12) - padToggleW;
            _padPadToggle.SetBounds(padPadToggleLeft, cy, padToggleW, envH);
            _padPadLabel.SetBounds(padPadToggleLeft - padGap - padLabelW, cy, padLabelW, envH);
            // 虚拟按键组（再往左）
            int padKeyToggleLeft = _padPadLabel.Left - Theme.S(12) - padToggleW;
            _padKeyToggle.SetBounds(padKeyToggleLeft, cy, padToggleW, envH);
            _padKeyLabel.SetBounds(padKeyToggleLeft - padGap - padLabelW, cy, padLabelW, envH);

            int titleRight = _padKeyLabel.Left - Theme.S(14);
            int titleWidth = Math.Max(Theme.S(180), Math.Min(Theme.S(320), titleRight - left));
            _projectBox.SetBounds(left, Theme.S(9), titleWidth, Theme.S(27));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            using (SolidBrush bg = new SolidBrush(Theme.Bg))
            {
                e.Graphics.FillRectangle(bg, ClientRectangle);
            }

            float s = Theme.Scale;
            RectangleF logo = new RectangleF(Theme.S(18), Theme.S(14), Theme.S(32), Theme.S(32));
            if (_iconBitmap != null)
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                e.Graphics.DrawImage(_iconBitmap, logo);
            }
            else
            {
                AppIconRenderer.Draw(e.Graphics, logo);
            }

            int left = Theme.S(70);
            Theme.DrawText(e.Graphics, "项目名称 · 点击文字可重命名", Theme.UiFont(7.6f, false), Theme.Faint,
                new RectangleF(left, Theme.S(38), Theme.S(230), Theme.S(16)), StringAlignment.Near, StringAlignment.Center, false);

            if (_titleFocused)
            {
                using (Pen p = new Pen(Theme.Alpha(Theme.Accent, 190), 1.5f))
                {
                    e.Graphics.DrawLine(p, left, _projectBox.Bottom + Theme.S(2), _projectBox.Right, _projectBox.Bottom + Theme.S(2));
                }
            }

            if (!string.IsNullOrEmpty(_saveText))
            {
                int saveRight = Width - Theme.S(22);
                int saveW = Theme.S(168);
                RectangleF area = new RectangleF(saveRight - saveW, 0, saveW, Height);
                Theme.FillCircle(e.Graphics, new RectangleF(area.X, Height / 2f - Theme.S(3), Theme.S(6), Theme.S(6)), _saveColor);
                Theme.DrawText(e.Graphics, _saveText, Theme.UiFont(8.2f, false), Theme.Muted,
                    new RectangleF(area.X + Theme.S(13), 0, area.Width - Theme.S(13), Height), StringAlignment.Near, StringAlignment.Center, true);
            }

            using (Pen bottom = new Pen(Theme.StrokeSoft, 1f))
            {
                e.Graphics.DrawLine(bottom, 0, Height - 1, Width, Height - 1);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }
    }
}
