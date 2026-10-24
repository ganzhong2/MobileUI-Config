using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MobileUiConfig
{
    // 一段节奏：左马达%、右马达%、时长毫秒；(0,0,ms) 为停顿。
    internal struct RumbleSegment
    {
        public int Left;
        public int Right;
        public int Ms;

        public RumbleSegment(int left, int right, int ms)
        {
            Left = left;
            Right = right;
            Ms = ms;
        }
    }

    // 全局 XInput 手柄震动服务：PC 调 XInputSetState → Sunshine ViGEm 虚拟手柄 →
    // 串流控制通道 → Moonlight 协同振动 → 手机机身震动。与游戏原生震动同一条链路，
    // 指令式触发，不受游戏音频低频杂音影响。所有震动结束自动归零，退出兜底归零。
    internal static class RumbleService
    {
        private const uint ERROR_SUCCESS = 0;

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

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_VIBRATION
        {
            public ushort wLeftMotorSpeed;
            public ushort wRightMotorSpeed;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint GetStateDelegate(uint dwUserIndex, out XINPUT_STATE pState);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint SetStateDelegate(uint dwUserIndex, ref XINPUT_VIBRATION pVibration);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll")]
        private static extern bool FreeLibrary(IntPtr hModule);

        private static readonly object _gate = new object();
        private static IntPtr _libHandle = IntPtr.Zero;
        private static GetStateDelegate _getState;
        private static SetStateDelegate _setState;
        private static List<int> _slots = new List<int>();
        private static bool _initialized;
        private static bool _holdActive;
        private static int _holdLeft;
        private static int _holdRight;

        // 链路状态快照：面板定时刷新读取，不阻塞触屏线程。
        public static bool SunshineRunning { get; private set; }
        public static bool ClientConnected { get; private set; }
        public static int ConnectedPort { get; private set; }
        public static string Problem { get; private set; }

        static RumbleService()
        {
            Problem = "";
            ConnectedPort = 47989;
            try
            {
                Application.ApplicationExit += delegate { Stop(); };
            }
            catch { }
        }

        public static List<int> ActiveSlots
        {
            get { lock (_gate) { return new List<int>(_slots); } }
        }

        public static bool HoldActive
        {
            get { lock (_gate) { return _holdActive; } }
        }

        private static bool EnsureLoaded()
        {
            if (_initialized) return _getState != null && _setState != null;
            _initialized = true;
            string[] names = new string[] { "XInput1_4.dll", "XInput1_3.dll", "xinput9_1_0.dll" };
            foreach (string name in names)
            {
                IntPtr h = LoadLibrary(name);
                if (h == IntPtr.Zero) continue;
                IntPtr pGet = GetProcAddress(h, "XInputGetState");
                IntPtr pSet = GetProcAddress(h, "XInputSetState");
                if (pGet == IntPtr.Zero || pSet == IntPtr.Zero)
                {
                    FreeLibrary(h);
                    continue;
                }
                _libHandle = h;
                _getState = (GetStateDelegate)Marshal.GetDelegateForFunctionPointer(pGet, typeof(GetStateDelegate));
                _setState = (SetStateDelegate)Marshal.GetDelegateForFunctionPointer(pSet, typeof(SetStateDelegate));
                break;
            }
            if (_getState == null)
            {
                ConfigStore.Log("[震动] XInput DLL 加载失败（XInput1_4 / 1_3 / 9_1_0 均不存在）");
            }
            return _getState != null && _setState != null;
        }

        // 串流重连后槽位可能变化，调用本方法重新探测 0～3 号槽位。
        public static void RefreshSlots()
        {
            lock (_gate)
            {
                _slots.Clear();
                if (!EnsureLoaded()) return;
                for (int i = 0; i < 4; i++)
                {
                    XINPUT_STATE state;
                    if (_getState((uint)i, out state) == ERROR_SUCCESS) _slots.Add(i);
                }
            }
        }

        private static ushort PctToU16(int pct)
        {
            if (pct < 0) pct = 0;
            if (pct > 100) pct = 100;
            return (ushort)Math.Round(pct * 65535.0 / 100.0);
        }

        private static void SetCore(int leftPct, int rightPct)
        {
            if (!EnsureLoaded()) return;
            XINPUT_VIBRATION vib;
            vib.wLeftMotorSpeed = PctToU16(leftPct);
            vib.wRightMotorSpeed = PctToU16(rightPct);
            foreach (int slot in _slots)
            {
                try { _setState((uint)slot, ref vib); }
                catch { }
            }
        }

        // 设置马达强度（百分比 0～100），非阻塞，一直保持到下次调用。
        public static void Set(int leftPct, int rightPct)
        {
            lock (_gate)
            {
                SetCore(leftPct, rightPct);
            }
        }

        // 立即停止所有马达；持续震动状态一并解除。
        public static void Stop()
        {
            lock (_gate)
            {
                _holdActive = false;
                SetCore(0, 0);
            }
        }

        // 震 ms 毫秒后自动停止（后台线程，不阻塞触屏触发线程）。
        public static void Pulse(int ms, int leftPct, int rightPct)
        {
            if (!EnsureLoaded()) return;
            int clampedMs = Math.Max(10, Math.Min(3000, ms));
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                lock (_gate) { SetCore(leftPct, rightPct); }
                Thread.Sleep(clampedMs);
                lock (_gate)
                {
                    if (_holdActive) SetCore(_holdLeft, _holdRight);
                    else SetCore(0, 0);
                }
            });
        }

        // 按节奏段播放（后台线程），(0,0,ms) 为停顿；结束后自动归零。
        public static void Pattern(List<RumbleSegment> segments)
        {
            if (!EnsureLoaded() || segments == null || segments.Count == 0) return;
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                foreach (RumbleSegment seg in segments)
                {
                    lock (_gate) { SetCore(seg.Left, seg.Right); }
                    Thread.Sleep(Math.Max(10, seg.Ms));
                }
                lock (_gate)
                {
                    if (_holdActive) SetCore(_holdLeft, _holdRight);
                    else SetCore(0, 0);
                }
            });
        }

        // 持续震动（面板验证用）：开始后一直保持，直到 HoldStop 或进程退出。
        public static void HoldStart(int strength)
        {
            if (!EnsureLoaded()) return;
            lock (_gate)
            {
                _holdActive = true;
                _holdLeft = strength;
                _holdRight = strength;
                SetCore(strength, strength);
            }
        }

        public static void HoldStop()
        {
            lock (_gate)
            {
                _holdActive = false;
                SetCore(0, 0);
            }
        }

        // 内置预设节奏段：1=心跳，2=提醒·三短，3=冲击·渐弱，4=连发。
        public static List<RumbleSegment> PresetSegments(int preset)
        {
            List<RumbleSegment> list = new List<RumbleSegment>();
            if (preset == 1)
            {
                list.Add(new RumbleSegment(100, 100, 120));
                list.Add(new RumbleSegment(0, 0, 90));
                list.Add(new RumbleSegment(70, 70, 180));
            }
            else if (preset == 2)
            {
                for (int i = 0; i < 3; i++)
                {
                    list.Add(new RumbleSegment(90, 90, 100));
                    list.Add(new RumbleSegment(0, 0, 80));
                }
            }
            else if (preset == 3)
            {
                list.Add(new RumbleSegment(100, 40, 150));
                list.Add(new RumbleSegment(60, 25, 150));
                list.Add(new RumbleSegment(30, 10, 200));
            }
            else if (preset == 4)
            {
                for (int i = 0; i < 5; i++)
                {
                    list.Add(new RumbleSegment(100, 60, 45));
                    list.Add(new RumbleSegment(0, 0, 55));
                }
            }
            return list;
        }

        // 解析自定义节奏：「左:右:毫秒,左:右:毫秒,...」；两段写法「强度:毫秒」= 左右同强度；
        // 0:0:毫秒 为停顿。解析失败返回 null。
        public static List<RumbleSegment> ParsePattern(string text)
        {
            List<RumbleSegment> list = new List<RumbleSegment>();
            if (string.IsNullOrEmpty(text)) return null;
            string[] parts = text.Split(',');
            foreach (string raw in parts)
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                string[] fields = part.Split(':');
                int l;
                int r;
                int ms;
                if (fields.Length == 2)
                {
                    int strength;
                    int ms2;
                    if (!int.TryParse(fields[0].Trim(), out strength)) return null;
                    if (!int.TryParse(fields[1].Trim(), out ms2)) return null;
                    l = strength;
                    r = strength;
                    ms = ms2;
                }
                else if (fields.Length == 3)
                {
                    int lv;
                    int rv;
                    int mv;
                    if (!int.TryParse(fields[0].Trim(), out lv)) return null;
                    if (!int.TryParse(fields[1].Trim(), out rv)) return null;
                    if (!int.TryParse(fields[2].Trim(), out mv)) return null;
                    l = lv;
                    r = rv;
                    ms = mv;
                }
                else
                {
                    return null;
                }
                if (ms < 10) ms = 10;
                if (ms > 2000) ms = 2000;
                list.Add(new RumbleSegment(l, r, ms));
            }
            return list.Count == 0 ? null : list;
        }

        // 真机按键统一入口：按全局配置分发单次 / 预设 / 自定义；开关关闭或无通道时静默返回。
        public static void PulseFrom(RumbleConfig cfg)
        {
            if (cfg == null || !cfg.Enabled) return;
            if (!EnsureLoaded()) return;
            cfg.Normalize();
            if (cfg.Preset == 0)
            {
                Pulse(cfg.DurationMs, cfg.LeftMotor, cfg.RightMotor);
                return;
            }
            if (cfg.Preset == 5)
            {
                List<RumbleSegment> segs = ParsePattern(cfg.Pattern);
                if (segs == null)
                {
                    Pulse(cfg.DurationMs, cfg.LeftMotor, cfg.RightMotor);
                }
                else
                {
                    Pattern(segs);
                }
                return;
            }
            Pattern(PresetSegments(cfg.Preset));
        }

        // 刷新链路状态：Sunshine 进程、串流客户端连接、XInput 槽位与问题提示。
        public static void RefreshStatus()
        {
            SunshineRunning = ProcessRunning("sunshine.exe") || ProcessRunning("sunshinesvc.exe");
            ConnectedPort = ReadSunshinePort();
            ClientConnected = IsClientConnected(ConnectedPort);
            RefreshSlots();
            List<string> problems = new List<string>();
            if (!SunshineRunning) problems.Add("Sunshine 未运行");
            if (SunshineRunning && !ClientConnected)
            {
                problems.Add("未检测到串流客户端连接");
            }
            if (_slots.Count == 0)
            {
                if (ClientConnected)
                {
                    problems.Add("串流已连接但无 XInput 通道：Sunshine 虚拟手柄类型需选 Xbox 360");
                }
                else
                {
                    problems.Add("无 XInput 通道（客户端连接并开启虚拟手柄后出现）");
                }
            }
            Problem = string.Join("；", problems.ToArray());
        }

        private static bool ProcessRunning(string imageName)
        {
            try
            {
                string procName = Path.GetFileNameWithoutExtension(imageName);
                Process[] procs = Process.GetProcessesByName(procName);
                if (procs == null || procs.Length == 0) return false;
                foreach (Process p in procs) { p.Dispose(); }
                return true;
            }
            catch { return false; }
        }

        private static int ReadSunshinePort()
        {
            try
            {
                string programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
                if (string.IsNullOrEmpty(programFiles)) programFiles = @"C:\Program Files";
                string conf = Path.Combine(programFiles, "Sunshine", "config", "sunshine.conf");
                if (!File.Exists(conf)) return 47989;
                string[] lines = File.ReadAllLines(conf);
                foreach (string line in lines)
                {
                    string t = line.Trim();
                    if (t.StartsWith("port") && t.IndexOf('=') >= 0)
                    {
                        string v = t.Substring(t.IndexOf('=') + 1).Trim();
                        int port;
                        if (int.TryParse(v, out port) && port > 0) return port;
                    }
                }
            }
            catch { }
            return 47989;
        }

        private static bool IsClientConnected(int port)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "netstat.exe";
                psi.Arguments = "-ano -p tcp";
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                string[] lines = output.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;
                    if (parts[0].ToUpperInvariant() != "TCP") continue;
                    if (parts[3].ToUpperInvariant() != "ESTABLISHED") continue;
                    string local = parts[1];
                    int colon = local.LastIndexOf(':');
                    if (colon < 0) continue;
                    int localPort;
                    if (int.TryParse(local.Substring(colon + 1), out localPort) && localPort == port) return true;
                }
            }
            catch { }
            return false;
        }
    }
}
