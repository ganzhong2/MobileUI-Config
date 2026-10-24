using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MobileUiConfig
{
    public class JoystickSettingsPanel : Panel, IPageVisibilityAware
    {
        private JoystickConfig _left;
        private JoystickConfig _right;
        private DpadConfig _dpad;
        private bool _binding;
        private bool _gamepadMode;
        private bool _layingOut;
        private bool _collapsed;

        private readonly StickGroup _leftGroup;
        private readonly StickGroup _rightGroup;
        private readonly DpadGroup _dpadGroup;

        // 按键页专用：摇杆方向绑定（上/下/左/右 离散键位）+ 显示模式条；手柄页隐藏。
        private readonly Label _dirTitle;
        private readonly Label[] _dirLabels;
        private readonly KeyCaptureBox[] _dirBoxes;
        private readonly Label _modeLabel;
        private readonly ModeVisibilityStrip _modeStrip;
        private List<ModeConfig> _modes;

        private static readonly string[] CurveNames = new string[] { "线性", "加速", "减速", "S 型" };
        private static readonly string[] DirNames = new string[] { "上", "下", "左", "右" };

        public event EventHandler Changed;

        public JoystickSettingsPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.SurfaceAlt;

            // 先创建三组控件并加入容器，再设置 Height；否则 Height 触发的 OnResize 会在
            // 分组尚未实例化时进入 LayoutControls 造成空引用崩溃。
            _leftGroup = new StickGroup(this, "摇杆", CurveNames);
            _rightGroup = new StickGroup(this, "右摇杆", CurveNames);
            _dpadGroup = new DpadGroup(this, "方向键");
            Controls.Add(_leftGroup.Host);
            Controls.Add(_rightGroup.Host);
            Controls.Add(_dpadGroup.Host);

            // 按键页方向绑定区与显示模式条。
            _dirTitle = MakePanelLabel("方向绑定");
            _dirTitle.Font = Theme.Semibold(8.4f);
            _dirTitle.ForeColor = Theme.Muted;
            _dirLabels = new Label[4];
            _dirBoxes = new KeyCaptureBox[4];
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                _dirLabels[i] = MakePanelLabel(DirNames[i]);
                _dirBoxes[i] = new KeyCaptureBox();
                _dirBoxes[i].BindingChanged += delegate { DirBindingChanged(idx); };
                Controls.Add(_dirLabels[i]);
                Controls.Add(_dirBoxes[i]);
            }
            _modeLabel = MakePanelLabel("显示模式");
            _modeLabel.ForeColor = Theme.Faint;
            _modeLabel.Font = Theme.UiFont(7.8f, false);
            _modeStrip = new ModeVisibilityStrip();
            _modeStrip.Accent = Theme.Pink;
            _modeStrip.Toggled += delegate { NotifyChanged(); };
            Controls.Add(_modeLabel);
            Controls.Add(_modeStrip);

            Height = Theme.S(506);
            SetGamepadMode(false);
        }

        private static Label MakePanelLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.BackColor = Theme.SurfaceAlt;
            label.ForeColor = Theme.Muted;
            label.Font = Theme.UiFont(8.1f, false);
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        // 选中某一组（0=左摇杆 1=右摇杆 2=方向键）后把该分组滚动到可视区，便于直接调参数。
        public void FocusGroup(int kind)
        {
            Control host = kind == 0 ? _leftGroup.Host : (kind == 1 ? _rightGroup.Host : _dpadGroup.Host);
            if (host == null) return;
            BringToFront();
            ScrollControlIntoView(host);
            Invalidate();
        }

        // 页面切换入口：false=虚拟按键（单摇杆 + WASD 方向绑定 + 显示模式条），true=虚拟手柄（左/右摇杆 + 方向键三组）。
        public void SetGamepadMode(bool gamepad)
        {
            _gamepadMode = gamepad;
            _leftGroup.SetTitle(gamepad ? "左摇杆" : "摇杆");
            ApplyModeVisibility();
            // 先切模式显隐，再按当前已绑定的左摇杆回填方向绑定框，避免 SetConfig 早于
            // SetGamepadMode 时残留上一页模式导致按键页方向框空白。
            if (!gamepad && _left != null)
            {
                _binding = true;
                try
                {
                    _dirBoxes[0].SetBindingSilent(_left.Up);
                    _dirBoxes[1].SetBindingSilent(_left.Down);
                    _dirBoxes[2].SetBindingSilent(_left.Left);
                    _dirBoxes[3].SetBindingSilent(_left.Right);
                }
                finally
                {
                    _binding = false;
                }
            }
            LayoutControls();
            Invalidate();
        }

        // 按当前页面模式设置各分组 / 按键页专属控件的期望可见性。抽出为独立方法，供
        // CardDock 展开恢复折叠快照后回调（ReapplyPageVisibility），避免旧快照把上一页
        // 专属控件恢复出来。
        private void ApplyModeVisibility()
        {
            // 折叠态：CardDock 已把面板压到标题栏高度，所有内容区控件必须保持隐藏。此前折叠
            // 期间跨页切换 / 回填调用本方法时，只按页面模式把三组 Host 重新置真，而
            // LayoutControls 在折叠态直接返回、不再布局，Host 停在构造默认坐标，内部开关与
            // 步进框就叠到标题行盖住标题文字（图1 残留开关、加号、标题只剩末字）。折叠态下
            // 一律先隐藏全部内容控件，展开后再按页面模式恢复期望可见性。折叠标志由 OnResize
            // 按卡片高度同步，不依赖调用瞬间的 Height，避免展开时序中快照恢复先于压高。
            if (_collapsed)
            {
                _leftGroup.Host.Visible = false;
                _rightGroup.Host.Visible = false;
                _dpadGroup.Host.Visible = false;
                _dirTitle.Visible = false;
                for (int i = 0; i < 4; i++)
                {
                    _dirLabels[i].Visible = false;
                    _dirBoxes[i].Visible = false;
                }
                _modeLabel.Visible = false;
                _modeStrip.Visible = false;
                return;
            }
            // 左摇杆（按键页即唯一摇杆）两页都显示；上一轮折叠态隐藏全部后，非折叠分支若不显式
            // 置真，leftGroup 会停留在折叠态 Visible=false，而 LayoutControls 仍为它预留整组
            // 高度，形成标题下一大片空白、其余参数组被挤到下半部（图1/图3 共同回归症状）。
            _leftGroup.Host.Visible = true;
            _rightGroup.Host.Visible = _gamepadMode;
            _dpadGroup.Host.Visible = _gamepadMode;
            _dirTitle.Visible = !_gamepadMode;
            for (int i = 0; i < 4; i++)
            {
                _dirLabels[i].Visible = !_gamepadMode;
                _dirBoxes[i].Visible = !_gamepadMode;
            }
            _modeLabel.Visible = !_gamepadMode;
            _modeStrip.Visible = !_gamepadMode;
        }

        // CardDock 展开折叠卡片、恢复旧可见性快照后回调：按当前页面模式重设期望可见性
        // 并重排，保证手柄页不再残留按键页方向绑定框与显示模式条，反之亦然。
        public void ReapplyPageVisibility()
        {
            ApplyModeVisibility();
            LayoutControls();
            Invalidate();
        }

        // 按键页显示模式条：绑定各模式 ShowJoystick；手柄页不使用。
        public void SetModes(List<ModeConfig> modes)
        {
            _modes = modes;
            if (_modeStrip != null) _modeStrip.SetModes(modes, true);
        }

        public void SetConfig(JoystickConfig left, JoystickConfig right, DpadConfig dpad)
        {
            _left = left;
            _right = right;
            _dpad = dpad;
            _binding = true;
            try
            {
                if (_left == null) return;
                _leftGroup.Bind(_left);
                if (_right != null)
                {
                    _rightGroup.Bind(_right);
                }
                _dpadGroup.Bind(_dpad);
                if (!_gamepadMode)
                {
                    _dirBoxes[0].SetBindingSilent(_left.Up);
                    _dirBoxes[1].SetBindingSilent(_left.Down);
                    _dirBoxes[2].SetBindingSilent(_left.Left);
                    _dirBoxes[3].SetBindingSilent(_left.Right);
                }
            }
            finally
            {
                _binding = false;
            }
            Invalidate();
        }

        private void DirBindingChanged(int idx)
        {
            if (_binding || _left == null) return;
            KeyBinding binding = _dirBoxes[idx].Binding;
            if (idx == 0) _left.Up = binding;
            else if (idx == 1) _left.Down = binding;
            else if (idx == 2) _left.Left = binding;
            else _left.Right = binding;
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            if (_binding) return;
            RaiseChanged();
        }

        private void NotifyNormalized(JoystickConfig joy, StickGroup group)
        {
            if (_binding || joy == null) return;
            joy.Normalize();
            _binding = true;
            try { group.Refill(joy); }
            finally { _binding = false; }
        }

        private void RaiseChanged()
        {
            EventHandler h = Changed;
            if (h != null) h(this, EventArgs.Empty);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // 按卡片当前高度同步折叠标志：CardDock 折叠压到标题栏高度、展开恢复完整高度，
            // 都会触发本事件；先刷新内容区可见性，再走布局，避免展开瞬间内容被旧快照隐藏。
            _collapsed = Height < Theme.S(48);
            ApplyModeVisibility();
            LayoutControls();
        }

        private void LayoutControls()
        {
            if (_leftGroup == null || _rightGroup == null || _dpadGroup == null || _layingOut) return;
            // 折叠态：CardDock 已把面板压到标题栏高度（<48），不重排、不撑高面板。
            if (Height < Theme.S(48)) return;
            _layingOut = true;
            try
            {
                int pad = Theme.S(16);
                int gap = Theme.S(10);
                int colW = Math.Max(Theme.S(100), (Width - pad * 2 - gap * 3) / 4);
                int groupH = Theme.S(168);
                if (_gamepadMode)
                {
                    int dpadH = Theme.S(86);
                    int leftTop = Theme.S(56);
                    int rightTop = leftTop + groupH + Theme.S(8);
                    int dpadTop = rightTop + groupH + Theme.S(8);
                    _leftGroup.Layout(pad, leftTop, colW, gap, Width);
                    _rightGroup.Layout(pad, rightTop, colW, gap, Width);
                    _dpadGroup.Layout(pad, dpadTop, colW, gap, Width, dpadH);
                    Height = dpadTop + dpadH + Theme.S(12);
                }
                else
                {
                    int leftTop = Theme.S(56);
                    _leftGroup.Layout(pad, leftTop, colW, gap, Width);

                    int dirTitleTop = leftTop + groupH + Theme.S(10);
                    _dirTitle.SetBounds(pad, dirTitleTop, Theme.S(160), Theme.S(18));
                    int colW2 = Math.Max(Theme.S(120), (Width - pad * 2 - gap) / 2);
                    int row1LabelY = dirTitleTop + Theme.S(22);
                    int row1BoxY = row1LabelY + Theme.S(16);
                    int row2LabelY = row1BoxY + Theme.S(44);
                    int row2BoxY = row2LabelY + Theme.S(16);
                    // 0 上 1 下 2 左 3 右，2×2 排布。
                    PlaceDir(0, pad, row1LabelY, row1BoxY, colW2);
                    PlaceDir(1, pad + colW2 + gap, row1LabelY, row1BoxY, colW2);
                    PlaceDir(2, pad, row2LabelY, row2BoxY, colW2);
                    PlaceDir(3, pad + colW2 + gap, row2LabelY, row2BoxY, colW2);

                    int modeTop = row2BoxY + Theme.S(44);
                    _modeLabel.SetBounds(pad, modeTop, Theme.S(68), Theme.S(32));
                    _modeStrip.SetBounds(pad + Theme.S(72), modeTop, Math.Max(80, Width - pad * 2 - Theme.S(72)), Theme.S(32));
                    Height = modeTop + Theme.S(32) + Theme.S(12);
                }
            }
            finally
            {
                _layingOut = false;
            }
        }

        private void PlaceDir(int idx, int x, int labelY, int boxY, int w)
        {
            _dirLabels[idx].SetBounds(x, labelY, w, Theme.S(16));
            _dirBoxes[idx].SetBounds(x, boxY, w, Theme.S(40));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRounded(e.Graphics, rect, Theme.S(13), BackColor);
            Theme.DrawRounded(e.Graphics, rect, Theme.S(13), Theme.StrokeSoft, 1f);
            // 折叠态以面板高度为准（CardDock 压到标题栏高度），与其他面板口径一致。
            bool collapsed = Height < Theme.S(48);
            int titleY = collapsed ? Theme.S(6) : Theme.S(4);
            string title = _gamepadMode ? "手柄摇杆" : "摇杆设置";
            Theme.DrawText(e.Graphics, title, Theme.Semibold(9.4f), Theme.Text,
                new RectangleF(Theme.S(34), titleY, Theme.S(220), Theme.S(24)), StringAlignment.Near, StringAlignment.Center, true);
            if (!collapsed)
            {
                string subText = _gamepadMode
                    ? "左摇杆 / 右摇杆 / 方向键 · 大小 · 死区 · 灵敏度 · 响应曲线"
                    : "大小 · 外圈 · 内圈 · 不透明度 · 死区 · 灵敏度 · 响应曲线 · 方向绑定";
                Theme.DrawText(e.Graphics, subText, Theme.UiFont(7.4f, false), Theme.Faint,
                    new RectangleF(Theme.S(34), Theme.S(30), Math.Max(80, Width - Theme.S(68)), Theme.S(18)),
                    StringAlignment.Near, StringAlignment.Center, true);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        // 单组摇杆参数控件：大小 / 外圈 / 内圈 / 不透明度 / 死区 / 灵敏度 / 弯曲度 / 收起秒数 + 响应曲线。
        private sealed class StickGroup
        {
            private readonly JoystickSettingsPanel _owner;
            private readonly Panel _host;
            private readonly Label _title;
            private readonly ToggleSwitch _enabled;
            private readonly StepperBox _size;
            private readonly StepperBox _outer;
            private readonly StepperBox _inner;
            private readonly StepperBox _opacity;
            private readonly StepperBox _deadZone;
            private readonly StepperBox _sensitivity;
            private readonly StepperBox _strength;
            private readonly StepperBox _autoHide;
            private readonly Label _sizeLabel;
            private readonly Label _outerLabel;
            private readonly Label _innerLabel;
            private readonly Label _opacityLabel;
            private readonly Label _deadZoneLabel;
            private readonly Label _sensitivityLabel;
            private readonly Label _strengthLabel;
            private readonly Label _autoHideLabel;
            private readonly Label _curveTitle;
            private readonly ModernButton[] _curveButtons;
            private JoystickConfig _joy;

            public Panel Host { get { return _host; } }

            public void SetTitle(string text)
            {
                _title.Text = text;
            }

            public StickGroup(JoystickSettingsPanel owner, string title, string[] curveNames)
            {
                _owner = owner;
                _host = new Panel();
                _host.BackColor = Theme.SurfaceAlt;
                _host.Dock = DockStyle.None;

                _title = MakeLabel(title);
                _title.Font = Theme.Semibold(8.4f);
                _title.ForeColor = Theme.Muted;

                _enabled = new ToggleSwitch();
                _enabled.CheckedChanged += delegate
                {
                    if (_owner._binding || _joy == null) return;
                    _joy.Enabled = _enabled.Checked;
                    _owner.NotifyChanged();
                };
                _host.Controls.Add(_enabled);

                _size = MakeStepper(80, 500);
                _outer = MakeStepper(40, 500);
                _inner = MakeStepper(20, 500);
                _opacity = MakeStepper(10, 100);
                _deadZone = MakeStepper(0, 40);
                _sensitivity = MakeStepper(50, 200);
                _strength = MakeStepper(0, 100);
                _autoHide = MakeStepper(0, 10);

                Wire(_size, delegate { _joy.Size = _size.Value; });
                Wire(_outer, delegate { _joy.OuterSize = _outer.Value; });
                Wire(_inner, delegate { _joy.InnerSize = _inner.Value; });
                Wire(_opacity, delegate { _joy.Opacity = _opacity.Value; });
                Wire(_deadZone, delegate { _joy.DeadZone = _deadZone.Value; });
                Wire(_sensitivity, delegate { _joy.Sensitivity = _sensitivity.Value; });
                Wire(_strength, delegate { _joy.CurveStrength = _strength.Value; });
                Wire(_autoHide, delegate { _joy.AutoHideSeconds = _autoHide.Value; });

                _sizeLabel = MakeLabel("摇杆大小");
                _outerLabel = MakeLabel("外圈大小");
                _innerLabel = MakeLabel("内圈大小");
                _opacityLabel = MakeLabel("不透明度");
                _deadZoneLabel = MakeLabel("死区 %");
                _sensitivityLabel = MakeLabel("灵敏度");
                _strengthLabel = MakeLabel("弯曲度");
                _autoHideLabel = MakeLabel("收起秒数");
                _curveTitle = MakeLabel("响应曲线");
                _curveTitle.Font = Theme.UiFont(7.8f, false);
                _curveTitle.ForeColor = Theme.Faint;

                _curveButtons = new ModernButton[curveNames.Length];
                for (int i = 0; i < curveNames.Length; i++)
                {
                    int idx = i;
                    ModernButton b = new ModernButton();
                    b.Text = curveNames[i];
                    b.Style = ModernButtonStyle.Secondary;
                    b.Radius = 8;
                    b.Click += delegate
                    {
                        if (_owner._binding || _joy == null) return;
                        _joy.CurveType = idx;
                        RefreshCurveButtons();
                        _owner.NotifyChanged();
                    };
                    _curveButtons[i] = b;
                    _host.Controls.Add(b);
                }

                _host.Controls.Add(_title);
                _host.Controls.AddRange(new Control[] { _sizeLabel, _outerLabel, _innerLabel, _opacityLabel,
                    _deadZoneLabel, _sensitivityLabel, _strengthLabel, _autoHideLabel, _curveTitle });
                _host.Controls.AddRange(new Control[] { _size, _outer, _inner, _opacity,
                    _deadZone, _sensitivity, _strength, _autoHide });
            }

            private void Wire(StepperBox box, Action apply)
            {
                box.ValueChanged += delegate
                {
                    if (_owner._binding || _joy == null) return;
                    apply();
                    _owner.NotifyNormalized(_joy, this);
                    _owner.NotifyChanged();
                };
            }

            public void Bind(JoystickConfig joy)
            {
                _joy = joy;
                _enabled.SetCheckedSilent(joy != null && joy.Enabled);
                Refill(joy);
                RefreshCurveButtons();
            }

            public void Refill(JoystickConfig joy)
            {
                if (joy == null) return;
                _size.SetValueSilent(joy.Size);
                _outer.SetValueSilent(joy.OuterSize);
                _inner.SetValueSilent(joy.InnerSize);
                _opacity.SetValueSilent(joy.Opacity);
                _deadZone.SetValueSilent(joy.DeadZone);
                _sensitivity.SetValueSilent(joy.Sensitivity);
                _strength.SetValueSilent(joy.CurveStrength);
                _autoHide.SetValueSilent(joy.AutoHideSeconds);
                RefreshCurveButtons();
            }

            private void RefreshCurveButtons()
            {
                if (_joy == null) return;
                for (int i = 0; i < _curveButtons.Length; i++)
                {
                    _curveButtons[i].Style = i == _joy.CurveType ? ModernButtonStyle.Primary : ModernButtonStyle.Secondary;
                }
            }

            public void Layout(int pad, int topY, int colW, int gap, int hostWidth)
            {
                _host.SetBounds(0, topY, hostWidth, Theme.S(168));
                int labelY = Theme.S(20);
                int stepY = Theme.S(36);
                int stepH = Theme.S(34);
                _title.SetBounds(pad, Theme.S(0), colW * 2 - Theme.S(64), Theme.S(18));
                _enabled.SetBounds(hostWidth - pad - Theme.S(46), Theme.S(-1), Theme.S(46), Theme.S(24));

                _sizeLabel.SetBounds(pad, labelY, colW, Theme.S(16));
                _outerLabel.SetBounds(pad + (colW + gap), labelY, colW, Theme.S(16));
                _innerLabel.SetBounds(pad + (colW + gap) * 2, labelY, colW, Theme.S(16));
                _opacityLabel.SetBounds(pad + (colW + gap) * 3, labelY, colW, Theme.S(16));
                _size.SetBounds(pad, stepY, colW, stepH);
                _outer.SetBounds(pad + (colW + gap), stepY, colW, stepH);
                _inner.SetBounds(pad + (colW + gap) * 2, stepY, colW, stepH);
                _opacity.SetBounds(pad + (colW + gap) * 3, stepY, colW, stepH);

                int row2Y = Theme.S(76);
                int row2StepY = Theme.S(92);
                _deadZoneLabel.SetBounds(pad, row2Y, colW, Theme.S(16));
                _sensitivityLabel.SetBounds(pad + (colW + gap), row2Y, colW, Theme.S(16));
                _strengthLabel.SetBounds(pad + (colW + gap) * 2, row2Y, colW, Theme.S(16));
                _autoHideLabel.SetBounds(pad + (colW + gap) * 3, row2Y, colW, Theme.S(16));
                _deadZone.SetBounds(pad, row2StepY, colW, stepH);
                _sensitivity.SetBounds(pad + (colW + gap), row2StepY, colW, stepH);
                _strength.SetBounds(pad + (colW + gap) * 2, row2StepY, colW, stepH);
                _autoHide.SetBounds(pad + (colW + gap) * 3, row2StepY, colW, stepH);

                int curveY = Theme.S(138);
                _curveTitle.SetBounds(pad, curveY + Theme.S(2), Theme.S(68), Theme.S(30));
                int curveLeft = pad + Theme.S(72);
                int curveGap = Theme.S(8);
                int curveW = Math.Max(Theme.S(80), (hostWidth - curveLeft - pad - curveGap * 3) / 4);
                for (int i = 0; i < _curveButtons.Length; i++)
                {
                    _curveButtons[i].SetBounds(curveLeft + (curveW + curveGap) * i, curveY, curveW, Theme.S(30));
                }
            }

            private StepperBox MakeStepper(int min, int max)
            {
                StepperBox box = new StepperBox();
                box.Minimum = min;
                box.Maximum = max;
                box.Step = 1;
                return box;
            }

            private Label MakeLabel(string text)
            {
                Label label = new Label();
                label.Text = text;
                label.AutoSize = false;
                label.BackColor = Theme.SurfaceAlt;
                label.ForeColor = Theme.Muted;
                label.Font = Theme.UiFont(8.1f, false);
                label.TextAlign = ContentAlignment.MiddleLeft;
                return label;
            }
        }

        // 方向键整体外观分组：仅负责启用、圆盘大小、不透明度；位置由手机预览整组拖动决定，
        // 四向触发仍由按键列表中四个方向键各自承担。
        private sealed class DpadGroup
        {
            private readonly JoystickSettingsPanel _owner;
            private readonly Panel _host;
            private readonly Label _title;
            private readonly ToggleSwitch _enabled;
            private readonly StepperBox _size;
            private readonly StepperBox _opacity;
            private readonly Label _sizeLabel;
            private readonly Label _opacityLabel;
            private DpadConfig _cfg;

            public Panel Host { get { return _host; } }

            public DpadGroup(JoystickSettingsPanel owner, string title)
            {
                _owner = owner;
                _host = new Panel();
                _host.BackColor = Theme.SurfaceAlt;
                _host.Dock = DockStyle.None;

                _title = MakeLabel(title);
                _title.Font = Theme.Semibold(8.4f);
                _title.ForeColor = Theme.Muted;

                _enabled = new ToggleSwitch();
                _enabled.CheckedChanged += delegate
                {
                    if (_owner._binding || _cfg == null) return;
                    _cfg.Enabled = _enabled.Checked;
                    _owner.NotifyChanged();
                };
                _host.Controls.Add(_enabled);

                _size = MakeStepper(80, 400);
                _opacity = MakeStepper(10, 100);
                _size.ValueChanged += delegate
                {
                    if (_owner._binding || _cfg == null) return;
                    _cfg.Size = _size.Value;
                    _cfg.Normalize();
                    _owner.NotifyChanged();
                };
                _opacity.ValueChanged += delegate
                {
                    if (_owner._binding || _cfg == null) return;
                    _cfg.Opacity = _opacity.Value;
                    _cfg.Normalize();
                    _owner.NotifyChanged();
                };

                _sizeLabel = MakeLabel("圆盘大小");
                _opacityLabel = MakeLabel("不透明度");
                _host.Controls.Add(_title);
                _host.Controls.AddRange(new Control[] { _sizeLabel, _opacityLabel });
                _host.Controls.AddRange(new Control[] { _size, _opacity });
            }

            public void Bind(DpadConfig cfg)
            {
                _cfg = cfg;
                _enabled.SetCheckedSilent(cfg != null && cfg.Enabled);
                if (cfg == null) return;
                _size.SetValueSilent(cfg.Size);
                _opacity.SetValueSilent(cfg.Opacity);
            }

            public void Layout(int pad, int topY, int colW, int gap, int hostWidth, int groupH)
            {
                _host.SetBounds(0, topY, hostWidth, groupH);
                int labelY = Theme.S(20);
                int stepY = Theme.S(36);
                int stepH = Theme.S(34);
                _title.SetBounds(pad, Theme.S(0), colW * 2 - Theme.S(64), Theme.S(18));
                _enabled.SetBounds(hostWidth - pad - Theme.S(46), Theme.S(-1), Theme.S(46), Theme.S(24));

                _sizeLabel.SetBounds(pad, labelY, colW, Theme.S(16));
                _opacityLabel.SetBounds(pad + (colW + gap), labelY, colW, Theme.S(16));
                _size.SetBounds(pad, stepY, colW, stepH);
                _opacity.SetBounds(pad + (colW + gap), stepY, colW, stepH);
            }

            private StepperBox MakeStepper(int min, int max)
            {
                StepperBox box = new StepperBox();
                box.Minimum = min;
                box.Maximum = max;
                box.Step = 1;
                return box;
            }

            private Label MakeLabel(string text)
            {
                Label label = new Label();
                label.Text = text;
                label.AutoSize = false;
                label.BackColor = Theme.SurfaceAlt;
                label.ForeColor = Theme.Muted;
                label.Font = Theme.UiFont(8.1f, false);
                label.TextAlign = ContentAlignment.MiddleLeft;
                return label;
            }
        }
    }
}
