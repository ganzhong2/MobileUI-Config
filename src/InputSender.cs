// ============================================================================
// InputSender.cs —— 输入注入核心（把虚拟按键绑定转换成真实键鼠 / 手柄事件）
// ----------------------------------------------------------------------------
// 悬浮层虚拟按键被按下 / 抬起后，统一调用本类的 SendBinding 派发：
//   · Keyboard ：SendInput 注入键盘按下 / 抬起，支持 Ctrl / Shift / Alt 组合键；
//   · MouseButton：SendInput 注入鼠标左 / 右 / 中 / 侧键按下抬起；
//   · MouseWheel：发送鼠标滚轮滚动（仅在按下瞬间触发一次）；
//   · Gamepad：转交给 GamepadService 上报 ViGEm 虚拟手柄按钮 / 扳机。
// 类内用 PressedKeys / PressedMouse 集合跟踪当前处于按下状态的键与鼠标键，
// ReleaseAll 在游戏切后台、退出或悬浮层关闭时统一兜底释放，避免修饰键卡在按下态。
// 注入的鼠标事件携带内部魔数 InputMagic，供输入辅助钩子识别并放行，避免二次拦截。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MobileUiConfig
{
    /// <summary>静态输入注入门面：所有虚拟按键到物理键鼠 / 手柄事件的转换都经过本类。</summary>
    internal static class InputSender
    {
        internal const long InputMagic = 0x57574D54L; // "WWMT": ignore our own injected mouse events in hooks
        private const uint INPUT_KEYBOARD = 1;
        private const uint INPUT_MOUSE = 0;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const uint MAPVK_VK_TO_VSC = 0;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_XDOWN = 0x0080;
        private const uint MOUSEEVENTF_XUP = 0x0100;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint XBUTTON1 = 0x0001;
        private const uint XBUTTON2 = 0x0002;

        private static readonly HashSet<int> PressedKeys = new HashSet<int>();
        private static readonly Dictionary<int, int> SuppressKeys = new Dictionary<int, int>();

        public static void SuppressKey(int vk, int milliseconds)
        {
            SuppressKeys[vk] = Environment.TickCount + milliseconds;
        }
        private static readonly HashSet<int> PressedMouse = new HashSet<int>();
        private static readonly object Gate = new object();

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        /// <summary>
        /// 把一条绑定按下（down=true）或抬起（down=false）派发为对应物理事件。
        /// mousePoint 为鼠标按键注入前希望移动到的屏幕坐标，moveMouse 控制是否先移动光标；
        /// 鼠标滚轮只在按下瞬间发送一次滚动，不参与抬起。
        /// </summary>
        public static void SendBinding(KeyBinding binding, bool down, Point mousePoint, bool moveMouse)
        {
            if (binding == null || binding.Kind == BindingKind.None) return;
            lock (Gate)
            {
                if (binding.Kind == BindingKind.Keyboard)
                {
                    Keys key = (Keys)binding.Code;
                    ConfigStore.Log("按键 " + key + (down ? " 按下" : " 松开"));
                    if (down)
                    {
                        if ((binding.Modifiers & (int)Keys.Control) != 0) SendKeyboardKey(Keys.ControlKey, false);
                        if ((binding.Modifiers & (int)Keys.Shift) != 0) SendKeyboardKey(Keys.ShiftKey, false);
                        if ((binding.Modifiers & (int)Keys.Alt) != 0) SendKeyboardKey(Keys.Menu, false);
                        SendKeyboardKey(key, false);
                    }
                    else
                    {
                        SendKeyboardKey(key, true);
                        if ((binding.Modifiers & (int)Keys.Alt) != 0) SendKeyboardKey(Keys.Menu, true);
                        if ((binding.Modifiers & (int)Keys.Shift) != 0) SendKeyboardKey(Keys.ShiftKey, true);
                        if ((binding.Modifiers & (int)Keys.Control) != 0) SendKeyboardKey(Keys.ControlKey, true);
                    }
                }
                else if (binding.Kind == BindingKind.MouseButton)
                {
                    if (moveMouse && down)
                    {
                        try { SetCursorPos(mousePoint.X, mousePoint.Y); }
                        catch { }
                    }
                    SendMouseButton(binding.Code, down, "按键");
                }
                else if (binding.Kind == BindingKind.MouseWheel)
                {
                    if (!down) return;
                    if (moveMouse)
                    {
                        try { SetCursorPos(mousePoint.X, mousePoint.Y); }
                        catch { }
                    }
                    SendMouseWheel(binding.Code > 0 ? 1 : -1);
                }
                else if (binding.Kind == BindingKind.Gamepad)
                {
                    try
                    {
                        if (binding.Modifiers == PadButtons.ModifierLeftTrigger || binding.Modifiers == PadButtons.ModifierRightTrigger)
                        {
                            GamepadService.SetTrigger(binding.Modifiers, down);
                        }
                        else
                        {
                            GamepadService.SetButton((ushort)binding.Code, down);
                        }
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// 兜底释放全部输入：松开仍处于按下状态的按键与鼠标键，补发 Win / Ctrl / Shift / Alt
        /// 等通用修饰键抬起，避免程序退出或切后台时修饰键残留在按下态（例如触发 Win+L 锁屏），
        /// 同时释放虚拟手柄全部按钮。游戏切后台与程序退出时都会调用。
        /// </summary>
        public static void ReleaseAll()
        {
            lock (Gate)
            {
                int[] keys = new int[PressedKeys.Count];
                PressedKeys.CopyTo(keys);
                for (int i = 0; i < keys.Length; i++) SendKeyboardKey((Keys)keys[i], true);
                // Also clear common modifier keys. If the app is closed while a
                // modifier is held, leaving Win down makes a later "L" produce
                // Win+L (lock screen). Key-up for an already-up key is harmless.
                int[] safetyKeys = new int[]
                {
                    (int)Keys.LWin, (int)Keys.RWin,
                    (int)Keys.LControlKey, (int)Keys.RControlKey,
                    (int)Keys.LShiftKey, (int)Keys.RShiftKey,
                    (int)Keys.LMenu, (int)Keys.RMenu,
                    (int)Keys.L
                };
                for (int i = 0; i < safetyKeys.Length; i++) SendKeyboardKey((Keys)safetyKeys[i], true);
                int[] mouse = new int[PressedMouse.Count];
                PressedMouse.CopyTo(mouse);
                for (int i = 0; i < mouse.Length; i++) SendMouseButton(mouse[i], true);
                for (int i = 0; i < 5; i++) SendMouseButton(i, true);
                try { GamepadService.ReleaseAll(); } catch { }
            }
        }

        private static void SendKeyboardKey(Keys key, bool up)
        {
            if (key == Keys.None) return;
            int vk = (int)key;
            int now = Environment.TickCount;
            if (!up)
            {
                int until;
                if (SuppressKeys.TryGetValue(vk, out until) && now < until) return;
            }
            if (!up && PressedKeys.Contains(vk)) return; // touch + mouse duplicate down
            if (up) PressedKeys.Remove(vk);
            else PressedKeys.Add(vk);
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].U.ki.wVk = 0;
            inputs[0].U.ki.wScan = (ushort)MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC);
            inputs[0].U.ki.dwFlags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0);
            inputs[0].U.ki.dwExtraInfo = (IntPtr)InputMagic;
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (up) PostKeyUpToGame(vk);
        }

        private static IntPtr FindGameWindow()
        {
            try
            {
                Process[] ps = Process.GetProcessesByName("yysls");
                for (int i = 0; i < ps.Length; i++)
                {
                    try
                    {
                        if (ps[i].MainWindowHandle != IntPtr.Zero)
                        {
                            IntPtr h = ps[i].MainWindowHandle;
                            for (int k = 0; k < ps.Length; k++) ps[k].Dispose();
                            return h;
                        }
                    }
                    catch { }
                }
                for (int i = 0; i < ps.Length; i++) try { ps[i].Dispose(); } catch { }
            }
            catch { }
            return IntPtr.Zero;
        }

        private static void PostKeyUpToGame(int vk)
        {
            try
            {
                ushort scan = (ushort)MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC);
                uint lp = 0xC0000001u | ((uint)scan << 16);
                IntPtr game = FindGameWindow();
                if (game != IntPtr.Zero) PostMessage(game, 0x0101, (IntPtr)vk, (IntPtr)lp);
                // Also post to the current foreground window: the game may have
                // switched windows during a loading/world-entry transition, and
                // the focused window at key-up time may differ from the one
                // FindGameWindow returns.
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero && fg != game) PostMessage(fg, 0x0101, (IntPtr)vk, (IntPtr)lp);
            }
            catch { }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        /// <summary>鼠标按键按下 / 抬起便捷重载，source 仅用于日志标注触发来源。</summary>
        public static void SendMouseButton(int code, bool up)
        {
            SendMouseButton(code, up, "系统");
        }

        /// <summary>
        /// 注入鼠标按键事件：code 0=左键、1=右键、2=中键、3/4=侧键；up 为 true 表示抬起。
        /// 同步维护 PressedMouse 集合，事件携带内部魔数供钩子识别放行。
        /// </summary>
        public static void SendMouseButton(int code, bool up, string source)
        {
            if (code < 0 || code > 4) return;
            lock (Gate)
            {
                uint flag;
                uint data = 0;
                if (code == 0) flag = up ? MOUSEEVENTF_LEFTUP : MOUSEEVENTF_LEFTDOWN;
                else if (code == 1) flag = up ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_RIGHTDOWN;
                else if (code == 2) flag = up ? MOUSEEVENTF_MIDDLEUP : MOUSEEVENTF_MIDDLEDOWN;
                else
                {
                    flag = up ? MOUSEEVENTF_XUP : MOUSEEVENTF_XDOWN;
                    data = code == 3 ? XBUTTON1 : XBUTTON2;
                }

                // 仅右键注入记日志，便于定位启动游戏时桌面右键菜单的触发来源；
                // 左键 / 中键 / X 键不记录以免日志淹没。
                if (code == 1)
                {
                    ConfigStore.Log("[鼠标] 右键" + (up ? "松开" : "按下") + " 来源=" + (source ?? ""));
                }

                if (up) PressedMouse.Remove(code);
                else PressedMouse.Add(code);

                INPUT[] inputs = new INPUT[1];
                inputs[0].type = INPUT_MOUSE;
                inputs[0].U.mi.dwFlags = flag;
                inputs[0].U.mi.mouseData = data;
                inputs[0].U.mi.dwExtraInfo = (IntPtr)InputMagic;
                SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
            }
        }

        /// <summary>发送相对鼠标移动（视角操控滑动使用），dx/dy 为相对位移，事件携带内部魔数。</summary>
        public static void MoveMouseRelative(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return;
            lock (Gate)
            {
                INPUT[] inputs = new INPUT[1];
                inputs[0].type = INPUT_MOUSE;
                inputs[0].U.mi.dx = dx;
                inputs[0].U.mi.dy = dy;
                inputs[0].U.mi.dwFlags = MOUSEEVENTF_MOVE;
                inputs[0].U.mi.dwExtraInfo = (IntPtr)InputMagic;
                SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
            }
        }

        public static void SetMousePosition(int x, int y)
        {
            try { SetCursorPos(x, y); } catch { }
        }

        // 无绑定视角按下/抬起时兜底清理可能残留的物理或串流合成鼠标左键等按键，
        // 避免上一会话未抬起的 down 在游戏侧表现为长按普攻/技能。只抬鼠标，不碰键盘。
        public static void ReleaseMouseButtons()
        {
            lock (Gate)
            {
                for (int i = 0; i < 5; i++) SendMouseButton(i, true, "残留清理");
            }
        }

        private static void SendMouseWheel(int direction)
        {
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].U.mi.dwFlags = MOUSEEVENTF_WHEEL;
            inputs[0].U.mi.mouseData = (uint)(120 * (direction > 0 ? 1 : -1));
            inputs[0].U.mi.dwExtraInfo = (IntPtr)InputMagic;
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
