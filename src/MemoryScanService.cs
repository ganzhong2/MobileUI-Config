using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MobileUiConfig
{
    // Polls for the configured game process, so it can be enabled before the game starts.
    // It does not depend on the overlay or on the game having been launched by this app.
    internal sealed class MemoryScanService : IDisposable
    {
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;

        private readonly AppConfig _config;
        private readonly Func<AppConfig> _rootProvider;
        private readonly Action<int, string> _switchMode;
        private readonly Action _gameEnded;
        private readonly Action<string> _notify;
        private readonly System.Threading.Timer _timer;
        private readonly object _gate = new object();
        private Process _process;
        private IntPtr _handle;
        private bool _sawProcess;
        private volatile bool _paused;
        private bool _endedNotified;
        private bool _openErrorLogged;
        private int _busy;
        private int _candidateMode = -1;
        private int _candidateCount;
        private DateTime _lastSwitch = DateTime.MinValue;

        public MemoryScanService(AppConfig config, Func<AppConfig> rootProvider, Action<int, string> switchMode, Action gameEnded,
            Action<string> notify)
        {
            _config = config;
            _rootProvider = rootProvider;
            _switchMode = switchMode;
            _gameEnded = gameEnded;
            _notify = notify;
            _timer = new System.Threading.Timer(Tick, null, 700, 700);
        }

        public void Apply()
        {
            // The timer stays alive; it waits for the game process when enabled.
            lock (_gate)
            {
                _candidateMode = -1;
                _candidateCount = 0;
            }
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (!paused) return;
            lock (_gate)
            {
                CloseProcess();
                _candidateMode = -1;
                _candidateCount = 0;
            }
        }

        private void Tick(object state)
        {
            if (_paused) return;
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
            try
            {
                AppConfig root = _rootProvider == null ? _config : _rootProvider();
                // 任一内存扫描项目启用且配置了基址，才需要附加到游戏进程。
                bool anyEnabled = false;
                if (root != null && root.MemoryProjects != null)
                {
                    for (int i = 0; i < root.MemoryProjects.Count; i++)
                    {
                        AutoMemoryProject p = root.MemoryProjects[i];
                        if (p != null && p.Enabled && p.Memory != null &&
                            !string.IsNullOrEmpty(p.Memory.BaseAddress))
                        {
                            anyEnabled = true;
                            break;
                        }
                    }
                }
                if (root == null || root.MemoryScan == null || !anyEnabled)
                {
                    CloseProcess();
                    return;
                }
                EnsureProcess();
                if (_process == null) return;
                Scan(root);
            }
            catch (Exception ex)
            {
                ConfigStore.Log("内存扫描异常: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _busy, 0);
                int interval = 700;
                try
                {
                    AppConfig root = _rootProvider == null ? _config : _rootProvider();
                    if (root != null && root.MemoryScan != null)
                        interval = Math.Max(50, Math.Min(1000, root.MemoryScan.IntervalMs));
                }
                catch { }
                try { _timer.Change(interval, interval); } catch { }
            }
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
                if (_sawProcess && !_endedNotified)
                {
                    _endedNotified = true;
                    Action h = _gameEnded;
                    if (h != null) h();
                }
                _sawProcess = false;
                _endedNotified = false;
                return;
            }

            string path = _config.GamePath == null ? "" : _config.GamePath.Trim().Trim('"');
            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(_config.GameProcessName)) return;
            string stem = "";
            try { stem = Path.GetFileNameWithoutExtension(path); } catch { }
            List<string> names = new List<string>();
            if (!string.IsNullOrEmpty(stem)) names.Add(stem);
            if (!string.IsNullOrEmpty(_config.GameProcessName))
            {
                string[] parts = _config.GameProcessName.Split(new char[] { ',', ';', ' ' },
                    StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                    if (!names.Contains(parts[i])) names.Add(parts[i]);
            }
            for (int i = 0; i < names.Count; i++)
            {
                Process[] candidates = null;
                try { candidates = Process.GetProcessesByName(names[i]); }
                catch { }
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
                    _handle = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false,
                        (uint)_process.Id);
                    _sawProcess = true;
                    _endedNotified = false;
                    _openErrorLogged = false;
                    if (_handle == IntPtr.Zero)
                    {
                        _openErrorLogged = true;
                        Log("无法读取游戏内存（可能需要管理员权限）: " + Marshal.GetLastWin32Error());
                    }
                    return;
                }
            }
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

        private void Scan(AppConfig root)
        {
            if (_handle == IntPtr.Zero)
            {
                if (!_openErrorLogged)
                {
                    _openErrorLogged = true;
                    Log("游戏内存句柄为空，无法扫描");
                }
                return;
            }
            List<ModeConfig> modes = root == null ? null : root.Modes;
            if (modes == null || root == null || root.MemoryProjects == null)
            {
                _candidateMode = -1;
                _candidateCount = 0;
                return;
            }

            // 遍历所有已启用的内存扫描项目：每个项目是一条独立的状态机规则，
            // 命中后切换到界面预览的目标模式。
            int pointerSize = PointerSize();
            for (int p = 0; p < root.MemoryProjects.Count; p++)
            {
                AutoMemoryProject project = root.MemoryProjects[p];
                if (project == null || !project.Enabled || project.Memory == null) continue;
                if (string.IsNullOrEmpty(project.Memory.BaseAddress)) continue;
                int target = project.TargetMode;
                if (target < 0 || target >= modes.Count || target == root.SelectedMode) continue;

                int value;
                if (!TryReadFeature(project.Memory, pointerSize, out value)) continue;
                long expected;
                bool hasTarget = TryParseValue(project.Memory.TargetValue, out expected);
                bool hit = hasTarget ? value == expected : value != 0;
                if (!hit) continue;

                string name = project.Name;
                if (string.IsNullOrEmpty(name)) name = "项目" + ModeConfig.ChineseNumber(p + 1);
                string selectedName = "「" + name + "」内存地址命中";
                if (_candidateMode == target) _candidateCount++;
                else { _candidateMode = target; _candidateCount = 1; }
                if (_candidateCount >= 2 && (DateTime.UtcNow - _lastSwitch).TotalMilliseconds > 1200)
                {
                    _lastSwitch = DateTime.UtcNow;
                    _candidateMode = -1;
                    _candidateCount = 0;
                    Action<int, string> h = _switchMode;
                    if (h != null) h(target, selectedName);
                }
                return;
            }
            _candidateMode = -1;
            _candidateCount = 0;
        }

        private bool TryReadFeature(MemoryFeatureConfig feature, int pointerSize, out int value)
        {
            value = 0;
            long baseAddress;
            if (!TryResolveBase(feature.BaseAddress, out baseAddress)) return false;
            long address = baseAddress;
            List<long> offsets = ParseOffsets(feature.Offsets);
            for (int i = 0; i < offsets.Count; i++)
            {
                long pointer;
                if (!ReadPointer(address, pointerSize, out pointer)) return false;
                address = pointer + offsets[i];
            }
            return ReadInt32(address, out value);
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
            try
            {
                return long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            }
            catch { return false; }
        }

        private bool TryParseValue(string text, out long value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Trim();
            try
            {
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return long.TryParse(text.Substring(2), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out value);
                if (text.IndexOfAny(new char[] { 'A', 'B', 'C', 'D', 'E', 'F',
                    'a', 'b', 'c', 'd', 'e', 'f' }) >= 0)
                    return long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
                return long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            }
            catch { return false; }
        }

        private int PointerSize()
        {
            if (_process == null) return IntPtr.Size;
            bool wow = false;
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
            if (size == 4) value = BitConverter.ToUInt32(buffer, 0);
            else value = BitConverter.ToInt64(buffer, 0);
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

        private void Log(string text)
        {
            try { ConfigStore.Log(text); } catch { }
            Action<string> h = _notify;
            if (h != null) h(text);
        }

        public void Dispose()
        {
            try { _timer.Dispose(); } catch { }
            lock (_gate)
            {
                CloseProcess();
            }
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
