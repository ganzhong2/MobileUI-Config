using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace MobileUiConfig
{
    // 单个内存地址：基址 / 偏移 / 目标值，命中后切换到唯一的目标模式。
    public class AutoMemoryRow : Panel
    {
        private readonly TextBox _base;
        private readonly TextBox _offsets;
        private readonly TextBox _value;
        private readonly ModernSelect _target;
        private readonly Label _baseLabel;
        private readonly Label _offsetLabel;
        private readonly Label _valueLabel;
        private readonly Label _targetLabel;
        private readonly Label _status;
        private AutoMemoryProject _project;
        private bool _binding;

        public event EventHandler Changed;

        public AutoMemoryRow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.SurfaceAlt;
            Height = Theme.S(98);

            _baseLabel = MakeLabel("基址");
            _baseLabel.Font = Theme.UiFont(7.3f, false);
            _baseLabel.ForeColor = Theme.Faint;
            Controls.Add(_baseLabel);
            _base = MakeBox();
            _base.TextChanged += delegate
            {
                if (_binding || _project == null || _project.Memory == null) return;
                _project.Memory.BaseAddress = _base.Text.Trim();
                RaiseChanged();
            };
            Controls.Add(_base);

            _offsetLabel = MakeLabel("偏移");
            _offsetLabel.Font = Theme.UiFont(7.3f, false);
            _offsetLabel.ForeColor = Theme.Faint;
            Controls.Add(_offsetLabel);
            _offsets = MakeBox();
            _offsets.TextChanged += delegate
            {
                if (_binding || _project == null || _project.Memory == null) return;
                _project.Memory.Offsets = _offsets.Text.Trim();
                RaiseChanged();
            };
            Controls.Add(_offsets);

            _valueLabel = MakeLabel("目标值");
            _valueLabel.Font = Theme.UiFont(7.3f, false);
            _valueLabel.ForeColor = Theme.Faint;
            Controls.Add(_valueLabel);
            _value = MakeBox();
            _value.TextChanged += delegate
            {
                if (_binding || _project == null || _project.Memory == null) return;
                _project.Memory.TargetValue = _value.Text.Trim();
                RaiseChanged();
            };
            Controls.Add(_value);

            _targetLabel = MakeLabel("命中后切换到");
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

            _status = MakeLabel("已填写「基址 + 偏移」后，目标值留空表示非 0 即命中");
            _status.Font = Theme.UiFont(7.0f, false);
            _status.ForeColor = Theme.Faint;
            Controls.Add(_status);
        }

        public void SetProject(AutoMemoryProject project, List<ModeConfig> modes)
        {
            _project = project;
            _binding = true;
            try
            {
                MemoryFeatureConfig memory = project == null ? null : project.Memory;
                _base.Text = memory == null ? "" : memory.BaseAddress;
                _offsets.Text = memory == null ? "" : memory.Offsets;
                _value.Text = memory == null ? "" : memory.TargetValue;
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
            }
            finally { _binding = false; }
            UpdateStatus();
        }

        public void UpdateStatus()
        {
            if (_project == null || _project.Memory == null) return;
            MemoryFeatureConfig memory = _project.Memory;
            if (string.IsNullOrEmpty(memory.BaseAddress))
                _status.Text = "未填写基址 · 支持 模块名+偏移 / 绝对地址";
            else
                _status.Text = string.IsNullOrEmpty(memory.TargetValue)
                    ? "目标值留空 · 读取值非 0 即命中"
                    : "读取值等于 " + memory.TargetValue + " 时命中";
        }

        private TextBox MakeBox()
        {
            TextBox box = new TextBox();
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = Theme.BgSoft;
            box.ForeColor = Theme.Text;
            box.Font = Theme.UiFont(7.8f, false);
            box.ShortcutsEnabled = true;
            return box;
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

        private void RaiseChanged()
        {
            UpdateStatus();
            EventHandler h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
        }

        private void LayoutChildren()
        {
            if (_base == null) return;
            int pad = Theme.S(12);
            int right = Width - pad;
            int boxH = Theme.S(27);
            int labelH = Theme.S(20);

            int available = Math.Max(160, right - pad);
            int baseW = (int)(available * 0.42f);
            int offsetW = (int)(available * 0.30f);
            int valueW = available - baseW - offsetW - Theme.S(24);
            int labelW = Theme.S(34);

            int x = pad;
            _baseLabel.SetBounds(x, Theme.S(8), labelW, labelH);
            _base.SetBounds(x + labelW, Theme.S(6), Math.Max(70, baseW - labelW), boxH);
            x += baseW + Theme.S(10);
            _offsetLabel.SetBounds(x, Theme.S(8), labelW, labelH);
            _offsets.SetBounds(x + labelW, Theme.S(6), Math.Max(60, offsetW - labelW), boxH);
            x += offsetW + Theme.S(10);
            _valueLabel.SetBounds(x, Theme.S(8), Theme.S(44), labelH);
            _value.SetBounds(x + Theme.S(44), Theme.S(6), Math.Max(50, valueW - Theme.S(44)), boxH);

            int targetW = Theme.S(132);
            int targetLabelW = Theme.S(84);
            _target.SetBounds(right - targetW, Theme.S(46), targetW, Theme.S(28));
            _targetLabel.SetBounds(_target.Left - targetLabelW - Theme.S(4), Theme.S(48),
                targetLabelW, Theme.S(24));
            _status.SetBounds(pad, Theme.S(74), Math.Max(80, _targetLabel.Left - pad - Theme.S(8)),
                Theme.S(18));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRounded(e.Graphics, rect, Theme.S(9), Color.FromArgb(22, 31, 47));
            Theme.DrawRounded(e.Graphics, rect, Theme.S(9), Theme.StrokeSoft, 1f);
        }
    }

    public class MemoryScanSettingsPanel : Panel
    {
        private MemoryScanConfig _scan;
        private AutoMemoryProject _project;
        private List<ModeConfig> _modes;
        private bool _binding;

        private readonly ToggleSwitch _enabled;
        private readonly AutoMemoryRow _row;
        private readonly ModeTagStrip _modeStrip;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly Label _hint;
        private readonly Label _status;
        private readonly Label _rule;
        private readonly Label _intervalLabel;
        private readonly StepperBox _interval;
        private readonly Label _disclaimer;

        // 折叠态需保留可见的标题控件，交给 CardDock 判定。
        public Control DockTitleControl { get { return _title; } }

        public event EventHandler Changed;
        public event Action<int> ModeSelectRequested;
        public event EventHandler AddModeRequested;
        public event EventHandler RenameModeRequested;
        public event EventHandler RemoveModeRequested;

        public MemoryScanSettingsPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.SurfaceAlt;
            Height = Theme.S(346);

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

            _enabled = new ToggleSwitch();
            _enabled.CheckedChanged += delegate
            {
                if (_binding || _project == null) return;
                _project.Enabled = _enabled.Checked;
                RaiseChanged();
            };
            Controls.Add(_enabled);

            _row = new AutoMemoryRow();
            _row.Changed += delegate { RaiseChanged(); };
            Controls.Add(_row);

            _title = MakeLabel("自动切换 · 内存扫描");
            _title.Font = Theme.Semibold(9.4f);
            _title.ForeColor = Theme.Text;
            _subtitle = MakeLabel("每个项目独立配置：扫描 1 个内存地址，命中后切换到界面预览的所选模式");
            _subtitle.Font = Theme.UiFont(7.3f, false);
            _subtitle.ForeColor = Theme.Faint;
            _hint = MakeLabel("基址支持 模块名+偏移 / 绝对地址；偏移用逗号分隔；目标值留空表示非 0 命中");
            _hint.Font = Theme.UiFont(7.1f, false);
            _hint.ForeColor = Theme.Faint;
            _status = MakeLabel("");
            _status.Font = Theme.UiFont(7.1f, false);
            _status.ForeColor = Theme.Mint;
            _rule = MakeLabel("");
            _rule.Font = Theme.UiFont(7.0f, false);
            _rule.ForeColor = Theme.Muted;
            _rule.TextAlign = ContentAlignment.TopLeft;
            _intervalLabel = MakeLabel("扫描间隔（50-1000ms）");
            _intervalLabel.Font = Theme.UiFont(7.2f, false);
            _intervalLabel.ForeColor = Theme.Faint;
            _interval = new StepperBox();
            _interval.Minimum = 50;
            _interval.Maximum = 1000;
            _interval.Value = 700;
            _interval.ValueChanged += delegate
            {
                if (_binding || _scan == null) return;
                _scan.IntervalMs = _interval.Value;
                RaiseChanged();
            };
            _disclaimer = MakeLabel("免责声明：内存读取可能被游戏反作弊判定为违规，存在封号风险，请谨慎使用。本功能只读内存，不修改游戏数据。");
            _disclaimer.Font = Theme.Semibold(7.1f);
            _disclaimer.ForeColor = Color.FromArgb(255, 132, 122);
            _disclaimer.TextAlign = ContentAlignment.TopLeft;
            Controls.AddRange(new Control[] { _title, _subtitle, _hint, _status, _rule, _intervalLabel, _interval, _disclaimer });
        }

        public void SetConfig(AppConfig config)
        {
            _scan = config == null ? null : config.MemoryScan;
            _modes = config == null ? null : config.Modes;
            _project = config == null ? null : config.ActiveMemoryProject;
            _binding = true;
            try
            {
                if (_scan == null) return;
                List<string> names = new List<string>();
                if (config != null && config.MemoryProjects != null)
                    for (int i = 0; i < config.MemoryProjects.Count; i++)
                        names.Add(config.MemoryProjects[i] == null ? "项目" : config.MemoryProjects[i].Name);
                _modeStrip.SetModes(names, config == null ? 0 : config.SelectedMemoryProject);
                _enabled.SetCheckedSilent(_project != null && _project.Enabled);
                _interval.SetValueSilent(_scan.IntervalMs);
                _row.SetProject(_project, _modes);
                _rule.Text = "识别规则：当前项目只扫描 1 个内存地址；读取值等于目标值（留空表示非 0 即命中）时，立即切换到上方选择的目标模式。";
            }
            finally { _binding = false; }
            UpdateEnabledState();
            Invalidate();
        }

        public void SetStatus(string text)
        {
            if (_status != null) _status.Text = text == null ? "" : text;
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
            _title.ForeColor = on ? Theme.Text : Theme.TextSoft;
            _subtitle.ForeColor = on ? Theme.Faint : Theme.Alpha(Theme.Faint, 80);
            _hint.ForeColor = on ? Theme.Faint : Theme.Alpha(Theme.Faint, 80);
            Invalidate();
        }

        private void RaiseChanged()
        {
            UpdateEnabledState();
            Invalidate();
            EventHandler h = Changed;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutControls();
        }

        private void LayoutControls()
        {
            if (_row == null) return;
            int pad = Theme.S(14);
            int rowW = Math.Max(100, Width - pad * 2);
            int right = Width - pad;
            _enabled.SetBounds(right - Theme.S(84), Theme.S(3), Theme.S(46), Theme.S(20));
            _title.SetBounds(Theme.S(36), 0, Math.Max(80, rowW - Theme.S(82)), Theme.S(36));
            // 标签条让出右上角启用开关的宽度，避免开关与「添加」按钮重叠。
            _modeStrip.SetBounds(pad, Theme.S(24), Math.Max(Theme.S(120), rowW - Theme.S(56)), Theme.S(38));
            _subtitle.SetBounds(pad, Theme.S(66), rowW, Theme.S(18));
            _row.SetBounds(pad, Theme.S(88), rowW, Theme.S(98));
            _hint.SetBounds(pad, Theme.S(192), rowW, Theme.S(18));
            _status.SetBounds(pad, Theme.S(212), rowW, Theme.S(16));
            _rule.SetBounds(pad, Theme.S(232), rowW, Theme.S(32));
            _intervalLabel.SetBounds(pad, Theme.S(270), Theme.S(140), Theme.S(24));
            _interval.SetBounds(pad + Theme.S(146), Theme.S(268), Theme.S(100), Theme.S(28));
            _disclaimer.SetBounds(pad, Theme.S(300), rowW, Theme.S(34));
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
