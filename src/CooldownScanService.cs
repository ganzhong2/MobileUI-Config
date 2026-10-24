using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MobileUiConfig
{
    internal sealed class MemoryCooldownSnapshot
    {
        public bool Running;
        public bool GameFound;
        public string Status = "";
        public readonly Dictionary<string, double> Values = new Dictionary<string, double>();
        public readonly HashSet<string> Valid = new HashSet<string>();

        public MemoryCooldownSnapshot Clone()
        {
            MemoryCooldownSnapshot copy = new MemoryCooldownSnapshot();
            copy.Running = Running;
            copy.GameFound = GameFound;
            copy.Status = Status;
            foreach (KeyValuePair<string, double> pair in Values) copy.Values[pair.Key] = pair.Value;
            foreach (string id in Valid) copy.Valid.Add(id);
            return copy;
        }
    }

    // Reads a base+offset pointer chain for each configured virtual key. Each
    // key has its own 50-1000 ms polling interval.
    internal sealed class CooldownScanService : IDisposable
    {
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        private readonly AppConfig _config;
        private readonly Func<AppConfig> _rootProvider;
        private readonly Action<MemoryCooldownSnapshot> _updated;
        private readonly System.Threading.Timer _timer;
        private readonly object _gate = new object();
        private readonly Dictionary<string, double> _values = new Dictionary<string, double>();
        private readonly HashSet<string> _valid = new HashSet<string>();
        private readonly Dictionary<string, int> _next = new Dictionary<string, int>();
        private Process _process;
        private IntPtr _handle;
        private volatile bool _paused;
        private int _busy;
        private bool _errorLogged;

        public CooldownScanService(AppConfig config, Func<AppConfig> rootProvider, Action<MemoryCooldownSnapshot> updated)
        {
            _config = config;
            _rootProvider = rootProvider;
            _updated = updated;
            _timer = new System.Threading.Timer(Tick, null, 50, 50);
        }

        public void Apply()
        {
            lock (_gate) { _next.Clear(); }
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (!paused) return;
            lock (_gate)
            {
                CloseProcess();
                _values.Clear();
                _valid.Clear();
                _next.Clear();
            }
        }

        private void Tick(object state)
        {
            if (_paused) return;
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            try
            {
                MemoryCooldownSnapshot snap = new MemoryCooldownSnapshot();
                snap.Running = true;
                List<VirtualKey> keys = CollectKeys();
                if (keys.Count == 0)
                {
                    CloseProcess();
                    lock (_gate) { _values.Clear(); _valid.Clear(); _next.Clear(); }
                    snap.Status = "请选择虚拟按键并开启冷却扫描";
                    Emit(snap);
                    return;
                }
                EnsureProcess();
                if (_process == null)
                {
                    CloseProcess();
                    lock (_gate) { _values.Clear(); _valid.Clear(); _next.Clear(); }
                    snap.Status = "等待游戏启动";
                    Emit(snap);
                    return;
                }
                snap.GameFound = true;
                if (_handle == IntPtr.Zero)
                {
                    snap.Status = "无法读取游戏内存，请用管理员运行";
                    Emit(snap);
                    return;
                }
                int pointerSize = PointerSize();
                int now = Environment.TickCount;
                lock (_gate)
                {
                    for (int i = 0; i < keys.Count; i++)
                    {
                        VirtualKey key = keys[i];
                        if (key == null) continue;
                        int due;
                        if (_next.TryGetValue(key.Id, out due) && now - due < 0) continue;
                        int interval = Math.Max(50, Math.Min(1000, key.CooldownIntervalMs));
                        _next[key.Id] = now + interval;
                        double seconds;
                        if (TryReadKey(key, pointerSize, out seconds))
                        {
                            _values[key.Id] = seconds;
                            _valid.Add(key.Id);
                        }
                        else
                        {
                            _values.Remove(key.Id);
                            _valid.Remove(key.Id);
                        }
                    }
                    foreach (KeyValuePair<string, double> pair in _values) snap.Values[pair.Key] = pair.Value;
                    foreach (string id in _valid) snap.Valid.Add(id);
                }
                snap.Status = snap.Valid.Count == 0 ? "已找到游戏，基址/偏移未命中" : "实时读取 " + snap.Valid.Count + " 个技能";
                Emit(snap);
            }
            catch (Exception ex)
            {
                if (!_errorLogged)
                {
                    _errorLogged = true;
                    ConfigStore.Log("冷却扫描异常: " + ex.Message);
                }
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }

        private List<VirtualKey> CollectKeys()
        {
            List<VirtualKey> list = new List<VirtualKey>();
            HashSet<string> ids = new HashSet<string>();
            AppConfig root = _rootProvider == null ? _config : _rootProvider();
            if (root == null || root.Modes == null) return list;
            for (int m = 0; m < root.Modes.Count; m++)
            {
                ModeConfig mode = root.Modes[m];
                if (mode == null || mode.Keys == null) continue;
                for (int i = 0; i < mode.Keys.Count; i++)
                {
                    VirtualKey key = mode.Keys[i];
                    if (key == null) continue;
                    if (!key.CooldownScanEnabled || string.IsNullOrEmpty(key.CooldownBaseAddress)) continue;
                    if (!ids.Add(key.Id)) continue;
                    list.Add(key);
                }
            }
            return list;
        }

        private void Emit(MemoryCooldownSnapshot snap)
        {
            Action<MemoryCooldownSnapshot> h = _updated;
            if (h != null) h(snap.Clone());
        }

        private void EnsureProcess()
        {
            if (_process != null)
            {
                try
                {
                    if (!_process.HasExited) return;
                }
                catch { }
                CloseProcess();
                return;
            }
            List<string> names = new List<string>();
            try
            {
                string stem = Path.GetFileNameWithoutExtension((_config.GamePath ?? "").Trim().Trim('"'));
                if (!string.IsNullOrEmpty(stem)) names.Add(stem);
            }
            catch { }
            string configured = _config.GameProcessName ?? "";
            string[] parts = configured.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++) if (!names.Contains(parts[i])) names.Add(parts[i]);
            for (int i = 0; i < names.Count; i++)
            {
                Process[] candidates = null;
                try { candidates = Process.GetProcessesByName(names[i]); } catch { }
                if (candidates == null) continue;
                Process best = null;
                for (int j = 0; j < candidates.Length; j++)
                {
                    try
                    {
                        if (candidates[j].MainWindowHandle != IntPtr.Zero || candidates.Length == 1)
                        {
                            best = candidates[j];
                            break;
                        }
                    }
                    catch { }
                }
                if (best == null && candidates.Length > 0) best = candidates[0];
                for (int j = 0; j < candidates.Length; j++)
                    if (!ReferenceEquals(candidates[j], best)) { try { candidates[j].Dispose(); } catch { } }
                if (best != null)
                {
                    _process = best;
                    _handle = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, (uint)_process.Id);
                    if (_handle == IntPtr.Zero) ConfigStore.Log("冷却扫描无法打开进程: " + Marshal.GetLastWin32Error());
                    return;
                }
            }
        }

        private bool TryReadKey(VirtualKey key, int pointerSize, out double seconds)
        {
            seconds = 0;
            long baseAddress;
            if (!TryResolveBase(key.CooldownBaseAddress, out baseAddress)) return false;
            List<long> offsets = ParseOffsets(key.CooldownOffsets);
            int maxSeconds = key.CooldownMaxSeconds < 1 ? 60 : key.CooldownMaxSeconds;

            long address = baseAddress;
            bool ok = true;
            for (int i = 0; i < offsets.Count; i++)
            {
                long pointer;
                if (!ReadPointer(address, pointerSize, out pointer)) { ok = false; break; }
                address = pointer + offsets[i];
            }
            if (ok && TryValue(address, maxSeconds, out seconds)) return true;

            long direct = baseAddress;
            for (int i = 0; i < offsets.Count; i++) direct += offsets[i];
            return TryValue(direct, maxSeconds, out seconds);
        }

        private bool TryValue(long address, int maxSeconds, out double seconds)
        {
            seconds = 0;
            float f;
            int i;
            bool hasFloat = ReadSingle(address, out f);
            bool hasInt = ReadInt32(address, out i);
            if (hasFloat && !float.IsNaN(f) && !float.IsInfinity(f) && f >= 0 && f <= maxSeconds + 5)
            {
                seconds = f;
                return true;
            }
            if (hasInt && i >= 0 && i <= maxSeconds * 1000 + 5000)
            {
                seconds = i / 1000.0;
                return true;
            }
            if (hasInt && i >= 0 && i <= maxSeconds + 5)
            {
                seconds = i;
                return true;
            }
            return false;
        }

        private bool TryResolveBase(string text, out long address)
        {
            address = 0;
            text = (text == null ? "" : text).Trim().Replace(" ", "");
            if (text.Length == 0) return TryGetModuleBase("", out address);
            int plus = text.IndexOf('+');
            if (plus >= 0)
            {
                string module = text.Substring(0, plus);
                string off = text.Substring(plus + 1);
                long offset;
                if (!TryParseHex(off, out offset)) return false;
                long absoluteBase;
                if (TryParseHex(module, out absoluteBase))
                {
                    address = absoluteBase + offset;
                    return true;
                }
                long moduleBase;
                if (!TryGetModuleBase(module, out moduleBase)) return false;
                address = moduleBase + offset;
                return true;
            }
            long absolute;
            if (TryParseHex(text, out absolute))
            {
                address = absolute;
                return true;
            }
            return TryGetModuleBase(text, out address);
        }

        private bool TryGetModuleBase(string name, out long address)
        {
            address = 0;
            if (_process == null) return false;
            try
            {
                if (string.IsNullOrEmpty(name))
                {
                    address = _process.MainModule.BaseAddress.ToInt64();
                    return address != 0;
                }
                foreach (ProcessModule module in _process.Modules)
                {
                    if (string.Equals(module.ModuleName, name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(Path.GetFileName(module.FileName), name, StringComparison.OrdinalIgnoreCase))
                    {
                        address = module.BaseAddress.ToInt64();
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private List<long> ParseOffsets(string text)
        {
            List<long> list = new List<long>();
            if (string.IsNullOrEmpty(text)) return list;
            string[] parts = text.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                long value;
                if (TryParseHex(parts[i], out value)) list.Add(value);
            }
            return list;
        }

        private bool TryParseHex(string text, out long value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
            try { return long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value); }
            catch { return false; }
        }

        private int PointerSize()
        {
            if (_process == null) return IntPtr.Size;
            bool wow;
            try
            {
                if (IsWow64Process(_process.Handle, out wow)) return wow ? 4 : IntPtr.Size;
            }
            catch { }
            return IntPtr.Size;
        }

        private bool ReadPointer(long address, int size, out long value)
        {
            value = 0;
            byte[] buffer = new byte[size];
            IntPtr read;
            if (!ReadProcessMemory(_handle, new IntPtr(address), buffer, (IntPtr)size, out read)) return false;
            if (read.ToInt64() != size) return false;
            value = size == 4 ? BitConverter.ToUInt32(buffer, 0) : BitConverter.ToInt64(buffer, 0);
            return true;
        }

        private bool ReadInt32(long address, out int value)
        {
            value = 0;
            byte[] buffer = new byte[4];
            IntPtr read;
            if (!ReadProcessMemory(_handle, new IntPtr(address), buffer, (IntPtr)4, out read)) return false;
            if (read.ToInt64() != 4) return false;
            value = BitConverter.ToInt32(buffer, 0);
            return true;
        }

        private bool ReadSingle(long address, out float value)
        {
            value = 0;
            byte[] buffer = new byte[4];
            IntPtr read;
            if (!ReadProcessMemory(_handle, new IntPtr(address), buffer, (IntPtr)4, out read)) return false;
            if (read.ToInt64() != 4) return false;
            value = BitConverter.ToSingle(buffer, 0);
            return true;
        }

        private void CloseProcess()
        {
            if (_handle != IntPtr.Zero)
            {
                try { CloseHandle(_handle); } catch { }
                _handle = IntPtr.Zero;
            }
            if (_process != null)
            {
                try { _process.Dispose(); } catch { }
                _process = null;
            }
        }

        public void Dispose()
        {
            try { _timer.Dispose(); } catch { }
            lock (_gate) { CloseProcess(); }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer,
            IntPtr size, out IntPtr read);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")]
        private static extern bool IsWow64Process(IntPtr process, out bool wow64);
    }
}
