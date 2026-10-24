using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MobileUiConfig
{
    public class PhonePreview : Control
    {
        private AppConfig _config;
        // 系统显示键 / 模式键是主配置全局项，与当前页（按键页 / 手柄页）根无关，
        // 单独持有主根引用；未注入时回退到 _config，保持按键页兼容。
        private AppConfig _global;
        private AppConfig SysRoot { get { return _global != null ? _global : _config; } }
        private int _modeIndex;
        private int _viewState;
        private bool _modeMenuOpen;
        private VirtualKey _selectedKey;
        private VirtualKey _selectedOverlayKey;
        private bool _dragTargetIsOverlay;
        private DisplayOrientation _orientation = DisplayOrientation.Landscape;

        private RectangleF _stage;
        private RectangleF _device;
        private RectangleF _screen;
        private float _uiScale = 1f;
        private float _screenScale = 1f;

        private object _dragTarget;
        private List<VirtualKey> _dragDpadKeys;
        private List<VirtualKey> _pendingDpadKeys;
        private bool _pendingDpadOverlay;
        private Point _mouseDownPoint;
        private Point _lastMousePoint;
        private bool _mouseDown;
        private bool _dragging;
        private float _dragStartX;
        private float _dragStartY;
        private float _dragOffsetX;
        private float _dragOffsetY;
        private int _hoverMenuIndex = -1;
        private object _hoverTarget;
        private bool _joystickKnobDragging;
        private float _joystickDx;
        private float _joystickDy;
        private bool _joystickCollapsed;
        private Timer _joystickHideTimer;
        private int _joystickHideMs;
        private bool _lookDragging;
        private float _lookDx;
        private float _lookDy;
        private List<ModeConfig> _menuModes = new List<ModeConfig>();
        private List<RectangleF> _menuRects = new List<RectangleF>();
        private RectangleF _menuBounds;

        private readonly Dictionary<string, Image> _imageCache = new Dictionary<string, Image>();

        public event Action<int> ModeSwitchRequested;
        public event Action<VirtualKey> KeySelected;
        public event Action LayoutChanged;
        public event Action<int> StickSelected;
        public event Action<int> ViewStateChanged;
        public event Action<int> WheelScrolled;

        public PhonePreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Surface;
            _viewState = 0;
            _joystickHideTimer = new Timer();
            _joystickHideTimer.Tick += JoystickHideTick;
        }

        public AppConfig Config
        {
            get { return _config; }
            set
            {
                _config = value;
                ClearImageCache();
                Invalidate();
            }
        }

        public AppConfig GlobalConfig
        {
            get { return _global; }
            set { _global = value; Invalidate(); }
        }

        // 虚拟手柄页：隐藏显示键 / 模式键等触屏系统操控卡片。
        public bool GamepadMode { get; set; }

        public int ActiveModeIndex
        {
            get { return _modeIndex; }
            set
            {
                _modeIndex = value;
                if (_config != null && _config.Modes != null && _config.Modes.Count > 0)
                {
                    if (_modeIndex < 0) _modeIndex = 0;
                    if (_modeIndex >= _config.Modes.Count) _modeIndex = _config.Modes.Count - 1;
                }
                _modeMenuOpen = false;
                ResetJoystickCollapsed();
                Invalidate();
            }
        }

        // 预览中当前选中的叠加模式索引；切换时清空叠加按键选中态，避免串味。
        public int OverlayModeIndex
        {
            get { return _config == null ? 0 : _config.SelectedOverlayMode; }
            set
            {
                if (_config == null || _config.OverlayModes == null) return;
                if (value < 0) value = 0;
                if (value >= _config.OverlayModes.Count) value = _config.OverlayModes.Count - 1;
                _config.SelectedOverlayMode = value;
                _selectedOverlayKey = null;
                _dragTargetIsOverlay = false;
                Invalidate();
            }
        }

        public int ViewState
        {
            get { return _viewState; }
        }

        public DisplayOrientation Orientation
        {
            get { return _orientation; }
            set
            {
                if (_orientation == value) return;
                _orientation = value;
                _modeMenuOpen = false;
                Invalidate();
            }
        }

        public bool ModeMenuOpen
        {
            get { return _modeMenuOpen; }
        }

        public VirtualKey SelectedKey
        {
            get { return _selectedKey; }
            set { _selectedKey = value; Invalidate(); }
        }

        public void SetViewState(int state)
        {
            if (state < 0 || state > 2) state = 0;
            if (_viewState == state) return;
            _viewState = state;
            if (_viewState == 1) _modeMenuOpen = false;
            _hoverMenuIndex = -1;
            Invalidate();
            Action<int> h = ViewStateChanged;
            if (h != null) h(_viewState);
        }

        public void SetSelectedKeySilent(VirtualKey key)
        {
            _selectedKey = key;
            Invalidate();
        }

        public void SetSelectedOverlayKeySilent(VirtualKey key)
        {
            _selectedOverlayKey = key;
            _dragTargetIsOverlay = false;
            Invalidate();
        }

        public void SetModeMenuOpen(bool open)
        {
            _modeMenuOpen = open;
            _hoverMenuIndex = -1;
            Invalidate();
        }

        public void ClearImageCache()
        {
            foreach (KeyValuePair<string, Image> pair in _imageCache)
            {
                if (pair.Value != null) pair.Value.Dispose();
            }
            _imageCache.Clear();
            Invalidate();
        }

        public void ReloadImages()
        {
            ClearImageCache();
        }

        private ModeConfig ActiveMode
        {
            get
            {
                if (_config == null || _config.Modes == null || _config.Modes.Count == 0) return null;
                int i = _modeIndex;
                if (i < 0) i = 0;
                if (i >= _config.Modes.Count) i = _config.Modes.Count - 1;
                return _config.Modes[i];
            }
        }

        // 当前预览选中的叠加模式；摇杆、鼠标视角仍跟随主模式，叠加层只承载按键。
        private OverlayModeConfig ActiveOverlayMode
        {
            get { return _config == null ? null : _config.ActiveOverlayMode; }
        }

        // 勾选「叠加层独立预览」：预览只渲染叠加层按键，隐藏主模式按键。
        private bool OverlaySolo
        {
            get { return _config != null && _config.PreviewSoloMode == 2; }
        }

        // 勾选「主界面独立预览」：预览只渲染主模式按键，隐藏叠加层按键。
        private bool MainSolo
        {
            get { return _config != null && _config.PreviewSoloMode == 1; }
        }

        private List<VirtualKey> VisibleOverlayKeys()
        {
            List<VirtualKey> list = new List<VirtualKey>();
            OverlayModeConfig overlay = ActiveOverlayMode;
            if (overlay == null || !overlay.Enabled || overlay.Keys == null) return list;
            for (int i = 0; i < overlay.Keys.Count; i++)
            {
                VirtualKey k = overlay.Keys[i];
                if (k == null) continue;
                if (_viewState == 1 && k.HideWithHidden) continue;
                list.Add(k);
            }
            return list;
        }

        // 摇杆配置按模式独立：只取当前生效根当前模式的摇杆，不回退全局遗留字段，避免两页串用。
        private JoystickConfig CurrentJoystick()
        {
            ModeConfig mode = ActiveMode;
            return mode == null ? null : mode.Joystick;
        }

        // 右摇杆配置按模式独立，仅用于预览静态展示，不参与拖动交互。
        private JoystickConfig CurrentJoystickR()
        {
            ModeConfig mode = ActiveMode;
            return mode == null ? null : mode.JoystickR;
        }

        private bool JoystickRVisible()
        {
            if (!GamepadMode) return false;
            JoystickConfig joy = CurrentJoystickR();
            if (joy == null || !joy.Enabled) return false;
            ModeConfig mode = ActiveMode;
            if (mode != null && !mode.ShowJoystick) return false;
            if (_viewState == 1 && joy.HideWithHidden) return false;
            return true;
        }

        private RectangleF JoystickRRect()
        {
            JoystickConfig joy = CurrentJoystickR();
            if (joy == null || !joy.Enabled) return RectangleF.Empty;
            float d = joy.Size * _screenScale;
            PointF c = new PointF(_screen.Left + joy.X * _screen.Width,
                _screen.Top + joy.Y * _screen.Height);
            return new RectangleF(c.X - d / 2f, c.Y - d / 2f, d, d);
        }

        // 方向键整体外观配置：位置 / 大小 / 透明度 / 显隐由 DpadConfig 统一管理；四向触发仍走四个方向键。
        private DpadConfig CurrentDpad()
        {
            ModeConfig mode = ActiveMode;
            return mode == null ? null : mode.Dpad;
        }

        private bool DpadVisible()
        {
            if (!GamepadMode) return false;
            DpadConfig d = CurrentDpad();
            if (d == null || !d.Enabled) return false;
            if (_viewState == 1 && d.HideWithHidden) return false;
            return true;
        }

        private RectangleF DpadRect()
        {
            DpadConfig d = CurrentDpad();
            if (d == null || !d.Enabled) return RectangleF.Empty;
            float dd = d.Size * _screenScale;
            PointF c = new PointF(_screen.Left + d.X * _screen.Width,
                _screen.Top + d.Y * _screen.Height);
            return new RectangleF(c.X - dd / 2f, c.Y - dd / 2f, dd, dd);
        }

        private bool JoystickVisible()
        {
            JoystickConfig joy = CurrentJoystick();
            if (joy == null || !joy.Enabled) return false;
            ModeConfig mode = ActiveMode;
            if (mode != null && !mode.ShowJoystick) return false;
            if (_viewState == 1 && joy.HideWithHidden) return false;
            return true;
        }

        // 外圈自动收起状态机：拖动中常显；松手后按 AutoHideSeconds 倒计时，
        // 超时只保留内圈；点击内圈恢复外圈并重新计时。0 = 常显外圈。
        private void ResetJoystickCollapsed()
        {
            if (_joystickHideTimer != null) _joystickHideTimer.Stop();
            _joystickCollapsed = false;
            _joystickHideMs = 0;
        }

        private void CancelJoystickHideTimer()
        {
            if (_joystickHideTimer != null) _joystickHideTimer.Stop();
        }

        private void RestartJoystickHideTimer()
        {
            JoystickConfig joy = CurrentJoystick();
            if (_joystickHideTimer == null) return;
            _joystickHideTimer.Stop();
            if (joy == null || joy.AutoHideSeconds <= 0)
            {
                _joystickCollapsed = false;
                return;
            }
            _joystickHideMs = joy.AutoHideSeconds * 1000;
            _joystickHideTimer.Interval = Math.Max(200, _joystickHideMs);
            _joystickHideTimer.Start();
        }

        private void JoystickHideTick(object sender, EventArgs e)
        {
            if (_joystickHideTimer != null) _joystickHideTimer.Stop();
            if (_joystickKnobDragging) return;
            JoystickConfig joy = CurrentJoystick();
            if (joy == null || joy.AutoHideSeconds <= 0) return;
            _joystickCollapsed = true;
            Invalidate();
        }

        private void RestoreJoystickOuter()
        {
            _joystickCollapsed = false;
            RestartJoystickHideTimer();
            Invalidate();
        }

        private Image GetImage(VirtualKey key)
        {
            if (key == null || string.IsNullOrEmpty(key.ImageFile)) return null;
            Image img;
            if (_imageCache.TryGetValue(key.ImageFile, out img)) return img;
            img = ConfigStore.LoadImageCopy(key.ImageFile);
            if (img != null) _imageCache[key.ImageFile] = img;
            return img;
        }

        private PointF KeyCenter(VirtualKey key)
        {
            return new PointF(_screen.Left + key.X * _screen.Width, _screen.Top + key.Y * _screen.Height);
        }

        private float KeyDiameter(VirtualKey key)
        {
            if (key == null) return 0f;
            return key.Size * _screenScale;
        }

        private RectangleF KeyRect(VirtualKey key)
        {
            PointF c = KeyCenter(key);
            float d = KeyDiameter(key);
            return new RectangleF(c.X - d / 2f, c.Y - d / 2f, d, d);
        }

        private PointF SystemCenter(bool display)
        {
            AppConfig sys = SysRoot;
            if (display)
            {
                return new PointF(_screen.Left + sys.DisplayKeyX * _screen.Width,
                    _screen.Top + sys.DisplayKeyY * _screen.Height);
            }
            return new PointF(_screen.Left + sys.ModeKeyX * _screen.Width,
                _screen.Top + sys.ModeKeyY * _screen.Height);
        }

        private float SystemDiameter(bool display)
        {
            AppConfig sys = SysRoot;
            return (display ? sys.DisplayKeySize : sys.ModeKeySize) * _screenScale;
        }

        private RectangleF SystemRect(bool display)
        {
            PointF c = SystemCenter(display);
            float d = SystemDiameter(display);
            return new RectangleF(c.X - d / 2f, c.Y - d / 2f, d, d);
        }

        private RectangleF JoystickRect()
        {
            JoystickConfig joy = CurrentJoystick();
            if (joy == null || !joy.Enabled) return RectangleF.Empty;
            float d = joy.Size * _screenScale;
            PointF c = new PointF(_screen.Left + joy.X * _screen.Width,
                _screen.Top + joy.Y * _screen.Height);
            return new RectangleF(c.X - d / 2f, c.Y - d / 2f, d, d);
        }

        private RectangleF MouseLookRect()
        {
            if (_config == null || _config.MouseLook == null || !_config.MouseLook.Enabled) return RectangleF.Empty;
            float w = _config.MouseLook.Width * _screenScale;
            float h = _config.MouseLook.Height * _screenScale;
            PointF c = new PointF(_screen.Left + _config.MouseLook.X * _screen.Width,
                _screen.Top + _config.MouseLook.Y * _screen.Height);
            return new RectangleF(c.X - w / 2f, c.Y - h / 2f, w, h);
        }

        private List<VirtualKey> VisibleKeys()
        {
            List<VirtualKey> list = new List<VirtualKey>();
            ModeConfig mode = ActiveMode;
            if (mode == null || mode.Keys == null) return list;
            for (int i = 0; i < mode.Keys.Count; i++)
            {
                VirtualKey k = mode.Keys[i];
                if (k == null) continue;
                if (_viewState == 1 && k.HideWithHidden) continue;
                list.Add(k);
            }
            return list;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            if (_config == null) return;
            _stage = new RectangleF(0, 0, Width, Height);
            ComputeDeviceRect();

            DrawStageBackground(e.Graphics);
            DrawSideInfo(e.Graphics);

            DrawDevice(e.Graphics);
            DrawScreen(e.Graphics);
            DrawModeMenu(e.Graphics);
            DrawDragBadge(e.Graphics);
        }

        private void ComputeDeviceRect()
        {
            float pad = 12f * Theme.Scale;
            float availableH = Math.Max(120f, Height - pad * 2f);
            float availableW = Math.Max(100f, Width - pad * 2f);
            bool landscape = _orientation == DisplayOrientation.Landscape;
            float designW = landscape ? 2340f : 1080f;
            float designH = landscape ? 1080f : 2340f;
            float aspect = designW / designH;
            float w;
            float h;
            if (landscape)
            {
                w = availableW * 0.92f;
                h = w / aspect;
                if (h > availableH)
                {
                    h = availableH * 0.94f;
                    w = h * aspect;
                }
            }
            else
            {
                h = availableH;
                w = h * aspect;
                if (w > availableW * 0.52f)
                {
                    w = availableW * 0.52f;
                    h = w / aspect;
                }
            }
            if (w < 150f)
            {
                w = Math.Min(availableW, 150f);
                h = w / aspect;
            }
            if (h > availableH)
            {
                h = availableH;
                w = h * aspect;
            }
            float x = (Width - w) / 2f;
            float y = (Height - h) / 2f;
            _device = new RectangleF(x, y, w, h);
            float bezel = Math.Max(4f, Math.Min(w, h) * 0.024f);
            _screen = new RectangleF(x + bezel, y + bezel, w - bezel * 2f, h - bezel * 2f);
            _screenScale = Math.Min(_screen.Width / designW, _screen.Height / designH);
            _uiScale = Math.Max(0.5f, Width / 900f);
        }

        private void DrawStageBackground(Graphics g)
        {
            using (LinearGradientBrush bg = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(19, 27, 43), Color.FromArgb(10, 15, 24), 90f))
            {
                g.FillRectangle(bg, ClientRectangle);
            }

            using (GraphicsPath glow1 = new GraphicsPath())
            {
                glow1.AddEllipse(-Width * 0.28f, -Height * 0.25f, Width * 0.9f, Height * 0.8f);
                using (PathGradientBrush brush = new PathGradientBrush(glow1))
                {
                    brush.CenterColor = Color.FromArgb(32, 70, 130, 255);
                    brush.SurroundColors = new Color[] { Color.FromArgb(0, 70, 130, 255) };
                    g.FillPath(brush, glow1);
                }
            }
            using (GraphicsPath glow2 = new GraphicsPath())
            {
                glow2.AddEllipse(Width * 0.48f, Height * 0.48f, Width * 0.65f, Height * 0.7f);
                using (PathGradientBrush brush = new PathGradientBrush(glow2))
                {
                    brush.CenterColor = Color.FromArgb(22, 58, 215, 191);
                    brush.SurroundColors = new Color[] { Color.FromArgb(0, 58, 215, 191) };
                    g.FillPath(brush, glow2);
                }
            }

            float step = 30f * Theme.Scale;
            using (SolidBrush dot = new SolidBrush(Color.FromArgb(15, 142, 165, 210)))
            {
                for (float y = step / 2f; y < Height; y += step)
                {
                    for (float x = step / 2f; x < Width; x += step)
                    {
                        g.FillEllipse(dot, x, y, 1.2f, 1.2f);
                    }
                }
            }
        }

        private void DrawSideInfo(Graphics g)
        {
            float s = Theme.Scale;
            float leftSpace = _device.Left;
            float rightSpace = Width - _device.Right;
            bool show = leftSpace > 150 * s && rightSpace > 150 * s && Width > 620 * s;
            if (!show) return;

            ModeConfig mode = ActiveMode;
            Color accent = Theme.AccentFor(_modeIndex);
            float x = 26 * s;
            float top = _device.Top + 54 * s;
            Theme.FillRounded(g, new RectangleF(x, top + 2 * s, 3 * s, 34 * s), 1.5f * s, accent);
            Theme.DrawText(g, "当前布局", Theme.UiFont(8f, false), Theme.Muted,
                new RectangleF(x + 13 * s, top, 190 * s, 18 * s), StringAlignment.Near, StringAlignment.Center, false);
            Theme.DrawText(g, mode == null ? "—" : mode.Name, Theme.Semibold(15f), Theme.Text,
                new RectangleF(x + 13 * s, top + 18 * s, 210 * s, 30 * s), StringAlignment.Near, StringAlignment.Center, true);
            int count = mode == null || mode.Keys == null ? 0 : mode.Keys.Count;
            Theme.DrawText(g, count + " 个虚拟按键", Theme.UiFont(8.2f, false), Theme.Faint,
                new RectangleF(x + 13 * s, top + 50 * s, 210 * s, 18 * s), StringAlignment.Near, StringAlignment.Center, false);

            float tipTop = top + 104 * s;
            DrawSideTip(g, x, tipTop, accent, "拖动按键", "按住并拖动即可调整位置");
            DrawSideTip(g, x, tipTop + 50 * s, Theme.Mint, "自动保存", "松开鼠标后立即写入配置");
            DrawSideTip(g, x, tipTop + 100 * s, Theme.Gold, "显示键", "显示 / 隐藏 / 移动三态切换");

            float rx = _device.Right + 26 * s;
            float rw = Width - rx - 26 * s;
            Theme.DrawText(g, "实时预览", Theme.UiFont(8f, false), Theme.Muted,
                new RectangleF(rx, top, rw, 18 * s), StringAlignment.Near, StringAlignment.Center, false);
            Theme.DrawText(g, "所见即所得", Theme.Semibold(14f), Theme.Text,
                new RectangleF(rx, top + 18 * s, rw, 28 * s), StringAlignment.Near, StringAlignment.Center, true);
            Theme.DrawText(g, "所有位置按屏幕比例保存，", Theme.UiFont(8.3f, false), Theme.Faint,
                new RectangleF(rx, top + 52 * s, rw, 20 * s), StringAlignment.Near, StringAlignment.Center, false);
            Theme.DrawText(g, "窗口缩放不会破坏布局。", Theme.UiFont(8.3f, false), Theme.Faint,
                new RectangleF(rx, top + 70 * s, rw, 20 * s), StringAlignment.Near, StringAlignment.Center, false);

            float stateTop = top + 122 * s;
            Theme.FillRounded(g, new RectangleF(rx, stateTop, rw, 86 * s), 12f * s, Theme.Alpha(Theme.SurfaceAlt, 210));
            Theme.DrawRounded(g, new RectangleF(rx, stateTop, rw, 86 * s), 12f * s, Theme.StrokeSoft, 1f);
            Theme.DrawText(g, "显示键状态", Theme.UiFont(7.8f, false), Theme.Muted,
                new RectangleF(rx + 12 * s, stateTop + 8 * s, rw - 24 * s, 18 * s), StringAlignment.Near, StringAlignment.Center, false);
            string[] labels = new string[] { "显示", "隐藏", "移动" };
            float chipW = (rw - 24 * s - 12 * s) / 3f;
            for (int i = 0; i < 3; i++)
            {
                RectangleF chip = new RectangleF(rx + 12 * s + i * (chipW + 6 * s), stateTop + 32 * s, chipW, 28 * s);
                bool active = _viewState == i;
                Theme.FillRounded(g, chip, 7f * s, active ? Theme.Alpha(Theme.Accent, 60) : Theme.Alpha(Theme.SurfaceHi, 130));
                Theme.DrawText(g, labels[i], Theme.UiFont(7.8f, active), active ? Theme.Text : Theme.Faint,
                    chip, StringAlignment.Center, StringAlignment.Center, false);
            }
        }

        private void DrawSideTip(Graphics g, float x, float y, Color color, string title, string subtitle)
        {
            float s = Theme.Scale;
            Theme.FillCircle(g, new RectangleF(x + 1 * s, y + 7 * s, 5 * s, 5 * s), color);
            Theme.DrawText(g, title, Theme.UiFont(8.4f, false), Theme.TextSoft,
                new RectangleF(x + 14 * s, y, 190 * s, 18 * s), StringAlignment.Near, StringAlignment.Center, false);
            Theme.DrawText(g, subtitle, Theme.UiFont(7.6f, false), Theme.Faint,
                new RectangleF(x + 14 * s, y + 17 * s, 195 * s, 18 * s), StringAlignment.Near, StringAlignment.Center, false);
        }

        private void DrawDevice(Graphics g)
        {
            float radius = Math.Min(_device.Width, _device.Height) * 0.10f;
            for (int i = 4; i >= 1; i--)
            {
                float spread = i * 3.5f;
                RectangleF shadow = new RectangleF(_device.X - spread, _device.Y + spread * 0.7f,
                    _device.Width + spread * 2f, _device.Height + spread * 1.6f);
                Theme.FillRounded(g, shadow, radius + spread, Color.FromArgb(6 + i * 5, 0, 0, 0));
            }

            Theme.FillRounded(g, _device, radius, Color.FromArgb(5, 8, 14));
            Theme.DrawRounded(g, new RectangleF(_device.X + 0.5f, _device.Y + 0.5f, _device.Width - 1f, _device.Height - 1f),
                radius, Color.FromArgb(64, 80, 108), 1f);
            Theme.DrawRounded(g, new RectangleF(_device.X + 1.5f, _device.Y + 1.5f, _device.Width - 3f, _device.Height - 3f),
                radius - 1f, Color.FromArgb(22, 30, 44), 1f);

            float bw = 2.2f * Theme.Scale;
            Color buttonColor = Color.FromArgb(29, 39, 55);
            Theme.FillRounded(g, new RectangleF(_device.Left - bw, _device.Top + _device.Height * 0.24f, bw,
                _device.Height * 0.055f), bw, buttonColor);
            Theme.FillRounded(g, new RectangleF(_device.Left - bw, _device.Top + _device.Height * 0.32f, bw,
                _device.Height * 0.085f), bw, buttonColor);
            Theme.FillRounded(g, new RectangleF(_device.Right, _device.Top + _device.Height * 0.28f, bw,
                _device.Height * 0.10f), bw, buttonColor);
        }

        private void DrawScreen(Graphics g)
        {
            GraphicsState state = g.Save();
            float radius = Math.Min(_screen.Width, _screen.Height) * 0.09f;
            using (GraphicsPath clip = Theme.RoundedPath(_screen, radius))
            {
                g.SetClip(clip);
            }

            using (LinearGradientBrush bg = new LinearGradientBrush(_screen,
                Color.FromArgb(27, 40, 67), Color.FromArgb(9, 14, 24), 100f))
            {
                g.FillRectangle(bg, _screen);
            }

            DrawScreenGlow(g);
            DrawScreenGrid(g);
            DrawNotch(g);
            DrawStatusBar(g);
            DrawJoystick(g);
            DrawJoystickR(g);
            DrawMouseLook(g);

            // 主模式按键：叠加层勾选「单独显示」时隐藏；叠加层为空时回退显示主层，避免整屏空白。
            if (ActiveMode != null && (!OverlaySolo || VisibleOverlayKeys().Count == 0))
            {
                List<VirtualKey> keys = VisibleKeys();
                List<VirtualKey> dpad = new List<VirtualKey>();
                for (int i = 0; i < keys.Count; i++)
                {
                    VirtualKey key = keys[i];
                    if (IsPadDpadKey(key)) { dpad.Add(key); continue; }
                    int originalIndex = ActiveMode.Keys.IndexOf(key);
                    Color accent = Theme.AccentFor(originalIndex);
                    DrawVirtualKey(g, key, KeyCenter(key), KeyDiameter(key), accent,
                        ReferenceEquals(key, _selectedKey), false, null);
                }
                DrawUnifiedDpad(g, dpad, false);
            }

            // 叠加层按键：主界面勾选「单独显示」时隐藏；主层为空时回退显示叠加层。
            if (!MainSolo || VisibleKeys().Count == 0)
            {
                List<VirtualKey> overlayKeys = VisibleOverlayKeys();
                List<VirtualKey> overlayDpad = new List<VirtualKey>();
                for (int i = 0; i < overlayKeys.Count; i++)
                {
                    VirtualKey key = overlayKeys[i];
                    if (IsPadDpadKey(key)) { overlayDpad.Add(key); continue; }
                    DrawVirtualKey(g, key, KeyCenter(key), KeyDiameter(key), Theme.Violet,
                        ReferenceEquals(key, _selectedOverlayKey), false, null);
                }
                DrawUnifiedDpad(g, overlayDpad, true);
            }

            if (_config == null || _config.DisplayKey == null || _config.DisplayKey.Enabled)
            {
                string displayText = _viewState == 0 ? "显示" : (_viewState == 1 ? "隐藏" : "移动");
                Color displayAccent = _viewState == 1 ? Theme.Gold : (_viewState == 2 ? Theme.Mint : Theme.Accent);
                DrawVirtualKey(g, null, SystemCenter(true), SystemDiameter(true), displayAccent,
                    false, true, displayText);
            }
            if (_viewState != 1 && (_config == null || _config.ModeKey == null || _config.ModeKey.Enabled))
            {
                DrawVirtualKey(g, null, SystemCenter(false), SystemDiameter(false), Theme.Violet,
                    false, true, "模式");
            }

            DrawSafeHint(g);
            g.Restore(state);
        }

        private void DrawScreenGlow(Graphics g)
        {
            using (GraphicsPath p1 = new GraphicsPath())
            {
                p1.AddEllipse(_screen.Left - _screen.Width * 0.2f, _screen.Top + _screen.Height * 0.44f,
                    _screen.Width * 0.9f, _screen.Height * 0.45f);
                using (PathGradientBrush brush = new PathGradientBrush(p1))
                {
                    brush.CenterColor = Color.FromArgb(62, 55, 104, 210);
                    brush.SurroundColors = new Color[] { Color.FromArgb(0, 55, 104, 210) };
                    g.FillPath(brush, p1);
                }
            }
            using (GraphicsPath p2 = new GraphicsPath())
            {
                p2.AddEllipse(_screen.Left + _screen.Width * 0.2f, _screen.Top + _screen.Height * 0.05f,
                    _screen.Width * 1.1f, _screen.Height * 0.55f);
                using (PathGradientBrush brush = new PathGradientBrush(p2))
                {
                    brush.CenterColor = Color.FromArgb(38, 55, 210, 190);
                    brush.SurroundColors = new Color[] { Color.FromArgb(0, 55, 210, 190) };
                    g.FillPath(brush, p2);
                }
            }
            using (GraphicsPath wave = new GraphicsPath())
            {
                wave.AddBezier(_screen.Left - _screen.Width * 0.1f, _screen.Top + _screen.Height * 0.78f,
                    _screen.Left + _screen.Width * 0.3f, _screen.Top + _screen.Height * 0.66f,
                    _screen.Left + _screen.Width * 0.65f, _screen.Top + _screen.Height * 0.94f,
                    _screen.Left + _screen.Width * 1.1f, _screen.Top + _screen.Height * 0.76f);
                using (Pen pen = new Pen(Color.FromArgb(26, 118, 230, 255), Math.Max(1f, _screen.Width * 0.006f)))
                {
                    g.DrawPath(pen, wave);
                }
            }
        }

        private void DrawScreenGrid(Graphics g)
        {
            float step = _screen.Width / 9f;
            using (Pen pen = new Pen(Color.FromArgb(9, 190, 215, 255), 1f))
            {
                for (float x = _screen.Left + step; x < _screen.Right; x += step)
                {
                    g.DrawLine(pen, x, _screen.Top, x, _screen.Bottom);
                }
                for (float y = _screen.Top + step; y < _screen.Bottom; y += step)
                {
                    g.DrawLine(pen, _screen.Left, y, _screen.Right, y);
                }
            }
        }

        private void DrawNotch(Graphics g)
        {
            float w = Math.Min(_screen.Width * 0.26f, _screen.Height * 0.26f);
            float h = Math.Max(8f, Math.Min(_screen.Width, _screen.Height) * 0.035f);
            RectangleF notch = new RectangleF(_screen.Left + (_screen.Width - w) / 2f, _screen.Top + h * 0.55f, w, h);
            Theme.FillRounded(g, notch, h / 2f, Color.FromArgb(6, 9, 14));
            Theme.FillCircle(g, new RectangleF(notch.Right - h * 0.7f, notch.Y + h * 0.34f, h * 0.26f, h * 0.26f),
                Color.FromArgb(24, 44, 65));
            Theme.FillCircle(g, new RectangleF(notch.Right - h * 0.63f, notch.Y + h * 0.40f, h * 0.13f, h * 0.13f),
                Color.FromArgb(45, 96, 140));
        }

        private void DrawStatusBar(Graphics g)
        {
            float s = Math.Max(0.58f, _screen.Height / 900f);
            float cornerPad = Math.Min(_screen.Width, _screen.Height) * 0.075f;
            float top = _screen.Top + 8f * s;
            Theme.DrawText(g, "9:41", Theme.Semibold(7.4f * s), Color.FromArgb(220, 240, 248, 255),
                new RectangleF(_screen.Left + 14f * s + cornerPad, top, 52f * s, 16f * s), StringAlignment.Near, StringAlignment.Center, false);

            float right = _screen.Right - 18f * s - cornerPad;
            float cy = top + 9f * s;
            // signal
            for (int i = 0; i < 4; i++)
            {
                float h = (2.5f + i * 2f) * s;
                Theme.FillRounded(g, new RectangleF(right - 35f * s + i * 4.2f * s, cy - h / 2f, 2.5f * s, h),
                    1.2f * s, Color.FromArgb(210, 226, 237, 250));
            }
            // wifi
            using (Pen p = new Pen(Color.FromArgb(210, 226, 237, 250), 1.1f * s))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                g.DrawArc(p, right - 12f * s, cy - 3f * s, 8f * s, 8f * s, 215f, 110f);
                g.DrawArc(p, right - 10.5f * s, cy - 1.5f * s, 5f * s, 5f * s, 215f, 110f);
            }
            Theme.FillCircle(g, new RectangleF(right - 8f * s, cy + 3.5f * s, 1.6f * s, 1.6f * s),
                Color.FromArgb(210, 226, 237, 250));
            // battery
            RectangleF battery = new RectangleF(right + 3f * s, cy - 4f * s, 14f * s, 8f * s);
            using (Pen p = new Pen(Color.FromArgb(210, 226, 237, 250), 1f * s))
            {
                Theme.DrawRounded(g, battery, 2.5f * s, Color.FromArgb(210, 226, 237, 250), 1f * s);
                g.DrawLine(p, battery.Right + 1.2f * s, battery.Y + 2f * s, battery.Right + 1.2f * s, battery.Bottom - 2f * s);
            }
            Theme.FillRounded(g, new RectangleF(battery.X + 1.5f * s, battery.Y + 1.5f * s, battery.Width * 0.68f, battery.Height - 3f * s),
                1.2f * s, Color.FromArgb(230, 226, 237, 250));
        }

        private void DrawJoystick(Graphics g)
        {
            if (!JoystickVisible()) return;
            RectangleF rect = JoystickRect();
            if (rect.Width < 10) return;
            JoystickConfig joy = CurrentJoystick();
            float opacity = Math.Max(0.1f, Math.Min(1f, joy.Opacity / 100f));
            PointF c = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
            float outerD = Math.Min(rect.Width, joy.OuterSize * _screenScale);
            float innerD = Math.Min(outerD, joy.InnerSize * _screenScale);
            RectangleF outer = new RectangleF(c.X - outerD / 2f, c.Y - outerD / 2f, outerD, outerD);
            bool collapsed = _joystickCollapsed && !_joystickKnobDragging;
            bool hover = ReferenceEquals(_hoverTarget, "joystick");
            bool active = _joystickKnobDragging || Math.Abs(_joystickDx) + Math.Abs(_joystickDy) > 0.02f;
            Color accent = Theme.Mint;

            if (!collapsed)
            {
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb((int)(70 * opacity), 0, 0, 0)))
                {
                    g.FillEllipse(shadow, new RectangleF(outer.X + outerD * 0.04f, outer.Y + outerD * 0.06f, outerD, outerD));
                }
                using (LinearGradientBrush baseBrush = new LinearGradientBrush(outer,
                    Color.FromArgb((int)(225 * opacity), 19, 27, 43), Color.FromArgb((int)(235 * opacity), 8, 12, 20), 90f))
                {
                    g.FillEllipse(baseBrush, outer);
                }
                Theme.DrawCircle(g, outer, Theme.Alpha(accent, (int)((hover || active ? 220 : 120) * opacity)),
                    Math.Max(1.2f, outerD * 0.018f));
                Theme.DrawCircle(g, new RectangleF(outer.X + outerD * 0.055f, outer.Y + outerD * 0.055f,
                    outerD * 0.89f, outerD * 0.89f), Color.FromArgb((int)(30 * opacity), 255, 255, 255),
                    Math.Max(1f, outerD * 0.008f));

                // direction hints（摇杆为连续轴，四角仅作方向视觉提示，不再显示绑定键名）。
                Font keyFont = Theme.UiFont(Math.Max(5f, outerD * 0.09f), false);
                DrawJoystickKey(g, "↑", new RectangleF(c.X - outerD * 0.12f, outer.Y + outerD * 0.07f, outerD * 0.24f, outerD * 0.18f),
                    keyFont, opacity, _joystickKnobDragging && _joystickDy < -0.2f);
                DrawJoystickKey(g, "↓", new RectangleF(c.X - outerD * 0.12f, outer.Bottom - outerD * 0.25f, outerD * 0.24f, outerD * 0.18f),
                    keyFont, opacity, _joystickKnobDragging && _joystickDy > 0.2f);
                DrawJoystickKey(g, "←", new RectangleF(outer.X + outerD * 0.07f, c.Y - outerD * 0.12f, outerD * 0.22f, outerD * 0.24f),
                    keyFont, opacity, _joystickKnobDragging && _joystickDx < -0.2f);
                DrawJoystickKey(g, "→", new RectangleF(outer.Right - outerD * 0.29f, c.Y - outerD * 0.12f, outerD * 0.22f, outerD * 0.24f),
                    keyFont, opacity, _joystickKnobDragging && _joystickDx > 0.2f);
            }

            float travel = collapsed ? 0f : Math.Max(0f, outerD / 2f - innerD / 2f - outerD * 0.06f);
            float dx = collapsed ? 0f : _joystickDx;
            float dy = collapsed ? 0f : _joystickDy;
            PointF knobCenter = new PointF(c.X + dx * travel, c.Y + dy * travel);
            RectangleF knob = new RectangleF(knobCenter.X - innerD / 2f, knobCenter.Y - innerD / 2f, innerD, innerD);
            using (SolidBrush knobShadow = new SolidBrush(Color.FromArgb((int)(90 * opacity), 0, 0, 0)))
            {
                g.FillEllipse(knobShadow, new RectangleF(knob.X, knob.Y + innerD * 0.08f, innerD, innerD));
            }
            using (LinearGradientBrush knobBrush = new LinearGradientBrush(knob,
                Color.FromArgb((int)(240 * opacity), Theme.Blend(accent, Color.White, 0.38f)),
                Color.FromArgb((int)(245 * opacity), Theme.Blend(accent, Color.Black, 0.35f)), 90f))
            {
                g.FillEllipse(knobBrush, knob);
            }
            Theme.DrawCircle(g, knob, Color.FromArgb((int)(235 * opacity), 234, 255, 250),
                Math.Max(1.2f, innerD * 0.03f));
            Theme.FillCircle(g, new RectangleF(knobCenter.X - innerD * 0.09f, knobCenter.Y - innerD * 0.09f,
                innerD * 0.18f, innerD * 0.18f), Color.FromArgb((int)(200 * opacity), 255, 255, 255));
        }

        // 右摇杆预览：静态展示布局与参数，不参与拖动交互，内圈居中、箭头不高亮。
        private void DrawJoystickR(Graphics g)
        {
            if (!JoystickRVisible()) return;
            RectangleF rect = JoystickRRect();
            if (rect.Width < 10) return;
            JoystickConfig joy = CurrentJoystickR();
            float opacity = Math.Max(0.1f, Math.Min(1f, joy.Opacity / 100f));
            PointF c = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
            float outerD = Math.Min(rect.Width, joy.OuterSize * _screenScale);
            float innerD = Math.Min(outerD, joy.InnerSize * _screenScale);
            RectangleF outer = new RectangleF(c.X - outerD / 2f, c.Y - outerD / 2f, outerD, outerD);
            Color accent = Theme.Gold;

            using (SolidBrush shadow = new SolidBrush(Color.FromArgb((int)(70 * opacity), 0, 0, 0)))
            {
                g.FillEllipse(shadow, new RectangleF(outer.X + outerD * 0.04f, outer.Y + outerD * 0.06f, outerD, outerD));
            }
            using (LinearGradientBrush baseBrush = new LinearGradientBrush(outer,
                Color.FromArgb((int)(225 * opacity), 19, 27, 43), Color.FromArgb((int)(235 * opacity), 8, 12, 20), 90f))
            {
                g.FillEllipse(baseBrush, outer);
            }
            Theme.DrawCircle(g, outer, Theme.Alpha(accent, (int)(120 * opacity)),
                Math.Max(1.2f, outerD * 0.018f));
            Theme.DrawCircle(g, new RectangleF(outer.X + outerD * 0.055f, outer.Y + outerD * 0.055f,
                outerD * 0.89f, outerD * 0.89f), Color.FromArgb((int)(30 * opacity), 255, 255, 255),
                Math.Max(1f, outerD * 0.008f));

            Font keyFont = Theme.UiFont(Math.Max(5f, outerD * 0.09f), false);
            DrawJoystickKey(g, "↑", new RectangleF(c.X - outerD * 0.12f, outer.Y + outerD * 0.07f, outerD * 0.24f, outerD * 0.18f),
                keyFont, opacity, false);
            DrawJoystickKey(g, "↓", new RectangleF(c.X - outerD * 0.12f, outer.Bottom - outerD * 0.25f, outerD * 0.24f, outerD * 0.18f),
                keyFont, opacity, false);
            DrawJoystickKey(g, "←", new RectangleF(outer.X + outerD * 0.07f, c.Y - outerD * 0.12f, outerD * 0.22f, outerD * 0.24f),
                keyFont, opacity, false);
            DrawJoystickKey(g, "→", new RectangleF(outer.Right - outerD * 0.29f, c.Y - outerD * 0.12f, outerD * 0.22f, outerD * 0.24f),
                keyFont, opacity, false);

            RectangleF knob = new RectangleF(c.X - innerD / 2f, c.Y - innerD / 2f, innerD, innerD);
            using (SolidBrush knobShadow = new SolidBrush(Color.FromArgb((int)(90 * opacity), 0, 0, 0)))
            {
                g.FillEllipse(knobShadow, new RectangleF(knob.X, knob.Y + innerD * 0.08f, innerD, innerD));
            }
            using (LinearGradientBrush knobBrush = new LinearGradientBrush(knob,
                Color.FromArgb((int)(240 * opacity), Theme.Blend(accent, Color.White, 0.38f)),
                Color.FromArgb((int)(245 * opacity), Theme.Blend(accent, Color.Black, 0.35f)), 90f))
            {
                g.FillEllipse(knobBrush, knob);
            }
            Theme.DrawCircle(g, knob, Color.FromArgb((int)(235 * opacity), 255, 248, 234),
                Math.Max(1.2f, innerD * 0.03f));
            Theme.FillCircle(g, new RectangleF(c.X - innerD * 0.09f, c.Y - innerD * 0.09f,
                innerD * 0.18f, innerD * 0.18f), Color.FromArgb((int)(200 * opacity), 255, 255, 255));
        }

        private void DrawJoystickKey(Graphics g, string text, RectangleF rect, Font font, float opacity, bool active)
        {
            if (string.IsNullOrEmpty(text)) text = "—";
            Theme.DrawText(g, text, font,
                active ? Color.FromArgb((int)(255 * opacity), 255, 255, 255) : Color.FromArgb((int)(130 * opacity), 214, 228, 244),
                rect, StringAlignment.Center, StringAlignment.Center, true);
        }

        private void DrawMouseLook(Graphics g)
        {
            // 视角操控仅虚拟按键页使用；虚拟手柄页不绘制视角虚拟键。
            if (GamepadMode) return;
            if (_config == null || _config.MouseLook == null || !_config.MouseLook.Enabled) return;
            if (_config.ActiveMode != null && !_config.ActiveMode.ShowMouseLook) return;
            if (_viewState == 1 && _config.MouseLook.HideWithHidden) return;
            RectangleF rect = MouseLookRect();
            if (rect.Width < 10 || rect.Height < 10) return;
            MouseLookConfig look = _config.MouseLook;
            float opacity = Math.Max(0.1f, Math.Min(1f, look.Opacity / 100f));
            int fillAlpha = (int)Math.Round(255 * opacity);
            bool hover = ReferenceEquals(_hoverTarget, "mouselook");
            bool active = _lookDragging;
            Color accent = Theme.Pink;
            using (GraphicsPath path = BuildLookPath(rect, look.Shape, Math.Max(8f, Math.Min(rect.Width, rect.Height) * 0.12f)))
            {
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb((int)(55 * opacity), 0, 0, 0)))
                {
                    using (GraphicsPath shadowPath = BuildLookPath(new RectangleF(rect.X + 2, rect.Y + 3, rect.Width, rect.Height),
                        look.Shape, Math.Max(8f, Math.Min(rect.Width, rect.Height) * 0.12f)))
                    {
                        g.FillPath(shadow, shadowPath);
                    }
                }
                using (LinearGradientBrush brush = new LinearGradientBrush(rect,
                    Color.FromArgb(fillAlpha, 30, 24, 42), Color.FromArgb(fillAlpha, 13, 15, 25), 90f))
                {
                    g.FillPath(brush, path);
                }
                using (Pen pen = new Pen(Theme.Alpha(accent, (int)((active ? 245 : 150) * opacity)),
                    Math.Max(1.2f, Math.Min(rect.Width, rect.Height) * 0.016f)))
                {
                    g.DrawPath(pen, path);
                }
            }
            if (active)
            {
                using (GraphicsPath glow = BuildLookPath(new RectangleF(rect.X - 4, rect.Y - 4, rect.Width + 8, rect.Height + 8),
                    look.Shape, Math.Max(8f, Math.Min(rect.Width, rect.Height) * 0.14f)))
                using (Pen pen = new Pen(Color.FromArgb((int)(110 * opacity), accent), 2.4f))
                {
                    g.DrawPath(pen, glow);
                }
            }

            PointF c = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
            float iconSize = Math.Min(rect.Width, rect.Height) * 0.17f;
            RectangleF mouseRect = new RectangleF(c.X - iconSize * 0.58f, c.Y - iconSize * 1.2f, iconSize * 1.16f, iconSize * 1.6f);
            Theme.DrawRounded(g, mouseRect, iconSize * 0.5f, Color.FromArgb((int)(220 * opacity), 232, 220, 255), 1.4f);
            using (Pen p = new Pen(Color.FromArgb((int)(180 * opacity), 232, 220, 255), 1.1f))
            {
                g.DrawLine(p, mouseRect.X + mouseRect.Width / 2f, mouseRect.Y + 1, mouseRect.X + mouseRect.Width / 2f,
                    mouseRect.Y + mouseRect.Height * 0.34f);
            }
            Theme.DrawText(g, "视角", Theme.Semibold(Math.Max(6f, Math.Min(rect.Width, rect.Height) * 0.105f)),
                Color.FromArgb((int)(245 * opacity), 244, 236, 255),
                new RectangleF(rect.X + rect.Width * 0.08f, c.Y + iconSize * 0.35f, rect.Width * 0.84f, iconSize * 1.1f),
                StringAlignment.Center, StringAlignment.Center, true);
            string button = MouseLookConfig.ButtonName(look.Button == null ? 1 : (look.Button.Kind == BindingKind.MouseButton ? look.Button.Code : -1));
            Theme.DrawText(g, button, Theme.UiFont(Math.Max(5f, Math.Min(rect.Width, rect.Height) * 0.065f)),
                Theme.Alpha(accent, (int)(200 * opacity)),
                new RectangleF(rect.X + rect.Width * 0.08f, c.Y + iconSize * 1.55f, rect.Width * 0.84f, iconSize * 0.9f),
                StringAlignment.Center, StringAlignment.Center, true);

            // four direction hints
            float arrow = Math.Max(5f, Math.Min(rect.Width, rect.Height) * 0.07f);
            Color arrowColor = Color.FromArgb((int)((active ? 225 : 105) * opacity), 255, 242, 252);
            DrawLookArrow(g, new PointF(c.X, rect.Y + arrow * 1.4f), 0, arrow, arrowColor);
            DrawLookArrow(g, new PointF(c.X, rect.Bottom - arrow * 1.4f), 2, arrow, arrowColor);
            DrawLookArrow(g, new PointF(rect.X + arrow * 1.4f, c.Y), 3, arrow, arrowColor);
            DrawLookArrow(g, new PointF(rect.Right - arrow * 1.4f, c.Y), 1, arrow, arrowColor);

            if (active)
            {
                float travel = Math.Min(rect.Width, rect.Height) * 0.24f;
                PointF dot = new PointF(c.X + _lookDx * travel, c.Y + _lookDy * travel);
                Theme.FillCircle(g, new RectangleF(dot.X - iconSize * 0.22f, dot.Y - iconSize * 0.22f,
                    iconSize * 0.44f, iconSize * 0.44f), Color.FromArgb((int)(230 * opacity), accent));
                using (Pen trail = new Pen(Color.FromArgb((int)(120 * opacity), accent), 2f))
                {
                    g.DrawLine(trail, c, dot);
                }
            }
        }

        private static GraphicsPath BuildLookPath(RectangleF rect, LookShape shape, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (shape == LookShape.Circle)
            {
                path.AddEllipse(rect);
            }
            else
            {
                float r = Math.Max(4f, radius);
                float d = r * 2f;
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
            }
            return path;
        }

        private static void DrawLookArrow(Graphics g, PointF p, int direction, float size, Color color)
        {
            PointF[] points;
            if (direction == 0) points = new PointF[] { new PointF(p.X, p.Y - size * 0.45f), new PointF(p.X - size * 0.45f, p.Y + size * 0.25f), new PointF(p.X + size * 0.45f, p.Y + size * 0.25f) };
            else if (direction == 1) points = new PointF[] { new PointF(p.X + size * 0.45f, p.Y), new PointF(p.X - size * 0.25f, p.Y - size * 0.45f), new PointF(p.X - size * 0.25f, p.Y + size * 0.45f) };
            else if (direction == 2) points = new PointF[] { new PointF(p.X, p.Y + size * 0.45f), new PointF(p.X - size * 0.45f, p.Y - size * 0.25f), new PointF(p.X + size * 0.45f, p.Y - size * 0.25f) };
            else points = new PointF[] { new PointF(p.X - size * 0.45f, p.Y), new PointF(p.X + size * 0.25f, p.Y - size * 0.45f), new PointF(p.X + size * 0.25f, p.Y + size * 0.45f) };
            using (SolidBrush b = new SolidBrush(color)) g.FillPolygon(b, points);
        }

        private void DrawVirtualKey(Graphics g, VirtualKey key, PointF center, float diameter, Color accent,
            bool selected, bool system, string systemLabel)
        {
            if (diameter < 6f) return;
            float opacity = key == null ? 1f : Math.Max(0.1f, Math.Min(1f, key.Opacity / 100f));
            int fillAlpha = (int)Math.Round(255 * opacity);
            RectangleF rect = new RectangleF(center.X - diameter / 2f, center.Y - diameter / 2f, diameter, diameter);
            bool hovered = (key != null && ReferenceEquals(_hoverTarget, key)) ||
                           (system && ReferenceEquals(_hoverTarget, systemLabel));
            if (system && systemLabel == "显示" && ReferenceEquals(_hoverTarget, "display")) hovered = true;
            if (system && systemLabel == "模式" && ReferenceEquals(_hoverTarget, "mode")) hovered = true;
            if (system && systemLabel == "隐藏" && ReferenceEquals(_hoverTarget, "display")) hovered = true;
            // shadow
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb((int)(85 * opacity), 0, 0, 0)))
            {
                g.FillEllipse(shadow, new RectangleF(rect.X + diameter * 0.03f, rect.Y + diameter * 0.07f, diameter, diameter));
            }

            Color effectiveAccent = accent;
            if (key != null && key.ShortPress != null &&
                key.ShortPress.Kind == BindingKind.Gamepad && IsPadActionCode(key.ShortPress.Code))
            {
                int actionLayout = ActiveMode != null ? ActiveMode.PadLayout : 0;
                effectiveAccent = PhoneActionColor(actionLayout, key.ShortPress.Code);
            }

            Color top = Theme.Blend(Color.FromArgb(fillAlpha, 15, 22, 35), effectiveAccent, hovered ? 0.28f : 0.16f);
            Color bottom = Theme.Blend(Color.FromArgb(fillAlpha, 8, 12, 20), effectiveAccent, 0.05f);
            using (LinearGradientBrush brush = new LinearGradientBrush(rect, top, bottom, 90f))
            {
                g.FillEllipse(brush, rect);
            }

            float ring = Math.Max(1f, diameter * 0.032f);
            Theme.DrawCircle(g, new RectangleF(rect.X + ring / 2f, rect.Y + ring / 2f, rect.Width - ring, rect.Height - ring),
                Theme.Alpha(effectiveAccent, (int)Math.Round((hovered ? 235 : 165) * opacity)), ring);

            using (GraphicsPath gloss = new GraphicsPath())
            {
                gloss.AddArc(rect.X + diameter * 0.12f, rect.Y + diameter * 0.08f, diameter * 0.76f, diameter * 0.55f, 190f, 160f);
                using (Pen pen = new Pen(Color.FromArgb((int)((hovered ? 55 : 28) * opacity), 255, 255, 255),
                    Math.Max(1f, diameter * 0.015f)))
                {
                    g.DrawPath(pen, gloss);
                }
            }

            Image image = key == null ? null : GetImage(key);
            if (image != null)
            {
                float inset = diameter * 0.18f;
                Theme.DrawImageContained(g, image, new RectangleF(rect.X + inset, rect.Y + inset,
                    rect.Width - inset * 2f, rect.Height - inset * 2f), opacity);
            }
            else
            {
                string name = key == null ? systemLabel : key.Name;
                if (string.IsNullOrEmpty(name)) name = "手柄键";
                int layout = ActiveMode != null ? ActiveMode.PadLayout : 0;
                string bindText = PadButtons.BindingLabel(key == null ? null : key.ShortPress, layout);

                // 索尼动作键：△ / ○ / □ / × 用几何图形自绘，不用字符。
                if (key != null && key.ShortPress != null &&
                    key.ShortPress.Kind == BindingKind.Gamepad &&
                    IsPadActionCode(key.ShortPress.Code) && layout == (int)PadLayout.Sony)
                {
                    DrawSonyActionGlyph(g, key.ShortPress.Code, rect, opacity);
                    return;
                }

                float namePt = Math.Max(5.2f, diameter * 0.17f);
                bool showBind = key != null && key.ShortPress != null && !key.ShortPress.IsEmpty &&
                    diameter > 36f && bindText != name;
                RectangleF nameRect = new RectangleF(rect.X + diameter * 0.04f,
                    rect.Y + diameter * (showBind ? 0.22f : 0.22f),
                    diameter * 0.92f, showBind ? diameter * 0.34f : diameter * 0.56f);
                Font nameFont = Theme.FitFont(g, name, namePt, true, diameter * 0.86f, 4.2f);
                Color nameColor = Color.FromArgb((int)(242 * opacity), 246, 255);
                if (key != null && key.ShortPress != null &&
                    key.ShortPress.Kind == BindingKind.Gamepad && IsPadActionCode(key.ShortPress.Code))
                {
                    nameColor = PhoneActionColor(layout, key.ShortPress.Code, opacity);
                }
                Theme.DrawText(g, name, nameFont, nameColor, nameRect,
                    StringAlignment.Center, StringAlignment.Center, false);
                if (showBind)
                {
                    if (bindText.Length > 8) bindText = Theme.Elide(g, bindText, Theme.UiFont(namePt * 0.55f), diameter * 0.82f);
                    Theme.DrawText(g, bindText, Theme.UiFont(Math.Max(4.8f, namePt * 0.54f)),
                        Theme.Alpha(accent, (int)(235 * opacity)),
                        new RectangleF(rect.X + diameter * 0.04f, rect.Y + diameter * 0.57f,
                            diameter * 0.92f, diameter * 0.24f), StringAlignment.Center, StringAlignment.Center, true);
                }
            }

            if (selected)
            {
                float gap = Math.Max(2f, diameter * 0.06f);
                RectangleF ringRect = new RectangleF(rect.X - gap, rect.Y - gap, rect.Width + gap * 2f, rect.Height + gap * 2f);
                Theme.DrawDashedRounded(g, ringRect, ringRect.Width / 2f, Color.FromArgb(225, Theme.Mint), Math.Max(1.2f, diameter * 0.025f), 2.4f);
                if (_viewState == 2)
                {
                    float dot = Math.Max(3f, diameter * 0.07f);
                    PointF[] pts = new PointF[]
                    {
                        new PointF(rect.Left, rect.Top + rect.Height / 2f),
                        new PointF(rect.Right, rect.Top + rect.Height / 2f),
                        new PointF(rect.Left + rect.Width / 2f, rect.Top),
                        new PointF(rect.Left + rect.Width / 2f, rect.Bottom)
                    };
                    for (int i = 0; i < pts.Length; i++)
                    {
                        Theme.FillCircle(g, new RectangleF(pts[i].X - dot / 2f, pts[i].Y - dot / 2f, dot, dot), Theme.Mint);
                    }
                }
            }
        }

        private static bool IsPadActionCode(int code)
        {
            return code == PadButtons.A || code == PadButtons.B || code == PadButtons.X || code == PadButtons.Y;
        }

        private static bool IsPadDpadCode(int code)
        {
            return code == PadButtons.DPadUp || code == PadButtons.DPadDown ||
                   code == PadButtons.DPadLeft || code == PadButtons.DPadRight;
        }

        private static bool IsPadDpadKey(VirtualKey key)
        {
            return key != null && key.ShortPress != null &&
                key.ShortPress.Kind == BindingKind.Gamepad && IsPadDpadCode(key.ShortPress.Code);
        }

        private static List<VirtualKey> CollectDpad(List<VirtualKey> keys)
        {
            List<VirtualKey> list = new List<VirtualKey>();
            if (keys == null) return list;
            for (int i = 0; i < keys.Count; i++)
            {
                if (IsPadDpadKey(keys[i])) list.Add(keys[i]);
            }
            return list;
        }

        private PointF DpadGroupCenter(List<VirtualKey> dpad)
        {
            float sx = 0f, sy = 0f;
            for (int i = 0; i < dpad.Count; i++)
            {
                PointF c = KeyCenter(dpad[i]);
                sx += c.X; sy += c.Y;
            }
            return new PointF(sx / dpad.Count, sy / dpad.Count);
        }

        private RectangleF DpadGroupRect(List<VirtualKey> dpad)
        {
            if (dpad == null || dpad.Count == 0) return RectangleF.Empty;
            PointF c = DpadGroupCenter(dpad);
            float d = _screen.Width * 0.13f;
            return new RectangleF(c.X - d / 2f, c.Y - d / 2f, d, d);
        }

        // 方向键四向合并为一个圆盘整体绘制：主模式位置 / 大小 / 透明度由 DpadConfig 统一管理；
        // 叠加层无独立方向键配置，沿用四键平均中心与默认尺寸。命中与拖动由整组负责。
        private void DrawUnifiedDpad(Graphics g, List<VirtualKey> dpad, bool overlay)
        {
            if (dpad == null || dpad.Count == 0) return;
            PointF center;
            RectangleF board;
            float opacity;
            if (!overlay)
            {
                DpadConfig cfg = CurrentDpad();
                if (cfg == null || !cfg.Enabled) return;
                board = DpadRect();
                center = new PointF(board.X + board.Width / 2f, board.Y + board.Height / 2f);
                opacity = Math.Max(0.1f, Math.Min(1f, cfg.Opacity / 100f));
            }
            else
            {
                float sx = 0f, sy = 0f, op = 0f;
                for (int i = 0; i < dpad.Count; i++)
                {
                    PointF c = KeyCenter(dpad[i]);
                    sx += c.X; sy += c.Y;
                    op += Math.Max(0.1f, Math.Min(1f, dpad[i].Opacity / 100f));
                }
                sx /= dpad.Count; sy /= dpad.Count; op /= dpad.Count;
                center = new PointF(sx, sy);
                opacity = op;
                float d = _screen.Width * 0.13f;
                board = new RectangleF(center.X - d / 2f, center.Y - d / 2f, d, d);
            }
            float R = board.Width / 2f;

            int fillAlpha = (int)Math.Round(255 * opacity);
            using (SolidBrush bb = new SolidBrush(Color.FromArgb(fillAlpha, 22, 30, 44)))
            {
                g.FillEllipse(bb, board);
            }
            Theme.DrawCircle(g, board, Theme.Alpha(Theme.StrokeSoft, (int)(220 * opacity)), 1.2f);

            // 中央十字凹槽
            float armW = R * 1.18f;
            float armH = R * 0.34f;
            using (SolidBrush arm = new SolidBrush(Color.FromArgb(fillAlpha, 9, 14, 22)))
            {
                g.FillRectangle(arm, new RectangleF(center.X - armW / 2f, center.Y - armH / 2f, armW, armH));
                g.FillRectangle(arm, new RectangleF(center.X - armH / 2f, center.Y - armW / 2f, armH, armW));
            }

            bool hasUp = false, hasDown = false, hasLeft = false, hasRight = false;
            bool upHot = false, downHot = false, leftHot = false, rightHot = false;
            for (int i = 0; i < dpad.Count; i++)
            {
                VirtualKey key = dpad[i];
                int code = key.ShortPress.Code;
                bool hot = ReferenceEquals(_hoverTarget, key) ||
                           ReferenceEquals(overlay ? _selectedOverlayKey : _selectedKey, key);
                if (code == PadButtons.DPadUp) { hasUp = true; upHot = hot; }
                else if (code == PadButtons.DPadDown) { hasDown = true; downHot = hot; }
                else if (code == PadButtons.DPadLeft) { hasLeft = true; leftHot = hot; }
                else if (code == PadButtons.DPadRight) { hasRight = true; rightHot = hot; }
            }

            // 小三角：内嵌在十字臂端部内侧，随圆盘比例放大，不撑满圆盘。
            float triW = R * 0.32f;
            DrawDpadTriangle(g, center, R, triW, 0, hasUp, upHot, opacity);
            DrawDpadTriangle(g, center, R, triW, 1, hasDown, downHot, opacity);
            DrawDpadTriangle(g, center, R, triW, 2, hasLeft, leftHot, opacity);
            DrawDpadTriangle(g, center, R, triW, 3, hasRight, rightHot, opacity);
        }

        private void DrawDpadTriangle(Graphics g, PointF c, float R, float w, int dir,
            bool exists, bool hot, float opacity)
        {
            Color baseColor = exists ? Theme.TextSoft : Theme.Faint;
            Color color = hot ? Theme.Accent : baseColor;
            int alpha = (int)Math.Round((exists ? 235 : 120) * opacity);
            using (SolidBrush b = new SolidBrush(Theme.Alpha(color, alpha)))
            {
                PointF[] pts;
                if (dir == 0) // 上
                {
                    pts = new PointF[]
                    {
                        new PointF(c.X, c.Y - R * 0.58f),
                        new PointF(c.X - w / 2f, c.Y - R * 0.34f),
                        new PointF(c.X + w / 2f, c.Y - R * 0.34f)
                    };
                }
                else if (dir == 1) // 下
                {
                    pts = new PointF[]
                    {
                        new PointF(c.X, c.Y + R * 0.58f),
                        new PointF(c.X - w / 2f, c.Y + R * 0.34f),
                        new PointF(c.X + w / 2f, c.Y + R * 0.34f)
                    };
                }
                else if (dir == 2) // 左
                {
                    pts = new PointF[]
                    {
                        new PointF(c.X - R * 0.58f, c.Y),
                        new PointF(c.X - R * 0.34f, c.Y - w / 2f),
                        new PointF(c.X - R * 0.34f, c.Y + w / 2f)
                    };
                }
                else // 右
                {
                    pts = new PointF[]
                    {
                        new PointF(c.X + R * 0.58f, c.Y),
                        new PointF(c.X + R * 0.34f, c.Y - w / 2f),
                        new PointF(c.X + R * 0.34f, c.Y + w / 2f)
                    };
                }
                g.FillPolygon(b, pts);
            }
        }

        private static Color PhoneActionColor(int layout, int code)
        {
            if (layout == (int)PadLayout.Sony)
            {
                if (code == PadButtons.Y) return Theme.Mint;
                if (code == PadButtons.X) return Theme.Violet;
                if (code == PadButtons.B) return Theme.Danger;
                return Theme.Accent;
            }
            if (layout == (int)PadLayout.Nintendo)
            {
                if (code == PadButtons.X) return Theme.Accent;
                if (code == PadButtons.Y) return Theme.Mint;
                if (code == PadButtons.A) return Theme.Danger;
                return Theme.Gold;
            }
            if (code == PadButtons.Y) return Theme.Gold;
            if (code == PadButtons.X) return Theme.Accent;
            if (code == PadButtons.B) return Theme.Danger;
            return Theme.Mint;
        }

        private static Color PhoneActionColor(int layout, int code, float opacity)
        {
            Color c = PhoneActionColor(layout, code);
            return Color.FromArgb((int)Math.Round(255 * opacity), c);
        }

        private static void DrawSonyActionGlyph(Graphics g, int code, RectangleF rect, float opacity)
        {
            Color color = PhoneActionColor((int)PadLayout.Sony, code, opacity);
            float pad = rect.Width * 0.24f;
            RectangleF inner = new RectangleF(rect.X + pad, rect.Y + pad, rect.Width - pad * 2f, rect.Height - pad * 2f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush fill = new SolidBrush(color))
            using (Pen pen = new Pen(color, Math.Max(1.6f, rect.Width * 0.08f)))
            {
                if (code == PadButtons.Y) // △ 实心三角
                {
                    PointF[] tri = new PointF[]
                    {
                        new PointF(inner.X + inner.Width / 2f, inner.Y),
                        new PointF(inner.X, inner.Bottom),
                        new PointF(inner.Right, inner.Bottom)
                    };
                    g.FillPolygon(fill, tri);
                }
                else if (code == PadButtons.X) // □ 实心方块
                {
                    float inset = inner.Width * 0.18f;
                    g.FillRectangle(fill, new RectangleF(inner.X + inset, inner.Y + inset,
                        inner.Width - inset * 2f, inner.Height - inset * 2f));
                }
                else if (code == PadButtons.B) // ○ 空心圆环
                {
                    g.DrawEllipse(pen, inner);
                }
                else // A → × 交叉线
                {
                    g.DrawLine(pen, inner.X, inner.Y, inner.Right, inner.Bottom);
                    g.DrawLine(pen, inner.Right, inner.Y, inner.X, inner.Bottom);
                }
            }
        }

        private void DrawSafeHint(Graphics g)
        {
            if (_viewState == 2)
            {
                float s = Math.Max(0.7f, Math.Min(_screen.Width / 390f, _screen.Height / 700f));
                RectangleF pill = new RectangleF(_screen.Left + _screen.Width / 2f - 43f * s,
                    _screen.Bottom - 31f * s, 86f * s, 20f * s);
                Theme.FillRounded(g, pill, 10f * s, Color.FromArgb(150, 8, 13, 22));
                Theme.DrawRounded(g, pill, 10f * s, Color.FromArgb(55, 69, 215, 191), 1f);
                Theme.DrawText(g, "拖动布局中", Theme.UiFont(6.6f * s, false), Theme.Mint,
                    pill, StringAlignment.Center, StringAlignment.Center, false);
            }
        }

        private void DrawModeMenu(Graphics g)
        {
            if (!_modeMenuOpen || _viewState == 1 || _config == null || _config.Modes == null) return;
            if (_config.Modes.Count == 0) return;
            ModeConfig mode = ActiveMode;
            if (mode == null) return;

            RectangleF anchor = SystemRect(false);
            float itemH = Math.Max(28f, Math.Min(38f, _screen.Height * 0.048f));
            float menuW = Math.Max(130f, Math.Min(_screen.Width * 0.44f, 280f));
            int count = _config.Modes.Count;
            float menuH = count * itemH + 12f;
            float maxH = _screen.Height * 0.52f;
            if (menuH > maxH)
            {
                itemH = Math.Max(15f, (maxH - 12f) / count);
                menuH = count * itemH + 12f;
            }
            float x = anchor.Right - menuW;
            if (x < _screen.Left + 7) x = _screen.Left + 7;
            if (x + menuW > _screen.Right - 7) x = _screen.Right - 7 - menuW;
            float y = anchor.Bottom + 8f;
            if (y + menuH > _screen.Bottom - 18f)
            {
                y = anchor.Top - menuH - 8f;
            }
            if (y < _screen.Top + 8f) y = _screen.Top + 8f;
            _menuBounds = new RectangleF(x, y, menuW, menuH);
            _menuRects.Clear();

            for (int i = 3; i >= 1; i--)
            {
                RectangleF sh = new RectangleF(x - i, y + i * 0.8f + 3f, menuW + i * 2, menuH + i * 1.5f);
                Theme.FillRounded(g, sh, 13f + i, Color.FromArgb(7 + i * 6, 0, 0, 0));
            }
            Theme.FillRounded(g, _menuBounds, 13f, Color.FromArgb(248, 22, 31, 47));
            Theme.DrawRounded(g, _menuBounds, 13f, Color.FromArgb(75, 94, 126), 1f);

            Font font = Theme.UiFont(Math.Max(6.6f, itemH * 0.34f), false);
            for (int i = 0; i < count; i++)
            {
                float iy = y + 6f + i * itemH;
                RectangleF item = new RectangleF(x + 5f, iy, menuW - 10f, itemH);
                _menuRects.Add(item);
                bool active = i == _modeIndex;
                bool hover = i == _hoverMenuIndex;
                if (active)
                {
                    Theme.FillRounded(g, item, 7f, Color.FromArgb(74, 82, 139, 255));
                }
                else if (hover)
                {
                    Theme.FillRounded(g, item, 7f, Color.FromArgb(70, 36, 50, 74));
                }
                if (active)
                {
                    Theme.FillRounded(g, new RectangleF(item.X + 3f, item.Y + item.Height / 2f - item.Height * 0.22f,
                        3f, item.Height * 0.44f), 1.5f, Theme.Accent);
                }
                Theme.DrawText(g, _config.Modes[i].Name, active ? Theme.Semibold(Math.Max(6.6f, itemH * 0.34f)) : font,
                    active ? Color.White : Theme.TextSoft,
                    new RectangleF(item.X + 12f, item.Y, item.Width - 28f, item.Height), StringAlignment.Near, StringAlignment.Center, true);
                if (active)
                {
                    DrawCheck(g, new RectangleF(item.Right - 18f, item.Y + item.Height / 2f - 5f, 10f, 10f), Theme.Mint);
                }
                else
                {
                    Theme.FillCircle(g, new RectangleF(item.Right - 12f, item.Y + item.Height / 2f - 1.5f, 3f, 3f),
                        Color.FromArgb(100, 140, 160, 190));
                }
            }
            _menuModes.Clear();
            for (int i = 0; i < _config.Modes.Count; i++) _menuModes.Add(_config.Modes[i]);
        }

        private void DrawCheck(Graphics g, RectangleF rect, Color color)
        {
            using (Pen p = new Pen(color, Math.Max(1.4f, rect.Width * 0.18f)))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                g.DrawLines(p, new PointF[]
                {
                    new PointF(rect.X + rect.Width * 0.16f, rect.Y + rect.Height * 0.52f),
                    new PointF(rect.X + rect.Width * 0.42f, rect.Y + rect.Height * 0.78f),
                    new PointF(rect.X + rect.Width * 0.86f, rect.Y + rect.Height * 0.22f)
                });
            }
        }

        private void DrawDragBadge(Graphics g)
        {
            if (!_dragging || _dragTarget == null) return;
            float x;
            float y;
            float size;
            if (_dragTarget is VirtualKey)
            {
                VirtualKey key = (VirtualKey)_dragTarget;
                PointF c = KeyCenter(key);
                x = c.X;
                y = c.Y;
                size = KeyDiameter(key);
            }
            else
            {
                bool display = (string)_dragTarget == "display";
                PointF c = SystemCenter(display);
                x = c.X;
                y = c.Y;
                size = SystemDiameter(display);
            }
            string text = "X " + Math.Round((x - _screen.Left) / _screen.Width * 100f).ToString("0") + "%   ·   Y " +
                          Math.Round((y - _screen.Top) / _screen.Height * 100f).ToString("0") + "%";
            float fontScale = Math.Max(0.72f, Math.Min(_screen.Width / 390f, _screen.Height / 700f));
            Font font = Theme.UiFont(6.8f * fontScale, false);
            float w = 110f * fontScale;
            float h = 22f * fontScale;
            float bx = _screen.Left + _screen.Width / 2f - w / 2f;
            float by = _screen.Top + _screen.Height * 0.055f;
            if (_screen.Height < 220) by = _screen.Top + 6;
            RectangleF pill = new RectangleF(bx, by, w, h);
            Theme.FillRounded(g, pill, h / 2f, Color.FromArgb(238, 12, 18, 29));
            Theme.DrawRounded(g, pill, h / 2f, Color.FromArgb(80, 82, 139, 255), 1f);
            Theme.DrawText(g, text, font, Theme.Text, pill, StringAlignment.Center, StringAlignment.Center, false);
        }

        private object HitTest(Point p, out VirtualKey hitKey, out bool hitDisplay, out bool hitMode, out bool hitOverlay)
        {
            hitKey = null;
            hitDisplay = false;
            hitMode = false;
            hitOverlay = false;
            if (_config == null) return null;

            if (_modeMenuOpen)
            {
                if (_menuBounds.Contains(p))
                {
                    for (int i = 0; i < _menuRects.Count; i++)
                    {
                        if (_menuRects[i].Contains(p))
                        {
                            _hoverMenuIndex = i;
                            return "menu:" + i;
                        }
                    }
                    return "menu";
                }
            }

            RectangleF displayRect = SystemRect(true);
            if ((_config == null || _config.DisplayKey == null || _config.DisplayKey.Enabled) && displayRect.Contains(p))
            {
                hitDisplay = true;
                return "display";
            }
            if (_viewState != 1 && (_config == null || _config.ModeKey == null || _config.ModeKey.Enabled))
            {
                RectangleF modeRect = SystemRect(false);
                if (modeRect.Contains(p))
                {
                    hitMode = true;
                    return "mode";
                }
            }

            // 左摇杆始终参与命中：内圈走位、外圈位置拖动由 OnMouseDown 按点击半径区分，
            // 不再依赖「移动」编辑器状态。
            if (JoystickVisible())
            {
                RectangleF joy = JoystickRect();
                if (joy.Width > 0 && joy.Contains(p)) return "joystick";
            }
            // 右摇杆：始终参与位置拖动，真机态静态展示不参与走位轴。
            if (JoystickRVisible())
            {
                RectangleF joyR = JoystickRRect();
                if (joyR.Width > 0 && joyR.Contains(p)) return "joystickR";
            }

            // 主模式方向键整组圆盘优先于普通按键命中，点中整组拖动（不依赖移动态）。
            if (!OverlaySolo && DpadVisible())
            {
                List<VirtualKey> mainDpad = CollectDpad(VisibleKeys());
                if (mainDpad.Count > 0 && DpadRect().Contains(p))
                {
                    _pendingDpadKeys = mainDpad;
                    _pendingDpadOverlay = false;
                    return "dpad";
                }
            }
            // 叠加层方向键整组圆盘。
            if (!MainSolo)
            {
                List<VirtualKey> overlayDpad = CollectDpad(VisibleOverlayKeys());
                if (overlayDpad.Count > 0 && DpadGroupRect(overlayDpad).Contains(p))
                {
                    _pendingDpadKeys = overlayDpad;
                    _pendingDpadOverlay = true;
                    return "dpad";
                }
            }

            // 叠加层按键命中；主界面独立预览时叠加层不参与命中。
            if (!MainSolo)
            {
                List<VirtualKey> overlayKeys = VisibleOverlayKeys();
                for (int i = overlayKeys.Count - 1; i >= 0; i--)
                {
                    if (KeyRect(overlayKeys[i]).Contains(p))
                    {
                        hitKey = overlayKeys[i];
                        hitOverlay = true;
                        return overlayKeys[i];
                    }
                }
            }

            if (!OverlaySolo)
            {
                List<VirtualKey> keys = VisibleKeys();
                for (int i = keys.Count - 1; i >= 0; i--)
                {
                    if (KeyRect(keys[i]).Contains(p))
                    {
                        hitKey = keys[i];
                        return keys[i];
                    }
                }
            }
            // 视角操控仅虚拟按键页参与命中；手柄页不命中视角区域。
            if (!GamepadMode && _config != null && _config.MouseLook != null && _config.MouseLook.Enabled)
            {
                if (_config.ActiveMode == null || _config.ActiveMode.ShowMouseLook)
                {
                    if (!(_viewState == 1 && _config.MouseLook.HideWithHidden))
                    {
                        RectangleF look = MouseLookRect();
                        if (look.Width > 0 && look.Contains(p)) return "mouselook";
                    }
                }
            }
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Focus();
            _mouseDown = true;
            _dragging = false;
            _mouseDownPoint = e.Location;
            _lastMousePoint = e.Location;
            if (_modeMenuOpen && !_menuBounds.Contains(e.Location))
            {
                RectangleF modeRect = SystemRect(false);
                if (!modeRect.Contains(e.Location))
                {
                    _modeMenuOpen = false;
                    _hoverMenuIndex = -1;
                    Invalidate();
                }
            }
            VirtualKey hitKey;
            bool hitDisplay;
            bool hitMode;
            bool hitOverlay;
            object target = HitTest(e.Location, out hitKey, out hitDisplay, out hitMode, out hitOverlay);
            _dragTarget = target;
            _dragTargetIsOverlay = hitOverlay;

            if (target is string && ((string)target).StartsWith("menu:"))
            {
                _mouseDown = false;
                _dragTarget = null;
                int idx = int.Parse(((string)target).Substring(5));
                ChooseMode(idx);
                return;
            }
            if (target is string && (string)target == "menu")
            {
                _mouseDown = false;
                _dragTarget = null;
                return;
            }

            if (hitKey != null)
            {
                if (hitOverlay)
                {
                    _selectedOverlayKey = hitKey;
                    _selectedKey = null;
                }
                else
                {
                    _selectedKey = hitKey;
                    _selectedOverlayKey = null;
                    Action<VirtualKey> selected = KeySelected;
                    if (selected != null) selected(hitKey);
                }
            }

            if (target == null) return;

            if (target is string && (string)target == "joystick")
            {
                RectangleF joy = JoystickRect();
                PointF jc = new PointF(joy.X + joy.Width / 2f, joy.Y + joy.Height / 2f);
                // 收起状态下点击：恢复外圈并重新计时，再进入本次交互。
                if (_joystickCollapsed) RestoreJoystickOuter();
                JoystickConfig joyCfg = CurrentJoystick();
                CancelJoystickHideTimer();
                // 预览态直接拖动位置，不再占用内圈走位轴；走位方向演示只在真机叠加层生效。
                if (joyCfg != null)
                {
                    _dragStartX = joyCfg.X;
                    _dragStartY = joyCfg.Y;
                }
                _dragOffsetX = jc.X - e.X;
                _dragOffsetY = jc.Y - e.Y;
                _selectedKey = null;
                _selectedOverlayKey = null;
                Action<int> stick = StickSelected;
                if (stick != null) stick(0);
                Capture = true;
                Invalidate();
                base.OnMouseDown(e);
                return;
            }

            if (target is string && (string)target == "joystickR")
            {
                // 右摇杆仅参与位置拖动，不走位轴；任意状态都可直接拖动调整位置。
                RectangleF joyR = JoystickRRect();
                PointF rc = new PointF(joyR.X + joyR.Width / 2f, joyR.Y + joyR.Height / 2f);
                JoystickConfig joyCfg = CurrentJoystickR();
                if (joyCfg != null)
                {
                    _dragStartX = joyCfg.X;
                    _dragStartY = joyCfg.Y;
                }
                _dragOffsetX = rc.X - e.X;
                _dragOffsetY = rc.Y - e.Y;
                _selectedKey = null;
                _selectedOverlayKey = null;
                Action<int> stickR = StickSelected;
                if (stickR != null) stickR(1);
                Capture = true;
                Invalidate();
                base.OnMouseDown(e);
                return;
            }

            if (target is string && (string)target == "dpad")
            {
                _dragDpadKeys = _pendingDpadKeys;
                _dragTargetIsOverlay = _pendingDpadOverlay;
                if (_dragDpadKeys != null && _dragDpadKeys.Count > 0)
                {
                    PointF dc;
                    if (_pendingDpadOverlay)
                    {
                        dc = DpadGroupCenter(_dragDpadKeys);
                    }
                    else
                    {
                        RectangleF dr = DpadRect();
                        dc = dr.Width > 0
                            ? new PointF(dr.X + dr.Width / 2f, dr.Y + dr.Height / 2f)
                            : DpadGroupCenter(_dragDpadKeys);
                    }
                    _dragOffsetX = dc.X - e.X;
                    _dragOffsetY = dc.Y - e.Y;
                    _dragStartX = dc.X;
                    _dragStartY = dc.Y;
                    VirtualKey first = _dragDpadKeys[0];
                    if (_pendingDpadOverlay)
                    {
                        _selectedOverlayKey = first;
                        _selectedKey = null;
                    }
                    else
                    {
                        _selectedKey = null;
                        _selectedOverlayKey = null;
                        Action<int> stickDpad = StickSelected;
                        if (stickDpad != null) stickDpad(2);
                    }
                }
                Capture = true;
                Invalidate();
                base.OnMouseDown(e);
                return;
            }

            if (target is string && (string)target == "mouselook")
            {
                RectangleF look = MouseLookRect();
                PointF lc = new PointF(look.X + look.Width / 2f, look.Y + look.Height / 2f);
                // 预览态直接拖动位置，不再依赖显示键「移动」模式；视角向量演示只在真机叠加层生效。
                _dragStartX = _config.MouseLook.X;
                _dragStartY = _config.MouseLook.Y;
                _dragOffsetX = lc.X - e.X;
                _dragOffsetY = lc.Y - e.Y;
                Capture = true;
                Invalidate();
                base.OnMouseDown(e);
                return;
            }

            if (target is VirtualKey)
            {
                VirtualKey key = (VirtualKey)target;
                PointF c = KeyCenter(key);
                _dragStartX = key.X;
                _dragStartY = key.Y;
                _dragOffsetX = c.X - e.X;
                _dragOffsetY = c.Y - e.Y;
            }
            else
            {
                bool display = (string)target == "display";
                PointF c = SystemCenter(display);
                AppConfig sys = SysRoot;
                _dragStartX = display ? sys.DisplayKeyX : sys.ModeKeyX;
                _dragStartY = display ? sys.DisplayKeyY : sys.ModeKeyY;
                _dragOffsetX = c.X - e.X;
                _dragOffsetY = c.Y - e.Y;
            }
            Capture = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            _lastMousePoint = e.Location;

            if (_modeMenuOpen)
            {
                int oldHover = _hoverMenuIndex;
                _hoverMenuIndex = -1;
                for (int i = 0; i < _menuRects.Count; i++)
                {
                    if (_menuRects[i].Contains(e.Location))
                    {
                        _hoverMenuIndex = i;
                        break;
                    }
                }
                if (oldHover != _hoverMenuIndex) Invalidate();
            }

            if (_joystickKnobDragging)
            {
                CancelJoystickHideTimer();
                UpdateJoystickVector(e.Location);
                Invalidate();
                base.OnMouseMove(e);
                return;
            }

            if (_lookDragging)
            {
                UpdateLookVector(e.Location);
                Invalidate();
                base.OnMouseMove(e);
                return;
            }

            if (_mouseDown && _dragTarget != null)
            {
                if (!_dragging)
                {
                    float dx = e.X - _mouseDownPoint.X;
                    float dy = e.Y - _mouseDownPoint.Y;
                    if (dx * dx + dy * dy > 9f)
                    {
                        _dragging = true;
                        Cursor = Cursors.SizeAll;
                    }
                }
                if (_dragging)
                {
                    MoveDragTarget(e.Location);
                    Invalidate();
                }
                base.OnMouseMove(e);
                return;
            }

            VirtualKey hoverKey;
            bool hoverDisplay;
            bool hoverMode;
            bool hoverOverlay;
            object target = HitTest(e.Location, out hoverKey, out hoverDisplay, out hoverMode, out hoverOverlay);
            object newHover = target;
            if (target is string && ((string)target).StartsWith("menu")) newHover = "menu";
            if (!object.Equals(newHover, _hoverTarget))
            {
                _hoverTarget = newHover;
                Invalidate();
            }
            if (target == null)
            {
                Cursor = Cursors.Default;
            }
            else if (target is string && ((string)target).StartsWith("menu"))
            {
                Cursor = (_hoverMenuIndex >= 0) ? Cursors.Hand : Cursors.Default;
            }
            else if (target is string &&
                (((string)target == "joystick") || ((string)target == "joystickR") ||
                 ((string)target == "dpad") || ((string)target == "mouselook")))
            {
                Cursor = Cursors.SizeAll;
            }
            else
            {
                Cursor = Cursors.Hand;
            }
            base.OnMouseMove(e);
        }

        private void MoveDragTarget(Point mouse)
        {
            float nx;
            float ny;
            if (_dragTarget is VirtualKey)
            {
                VirtualKey key = (VirtualKey)_dragTarget;
                nx = (mouse.X + _dragOffsetX - _screen.Left) / _screen.Width;
                ny = (mouse.Y + _dragOffsetY - _screen.Top) / _screen.Height;
                float marginX = KeyDiameter(key) / 2f / _screen.Width;
                float marginY = KeyDiameter(key) / 2f / _screen.Height;
                key.X = AppConfig.ClampF(nx, marginX, 1f - marginX);
                key.Y = AppConfig.ClampF(ny, marginY, 1f - marginY);
            }
            else if ((string)_dragTarget == "joystick")
            {
                JoystickConfig joyCfg = CurrentJoystick();
                if (joyCfg == null) return;
                nx = (mouse.X + _dragOffsetX - _screen.Left) / _screen.Width;
                ny = (mouse.Y + _dragOffsetY - _screen.Top) / _screen.Height;
                float d = joyCfg.Size * _screenScale;
                float marginX = d / 2f / _screen.Width;
                float marginY = d / 2f / _screen.Height;
                joyCfg.X = AppConfig.ClampF(nx, marginX, 1f - marginX);
                joyCfg.Y = AppConfig.ClampF(ny, marginY, 1f - marginY);
            }
            else if ((string)_dragTarget == "joystickR")
            {
                JoystickConfig joyR = CurrentJoystickR();
                if (joyR == null) return;
                nx = (mouse.X + _dragOffsetX - _screen.Left) / _screen.Width;
                ny = (mouse.Y + _dragOffsetY - _screen.Top) / _screen.Height;
                float d = joyR.Size * _screenScale;
                float marginX = d / 2f / _screen.Width;
                float marginY = d / 2f / _screen.Height;
                joyR.X = AppConfig.ClampF(nx, marginX, 1f - marginX);
                joyR.Y = AppConfig.ClampF(ny, marginY, 1f - marginY);
            }
            else if ((string)_dragTarget == "dpad")
            {
                if (_dragDpadKeys == null || _dragDpadKeys.Count == 0) return;
                PointF targetCenter = new PointF(mouse.X + _dragOffsetX, mouse.Y + _dragOffsetY);
                PointF oldCenter = new PointF(_dragStartX, _dragStartY);
                float ddx = (targetCenter.X - oldCenter.X) / _screen.Width;
                float ddy = (targetCenter.Y - oldCenter.Y) / _screen.Height;

                DpadConfig dpadCfg = _pendingDpadOverlay ? null : CurrentDpad();
                if (dpadCfg != null)
                {
                    float cx = (targetCenter.X - _screen.Left) / _screen.Width;
                    float cy = (targetCenter.Y - _screen.Top) / _screen.Height;
                    float dd = dpadCfg.Size * _screenScale;
                    float marginX = dd / 2f / _screen.Width;
                    float marginY = dd / 2f / _screen.Height;
                    dpadCfg.X = AppConfig.ClampF(cx, marginX, 1f - marginX);
                    dpadCfg.Y = AppConfig.ClampF(cy, marginY, 1f - marginY);
                }
                for (int i = 0; i < _dragDpadKeys.Count; i++)
                {
                    VirtualKey k = _dragDpadKeys[i];
                    float marginX = KeyDiameter(k) / 2f / _screen.Width;
                    float marginY = KeyDiameter(k) / 2f / _screen.Height;
                    k.X = AppConfig.ClampF(k.X + ddx, marginX, 1f - marginX);
                    k.Y = AppConfig.ClampF(k.Y + ddy, marginY, 1f - marginY);
                }
            }
            else if ((string)_dragTarget == "mouselook")
            {
                nx = (mouse.X + _dragOffsetX - _screen.Left) / _screen.Width;
                ny = (mouse.Y + _dragOffsetY - _screen.Top) / _screen.Height;
                float w = _config.MouseLook.Width * _screenScale;
                float h = _config.MouseLook.Height * _screenScale;
                _config.MouseLook.X = AppConfig.ClampF(nx, w / 2f / _screen.Width, 1f - w / 2f / _screen.Width);
                _config.MouseLook.Y = AppConfig.ClampF(ny, h / 2f / _screen.Height, 1f - h / 2f / _screen.Height);
            }
            else
            {
                bool display = (string)_dragTarget == "display";
                nx = (mouse.X + _dragOffsetX - _screen.Left) / _screen.Width;
                ny = (mouse.Y + _dragOffsetY - _screen.Top) / _screen.Height;
                float marginX = SystemDiameter(display) / 2f / _screen.Width;
                float marginY = SystemDiameter(display) / 2f / _screen.Height;
                nx = AppConfig.ClampF(nx, marginX, 1f - marginX);
                ny = AppConfig.ClampF(ny, marginY, 1f - marginY);
                AppConfig sys = SysRoot;
                if (display)
                {
                    sys.DisplayKeyX = nx;
                    sys.DisplayKeyY = ny;
                }
                else
                {
                    sys.ModeKeyX = nx;
                    sys.ModeKeyY = ny;
                }
            }
        }

        private void UpdateJoystickVector(Point mouse)
        {
            JoystickConfig joy = CurrentJoystick();
            if (joy == null) return;
            RectangleF rect = JoystickRect();
            if (rect.Width <= 0) return;
            PointF center = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
            float outerD = Math.Min(rect.Width, joy.OuterSize * _screenScale);
            float innerD = Math.Min(outerD, joy.InnerSize * _screenScale);
            float travel = Math.Max(1f, outerD / 2f - innerD / 2f - outerD * 0.06f);
            float ox = (mouse.X - center.X) / travel;
            float oy = (mouse.Y - center.Y) / travel;
            float rawLen = (float)Math.Sqrt(ox * ox + oy * oy);
            if (rawLen > 1f)
            {
                ox /= rawLen;
                oy /= rawLen;
            }
            float outLen;
            JoystickMath.Apply(joy, ox, oy, out _joystickDx, out _joystickDy, out outLen);
        }

        private void UpdateLookVector(Point mouse)
        {
            RectangleF rect = MouseLookRect();
            if (rect.Width <= 0) return;
            PointF center = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
            _lookDx = (mouse.X - center.X) / Math.Max(1f, rect.Width * 0.38f);
            _lookDy = (mouse.Y - center.Y) / Math.Max(1f, rect.Height * 0.38f);
            float len = (float)Math.Sqrt(_lookDx * _lookDx + _lookDy * _lookDy);
            if (len > 1f)
            {
                _lookDx /= len;
                _lookDy /= len;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (_joystickKnobDragging)
            {
                _joystickKnobDragging = false;
                _joystickDx = 0f;
                _joystickDy = 0f;
                _mouseDown = false;
                _dragging = false;
                Capture = false;
                Cursor = Cursors.Default;
                RestartJoystickHideTimer();
                Invalidate();
                base.OnMouseUp(e);
                return;
            }
            if (_lookDragging)
            {
                _lookDragging = false;
                _lookDx = 0f;
                _lookDy = 0f;
                _mouseDown = false;
                _dragging = false;
                Capture = false;
                Cursor = Cursors.Default;
                Invalidate();
                base.OnMouseUp(e);
                return;
            }
            bool wasDragging = _dragging;
            object target = _dragTarget;
            _mouseDown = false;
            _dragging = false;
            Capture = false;
            Cursor = Cursors.Default;

            if (wasDragging)
            {
                Action h = LayoutChanged;
                if (h != null) h();
            }
            else if (target != null)
            {
                if (target is string && (string)target == "display")
                {
                    SetViewState((_viewState + 1) % 3);
                }
                else if (target is string && (string)target == "mode")
                {
                    _modeMenuOpen = !_modeMenuOpen;
                    _hoverMenuIndex = -1;
                    Invalidate();
                }
                else if (target is VirtualKey)
                {
                    if (!_dragTargetIsOverlay)
                    {
                        Action<VirtualKey> selected = KeySelected;
                        if (selected != null) selected((VirtualKey)target);
                    }
                }
            }
            _dragTarget = null;
            _dragDpadKeys = null;
            _pendingDpadKeys = null;
            Invalidate();
            base.OnMouseUp(e);
        }

        private void ChooseMode(int index)
        {
            _modeMenuOpen = false;
            _hoverMenuIndex = -1;
            Invalidate();
            Action<int> h = ModeSwitchRequested;
            if (h != null) h(index);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (!_mouseDown)
            {
                _hoverTarget = null;
                _hoverMenuIndex = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (_modeMenuOpen)
            {
                // The menu is sized to fit the visible screen, so wheel only closes it on scroll.
                _modeMenuOpen = false;
                Invalidate();
            }
            if (WheelScrolled != null) WheelScrolled(e.Delta);
            base.OnMouseWheel(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _modeMenuOpen = false;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_joystickHideTimer != null) _joystickHideTimer.Dispose();
                ClearImageCache();
            }
            base.Dispose(disposing);
        }
    }
}
