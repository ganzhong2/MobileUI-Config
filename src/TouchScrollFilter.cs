using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MobileUiConfig
{
    // Touch on the settings UI is delivered as a promoted mouse event. This
    // filter turns a vertical touch drag anywhere inside a SmoothScrollPane
    // into a scroll, and replays a normal click when the finger only taps.
    internal sealed class TouchScrollFilter : IMessageFilter
    {
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;

        private Control _target;
        private SmoothScrollPane _pane;
        private Point _startScreen;
        private int _lastY;
        private bool _dragging;
        private bool _active;
        private int _startOffset;

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_LBUTTONDOWN && m.Msg != WM_MOUSEMOVE && m.Msg != WM_LBUTTONUP)
                return false;
            if (!IsTouchMouse()) return false;

            if (m.Msg == WM_LBUTTONDOWN)
            {
                Control target = Control.FromChildHandle(m.HWnd);
                if (target == null) return false;
                // These controls have their own drag gestures.
                if (target is ModernSlider || target is PhonePreview) return false;
                SmoothScrollPane pane = FindPane(target);
                if (pane == null || pane.MaximumOffset <= 0) return false;
                // Let a touch that starts on the scrollbar use the native
                // scrollbar drag path, but allow swiping the blank pane.
                if (target == pane)
                {
                    Point local = pane.PointToClient(Cursor.Position);
                    if (local.X >= pane.Width - Theme.S(16)) return false;
                }

                _active = true;
                _target = target;
                _pane = pane;
                _startScreen = Cursor.Position;
                _lastY = _startScreen.Y;
                _startOffset = pane.ScrollOffset;
                _dragging = false;
                return true;
            }

            if (!_active) return false;

            if (m.Msg == WM_MOUSEMOVE)
            {
                Point now = Cursor.Position;
                int dy = now.Y - _startScreen.Y;
                if (!_dragging && Math.Abs(dy) >= Theme.S(10))
                    _dragging = true;
                if (_dragging)
                {
                    int delta = _lastY - now.Y;
                    if (delta != 0) _pane.ScrollBy(delta);
                    _lastY = now.Y;
                }
                return true;
            }

            // WM_LBUTTONUP
            if (_dragging)
            {
                _active = false;
                _dragging = false;
                _target = null;
                _pane = null;
                return true;
            }

            Control clicked = _target;
            Point screen = _startScreen;
            _active = false;
            _target = null;
            _pane = null;
            if (clicked != null && clicked.IsHandleCreated)
                ReplayClick(clicked, screen);
            return true;
        }

        private static SmoothScrollPane FindPane(Control control)
        {
            Control c = control;
            while (c != null)
            {
                SmoothScrollPane pane = c as SmoothScrollPane;
                if (pane != null) return pane;
                c = c.Parent;
            }
            return null;
        }

        private static void ReplayClick(Control target, Point screen)
        {
            try
            {
                Point client = target.PointToClient(screen);
                int lparam = (client.Y << 16) | (client.X & 0xFFFF);
                SendMessage(target.Handle, WM_LBUTTONDOWN, (IntPtr)1, (IntPtr)lparam);
                SendMessage(target.Handle, WM_LBUTTONUP, IntPtr.Zero, (IntPtr)lparam);
            }
            catch { }
        }

        private static bool IsTouchMouse()
        {
            try
            {
                long extra = GetMessageExtraInfo().ToInt64();
                return (extra & 0xFFFFFF00L) == 0xFF515700L;
            }
            catch { return false; }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetMessageExtraInfo();
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
