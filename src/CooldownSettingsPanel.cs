using System;
using System.Drawing;
using System.Windows.Forms;

namespace MobileUiConfig
{
    // Per-key cooldown monitor. It is attached to the virtual-key editor scroll
    // content so it scrolls with the other key sections, like the left panel.
    public class CooldownSettingsPanel : SectionPanel
    {
        private readonly ToggleSwitch _enabled;
        private readonly Label _keyLabel;
        private readonly Label _baseLabel;
        private readonly Label _offsetLabel;
        private readonly Label _intervalLabel;
        private readonly TextBox _base;
        private readonly TextBox _offsets;
        private readonly StepperBox _interval;
        private readonly Label _status;
        private readonly Label _disclaimer;
        private VirtualKey _key;
        private bool _binding;

        public event EventHandler Changed;

        public CooldownSettingsPanel()
        {
            BackColor = Theme.SurfaceAlt;
            Height = Theme.S(258);
            Title = "内存扫描 · 技能冷却";
            Hint = "选中虚拟按键独立配置；基址/偏移与左侧内存扫描相同";

            _enabled = new ToggleSwitch();
            _enabled.CheckedChanged += delegate
            {
                if (_binding || _key == null) return;
                _key.CooldownScanEnabled = _enabled.Checked;
                RaiseChanged();
            };
            Controls.Add(_enabled);

            _keyLabel = MakeLabel("当前按键：未选择");
            _keyLabel.Font = Theme.UiFont(7.5f, false);
            _keyLabel.ForeColor = Theme.Mint;
            Controls.Add(_keyLabel);

            _baseLabel = MakeLabel("基址");
            _offsetLabel = MakeLabel("偏移");
            _base = MakeBox();
            _offsets = MakeBox();
            _base.TextChanged += delegate
            {
                if (_binding || _key == null) return;
                _key.CooldownBaseAddress = _base.Text.Trim();
                RaiseChanged();
            };
            _offsets.TextChanged += delegate
            {
                if (_binding || _key == null) return;
                _key.CooldownOffsets = _offsets.Text.Trim();
                RaiseChanged();
            };
            Controls.Add(_baseLabel);
            Controls.Add(_offsetLabel);
            Controls.Add(_base);
            Controls.Add(_offsets);

            _intervalLabel = MakeLabel("扫描间隔");
            _interval = new StepperBox();
            _interval.Minimum = 50;
            _interval.Maximum = 1000;
            _interval.Value = 200;
            _interval.ValueChanged += delegate
            {
                if (_binding || _key == null) return;
                _key.CooldownIntervalMs = _interval.Value;
                RaiseChanged();
            };
            Controls.Add(_intervalLabel);
            Controls.Add(_interval);

            _status = MakeLabel("未开启");
            _status.Font = Theme.UiFont(7.5f, false);
            _status.ForeColor = Theme.Faint;
            Controls.Add(_status);

            _disclaimer = MakeLabel("免责声明：内存读取可能被游戏反作弊判定为违规，存在封号风险，请谨慎使用。本功能只读内存，不修改游戏数据。");
            _disclaimer.Font = Theme.Semibold(7.1f);
            _disclaimer.ForeColor = Color.FromArgb(255, 132, 122);
            _disclaimer.TextAlign = ContentAlignment.TopLeft;
            Controls.Add(_disclaimer);
        }

        public void SetKey(VirtualKey key)
        {
            _key = key;
            _binding = true;
            try
            {
                _enabled.SetCheckedSilent(key != null && key.CooldownScanEnabled);
                _base.Text = key == null ? "" : (key.CooldownBaseAddress ?? "");
                _offsets.Text = key == null ? "" : (key.CooldownOffsets ?? "");
                _interval.SetValueSilent(key == null ? 200 : key.CooldownIntervalMs);
                _keyLabel.Text = key == null ? "当前按键：未选择" : "当前按键：" + (string.IsNullOrEmpty(key.Name) ? "未命名" : key.Name);
                _status.Text = key == null ? "请选择一个虚拟按键" : "未开启";
                _status.ForeColor = Theme.Faint;
            }
            finally { _binding = false; }
        }

        internal void UpdateSnapshot(MemoryCooldownSnapshot snapshot)
        {
            if (snapshot == null) return;
            _status.Text = snapshot.Status;
            _status.ForeColor = snapshot.GameFound ? Theme.Mint : Theme.Faint;
            if (_key == null) return;
            double seconds;
            if (snapshot.Values.TryGetValue(_key.Id, out seconds) && snapshot.Valid.Contains(_key.Id))
                _status.Text = seconds <= 0.05 ? "已命中：技能就绪" : "已命中：剩余 " + seconds.ToString("0.0") + " 秒";
        }

        private void RaiseChanged()
        {
            EventHandler h = Changed;
            if (h != null) h(this, EventArgs.Empty);
            Invalidate();
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
            label.Font = Theme.UiFont(7.8f, false);
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
            if (_base == null) return;
            int pad = Theme.S(16);
            int right = Width - pad;
            _enabled.SetBounds(right - Theme.S(104), Theme.S(10), Theme.S(46), Theme.S(26));
            _keyLabel.SetBounds(pad, Theme.S(56), Width - pad * 2, Theme.S(18));

            int colGap = Theme.S(10);
            int colW = Math.Max(70, (right - pad - colGap) / 2);
            _baseLabel.SetBounds(pad, Theme.S(80), colW, Theme.S(15));
            _offsetLabel.SetBounds(pad + colW + colGap, Theme.S(80), colW, Theme.S(15));
            _base.SetBounds(pad, Theme.S(97), colW, Theme.S(28));
            _offsets.SetBounds(pad + colW + colGap, Theme.S(97), colW, Theme.S(28));

            _intervalLabel.SetBounds(pad, Theme.S(132), Theme.S(100), Theme.S(24));
            _interval.SetBounds(pad + Theme.S(106), Theme.S(132), Theme.S(104), Theme.S(28));
            _status.SetBounds(pad, Theme.S(166), Width - pad * 2, Theme.S(18));
            _disclaimer.SetBounds(pad, Theme.S(188), Width - pad * 2, Theme.S(48));
        }
    }
}
