using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace MobileUiConfig
{
    public class StorySkipRow : Panel
    {
        private readonly int _index;
        private readonly ToggleSwitch _enabled;
        private readonly PictureBox[] _thumbs = new PictureBox[3];
        private readonly Label[] _imageLabels = new Label[3];
        private readonly Label _title;
        private readonly Label _status;
        private readonly ModernButton _capture;
        private readonly ModernButton _clear;
        private readonly ModernButton _select;
        private StorySkipFeatureConfig _feature;
        private readonly Image[] _images = new Image[3];
        private bool _selected;
        private int _selectedImage;

        public event EventHandler Changed;
        public event Action<int> CaptureRequested;
        public event Action<int> ClearRequested;
        public event Action<int> SelectRequested;
        public event EventHandler SelectedChanged;

        public StorySkipRow(int index)
        {
            _index = index;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.SurfaceAlt;
            Height = Theme.S(76);

            for (int i = 0; i < 3; i++)
            {
                int imageIndex = i;
                PictureBox thumb = new PictureBox();
                thumb.BackColor = Theme.BgSoft;
                thumb.SizeMode = PictureBoxSizeMode.Zoom;
                thumb.Cursor = Cursors.Hand;
                thumb.Click += delegate
                {
                    _selectedImage = imageIndex;
                    UpdateImageSelection();
                    SelectMe();
                };
                thumb.Paint += delegate(object sender, PaintEventArgs pe)
                {
                    if (_selectedImage != imageIndex) return;
                    using (Pen pen = new Pen(Theme.Accent, 2f))
                        pe.Graphics.DrawRectangle(pen, 1, 1, thumb.Width - 3, thumb.Height - 3);
                };
                _thumbs[i] = thumb;
                Controls.Add(thumb);

                Label label = MakeLabel(i == 0 ? "图片一" : (i == 1 ? "图片二" : "图片三"));
                label.Font = Theme.UiFont(6.6f, false);
                label.ForeColor = Theme.Faint;
                label.TextAlign = ContentAlignment.MiddleCenter;
                label.Cursor = Cursors.Hand;
                label.Click += delegate
                {
                    _selectedImage = imageIndex;
                    UpdateImageSelection();
                    SelectMe();
                };
                _imageLabels[i] = label;
                Controls.Add(label);
            }

            _title = MakeLabel("特征 " + (index + 1));
            _title.Font = Theme.Semibold(8.6f);
            _title.ForeColor = Theme.TextSoft;
            _title.Click += delegate { SelectMe(); };
            Controls.Add(_title);

            _status = MakeLabel("未截图");
            _status.ForeColor = Theme.Faint;
            _status.Font = Theme.UiFont(7.2f, false);
            Controls.Add(_status);

            _enabled = new ToggleSwitch();
            _enabled.CheckedChanged += delegate
            {
                if (_feature == null) return;
                _feature.Enabled = _enabled.Checked;
                RaiseChanged();
            };
            Controls.Add(_enabled);

            _capture = new ModernButton();
            _capture.Text = "截图";
            _capture.Style = ModernButtonStyle.Secondary;
            _capture.Height = Theme.S(27);
            _capture.Font = Theme.UiFont(7.8f, false);
            _capture.Click += delegate
            {
                SelectMe();
                Action<int> h = CaptureRequested;
                if (h != null) h(_selectedImage);
            };
            Controls.Add(_capture);

            _clear = new ModernButton();
            _clear.Text = "清除";
            _clear.Style = ModernButtonStyle.Ghost;
            _clear.Height = Theme.S(27);
            _clear.Font = Theme.UiFont(7.6f, false);
            _clear.Click += delegate
            {
                SelectMe();
                Action<int> h = ClearRequested;
                if (h != null) h(_selectedImage);
            };
            Controls.Add(_clear);

            _select = new ModernButton();
            _select.Text = "选择";
            _select.Style = ModernButtonStyle.Ghost;
            _select.Height = Theme.S(27);
            _select.Font = Theme.UiFont(7.6f, false);
            _select.Click += delegate
            {
                SelectMe();
                Action<int> h = SelectRequested;
                if (h != null) h(_selectedImage);
            };
            Controls.Add(_select);
        }

        public int SlotIndex { get { return _index; } }
        public StorySkipFeatureConfig Feature { get { return _feature; } }
        public bool Selected { get { return _selected; } set { if (_selected != value) { _selected = value; Invalidate(); } } }

        public void SetFeature(StorySkipFeatureConfig feature)
        {
            _feature = feature;
            _enabled.SetCheckedSilent(feature != null && feature.Enabled);
            for (int i = 0; i < 3; i++)
            {
                if (_images[i] != null)
                {
                    _thumbs[i].Image = null;
                    _images[i].Dispose();
                    _images[i] = null;
                }
                try
                {
                    bool configured = feature != null && feature.Images != null &&
                                      i < feature.Images.Count &&
                                      !string.IsNullOrEmpty(feature.Images[i].ImageFile);
                    string path = ConfigStore.StorySkipPath(_index, i);
                    if (configured && File.Exists(path))
                    {
                        using (Image source = Image.FromFile(path)) _images[i] = new Bitmap(source);
                        _thumbs[i].Image = _images[i];
                    }
                }
                catch { }
            }
            UpdateStatus();
            UpdateImageSelection();
        }

        public void RefreshImage(int imageIndex)
        {
            if (imageIndex < 0 || imageIndex >= 3) return;
            if (_images[imageIndex] != null)
            {
                _thumbs[imageIndex].Image = null;
                _images[imageIndex].Dispose();
                _images[imageIndex] = null;
            }
            try
            {
                bool configured = _feature != null && _feature.Images != null &&
                                  imageIndex < _feature.Images.Count &&
                                  !string.IsNullOrEmpty(_feature.Images[imageIndex].ImageFile);
                string path = ConfigStore.StorySkipPath(_index, imageIndex);
                if (configured && File.Exists(path))
                {
                    using (Image source = Image.FromFile(path)) _images[imageIndex] = new Bitmap(source);
                    _thumbs[imageIndex].Image = _images[imageIndex];
                }
            }
            catch { }
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            int count = 0;
            for (int i = 0; i < 3; i++) if (_images[i] != null) count++;
            if (count == 0) _status.Text = "未选择图片 · 建议裁切 60–320 px";
            else if (count == 1) _status.Text = "1 张图片 · 识别到即点击";
            else _status.Text = count + " 张图片 · 任意一张识别到即点击";
        }

        private void UpdateImageSelection()
        {
            for (int i = 0; i < 3; i++)
            {
                _imageLabels[i].ForeColor = i == _selectedImage ? Theme.Gold : Theme.Faint;
                _thumbs[i].Invalidate();
            }
        }

        private void SelectMe()
        {
            EventHandler h = SelectedChanged;
            if (h != null) h(this, EventArgs.Empty);
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            SelectMe();
            base.OnMouseDown(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            if (_thumbs[0] == null) return;
            int pad = Theme.S(8);
            int thumbW = Theme.S(44);
            int thumbH = Theme.S(32);
            int gap = Theme.S(4);
            for (int i = 0; i < 3; i++)
            {
                _thumbs[i].SetBounds(pad + i * (thumbW + gap), Theme.S(6), thumbW, thumbH);
                _imageLabels[i].SetBounds(pad + i * (thumbW + gap), Theme.S(38), thumbW, Theme.S(14));
            }
            int right = Width - pad;
            _enabled.SetBounds(right - Theme.S(42), Theme.S(4), Theme.S(42), Theme.S(24));
            int capW = Theme.S(48), clearW = Theme.S(42), selectW = Theme.S(42);
            _clear.SetBounds(right - clearW, Theme.S(30), clearW, Theme.S(26));
            _capture.SetBounds(_clear.Left - Theme.S(6) - capW, Theme.S(30), capW, Theme.S(26));
            _select.SetBounds(_capture.Left - Theme.S(6) - selectW, Theme.S(30), selectW, Theme.S(26));
            int textLeft = pad + 3 * thumbW + 2 * gap + Theme.S(10);
            int textRight = _select.Left - Theme.S(8);
            _title.SetBounds(textLeft, Theme.S(4), Math.Max(40, textRight - textLeft), Theme.S(20));
            _status.SetBounds(textLeft, Theme.S(26), Math.Max(40, textRight - textLeft), Theme.S(22));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRounded(e.Graphics, rect, Theme.S(9), _selected ? Color.FromArgb(31, 43, 65) : Color.FromArgb(22, 31, 47));
            Theme.DrawRounded(e.Graphics, rect, Theme.S(9), _selected ? Theme.Accent : Theme.StrokeSoft, _selected ? 1.4f : 1f);
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

    public class StorySkipSettingsPanel : Panel
    {
        private StorySkipConfig _config;
        private readonly StorySkipRow[] _rows;
        private readonly ToggleSwitch _enabled;
        private readonly ToggleSwitch _local;
        private readonly ToggleSwitch _stream;
        private readonly ToggleSwitch _always;
        private readonly ModernButton _random;
        private readonly ModernButton _cross;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly Label _localLabel;
        private readonly Label _streamLabel;
        private readonly Label _alwaysLabel;
        private readonly Label _hint;
        private readonly Label _rule;
        private readonly Label _hitCountLabel;
        private readonly StepperBox _hitStepper;
        private readonly Label _intervalLabel;
        private readonly StepperBox _intervalStepper;
        private readonly Label _rateLabel;
        private readonly StepperBox _rateStepper;
        private readonly KeyCaptureBox _hotkey;
        private readonly Label _hotkeyLabel;
        private readonly Label _hotkeyHint;
        private bool _binding;

        // 折叠态需保留可见的标题控件，交给 CardDock 判定。
        public Control DockTitleControl { get { return _title; } }

        public event EventHandler Changed;
        public event EventHandler HotkeyChanged;
        public event Action<int, int> CaptureRequested;
        public event Action<int, int> SelectRequested;

        public StorySkipSettingsPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.SurfaceAlt;
            Height = Theme.S(504);
            _rows = new StorySkipRow[3];

            _enabled = new ToggleSwitch();
            _enabled.CheckedChanged += delegate
            {
                if (_binding || _config == null) return;
                _config.Enabled = _enabled.Checked;
                RaiseChanged();
            };
            Controls.Add(_enabled);

            _local = new ToggleSwitch();
            _local.CheckedChanged += delegate
            {
                if (_binding || _config == null) return;
                _config.LocalEnabled = _local.Checked;
                RaiseChanged();
            };
            Controls.Add(_local);

            _stream = new ToggleSwitch();
            _stream.CheckedChanged += delegate
            {
                if (_binding || _config == null) return;
                _config.StreamEnabled = _stream.Checked;
                RaiseChanged();
            };
            Controls.Add(_stream);

            _always = new ToggleSwitch();
            _always.CheckedChanged += delegate
            {
                if (_binding || _config == null) return;
                _config.AlwaysEnabled = _always.Checked;
                RaiseChanged();
            };
            Controls.Add(_always);

            _random = new ModernButton();
            _random.Text = "随机";
            _random.Style = ModernButtonStyle.Ghost;
            _random.Font = Theme.UiFont(7.6f, false);
            _random.Click += delegate
            {
                if (_binding || _config == null) return;
                _config.CrossValidation = false;
                UpdateModeButtons();
                UpdateRule();
                RaiseChanged();
            };
            Controls.Add(_random);

            _cross = new ModernButton();
            _cross.Text = "顺序";
            _cross.Style = ModernButtonStyle.Ghost;
            _cross.Font = Theme.UiFont(7.6f, false);
            _cross.Click += delegate
            {
                if (_binding || _config == null) return;
                _config.CrossValidation = true;
                UpdateModeButtons();
                UpdateRule();
                RaiseChanged();
            };
            Controls.Add(_cross);

            for (int i = 0; i < 3; i++)
            {
                int index = i;
                StorySkipRow row = new StorySkipRow(i);
                row.SelectedChanged += delegate { SelectSlot(index); };
                row.Changed += delegate { RaiseChanged(); };
                row.CaptureRequested += delegate(int imageIndex)
                {
                    SelectSlot(index);
                    Action<int, int> h = CaptureRequested;
                    if (h != null) h(index, imageIndex);
                };
                row.SelectRequested += delegate(int imageIndex)
                {
                    SelectSlot(index);
                    Action<int, int> h = SelectRequested;
                    if (h != null) h(index, imageIndex);
                };
                row.ClearRequested += delegate(int imageIndex)
                {
                    SelectSlot(index);
                    ClearFeature(index, imageIndex);
                };
                _rows[i] = row;
                Controls.Add(row);
            }

            _title = MakeLabel("自动跳过剧情");
            _title.Font = Theme.Semibold(9.4f);
            _title.ForeColor = Theme.Text;
            _subtitle = MakeLabel("识别目标按钮，命中后自动点击；最多设置 3 个目标");
            _subtitle.Font = Theme.UiFont(7.3f, false);
            _subtitle.ForeColor = Theme.Faint;
            _localLabel = MakeLabel("本地模式");
            _streamLabel = MakeLabel("串流模式");
            _alwaysLabel = MakeLabel("始终开启");
            _alwaysLabel.ForeColor = Theme.Gold;
            _hint = MakeLabel("截图建议 60–320 px；未设置的后续目标会自动跳过。独立开关：开启后不管右上角本地模式还是串流模式，自动跳过剧情都会始终开启。");
            _hint.Font = Theme.UiFont(7.0f, false);
            _hint.ForeColor = Theme.Faint;
            _hint.TextAlign = ContentAlignment.TopLeft;
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
                if (_config != null) { _config.HitCount = _hitStepper.Value; RaiseChanged(); }
            };

            _intervalLabel = MakeLabel("扫描间隔");
            _intervalLabel.Font = Theme.UiFont(7.5f, false);
            _intervalLabel.ForeColor = Theme.TextSoft;
            _intervalStepper = new StepperBox();
            _intervalStepper.Minimum = 200; _intervalStepper.Maximum = 3000; _intervalStepper.Step = 100;
            _intervalStepper.Size = new Size(136, 28);
            _intervalStepper.ValueChanged += delegate
            {
                if (_config != null) { _config.IntervalMs = _intervalStepper.Value; RaiseChanged(); }
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
                if (_binding || _config == null) return;
                _config.Threshold = (float)_rateStepper.DecimalValue;
                RaiseChanged();
            };

            // 跳过剧情全局快捷键：参照图片识别截图快捷键一行（标签 + 捕获框，捕获框内置「打开键盘」）。
            _hotkey = new KeyCaptureBox();
            _hotkey.BindingChanged += delegate
            {
                if (_binding || _config == null) return;
                _config.Hotkey = _hotkey.Binding.Clone();
                if (_config.Hotkey.Kind != BindingKind.Keyboard)
                {
                    _config.Hotkey = new KeyBinding(BindingKind.Keyboard, (int)Keys.F9);
                    _hotkey.SetBindingSilent(_config.Hotkey.Clone());
                }
                EventHandler h = HotkeyChanged;
                if (h != null) h(this, EventArgs.Empty);
                RaiseChanged();
            };
            Controls.Add(_hotkey);

            _hotkeyLabel = MakeLabel("跳过快捷键");
            _hotkeyLabel.Font = Theme.UiFont(8.1f, false);
            _hotkeyLabel.ForeColor = Theme.Muted;
            _hotkeyHint = MakeLabel("快捷键在设置隐藏/最小化后生效；按下切换自动跳过剧情总开关，屏幕正中弹出开/关提示");
            _hotkeyHint.Font = Theme.UiFont(7.0f, false);
            _hotkeyHint.ForeColor = Theme.Faint;
            _hotkeyHint.TextAlign = ContentAlignment.TopLeft;

            Controls.AddRange(new Control[] { _title, _subtitle, _localLabel, _streamLabel, _alwaysLabel,
                _hint, _rule, _hitCountLabel, _hitStepper, _intervalLabel, _intervalStepper,
                _rateLabel, _rateStepper, _hotkeyLabel, _hotkeyHint });
        }

        public void SetConfig(StorySkipConfig config)
        {
            _config = config;
            _binding = true;
            try
            {
                _enabled.SetCheckedSilent(config != null && config.Enabled);
                _local.SetCheckedSilent(config != null && config.LocalEnabled);
                _stream.SetCheckedSilent(config != null && config.StreamEnabled);
                _always.SetCheckedSilent(config != null && config.AlwaysEnabled);
                if (config != null)
                {
                    _hotkey.SetBindingSilent(config.Hotkey == null
                        ? new KeyBinding(BindingKind.Keyboard, (int)Keys.F9)
                        : config.Hotkey.Clone());
                    for (int i = 0; i < 3; i++)
                        _rows[i].SetFeature(i < config.Features.Count ? config.Features[i] : null);
                    _hitStepper.Value = Math.Max(1, Math.Min(5, config.HitCount));
                    _intervalStepper.Value = Math.Max(200, Math.Min(3000, config.IntervalMs));
                    _rateStepper.DecimalValue = Math.Max(0.5, Math.Min(1.0, config.Threshold));
                }
            }
            finally { _binding = false; }
            UpdateModeButtons();
            UpdateRule();
            Invalidate();
        }

        public void RefreshImage(int index)
        {
            if (index >= 0 && index < 3) _rows[index].SetFeature(_config == null ? null :
                (index < _config.Features.Count ? _config.Features[index] : null));
        }

        private void ClearFeature(int index, int imageIndex)
        {
            if (_config == null || index < 0 || index >= _config.Features.Count) return;
            if (imageIndex < 0 || imageIndex > 2) imageIndex = 0;
            StorySkipFeatureConfig f = _config.Features[index];
            StorySkipImageConfig image = f.Images[imageIndex];
            image.ImageFile = "";
            image.CaptureWidth = 0;
            image.CaptureHeight = 0;
            try { File.Delete(ConfigStore.StorySkipPath(index, imageIndex)); } catch { }
            bool any = false;
            for (int i = 0; i < 3; i++)
                if (!string.IsNullOrEmpty(f.Images[i].ImageFile)) any = true;
            if (!any) f.Enabled = false;
            _rows[index].RefreshImage(imageIndex);
            RaiseChanged();
        }

        private void SelectSlot(int index)
        {
            for (int i = 0; i < _rows.Length; i++) _rows[i].Selected = i == index;
        }

        private void UpdateModeButtons()
        {
            if (_random == null || _cross == null) return;
            bool cross = _config != null && _config.CrossValidation;
            _random.Style = cross ? ModernButtonStyle.Ghost : ModernButtonStyle.Primary;
            _cross.Style = cross ? ModernButtonStyle.Primary : ModernButtonStyle.Ghost;
        }

        private void UpdateRule()
        {
            if (_rule == null || _config == null) return;
            _rule.Text = _config.CrossValidation
                ? "顺序模式：严格按目标 1 → 目标 2 → 目标 3 识别并点击；目标 1 成功后自动开始识别目标 2，目标 2 成功后再识别目标 3。只设置目标 1 时，点击后重新等待。"
                : "随机模式：每次从尚未点击的目标中随机选一个识别，点击成功后继续识别剩余目标；未设置的后续目标会自动跳过。";
        }

        private void RaiseChanged()
        {
            EventHandler h = Changed;
            if (h != null) h(this, EventArgs.Empty);
            UpdateRule();
            Invalidate();
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
            LayoutControls();
        }

        private void LayoutControls()
        {
            if (_rows == null || _rows[0] == null) return;
            int pad = Theme.S(14);
            int toggleW = Theme.S(42);
            int buttonW = Theme.S(54);
            int right = Width - pad;
            int gap = Theme.S(8);
            bool collapsed = Height < Theme.S(48);

            // 折叠态只保留标题；头部开关、顺序/随机按钮、副标题与快捷键行全部隐藏。
            _enabled.Visible = !collapsed;
            _cross.Visible = !collapsed;
            _random.Visible = !collapsed;
            _subtitle.Visible = !collapsed;
            _hotkeyLabel.Visible = !collapsed;
            _hotkey.Visible = !collapsed;
            _hotkeyHint.Visible = !collapsed;
            if (collapsed)
            {
                _title.SetBounds(Theme.S(36), 0, Math.Max(80, Width - Theme.S(70)), Theme.S(36));
                return;
            }

            int enabledRight = right - Theme.S(34);
            _enabled.SetBounds(enabledRight - toggleW, Theme.S(5), toggleW, Theme.S(24));
            int crossRight = enabledRight - toggleW - gap;
            _cross.SetBounds(crossRight - buttonW, Theme.S(5), buttonW, Theme.S(26));
            int randomRight = crossRight - buttonW - Theme.S(6);
            _random.SetBounds(randomRight - buttonW, Theme.S(5), buttonW, Theme.S(26));
            _title.SetBounds(Theme.S(36), 0, Math.Max(80, _random.Left - Theme.S(36) - Theme.S(8)), Theme.S(36));
            // 副标题放在标题行下方，不与标题文字重叠。
            _subtitle.SetBounds(pad, Theme.S(38), Math.Max(80, right - pad), Theme.S(18));

            int modeY = Theme.S(62);
            _localLabel.SetBounds(pad, modeY, Theme.S(64), Theme.S(24));
            _local.SetBounds(pad + Theme.S(66), modeY + 1, toggleW, Theme.S(22));
            _streamLabel.SetBounds(pad + Theme.S(120), modeY, Theme.S(64), Theme.S(24));
            _stream.SetBounds(pad + Theme.S(186), modeY + 1, toggleW, Theme.S(22));
            _alwaysLabel.SetBounds(pad + Theme.S(240), modeY, Theme.S(64), Theme.S(24));
            _always.SetBounds(pad + Theme.S(306), modeY + 1, toggleW, Theme.S(22));

            int y = Theme.S(94);
            for (int i = 0; i < 3; i++)
            {
                _rows[i].SetBounds(pad, y, Math.Max(100, right - pad), Theme.S(76));
                y += Theme.S(80);
            }
            _hint.SetBounds(pad, y + Theme.S(2), Math.Max(100, right - pad), Theme.S(30));
            // 命中次数 + 扫描间隔 + 命中率 同一排
            int ySettings = y + Theme.S(34);

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

            // 快捷键行放在底部规则文案上方：标签 + 捕获框一排，说明小字一排，规则文案再下移。
            int yHotkey = ySettings + Theme.S(28);
            _hotkeyLabel.SetBounds(pad, yHotkey + Theme.S(2), Theme.S(78), Theme.S(30));
            _hotkey.SetBounds(pad + Theme.S(82), yHotkey,
                Math.Min(Theme.S(190), Math.Max(Theme.S(100), right - pad - Theme.S(90))), Theme.S(32));
            _hotkeyHint.SetBounds(pad, yHotkey + Theme.S(36), Math.Max(100, right - pad), Theme.S(18));

            _rule.SetBounds(pad, yHotkey + Theme.S(56), Math.Max(100, right - pad), Theme.S(40));
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
