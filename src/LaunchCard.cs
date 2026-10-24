using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MobileUiConfig
{
    public class LaunchCard : CardPanel
    {
        private readonly Label _pathLabel;
        private readonly Label _argsLabel;
        private readonly RoundTextBox _pathBox;
        private readonly RoundTextBox _argsBox;
        private readonly ModernButton _browseButton;
        private readonly ModernButton _pasteButton;
        private readonly ModernButton _launchButton;
        private readonly Label _runAtLoginLabel;
        private readonly ToggleSwitch _runAtLoginToggle;
        private readonly ToolTip _tip;

        public event EventHandler BrowseRequested;
        public event EventHandler PasteRequested;
        public event EventHandler LaunchRequested;
        public event EventHandler RunAtLoginChanged;

        public LaunchCard()
        {
            Radius = 16;
            BackColor = Theme.Surface;
            BorderColor = Theme.StrokeSoft;
            Height = Theme.S(112);

            _pathLabel = MakeLabel("程序路径");
            _argsLabel = MakeLabel("启动参数");
            Controls.Add(_pathLabel);
            Controls.Add(_argsLabel);

            _pathBox = new RoundTextBox();
            _pathBox.Placeholder = "选择或粘贴游戏程序路径，例如 D:\\Games\\Game\\game.exe";
            _pathBox.FillColor = Theme.SurfaceAlt;
            Controls.Add(_pathBox);

            _argsBox = new RoundTextBox();
            _argsBox.Placeholder = "可选：启动参数，例如 -windowed -dx11";
            _argsBox.FillColor = Theme.SurfaceAlt;
            Controls.Add(_argsBox);

            _browseButton = new ModernButton();
            _browseButton.Text = "浏览";
            _browseButton.Style = ModernButtonStyle.Secondary;
            _browseButton.Click += delegate
            {
                EventHandler h = BrowseRequested;
                if (h != null) h(this, EventArgs.Empty);
            };
            Controls.Add(_browseButton);

            _pasteButton = new ModernButton();
            _pasteButton.Text = "粘贴";
            _pasteButton.Style = ModernButtonStyle.Secondary;
            _pasteButton.Click += delegate
            {
                EventHandler h = PasteRequested;
                if (h != null) h(this, EventArgs.Empty);
            };
            Controls.Add(_pasteButton);

            _launchButton = new ModernButton();
            _launchButton.Text = "启动游戏";
            _launchButton.Icon = "▶";
            _launchButton.Style = ModernButtonStyle.Primary;
            _launchButton.Font = Theme.Semibold(9.2f);
            _launchButton.Click += delegate
            {
                EventHandler h = LaunchRequested;
                if (h != null) h(this, EventArgs.Empty);
            };
            Controls.Add(_launchButton);

            _runAtLoginLabel = new Label();
            _runAtLoginLabel.Text = "开机后台待命";
            _runAtLoginLabel.AutoSize = false;
            _runAtLoginLabel.BackColor = Theme.Surface;
            _runAtLoginLabel.ForeColor = Theme.Muted;
            _runAtLoginLabel.Font = Theme.UiFont(8.4f, false);
            _runAtLoginLabel.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_runAtLoginLabel);

            _runAtLoginToggle = new ToggleSwitch();
            _runAtLoginToggle.CheckedChanged += delegate
            {
                EventHandler h = RunAtLoginChanged;
                if (h != null) h(this, EventArgs.Empty);
            };
            Controls.Add(_runAtLoginToggle);

            _tip = new ToolTip();
            string tip = "勾选后随 Windows 登录后台待命；检测到游戏启动时自动加载串流悬浮层。";
            _tip.SetToolTip(_runAtLoginToggle, tip);
            _tip.SetToolTip(_runAtLoginLabel, tip);
        }

        public bool RunAtLoginChecked
        {
            get { return _runAtLoginToggle.Checked; }
            set { _runAtLoginToggle.SetCheckedSilent(value); }
        }

        public string ProgramPath
        {
            get { return _pathBox.Text; }
            set { _pathBox.Text = value == null ? "" : value; }
        }

        public string Arguments
        {
            get { return _argsBox.Text; }
            set { _argsBox.Text = value == null ? "" : value; }
        }

        public RoundTextBox PathInput
        {
            get { return _pathBox; }
        }

        public RoundTextBox ArgsInput
        {
            get { return _argsBox; }
        }

        private Label MakeLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.BackColor = Theme.Surface;
            label.ForeColor = Theme.TextSoft;
            label.Font = Theme.UiFont(8.8f, false);
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (_pathBox == null || _launchButton == null || _runAtLoginToggle == null) return;
            float s = Theme.Scale;
            int left = (int)(22 * s);
            int labelW = (int)(90 * s);
            int gap = (int)(8 * s);
            int inputLeft = left + labelW;
            int right = Width - left;
            int launchW = (int)(126 * s);
            int pasteW = (int)(64 * s);
            int browseW = (int)(74 * s);
            int inputH = (int)(38 * s);
            int row1Y = (int)(14 * s);
            int row2Y = (int)(64 * s);

            _launchButton.SetBounds(right - launchW, row1Y, launchW, inputH);
            _pasteButton.SetBounds(_launchButton.Left - gap - pasteW, row1Y, pasteW, inputH);
            _browseButton.SetBounds(_pasteButton.Left - gap - browseW, row1Y, browseW, inputH);

            int pathWidth = Math.Max(120, _browseButton.Left - gap - inputLeft);
            _pathBox.SetBounds(inputLeft, row1Y, pathWidth, inputH);
            _pathLabel.SetBounds(left, row1Y, labelW, inputH);

            int argsRight = right;
            int toggleW = (int)(46 * s);
            int toggleH = (int)(26 * s);
            int autoLabelW = (int)(98 * s);
            _runAtLoginToggle.SetBounds(right - toggleW, row2Y + ((int)(36 * s) - toggleH) / 2, toggleW, toggleH);
            _runAtLoginLabel.SetBounds(_runAtLoginToggle.Left - (int)(8 * s) - autoLabelW, row2Y, autoLabelW, (int)(36 * s));
            argsRight = _runAtLoginLabel.Left - (int)(10 * s);
            _argsBox.SetBounds(inputLeft, row2Y, Math.Max(120, argsRight - inputLeft), (int)(36 * s));
            _argsLabel.SetBounds(left, row2Y, labelW, (int)(36 * s));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _tip != null) _tip.Dispose();
            base.Dispose(disposing);
        }
    }

    public class ModeTab : Control
    {
        private bool _selected;
        private bool _hover;
        private bool _pressed;
        private int _index;
        private string _modeName = "";

        public event EventHandler SelectedRequested;
        public event EventHandler RenameRequested;

        public ModeTab()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.StandardClick |
                     ControlStyles.StandardDoubleClick, true);
            Height = Theme.S(36);
            Width = Theme.S(92);
            Cursor = Cursors.Hand;
            Font = Theme.UiFont(8.8f, false);
            Margin = new Padding(0, 0, Theme.S(6), 0);
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        public int Index
        {
            get { return _index; }
            set { _index = value; }
        }

        public string ModeName
        {
            get { return _modeName; }
            set
            {
                _modeName = value == null ? "" : value;
                ResizeForText();
                Invalidate();
            }
        }

        // 标签的理想宽度（按文字测量），供 ModeTagStrip 手动布局压缩使用。
        public int PreferredWidth { get; private set; }

        private void ResizeForText()
        {
            using (Graphics g = CreateGraphics())
            {
                float width = g.MeasureString(_modeName, Font).Width + 36f * Theme.Scale;
                width = Math.Max(82f * Theme.Scale, Math.Min(158f * Theme.Scale, width));
                PreferredWidth = (int)width;
                Width = (int)width;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            // 标签背景是透明的，父级若未擦除旧像素（滚动/首次布局）会留下重影，
            // 这里先按父级底色涂满自身矩形，任何情况下都不会显示错位残留。
            if (Parent != null && Parent.BackColor.A == 255)
            {
                using (SolidBrush bg = new SolidBrush(Parent.BackColor))
                {
                    e.Graphics.FillRectangle(bg, ClientRectangle);
                }
            }
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            if (_selected)
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(rect,
                    Theme.Accent, Theme.AccentDeep, 0f))
                {
                    Theme.FillRounded(e.Graphics, rect, Height / 2f, brush);
                }
                using (Pen pen = new Pen(Color.FromArgb(60, 255, 255, 255), 1f))
                {
                    Theme.DrawRounded(e.Graphics, new RectangleF(1, 1, Width - 2, Height - 2), Height / 2f,
                        Color.FromArgb(50, 255, 255, 255), 1f);
                }
            }
            else
            {
                if (_hover)
                {
                    Theme.FillRounded(e.Graphics, rect, Height / 2f, Theme.SurfaceHi);
                }
                Theme.DrawRounded(e.Graphics, rect, Height / 2f, _hover ? Theme.Stroke : Theme.StrokeSoft, 1f);
            }

            Color color = _selected ? Color.White : (_hover ? Theme.Text : Theme.TextSoft);
            Theme.DrawText(e.Graphics, _modeName, _selected ? Theme.Semibold(8.9f) : Font, color,
                new RectangleF(10, 0, Width - 20, Height), StringAlignment.Center, StringAlignment.Center, true);
            if (_pressed)
            {
                Theme.FillRounded(e.Graphics, new RectangleF(1, 1, Width - 2, Height - 2), Height / 2f,
                    Color.FromArgb(55, 255, 255, 255));
                Theme.DrawRounded(e.Graphics, new RectangleF(2, 2, Width - 4, Height - 4), Height / 2f,
                    Color.FromArgb(80, 255, 255, 255), 1.2f);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
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
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
            {
                EventHandler h = SelectedRequested;
                if (h != null) h(this, EventArgs.Empty);
            }
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left)
            {
                EventHandler h = RenameRequested;
                if (h != null) h(this, EventArgs.Empty);
            }
        }
    }
}
