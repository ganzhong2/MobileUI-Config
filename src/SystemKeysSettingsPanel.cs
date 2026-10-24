using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace MobileUiConfig
{
    public class SystemKeysSettingsPanel : Panel
    {
        private AppConfig _config;
        private AppConfig _modeRoot;
        private bool _binding;
        private readonly ToggleSwitch _displayEnabled;
        private readonly ToggleSwitch _modeEnabled;
        private readonly ModernSelect _displayShort;
        private readonly ModernSelect _displayLong;
        private readonly ModernSelect _displayDouble;
        private readonly ModernSelect _modeShort;
        private readonly ModernSelect _modeLong;
        private readonly ModernSelect _modeDouble;
        private readonly Label _displayLabel;
        private readonly Label _modeLabel;
        private readonly Label _summary;

        public event EventHandler Changed;

        public SystemKeysSettingsPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.SurfaceAlt;
            Height = Theme.S(116);

            _displayEnabled = new ToggleSwitch();
            _displayEnabled.CheckedChanged += delegate
            {
                if (_binding || _config == null) return;
                _config.DisplayKey.Enabled = _displayEnabled.Checked;
                UpdateSummary();
                RaiseChanged();
            };
            Controls.Add(_displayEnabled);

            _modeEnabled = new ToggleSwitch();
            _modeEnabled.CheckedChanged += delegate
            {
                if (_binding || _config == null) return;
                _config.ModeKey.Enabled = _modeEnabled.Checked;
                UpdateSummary();
                RaiseChanged();
            };
            Controls.Add(_modeEnabled);

            _displayLabel = MakeLabel("显示键");
            _displayLabel.ForeColor = Theme.TextSoft;
            _modeLabel = MakeLabel("模式键");
            _modeLabel.ForeColor = Theme.TextSoft;
            Controls.Add(_displayLabel);
            Controls.Add(_modeLabel);

            _displayShort = MakeSelect(false);
            _displayLong = MakeSelect(false);
            _displayDouble = MakeSelect(false);
            _modeShort = MakeSelect(true);
            _modeLong = MakeSelect(true);
            _modeDouble = MakeSelect(true);

            _displayShort.SelectionChanged += delegate { if (!_binding) SetDisplayAction(_displayShort, 0); };
            _displayLong.SelectionChanged += delegate { if (!_binding) SetDisplayAction(_displayLong, 1); };
            _displayDouble.SelectionChanged += delegate { if (!_binding) SetDisplayAction(_displayDouble, 2); };
            _modeShort.SelectionChanged += delegate { if (!_binding) SetModeAction(_modeShort, 0); };
            _modeLong.SelectionChanged += delegate { if (!_binding) SetModeAction(_modeLong, 1); };
            _modeDouble.SelectionChanged += delegate { if (!_binding) SetModeAction(_modeDouble, 2); };

            _summary = MakeLabel("");
            _summary.Font = Theme.UiFont(7.1f, false);
            _summary.ForeColor = Theme.Faint;
            Controls.Add(_summary);
        }

        // 显示键/模式键的开关与短按/长按/双击动作存当前页根（按键页主根、手柄页 Gamepad 根），
        // 两页独立保存互不覆盖；位置坐标由主根在真机/预览层单独维护，不在本卡片内。
        public void SetConfig(AppConfig config)
        {
            _config = config;
            if (_config == null) return;
            _binding = true;
            try
            {
                _displayEnabled.SetCheckedSilent(_config.DisplayKey.Enabled);
                _modeEnabled.SetCheckedSilent(_config.ModeKey.Enabled);
                BindDisplaySelects();
                BindModeSelects();
            }
            finally
            {
                _binding = false;
            }
            UpdateSummary();
            Invalidate();
        }

        // 模式名下拉按当前页生效根显示；系统键开关与动作绑定本身随 SetConfig 传入的页根保存。
        public void SetModeRoot(AppConfig modeRoot)
        {
            _modeRoot = modeRoot;
            SetModes();
        }

        private AppConfig ModeRoot { get { return _modeRoot ?? _config; } }

        public void SetModes()
        {
            if (_config == null) return;
            _binding = true;
            try
            {
                BindDisplaySelects();
                BindModeSelects();
            }
            finally
            {
                _binding = false;
            }
            UpdateSummary();
            Invalidate();
        }

        private ModernSelect MakeSelect(bool mode)
        {
            ModernSelect select = new ModernSelect();
            select.Height = Theme.S(30);
            select.Font = Theme.UiFont(7.7f, false);
            Controls.Add(select);
            return select;
        }

        private List<SelectOption> DisplayOptions()
        {
            List<SelectOption> list = new List<SelectOption>();
            list.Add(new SelectOption("-1", "无"));
            list.Add(new SelectOption("0", "切换显示/隐藏"));
            list.Add(new SelectOption("1", "显示按键"));
            list.Add(new SelectOption("2", "隐藏按键"));
            list.Add(new SelectOption("3", "进入移动"));
            list.Add(new SelectOption("4", "拦截鼠标点击"));
            list.Add(new SelectOption("5", "跳过剧情"));
            return list;
        }

        private List<SelectOption> ModeOptions()
        {
            List<SelectOption> list = new List<SelectOption>();
            list.Add(new SelectOption("-1", "无"));
            list.Add(new SelectOption("0", "模式选择菜单"));
            AppConfig modeRoot = ModeRoot;
            if (modeRoot != null && modeRoot.Modes != null)
            {
                for (int i = 0; i < modeRoot.Modes.Count && i < 12; i++)
                {
                    string name = modeRoot.Modes[i] == null ? "模式" + (i + 1) : modeRoot.Modes[i].Name;
                    list.Add(new SelectOption((i + 1).ToString(CultureInfo.InvariantCulture), name));
                }
            }
            return list;
        }

        private void BindDisplaySelects()
        {
            if (_config == null) return;
            List<SelectOption> opts = DisplayOptions();
            _displayShort.SetItems(opts, _config.DisplayKey.ShortAction.ToString(CultureInfo.InvariantCulture));
            _displayLong.SetItems(opts, _config.DisplayKey.LongAction.ToString(CultureInfo.InvariantCulture));
            _displayDouble.SetItems(opts, _config.DisplayKey.DoubleAction.ToString(CultureInfo.InvariantCulture));
        }

        private void BindModeSelects()
        {
            if (_config == null) return;
            List<SelectOption> opts = ModeOptions();
            _modeShort.SetItems(opts, _config.ModeKey.ShortAction.ToString(CultureInfo.InvariantCulture));
            _modeLong.SetItems(opts, _config.ModeKey.LongAction.ToString(CultureInfo.InvariantCulture));
            _modeDouble.SetItems(opts, _config.ModeKey.DoubleAction.ToString(CultureInfo.InvariantCulture));
        }

        private void SetDisplayAction(ModernSelect select, int slot)
        {
            if (_config == null) return;
            int value;
            if (!int.TryParse(select.SelectedId, out value)) value = -1;
            if (slot == 0) _config.DisplayKey.ShortAction = value;
            else if (slot == 1) _config.DisplayKey.LongAction = value;
            else _config.DisplayKey.DoubleAction = value;
            UpdateSummary();
            RaiseChanged();
        }

        private void SetModeAction(ModernSelect select, int slot)
        {
            if (_config == null) return;
            int value;
            if (!int.TryParse(select.SelectedId, out value)) value = -1;
            if (slot == 0) _config.ModeKey.ShortAction = value;
            else if (slot == 1) _config.ModeKey.LongAction = value;
            else _config.ModeKey.DoubleAction = value;
            UpdateSummary();
            RaiseChanged();
        }

        public void UpdateSummary()
        {
            if (_config == null) return;
            string ds = DisplayActionName(_config.DisplayKey.ShortAction);
            string dl = DisplayActionName(_config.DisplayKey.LongAction);
            string ms = ModeActionName(_config.ModeKey.ShortAction);
            string ml = ModeActionName(_config.ModeKey.LongAction);
            string md = ModeActionName(_config.ModeKey.DoubleAction);
            _summary.Text = "显示键：短按 " + ds + " · 长按 " + dl + "　|　模式键：短按 " + ms + " · 长按 " + ml + " · 双击 " + md;
            Invalidate();
        }

        private string DisplayActionName(int code)
        {
            if (code == -1) return "无";
            if (code == 1) return "显示";
            if (code == 2) return "隐藏";
            if (code == 3) return "进入移动";
            if (code == 4) return "拦截鼠标";
            if (code == 5) return "跳过剧情";
            return "切换显示/隐藏";
        }

        private string ModeActionName(int code)
        {
            if (code == -1) return "无";
            if (code == 0) return "选择菜单";
            AppConfig modeRoot = ModeRoot;
            if (modeRoot != null && code >= 1 && code <= modeRoot.Modes.Count)
                return modeRoot.Modes[code - 1].Name;
            return "模式" + code;
        }

        private Label MakeLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.BackColor = Theme.SurfaceAlt;
            label.ForeColor = Theme.Muted;
            label.Font = Theme.UiFont(7.8f, false);
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
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
            if (_displayLabel == null || _displayEnabled == null || _displayShort == null) return;
            int pad = Theme.S(14);
            int toggleW = Theme.S(42);
            int typeW = Theme.S(58);
            int rowH = Theme.S(30);
            int gap = Theme.S(7);
            int actionLabelW = Theme.S(32);
            int right = Width - pad;
            _displayLabel.SetBounds(pad, Theme.S(28), typeW, rowH);
            _displayEnabled.SetBounds(pad + typeW, Theme.S(30), toggleW, Theme.S(24));
            int actionsLeft = pad + typeW + toggleW + Theme.S(8);
            int actionW = Math.Max(Theme.S(58), (right - actionsLeft - gap * 2) / 3);
            int x = actionsLeft;
            SetActionBounds(_displayShort, "短按", x, Theme.S(28), actionLabelW, actionW, rowH);
            x += actionW + gap;
            SetActionBounds(_displayLong, "长按", x, Theme.S(28), actionLabelW, actionW, rowH);
            x += actionW + gap;
            SetActionBounds(_displayDouble, "双击", x, Theme.S(28), actionLabelW, actionW, rowH);

            int y2 = Theme.S(64);
            _modeLabel.SetBounds(pad, y2, typeW, rowH);
            _modeEnabled.SetBounds(pad + typeW, y2 + 2, toggleW, Theme.S(24));
            x = actionsLeft;
            SetActionBounds(_modeShort, "短按", x, y2, actionLabelW, actionW, rowH);
            x += actionW + gap;
            SetActionBounds(_modeLong, "长按", x, y2, actionLabelW, actionW, rowH);
            x += actionW + gap;
            SetActionBounds(_modeDouble, "双击", x, y2, actionLabelW, actionW, rowH);
            _summary.SetBounds(pad, Theme.S(97), right - pad, Theme.S(17));
        }

        private void SetActionBounds(ModernSelect select, string text, int x, int y, int labelW, int selectW, int rowH)
        {
            Label label = select.Tag as Label;
            if (label == null)
            {
                label = MakeLabel(text);
                label.ForeColor = Theme.Faint;
                label.Font = Theme.UiFont(7.1f, false);
                select.Tag = label;
                Controls.Add(label);
            }
            label.Text = text;
            label.SetBounds(x, y, labelW, rowH);
            select.SetBounds(x + labelW, y, Math.Max(Theme.S(50), selectW - labelW), rowH);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRounded(e.Graphics, rect, Theme.S(12), BackColor);
            Theme.DrawRounded(e.Graphics, rect, Theme.S(12), Theme.StrokeSoft, 1f);
            Theme.FillRounded(e.Graphics, new RectangleF(Theme.S(13), Theme.S(5), Theme.S(3), Theme.S(16)),
                Theme.S(2), Theme.Gold);
            Theme.DrawText(e.Graphics, "手机框上方系统按键", Theme.Semibold(8.8f), Theme.Text,
                new RectangleF(Theme.S(22), Theme.S(2), Theme.S(180), Theme.S(22)), StringAlignment.Near, StringAlignment.Center, true);
            Theme.DrawText(e.Graphics, "可单独开关显示/模式键，并设置短按、长按、双击功能", Theme.UiFont(7.0f, false), Theme.Faint,
                new RectangleF(Theme.S(175), Theme.S(3), Math.Max(40, Width - Theme.S(200)), Theme.S(21)),
                StringAlignment.Near, StringAlignment.Center, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }
    }
}
