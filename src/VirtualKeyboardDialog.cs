using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MobileUiConfig
{
    public class VirtualKeyboardDialog : Form
    {
        public KeyBinding Selected;
        private CheckBox _ctrlBox;
        private CheckBox _shiftBox;
        private CheckBox _altBox;
        private Label _currentLabel;
        private Label _modLabel;
        private Label _titleLabel;
        private Label _mouseLabel;
        private readonly List<ModernButton> _mouseButtons = new List<ModernButton>();
        private ModernButton _clearBtn;
        private ModernButton _cancelBtn;
        private ModernButton _okBtn;

        // 每行按键：记录按钮 + 基准宽度（相对值，按比例缩放）
        private class KeyBtn
        {
            public ModernButton Btn;
            public int BaseWidth;
        }
        private readonly List<List<KeyBtn>> _keyRows = new List<List<KeyBtn>>();

        // 基准设计宽度（所有按键在此宽度下的像素尺寸）
        private const int DesignWidth = 820;
        private const int Pad = 16;
        private const int Gap = 5;
        private const int KeyH = 38;
        private const int RowH = 32;

        public VirtualKeyboardDialog(KeyBinding current)
        {
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Text = "选择按键";
            Font = Theme.UiFont(9f, false);
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;

            // 初始 ClientSize（足够宽，给 DPI 缩放留余量）
            int contentH = Pad + 24 + 10 + RowH + 14 +
                (KeyH + Gap) * 5 + KeyH + 10 + KeyH + 18 +
                RowH + 16 + 40 + Pad;

            ClientSize = new Size(DesignWidth, contentH);
            MinimumSize = new Size(DesignWidth - 60, contentH);

            // === 顶部标题 ===
            _titleLabel = new Label();
            _titleLabel.Text = "从虚拟键盘点选按键，支持组合修饰键";
            _titleLabel.Font = Theme.UiFont(9.4f, false);
            _titleLabel.ForeColor = Theme.Muted;
            _titleLabel.AutoSize = true;
            _titleLabel.Location = new Point(Pad, Pad);
            Controls.Add(_titleLabel);

            int y = _titleLabel.Bottom + 10;

            // === 修饰键勾选行 ===
            _modLabel = new Label();
            _modLabel.Text = "组合修饰键：";
            _modLabel.Font = Theme.UiFont(9f, false);
            _modLabel.ForeColor = Theme.TextSoft;
            _modLabel.AutoSize = true;
            Controls.Add(_modLabel);

            _ctrlBox = MakeModCheckBox("Ctrl");
            _shiftBox = MakeModCheckBox("Shift");
            _altBox = MakeModCheckBox("Alt");
            Controls.AddRange(new Control[] { _ctrlBox, _shiftBox, _altBox });

            _currentLabel = new Label();
            _currentLabel.Font = Theme.Semibold(10f);
            _currentLabel.ForeColor = Theme.Accent;
            _currentLabel.AutoSize = true;
            Controls.Add(_currentLabel);

            // === 7 行键盘 ===
            AddKeyRow(new KeySpec[]
            {
                K("Esc", Keys.Escape, 50),
                K("F1", Keys.F1, 42), K("F2", Keys.F2, 42), K("F3", Keys.F3, 42), K("F4", Keys.F4, 42),
                K("F5", Keys.F5, 42), K("F6", Keys.F6, 42), K("F7", Keys.F7, 42), K("F8", Keys.F8, 42),
                K("F9", Keys.F9, 42), K("F10", Keys.F10, 42), K("F11", Keys.F11, 42), K("F12", Keys.F12, 42),
            });
            AddKeyRow(new KeySpec[]
            {
                K("`", Keys.Oem3, 38), K("1", Keys.D1, 38), K("2", Keys.D2, 38), K("3", Keys.D3, 38),
                K("4", Keys.D4, 38), K("5", Keys.D5, 38), K("6", Keys.D6, 38), K("7", Keys.D7, 38),
                K("8", Keys.D8, 38), K("9", Keys.D9, 38), K("0", Keys.D0, 38), K("-", Keys.OemMinus, 38),
                K("=", Keys.Oemplus, 38), K("⌫", Keys.Back, 72),
            });
            AddKeyRow(new KeySpec[]
            {
                K("Tab", Keys.Tab, 56), K("Q", Keys.Q, 38), K("W", Keys.W, 38), K("E", Keys.E, 38),
                K("R", Keys.R, 38), K("T", Keys.T, 38), K("Y", Keys.Y, 38), K("U", Keys.U, 38),
                K("I", Keys.I, 38), K("O", Keys.O, 38), K("P", Keys.P, 38), K("[", Keys.Oem4, 38),
                K("]", Keys.Oem6, 38), K("\\", Keys.Oem5, 38),
            });
            AddKeyRow(new KeySpec[]
            {
                K("Caps", Keys.CapsLock, 56), K("A", Keys.A, 38), K("S", Keys.S, 38), K("D", Keys.D, 38),
                K("F", Keys.F, 38), K("G", Keys.G, 38), K("H", Keys.H, 38), K("J", Keys.J, 38),
                K("K", Keys.K, 38), K("L", Keys.L, 38), K(";", Keys.Oem1, 38), K("'", Keys.Oem7, 38),
                K("Enter", Keys.Enter, 78),
            });
            AddKeyRow(new KeySpec[]
            {
                K("Shift", Keys.ShiftKey, 70), K("Z", Keys.Z, 38), K("X", Keys.X, 38), K("C", Keys.C, 38),
                K("V", Keys.V, 38), K("B", Keys.B, 38), K("N", Keys.N, 38), K("M", Keys.M, 38),
                K(",", Keys.Oemcomma, 38), K(".", Keys.OemPeriod, 38), K("/", Keys.Oem2, 38),
                K("Shift", Keys.ShiftKey, 70),
            });
            AddKeyRow(new KeySpec[]
            {
                K("Ctrl", Keys.ControlKey, 64), K("Alt", Keys.Menu, 64),
                K("Space", Keys.Space, 200),
                K("Alt", Keys.Menu, 64), K("Ctrl", Keys.ControlKey, 64),
                K("↑", Keys.Up, 44),
            });
            AddKeyRow(new KeySpec[]
            {
                K("Win", Keys.LWin, 64),
                K("←", Keys.Left, 44), K("↓", Keys.Down, 44), K("→", Keys.Right, 44),
                K("Home", Keys.Home, 46), K("End", Keys.End, 46),
                K("PgUp", Keys.PageUp, 46), K("PgDn", Keys.PageDown, 46),
                K("Del", Keys.Delete, 46), K("Ins", Keys.Insert, 46),
            });

            // === 鼠标绑定行 ===
            _mouseLabel = new Label();
            _mouseLabel.Text = "鼠标绑定：";
            _mouseLabel.Font = Theme.UiFont(9f, false);
            _mouseLabel.ForeColor = Theme.TextSoft;
            _mouseLabel.AutoSize = true;
            Controls.Add(_mouseLabel);

            AddMouseButton("鼠标左键", BindingKind.MouseButton, 0);
            AddMouseButton("鼠标右键", BindingKind.MouseButton, 1);
            AddMouseButton("鼠标中键", BindingKind.MouseButton, 2);
            AddMouseButton("滚轮↑", BindingKind.MouseWheel, 1);
            AddMouseButton("滚轮↓", BindingKind.MouseWheel, -1);

            // === 底部按钮栏 ===
            _clearBtn = new ModernButton();
            _clearBtn.Text = "清除绑定";
            _clearBtn.Style = ModernButtonStyle.Danger;
            _clearBtn.Height = 36;
            _clearBtn.Width = 110;
            _clearBtn.Click += delegate { Selected = new KeyBinding(); DialogResult = DialogResult.OK; Close(); };
            Controls.Add(_clearBtn);

            _cancelBtn = new ModernButton();
            _cancelBtn.Text = "取消";
            _cancelBtn.Style = ModernButtonStyle.Ghost;
            _cancelBtn.Height = 36;
            _cancelBtn.Width = 88;
            _cancelBtn.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_cancelBtn);

            _okBtn = new ModernButton();
            _okBtn.Text = "确定";
            _okBtn.Style = ModernButtonStyle.Primary;
            _okBtn.Height = 36;
            _okBtn.Width = 88;
            _okBtn.Click += delegate { DialogResult = DialogResult.OK; Close(); };
            Controls.Add(_okBtn);

            // 锚定底部
            _clearBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            _cancelBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            _okBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            // 初始化当前按键显示
            if (current != null && !current.IsEmpty)
            {
                _ctrlBox.Checked = (current.Modifiers & (int)Keys.Control) != 0;
                _shiftBox.Checked = (current.Modifiers & (int)Keys.Shift) != 0;
                _altBox.Checked = (current.Modifiers & (int)Keys.Alt) != 0;
            }
            Selected = current == null ? null : current.Clone();
            RefreshCurrentLabel();

            // 首次 + 后续 Resize 都重新排布
            Resize += delegate { LayoutAll(); };
            LayoutAll();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LayoutAll();
        }

        private void LayoutAll()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            if (w < MinimumSize.Width) w = MinimumSize.Width;

            int availW = w - 2 * Pad;

            // === 标题 ===
            _titleLabel.Location = new Point(Pad, Pad);

            // === 修饰键行（标题下方）===
            int modY = _titleLabel.Bottom + 10;
            _modLabel.Location = new Point(Pad, modY + 6);
            // 三个修饰键复选框按比例排布
            int cbX = _modLabel.Right + 14;
            _ctrlBox.Location = new Point(cbX, modY + 4); cbX += _ctrlBox.Width + 18;
            _shiftBox.Location = new Point(cbX, modY + 4); cbX += _shiftBox.Width + 18;
            _altBox.Location = new Point(cbX, modY + 4);

            // 当前按键标签靠右
            int rightEdge = w - Pad;
            int labelW = TextRenderer.MeasureText(_currentLabel.Text, _currentLabel.Font).Width;
            _currentLabel.Location = new Point(Math.Max(_altBox.Right + 20, rightEdge - labelW), modY + 6);

            // === 计算最宽行的基准总宽，得到缩放因子 ===
            int maxBase = 0;
            foreach (var row in _keyRows)
            {
                int s = 0;
                foreach (var k in row) s += k.BaseWidth;
                s += Gap * (row.Count - 1);
                if (s > maxBase) maxBase = s;
            }
            if (maxBase <= 0) return;

            float scale = (float)availW / maxBase;
            scale = Math.Max(0.7f, Math.Min(1.5f, scale));
            float keyHReal = KeyH * scale;

            // === 排布 7 行键盘（修饰键行下方）===
            int y = modY + RowH + 14;
            for (int r = 0; r < _keyRows.Count; r++)
            {
                var row = _keyRows[r];
                int rowTotalW = 0;
                foreach (var k in row) rowTotalW += (int)Math.Round(k.BaseWidth * scale);
                rowTotalW += Gap * (row.Count - 1);
                int xOff = Pad + Math.Max(0, (availW - rowTotalW) / 2);

                int x = xOff;
                foreach (var k in row)
                {
                    int kw = (int)Math.Round(k.BaseWidth * scale);
                    k.Btn.Width = kw;
                    k.Btn.Height = (int)Math.Round(keyHReal);
                    k.Btn.Location = new Point(x, y);
                    x += kw + Gap;
                }
                y += (int)Math.Round(keyHReal) + Gap;
            }

            // === 鼠标绑定行（最后一行键盘下方）===
            y += 14;
            _mouseLabel.Location = new Point(Pad, y + 7);

            // 5 个鼠标按钮也按 scale 等比缩放，保证窗口变窄时不会被挤出边界
            int mouseBtnBaseW = 76;
            int mouseBtnBaseGap = 10;
            int realMouseW = Math.Max(52, (int)Math.Round(mouseBtnBaseW * scale));
            int realGap = Math.Max(6, (int)Math.Round(mouseBtnBaseGap * scale));
            int mouseRowTotalW = realMouseW * _mouseButtons.Count + realGap * (_mouseButtons.Count - 1);
            int mStartX = Pad + Math.Max(0, (availW - mouseRowTotalW) / 2);
            int mx = mStartX;
            foreach (var btn in _mouseButtons)
            {
                btn.Width = realMouseW;
                btn.Location = new Point(mx, y);
                mx += realMouseW + realGap;
            }

            // === 底部按钮栏（窗口底部）===
            int bottomY = h - Pad - 36;
            _clearBtn.Location = new Point(Pad, bottomY);
            _cancelBtn.Location = new Point(w - Pad - _cancelBtn.Width - _okBtn.Width - 12, bottomY);
            _okBtn.Location = new Point(w - Pad - _okBtn.Width, bottomY);
        }

        private void AddKeyRow(KeySpec[] specs)
        {
            var row = new List<KeyBtn>();
            foreach (var spec in specs)
            {
                ModernButton btn = new ModernButton();
                btn.Text = spec.Label;
                btn.Style = ModernButtonStyle.Soft;
                btn.Font = Theme.UiFont(8.6f, false);
                btn.Height = KeyH;
                btn.Width = spec.Width;
                Keys kc = spec.Key;
                btn.Click += delegate
                {
                    // 排除主键自身对应的修饰键复选框，避免 "Alt + Alt" 自引用
                    int mods = 0;
                    bool isCtrlKc = kc == Keys.ControlKey || kc == Keys.LControlKey || kc == Keys.RControlKey;
                    bool isShiftKc = kc == Keys.ShiftKey || kc == Keys.LShiftKey || kc == Keys.RShiftKey;
                    bool isAltKc = kc == Keys.Menu || kc == Keys.LMenu || kc == Keys.RMenu;
                    if (_ctrlBox.Checked && !isCtrlKc) mods |= (int)Keys.Control;
                    if (_shiftBox.Checked && !isShiftKc) mods |= (int)Keys.Shift;
                    if (_altBox.Checked && !isAltKc) mods |= (int)Keys.Alt;
                    Selected = new KeyBinding(BindingKind.Keyboard, (int)kc, mods);
                    RefreshCurrentLabel();
                };
                Controls.Add(btn);
                row.Add(new KeyBtn { Btn = btn, BaseWidth = spec.Width });
            }
            _keyRows.Add(row);
        }

        private CheckBox MakeModCheckBox(string text)
        {
            CheckBox cb = new CheckBox();
            cb.Text = text;
            cb.Font = Theme.UiFont(9f, false);
            cb.ForeColor = Theme.Text;
            cb.AutoSize = true;
            cb.UseVisualStyleBackColor = true;
            cb.CheckedChanged += delegate { RefreshCurrentLabel(); };
            return cb;
        }

        private void AddMouseButton(string label, BindingKind kind, int code)
        {
            ModernButton btn = new ModernButton();
            btn.Text = label;
            btn.Style = ModernButtonStyle.Link;
            btn.Font = Theme.UiFont(8.4f, false);
            btn.Height = 32;
            btn.Width = 76;
            btn.Click += delegate
            {
                Selected = new KeyBinding(kind, code);
                _ctrlBox.Checked = false;
                _shiftBox.Checked = false;
                _altBox.Checked = false;
                RefreshCurrentLabel();
            };
            Controls.Add(btn);
            _mouseButtons.Add(btn);
        }

        private void RefreshCurrentLabel()
        {
            if (Selected == null || Selected.IsEmpty)
            {
                _currentLabel.Text = "当前：未绑定";
                _currentLabel.ForeColor = Theme.Faint;
            }
            else
            {
                _currentLabel.Text = "当前：" + Selected.DisplayName;
                _currentLabel.ForeColor = Theme.Accent;
            }
        }

        private static KeySpec K(string label, Keys key, int width)
        {
            return new KeySpec { Label = label, Key = key, Width = width };
        }

        private struct KeySpec
        {
            public string Label;
            public Keys Key;
            public int Width;
        }
    }
}
