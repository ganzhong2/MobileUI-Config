using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MobileUiConfig
{
    // 全局震动反馈卡片：配置保存在 AppConfig.Rumble，所有虚拟按键按下共用一套节奏；
    // 经 XInput → Sunshine ViGEm 虚拟手柄 → 串流控制通道转发到客户端协同振动。
    public class RumbleSettingsPanel : Panel
    {
        private RumbleConfig _cfg;
        private bool _binding;
        private readonly System.Windows.Forms.Timer _statusTimer;

        private readonly ToggleSwitch _enabled;
        private readonly Label _sunshineState;
        private readonly Label _clientState;
        private readonly Label _slotState;
        private readonly Label _problemLabel;
        private readonly ModernButton _refreshButton;

        private readonly ModernSelect _presetSelect;
        private readonly StepperBox _leftStep;
        private readonly StepperBox _rightStep;
        private readonly StepperBox _durationStep;
        private readonly ModernButton _testPulseButton;

        private readonly ModernButton _heartbeatButton;
        private readonly ModernButton _alertButton;
        private readonly ModernButton _impactButton;
        private readonly ModernButton _machineButton;

        private readonly RoundTextBox _patternBox;
        private readonly ModernButton _testPatternButton;

        private readonly ModernSlider _holdSlider;
        private readonly Label _holdValue;
        private readonly ModernButton _holdButton;

        public event EventHandler Changed;

        public RumbleSettingsPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.SurfaceAlt;
            Height = Theme.S(490);

            _enabled = new ToggleSwitch();
            _enabled.CheckedChanged += delegate
            {
                if (_binding || _cfg == null) return;
                _cfg.Enabled = _enabled.Checked;
                UpdateEnabledState();
                RaiseChanged();
            };
            Controls.Add(_enabled);

            _sunshineState = MakeStatusLabel();
            _clientState = MakeStatusLabel();
            _slotState = MakeStatusLabel();
            Controls.Add(_sunshineState);
            Controls.Add(_clientState);
            Controls.Add(_slotState);

            _problemLabel = MakeLabel("");
            _problemLabel.ForeColor = Theme.Faint;
            _problemLabel.Font = Theme.UiFont(7.2f, false);
            _problemLabel.TextAlign = ContentAlignment.TopLeft;
            Controls.Add(_problemLabel);

            _refreshButton = new ModernButton();
            _refreshButton.Text = "立即刷新";
            _refreshButton.Style = ModernButtonStyle.Ghost;
            _refreshButton.Font = Theme.UiFont(7.6f, false);
            _refreshButton.Click += delegate { RefreshStatus(); };
            Controls.Add(_refreshButton);

            _presetSelect = new ModernSelect();
            _presetSelect.SelectionChanged += delegate
            {
                if (_binding || _cfg == null) return;
                int id;
                if (int.TryParse(_presetSelect.SelectedId, out id)) _cfg.Preset = id;
                UpdateEnabledState();
                RaiseChanged();
            };
            Controls.Add(_presetSelect);

            _leftStep = MakeStepper(0, 100);
            _rightStep = MakeStepper(0, 100);
            _durationStep = MakeStepper(50, 3000);
            _leftStep.ValueChanged += delegate
            {
                if (_binding || _cfg == null) return;
                _cfg.LeftMotor = _leftStep.Value;
                RaiseChanged();
            };
            _rightStep.ValueChanged += delegate
            {
                if (_binding || _cfg == null) return;
                _cfg.RightMotor = _rightStep.Value;
                RaiseChanged();
            };
            _durationStep.ValueChanged += delegate
            {
                if (_binding || _cfg == null) return;
                _cfg.DurationMs = _durationStep.Value;
                RaiseChanged();
            };
            Controls.Add(_leftStep);
            Controls.Add(_rightStep);
            Controls.Add(_durationStep);

            _testPulseButton = new ModernButton();
            _testPulseButton.Text = "震动";
            _testPulseButton.Style = ModernButtonStyle.Secondary;
            _testPulseButton.Font = Theme.UiFont(7.8f, false);
            _testPulseButton.Click += delegate
            {
                if (_cfg == null) return;
                RumbleService.Pulse(_cfg.DurationMs, _cfg.LeftMotor, _cfg.RightMotor);
            };
            Controls.Add(_testPulseButton);

            _heartbeatButton = MakePresetButton("心跳", 1);
            _alertButton = MakePresetButton("提醒·三短", 2);
            _impactButton = MakePresetButton("冲击·渐弱", 3);
            _machineButton = MakePresetButton("连发", 4);
            Controls.AddRange(new Control[] { _heartbeatButton, _alertButton, _impactButton, _machineButton });

            _patternBox = new RoundTextBox();
            _patternBox.Placeholder = "左:右:毫秒，逗号分段，0:0:毫秒=停顿；例：100:80:120,0:0:90,70:70:180";
            _patternBox.TextChanged += delegate
            {
                if (_binding || _cfg == null) return;
                _cfg.Pattern = _patternBox.Text;
                RaiseChanged();
            };
            Controls.Add(_patternBox);

            _testPatternButton = new ModernButton();
            _testPatternButton.Text = "播放";
            _testPatternButton.Style = ModernButtonStyle.Secondary;
            _testPatternButton.Font = Theme.UiFont(7.8f, false);
            _testPatternButton.Click += delegate
            {
                if (_cfg == null) return;
                List<RumbleSegment> segs = RumbleService.ParsePattern(_cfg.Pattern);
                if (segs != null) RumbleService.Pattern(segs);
            };
            Controls.Add(_testPatternButton);

            _holdSlider = new ModernSlider();
            _holdSlider.Minimum = 0;
            _holdSlider.Maximum = 100;
            _holdSlider.Value = 80;
            _holdSlider.ValueChanged += delegate
            {
                if (_binding || _cfg == null) return;
                _cfg.HoldStrength = _holdSlider.Value;
                _holdValue.Text = _holdSlider.Value.ToString() + "%";
                RaiseChanged();
            };
            Controls.Add(_holdSlider);

            _holdValue = MakeLabel("80%");
            _holdValue.Font = Theme.Semibold(8.4f);
            _holdValue.ForeColor = Theme.Mint;
            _holdValue.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_holdValue);

            _holdButton = new ModernButton();
            _holdButton.Text = "开始持续震动";
            _holdButton.Style = ModernButtonStyle.Secondary;
            _holdButton.Font = Theme.UiFont(7.8f, false);
            _holdButton.Click += delegate
            {
                if (_cfg == null) return;
                if (RumbleService.HoldActive)
                {
                    RumbleService.HoldStop();
                    _holdButton.Text = "开始持续震动";
                    _holdButton.Style = ModernButtonStyle.Secondary;
                }
                else
                {
                    RumbleService.HoldStart(_cfg.HoldStrength);
                    _holdButton.Text = "停止持续震动";
                    _holdButton.Style = ModernButtonStyle.Danger;
                }
                Invalidate();
            };
            Controls.Add(_holdButton);

            _statusTimer = new System.Windows.Forms.Timer();
            _statusTimer.Interval = 2000;
            _statusTimer.Tick += delegate { RefreshStatus(); };
            _statusTimer.Start();

            RefreshStatus();
            Resize += delegate { LayoutControls(); };
        }

        private ModernButton MakePresetButton(string text, int preset)
        {
            ModernButton b = new ModernButton();
            b.Text = text;
            b.Style = ModernButtonStyle.Ghost;
            b.Font = Theme.UiFont(7.6f, false);
            b.Click += delegate { RumbleService.Pattern(RumbleService.PresetSegments(preset)); };
            return b;
        }

        private StepperBox MakeStepper(int min, int max)
        {
            StepperBox box = new StepperBox();
            box.Minimum = min;
            box.Maximum = max;
            box.Step = 1;
            return box;
        }

        private Label MakeStatusLabel()
        {
            Label label = new Label();
            label.AutoSize = false;
            label.BackColor = Theme.SurfaceAlt;
            label.Font = Theme.UiFont(7.8f, false);
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
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

        public void SetConfig(RumbleConfig cfg)
        {
            _cfg = cfg;
            _binding = true;
            try
            {
                if (_cfg == null) return;
                _enabled.SetCheckedSilent(_cfg.Enabled);
                List<SelectOption> presets = new List<SelectOption>();
                presets.Add(new SelectOption("0", "单次脉冲"));
                presets.Add(new SelectOption("1", "心跳"));
                presets.Add(new SelectOption("2", "提醒·三短"));
                presets.Add(new SelectOption("3", "冲击·渐弱"));
                presets.Add(new SelectOption("4", "连发"));
                presets.Add(new SelectOption("5", "自定义节奏"));
                _presetSelect.SetItems(presets, _cfg.Preset.ToString(System.Globalization.CultureInfo.InvariantCulture));
                _leftStep.SetValueSilent(_cfg.LeftMotor);
                _rightStep.SetValueSilent(_cfg.RightMotor);
                _durationStep.SetValueSilent(_cfg.DurationMs);
                _patternBox.Text = _cfg.Pattern;
                _holdSlider.SetValueSilent(_cfg.HoldStrength);
                _holdValue.Text = _cfg.HoldStrength.ToString() + "%";
            }
            finally
            {
                _binding = false;
            }
            UpdateEnabledState();
            RefreshStatus();
            Invalidate();
        }

        private void UpdateEnabledState()
        {
            bool on = _cfg != null && _cfg.Enabled;
            _presetSelect.Enabled = on;
            _leftStep.Enabled = on;
            _rightStep.Enabled = on;
            _durationStep.Enabled = on;
            _testPulseButton.Enabled = on;
            _heartbeatButton.Enabled = on;
            _alertButton.Enabled = on;
            _impactButton.Enabled = on;
            _machineButton.Enabled = on;
            _patternBox.Enabled = on;
            _testPatternButton.Enabled = on;
            _holdSlider.Enabled = on;
            _holdButton.Enabled = on;
            _holdValue.ForeColor = on ? Theme.Mint : Theme.Faint;
        }

        private void RefreshStatus()
        {
            RumbleService.RefreshStatus();
            _sunshineState.ForeColor = RumbleService.SunshineRunning ? Theme.Mint : Theme.Danger;
            _sunshineState.Text = RumbleService.SunshineRunning ? "运行中" : "未运行";

            _clientState.ForeColor = RumbleService.ClientConnected ? Theme.Mint : Theme.Muted;
            _clientState.Text = RumbleService.ClientConnected
                ? "已连接"
                : "未连接";

            List<int> slots = RumbleService.ActiveSlots;
            if (slots.Count == 0)
            {
                _slotState.ForeColor = Theme.Danger;
                _slotState.Text = "无";
            }
            else
            {
                _slotState.ForeColor = Theme.Mint;
                string text = "通道 ";
                for (int i = 0; i < slots.Count; i++)
                {
                    if (i > 0) text += "、";
                    text += slots[i].ToString();
                }
                _slotState.Text = text;
            }
            _problemLabel.Text = RumbleService.Problem;
            Invalidate();
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
            if (_enabled == null) return;
            float s = Theme.Scale;
            int pad = Theme.S(16);
            int gap = Theme.S(10);
            int width = Math.Max(120, Width - pad * 2);

            _enabled.SetBounds(Width - pad - Theme.S(46), Theme.S(6), Theme.S(46), Theme.S(26));

            // 链路状态三行 + 刷新按钮。
            Label sunshineName = FindOrCreateLabel("sunshineName", "Sunshine 进程");
            sunshineName.ForeColor = Theme.Muted;
            sunshineName.SetBounds(pad, Theme.S(44), Theme.S(96), Theme.S(20));
            Label clientName = FindOrCreateLabel("clientName", "串流客户端");
            clientName.ForeColor = Theme.Muted;
            clientName.SetBounds(pad, Theme.S(66), Theme.S(96), Theme.S(20));
            Label slotName = FindOrCreateLabel("slotName", "XInput 通道");
            slotName.ForeColor = Theme.Muted;
            slotName.SetBounds(pad, Theme.S(88), Theme.S(96), Theme.S(20));
            _sunshineState.SetBounds(pad + Theme.S(100), Theme.S(44), Theme.S(120), Theme.S(20));
            _clientState.SetBounds(pad + Theme.S(100), Theme.S(66), Theme.S(180), Theme.S(20));
            _slotState.SetBounds(pad + Theme.S(100), Theme.S(88), Theme.S(180), Theme.S(20));
            _refreshButton.SetBounds(Width - pad - Theme.S(92), Theme.S(42), Theme.S(92), Theme.S(28));
            _problemLabel.SetBounds(pad, Theme.S(112), width, Theme.S(34));

            // 前置条件两行说明（静态配置口径，不随开关置灰）。
            Label preset1 = FindOrCreateLabel("prereq1", "Sunshine 控制器：虚拟手柄类型选 Xbox 360");
            preset1.ForeColor = Theme.Muted;
            preset1.Font = Theme.UiFont(7.2f, false);
            preset1.SetBounds(pad, Theme.S(148), width, Theme.S(18));
            Label preset2 = FindOrCreateLabel("prereq2", "手机 Moonlight：打开「虚拟手柄」；随便触摸一个虚拟按键唤醒 XInput 通道");
            preset2.ForeColor = Theme.Muted;
            preset2.Font = Theme.UiFont(7.2f, false);
            preset2.SetBounds(pad, Theme.S(168), width, Theme.S(18));

            // 触发节奏选择。
            Label presetLabel = FindOrCreateLabel("presetLabel", "触发节奏");
            presetLabel.SetBounds(pad, Theme.S(196), Theme.S(80), Theme.S(24));
            _presetSelect.SetBounds(pad + Theme.S(84), Theme.S(194), width - Theme.S(84), Theme.S(32));

            // 单次震动参数：左马达 / 右马达 / 时长 / 震动按钮。
            int colW = (width - gap * 3) / 4;
            Label leftLabel = FindOrCreateLabel("leftLabel", "左马达");
            Label rightLabel = FindOrCreateLabel("rightLabel", "右马达");
            Label durationLabel = FindOrCreateLabel("durationLabel", "时长");
            leftLabel.SetBounds(pad, Theme.S(240), colW, Theme.S(18));
            rightLabel.SetBounds(pad + (colW + gap), Theme.S(240), colW, Theme.S(18));
            durationLabel.SetBounds(pad + (colW + gap) * 2, Theme.S(240), colW, Theme.S(18));
            _leftStep.SetBounds(pad, Theme.S(260), colW, Theme.S(34));
            _rightStep.SetBounds(pad + (colW + gap), Theme.S(260), colW, Theme.S(34));
            _durationStep.SetBounds(pad + (colW + gap) * 2, Theme.S(260), colW, Theme.S(34));
            _testPulseButton.SetBounds(pad + (colW + gap) * 3, Theme.S(260), colW, Theme.S(34));

            // 预设试听四个按钮。
            int presetW = (width - gap * 3) / 4;
            _heartbeatButton.SetBounds(pad, Theme.S(306), presetW, Theme.S(32));
            _alertButton.SetBounds(pad + (presetW + gap), Theme.S(306), presetW, Theme.S(32));
            _impactButton.SetBounds(pad + (presetW + gap) * 2, Theme.S(306), presetW, Theme.S(32));
            _machineButton.SetBounds(pad + (presetW + gap) * 3, Theme.S(306), presetW, Theme.S(32));

            // 自定义节奏输入 + 播放。
            Label patternLabel = FindOrCreateLabel("patternLabel", "自定义节奏");
            patternLabel.SetBounds(pad, Theme.S(352), Theme.S(80), Theme.S(20));
            _patternBox.SetBounds(pad, Theme.S(374), width - Theme.S(96), Theme.S(36));
            _testPatternButton.SetBounds(Width - pad - Theme.S(84), Theme.S(374), Theme.S(84), Theme.S(36));
            Label patternHint = FindOrCreateLabel("patternHint", "格式：左:右:毫秒，逗号分段；0:0:毫秒 为停顿；两段写法 强度:毫秒 表示左右同强度。");
            patternHint.ForeColor = Theme.Faint;
            patternHint.Font = Theme.UiFont(7.0f, false);
            patternHint.SetBounds(pad, Theme.S(416), width, Theme.S(20));

            // 持续震动验证：强度滑块 + 数值 + 开始/停止。滑块右端到数值之间留空，
            // 数值标签位于按钮左侧，避免与按钮左半段互相压盖。
            Label holdLabel = FindOrCreateLabel("holdLabel", "持续震动验证");
            holdLabel.SetBounds(pad, Theme.S(446), Theme.S(96), Theme.S(24));
            int holdButtonX = Width - pad - Theme.S(128);
            int holdValueX = holdButtonX - Theme.S(60);
            _holdSlider.SetBounds(pad + Theme.S(100), Theme.S(450), holdValueX - (pad + Theme.S(100)) - Theme.S(8), Theme.S(28));
            _holdValue.SetBounds(holdValueX, Theme.S(446), Theme.S(52), Theme.S(24));
            _holdButton.SetBounds(holdButtonX, Theme.S(446), Theme.S(128), Theme.S(32));
        }

        private Label FindOrCreateLabel(string name, string text)
        {
            Label existing = Controls[name] as Label;
            if (existing != null) return existing;
            Label label = MakeLabel(text);
            label.Name = name;
            Controls.Add(label);
            return label;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRounded(e.Graphics, rect, Theme.S(13), BackColor);
            Theme.DrawRounded(e.Graphics, rect, Theme.S(13), Theme.StrokeSoft, 1f);
            Theme.FillRounded(e.Graphics, new RectangleF(Theme.S(14), Theme.S(7), Theme.S(3), Theme.S(17)),
                Theme.S(2), Theme.Mint);
            Theme.DrawText(e.Graphics, "震动反馈", Theme.Semibold(9.4f), Theme.Text,
                new RectangleF(Theme.S(24), Theme.S(4), Theme.S(180), Theme.S(24)), StringAlignment.Near, StringAlignment.Center, true);
            Theme.DrawText(e.Graphics, "全局生效 · 所有虚拟按键按下触发 · XInput 手柄震动", Theme.UiFont(7.2f, false), Theme.Faint,
                new RectangleF(Theme.S(104), Theme.S(4), Math.Max(40, Width - Theme.S(270)), Theme.S(24)),
                StringAlignment.Near, StringAlignment.Center, true);
            Theme.DrawText(e.Graphics, "启用", Theme.UiFont(8.1f, false), Theme.Muted,
                new RectangleF(Width - Theme.S(108), Theme.S(6), Theme.S(50), Theme.S(24)),
                StringAlignment.Far, StringAlignment.Center, false);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }
    }
}
