using System;
using System.Drawing;
using System.Windows.Forms;

namespace MobileUiConfig
{
    public class PromptDialog : Form
    {
        private readonly string _title;
        private readonly string _message;
        private readonly bool _showInput;
        private readonly bool _danger;
        private readonly RoundTextBox _input;
        private readonly ModernButton _ok;
        private readonly ModernButton _cancel;

        private PromptDialog(string title, string message, string initial, bool showInput, bool danger, string okText)
        {
            _title = title == null ? "" : title;
            _message = message == null ? "" : message;
            _showInput = showInput;
            _danger = danger;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(9.4f, false);
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(452), Theme.S(showInput ? 230 : 206));
            DoubleBuffered = true;
            KeyPreview = true;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            Padding = new Padding(1);

            if (showInput)
            {
                _input = new RoundTextBox();
                _input.Placeholder = "输入名称";
                _input.Text = initial == null ? "" : initial;
                _input.FillColor = Theme.SurfaceAlt;
                Controls.Add(_input);
            }

            _ok = new ModernButton();
            _ok.Text = okText == null ? "确定" : okText;
            _ok.Style = danger ? ModernButtonStyle.Danger : ModernButtonStyle.Primary;
            _ok.Height = 38;
            _ok.Width = 88;
            _ok.Click += new EventHandler(OkOnClick);
            Controls.Add(_ok);

            _cancel = new ModernButton();
            _cancel.Text = "取消";
            _cancel.Style = ModernButtonStyle.Secondary;
            _cancel.Height = 38;
            _cancel.Width = 78;
            _cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_cancel);

            KeyDown += new KeyEventHandler(DialogOnKeyDown);
            MouseDown += new MouseEventHandler(DialogOnMouseDown);
            Shown += new EventHandler(DialogOnShown);
        }

        public string Value
        {
            get { return _input == null ? "" : _input.Text.Trim(); }
        }

        public static string ShowRename(IWin32Window owner, string current)
        {
            using (PromptDialog dlg = new PromptDialog("重命名模式", "为当前模式设置一个便于识别的名称。", current, true, false, "保存名称"))
            {
                if (dlg.ShowDialog(owner) == DialogResult.OK)
                {
                    return dlg.Value;
                }
            }
            return null;
        }

        public static string ShowGameName(IWin32Window owner, string current)
        {
            using (PromptDialog dlg = new PromptDialog("保存游戏", "给当前游戏起一个名称，保存后会显示在游戏配置栏。",
                current, true, false, "保存游戏"))
            {
                if (dlg.ShowDialog(owner) == DialogResult.OK) return dlg.Value;
            }
            return null;
        }

        public static bool Confirm(IWin32Window owner, string title, string message, string okText)
        {
            using (PromptDialog dlg = new PromptDialog(title, message, "", false, true, okText))
            {
                return dlg.ShowDialog(owner) == DialogResult.OK;
            }
        }

        private void DialogOnShown(object sender, EventArgs e)
        {
            if (_input != null)
            {
                _input.InnerTextBox.Focus();
                _input.InnerTextBox.SelectAll();
            }
            else
            {
                _ok.Focus();
            }
            Invalidate();
        }

        private void OkOnClick(object sender, EventArgs e)
        {
            if (_showInput && (_input == null || Value.Length == 0))
            {
                _input.InnerTextBox.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        private void DialogOnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                DialogResult = DialogResult.Cancel;
                Close();
            }
            else if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                OkOnClick(null, EventArgs.Empty);
            }
        }

        private void DialogOnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y < 56 * Theme.Scale)
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, new IntPtr(NativeMethods.HTCAPTION), IntPtr.Zero);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Theme.SetupGraphics(e.Graphics);
            float s = Theme.Scale;
            RectangleF rect = new RectangleF(0.5f, 0.5f, ClientSize.Width - 1f, ClientSize.Height - 1f);
            using (SolidBrush brush = new SolidBrush(Theme.Surface))
            {
                Theme.FillRounded(e.Graphics, rect, 14f * s, brush);
            }
            Theme.DrawRounded(e.Graphics, rect, 14f * s, Theme.Stroke, 1f);

            Color accent = _danger ? Theme.Danger : Theme.Accent;
            Theme.FillRounded(e.Graphics, new RectangleF(22 * s, 22 * s, 4 * s, 24 * s), 2f * s, accent);
            Theme.DrawText(e.Graphics, _title, Theme.Semibold(13f), Theme.Text,
                new RectangleF(38 * s, 16 * s, ClientSize.Width - 70 * s, 34 * s), StringAlignment.Near, StringAlignment.Center, true);
            Theme.DrawText(e.Graphics, _message, Theme.UiFont(8.9f, false), Theme.Muted,
                new RectangleF(38 * s, 50 * s, ClientSize.Width - 70 * s, 42 * s), StringAlignment.Near, StringAlignment.Near, true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_ok == null || _cancel == null) return;
            float s = Theme.Scale;
            int left = 22;
            int right = ClientSize.Width - 22;
            if (_input != null)
            {
                _input.SetBounds(left, (int)(96 * s), right - left, (int)(42 * s));
            }
            int buttonY = ClientSize.Height - (int)(38 * s) - 20;
            _ok.SetBounds(right - (int)(88 * s), buttonY, (int)(88 * s), (int)(38 * s));
            _cancel.SetBounds(_ok.Left - (int)(8 * s) - (int)(78 * s), buttonY, (int)(78 * s), (int)(38 * s));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int preference = 2;
                NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, 4);
            }
            catch
            {
            }
        }
    }
}
