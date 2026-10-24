using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MobileUiConfig
{
    // Optional global input helpers. The low-level hook deliberately ignores our own process,
    // so the configuration UI remains usable while a game is in front.
    internal sealed class InputAssistService : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int VK_F4 = 0x73;
        private const int VK_MENU = 0x12;
        private const short KEY_PRESSED = unchecked((short)0x8000);

        private const uint POINTER_FLAG_INRANGE = 0x00000002;
        private const uint POINTER_FLAG_INCONTACT = 0x00000004;
        private const uint POINTER_FLAG_PRIMARY = 0x00002000;
        private const uint POINTER_FLAG_DOWN = 0x00010000;
        private const uint POINTER_FLAG_UPDATE = 0x00020000;
        private const uint POINTER_FLAG_UP = 0x00040000;
        private const uint TOUCH_MASK_CONTACTAREA = 0x00000001;
        private const uint TOUCH_MASK_PRESSURE = 0x00000004;

        private readonly AppConfig _main;
        private readonly Func<AppConfig> _rootProvider;
        private readonly HookProc _mouseProc;
        private readonly HookProc _keyProc;
        private IntPtr _mouseHook;
        private IntPtr _keyHook;
        private bool _disposed;
        private bool _touchDown;
        private bool _injecting;
        private bool _touchReady;
        private uint _ownPid;
        private bool _contextActive;
        // 显示键「拦截鼠标点击」运行时开关：仅按键页生效，只拦游戏前台窗口的鼠标按下/抬起，
        // 不拦鼠标移动与滚轮；不持久化，游戏中由显示键随时切换，重启默认关闭。
        private bool _blockMouseClicks;

        private AppConfig Root
        {
            get
            {
                AppConfig r = _rootProvider == null ? null : _rootProvider();
                return r == null ? _main : r;
            }
        }

        public InputAssistService(AppConfig mainConfig, Func<AppConfig> rootProvider)
        {
            _main = mainConfig;
            _rootProvider = rootProvider;
            _mouseProc = MouseHookCallback;
            _keyProc = KeyHookCallback;
            _ownPid = (uint)Process.GetCurrentProcess().Id;
            Apply();
        }

        public void SetContextActive(bool active)
        {
            if (_contextActive == active) return;
            _contextActive = active;
            Apply();
        }

        public bool BlockMouseClicks { get { return _blockMouseClicks; } }

        public void SetBlockMouseClicks(bool on)
        {
            if (_blockMouseClicks == on) return;
            _blockMouseClicks = on;
            Apply();
            ConfigStore.Log(on ? "已开启拦截游戏鼠标点击（不拦移动/滚轮）" : "已关闭拦截游戏鼠标点击");
        }

        public void ToggleBlockMouseClicks()
        {
            SetBlockMouseClicks(!_blockMouseClicks);
        }

        // 手柄页开启「屏蔽物理键鼠」时，是否需要吞掉物理输入。页面侧是系统级标志，读主根；
        // 开关值读当前页独立根（手柄配置存 Gamepad 根），两页互不影响。
        private bool BlockPhysical
        {
            get
            {
                AppConfig r = Root;
                return _main != null && _main.UiPadSide == 1 && r != null && r.BlockPhysicalInput;
            }
        }

        public void Apply()
        {
            bool padPage = _main != null && _main.UiPadSide == 0;
            // 鼠标钩子：按键页旧功能（鼠标转触摸 / 双指缩放 / 拦截鼠标点击，仅按键页），
            // 或手柄页屏蔽物理鼠标按钮。
            bool mouseNeed = _contextActive && _main != null &&
                ((padPage && (_main.MouseToTouch || _main.PinchZoom || _blockMouseClicks)) || BlockPhysical);
            if (mouseNeed && _mouseHook == IntPtr.Zero)
            {
                _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
                if (_mouseHook == IntPtr.Zero) ConfigStore.Log("鼠标输入钩子安装失败: " + Marshal.GetLastWin32Error());
            }
            else if (!mouseNeed && _mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
                _touchDown = false;
            }
            // 键盘钩子：仅手柄页屏蔽物理键盘时安装。
            bool keyNeed = _contextActive && BlockPhysical;
            if (keyNeed && _keyHook == IntPtr.Zero)
            {
                _keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyProc, GetModuleHandle(null), 0);
                if (_keyHook == IntPtr.Zero) ConfigStore.Log("键盘输入钩子安装失败: " + Marshal.GetLastWin32Error());
            }
            else if (!keyNeed && _keyHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyHook);
                _keyHook = IntPtr.Zero;
            }
            if (padPage && _main.MouseToTouch && !_touchReady)
            {
                _touchReady = InitializeTouchInjection(1, 1);
                if (!_touchReady) ConfigStore.Log("触摸注入初始化失败: " + Marshal.GetLastWin32Error());
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0 || _main == null || _injecting) return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            MSLLHOOKSTRUCT data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
            if (data.dwExtraInfo.ToInt64() == InputSender.InputMagic)
                return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            int msg = wParam.ToInt32();

            // 手柄页屏蔽物理鼠标按钮：自身配置窗口与我方注入不拦；移动、滚轮放行。
            if (BlockPhysical && !IsOwnForeground())
            {
                if (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP ||
                    msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP ||
                    msg == WM_MBUTTONDOWN || msg == WM_MBUTTONUP ||
                    msg == WM_XBUTTONDOWN || msg == WM_XBUTTONUP)
                {
                    return (IntPtr)1;
                }
            }

            // 双指缩放仅按键页生效，手柄页不执行旧鼠标辅助逻辑。
            bool padPage = _main.UiPadSide == 0;

            // 按键页「拦截鼠标点击」：钩子本身只在游戏/串流前台（_contextActive=true）期间
            // 安装，回调期间前台就是游戏或串流窗口；这里只排除自身配置窗口，吞掉鼠标左/右/中/X
            // 键的按下与抬起，避免手指误触屏幕边缘把鼠标左键点进游戏变成普攻；鼠标移动、滚轮
            // 一律放行；桌面与其它程序不在钩子安装窗口内，天然不会被拦。开启期间不执行下方左键转触摸。
            if (padPage && _blockMouseClicks && !IsOwnForeground())
            {
                if (msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP ||
                    msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP ||
                    msg == WM_MBUTTONDOWN || msg == WM_MBUTTONUP ||
                    msg == WM_XBUTTONDOWN || msg == WM_XBUTTONUP)
                {
                    return (IntPtr)1;
                }
            }

            if (padPage && msg == WM_MOUSEWHEEL && _main.PinchZoom)
            {
                if (!IsOwnForeground())
                {
                    int delta = (short)((data.mouseData >> 16) & 0xffff);
                    SendZoom(delta);
                    return (IntPtr)1;
                }
                return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            }
            if (!padPage || !_main.MouseToTouch) return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            if (IsOwnForeground()) return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            if (msg == WM_LBUTTONDOWN)
            {
                if (!_touchReady) _touchReady = InitializeTouchInjection(1, 1);
                if (!_touchReady) return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                _touchDown = true;
                SendTouch(data.pt, POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_PRIMARY);
                return (IntPtr)1;
            }
            if (msg == WM_MOUSEMOVE && _touchDown)
            {
                SendTouch(data.pt, POINTER_FLAG_UPDATE | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT);
                return (IntPtr)1;
            }
            if (msg == WM_LBUTTONUP && _touchDown)
            {
                _touchDown = false;
                SendTouch(data.pt, POINTER_FLAG_UP);
                return (IntPtr)1;
            }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private IntPtr KeyHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0 || !BlockPhysical || _injecting) return CallNextHookEx(_keyHook, nCode, wParam, lParam);
            if (IsOwnForeground()) return CallNextHookEx(_keyHook, nCode, wParam, lParam);
            KBDLLHOOKSTRUCT data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            if (data.dwExtraInfo.ToInt64() == InputSender.InputMagic)
                return CallNextHookEx(_keyHook, nCode, wParam, lParam);
            int msg = wParam.ToInt32();
            // Alt+F4 等系统组合键必须完整放行给系统/游戏窗口，否则屏蔽物理键鼠后无法强制
            // 退出游戏。根因：上一轮只单独放行 F4，但 Alt(VK_MENU) 自身的按下/抬起仍被吞，
            // 系统收不到 Alt 按住状态，F4 不被识别为 Alt+F4。本轮口径：Alt 键本身一律放行；
            // Alt 处于异步按住状态期间，后续所有按键消息一并放行（覆盖 Alt+F4、Alt+Tab 等
            // 系统组合键）；其余物理键盘按键继续拦截。Alt 状态以异步键态读取（KBDLLHOOKSTRUCT
            // 本身不带修饰位）。
            bool altDown = (GetAsyncKeyState(VK_MENU) & KEY_PRESSED) != 0;
            if (data.vkCode == VK_MENU || altDown)
                return CallNextHookEx(_keyHook, nCode, wParam, lParam);
            if (msg == WM_KEYDOWN || msg == WM_KEYUP || msg == WM_SYSKEYDOWN || msg == WM_SYSKEYUP)
            {
                return (IntPtr)1;
            }
            return CallNextHookEx(_keyHook, nCode, wParam, lParam);
        }

        private bool IsOwnForeground()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero) return false;
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            return pid == _ownPid;
        }


        private void SendTouch(POINT pt, uint flags)
        {
            if (!_touchReady || _injecting) return;
            _injecting = true;
            try
            {
                int sw = Math.Max(1, GetSystemMetrics(0));
                int sh = Math.Max(1, GetSystemMetrics(1));
                POINTER_TOUCH_INFO info = new POINTER_TOUCH_INFO();
                info.pointerInfo.pointerType = 2; // PT_TOUCH
                info.pointerInfo.pointerId = 0;
                info.pointerInfo.frameId = 0;
                info.pointerInfo.pointerFlags = flags;
                info.pointerInfo.ptPixelLocation = pt;
                info.pointerInfo.ptPixelLocationRaw = pt;
                info.pointerInfo.ptHimetricLocation = pt;
                info.pointerInfo.ptHimetricLocationRaw = pt;
                info.pointerInfo.dwTime = (uint)Environment.TickCount;
                info.touchFlags = 0;
                info.touchMask = TOUCH_MASK_CONTACTAREA | TOUCH_MASK_PRESSURE;
                info.rcContact = new RECT
                {
                    Left = Math.Max(0, pt.X - 8),
                    Top = Math.Max(0, pt.Y - 8),
                    Right = Math.Min(sw, pt.X + 8),
                    Bottom = Math.Min(sh, pt.Y + 8)
                };
                info.rcContactRaw = info.rcContact;
                info.orientation = 0;
                info.pressure = 320;
                POINTER_TOUCH_INFO[] contacts = new POINTER_TOUCH_INFO[] { info };
                InjectTouchInput(1, contacts);
            }
            catch (Exception ex)
            {
                ConfigStore.Log("触摸注入失败: " + ex.Message);
            }
            finally
            {
                _injecting = false;
            }
        }

        private void SendZoom(int delta)
        {
            if (_injecting || delta == 0) return;
            _injecting = true;
            try
            {
                // 纯滚轮缩放视野：不附带 Ctrl 修饰键。原实现按住 Ctrl 再滚滚轮，组合状态在
                // 游戏内容易触发其它动作；滚轮本身即承担视野缩放，不需要任何修饰键。
                mouse_event(0x0800, 0, 0, (uint)delta, UIntPtr.Zero);
            }
            catch { }
            finally
            {
                _injecting = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            if (_keyHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyHook);
                _keyHook = IntPtr.Zero;
            }
        }

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINTER_INFO
        {
            public int pointerType;
            public uint pointerId;
            public uint frameId;
            public uint pointerFlags;
            public IntPtr sourceDevice;
            public IntPtr hwndTarget;
            public POINT ptPixelLocation;
            public POINT ptHimetricLocation;
            public POINT ptPixelLocationRaw;
            public POINT ptHimetricLocationRaw;
            public uint dwTime;
            public uint historyCount;
            public int inputState;
            public uint keyStates;
            public ulong performanceCount;
            public int buttonChangeType;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINTER_TOUCH_INFO
        {
            public POINTER_INFO pointerInfo;
            public uint touchFlags;
            public uint touchMask;
            public RECT rcContact;
            public RECT rcContactRaw;
            public uint orientation;
            public uint pressure;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr hMod, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool InitializeTouchInjection(uint maxCount, uint mode);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool InjectTouchInput(uint count, [In] POINTER_TOUCH_INFO[] contacts);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
    }
}
