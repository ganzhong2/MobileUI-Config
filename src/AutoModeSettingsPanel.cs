using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace MobileUiConfig
{
    // 单横条：最左侧三个可点选的特征图槽，右侧为选择/截图/清除、开关与唯一的目标模式。
    public class AutoImageBar : Panel
    {
        private readonly PictureBox[] _thumbs = new PictureBox[3];
        private readonly Label[] _thumbsLabels = new Label[3];
        private readonly Image[] _images = new Image[3];
        private readonly ToggleSwitch _enabled;
        private readonly ModernButton _capture;
        private readonly ModernButton _clear;
        private readonly ModernButton _select;
        private readonly ModernSelect _target;
        private readonly Label _targetLabel;
        private readonly Label _barTitle;
        private readonly Label _barStatus;

        private AutoImageProject _project;
        private List<ModeConfig> _modes;
        private int _projectIndex;
        private int _activeSlot;
        private bool _binding;

        public event EventHandler Changed;
        public event Action<int> CaptureRequested;
        public event Action<int> SelectRequested;
        public event Action<int> ClearRequested;

        public AutoImageBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.SurfaceAlt;
            Height = Theme.S(124);

            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                PictureBox thumb = new PictureBox();
                thumb.BackColor = Theme.BgSoft;
                thumb.SizeMode = PictureBoxSizeMode.Zoom;
                thumb.Cursor = Cursors.Hand;
                thumb.Click += delegate { SelectSlot(slot); };
                thumb.Paint += delegate(object sender, PaintEventArgs pe)
                {
                    if (_activeSlot != slot) return;
                    using (Pen pen = new Pen(Theme.Accent, 2f))
                        pe.Graphics.DrawRectangle(pen, 1, 1, thumb.Width - 3, thumb.Height - 3);
                };
                _thumbs[i] = thumb;
                Controls.Add(thumb);

                Label label = MakeLabel(i == 0 ? "特征一" : (i == 1 ? "特征二" : "特征三"));
                label.Font = Theme.UiFont(7.0f, false);
                label.ForeColor = Theme.Faint;
                label.TextAlign = ContentAlignment.MiddleCenter;
                label.Cursor = Cursors.Hand;
                label.Click += delegate { SelectSlot(slot); };
                _thumbsLabels[i] = label;
                Controls.Add(label);
            }

            _barTitle = MakeLabel("特征图（可只设 1 张）");
            _barTitle.Font = Theme.Semibold(8.4f);
            _barTitle.ForeColor = Theme.TextSoft;
            Controls.Add(_barTitle);

            _barStatus = MakeLabel("未设置图片 · 点击缩略图选中后截图或选择");
            _barStatus.Font = Theme.UiFont(7.2f, false);
            _barStatus.ForeColor = Theme.Faint;
            _barStatus.TextAlign = ContentAlignment.TopLeft;
            Controls.Add(_barStatus);

            _enabled = new ToggleSwitch();
            _enabled.CheckedChanged += delegate
            {
                if (_binding || _project == null) return;
                _project.Enabled = _enabled.Checked;
                RaiseChanged();
            };
            Controls.Add(_enabled);

            _select = new ModernButton();
            _select.Text = "选择";
            _select.Style = ModernButtonStyle.Ghost;
            _select.Font = Theme.UiFont(7.6f, false);
            _select.Click += delegate { RaiseRequest(SelectRequested); };
            Controls.Add(_select);

            _capture = new ModernButton();
            _capture.Text = "截图";
            _capture.Style = ModernButtonStyle.Secondary;
            _capture.Font = Theme.UiFont(7.6f, false);
            _capture.Click += delegate { RaiseRequest(CaptureRequested); };
            Controls.Add(_capture);

            _clear = new ModernButton();
            _clear.Text = "清除";
            _clear.Style = ModernButtonStyle.Ghost;
            _clear.Font = Theme.UiFont(7.6f, false);
            _clear.Click += delegate { RaiseRequest(ClearRequested); };
            Controls.Add(_clear);

            _targetLabel = MakeLabel("识别到图片切换到");
            _targetLabel.Font = Theme.UiFont(7.6f, false);
            _targetLabel.ForeColor = Theme.Muted;
            _targetLabel.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_targetLabel);

            _target = new ModernSelect();
            _target.Height = Theme.S(28);
            _target.Font = Theme.UiFont(8.0f, false);
            _target.SelectionChanged += delegate
            {
                if (_binding || _project == null) return;
                int value;
                if (int.TryParse(_target.SelectedId, out value)) _project.TargetMode = value;
                RaiseChanged();
            };
            Controls.Add(_target);

            SelectSlot(0);
        }

        public int ActiveSlot { get { return _activeSlot; } }

        public void SetProject(int projectIndex, AutoImageProject project, List<ModeConfig> modes)
        {
            _projectIndex = Math.Max(0, projectIndex);
            _project = project;
            _modes = modes;
            _binding = true;
            try
            {
                _enabled.SetCheckedSilent(project != null && project.Enabled);
                List<SelectOption> options = new List<SelectOption>();
                if (modes != null)
                {
                    for (int i = 0; i < modes.Count; i++)
                        options.Add(new SelectOption(i.ToString(CultureInfo.InvariantCulture),
                            modes[i] == null ? "模式" : modes[i].Name));
                }
                int target = project == null ? 0 : project.TargetMode;
                if (options.Count > 0)
                {
                    if (target < 0 || target >= options.Count) target = options.Count - 1;
                    _target.SetItems(options, target.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    _target.SetItems(options, "");
                }
                for (int i = 0; i < 3; i++) LoadImage(i);
            }
            finally { _binding = false; }
            SelectSlot(Math.Min(_activeSlot, 2));
            UpdateStatus();
        }

        public void RefreshImage(int slot)
        {
            if (slot < 0 || slot > 2) return;
            LoadImage(slot);
            UpdateStatus();
        }

        public void RefreshImages()
        {
            for (int i = 0; i < 3; i++) LoadImage(i);
            UpdateStatus();
        }

        public void SetStatus(string text)
        {
            if (_barStatus != null && !string.IsNullOrEmpty(text)) _barStatus.Text = text;
        }

        private void LoadImage(int slot)
        {
            if (_images[slot] != null)
            {
                _thumbs[slot].Image = null;
                _images[slot].Dispose();
                _images[slot] = null;
            }
            try
            {
                AutoFeatureConfig feature = _project == null || _project.Features == null ||
                    slot >= _project.Features.Count ? null : _project.Features[slot];
                if (feature == null || string.IsNullOrEmpty(feature.ImageFile)) return;
                _images[slot] = ConfigStore.LoadFeatureImage(_projectIndex, slot);
                _thumbs[slot].Image = _images[slot];
            }
            catch { }
        }

        private void UpdateStatus()
        {
            int count = 0;
            if (_project != null && _project.Features != null)
            {
                for (int i = 0; i < _project.Features.Count && i < 3; i++)
                {
                    AutoFeatureConfig feature = _project.Features[i];
                    if (feature != null && !string.IsNullOrEmpty(feature.ImageFile) && _images[i] != null)
                        count++;
                }
            }
            if (_project == null) return;
            string rule = _project.CrossValidation
                ? "所有已设图片必须同时命中"
                : "任意一张命中即切换";
            if (count == 0) _barStatus.Text = "未设置图片 · 选中「特征一/二/三」后截图或选择";
            else _barStatus.Text = "已设置 " + count.ToString(CultureInfo.InvariantCulture) +
                " 张图片 · " + rule;
        }

        private void RaiseRequest(Action<int> handler)
        {
            if (_project == null) return;
            if (handler != null) handler(_activeSlot);
        }

        private void SelectSlot(int slot)
        {
            if (slot < 0 || slot > 2) return;
            _activeSlot = slot;
            for (int i = 0; i < 3; i++)
            {
                _thumbsLabels[i].ForeColor = i == slot ? Theme.Gold : Theme.Faint;
                _thumbs[i].Invalidate();
            }
        }

        private void RaiseChanged()
        {
            EventHandler h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }

        private Label MakeLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.BackColor = Color.Transparent;
            label.ForeColor = Theme.Muted;
            label.Font = Theme.UiFont(8.1f, false);
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            if (_thumbs[0] == null) return;
            int pad = Theme.S(10);
            int thumbW = Theme.S(62);
            int thumbH = Theme.S(48);
            int gap = Theme.S(8);
            for (int i = 0; i < 3; i++)
            {
                _thumbs[i].SetBounds(pad + i * (thumbW + gap), Theme.S(8), thumbW, thumbH);
                _thumbsLabels[i].SetBounds(pad + i * (thumbW + gap), Theme.S(58), thumbW, Theme.S(16));
            }

            int right = Width - pad;
            int toggleW = Theme.S(46);
            _enabled.SetBounds(right - toggleW, Theme.S(8), toggleW, Theme.S(24));

            int targetW = Theme.S(132);
            int targetLabelW = Theme.S(96);
            _target.SetBounds(right - targetW, Theme.S(42), targetW, Theme.S(28));
            _targetLabel.SetBounds(_target.Left - targetLabelW - Theme.S(4), Theme.S(44),
                targetLabelW, Theme.S(24));

            int btnY = Theme.S(80);
            int btnH = Theme.S(28);
            int btnW = Theme.S(46);
            _clear.SetBounds(right - btnW, btnY, btnW, btnH);
            _capture.SetBounds(_clear.Left - Theme.S(6) - btnW, btnY, btnW, btnH);
            _select.SetBounds(_capture.Left - Theme.S(6) - btnW, btnY, btnW, btnH);

            int textLeft = pad + 3 * thumbW + 2 * gap + Theme.S(14);
            int textRight = Math.Max(textLeft + Theme.S(80), _targetLabel.Left - Theme.S(10));
            _barTitle.SetBounds(textLeft, Theme.S(6), textRight - textLeft, Theme.S(20));
            _barStatus.SetBounds(textLeft, Theme.S(28), textRight - textLeft, Theme.S(44));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRounded(e.Graphics, rect, Theme.S(10), Color.FromArgb(22, 31, 47));
            Theme.DrawRounded(e.Graphics, rect, Theme.S(10), Theme.StrokeSoft, 1f);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (_images[i] != null)
                    {
                        _images[i].Dispose();
                        _images[i] = null;
                    }
                }
            }
            base.Dispose(disposing);
        }
    }

    public class AutoModeSettingsPanel : Panel
    {
        private AutoModeConfig _config;
        private AutoImageProject _project;
        private List<ModeConfig> _modes;
        private int _projectIndex;
        private bool _binding;

        private readonly AutoImageBar _bar;
        private readonly ModeTagStrip _modeStrip;
        private readonly ModernButton _randomButton;
        private readonly ModernButton _crossButton;
        private readonly KeyCaptureBox _hotkey;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly Label _hotkeyLabel;
        private readonly Label _hint;
        private readonly Label _status;
        private readonly Label _rule;
        private readonly Label _hitCountLabel;
        private readonly StepperBox _hitStepper;
        private readonly Label _intervalLabel;
        private readonly StepperBox _intervalStepper;
        private readonly Label _rateLabel;
        private readonly StepperBox _rateStepper;

        // 折叠态需保留可见的标题控件，交给 CardDock 判定。
        public Control DockTitleControl { get { return _title; } }

        public event EventHandler Changed;
        public event EventHandler HotkeyChanged;
        public event Action<int> CaptureRequested;
        public event Action<int> SelectRequested;
        public event Action<int> ModeSelectRequested;
        public event EventHandler AddModeRequested;
        public event EventHandler RenameModeRequested;
        public event EventHandler RemoveModeRequested;

        public AutoModeSettingsPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.SurfaceAlt;
            Height = Theme.S(372);

            _modeStrip = new ModeTagStrip();
            _modeStrip.ModeSelected += delegate(int index)
            {
                Action<int> h = ModeSelectRequested;
                if (h != null) h(index);
            };
            _modeStrip.AddRequested += delegate
            {
                EventHandler h = AddModeRequested;
                if (h != null) h(this, EventArgs.Empty);
            };
            _modeStrip.RenameRequested += delegate
            {
                EventHandler h = RenameModeRequested;
                if (h != null) h(this, EventArgs.Empty);
            };
            _modeStrip.RemoveRequested += delegate
            {
                EventHandler h = RemoveModeRequested;
                if (h != null) h(this, EventArgs.Empty);
            };
            Controls.Add(_modeStrip);

            _bar = new AutoImageBar();
            _bar.Changed += delegate { RaiseChanged(); };
            _bar.CaptureRequested += delegate(int slot)
            {
                Action<int> h = CaptureRequested;
                if (h != null) h(slot);
            };
            _bar.SelectRequested += delegate(int slot)
            {
                Action<int> h = SelectRequested;
                if (h != null) h(slot);
            };
            _bar.ClearRequested += delegate(int slot) { ClearFeature(slot); };
            Controls.Add(_bar);

            _randomButton = new ModernButton();
            _randomButton.Text = "随机";
            _randomButton.Style = ModernButtonStyle.Ghost;
            _randomButton.Font = Theme.UiFont(7.6f, false);
            _randomButton.Click += delegate
            {
                if (_binding || _project == null) return;
                _project.CrossValidation = false;
                _bar.RefreshImages();
                UpdateModeButtons();
                UpdateRuleText();
                RaiseChanged();
            };
            Controls.Add(_randomButton);

            _crossButton = new ModernButton();
            _crossButton.Text = "交叉";
            _crossButton.Style = ModernButtonStyle.Ghost;
            _crossButton.Font = Theme.UiFont(7.6f, false);
            _crossButton.Click += delegate
            {
                if (_binding || _project == null) return;
                _project.CrossValidation = true;
                _bar.RefreshImages();
                UpdateModeButtons();
                UpdateRuleText();
                RaiseChanged();
            };
            Controls.Add(_crossButton);

            _hotkey = new KeyCaptureBox();
            _hotkey.BindingChanged += delegate
            {
                if (_binding || _config == null) return;
                _config.Hotkey = _hotkey.Binding.Clone();
                if (_config.Hotkey.Kind != BindingKind.Keyboard)
                {
                    _config.Hotkey = new KeyBinding(BindingKind.Keyboard, (int)Keys.F8);
                    _hotkey.SetBindingSilent(_config.Hotkey.Clone());
                    SetStatus("截图快捷键仅支持键盘，已恢复 F8");
                }
                else
                {
                    SetStatus("快捷键已更新为 " + _config.Hotkey.DisplayName);
                }
                EventHandler h = HotkeyChanged;
                if (h != null) h(this, EventArgs.Empty);
                RaiseChanged();
            };
            Controls.Add(_hotkey);

            _title = MakeLabel("自动切换 · 图片识别");
            _title.Font = Theme.Semibold(9.4f);
            _title.ForeColor = Theme.Text;
            _subtitle = MakeLabel("每个项目独立配置：识别到特征图后，切换到界面预览的所选模式");
            _subtitle.Font = Theme.UiFont(7.3f, false);
            _subtitle.ForeColor = Theme.Faint;
            _hotkeyLabel = MakeLabel("截图快捷键");
            _hotkeyLabel.Font = Theme.UiFont(8.1f, false);
            _hotkeyLabel.ForeColor = Theme.Muted;
            _hint = MakeLabel("快捷键在设置隐藏/最小化后生效；建议框选 60–320 px，截图保存到软件同目录");
            _hint.Font = Theme.UiFont(7.2f, false);
            _hint.ForeColor = Theme.Faint;
            _status = MakeLabel("");
            _status.Font = Theme.UiFont(7.2f, false);
            _status.ForeColor = Theme.Mint;
            _rule = MakeLabel("");
            _rule.Font = Theme.UiFont(7.0f, false);
            _rule.ForeColor = Theme.Muted;
            _rule.TextAlign = ContentAlignment.TopLeft;
            _rule.AutoSize = false;
            _rule.AutoEllipsis = true;

            _hitCountLabel = MakeLabel("命中次数");
            _hitCountLabel.Font = Theme.UiFont(7.5f, false);
            _hitCountLabel.ForeColor = Theme.TextSoft;
            _hitStepper = new StepperBox();
            _hitStepper.Minimum = 1; _hitStepper.Maximum = 5; _hitStepper.Step = 1;
            _hitStepper.Size = new Size(108, 28);
            _hitStepper.ValueChanged += delegate
            {
                if (_project != null) { _project.HitCount = _hitStepper.Value; RaiseChanged(); }
            };

            _intervalLabel = MakeLabel("扫描间隔");
            _intervalLabel.Font = Theme.UiFont(7.5f, false);
            _intervalLabel.ForeColor = Theme.TextSoft;
            _intervalStepper = new StepperBox();
            _intervalStepper.Minimum = 200; _intervalStepper.Maximum = 3000; _intervalStepper.Step = 100;
            _intervalStepper.Size = new Size(136, 28);
            _intervalStepper.ValueChanged += delegate
            {
                if (_project != null) { _project.IntervalMs = _intervalStepper.Value; RaiseChanged(); }
            };

            // 命中率：内部按 100 倍存储，显示为两位小数，每次加减 0.05
            _rateLabel = MakeLabel("命中率");
            _rateLabel.Font = Theme.UiFont(7.5f, false);
            _rateLabel.ForeColor = Theme.TextSoft;
            _rateStepper = new StepperBox();
            _rateStepper.Divisor = 100;
            _rateStepper.Minimum = 50; _rateStepper.Maximum = 100; _rateStepper.Step = 5;
            _rateStepper.Size = new Size(108, 28);
            _rateStepper.ValueChanged += delegate
            {
                if (_binding || _project == null) return;
                _project.Threshold = (float)_rateStepper.DecimalValue;
                RaiseChanged();
            };

            Controls.AddRange(new Control[] { _title, _subtitle, _hotkeyLabel, _hint, _status,
                _rule, _hitCountLabel, _hitStepper, _intervalLabel, _intervalStepper,
                _rateLabel, _rateStepper });
        }

        public int ActiveSlot { get { return _bar.ActiveSlot; } }

        public void SetConfig(AppConfig config)
        {
            _config = config == null ? null : config.AutoMode;
            _modes = config == null ? null : config.Modes;
            _projectIndex = config == null ? 0 : config.SelectedImageProject;
            _project = config == null ? null : config.ActiveImageProject;
            _binding = true;
            try
            {
                if (_config == null) return;
                _hotkey.SetBindingSilent(_config.Hotkey == null
                    ? new KeyBinding(BindingKind.Keyboard, (int)Keys.F8)
                    : _config.Hotkey.Clone());
                List<string> names = new List<string>();
                if (config != null && config.ImageProjects != null)
                    for (int i = 0; i < config.ImageProjects.Count; i++)
                        names.Add(config.ImageProjects[i] == null ? "项目" : config.ImageProjects[i].Name);
                _modeStrip.SetModes(names, _projectIndex);
                _bar.SetProject(_projectIndex, _project, _modes);
                UpdateModeButtons();
                UpdateRuleText();
                if (_project != null)
                {
                    _hitStepper.Value = Math.Max(1, Math.Min(5, _project.HitCount));
                    _intervalStepper.Value = Math.Max(200, Math.Min(3000, _project.IntervalMs));
                    _rateStepper.DecimalValue = Math.Max(0.5, Math.Min(1.0, _project.Threshold));
                }
            }
            finally
            {
                _binding = false;
            }
            UpdateEnabledState();
            Invalidate();
        }

        public void RefreshImages()
        {
            _bar.RefreshImages();
            UpdateRuleText();
        }

        public void SetStatus(string text)
        {
            if (_status == null) return;
            if (text != null) _status.Text = text;
        }

        private void ClearFeature(int slot)
        {
            if (_project == null || _project.Features == null || slot < 0 || slot >= _project.Features.Count)
                return;
            AutoFeatureConfig feature = _project.Features[slot];
            feature.Enabled = false;
            feature.ImageFile = "";
            feature.CaptureWidth = 0;
            feature.CaptureHeight = 0;
            try
            {
                string path = ConfigStore.FeaturePath(_projectIndex, slot);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
            _bar.RefreshImages();
            SetStatus("已清除特征" + ModeConfig.ChineseNumber(slot + 1));
            RaiseChanged();
        }

        private Label MakeLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.BackColor = Color.Transparent;
            label.ForeColor = Theme.Muted;
            label.Font = Theme.UiFont(8.1f, false);
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private void UpdateEnabledState()
        {
            bool on = _project != null && _project.Enabled;
            _hotkey.Enabled = _config != null;
            _title.ForeColor = on ? Theme.Text : Theme.TextSoft;
            _subtitle.ForeColor = on ? Theme.Faint : Theme.Alpha(Theme.Faint, 80);
            _hotkeyLabel.ForeColor = on ? Theme.Muted : Theme.Faint;
            _hint.ForeColor = on ? Theme.Faint : Theme.Alpha(Theme.Faint, 80);
            Invalidate();
        }

        private void UpdateModeButtons()
        {
            if (_randomButton == null || _crossButton == null) return;
            bool cross = _project != null && _project.CrossValidation;
            _randomButton.Style = cross ? ModernButtonStyle.Ghost : ModernButtonStyle.Primary;
            _crossButton.Style = cross ? ModernButtonStyle.Primary : ModernButtonStyle.Ghost;
            _randomButton.Invalidate();
            _crossButton.Invalidate();
        }

        private void UpdateRuleText()
        {
            if (_rule == null || _project == null) return;
            _rule.Text = _project.CrossValidation
                ? "识别规则：交叉模式下，已设置的特征图必须同时被识别到才切换（设 2 张就需同时命中 2 张，设 3 张就需同时命中 3 张）；只设 1 张时命中即切换。"
                : "识别规则：随机模式下，任意一张已设置的特征图被识别到就立即切换到目标模式；只设 1 张时命中即切换。";
        }

        private void RaiseChanged()
        {
            UpdateEnabledState();
            UpdateRuleText();
            Invalidate();
            EventHandler h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // 始终重排标题等头部控件；折叠态由 LayoutControls 内部判断不撑高自身高度。
            LayoutControls();
        }

        private void LayoutControls()
        {
            if (_bar == null) return;
            int pad = Theme.S(14);
            int rowW = Math.Max(100, Width - pad * 2);
            _title.SetBounds(Theme.S(36), 0, Math.Max(80, rowW - Theme.S(22)), Theme.S(36));
            _modeStrip.SetBounds(pad, Theme.S(24), rowW, Theme.S(38));
            _subtitle.SetBounds(pad, Theme.S(66), Math.Max(80, rowW), Theme.S(18));
            _bar.SetBounds(pad, Theme.S(88), rowW, Theme.S(124));
            int y = Theme.S(218);
            int buttonW = Theme.S(54);
            _randomButton.SetBounds(pad, y, buttonW, Theme.S(26));
            _crossButton.SetBounds(pad + buttonW + Theme.S(6), y, buttonW, Theme.S(26));
            // 命中次数 + 扫描间隔 + 命中率 同一排
            int ySettings = y + Theme.S(30);

            int xCur = pad;
            _hitCountLabel.SetBounds(xCur, ySettings + Theme.S(2), Theme.S(62), Theme.S(24));
            xCur += Theme.S(64);
            _hitStepper.SetBounds(xCur, ySettings, Theme.S(94), Theme.S(28));
            xCur += Theme.S(102);
            _intervalLabel.SetBounds(xCur, ySettings + Theme.S(2), Theme.S(62), Theme.S(24));
            xCur += Theme.S(64);
            _intervalStepper.SetBounds(xCur, ySettings, Theme.S(118), Theme.S(28));
            xCur += Theme.S(126);
            _rateLabel.SetBounds(xCur, ySettings + Theme.S(2), Theme.S(44), Theme.S(24));
            xCur += Theme.S(46);
            _rateStepper.SetBounds(xCur, ySettings, Theme.S(112), Theme.S(28));

            _rule.SetBounds(pad, ySettings + Theme.S(28), rowW, Theme.S(42));
            // hotkey 区域接在 rule 下方，用 rule 的实际底部算 y
            int yAfterRule = ySettings + Theme.S(28) + Theme.S(42) + Theme.S(6);
            _hotkeyLabel.SetBounds(pad, yAfterRule + Theme.S(2), Theme.S(78), Theme.S(30));
            _hotkey.SetBounds(pad + Theme.S(82), yAfterRule,
                Math.Min(Theme.S(190), Math.Max(Theme.S(100), rowW - Theme.S(90))), Theme.S(32));
            _hint.SetBounds(pad, yAfterRule + Theme.S(36), rowW, Theme.S(18));
            _status.SetBounds(pad, yAfterRule + Theme.S(52), rowW, Theme.S(16));
            // 面板总高度跟着内容调整；折叠态保持单行标题栏高度，不按内容撑高。
            if (Height >= Theme.S(48))
            {
                Height = Math.Max(Height, yAfterRule + Theme.S(72));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRounded(e.Graphics, rect, Theme.S(13), BackColor);
            Theme.DrawRounded(e.Graphics, rect, Theme.S(13), Theme.StrokeSoft, 1f);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }
    }
}
