using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MobileUiConfig
{
    public class MouseLookSettingsPanel : Panel
    {
        private MouseLookConfig _look;
        private bool _binding;

        private readonly ToggleSwitch _enabled;
        private readonly StepperBox _width;
        private readonly StepperBox _height;
        private readonly StepperBox _opacity;
        private readonly StepperBox _sensitivity;
        private readonly ModernSlider _sensitivitySlider;
        private readonly Label _sensitivityValue;
        private readonly ModernSelect _shape;
        private readonly ModernSelect _button;
        private readonly Label _widthLabel;
        private readonly Label _heightLabel;
        private readonly Label _opacityLabel;
        private readonly Label _sensitivityLabel;
        private readonly Label _shapeLabel;
        private readonly Label _buttonLabel;
        private readonly Label _messageLabel;
        private readonly ToggleSwitch _messageToggle;
        private readonly Label _hint;
        private readonly Label _modeLabel;
        private readonly ModeVisibilityStrip _modeStrip;

        public event EventHandler Changed;

        public MouseLookSettingsPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.SurfaceAlt;
            Height = Theme.S(240);

            _enabled = new ToggleSwitch();
            _enabled.CheckedChanged += delegate
            {
                if (_binding || _look == null) return;
                _look.Enabled = _enabled.Checked;
                UpdateEnabledState();
                RaiseChanged();
            };
            Controls.Add(_enabled);

            _width = MakeStepper(60, 900);
            _height = MakeStepper(60, 900);
            _opacity = MakeStepper(10, 100);
            _sensitivity = MakeStepper(100, 2000);
            _sensitivity.Visible = false;
            _sensitivitySlider = new ModernSlider();
            _sensitivitySlider.Minimum = 100;
            _sensitivitySlider.Maximum = 2000;
            _sensitivitySlider.Value = 100;
            _sensitivityValue = MakeLabel("1.0x");
            _sensitivityValue.Font = Theme.Semibold(8.4f);
            _sensitivityValue.ForeColor = Theme.Mint;
            _sensitivityValue.TextAlign = ContentAlignment.MiddleRight;
            _sensitivitySlider.ValueChanged += delegate
            {
                if (_binding || _look == null) return;
                _look.Sensitivity = _sensitivitySlider.Value;
                _sensitivity.SetValueSilent(_look.Sensitivity);
                _sensitivitySlider.SetValueSilent(_look.Sensitivity);
                _sensitivityValue.Text = (Math.Max(1f, Math.Min(20f, _look.Sensitivity / 100f))).ToString("0.0") + "x";
                _sensitivityValue.Text = (Math.Max(1f, Math.Min(20f, _look.Sensitivity / 100f))).ToString("0.0") + "x";
                RaiseChanged();
            };
            Controls.Add(_sensitivitySlider);
            Controls.Add(_sensitivityValue);
            _width.ValueChanged += delegate
            {
                if (_binding || _look == null) return;
                _look.Width = _width.Value;
                if (_look.Shape != LookShape.Rectangle) _look.Height = _look.Width;
                NormalizeAndSync();
                RaiseChanged();
            };
            _height.ValueChanged += delegate
            {
                if (_binding || _look == null) return;
                _look.Height = _height.Value;
                if (_look.Shape != LookShape.Rectangle) _look.Width = _look.Height;
                NormalizeAndSync();
                RaiseChanged();
            };
            _opacity.ValueChanged += delegate
            {
                if (_binding || _look == null) return;
                _look.Opacity = _opacity.Value;
                RaiseChanged();
            };
            _sensitivity.ValueChanged += delegate
            {
                if (_binding || _look == null) return;
                _look.Sensitivity = _sensitivity.Value;
                RaiseChanged();
            };

            _shape = new ModernSelect();
            _shape.SelectionChanged += delegate
            {
                if (_binding || _look == null) return;
                int value;
                if (!int.TryParse(_shape.SelectedId, out value)) value = 0;
                _look.Shape = (LookShape)value;
                _look.Normalize();
                NormalizeAndSync();
                RaiseChanged();
            };
            Controls.Add(_shape);

            _button = new ModernSelect();
            _button.SelectionChanged += delegate
            {
                if (_binding || _look == null) return;
                int value;
                if (!int.TryParse(_button.SelectedId, out value)) value = 1;
                _look.Button = value < 0 ? new KeyBinding(BindingKind.None, 0) : new KeyBinding(BindingKind.MouseButton, value);
                RaiseChanged();
            };
            Controls.Add(_button);

            _widthLabel = MakeLabel("宽度");
            _heightLabel = MakeLabel("高度");
            _opacityLabel = MakeLabel("不透明度");
            _sensitivityLabel = MakeLabel("灵敏度");
            _shapeLabel = MakeLabel("形状");
            _buttonLabel = MakeLabel("鼠标键");
            _messageLabel = MakeLabel("消息注入（全屏兼容）");
            _messageLabel.Font = Theme.UiFont(7.8f, false);
            _messageLabel.ForeColor = Theme.Muted;
            _messageToggle = new ToggleSwitch();
            _messageToggle.CheckedChanged += delegate
            {
                if (_binding || _look == null) return;
                _look.MessageInjection = _messageToggle.Checked;
                RaiseChanged();
            };
            Controls.Add(_messageLabel);
            Controls.Add(_messageToggle);
            _hint = MakeLabel("长按后在屏幕上滑动，模拟鼠标拖动视角");
            _hint.ForeColor = Theme.Faint;
            _hint.Font = Theme.UiFont(7.2f, false);

            _modeLabel = MakeLabel("显示模式");
            _modeLabel.ForeColor = Theme.Faint;
            _modeLabel.Font = Theme.UiFont(7.8f, false);
            _modeStrip = new ModeVisibilityStrip();
            _modeStrip.Accent = Theme.Pink;
            _modeStrip.Toggled += delegate { RaiseChanged(); };

            Controls.Add(_modeLabel);
            Controls.Add(_modeStrip);
            Controls.Add(_width);
            Controls.Add(_height);
            Controls.Add(_opacity);
            Controls.Add(_sensitivity);
            Controls.AddRange(new Control[] { _widthLabel, _heightLabel, _opacityLabel, _sensitivityLabel,
                _shapeLabel, _buttonLabel, _hint });
        }

        public void SetModes(List<ModeConfig> modes)
        {
            if (_modeStrip != null) _modeStrip.SetModes(modes, false);
        }

        public void SetConfig(MouseLookConfig look)
        {
            _look = look;
            _binding = true;
            try
            {
                if (_look == null) return;
                _enabled.SetCheckedSilent(_look.Enabled);
                _width.SetValueSilent(_look.Width);
                _height.SetValueSilent(_look.Height);
                _opacity.SetValueSilent(_look.Opacity);
                _sensitivity.SetValueSilent(_look.Sensitivity);
                _sensitivitySlider.SetValueSilent(_look.Sensitivity);
                _sensitivityValue.Text = (Math.Max(1f, Math.Min(20f, _look.Sensitivity / 100f))).ToString("0.0") + "x";
                List<SelectOption> shapes = new List<SelectOption>();
                shapes.Add(new SelectOption("0", "圆形"));
                shapes.Add(new SelectOption("1", "方形"));
                shapes.Add(new SelectOption("2", "长方形"));
                _shape.SetItems(shapes, ((int)_look.Shape).ToString());
                List<SelectOption> buttons = new List<SelectOption>();
                buttons.Add(new SelectOption("-1", "无"));
                buttons.Add(new SelectOption("0", "鼠标左键"));
                buttons.Add(new SelectOption("1", "鼠标右键"));
                buttons.Add(new SelectOption("2", "鼠标中键"));
                buttons.Add(new SelectOption("3", "侧键 1"));
                buttons.Add(new SelectOption("4", "侧键 2"));
                int code = _look.Button == null ? 1 : (_look.Button.Kind == BindingKind.MouseButton ? _look.Button.Code : -1);
                _button.SetItems(buttons, code.ToString());
                _messageToggle.SetCheckedSilent(_look.MessageInjection);
            }
            finally
            {
                _binding = false;
            }
            UpdateEnabledState();
            Invalidate();
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

        private void NormalizeAndSync()
        {
            if (_look == null) return;
            _look.Normalize();
            _binding = true;
            try
            {
                _width.SetValueSilent(_look.Width);
                _height.SetValueSilent(_look.Height);
                _opacity.SetValueSilent(_look.Opacity);
                _sensitivity.SetValueSilent(_look.Sensitivity);
                _sensitivitySlider.SetValueSilent(_look.Sensitivity);
                _sensitivityValue.Text = (Math.Max(1f, Math.Min(20f, _look.Sensitivity / 100f))).ToString("0.0") + "x";
            }
            finally
            {
                _binding = false;
            }
            UpdateEnabledState();
            Invalidate();
        }

        private void UpdateEnabledState()
        {
            bool on = _look != null && _look.Enabled;
            _width.Enabled = on;
            _height.Enabled = on && _look != null && _look.Shape == LookShape.Rectangle;
            _opacity.Enabled = on;
            _sensitivity.Enabled = on;
            _sensitivitySlider.Enabled = on;
            _sensitivityValue.ForeColor = on ? Theme.Mint : Theme.Faint;
            _shape.Enabled = on;
            _button.Enabled = on;
            _messageToggle.Enabled = on;
            _messageLabel.ForeColor = on ? Theme.Muted : Theme.Faint;
            _widthLabel.ForeColor = on ? Theme.Muted : Theme.Faint;
            _heightLabel.ForeColor = on && _height.Enabled ? Theme.Muted : Theme.Faint;
            _opacityLabel.ForeColor = on ? Theme.Muted : Theme.Faint;
            _sensitivityLabel.ForeColor = on ? Theme.Muted : Theme.Faint;
            _shapeLabel.ForeColor = on ? Theme.Muted : Theme.Faint;
            _buttonLabel.ForeColor = on ? Theme.Muted : Theme.Faint;
            _hint.ForeColor = on ? Theme.Faint : Theme.Alpha(Theme.Faint, 80);
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
            LayoutControls();
        }

        private void LayoutControls()
        {
            if (_enabled == null || _width == null || _height == null || _opacity == null || _sensitivity == null ||
                _shape == null || _button == null || _widthLabel == null || _heightLabel == null ||
                _opacityLabel == null || _sensitivityLabel == null || _shapeLabel == null || _buttonLabel == null ||
                _sensitivitySlider == null || _sensitivityValue == null ||
                _hint == null || _modeLabel == null || _modeStrip == null ||
                _messageLabel == null || _messageToggle == null) return;

            int pad = Theme.S(16);
            int gap = Theme.S(10);
            int colW = Math.Max(Theme.S(80), (Width - pad * 2 - gap * 3) / 4);
            int labelY = Theme.S(32);
            int stepY = Theme.S(48);
            int stepH = Theme.S(34);
            _enabled.SetBounds(Width - pad - Theme.S(84), Theme.S(6), Theme.S(46), Theme.S(26));

            _widthLabel.SetBounds(pad, labelY, colW, Theme.S(16));
            _heightLabel.SetBounds(pad + (colW + gap), labelY, colW, Theme.S(16));
            _shapeLabel.SetBounds(pad + (colW + gap) * 2, labelY, colW, Theme.S(16));
            _buttonLabel.SetBounds(pad + (colW + gap) * 3, labelY, colW, Theme.S(16));
            _width.SetBounds(pad, stepY, colW, stepH);
            _height.SetBounds(pad + (colW + gap), stepY, colW, stepH);
            _shape.SetBounds(pad + (colW + gap) * 2, stepY, colW, stepH);
            _button.SetBounds(pad + (colW + gap) * 3, stepY, colW, stepH);

            int label2Y = Theme.S(94);
            _opacityLabel.SetBounds(pad, label2Y, colW, Theme.S(16));
            _sensitivityLabel.SetBounds(pad + (colW + gap), label2Y, colW, Theme.S(16));
            _opacity.SetBounds(pad, Theme.S(110), colW, stepH);
            _sensitivity.SetBounds(pad + (colW + gap), Theme.S(110), colW, stepH);
            _sensitivitySlider.SetBounds(pad + (colW + gap), Theme.S(112), colW, Theme.S(30));
            _sensitivityValue.SetBounds(pad + (colW + gap) + colW - Theme.S(58), Theme.S(94), Theme.S(58), Theme.S(16));
            _hint.SetBounds(pad + (colW + gap) * 2, Theme.S(102), colW * 2 + gap, Theme.S(38));

            _modeLabel.SetBounds(pad, Theme.S(151), Theme.S(68), Theme.S(30));
            _modeStrip.SetBounds(pad + Theme.S(72), Theme.S(150), Math.Max(80, Width - pad * 2 - Theme.S(72)), Theme.S(32));
            _messageLabel.SetBounds(pad, Theme.S(195), Theme.S(168), Theme.S(30));
            _messageToggle.SetBounds(pad + Theme.S(172), Theme.S(197), Theme.S(46), Theme.S(24));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRounded(e.Graphics, rect, Theme.S(13), BackColor);
            Theme.DrawRounded(e.Graphics, rect, Theme.S(13), Theme.StrokeSoft, 1f);
            bool collapsed = Height < Theme.S(48);
            int titleY = collapsed ? Theme.S(6) : Theme.S(4);
            Theme.DrawText(e.Graphics, "视角操控", Theme.Semibold(9.4f), Theme.Text,
                new RectangleF(Theme.S(34), titleY, Theme.S(180), Theme.S(24)), StringAlignment.Near, StringAlignment.Center, true);
            if (!collapsed)
            {
                Theme.DrawText(e.Graphics, "模拟鼠标拖动视角 · 支持圆形 / 方形 / 长方形", Theme.UiFont(7.2f, false), Theme.Faint,
                    new RectangleF(Theme.S(118), Theme.S(4), Math.Max(40, Width - Theme.S(270)), Theme.S(24)),
                    StringAlignment.Near, StringAlignment.Center, true);
                Theme.DrawText(e.Graphics, "启用", Theme.UiFont(8.1f, false), Theme.Muted,
                    new RectangleF(Width - Theme.S(108), Theme.S(6), Theme.S(50), Theme.S(24)),
                    StringAlignment.Far, StringAlignment.Center, false);
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }
    }
}
