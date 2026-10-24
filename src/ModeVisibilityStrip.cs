using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MobileUiConfig
{
    public class ModeVisibilityStrip : Control
    {
        private List<ModeConfig> _modes = new List<ModeConfig>();
        private bool _joystick;
        private int _hover = -1;
        private Color _accent = Theme.Accent;

        public event Action<int> Toggled;

        public ModeVisibilityStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint |
                     ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            Height = Theme.S(32);
            Cursor = Cursors.Hand;
        }

        public Color Accent
        {
            get { return _accent; }
            set { _accent = value; Invalidate(); }
        }

        public void SetModes(List<ModeConfig> modes, bool joystick)
        {
            _modes = modes == null ? new List<ModeConfig>() : modes;
            _joystick = joystick;
            _hover = -1;
            Invalidate();
        }

        private RectangleF ChipRect(int index)
        {
            int count = Math.Max(1, _modes.Count);
            float gap = Theme.S(6);
            float width = (Width - gap * (count - 1)) / count;
            if (width > Theme.S(92)) width = Theme.S(92);
            if (width < Theme.S(42)) width = Theme.S(42);
            return new RectangleF(index * (width + gap), 0, width, Height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            for (int i = 0; i < _modes.Count; i++)
            {
                ModeConfig mode = _modes[i];
                if (mode == null) continue;
                bool on = _joystick ? mode.ShowJoystick : mode.ShowMouseLook;
                RectangleF rect = ChipRect(i);
                Color fill = on
                    ? Color.FromArgb(48, _accent)
                    : (_hover == i ? Color.FromArgb(32, 255, 255, 255) : Color.FromArgb(22, 31, 47));
                Theme.FillRounded(e.Graphics, rect, Theme.S(8), fill);
                Theme.DrawRounded(e.Graphics, rect, Theme.S(8),
                    on ? Theme.Alpha(_accent, 220) : Theme.StrokeSoft, on ? 1.2f : 1f);
                if (on)
                {
                    Theme.FillCircle(e.Graphics, new RectangleF(rect.X + Theme.S(7), rect.Y + rect.Height / 2 - Theme.S(3),
                        Theme.S(6), Theme.S(6)), _accent);
                }
                string name = string.IsNullOrEmpty(mode.Name) ? "模式" : mode.Name;
                float textLeft = on ? Theme.S(17) : Theme.S(6);
                Theme.DrawText(e.Graphics, name, Theme.UiFont(7.6f, false),
                    on ? Color.White : Theme.Muted,
                    new RectangleF(rect.X + textLeft, rect.Y, rect.Width - textLeft - Theme.S(5), rect.Height),
                    StringAlignment.Center, StringAlignment.Center, true);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hover = -1;
            for (int i = 0; i < _modes.Count; i++) if (ChipRect(i).Contains(e.Location)) hover = i;
            if (hover != _hover)
            {
                _hover = hover;
                Cursor = hover >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            for (int i = 0; i < _modes.Count; i++)
            {
                if (!ChipRect(i).Contains(e.Location)) continue;
                ModeConfig mode = _modes[i];
                if (mode == null) return;
                if (_joystick) mode.ShowJoystick = !mode.ShowJoystick;
                else mode.ShowMouseLook = !mode.ShowMouseLook;
                Invalidate();
                Action<int> h = Toggled;
                if (h != null) h(i);
                return;
            }
            base.OnMouseDown(e);
        }
    }
}
