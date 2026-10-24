using System;
using System.Drawing;
using System.Windows.Forms;

namespace MobileUiConfig
{
    public class FeatureBar : Panel
    {
        private AppConfig _config;
        private bool _binding;
        private bool _gamepadMode;

        // 虚拟按键页旧功能卡：鼠标转触摸 / 双指缩放 / 空白区域穿透（系统级，写主根）。
        private readonly ToggleSwitch _touch;
        private readonly ToggleSwitch _zoom;
        private readonly ToggleSwitch _pass;
        private readonly Label _touchTitle;
        private readonly Label _zoomTitle;
        private readonly Label _passTitle;
        private readonly Label _touchDesc;
        private readonly Label _zoomDesc;
        private readonly Label _passDesc;

        // 虚拟手柄页新功能卡：拦截屏幕触摸 / 屏蔽物理键鼠（写手柄独立根，与按键页互不影响）。
        private readonly ToggleSwitch _shield;
        private readonly ToggleSwitch _phys;
        private readonly Label _shieldTitle;
        private readonly Label _physTitle;
        private readonly Label _shieldDesc;
        private readonly Label _physDesc;

        public event EventHandler Changed;

        public FeatureBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Bg;
            Height = Theme.S(86);

            _touch = MakeToggle();
            _zoom = MakeToggle();
            _pass = MakeToggle();
            _shield = MakeToggle();
            _phys = MakeToggle();

            _touch.CheckedChanged += delegate { if (!_binding && _config != null) { _config.MouseToTouch = _touch.Checked; Raise(); } };
            _zoom.CheckedChanged += delegate { if (!_binding && _config != null) { _config.PinchZoom = _zoom.Checked; Raise(); } };
            _pass.CheckedChanged += delegate { if (!_binding && _config != null) { _config.ClickThrough = _pass.Checked; Raise(); } };
            _shield.CheckedChanged += delegate { if (!_binding && _config != null) { _config.BlockBlankTouch = _shield.Checked; Raise(); } };
            _phys.CheckedChanged += delegate { if (!_binding && _config != null) { _config.BlockPhysicalInput = _phys.Checked; Raise(); } };

            _touchTitle = MakeLabel("鼠标转触摸");
            _zoomTitle = MakeLabel("双指缩放");
            _passTitle = MakeLabel("空白区域穿透");
            _shieldTitle = MakeLabel("拦截屏幕触摸");
            _physTitle = MakeLabel("屏蔽物理键鼠");

            _touchDesc = MakeDesc("系统级鼠标转触摸\n触屏设备专用，游戏内可用");
            _zoomDesc = MakeDesc("双指缩放映射鼠标滚轮\n滚轮上下缩放画面");
            _passDesc = MakeDesc("点击空白区域穿透\n不阻挡游戏鼠标操作");
            _shieldDesc = MakeDesc("手指滑出按键区不穿透到游戏画面\n防止误触游戏界面");
            _physDesc = MakeDesc("手柄模式下屏蔽物理键盘与鼠标按钮\n防止游戏切回键鼠界面");

            Controls.Add(_touch); Controls.Add(_zoom); Controls.Add(_pass);
            Controls.Add(_shield); Controls.Add(_phys);
            Controls.Add(_touchTitle); Controls.Add(_zoomTitle); Controls.Add(_passTitle);
            Controls.Add(_shieldTitle); Controls.Add(_physTitle);
            Controls.Add(_touchDesc); Controls.Add(_zoomDesc); Controls.Add(_passDesc);
            Controls.Add(_shieldDesc); Controls.Add(_physDesc);
        }

        // config：按键页传主根 _config；手柄页传手柄独立根 Root。gamepadMode：true=虚拟手柄页。
        public void SetConfig(AppConfig config, bool gamepadMode)
        {
            _config = config;
            _gamepadMode = gamepadMode;
            if (_config == null) return;
            _binding = true;
            try
            {
                _touch.SetCheckedSilent(_config.MouseToTouch);
                _zoom.SetCheckedSilent(_config.PinchZoom);
                _pass.SetCheckedSilent(_config.ClickThrough);
                _shield.SetCheckedSilent(_config.BlockBlankTouch);
                _phys.SetCheckedSilent(_config.BlockPhysicalInput);
            }
            finally { _binding = false; }
            ApplyVisibility();
            LayoutCards();
            Invalidate();
        }

