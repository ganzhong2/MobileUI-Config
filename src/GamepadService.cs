using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MobileUiConfig
{
    // ViGEm 虚拟手柄服务：动态加载同目录 ViGEmClient.dll，按所选通道创建一个虚拟手柄目标，
    // 维护按钮位 / 扳机 / 双摇杆轴状态，仅在状态变化时上报。
    //   通道 0 ViGEmBus：Xbox360 目标（XInput 路径）。
    //   通道 1 HID：DualShock4 目标（标准 HID 游戏设备路径，仍由 ViGEmBus 总线接管）。
    // DLL 缺失、导出缺失或 ViGEmBus 未安装时静默降级（手柄注入不动作，不影响键盘鼠标旧链路）。
    // 线程安全，退出兜底释放。
    internal static class GamepadService
    {
        // 通道常量。
        public const int DriverVigemBus = 0;
        public const int DriverHid = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        // ViGEm DS4_REPORT 基础报告结构（与 vigemclient.h 布局一致）。
        [StructLayout(LayoutKind.Sequential)]
        private struct DS4_REPORT
        {
            public byte bThumbLX;
            public byte bThumbLY;
            public byte bThumbRX;
            public byte bThumbRY;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public ushort wButtons;
            public byte bSpecial;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] bAnalogButtons;
            public ushort bTimestamp;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] bSensor1;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] bSensor2;
        }

        private const uint VIGEM_ERROR_NONE = 0;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr DelegateAlloc();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint DelegateConnect(IntPtr client);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr DelegateTargetX360Alloc();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr DelegateTargetDs4Alloc();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint DelegateTargetAdd(IntPtr client, IntPtr target);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DelegateTargetSetX360(IntPtr target, ref XINPUT_GAMEPAD state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DelegateTargetSetDs4(IntPtr target, ref DS4_REPORT state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DelegateTargetUnplug(IntPtr target);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DelegateTargetFree(IntPtr target);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DelegateDisconnect(IntPtr client);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DelegateFree(IntPtr client);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
        [DllImport("kernel32.dll")]
        private static extern bool FreeLibrary(IntPtr hModule);

        private static readonly object _gate = new object();
        private static bool _tried;
        private static bool _available;
        private static bool _started;
        private static int _driver = DriverVigemBus;

        private static IntPtr _client = IntPtr.Zero;
        private static IntPtr _target = IntPtr.Zero;
        private static IntPtr _lib = IntPtr.Zero;

        private static DelegateAlloc _alloc;
        private static DelegateConnect _connect;
        private static DelegateTargetX360Alloc _x360Alloc;
        private static DelegateTargetDs4Alloc _ds4Alloc;
        private static DelegateTargetAdd _targetAdd;
        private static DelegateTargetSetX360 _x360SetState;
        private static DelegateTargetSetDs4 _ds4SetState;
        private static DelegateTargetUnplug _x360Unplug;
        private static DelegateTargetUnplug _ds4Unplug;
        private static DelegateTargetFree _targetFree;
        private static DelegateDisconnect _disconnect;
        private static DelegateFree _free;

        private static XINPUT_GAMEPAD _state;
        private static XINPUT_GAMEPAD _last;
        private static bool _x360Sent;
        private static DS4_REPORT _ds4;
        private static DS4_REPORT _lastDs4;
        private static bool _ds4Sent;

        public static bool Available
        {
            get { lock (_gate) { return _available; } }
        }

        public static int Driver
        {
            get { lock (_gate) { return _driver; } }
        }

        public static string StatusText { get; private set; }

        static GamepadService()
        {
            StatusText = "";
            try
            {
                Application.ApplicationExit += delegate { Shutdown(); };
            }
            catch { }
        }

        private static IntPtr Resolve(IntPtr lib, string name)
        {
            if (lib == IntPtr.Zero) return IntPtr.Zero;
            return GetProcAddress(lib, name);
        }

        // 标准版 BaseDirectory 即 exe 目录；独立版单文件可能解压到临时目录，按多候选目录回退查找同目录 DLL。
        private static string ResolveDllPath()
        {
            List<string> candidates = new List<string>();
            try { candidates.Add(AppDomain.CurrentDomain.BaseDirectory); } catch { }
            try
            {
                string exeDir = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                if (!string.IsNullOrEmpty(exeDir)) candidates.Add(exeDir);
            }
            catch { }
            try { candidates.Add(Directory.GetCurrentDirectory()); } catch { }
            foreach (string dir in candidates)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string p = Path.Combine(dir, "ViGEmClient.dll");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        // 切换虚拟手柄通道；已就绪则释放旧目标按新通道重建，未就绪仅记录所选通道。
        public static void SetDriver(int type)
        {
            lock (_gate)
            {
                if (type != DriverHid) type = DriverVigemBus;
                if (type == _driver) return;
                _driver = type;
                if (_started)
                {
                    Shutdown();
                    EnsureStarted();
                }
            }
        }

        private static bool EnsureStarted()
        {
            if (_started) return _available;
            if (_tried) return false;
            _tried = true;

            string path = ResolveDllPath();
            if (string.IsNullOrEmpty(path))
            {
                StatusText = "未找到 ViGEmClient.dll，手柄注入不可用";
                ConfigStore.Log("[手柄] " + StatusText);
                return false;
            }
            IntPtr lib = LoadLibrary(path);
            if (lib == IntPtr.Zero)
            {
                StatusText = "未找到 ViGEmClient.dll，手柄注入不可用";
                ConfigStore.Log("[手柄] " + StatusText);
                return false;
            }

            _alloc = Marshal.GetDelegateForFunctionPointer(Resolve(lib, "vigem_alloc"), typeof(DelegateAlloc)) as DelegateAlloc;
            _connect = Marshal.GetDelegateForFunctionPointer(Resolve(lib, "vigem_connect"), typeof(DelegateConnect)) as DelegateConnect;
            _targetAdd = Marshal.GetDelegateForFunctionPointer(Resolve(lib, "vigem_target_add"), typeof(DelegateTargetAdd)) as DelegateTargetAdd;
            _targetFree = Marshal.GetDelegateForFunctionPointer(Resolve(lib, "vigem_target_free"), typeof(DelegateTargetFree)) as DelegateTargetFree;
            _disconnect = Marshal.GetDelegateForFunctionPointer(Resolve(lib, "vigem_disconnect"), typeof(DelegateDisconnect)) as DelegateDisconnect;
            _free = Marshal.GetDelegateForFunctionPointer(Resolve(lib, "vigem_free"), typeof(DelegateFree)) as DelegateFree;

            // 通道 0：Xbox360 / XInput。
            _x360Alloc = Marshal.GetDelegateForFunctionPointer(Resolve(lib, "vigem_target_x360_alloc"), typeof(DelegateTargetX360Alloc)) as DelegateTargetX360Alloc;
            _x360SetState = Marshal.GetDelegateForFunctionPointer(Resolve(lib, "vigem_target_x360_set_state"), typeof(DelegateTargetSetX360)) as DelegateTargetSetX360;
            _x360Unplug = Marshal.GetDelegateForFunctionPointer(Resolve(lib, "vigem_target_x360_unplug"), typeof(DelegateTargetUnplug)) as DelegateTargetUnplug;

            // 通道 1：DualShock4 / HID。
            _ds4Alloc = Marshal.GetDelegateForFunctionPointer(Resolve(lib, "vigem_target_ds4_alloc"), typeof(DelegateTargetDs4Alloc)) as DelegateTargetDs4Alloc;
            _ds4SetState = Marshal.GetDelegateForFunctionPointer(Resolve(lib, "vigem_target_ds4_set_state"), typeof(DelegateTargetSetDs4)) as DelegateTargetSetDs4;
            _ds4Unplug = Marshal.GetDelegateForFunctionPointer(Resolve(lib, "vigem_target_ds4_unplug"), typeof(DelegateTargetUnplug)) as DelegateTargetUnplug;

            if (_alloc == null || _connect == null || _targetAdd == null ||
                _targetFree == null || _disconnect == null || _free == null)
            {
                StatusText = "ViGEmClient.dll 导出函数缺失，手柄注入不可用";
                ConfigStore.Log("[手柄] " + StatusText);
                FreeLibrary(lib);
                _lib = IntPtr.Zero;
                return false;
            }

            // HID 通道缺少 DS4 导出时回退到 Xbox360 通道，保证注入仍可用。
            if (_driver == DriverHid && (_ds4Alloc == null || _ds4SetState == null || _ds4Unplug == null))
            {
                ConfigStore.Log("[手柄] 当前 ViGEmClient.dll 不支持 DS4/HID 目标，已回退 Xbox360 通道。");
                _driver = DriverVigemBus;
            }
            if (_driver == DriverVigemBus && (_x360Alloc == null || _x360SetState == null || _x360Unplug == null))
            {
                StatusText = "ViGEmClient.dll 导出函数缺失，手柄注入不可用";
                ConfigStore.Log("[手柄] " + StatusText);
                FreeLibrary(lib);
                _lib = IntPtr.Zero;
                return false;
            }

            IntPtr client = _alloc();
            if (client == IntPtr.Zero)
            {
                StatusText = "分配 ViGEm 客户端失败，手柄注入不可用";
                ConfigStore.Log("[手柄] " + StatusText);
                FreeLibrary(lib);
                return false;
            }

            uint err = _connect(client);
            if (err != VIGEM_ERROR_NONE)
            {
                StatusText = "连接 ViGEmBus 失败（请确认已安装并启动 ViGEmBus 驱动），错误码 " + err;
                ConfigStore.Log("[手柄] " + StatusText);
                try { _free(client); } catch { }
                FreeLibrary(lib);
                return false;
            }

            IntPtr target = IntPtr.Zero;
            if (_driver == DriverHid)
            {
                target = _ds4Alloc();
            }
            else
            {
                target = _x360Alloc();
            }
            if (target == IntPtr.Zero)
            {
                StatusText = "分配虚拟手柄目标失败，手柄注入不可用";
                ConfigStore.Log("[手柄] " + StatusText);
                try { _disconnect(client); } catch { }
                try { _free(client); } catch { }
                FreeLibrary(lib);
                return false;
            }

            err = _targetAdd(client, target);
            if (err != VIGEM_ERROR_NONE)
            {
                StatusText = "挂载虚拟手柄失败，错误码 " + err;
                ConfigStore.Log("[手柄] " + StatusText);
                try { _targetFree(target); } catch { }
                try { _disconnect(client); } catch { }
                try { _free(client); } catch { }
                FreeLibrary(lib);
                return false;
            }

            _lib = lib;
            _client = client;
            _target = target;
            _state = new XINPUT_GAMEPAD();
            _last = new XINPUT_GAMEPAD();
            _ds4 = NewDs4();
            _lastDs4 = NewDs4();
            _available = true;
            _started = true;
            StatusText = _driver == DriverHid ? "虚拟 HID(DS4) 手柄已就绪" : "虚拟 Xbox360 手柄已就绪";
            ConfigStore.Log("[手柄] " + StatusText);
            return true;
        }

        private static DS4_REPORT NewDs4()
        {
            DS4_REPORT r = new DS4_REPORT();
            r.bAnalogButtons = new byte[8];
            r.bSensor1 = new byte[6];
            r.bSensor2 = new byte[6];
            r.bThumbLX = 128;
            r.bThumbLY = 128;
            r.bThumbRX = 128;
            r.bThumbRY = 128;
            return r;
        }

        private static bool EqualState(ref XINPUT_GAMEPAD a, ref XINPUT_GAMEPAD b)
        {
            return a.wButtons == b.wButtons && a.bLeftTrigger == b.bLeftTrigger && a.bRightTrigger == b.bRightTrigger &&
                   a.sThumbLX == b.sThumbLX && a.sThumbLY == b.sThumbLY &&
                   a.sThumbRX == b.sThumbRX && a.sThumbRY == b.sThumbRY;
        }

        private static bool EqualDs4(ref DS4_REPORT a, ref DS4_REPORT b)
        {
            return a.bThumbLX == b.bThumbLX && a.bThumbLY == b.bThumbLY &&
                   a.bThumbRX == b.bThumbRX && a.bThumbRY == b.bThumbRY &&
                   a.bLeftTrigger == b.bLeftTrigger && a.bRightTrigger == b.bRightTrigger &&
                   a.wButtons == b.wButtons && a.bSpecial == b.bSpecial;
        }

        private static byte MapAxis(short v)
        {
            if (v < -32767) v = -32767;
            if (v > 32767) v = 32767;
            int b = 128 + (int)Math.Round(v / 257.0);
            if (b < 0) b = 0;
            if (b > 255) b = 255;
            return (byte)b;
        }

        // XInput 位码映射到 DS4 动作 / 肩键 / 系统键；十字方向单独计算方向码。
        private static void MapToDs4(XINPUT_GAMEPAD x, out DS4_REPORT r)
        {
            r = NewDs4();
            r.bThumbLX = MapAxis(x.sThumbLX);
            r.bThumbLY = MapAxis(x.sThumbLY);
            r.bThumbRX = MapAxis(x.sThumbRX);
            r.bThumbRY = MapAxis(x.sThumbRY);
            r.bLeftTrigger = x.bLeftTrigger;
            r.bRightTrigger = x.bRightTrigger;

            ushort b = 0;
            ushort w = x.wButtons;
            if ((w & PadButtons.A) != 0) b |= 0x0020;   // Cross
            if ((w & PadButtons.B) != 0) b |= 0x0040;   // Circle
            if ((w & PadButtons.X) != 0) b |= 0x0010;   // Square
            if ((w & PadButtons.Y) != 0) b |= 0x0080;   // Triangle
            if ((w & PadButtons.LeftShoulder) != 0) b |= 0x0100; // L1
            if ((w & PadButtons.RightShoulder) != 0) b |= 0x0200; // R1
            if ((w & PadButtons.LeftThumb) != 0) b |= 0x4000;   // L3
            if ((w & PadButtons.RightThumb) != 0) b |= 0x8000; // R3
            if ((w & PadButtons.Start) != 0) b |= 0x2000;      // Options
            if ((w & PadButtons.Back) != 0) b |= 0x1000;       // Share

            bool up = (w & PadButtons.DPadUp) != 0;
            bool down = (w & PadButtons.DPadDown) != 0;
            bool left = (w & PadButtons.DPadLeft) != 0;
            bool right = (w & PadButtons.DPadRight) != 0;
            byte dpad = 8; // 无方向
            if (up && right) dpad = 1;
            else if (down && right) dpad = 3;
            else if (down && left) dpad = 5;
            else if (up && left) dpad = 7;
            else if (up) dpad = 0;
            else if (right) dpad = 2;
            else if (down) dpad = 4;
            else if (left) dpad = 6;

            r.wButtons = (ushort)(b | dpad);
        }

        private static void Push()
        {
            if (!_available || _target == IntPtr.Zero) return;
            if (_driver == DriverHid)
            {
                MapToDs4(_state, out _ds4);
                if (_ds4Sent && EqualDs4(ref _ds4, ref _lastDs4)) return;
                _ds4SetState(_target, ref _ds4);
                _lastDs4 = _ds4;
                _ds4Sent = true;
            }
            else
            {
                if (_x360Sent && EqualState(ref _state, ref _last)) return;
                _x360SetState(_target, ref _state);
                _last = _state;
                _x360Sent = true;
            }
        }

        private static short ToShort(float v)
        {
            if (v < -1f) v = -1f;
            if (v > 1f) v = 1f;
            return (short)Math.Round(v * 32767.0);
        }

        public static void SetButton(ushort mask, bool down)
        {
            lock (_gate)
            {
                if (!EnsureStarted()) return;
                if (down) _state.wButtons |= mask;
                else _state.wButtons = (ushort)(_state.wButtons & ~mask);
                Push();
            }
        }

        public static void SetTrigger(int modifier, bool down)
        {
            lock (_gate)
            {
                if (!EnsureStarted()) return;
                byte v = down ? (byte)255 : (byte)0;
                if (modifier == PadButtons.ModifierLeftTrigger) _state.bLeftTrigger = v;
                else if (modifier == PadButtons.ModifierRightTrigger) _state.bRightTrigger = v;
                Push();
            }
        }

        public static void SetLeftStick(float x, float y)
        {
            lock (_gate)
            {
                if (!EnsureStarted()) return;
                _state.sThumbLX = ToShort(x);
                // 屏幕坐标系向下为正，手柄轴向上为正，Y 取反。
                _state.sThumbLY = (short)(-ToShort(y));
                Push();
            }
        }

        public static void SetRightStick(float x, float y)
        {
            lock (_gate)
            {
                if (!EnsureStarted()) return;
                _state.sThumbRX = ToShort(x);
                _state.sThumbRY = (short)(-ToShort(y));
                Push();
            }
        }

        public static void ReleaseAll()
        {
            lock (_gate)
            {
                if (!_available) return;
                _state = new XINPUT_GAMEPAD();
                Push();
            }
        }

        public static void Shutdown()
        {
            lock (_gate)
            {
                if (_target != IntPtr.Zero)
                {
                    try
                    {
                        if (_driver == DriverHid && _ds4Unplug != null) _ds4Unplug(_target);
                        else if (_x360Unplug != null) _x360Unplug(_target);
                    }
                    catch { }
                    try { _targetFree(_target); } catch { }
                    _target = IntPtr.Zero;
                }
                if (_client != IntPtr.Zero)
                {
                    try { _disconnect(_client); } catch { }
                    try { _free(_client); } catch { }
                    _client = IntPtr.Zero;
                }
                if (_lib != IntPtr.Zero)
                {
                    FreeLibrary(_lib);
                    _lib = IntPtr.Zero;
                }
                _available = false;
                _started = false;
                _tried = false;
                _x360Sent = false;
                _ds4Sent = false;
                _x360Alloc = null;
                _x360SetState = null;
                _x360Unplug = null;
                _ds4Alloc = null;
                _ds4SetState = null;
                _ds4Unplug = null;
            }
        }
    }
}
