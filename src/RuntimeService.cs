using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MobileUiConfig
{
    internal class RuntimeService
    {
        private readonly AppConfig _config;
        private readonly Timer _timer;
        private readonly RuntimeOverlay _overlay;
        private DateTime _lastStreamCheck = DateTime.MinValue;
        private bool _streamDetected;
        private int _streamLocalStreak;
        private string _streamReason = "等待检测";
        // 最近一次真实检测到串流连接的时间；连接短暂消失（如串流窗口最小化休眠）
        // 时在 StreamHoldSeconds 内保持串流模式与游戏会话，避免误判游戏结束。
        private DateTime _streamLastSeen = DateTime.MinValue;
        private const double StreamHoldSeconds = 180;
        private int _gamePid;
        private bool _gameSeen;
        private bool _gameRunning;
        private bool _gameEnded;
        private bool _gameForeground;
        private DateTime _missingSince = DateTime.MinValue;
        private DateTime _launchGraceUntil = DateTime.MinValue;
        private bool _settingsVisible;
        private bool _forceOverlay;
        private Rectangle _gameBounds;
        private bool _stopped;
        private bool _gameProcessAlive; // 游戏进程真实存在（不含串流虚拟存活）

        public Action<bool, string> EnvironmentChanged;
        public Action OnGameEnded;
        public Action<bool> ForegroundChanged;
        public Action<string> Notify;
        public Action<int> ModeChangedByOverlay;
        public Action OverlayStateChanged;
        // 显示键「拦截鼠标点击」切换：主程序接到后切换输入辅助服务运行时拦截态。
        public Action BlockMouseClicksToggled;
        // 显示键「跳过剧情」切换：主程序接到后切换当前页根跳过剧情总开关并弹居中提示。
        public Action StorySkipToggled;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public RuntimeService(AppConfig config)
        {
            _config = config;
            _overlay = new RuntimeOverlay(config);
            _overlay.LayoutChanged = delegate
            {
                Action h = OverlayLayoutChanged;
                if (h != null) h();
            };
            _overlay.ConfigChanged = delegate
            {
                Action<int> m = ModeChangedByOverlay;
                if (m != null) m(_overlay.WorkingRoot.SelectedMode);
            };
            _overlay.StatusMessage = delegate(string msg)
            {
                Action<string> n = Notify;
                if (n != null) n(msg);
            };
            _overlay.ToggleBlockMouseClicks = delegate
            {
                Action h = BlockMouseClicksToggled;
                if (h != null) h();
            };
            _overlay.ToggleStorySkip = delegate
            {
                Action h = StorySkipToggled;
                if (h != null) h();
            };
            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate { Tick(); };
        }

        public Action OverlayLayoutChanged;
        public bool GameRunning { get { return _gameRunning; } }
        public bool GameProcessAlive { get { return _gameProcessAlive; } }
        public bool Stopped { get { return _stopped; } }
        public bool GameEndedFlag { get { return _gameEnded; } }
        public int GamePid { get { return _gamePid; } }
        public bool GameForeground { get { return _gameForeground; } }
        public bool StreamDetected { get { return _streamDetected; } }
        public string StreamReason { get { return _streamReason; } }
        public bool OverlayActive { get { return _overlay.Active; } }
        // 当前游戏画面窗口矩形（本地=游戏主窗口，串流=前台串流窗口）；识图服务优先截取该区域，
        // 避免把整屏/多显示器背景混进匹配画面。取不到时返回 PrimaryScreen 全屏兜底。
        public Rectangle CurrentBounds { get { return _gameBounds; } }

        public void EnableTestOverlay()
        {
            _forceOverlay = true;
        }

        public void Start()
        {
            _stopped = false;
            _gameEnded = false;
            _gameSeen = false;
            _gameRunning = false;
            _gameProcessAlive = false;
            _gamePid = 0;
            _gameForeground = false;
            _missingSince = DateTime.MinValue;
            _launchGraceUntil = DateTime.MinValue;
            CheckGameNow();
            CheckStream(true);
            UpdateOverlay();
            _timer.Start();
        }

        public void Stop()
        {
            _stopped = true;
            _timer.Stop();
            try { _overlay.Stop(); } catch { }
            try { InputSender.ReleaseAll(); } catch { }
            try { RumbleService.Stop(); } catch { }
        }

        // 会话级"停止"：游戏结束后调用。保留 _stopped（运行时仍视为停止状态），
        // 但清空所有"卡住"的标记，允许下次启动游戏时通过 NotifyGameLaunched
        // 或 RestartMonitoring 重新恢复监控。
        public void MarkSessionEnded()
        {
            _stopped = true; // 标记为已停止，让 RestartMonitoring 能恢复
            _gameRunning = false;
            _gameSeen = false;
            _gameEnded = false;
            _gamePid = 0;
            _gameForeground = false;
            _missingSince = DateTime.MinValue;
            _launchGraceUntil = DateTime.MinValue;
            _streamLastSeen = DateTime.MinValue;
            _streamLocalStreak = 0;
            try { _overlay.Stop(); } catch { }
            try { InputSender.ReleaseAll(); } catch { }
            try { RumbleService.Stop(); } catch { }
        }

        // 重新启动监控（用于"游戏结束后不退出软件，再次启动游戏"场景）
        public void RestartMonitoring()
        {
            if (!_stopped) return; // 没停过就不需要重启
            _stopped = false;
            _gameEnded = false;
            _gameSeen = false;
            _gameRunning = false;
            _gameProcessAlive = false;
            _gamePid = 0;
            _gameForeground = false;
            _missingSince = DateTime.MinValue;
            _launchGraceUntil = DateTime.MinValue;
            CheckGameNow();
            CheckStream(true);
            UpdateOverlay();
            _timer.Start();
        }

        public void SetSettingsVisible(bool visible)
        {
            _settingsVisible = visible;
            _overlay.SetSuspended(visible);
        }

        public void NotifyGameLaunched(int pid)
        {
            if (pid > 0) _gamePid = pid;
            _gameSeen = true;
            _gameRunning = true;
            _gameEnded = false;
            _missingSince = DateTime.MinValue;
            _launchGraceUntil = DateTime.Now.AddSeconds(30);
            _overlay.ResetToDefaultHidden();
            CheckGameNow();
            CheckStream(true);
            UpdateOverlay();
        }

        public void Recheck()
        {
            CheckGameNow();
            CheckStream(true);
            UpdateOverlay();
        }

        public void RefreshConfig()
        {
            CheckGameNow();
            UpdateOverlay();
            if (_overlay.Active) _overlay.Reload();
        }

        public void OpenOverlayMenuForTest()
        {
            _overlay.OpenMenuForTest();
        }

        public void RefreshCooldownDisplay()
        {
            if (_overlay != null) _overlay.RefreshCooldownDisplay();
        }

        // 真机悬浮层居中汉字提示转发（拦截鼠标点击等开关切换反馈）。
        public void ShowOverlayNotice(string text)
        {
            if (_overlay != null) _overlay.ShowCenteredNotice(text);
        }

        // 「拦截鼠标点击」运行时状态转发到悬浮层空白遮罩（仅按键页）。
        public void SetBlockBlankClicks(bool block)
        {
            if (_overlay != null) _overlay.SetBlockBlankClicks(block);
        }

        public void NotifyConfigChanged(bool positionOnly)
        {
            if (positionOnly)
            {
                return;
            }
            if (_overlay.Active) _overlay.Reload();
            UpdateOverlay();
        }

        private void Tick()
        {
            if (_stopped) return;
            try
            {
                CheckGameNow();
                UpdateForeground();
                if ((DateTime.Now - _lastStreamCheck).TotalSeconds >= 2.5)
                {
                    CheckStream(false);
                }
                UpdateOverlay();
                if (_overlay.Active) _overlay.ReassertTopMost();
                if (_gameEnded)
                {
                    _timer.Stop();
                    SetForeground(false);
                    try { _overlay.Stop(); } catch { }
                    try { InputSender.ReleaseAll(); } catch { }
                    Action h = OnGameEnded;
                    if (h != null) h();
                }
            }
            catch (Exception ex)
            {
                ConfigStore.Log("运行时监控异常: " + ex.Message);
            }
        }

        private void CheckGameNow()
        {
            if (_gameEnded) return;
            bool alive = false;
            int alivePid = 0;
            if (_gamePid > 0)
            {
                try
                {
                    using (Process p = Process.GetProcessById(_gamePid))
                    {
                        // 必须验证进程名匹配，防止 PID 被系统回收重用后误判
                        string pn = (p.ProcessName ?? "").ToLowerInvariant();
                        List<string> names = GameProcessNames();
                        bool match = false;
                        for (int i = 0; i < names.Count; i++)
                        {
                            if (pn == names[i]) { match = true; break; }
                        }
                        if (match && !IsSystemHelperProcess(pn))
                        {
                            // 关键：跟任务管理器保持一致——取不到进程可执行路径的"幽灵进程"
                            // （已退出但句柄未释放、或在其他用户会话），一律视为不存在。
                            bool realProcess = false;
                            try
                            {
                                string exePath = p.MainModule != null ? p.MainModule.FileName : null;
                                if (!string.IsNullOrEmpty(exePath)) realProcess = true;
                            }
                            catch { /* 取不到路径 = 任务管理器也看不到 */ }
                            if (realProcess)
                            {
                                alive = true;
                                alivePid = p.Id;
                            }
                        }
                    }
                }
                catch
                {
                }
            }

            if (!alive)
            {
                List<string> names = GameProcessNames();
                for (int i = 0; i < names.Count; i++)
                {
                    Process[] ps = null;
                    try { ps = Process.GetProcessesByName(names[i]); }
                    catch { }
                    if (ps == null) continue;
                    for (int j = 0; j < ps.Length; j++)
                    {
                        try
                        {
                            // 严格匹配：进程名必须等于游戏程序名（不含扩展名），
                            // 且排除系统辅助进程（GameBar、GameViewer 等）。
                            // 注意：这里不做 MainModule 检查——游戏若以管理员运行而本软件
                            // 是普通权限，取 MainModule 会"拒绝访问"导致误判游戏不存在。
                            string pn = ps[j].ProcessName.ToLowerInvariant();
                            if (IsSystemHelperProcess(pn)) continue;
                            alive = true;
                            alivePid = ps[j].Id;
                            break;
                        }
                        catch { }
                    }
                    if (ps != null)
                    {
                        for (int j = 0; j < ps.Length; j++)
                        {
                            try { ps[j].Dispose(); } catch { }
                        }
                    }
                    if (alive) break;
                }
            }

            if (alive)
            {
                _gamePid = alivePid;
                _gameRunning = true;
                _gameSeen = true;
                _gameProcessAlive = true;
                _missingSince = DateTime.MinValue;
                _gameBounds = GetGameBounds();
            }
            else
            {
                _gameProcessAlive = false;
                // 串流会话仍在（或刚刚还在，如串流窗口最小化导致连接暂时休眠）时，
                // 游戏会话视为存活：只隐藏悬浮层，绝不误判"游戏结束"导致运行时
                // 停摆、虚拟按键再也恢复不了。
                bool streamHold = _streamDetected ||
                    (_streamLastSeen != DateTime.MinValue &&
                     (DateTime.Now - _streamLastSeen).TotalSeconds < StreamHoldSeconds);
                if (streamHold)
                {
                    _gameRunning = true;
                    _gameSeen = true;
                    _missingSince = DateTime.MinValue;
                    return;
                }
                _gameRunning = false;
                if (_gameSeen && !_gameEnded)
                {
                    if (_missingSince == DateTime.MinValue) _missingSince = DateTime.Now;
                    bool inLaunchGrace = DateTime.Now < _launchGraceUntil;
                    if (!inLaunchGrace && (DateTime.Now - _missingSince).TotalSeconds >= 10.0)
                    {
                        _gameEnded = true;
                        ConfigStore.Log("检测到游戏进程结束");
                    }
                }
            }
        }

        private bool IsGameProcessName(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    string name = (p.ProcessName ?? "").ToLowerInvariant();
                    if (name.Length == 0) return false;
                    List<string> names = GameProcessNames();
                    for (int i = 0; i < names.Count; i++)
                        if (name == names[i]) return true;
                }
            }
            catch { }
            return false;
        }

        // Windows shell 的临时界面进程：开始菜单、搜索、任务视图等。
        // 这些界面由 Win 键等触发，只是盖在游戏上方，不代表游戏切到后台。
        private static bool IsShellTransientProcess(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    string name = (p.ProcessName ?? "").ToLowerInvariant();
                    return name == "startmenuexperiencehost" ||
                           name == "searchhost" ||
                           name == "searchui" ||
                           name == "shellexperiencehost" ||
                           name == "textinputhost" ||
                           name == "cortana";
                }
            }
            catch { }
            return false;
        }

        private void UpdateForeground()
        {
            bool foreground = false;
            if (_gameRunning || _streamDetected)
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd != IntPtr.Zero)
                {
                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    int foregroundPid = (int)pid;
                    if (foregroundPid == Process.GetCurrentProcess().Id)
                    {
                        // The overlay itself is WS_EX_NOACTIVATE, but Windows
                        // can still report it as foreground for a frame. Do not
                        // treat our own overlay as "game in background", or the
                        // overlay disappears the moment a virtual key is clicked.
                        foreground = !_settingsVisible && (_gameRunning || _streamDetected);
                    }
                    else if (IsShellTransientProcess(foregroundPid))
                    {
                        // 按 Win 键呼出开始菜单、搜索等 shell 临时界面时，前台进程
                        // 变成 StartMenuExperienceHost 等，但游戏并未真正切后台。
                        // 保持当前前台状态不变，避免虚拟按键因误判而后台消失。
                        foreground = _gameForeground;
                    }
                    else
                    {
                        if (_gamePid > 0 && foregroundPid == _gamePid) foreground = true;
                        else if (_gameRunning && IsGameProcessName(foregroundPid)) foreground = true;
                        else if (_streamDetected &&
                                 StreamDetector.IsStreamProcess(foregroundPid, _config.StreamProcessList))
                            foreground = true;
                    }
                }
            }
            SetForeground(foreground);
        }

        private void SetForeground(bool foreground)
        {
            if (_gameForeground == foreground) return;
            _gameForeground = foreground;
            ConfigStore.Log(foreground ? "游戏回到前台，恢复功能" : "游戏进入后台，暂停功能并释放按键");
            if (!foreground)
            {
                try { InputSender.ReleaseAll(); } catch { }
            }
            else
            {
                // On (re)gaining foreground — e.g. after a loading screen or
                // world-entry transition — the game may have missed a key-up
                // delivered while its window state was unstable. Releasing any
                // keys the overlay still considers pressed clears the stuck
                // state (character walking on its own) without affecting keys
                // the user is actively holding at this instant.
                try { InputSender.ReleaseAll(); } catch { }
            }
            Action<bool> h = ForegroundChanged;
            if (h != null) h(foreground);
        }

        private static bool IsSystemHelperProcess(string processName)
        {
            // 系统/平台辅助进程：它们不是游戏本体，不能算"游戏正在运行"
            // 只排除真正会干扰检测的系统服务，不排除游戏平台（用户不会配错）
            return processName.Contains("gamebar")
                || processName.Contains("gameviewer")
                || processName.Contains("fevergames")
                || processName.Contains("gameinput")
                || processName.Contains("xboxgame")
                || processName.Contains("xboxapp");
        }

        private List<string> GameProcessNames()        {
            List<string> result = new List<string>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            string configured = _config.GameProcessName == null ? "" : _config.GameProcessName;
            string[] parts = configured.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++) AddName(result, seen, parts[i]);
            try
            {
                if (!string.IsNullOrEmpty(_config.GamePath))
                {
                    string stem = System.IO.Path.GetFileNameWithoutExtension(_config.GamePath.Trim().Trim('"'));
                    AddName(result, seen, stem);
                }
            }
            catch { }
            return result;
        }

        private static void AddName(List<string> list, Dictionary<string, bool> seen, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            string n = name.Trim();
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            n = n.ToLowerInvariant();
            if (n.Length == 0 || seen.ContainsKey(n)) return;
            seen[n] = true;
            list.Add(n);
        }

        private Rectangle GetGameBounds()
        {
            try
            {
                if (_gamePid > 0)
                {
                    using (Process p = Process.GetProcessById(_gamePid))
                    {
                        IntPtr h = p.MainWindowHandle;
                        if (h != IntPtr.Zero)
                        {
                            RECT r;
                            if (GetWindowRect(h, out r))
                            {
                                int w = r.Right - r.Left;
                                int hh = r.Bottom - r.Top;
                                if (w > 200 && hh > 200)
                                {
                                    return new Rectangle(r.Left, r.Top, w, hh);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return Screen.PrimaryScreen.Bounds;
        }

        private Rectangle GetStreamBounds()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd != IntPtr.Zero)
                {
                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    if (StreamDetector.IsStreamProcess((int)pid, _config.StreamProcessList))
                    {
                        RECT r;
                        if (GetWindowRect(hwnd, out r))
                        {
                            int w = r.Right - r.Left;
                            int h = r.Bottom - r.Top;
                            if (w > 200 && h > 200) return new Rectangle(r.Left, r.Top, w, h);
                        }
                    }
                }
            }
            catch { }
            return Screen.PrimaryScreen.Bounds;
        }

        private void CheckStream(bool forceEvent)
        {
            _lastStreamCheck = DateTime.Now;
            string reason;
            bool stream;
            try
            {
                stream = StreamDetector.Detect(_config.StreamProcessList, out reason);
            }
            catch (Exception ex)
            {
                stream = false;
                reason = "检测失败: " + ex.Message;
            }
            // Stream connection counts fluctuate; a single empty reading must
            // not tear down the overlay mid-game. Hold stream mode for a while
            // after the last real detection (covers the stream window being
            // minimized, which suspends its connections).
            if (stream) _streamLastSeen = DateTime.Now;
            if (!stream && _streamDetected &&
                (DateTime.Now - _streamLastSeen).TotalSeconds < StreamHoldSeconds)
            {
                _streamLocalStreak++;
                stream = true;
                reason = "串流保持中（本地检测 " + _streamLocalStreak + ")";
            }
            else if (stream)
            {
                _streamLocalStreak = 0;
            }
            bool changed = _streamDetected != stream || _streamReason != reason;
            _streamDetected = stream;
            _streamReason = reason;
            if (changed)
            {
                ConfigStore.Log("串流检测: " + reason + " => " + (stream ? "串流模式" : "本地模式"));
            }
            if (forceEvent || changed)
            {
                Action<bool, string> h = EnvironmentChanged;
                if (h != null) h(_streamDetected, _streamReason);
            }
        }

        private void UpdateOverlay()
        {
            // 悬浮层的存活只取决于游戏是否在运行；前台/后台改用 SetHidden
            // 切换可见性，避免切后台销毁、回前台重建导致虚拟按键消失。
            bool want = _forceOverlay ||
                (_gameRunning && (_streamDetected ? _config.StreamOverlayEnabled : _config.LocalOverlayEnabled));
            bool wasActive = _overlay.Active;
            if (!want)
            {
                if (_overlay.Active)
                {
                    _overlay.Stop();
                    ConfigStore.Log("串流悬浮层已关闭");
                }
                if (wasActive)
                {
                    Action h = OverlayStateChanged;
                    if (h != null) h();
                }
                return;
            }
            if (_forceOverlay)
            {
                _gameBounds = Screen.PrimaryScreen.Bounds;
            }
            else if (_streamDetected)
            {
                _gameBounds = GetStreamBounds();
            }
            else
            {
                _gameBounds = GetGameBounds();
            }
            if (!_overlay.Active)
            {
                _overlay.SetSuspended(_settingsVisible && !_forceOverlay);
                _overlay.Start(_gameBounds);
                ConfigStore.Log("串流悬浮层已加载，模式=" + (_overlay.WorkingRoot.ActiveMode == null ? "-" : _overlay.WorkingRoot.ActiveMode.Name)
                    + "，区域=" + _gameBounds);
            }
            else
            {
                _overlay.SetBounds(_gameBounds);
                _overlay.SetSuspended(_settingsVisible && !_forceOverlay);
            }
            _overlay.SetHidden(!_forceOverlay && !_gameForeground);
            if (!wasActive && _overlay.Active)
            {
                Action h = OverlayStateChanged;
                if (h != null) h();
            }
        }
    }

    internal static class AutoStart
    {
        private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string ValueName = "MobileUIConfig";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    return key.GetValue(ValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return false;
                    if (enabled)
                    {
                        string exe = Application.ExecutablePath;
                        key.SetValue(ValueName, "\"" + exe + "\" --background");
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                ConfigStore.Log("设置开机后台待命失败: " + ex.Message);
                return false;
            }
        }
    }
}