        private void ApplyVisibility()
        {
            bool pad = !_gamepadMode;
            SetVisible(_touch, pad);
            SetVisible(_zoom, pad);
            SetVisible(_pass, pad);
            SetVisible(_shield, _gamepadMode);
            SetVisible(_phys, _gamepadMode);
            SetVisible(_touchTitle, pad); SetVisible(_zoomTitle, pad); SetVisible(_passTitle, pad);
            SetVisible(_shieldTitle, _gamepadMode); SetVisible(_physTitle, _gamepadMode);
            SetVisible(_touchDesc, pad); SetVisible(_zoomDesc, pad); SetVisible(_passDesc, pad);
            SetVisible(_shieldDesc, _gamepadMode); SetVisible(_physDesc, _gamepadMode);
        }

        private static void SetVisible(Control c, bool visible)
        {
            if (c != null) c.Visible = visible;
        }

        private ToggleSwitch MakeToggle()
        {
            ToggleSwitch toggle = new ToggleSwitch();
            return toggle;
        }

        private Label MakeLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.BackColor = Theme.SurfaceAlt;
            label.ForeColor = Theme.Text;
            label.Font = Theme.UiFont(8.8f, true);
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private Label MakeDesc(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.BackColor = Theme.SurfaceAlt;
            label.ForeColor = Theme.Faint;
            label.Font = Theme.UiFont(7.0f, false);
            label.TextAlign = ContentAlignment.TopLeft;
            return label;
        }

        private void Raise()
        {
            EventHandler h = Changed;
            if (h != null) h(this, EventArgs.Empty);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutCards();
        }

        private void LayoutCards()
        {
            if (_touch == null) return;
            ToggleSwitch[] toggles;
            Label[] titles;
            Label[] descs;
            if (_gamepadMode)
            {
                toggles = new ToggleSwitch[] { _shield, _phys };
                titles = new Label[] { _shieldTitle, _physTitle };
                descs = new Label[] { _shieldDesc, _physDesc };
            }
            else
            {
                toggles = new ToggleSwitch[] { _touch, _zoom, _pass };
                titles = new Label[] { _touchTitle, _zoomTitle, _passTitle };
                descs = new Label[] { _touchDesc, _zoomDesc, _passDesc };
            }

            int pad = Theme.S(18);
            int gap = Theme.S(10);
            int width = Width - pad * 2;
            int count = toggles.Length;
            int cardW = Math.Max(Theme.S(220), (width - gap * (count - 1)) / count);
            int cardH = Height - Theme.S(8);
            int x = pad;
            int y = Theme.S(4);
            for (int i = 0; i < count; i++)
            {
                LayoutCard(toggles[i], titles[i], descs[i], x, y, cardW, cardH);
                x += cardW + gap;
            }
        }

        private void LayoutCard(ToggleSwitch toggle, Label title, Label desc, int x, int y, int w, int h)
        {
            toggle.SetBounds(x + w - Theme.S(62), y + Theme.S(12), Theme.S(42), Theme.S(24));
            title.SetBounds(x + Theme.S(14), y + Theme.S(8), Math.Max(40, w - Theme.S(82)), Theme.S(22));
            desc.SetBounds(x + Theme.S(14), y + Theme.S(30), w - Theme.S(24), h - Theme.S(32));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            int pad = Theme.S(18);
            int gap = Theme.S(10);
            int width = Width - pad * 2;
            int count = _gamepadMode ? 2 : 3;
            int cardW = Math.Max(Theme.S(220), (width - gap * (count - 1)) / count);
            int cardH = Height - Theme.S(8);
            int x = pad;
            for (int i = 0; i < count; i++)
            {
                RectangleF rect = new RectangleF(x, Theme.S(4), cardW, cardH);
                Theme.FillRounded(e.Graphics, rect, Theme.S(10), Theme.SurfaceAlt);
                Theme.DrawRounded(e.Graphics, rect, Theme.S(10), Theme.StrokeSoft, 1f);
                x += cardW + gap;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }
    }
}
