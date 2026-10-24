// ============================================================================
// Overlay.cs —— 真机悬浮层（游戏画面之上的虚拟按键 / 摇杆 / 视角 / 菜单窗口集合）
// ----------------------------------------------------------------------------
// 本文件只负责“游戏运行时”实际显示在游戏画面上的悬浮控件；设置窗口里的手机预览
// 由 PhonePreview.cs 负责，二者互不相同。
//
// 结构总览：
//   · RuntimeOverlay      悬浮层总控：创建 / 销毁各悬浮窗口，维护显示、隐藏、挂起三态，
//                         游戏切后台时挂起、回前台时恢复，并定期重新置顶；
//   · OverlayFormBase     所有悬浮窗口基类：无边框、置顶、不抢焦点、触摸消息处理；
//   · OverlayKeyForm      单个虚拟按键悬浮窗：短按 / 长按 / 双击 / 联动保持长按状态机；
//   · OverlaySystemForm   显示键、模式键两个系统按键悬浮窗；
//   · OverlayJoystickForm 左 / 右摇杆悬浮窗：中心弹簧回弹、八方向 / 轴上报；
//   · OverlayMouseLookForm 视角操控悬浮窗：按住后滑动转换为相对鼠标移动；
//   · OverlayModeMenuForm 模式选择弹出菜单；
//   · OverlayClickShield  空白区域点击穿透遮罩；
//   · OverlayNoticeForm   屏幕居中文字提示（功能开关开启 / 关闭提示等）；
//   · OverlayViewMoveHook / OverlayMouseReleaseHook 鼠标低级钩子，用于视角会话独占与残留按键清理。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MobileUiConfig
{
    /// <summary>
    /// 悬浮层总控：持有并管理全部真机悬浮窗口，提供 Start / Stop / Reload / SetSuspended /
    /// SetHidden / ReassertTopMost 等生命周期方法；游戏前台才激活，后台自动挂起并释放输入。
    /// </summary>
    internal class RuntimeOverlay
    {
        private readonly AppConfig _config;
        private readonly List<OverlayKeyForm> _keyForms = new List<OverlayKeyForm>();
        private readonly HashSet<string> _activeKeyIds = new HashSet<string>();
        private OverlaySystemForm _displayForm;
        private OverlaySystemForm _modeForm;
        private OverlayJoystickForm _joystickForm;
        private OverlayJoystickForm _joystickRForm;
        private OverlayMouseLookForm _mouseLookForm;
        private OverlayModeMenuForm _menuForm;
        private OverlayClickShield _clickShield;
        private Rectangle _bounds;
        private bool _active;
        private bool _suspended;
        private bool _hidden;
        private bool _hasDefaultViewState;
        private bool _menuOpen;
        private bool _joystickActive;
        private bool _mouseLookActive;
        private int _viewState;
        private Timer _showTimer;
        // 显示键「拦截鼠标点击」运行时开关：开启时强制在按键页空白区加一层捕获遮罩，
        // 吞掉穿透到游戏的鼠标/触摸按下；关闭后按 ClickThrough 原口径放行。不持久化。
        private bool _blockBlankClicks;

        // 叠加层：独立持有一组叠加按键表单；主模式切换后默认关闭，
        // 仅非切换类重建（尺寸变化、配置重载）时保持显示。
        private readonly List<OverlayKeyForm> _overlayKeyForms = new List<OverlayKeyForm>();
        private int _overlayActiveIndex = -1;
        private System.Windows.Forms.Timer _overlayHideTimer;
        private int _lastRebuiltModeIndex = -1;

        // 屏幕正中短时汉字浮层提示（拦截鼠标点击等开关切换反馈）；鼠标/触控穿透，不挡游戏操作。
        private OverlayNoticeForm _noticeForm;
        private System.Windows.Forms.Timer _noticeTimer;

        public Action LayoutChanged;
        public Action ConfigChanged;
        public Action<string> StatusMessage;
        // 显示键动作「拦截鼠标点击」切换回调：由主程序接到输入辅助服务切换运行时拦截态。
        public Action ToggleBlockMouseClicks;
        // 显示键动作「跳过剧情」切换回调：由主程序切换当前页根跳过剧情总开关并弹居中提示。
        public Action ToggleStorySkip;

        // 当前页生效配置根：0=虚拟按键主根，1=虚拟手柄独立根。模式、摇杆、方向键、视角、
        // 按键列表、叠加层、模式菜单全部从该根读取；显示键/模式键/全局反馈/震动等系统级
        // 配置仍走主根 _config，两页共用。
        internal AppConfig WorkingRoot
        {
            get { return _config != null && _config.UiPadSide == 1 && _config.Gamepad != null ? _config.Gamepad : _config; }
        }

        public RuntimeOverlay(AppConfig config)
        {
            _config = config;
            _viewState = 0;
        }

        public bool Active
        {
            get { return _active; }
        }

        public int ViewState
        {
            get { return _viewState; }
        }

        /// <summary>按给定游戏窗口边界创建并显示全部悬浮控件；重复调用前会先清理旧窗口。</summary>
        public void Start(Rectangle bounds)
        {
            _bounds = bounds;
            _active = true;
            if (!_hasDefaultViewState)
            {
                // Default to the hidden state on launch; the on-screen 显示
                // key (or topbar/mode key) is used to reveal the controls.
                _viewState = 1;
                _hasDefaultViewState = true;
            }
            _menuOpen = false;
            Rebuild();
        }

        private void ReleaseKeysOnly()
        {
            if (!_active) return;
            for (int i = 0; i < _keyForms.Count; i++)
            {
                OverlayKeyForm form = _keyForms[i];
                if (form == null || !form.HasActivePress) continue;
                if (form.Key != null && form.Key.ShortPress != null && form.Key.ShortPress.Kind == BindingKind.Keyboard)
                    InputSender.SuppressKey(form.Key.ShortPress.Code, 150);
                form.ForceReleaseInteraction();
            }
            for (int i = 0; i < _overlayKeyForms.Count; i++)
            {
                OverlayKeyForm form = _overlayKeyForms[i];
                if (form == null || !form.HasActivePress) continue;
                if (form.Key != null && form.Key.ShortPress != null && form.Key.ShortPress.Kind == BindingKind.Keyboard)
                    InputSender.SuppressKey(form.Key.ShortPress.Code, 150);
                form.ForceReleaseInteraction();
            }
        }

        private void ReleaseAllInteractions()
        {
            if (!_active) return;
            for (int i = 0; i < _keyForms.Count; i++)
                if (_keyForms[i] != null) _keyForms[i].ForceReleaseInteraction();
            for (int i = 0; i < _overlayKeyForms.Count; i++)
                if (_overlayKeyForms[i] != null) _overlayKeyForms[i].ForceReleaseInteraction();
            if (_joystickForm != null) _joystickForm.ForceReleaseInteraction();
            if (_joystickRForm != null) _joystickRForm.ForceReleaseInteraction();
            if (_mouseLookForm != null) _mouseLookForm.ForceReleaseInteraction();
            if (_displayForm != null) _displayForm.ForceReleaseInteraction();
            if (_modeForm != null) _modeForm.ForceReleaseInteraction();
        }

        public void RefreshCooldownDisplay()
        {
            for (int i = 0; i < _keyForms.Count; i++)
            {
                if (_keyForms[i] != null) _keyForms[i].Invalidate();
            }
        }

        public void ResetToDefaultHidden()
        {
            _viewState = 1;
            _hasDefaultViewState = true;
            if (_active && !_suspended) Rebuild();
        }

        /// <summary>销毁全部悬浮窗口并释放已按下的键盘 / 鼠标 / 手柄输入。</summary>
        public void Stop()
        {
            _active = false;
            _menuOpen = false;
            _hidden = false;
            if (_showTimer != null) _showTimer.Stop();
            CloseAll();
            CloseAllOverlay();
        }

        public void SetBounds(Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            if (_bounds == bounds) return;
            _bounds = bounds;
            if (_active && !_suspended) Rebuild();
        }

        /// <summary>
        /// 挂起 / 恢复：游戏切后台时销毁全部悬浮窗并释放输入；回前台且仍处于激活态时重建。
        /// 与 SetHidden 的区别：挂起是“销毁”，隐藏只是不可见。
        /// </summary>
        public void SetSuspended(bool suspended)
        {
            if (_suspended == suspended) return;
            _suspended = suspended;
            _menuOpen = false;
            if (_suspended)
            {
                CloseAll();
                CloseAllOverlay();
            }
            else if (_active)
            {
                Rebuild();
            }
        }

        // 游戏切后台时只隐藏悬浮窗、不销毁；回前台瞬时恢复可见，
        // 避免反复 Stop/Start 全量重建导致虚拟按键“消失”。
        public void SetHidden(bool hidden)
        {
            if (_hidden == hidden) return;
            _hidden = hidden;
            if (!_active || _suspended) return;
            ApplyHidden();
        }

        private void ApplyHidden()
        {
            bool visible = !_hidden;
            if (_clickShield != null && !_clickShield.IsDisposed) _clickShield.Visible = visible;
            if (_joystickForm != null && !_joystickForm.IsDisposed) _joystickForm.Visible = visible;
            if (_joystickRForm != null && !_joystickRForm.IsDisposed) _joystickRForm.Visible = visible;
            if (_mouseLookForm != null && !_mouseLookForm.IsDisposed) _mouseLookForm.Visible = visible;
            for (int i = 0; i < _keyForms.Count; i++)
            {
                OverlayKeyForm form = _keyForms[i];
                if (form != null && !form.IsDisposed) form.Visible = visible;
            }
            for (int i = 0; i < _overlayKeyForms.Count; i++)
            {
                OverlayKeyForm form = _overlayKeyForms[i];
                if (form != null && !form.IsDisposed) form.Visible = visible;
            }
            if (_displayForm != null && !_displayForm.IsDisposed) _displayForm.Visible = visible;
            if (_modeForm != null && !_modeForm.IsDisposed) _modeForm.Visible = visible;
            if (_hidden) CloseMenu();
        }

        /// <summary>配置被修改后，在激活且未挂起的状态下重建全部悬浮控件以应用新布局。</summary>
        public void Reload()
        {
            if (_active && !_suspended) Rebuild();
        }

        // 切换「拦截鼠标点击」运行时状态：仅按键页生效；置位后重建悬浮层，
        // 由 Rebuild 决定是否补一层空白捕获遮罩。关闭后回到 ClickThrough 原口径。
        public void SetBlockBlankClicks(bool block)
        {
            if (_blockBlankClicks == block) return;
            _blockBlankClicks = block;
            if (_active && !_suspended) Rebuild();
        }

        // 屏幕正中短时汉字浮层提示（拦截鼠标点击已开启/已关闭等开关切换反馈）；复用同一浮层，
        // 1.4 秒后自动隐藏，鼠标/触控穿透，不拦截游戏操作。
        public void ShowCenteredNotice(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_noticeForm == null)
            {
                _noticeForm = new OverlayNoticeForm();
                _noticeTimer = new Timer();
                _noticeTimer.Interval = 1400;
                _noticeTimer.Tick += delegate
                {
                    if (_noticeTimer != null) _noticeTimer.Stop();
                    if (_noticeForm != null) _noticeForm.Visible = false;
                };
            }
            _noticeForm.SetText(text);
            Rectangle center = _bounds;
            if (center.IsEmpty) center = Screen.PrimaryScreen.Bounds;
            int w = Math.Min(Theme.S(440), center.Width);
            int h = Theme.S(88);
            _noticeForm.SetBounds(center.Left + (center.Width - w) / 2,
                center.Top + (center.Height - h) / 2, w, h);
            if (!_noticeForm.Visible) _noticeForm.ShowNoActivate();
            _noticeForm.Visible = true;
            _noticeTimer.Stop();
            _noticeTimer.Start();
        }

        private void SetViewDragging(bool active)
        {
            if (_clickShield == null) return;
            // When blank-area click-through is disabled, the full-screen
            // shield would otherwise receive the injected camera button-down.
            // Do not show it again on release: making a full-screen topmost
            // window visible cancels the other finger's touch contact and
            // steals focus from the game.
            if (active) _clickShield.Visible = false;
        }

        /// <summary>全屏游戏可能抢走置顶层级；定时以不改变位置、不激活窗口的方式重新置顶全部悬浮窗。</summary>
        public void ReassertTopMost()
        {            if (!_active || _suspended) return;
            // Fullscreen games can steal the topmost z-order. Re-assert without
            // activating or moving the overlay windows.
            const uint flags = 0x0001 | 0x0002 | 0x0010; // NOSIZE | NOMOVE | NOACTIVATE
            if (_clickShield != null && _clickShield.IsHandleCreated)
                SetWindowPos(_clickShield.Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
            if (_joystickForm != null && _joystickForm.IsHandleCreated)
                SetWindowPos(_joystickForm.Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
            if (_joystickRForm != null && _joystickRForm.IsHandleCreated)
                SetWindowPos(_joystickRForm.Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
            if (_mouseLookForm != null && _mouseLookForm.IsHandleCreated)
                SetWindowPos(_mouseLookForm.Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
            for (int i = 0; i < _keyForms.Count; i++)
            {
                OverlayKeyForm form = _keyForms[i];
                if (form != null && form.IsHandleCreated)
                    SetWindowPos(form.Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
            }
            for (int i = 0; i < _overlayKeyForms.Count; i++)
            {
                OverlayKeyForm form = _overlayKeyForms[i];
                if (form != null && form.IsHandleCreated)
                    SetWindowPos(form.Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
            }
            if (_displayForm != null && _displayForm.IsHandleCreated)
                SetWindowPos(_displayForm.Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
            if (_modeForm != null && _modeForm.IsHandleCreated)
                SetWindowPos(_modeForm.Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
            if (_menuForm != null && _menuForm.IsHandleCreated)
                SetWindowPos(_menuForm.Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
            if (_noticeForm != null && _noticeForm.IsHandleCreated)
                SetWindowPos(_noticeForm.Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
        }

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        public void OpenMenuForTest()
        {
            if (_active && !_suspended) ToggleMenu();
        }

        private float VisualScale()
        {
            float scale = Theme.Scale;
            if (scale < 0.75f) scale = 0.75f;
            if (scale > 3f) scale = 3f;
            return scale;
        }

        private void CloseAll()
        {
            for (int i = 0; i < _keyForms.Count; i++)
            {
                try { _keyForms[i].Close(); _keyForms[i].Dispose(); }
                catch { }
            }
            _keyForms.Clear();
            _activeKeyIds.Clear();
            _joystickActive = false;
            _mouseLookActive = false;
            if (_joystickForm != null)
            {
                try { _joystickForm.Close(); _joystickForm.Dispose(); } catch { }
                _joystickForm = null;
            }
            if (_joystickRForm != null)
            {
                try { _joystickRForm.Close(); _joystickRForm.Dispose(); } catch { }
                _joystickRForm = null;
            }
            if (_mouseLookForm != null)
            {
                try { _mouseLookForm.Close(); _mouseLookForm.Dispose(); } catch { }
                _mouseLookForm = null;
            }
            if (_displayForm != null)
            {
                try { _displayForm.Close(); _displayForm.Dispose(); } catch { }
                _displayForm = null;
            }
            if (_modeForm != null)
            {
                try { _modeForm.Close(); _modeForm.Dispose(); } catch { }
                _modeForm = null;
            }
            if (_clickShield != null)
            {
                try { _clickShield.Close(); _clickShield.Dispose(); } catch { }
                _clickShield = null;
            }
            if (_noticeForm != null)
            {
                try { _noticeForm.Close(); _noticeForm.Dispose(); } catch { }
                _noticeForm = null;
            }
            if (_noticeTimer != null)
            {
                try { _noticeTimer.Stop(); _noticeTimer.Dispose(); } catch { }
                _noticeTimer = null;
            }
            CloseMenu();
        }

        // 叠加层表单独立持有：主层 CloseAll/Rebuild 不触碰；仅在停止、挂起或显式隐藏时关闭。
        private void CloseAllOverlay()
        {
            if (_overlayHideTimer != null)
            {
                try { _overlayHideTimer.Stop(); } catch { }
                try { _overlayHideTimer.Dispose(); } catch { }
                _overlayHideTimer = null;
            }
            for (int i = 0; i < _overlayKeyForms.Count; i++)
            {
                try { _overlayKeyForms[i].ForceReleaseInteraction(); } catch { }
                try { _overlayKeyForms[i].Close(); _overlayKeyForms[i].Dispose(); }
                catch { }
            }
            _overlayKeyForms.Clear();
            _overlayActiveIndex = -1;
        }

        private void CloseMenu()
        {
            _menuOpen = false;
            if (_menuForm != null)
            {
                try { _menuForm.Close(); _menuForm.Dispose(); } catch { }
                _menuForm = null;
            }
        }

        private void Rebuild()
        {
            // 销毁旧窗体前先干净释放按键/摇杆/视角的持有态，避免残留的物理键位、
            // 鼠标右键拖拽在重建瞬间卡住（模式切换重建时尤其需要）。
            try { ReleaseAllInteractions(); } catch { }
            CloseAll();
            if (!_active || _suspended) return;
            ModeConfig mode = WorkingRoot.ActiveMode;
            if (mode == null) return;

            // 主模式发生切换：叠加层默认关闭、不跨模式保留；非切换重建
            // （尺寸变化、配置重载、隐藏态恢复）才保持叠加层显示。
            int currentMode = WorkingRoot.SelectedMode;
            bool modeChanged = currentMode != _lastRebuiltModeIndex;
            _lastRebuiltModeIndex = currentMode;
            if (modeChanged) CloseAllOverlay();

            float scale = VisualScale();
            // 页面隔离：1=虚拟手柄（右摇杆连续轴、不显示视角）；0=虚拟按键（左摇杆走离散方向绑定、显示视角）。
            bool gamepad = _config.UiPadSide == 1;
            // 按键页空白穿透由主根 ClickThrough 决定；手柄页独立根 BlockBlankTouch 开启时拦截空白触摸，
            // 防止手指滑出按键区误触游戏画面。按键页「拦截鼠标点击」运行时开启时同样强制补一层
            // 空白捕获遮罩，吞掉穿透到游戏的鼠标/触摸按下（原生触摸不经过低级鼠标钩子，必须在
            // 悬浮窗体层拦截才生效）；移动/视角仍由视角窗与各按键窗自行处理。
            if (!_config.ClickThrough || (!gamepad && _blockBlankClicks) || (gamepad && WorkingRoot.BlockBlankTouch))
            {
                _clickShield = new OverlayClickShield(_bounds);
                _clickShield.Show();
                _clickShield.SendToBack();
            }
            JoystickConfig overlayJoy = mode.Joystick;
            if (overlayJoy != null && overlayJoy.Enabled && _viewState != 1 && mode.ShowJoystick)
            {
                _joystickForm = new OverlayJoystickForm(_config, overlayJoy, _bounds, _viewState == 2, false,
                    delegate(bool active) { HandleJoystickActive(active); },
                    delegate { LayoutChangedInternal(); });
                _joystickForm.ShowNoActivate();
            }
            // 右摇杆仅虚拟手柄页创建；按键页不出现右摇杆。
            JoystickConfig overlayJoyR = mode.JoystickR;
            if (gamepad && overlayJoyR != null && overlayJoyR.Enabled && _viewState != 1 && mode.ShowJoystick)
            {
                _joystickRForm = new OverlayJoystickForm(_config, overlayJoyR, _bounds, _viewState == 2, true,
                    delegate(bool active) { HandleJoystickActive(active); },
                    delegate { LayoutChangedInternal(); });
                _joystickRForm.ShowNoActivate();
            }
            // 视角操控仅虚拟按键页创建；手柄页不创建视角悬浮窗。
            if (!gamepad && WorkingRoot.MouseLook != null && WorkingRoot.MouseLook.Enabled && _viewState != 1 && mode.ShowMouseLook)
            {
                _mouseLookForm = new OverlayMouseLookForm(WorkingRoot, _bounds, _viewState == 2,
                    delegate(bool active) { HandleMouseLookActive(active); },
                    delegate { LayoutChangedInternal(); },
                    null);
                _mouseLookForm.ShowNoActivate();
            }
            for (int i = 0; i < mode.Keys.Count; i++)
            {
                VirtualKey key = mode.Keys[i];
                if (key == null) continue;
                if (_viewState == 1 && key.HideWithHidden) continue;
                OverlayKeyForm form = new OverlayKeyForm(_config, key, i, _bounds,
                    _viewState == 2, HandleKeyBinding, HandleKeyMoved,
                    HandleKeyActiveChanged, IsControlActive, mode.PadLayout);
                form.ShowNoActivate();
                _keyForms.Add(form);
            }

            string label = _viewState == 0 ? "显示" : (_viewState == 1 ? "隐藏" : "移动");
            Color accent = _viewState == 1 ? Theme.Gold : (_viewState == 2 ? Theme.Mint : Theme.Accent);
            if (WorkingRoot.DisplayKey != null && WorkingRoot.DisplayKey.Enabled)
            {
                int displaySize = Math.Max(28, (int)Math.Round(_config.DisplayKeySize * scale));
                Point displayCenter = MapPoint(_config.DisplayKeyX, _config.DisplayKeyY);
                Rectangle displayBounds = CenterBounds(displayCenter, displaySize);
                _displayForm = new OverlaySystemForm(label, accent, displayBounds, _viewState == 2,
                    DisplayAction(WorkingRoot.DisplayKey.ShortAction),
                    DisplayAction(WorkingRoot.DisplayKey.LongAction),
                    DisplayAction(WorkingRoot.DisplayKey.DoubleAction),
                    WorkingRoot.DisplayKey.LongPressMs, HandleSystemMoved);
                _displayForm.SetDragBounds(_bounds);
                _displayForm.ShowNoActivate();
            }

            if (_viewState != 1 && WorkingRoot.ModeKey != null && WorkingRoot.ModeKey.Enabled)
            {
                int modeSize = Math.Max(28, (int)Math.Round(_config.ModeKeySize * scale));
                Point modeCenter = MapPoint(_config.ModeKeyX, _config.ModeKeyY);
                Rectangle modeBounds = CenterBounds(modeCenter, modeSize);
                _modeForm = new OverlaySystemForm("模式", Theme.Violet, modeBounds, _viewState == 2,
                    ModeAction(WorkingRoot.ModeKey.ShortAction),
                    ModeAction(WorkingRoot.ModeKey.LongAction),
                    ModeAction(WorkingRoot.ModeKey.DoubleAction),
                    WorkingRoot.ModeKey.LongPressMs, HandleSystemMoved);
                _modeForm.SetDragBounds(_bounds);
                _modeForm.ShowNoActivate();
                _modeForm.BringToFront();
            }
            if (_displayForm != null) _displayForm.BringToFront();
            // 非切换类重建（尺寸变化、配置重载、隐藏态恢复）保持叠加层显示；
            // 主模式切换已在开头 CloseAllOverlay() 关闭叠加层，此处不再重建。
            if (!modeChanged) RebuildOverlayLayerIfOpen();
            // 隐藏状态下重建（如回前台瞬间的边界变化）也要立即恢复隐藏。
            if (_hidden) ApplyHidden();
        }

        private Action DisplayAction(int action)
        {
            if (action < 0) return null;
            return delegate { ExecuteDisplayAction(action); };
        }

        private Action ModeAction(int action)
        {
            if (action < 0) return null;
            return delegate { ExecuteModeAction(action); };
        }

        private void ExecuteDisplayAction(int action)
        {
            if (action == 1) SetViewState(0);
            else if (action == 2) SetViewState(1);
            else if (action == 3) SetViewState(2);
            else if (action == 4)
            {
                // 切换「拦截游戏鼠标点击」运行时开关；只拦点击、不拦移动，由输入辅助服务落地。
                Action h = ToggleBlockMouseClicks;
                if (h != null) h();
            }
            else if (action == 5)
            {
                // 切换「自动跳过剧情」总开关；状态切换与居中提示由主程序落地。
                Action h = ToggleStorySkip;
                if (h != null) h();
            }
            else if (action == 0)
            {
                if (_viewState == 0) SetViewState(1);
                else if (_viewState == 1) ShowAfterTouch();
                else SetViewState(0);
            }
        }

        private void ExecuteModeAction(int action)
        {
            if (action == 0) ToggleMenu();
            else if (action > 0 && action <= WorkingRoot.Modes.Count) ChooseMode(action - 1);
        }

        private Point MapPoint(float nx, float ny)
        {
            int x = _bounds.Left + (int)Math.Round(nx * _bounds.Width);
            int y = _bounds.Top + (int)Math.Round(ny * _bounds.Height);
            return new Point(x, y);
        }

        private Rectangle CenterBounds(Point center, int size)
        {
            int x = center.X - size / 2;
            int y = center.Y - size / 2;
            if (x < _bounds.Left) x = _bounds.Left;
            if (y < _bounds.Top) y = _bounds.Top;
            if (x + size > _bounds.Right) x = _bounds.Right - size;
            if (y + size > _bounds.Bottom) y = _bounds.Bottom - size;
            return new Rectangle(x, y, size, size);
        }

        private void CycleViewState()
        {
            SetViewState((_viewState + 1) % 3);
        }

        private void ShowAfterTouch()
        {
            if (_showTimer == null)
            {
                _showTimer = new Timer();
                _showTimer.Interval = 250;
                _showTimer.Tick += delegate
                {
                    _showTimer.Stop();
                    if (_active && !_suspended) SetViewState(0);
                };
            }
            _showTimer.Stop();
            _showTimer.Start();
        }

        private void SetViewState(int state)
        {
            if (state < 0 || state > 2) state = 0;
            _viewState = state;
            _menuOpen = false;
            // Rebuild replaces every overlay window. A delayed promoted-mouse
            // event from the same touch would otherwise hit the new window and
            // toggle the state a second time (white flash then disappear).
            OverlayFormBase.SuppressTouchMouseUntil = Environment.TickCount + 100;
            Rebuild();
        }

        private void ToggleMenu()
        {
            if (_menuOpen)
            {
                CloseMenu();
                return;
            }
            if (_viewState == 1 || _modeForm == null) return;
            ModeConfig mode = WorkingRoot.ActiveMode;
            if (mode == null) return;

            float scale = VisualScale();
            int rowH = Math.Max(28, (int)Math.Round(38 * scale));
            int width = Math.Max(140, (int)Math.Round(196 * scale));
            int height = WorkingRoot.Modes.Count * rowH + 10;
            int x = _modeForm.Right - width;
            int y = _modeForm.Bottom + 6;
            if (x < _bounds.Left + 4) x = _bounds.Left + 4;
            if (x + width > _bounds.Right - 4) x = _bounds.Right - 4 - width;
            if (y + height > _bounds.Bottom - 4) y = _modeForm.Top - height - 6;
            if (y < _bounds.Top + 4) y = _bounds.Top + 4;

            _menuForm = new OverlayModeMenuForm(WorkingRoot, new Rectangle(x, y, width, height), rowH,
                delegate(int index) { ChooseMode(index); }, delegate { _menuOpen = false; _menuForm = null; });
            _menuForm.ShowNoActivate();
            _menuOpen = true;
        }

        private void ChooseMode(int index)
        {
            CloseMenu();
            if (index < 0 || index >= WorkingRoot.Modes.Count) return;
            WorkingRoot.SelectedMode = index;
            Action h = ConfigChanged;
            if (h != null) h();
            Rebuild();
        }

        private void HandleKeyBinding(VirtualKey key, bool down, Point screenPoint)
        {
            if (key == null) return;
            // 配置了叠加呼出的主键：按下时唤出指定叠加模式按键层；未绑物理按键时 SendBinding 内部直接返回。
            if (down && key.OverlayEnabled)
            {
                ShowOverlayLayer(key.OverlayTargetMode, key.OverlayAutoHideSeconds);
            }
            Point target = new Point(_bounds.Left + _bounds.Width / 2, _bounds.Top + _bounds.Height / 2);
            InputSender.SendBinding(key.ShortPress, down, target, true);
        }

        // 呼出指定叠加模式的按键层；autoHideSeconds <= 0 表示常驻不自动隐藏。
        private void ShowOverlayLayer(int targetMode, int autoHideSeconds)
        {
            if (!_active || _suspended) return;
            List<OverlayModeConfig> overlays = WorkingRoot.OverlayModes;
            if (overlays == null || targetMode < 0 || targetMode >= overlays.Count)
            {
                HideOverlayLayer();
                return;
            }
            OverlayModeConfig overlay = overlays[targetMode];
            if (overlay == null || !overlay.Enabled || overlay.Keys == null || overlay.Keys.Count == 0)
            {
                HideOverlayLayer();
                return;
            }

            // 先关闭旧叠加层，再按新目标模式重建。
            CloseAllOverlay();
            for (int i = 0; i < overlay.Keys.Count; i++)
            {
                VirtualKey key = overlay.Keys[i];
                if (key == null) continue;
                OverlayKeyForm form = new OverlayKeyForm(_config, key, i, _bounds,
                    _viewState == 2, HandleOverlayKeyBinding, HandleKeyMoved,
                    HandleKeyActiveChanged, IsControlActive,
                    WorkingRoot.ActiveMode != null ? WorkingRoot.ActiveMode.PadLayout : 0);
                form.ShowNoActivate();
                form.BringToFront();
                _overlayKeyForms.Add(form);
            }
            _overlayActiveIndex = targetMode;
            StartOverlayHideTimer(autoHideSeconds);
        }

        private void HideOverlayLayer()
        {
            CloseAllOverlay();
        }

        // 主层 Rebuild 后叠加层保持显示：按当前呼出目标重新挂载表单并刷新位置/外观。
        private void RebuildOverlayLayerIfOpen()
        {
            if (_overlayActiveIndex < 0) return;
            List<OverlayModeConfig> overlays = WorkingRoot.OverlayModes;
            if (overlays == null || _overlayActiveIndex >= overlays.Count)
            {
                CloseAllOverlay();
                return;
            }
            OverlayModeConfig overlay = overlays[_overlayActiveIndex];
            if (overlay == null || !overlay.Enabled || overlay.Keys == null)
            {
                CloseAllOverlay();
                return;
            }
            for (int i = 0; i < overlay.Keys.Count; i++)
            {
                VirtualKey key = overlay.Keys[i];
                if (key == null) continue;
                OverlayKeyForm form = new OverlayKeyForm(_config, key, i, _bounds,
                    _viewState == 2, HandleOverlayKeyBinding, HandleKeyMoved,
                    HandleKeyActiveChanged, IsControlActive,
                    WorkingRoot.ActiveMode != null ? WorkingRoot.ActiveMode.PadLayout : 0);
                form.ShowNoActivate();
                form.BringToFront();
                _overlayKeyForms.Add(form);
            }
            // 重建视为一次活动：倒计时刷新为满周期，避免主层重建后叠加层被提前隐藏。
            ResetOverlayHideTimer();
        }

        private void StartOverlayHideTimer(int autoHideSeconds)
        {
            if (_overlayHideTimer != null)
            {
                try { _overlayHideTimer.Stop(); } catch { }
                _overlayHideTimer.Dispose();
                _overlayHideTimer = null;
            }
            if (autoHideSeconds <= 0) return;
            int ms = Math.Max(1000, Math.Min(60, autoHideSeconds) * 1000);
            _overlayHideTimer = new System.Windows.Forms.Timer();
            _overlayHideTimer.Interval = ms;
            _overlayHideTimer.Tick += delegate { HideOverlayLayer(); };
            _overlayHideTimer.Start();
        }

        // 叠加层上任意一次触摸按下都重置无操作倒计时。
        private void ResetOverlayHideTimer()
        {
            if (_overlayHideTimer != null)
            {
                try
                {
                    _overlayHideTimer.Stop();
                    _overlayHideTimer.Start();
                }
                catch { }
            }
        }

        private void HandleOverlayKeyBinding(VirtualKey key, bool down, Point screenPoint)
        {
            if (key == null) return;
            if (down) ResetOverlayHideTimer();
            Point target = new Point(_bounds.Left + _bounds.Width / 2, _bounds.Top + _bounds.Height / 2);
            InputSender.SendBinding(key.ShortPress, down, target, true);
        }

        private void HandleKeyMoved(OverlayKeyForm form)
        {
            if (form == null || form.Key == null) return;
            float size = form.Width;
            float nx = (form.Left + size / 2f - _bounds.Left) / _bounds.Width;
            float ny = (form.Top + size / 2f - _bounds.Top) / _bounds.Height;
            float mx = size / 2f / _bounds.Width;
            float my = size / 2f / _bounds.Height;
            form.Key.X = AppConfig.ClampF(nx, mx, 1f - mx);
            form.Key.Y = AppConfig.ClampF(ny, my, 1f - my);
            Action h = LayoutChanged;
            if (h != null) h();
        }

        private void HandleSystemMoved(OverlaySystemForm form)
        {
            if (form == null) return;
            float size = form.Width;
            float nx = (form.Left + size / 2f - _bounds.Left) / _bounds.Width;
            float ny = (form.Top + size / 2f - _bounds.Top) / _bounds.Height;
            float mx = size / 2f / _bounds.Width;
            float my = size / 2f / _bounds.Height;
            nx = AppConfig.ClampF(nx, mx, 1f - mx);
            ny = AppConfig.ClampF(ny, my, 1f - my);
            if (form.IsModeKey)
            {
                _config.ModeKeyX = nx;
                _config.ModeKeyY = ny;
            }
            else
            {
                _config.DisplayKeyX = nx;
                _config.DisplayKeyY = ny;
            }
            Action h = LayoutChanged;
            if (h != null) h();
        }

        private void HandleKeyActiveChanged(VirtualKey key, bool active)
        {
            if (key == null) return;
            if (active) _activeKeyIds.Add(key.Id);
            else _activeKeyIds.Remove(key.Id);
            if (!active) ReleaseSustainedForSource(key.Id);
        }

        private bool IsControlActive(string controlId)
        {
            if (string.IsNullOrEmpty(controlId)) return false;
            if (controlId == "joystick") return _joystickActive;
            if (controlId == "mouselook") return _mouseLookActive;
            return _activeKeyIds.Contains(controlId);
        }

        private void HandleJoystickActive(bool active)
        {
            _joystickActive = active;
            if (!active) ReleaseSustainedForSource("joystick");
        }

        private void HandleMouseLookActive(bool active)
        {
            _mouseLookActive = active;
            if (!active) ReleaseSustainedForSource("mouselook");
        }

        private void ReleaseSustainedForSource(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId)) return;
            if (IsControlActive(sourceId)) return;
            for (int i = 0; i < _keyForms.Count; i++)
            {
                OverlayKeyForm form = _keyForms[i];
                if (form == null || form.Key == null) continue;
                if (form.Key.FollowControlEnabled && form.Key.FollowControlId == sourceId && form.IsSustained)
                {
                    form.ReleaseSustained();
                }
            }
        }

        private void LayoutChangedInternal()
        {
            Action h = LayoutChanged;
            if (h != null) h();
        }
    }

    /// <summary>悬浮窗口基类：无边框、置顶、不激活抢焦点，统一封装触摸消息与显示辅助方法。</summary>
    internal abstract class OverlayFormBase : Form
    {
        private const int WM_TOUCH = 0x0240;
        private const uint TOUCHEVENTF_DOWN = 0x0002;
        private const uint TOUCHEVENTF_MOVE = 0x0001;
        private const uint TOUCHEVENTF_UP = 0x0004;

        protected Rectangle DragBounds;
        protected bool DragEnabled;
        protected bool IgnoreCaptureLoss;
        private bool _pressed;
        private bool _dragging;
        private int _lastEndTick;
        internal static int SuppressTouchMouseUntil;
        private Point _pressScreen;
        private Point _startLocation;
        private bool _sawTouch;
        private int _lastTouchTick = -100000;

        // 视角会话归属：视角窗按下期间持有，供诊断日志与会话隔离使用；
        // 不按窗类型一刀切拦截其他控件，保留左手摇杆 + 右手视角同时操作的多指能力。
        internal static bool ViewHoldActive;
        internal static int ViewHoldReleaseTick = -100000;

        protected virtual bool IsViewControl { get { return false; } }

        protected virtual string ControlKind { get { return GetType().Name; } }

        protected bool VisualPressed
        {
            get { return _pressed; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOUCHINPUT
        {
            public int x;
            public int y;
            public IntPtr hSource;
            public uint dwID;
            public uint dwFlags;
            public uint dwMask;
            public uint dwTime;
            public IntPtr dwExtraInfo;
            public uint cxContact;
            public uint cyContact;
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterTouchWindow(IntPtr hWnd, uint ulFlags);

        [DllImport("user32.dll")]
        private static extern bool GetTouchInputInfo(IntPtr hTouchInput, int cInputs, [In, Out] TOUCHINPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool CloseTouchInputHandle(IntPtr hTouchInput);

        protected OverlayFormBase()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Fuchsia;
            TransparencyKey = Color.Fuchsia;
            DoubleBuffered = true;
            Opacity = 0.95;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void ShowNoActivate()
        {
            double target = Opacity;
            Opacity = 0f;
            Show();
            Refresh();
            BeginInvoke(new System.Windows.Forms.MethodInvoker(delegate
            {
                try { Opacity = target; } catch { }
            }));
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { RegisterTouchWindow(Handle, 0); } catch { }
            ApplyRegion();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRegion();
        }

        protected void ApplyRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(0, 0, Width, Height);
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected abstract void OnDrawContent(Graphics g);

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            e.Graphics.Clear(BackColor);
            OnDrawContent(e.Graphics);
            if (_pressed)
            {
                RectangleF overlay = new RectangleF(1, 1, Width - 2, Height - 2);
                DrawPressed(e.Graphics, overlay);
            }
        }

        // 默认按下态：白色半透明遮罩 + 白色描边；按键子类可按反馈配置覆写。
        protected virtual void DrawPressed(Graphics g, RectangleF bounds)
        {
            using (SolidBrush mask = new SolidBrush(Color.FromArgb(72, 255, 255, 255)))
            {
                g.FillEllipse(mask, bounds);
            }
            Theme.DrawCircle(g, bounds, Color.FromArgb(210, 255, 255, 255),
                Math.Max(1.5f, Width * 0.035f));
        }

        protected virtual void OnInteractionStart(Point screenPoint)
        {
        }

        protected virtual void OnInteractionEnd(bool dragged)
        {
        }

        protected virtual void OnDragged()
        {
        }

        protected virtual void OnPointerMove(Point screenPoint)
        {
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetMessageExtraInfo();

        protected static bool IsTouchOriginMouse()
        {
            long extra = GetMessageExtraInfo().ToInt64();
            return (extra & 0xFFFFFF00L) == 0xFF515700L;
        }

        protected virtual int RestartGuardMs
        {
            get { return 20; }
        }

        private void BeginInteraction(Point screenPoint)
        {
            // 视角刚松开的短窗口内，串流软件可能补发视角会话的残留合成触点；
            // 此时拒绝其他悬浮窗新按压，避免落到摇杆触发误走位。
            // 视角进行中不拦截：保留左手摇杆 + 右手视角同时操作的多指能力。
            if (!IsViewControl && Environment.TickCount - ViewHoldReleaseTick < 150)
            {
                ConfigStore.Log("[触点] 拦截 " + ControlKind + " 视角释放残留窗口 pt=" + screenPoint.X + "," + screenPoint.Y);
                return;
            }
            if (_pressed)
            {
                ConfigStore.Log("[触点] 拦截 " + ControlKind + " 已按压 pt=" + screenPoint.X + "," + screenPoint.Y);
                return;
            }
            if (Environment.TickCount - _lastEndTick < RestartGuardMs)
            {
                ConfigStore.Log("[触点] 拦截 " + ControlKind + " 重启冷却 " + (Environment.TickCount - _lastEndTick) + "ms pt=" + screenPoint.X + "," + screenPoint.Y);
                return;
            }
            _pressed = true;
            _dragging = false;
            _pressScreen = screenPoint;
            _startLocation = Location;
            Invalidate();
            OnInteractionStart(screenPoint);
        }

        private void MoveInteraction(Point screenPoint)
        {
            if (!_pressed) return;
            OnPointerMove(screenPoint);
            if (DragEnabled)
            {
                int dx = screenPoint.X - _pressScreen.X;
                int dy = screenPoint.Y - _pressScreen.Y;
                if (!_dragging && dx * dx + dy * dy > 16 * 16) _dragging = true;
                if (_dragging)
                {
                    int x = _startLocation.X + dx;
                    int y = _startLocation.Y + dy;
                    if (x < DragBounds.Left) x = DragBounds.Left;
                    if (y < DragBounds.Top) y = DragBounds.Top;
                    if (x + Width > DragBounds.Right) x = DragBounds.Right - Width;
                    if (y + Height > DragBounds.Bottom) y = DragBounds.Bottom - Height;
                    Location = new Point(x, y);
                    Cursor = Cursors.SizeAll;
                    OnDragged();
                }
            }
        }

        protected void EndInteraction()
        {
            if (!_pressed) return;
            bool dragged = _dragging;
            _pressed = false;
            _dragging = false;
            _sawTouch = false;
            _lastEndTick = Environment.TickCount;
            Cursor = Cursors.Hand;
            Invalidate();
            OnInteractionEnd(dragged);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            // Windows can release capture before/without delivering MouseUp.
            // Treat that as a release so keys cannot remain stuck down.
            // Touch-driven forms ignore capture changes: stray mouse events
            // injected by streaming software must not kill an active touch.
            if (!IgnoreCaptureLoss && !Capture && _pressed && !_sawTouch) EndInteraction();
            base.OnMouseCaptureChanged(e);
        }

        public virtual void ForceReleaseInteraction()
        {
            if (_pressed) EndInteraction();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !_sawTouch &&
                Environment.TickCount >= SuppressTouchMouseUntil &&
                Environment.TickCount - _lastTouchTick >= 20)
            {
                Point sp = PointToScreen(e.Location);
                ConfigStore.Log("[触点] DOWN " + ControlKind + " mouse btn=" + e.Button + " pt=" + sp.X + "," + sp.Y + " extra=0x" + GetMessageExtraInfo().ToInt64().ToString("X") + " sawTouch=" + _sawTouch);
                BeginInteraction(sp);
                base.OnMouseDown(e);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_sawTouch) return;
            if (IsTouchOriginMouse() && (_sawTouch || Environment.TickCount - _lastTouchTick < 800)) return;
            MoveInteraction(PointToScreen(e.Location));
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            // Touch-driven: ignore stray mouse ups injected by streaming
            // software (they visibly move the cursor and would otherwise cut
            // the active touch gesture short). The real release arrives via
            // WM_TOUCH up. Mouse-driven use keeps the release fallback.
            if (_sawTouch) return;
            ConfigStore.Log("[触点] UP " + ControlKind + " mouse");
            EndInteraction();
            base.OnMouseUp(e);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEACTIVATE = 0x0021;
            const int WM_ACTIVATE = 0x0006;
            const int WM_NCACTIVATE = 0x0086;
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)3; // MA_NOACTIVATE
                return;
            }
            if ((m.Msg == WM_ACTIVATE || m.Msg == WM_NCACTIVATE) && m.WParam != IntPtr.Zero)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WM_TOUCH)
            {
                _lastTouchTick = Environment.TickCount;
                _sawTouch = true;
                int count = m.WParam.ToInt32() & 0xFFFF;
                if (count > 0 && count <= 16)
                {
                    TOUCHINPUT[] inputs = new TOUCHINPUT[count];
                    if (GetTouchInputInfo(m.LParam, count, inputs, Marshal.SizeOf(typeof(TOUCHINPUT))))
                    {
                        try
                        {
                            for (int i = 0; i < count; i++)
                            {
                                Point screen = new Point(inputs[i].x / 100, inputs[i].y / 100);
                                bool down = (inputs[i].dwFlags & TOUCHEVENTF_DOWN) != 0;
                                bool up = (inputs[i].dwFlags & TOUCHEVENTF_UP) != 0;
                                bool move = (inputs[i].dwFlags & TOUCHEVENTF_MOVE) != 0;
                                if (down)
                                    ConfigStore.Log("[触点] DOWN " + ControlKind + " touch id=" + inputs[i].dwID + " pt=" + screen.X + "," + screen.Y);
                                if (down) BeginInteraction(screen);
                                if (move) MoveInteraction(screen);
                                if (up)
                                {
                                    ConfigStore.Log("[触点] UP " + ControlKind + " touch id=" + inputs[i].dwID);
                                    EndInteraction();
                                }
                            }
                        }
                        finally
                        {
                            CloseTouchInputHandle(m.LParam);
                        }
                    }
                }
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }
    }

    /// <summary>单个虚拟按键悬浮窗：维护短按 / 长按 / 双击 / 联动保持长按状态机，按压时向 InputSender 派发绑定事件。</summary>
    internal class OverlayKeyForm : OverlayFormBase
    {
        private readonly AppConfig _config;
        private readonly VirtualKey _key;
        private readonly int _index;
        private readonly int _padLayout;
        private readonly Action<VirtualKey, bool, Point> _send;
        private readonly Action<OverlayKeyForm> _moved;
        private readonly Action<VirtualKey, bool> _activeChanged;
        private readonly Func<string, bool> _isSourceActive;
        private Image _image;
        private System.Threading.Timer _longTimer;
        private System.Windows.Forms.Timer _clickTimer;
        private volatile bool _shortDown;
        private volatile bool _shortSent;
        private volatile bool _longActive;
        private volatile bool _waitingClick;
        private volatile bool _doubleCandidate;
        private readonly object _longGate = new object();
        private bool _userHeld;
        private bool _sustained;
        private int _pressTick;

        protected override string ControlKind { get { return "按键"; } }

        public OverlayKeyForm(AppConfig config, VirtualKey key, int index, Rectangle bounds, bool dragEnabled,
            Action<VirtualKey, bool, Point> send, Action<OverlayKeyForm> moved,
            Action<VirtualKey, bool> activeChanged, Func<string, bool> isSourceActive, int padLayout)
        {
            _config = config;
            _key = key;
            _index = index;
            _padLayout = padLayout;
            _send = send;
            _moved = moved;
            _activeChanged = activeChanged;
            _isSourceActive = isSourceActive;
            DragBounds = bounds;
            DragEnabled = dragEnabled;
            Opacity = Math.Max(0.1f, Math.Min(1f, key.Opacity / 100f));
            int size = (int)Math.Round(key.Size * VisualScaleFor(config, bounds));
            if (size < 28) size = 28;
            Size = new Size(size, size);
            int x = bounds.Left + (int)Math.Round(key.X * bounds.Width) - size / 2;
            int y = bounds.Top + (int)Math.Round(key.Y * bounds.Height) - size / 2;
            if (x < bounds.Left) x = bounds.Left;
            if (y < bounds.Top) y = bounds.Top;
            if (x + size > bounds.Right) x = bounds.Right - size;
            if (y + size > bounds.Bottom) y = bounds.Bottom - size;
            Location = new Point(x, y);
            if (!string.IsNullOrEmpty(key.ImageFile)) _image = ConfigStore.LoadImageCopy(key.ImageFile);
            // 双击判定窗口固定 300ms；未开启双击时不创建定时器，抬起直接走单击。
            if (key.DoublePressEnabled)
            {
                _clickTimer = new System.Windows.Forms.Timer();
                _clickTimer.Interval = 300;
                _clickTimer.Tick += delegate
                {
                    if (_clickTimer != null) _clickTimer.Stop();
                    _waitingClick = false;
                    _doubleCandidate = false;
                };
            }
        }

        public VirtualKey Key
        {
            get { return _key; }
        }

        public bool HasActivePress
        {
            get { return _shortDown; }
        }

        protected override int RestartGuardMs
        {
            get { return 150; }
        }

        public bool IsSustained
        {
            get { return _sustained; }
        }

        public void ReleaseSustained()
        {
            if (_sustained && !_userHeld)
            {
                SendRelease();
            }
        }

        private static float VisualScaleFor(AppConfig config, Rectangle bounds)
        {
            float scale = Theme.Scale;
            if (scale < 0.75f) scale = 0.75f;
            if (scale > 3f) scale = 3f;
            return scale;
        }

        protected override void OnInteractionStart(Point screenPoint)
        {
            if (DragEnabled || _key == null) return;
            StopLongTimer();
            // 第二次按下：取消第一次单击的挂起等待，进入双击候选；本次不发短按。
            if (_waitingClick && _clickTimer != null)
            {
                _clickTimer.Stop();
                _waitingClick = false;
                _doubleCandidate = true;
                _shortDown = true;
                _shortSent = false;
                // 第二次按下若长时间按住，长按定时器照常启动，长按触发将打断双击判定。
                StartLongTimer();
            }
            else
            {
                _doubleCandidate = false;
                if (!_shortDown)
                {
                    _shortDown = true;
                    _shortSent = true;
                    _pressTick = Environment.TickCount;
                    ConfigStore.Log("[按键] down name=" + _key.Name + " pt=" + screenPoint.X + "," + screenPoint.Y);
                    _send(_key, true, screenPoint);
                    try { FeedbackSound.Play(_key, _config == null ? null : _config.GlobalFeedback); } catch { }
                    try { TriggerRumble(); } catch { }
                }
                StartLongTimer();
            }
            _userHeld = true;
            _longActive = false;
            _sustained = false;
            if (_activeChanged != null) _activeChanged(_key, true);
        }

        // 震动优先级：按键自身开启震动时用按键配置；否则回落全局震动配置；都关闭则静默。
        private void TriggerRumble()
        {
            if (_key == null) return;
            if (_key.RumbleEnabled)
            {
                RumbleConfig temp = new RumbleConfig();
                temp.Enabled = true;
                temp.Preset = _key.RumblePreset;
                temp.Pattern = _key.RumblePattern;
                temp.LeftMotor = _key.RumbleLeftMotor;
                temp.RightMotor = _key.RumbleRightMotor;
                temp.DurationMs = _key.RumbleDurationMs;
                RumbleService.PulseFrom(temp);
                return;
            }
            RumbleService.PulseFrom(_config == null ? null : _config.Rumble);
        }

        protected override void OnInteractionEnd(bool dragged)
        {
            if (dragged)
            {
                if (_shortDown) SendRelease();
                Action<OverlayKeyForm> h = _moved;
                if (h != null) h(this);
                return;
            }
            if (DragEnabled) return;
            StopLongTimer();
            _userHeld = false;
            ConfigStore.Log("[按键] up name=" + (_key == null ? "" : _key.Name));
            if (_activeChanged != null) _activeChanged(_key, false);

            // 长按已触发：吞掉单击与双击，仅发长按抬起。
            if (_longActive)
            {
                lock (_longGate)
                {
                    _longActive = false;
                    _shortDown = false;
                    _shortSent = false;
                    _waitingClick = false;
                    _doubleCandidate = false;
                    _sustained = false;
                    if (_clickTimer != null) _clickTimer.Stop();
                }
                _sendLong(_key, true);
                return;
            }

            // 第二次抬起：判定双击，发双击绑定（按下 + 抬起）。
            if (_doubleCandidate)
            {
                lock (_longGate)
                {
                    _doubleCandidate = false;
                    _waitingClick = false;
                    _shortDown = false;
                    _shortSent = false;
                    _sustained = false;
                    if (_clickTimer != null) _clickTimer.Stop();
                }
                SendDouble();
                return;
            }

            // 普通抬起：先发短按抬起；开启双击则挂起 300ms 等待第二次按下。
            lock (_longGate)
            {
                if (_shortDown && _shortSent)
                {
                    int elapsed = Environment.TickCount - _pressTick;
                    if (elapsed >= 0 && elapsed < 80)
                    {
                        try { System.Threading.Thread.Sleep(80 - elapsed); } catch { }
                    }
                    _send(_key, false, Cursor.Position);
                }
                _shortDown = false;
                _shortSent = false;
                _sustained = false;
                if (_clickTimer != null)
                {
                    _waitingClick = true;
                    _clickTimer.Stop();
                    _clickTimer.Start();
                }
            }
        }

        private void StartLongTimer()
        {
            StopLongTimer();
            if (_key == null || !_key.LongPressEnabled || _key.LongPress == null) return;
            int ms = Math.Max(200, _key.LongPressMs);
            _longTimer = new System.Threading.Timer(OnLongTimer, null, ms, System.Threading.Timeout.Infinite);
        }

        private void OnLongTimer(object state)
        {
            lock (_longGate)
            {
                if (!_shortDown || _longActive || _key == null || _key.LongPress == null) return;
                // Fire while the finger/mouse is still held; do not wait for release.
                bool hadShort = _shortSent;
                _shortDown = false;
                _shortSent = false;
                _longActive = true;
                _sustained = false;
                _waitingClick = false;
                _doubleCandidate = false;
                if (_clickTimer != null) _clickTimer.Stop();
                try
                {
                    if (hadShort) _send(_key, false, Cursor.Position); // release short binding
                    _sendLong(_key, false);             // press long binding
                }
                catch { }
            }
            if (IsHandleCreated)
            {
                try { BeginInvoke(new Action(delegate { Invalidate(); })); } catch { }
            }
        }

        private void StopLongTimer()
        {
            System.Threading.Timer timer = _longTimer;
            _longTimer = null;
            if (timer == null) return;
            try { timer.Dispose(); } catch { }
        }

        private void SendRelease()
        {
            StopLongTimer();
            if (_clickTimer != null) _clickTimer.Stop();
            bool wasLong;
            bool wasShort;
            lock (_longGate)
            {
                wasLong = _longActive;
                wasShort = _shortDown && _shortSent;
                _shortDown = false;
                _shortSent = false;
                _longActive = false;
                _waitingClick = false;
                _doubleCandidate = false;
                _sustained = false;
                if (wasLong) _sendLong(_key, true);
                else if (wasShort)
                {
                    int elapsed = Environment.TickCount - _pressTick;
                    if (elapsed >= 0 && elapsed < 80)
                    {
                        try { System.Threading.Thread.Sleep(80 - elapsed); } catch { }
                    }
                    _send(_key, false, Cursor.Position);
                }
            }
        }

        private void _sendLong(VirtualKey key, bool up)
        {
            // Sent without the short-press helper so the long binding can be a mouse action.
            Point target = new Point(DragBounds.Left + DragBounds.Width / 2, DragBounds.Top + DragBounds.Height / 2);
            InputSender.SendBinding(key.LongPress, !up, target, true);
        }

        // 双击绑定：按下与抬起成对发出，不经过短按辅助回调。
        private void SendDouble()
        {
            if (_key == null || _key.DoublePress == null) return;
            Point target = new Point(DragBounds.Left + DragBounds.Width / 2, DragBounds.Top + DragBounds.Height / 2);
            try
            {
                InputSender.SendBinding(_key.DoublePress, true, target, true);
                InputSender.SendBinding(_key.DoublePress, false, target, true);
            }
            catch { }
        }

        protected override void OnDrawContent(Graphics g)
        {
            float d = Width;
            Color accent = Theme.AccentFor(_index);
            RectangleF rect = new RectangleF(1, 1, Width - 2, Height - 2);
            using (LinearGradientBrush brush = new LinearGradientBrush(rect,
                Color.FromArgb(230, 16, 24, 38), Color.FromArgb(238, 7, 11, 18), 90f))
            {
                g.FillEllipse(brush, rect);
            }
            Theme.DrawCircle(g, rect, Theme.Alpha(accent, 205), Math.Max(1.5f, d * 0.03f));

            if (_image != null)
            {
                Theme.DrawImageContained(g, _image, new RectangleF(d * 0.18f, d * 0.18f, d * 0.64f, d * 0.64f));
            }
            else
            {
                string name = _key == null || string.IsNullOrEmpty(_key.Name) ? "手柄键" : _key.Name;
                int layout = _padLayout;
                string bindText = PadButtons.BindingLabel(_key.ShortPress, layout);
                bool showBind = _key != null && _key.ShortPress != null && !_key.ShortPress.IsEmpty && bindText != name;
                float namePt = Math.Max(6f, d * 0.17f);
                Font nameFont = Theme.FitFont(g, name, namePt, true, d * 0.84f, 4.6f);
                Theme.DrawText(g, name, nameFont, Color.White,
                    new RectangleF(d * 0.04f, d * 0.22f, d * 0.92f, showBind ? d * 0.34f : d * 0.56f),
                    StringAlignment.Center, StringAlignment.Center, false);
                if (showBind)
                {
                    Theme.DrawText(g, bindText, Theme.UiFont(Math.Max(5f, namePt * 0.55f)),
                        Theme.Alpha(accent, 240),
                        new RectangleF(d * 0.04f, d * 0.57f, d * 0.92f, d * 0.24f),
                        StringAlignment.Center, StringAlignment.Center, true);
                }
            }
            if (_key != null && _key.CooldownValid && _key.CooldownRemaining > 0.05)
            {
                RectangleF cdRect = new RectangleF(2, 2, Width - 4, Height - 4);
                using (SolidBrush shade = new SolidBrush(Color.FromArgb(172, 0, 0, 0)))
                {
                    g.FillEllipse(shade, cdRect);
                }
                string text = _key.CooldownRemaining >= 10
                    ? _key.CooldownRemaining.ToString("0")
                    : _key.CooldownRemaining.ToString("0.0");
                float cdPt = Math.Max(9f, d * 0.25f);
                Font cdFont = Theme.FitFont(g, text, cdPt, true, d * 0.78f, 8f);
                Theme.DrawText(g, text, cdFont, Theme.Gold,
                    new RectangleF(d * 0.05f, d * 0.18f, d * 0.90f, d * 0.64f),
                    StringAlignment.Center, StringAlignment.Center, false);
            }
            if (_sustained && !_userHeld)
            {
                Theme.DrawCircle(g, new RectangleF(2, 2, Width - 4, Height - 4), Theme.Alpha(Theme.Mint, 220),
                    Math.Max(2f, Width * 0.035f));
                Theme.FillCircle(g, new RectangleF(Width - d * 0.18f, d * 0.10f, d * 0.075f, d * 0.075f), Theme.Mint);
            }
        }

        // 按下态视觉反馈：按键自身开启时用按键配置；否则回落全局视觉配置；都未开启走默认遮罩。
        protected override void DrawPressed(Graphics g, RectangleF bounds)
        {
            int preset;
            string colorHex;
            int intensity;
            int scalePercent;
            bool active = false;
            if (_key != null && _key.VisualFeedbackEnabled)
            {
                preset = _key.VisualPreset;
                colorHex = _key.VisualColor;
                intensity = _key.VisualIntensity;
                scalePercent = _key.VisualScalePercent;
                active = true;
            }
            else if (_config != null && _config.GlobalFeedback != null && _config.GlobalFeedback.VisualEnabled)
            {
                GlobalFeedbackConfig gf = _config.GlobalFeedback;
                preset = gf.VisualPreset;
                colorHex = gf.VisualColor;
                intensity = gf.VisualIntensity;
                scalePercent = gf.VisualScalePercent;
                active = true;
            }
            else
            {
                preset = 0;
                colorHex = "#FFFFFF";
                intensity = 0;
                scalePercent = 90;
            }
            if (!active)
            {
                base.DrawPressed(g, bounds);
                return;
            }
            intensity = Math.Max(0, Math.Min(100, intensity));
            Color color = ParseFeedbackColor(colorHex);
            RectangleF r = bounds;
            switch (preset)
            {
                case 0:
                {
                    // 按下缩放：内圈按缩放比例收缩，叠加自定义色填充与描边。
                    float pct = Math.Max(80, Math.Min(100, scalePercent)) / 100f;
                    RectangleF inner = new RectangleF(
                        r.Left + r.Width * (1 - pct) / 2f,
                        r.Top + r.Height * (1 - pct) / 2f,
                        r.Width * pct, r.Height * pct);
                    using (SolidBrush fill = new SolidBrush(Theme.Alpha(color, (int)(60 * intensity / 100.0))))
                        g.FillEllipse(fill, inner);
                    Theme.DrawCircle(g, inner, Theme.Alpha(color, (int)(210 * intensity / 100.0)),
                        Math.Max(1.5f, Width * 0.035f));
                    break;
                }
                case 1:
                {
                    // 高亮闪白：白色遮罩，透明度随强度。
                    using (SolidBrush mask = new SolidBrush(
                        Color.FromArgb((int)(255 * intensity / 100.0), 255, 255, 255)))
                        g.FillEllipse(mask, r);
                    Theme.DrawCircle(g, r, Color.FromArgb(210, 255, 255, 255),
                        Math.Max(1.5f, Width * 0.035f));
                    break;
                }
                case 2:
                {
                    // 边框发光：两层外扩描边，颜色取自定义色，强度调 alpha。
                    int alpha = (int)(220 * intensity / 100.0);
                    Theme.DrawCircle(g, r, Theme.Alpha(color, alpha), Math.Max(2f, Width * 0.06f));
                    Theme.DrawCircle(g, r, Theme.Alpha(color, Math.Max(0, alpha - 70)),
                        Math.Max(1f, Width * 0.03f));
                    break;
                }
                case 3:
                {
                    // 颜色脉冲：按键内填充自定义色半透明。
                    using (SolidBrush fill = new SolidBrush(Theme.Alpha(color, (int)(120 * intensity / 100.0))))
                        g.FillEllipse(fill, r);
                    Theme.DrawCircle(g, r, Theme.Alpha(color, (int)(200 * intensity / 100.0)),
                        Math.Max(1.5f, Width * 0.035f));
                    break;
                }
                default:
                {
                    // 不透明度脉冲：整体叠一层低 alpha 自定义色，强度越高覆盖越明显。
                    using (SolidBrush fill = new SolidBrush(Theme.Alpha(color, (int)(55 * intensity / 100.0))))
                        g.FillEllipse(fill, r);
                    break;
                }
            }
        }

        private static Color ParseFeedbackColor(string hex)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return Color.White;
                string h = hex.TrimStart('#');
                if (h.Length == 6)
                {
                    int rr = Convert.ToInt32(h.Substring(0, 2), 16);
                    int gg = Convert.ToInt32(h.Substring(2, 2), 16);
                    int bb = Convert.ToInt32(h.Substring(4, 2), 16);
                    return Color.FromArgb(rr, gg, bb);
                }
            }
            catch { }
            return Color.White;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopLongTimer();
            ReleaseAllInput();
            base.OnFormClosing(e);
        }

        public void ReleaseAllInput()
        {
            StopLongTimer();
            if (_clickTimer != null) _clickTimer.Stop();
            try
            {
                if (_shortDown && _shortSent && _key != null) _send(_key, false, Cursor.Position);
                if (_longActive && _key != null) _sendLong(_key, true);
                if (_activeChanged != null && _key != null) _activeChanged(_key, false);
            }
            catch { }
            _shortDown = false;
            _shortSent = false;
            _longActive = false;
            _waitingClick = false;
            _doubleCandidate = false;
            _userHeld = false;
            _sustained = false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopLongTimer();
                if (_clickTimer != null) { _clickTimer.Stop(); _clickTimer.Dispose(); _clickTimer = null; }
                if (_image != null) { _image.Dispose(); _image = null; }
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>空白区域遮罩：开启“空白区域穿透”关闭时，用透明遮罩拦截按键以外的鼠标点击。</summary>
    internal class OverlayClickShield : Form
    {
        public OverlayClickShield(Rectangle bounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            BackColor = Color.Black;
            Opacity = 0.01;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return cp;
            }
        }
    }

    // 屏幕正中短时汉字提示浮层：鼠标/触控穿透（WS_EX_TRANSPARENT），不拦截游戏操作；
    // 圆角半透明深色底 + 居中粗白字，由 RuntimeOverlay.ShowCenteredNotice 复用并定时隐藏。
    /// <summary>屏幕居中文字提示窗：开关切换、功能提示等短暂显示在游戏画面正中央，不抢焦点。</summary>
    internal class OverlayNoticeForm : Form
    {
        private string _text = "";

        public OverlayNoticeForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Fuchsia;
            TransparencyKey = Color.Fuchsia;
            DoubleBuffered = true;
            Opacity = 0.92;
            Cursor = Cursors.Default;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void SetText(string text)
        {
            _text = text == null ? "" : text;
            Invalidate();
        }

        public void ShowNoActivate()
        {
            double target = Opacity;
            Opacity = 0f;
            Show();
            Refresh();
            BeginInvoke(new System.Windows.Forms.MethodInvoker(delegate
            {
                try { Opacity = target; } catch { }
            }));
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT：鼠标/触控穿透
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            RectangleF rect = new RectangleF(2, 2, Width - 4, Height - 4);
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(215, 16, 22, 34)))
                Theme.FillRounded(e.Graphics, rect, Theme.S(16), bg);
            Theme.DrawRounded(e.Graphics, rect, Theme.S(16), Theme.StrokeSoft, 1f);
            Theme.DrawText(e.Graphics, _text, Theme.Semibold(15f), Color.White,
                new RectangleF(Theme.S(18), 0, Width - Theme.S(36), Height),
                StringAlignment.Center, StringAlignment.Center, true);
        }
    }

    /// <summary>系统按键悬浮窗：显示键（显示 / 隐藏 / 移动布局）与模式键（呼出模式菜单），各自支持短按 / 长按 / 双击。</summary>
    internal class OverlaySystemForm : OverlayFormBase
    {
        private readonly string _label;
        private readonly Color _accent;
        private readonly Action _click;
        private readonly Action _longClick;
        private readonly Action _doubleClick;
        private readonly int _longMs;
        private readonly Action<OverlaySystemForm> _moved;
        private readonly bool _isModeKey;
        private Timer _longTimer;
        private Timer _clickTimer;
        private bool _longFired;
        private bool _waitingClick;
        private bool _doubleCandidate;
        private int _lastSingleTick;

        protected override string ControlKind { get { return "系统"; } }

        public OverlaySystemForm(string label, Color accent, Rectangle bounds, bool dragEnabled,
            Action click, Action longClick, Action doubleClick, int longMs, Action<OverlaySystemForm> moved)
        {
            _label = label;
            _accent = accent;
            _click = click;
            _longClick = longClick;
            _doubleClick = doubleClick;
            _longMs = longMs;
            _moved = moved;
            DragBounds = new Rectangle(0, 0, 1, 1);
            DragEnabled = dragEnabled;
            Bounds = bounds;
            _isModeKey = label == "模式";
            if (longClick != null)
            {
                _longTimer = new Timer();
                _longTimer.Interval = Math.Max(200, longMs);
                _longTimer.Tick += delegate
                {
                    _longTimer.Stop();
                    if (DragEnabled) return;
                    _longFired = true;
                    _doubleCandidate = false;
                    _waitingClick = false;
                    if (_clickTimer != null) _clickTimer.Stop();
                    Action h = _longClick;
                    if (h != null) h();
                };
            }
            if (doubleClick != null)
            {
                _clickTimer = new Timer();
                _clickTimer.Interval = 300;
                _clickTimer.Tick += delegate
                {
                    _clickTimer.Stop();
                    _waitingClick = false;
                    _doubleCandidate = false;
                    Action h = _click;
                    if (h != null) h();
                };
            }
        }

        public bool IsModeKey
        {
            get { return _isModeKey; }
        }

        public void SetDragBounds(Rectangle bounds)
        {
            DragBounds = bounds;
        }

        protected override void OnInteractionStart(Point screenPoint)
        {
            if (DragEnabled) return;
            _longFired = false;
            if (_waitingClick && _clickTimer != null)
            {
                _clickTimer.Stop();
                _waitingClick = false;
                _doubleCandidate = true;
            }
            else
            {
                _doubleCandidate = false;
            }
            if (_longTimer != null)
            {
                _longTimer.Stop();
                _longTimer.Start();
            }
            base.OnInteractionStart(screenPoint);
        }

        protected override void OnInteractionEnd(bool dragged)
        {
            if (dragged)
            {
                if (_longTimer != null) _longTimer.Stop();
                Action<OverlaySystemForm> h = _moved;
                if (h != null) h(this);
                return;
            }
            if (_longTimer != null) _longTimer.Stop();
            if (_longFired) return;
            if (_doubleClick != null && _doubleCandidate)
            {
                _doubleCandidate = false;
                _waitingClick = false;
                if (_clickTimer != null) _clickTimer.Stop();
                Action h = _doubleClick;
                if (h != null) h();
                return;
            }
            if (_clickTimer != null)
            {
                _waitingClick = true;
                _clickTimer.Stop();
                _clickTimer.Start();
                return;
            }
            Action single = _click;
            if (single != null && Environment.TickCount - _lastSingleTick >= 300)
            {
                _lastSingleTick = Environment.TickCount;
                single();
            }
        }

        protected override void OnDrawContent(Graphics g)
        {
            float d = Width;
            RectangleF rect = new RectangleF(1, 1, Width - 2, Height - 2);
            using (LinearGradientBrush brush = new LinearGradientBrush(rect,
                Theme.Blend(Color.FromArgb(17, 24, 37), _accent, 0.22f),
                Color.FromArgb(8, 12, 20), 90f))
            {
                g.FillEllipse(brush, rect);
            }
            Theme.DrawCircle(g, rect, Theme.Alpha(_accent, 220), Math.Max(1.5f, d * 0.03f));
            float namePt = Math.Max(7f, d * 0.2f);
            Theme.DrawText(g, _label, Theme.Semibold(namePt), Color.White,
                new RectangleF(d * 0.05f, 0, d * 0.9f, d), StringAlignment.Center, StringAlignment.Center, false);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_longTimer != null) _longTimer.Stop();
            if (_clickTimer != null) _clickTimer.Stop();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_longTimer != null) _longTimer.Dispose();
                if (_clickTimer != null) _clickTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>摇杆悬浮窗：左 / 右摇杆共用，中心弹簧回弹、死区与灵敏度计算，输出 WASD 方向或虚拟手柄摇杆轴。</summary>
    internal class OverlayJoystickForm : OverlayFormBase
    {
        private readonly AppConfig _config;
        private readonly JoystickConfig _joy;
        private readonly bool _isRight;
        private readonly Action<bool> _activeChanged;
        private readonly Action _moved;
        private float _knobX;
        private float _knobY;
        private bool _engaged;
        private bool _collapsed;
        private bool _up;
        private bool _down;
        private bool _left;
        private bool _right;
        // 按键页离散方向键持有态：跟踪上/下/左/右各自是否已按下，状态变化时才补发 up/down。
        private bool _heldUp;
        private bool _heldDown;
        private bool _heldLeft;
        private bool _heldRight;
        private Timer _springTimer;
        private Timer _hideTimer;

        protected override string ControlKind { get { return "摇杆"; } }

        public OverlayJoystickForm(AppConfig config, JoystickConfig joy, Rectangle bounds, bool dragEnabled,
            bool isRight, Action<bool> activeChanged, Action moved)
        {
            _config = config;
            _joy = joy;
            _isRight = isRight;
            _activeChanged = activeChanged;
            _moved = moved;
            if (_joy == null) return;
            DragEnabled = dragEnabled;
            DragBounds = bounds;
            JoystickConfig j = _joy;
            int size = Math.Max(80, (int)Math.Round(j.Size * Theme.Scale));
            Size = new Size(size, size);
            int x = bounds.Left + (int)Math.Round(j.X * bounds.Width) - size / 2;
            int y = bounds.Top + (int)Math.Round(j.Y * bounds.Height) - size / 2;
            if (x < bounds.Left) x = bounds.Left;
            if (y < bounds.Top) y = bounds.Top;
            if (x + size > bounds.Right) x = bounds.Right - size;
            if (y + size > bounds.Bottom) y = bounds.Bottom - size;
            Location = new Point(x, y);
            Opacity = Math.Max(0.1f, Math.Min(1f, j.Opacity / 100f));
            _springTimer = new Timer();
            _springTimer.Interval = 15;
            _springTimer.Tick += SpringTick;
            _hideTimer = new Timer();
            _hideTimer.Tick += HideTick;
            RestartHideTimer();
        }

        private void RestartHideTimer()
        {
            if (_hideTimer == null || _joy == null) return;
            _hideTimer.Stop();
            if (_joy.AutoHideSeconds <= 0)
            {
                _collapsed = false;
                return;
            }
            _hideTimer.Interval = Math.Max(200, _joy.AutoHideSeconds * 1000);
            _hideTimer.Start();
        }

        private void HideTick(object sender, EventArgs e)
        {
            if (_hideTimer != null) _hideTimer.Stop();
            if (_engaged) return;
            _collapsed = true;
            ApplyVisualRegion();
            Invalidate();
        }

        private void RestoreOuter()
        {
            _collapsed = false;
            ApplyVisualRegion();
            RestartHideTimer();
            Invalidate();
        }

        protected override void OnInteractionStart(Point screenPoint)
        {
            if (_collapsed) RestoreOuter();
            if (!DragEnabled)
            {
                if (_springTimer != null) _springTimer.Stop();
                if (_hideTimer != null) _hideTimer.Stop();
                _engaged = true;
                ConfigStore.Log("[摇杆] engage pt=" + screenPoint.X + "," + screenPoint.Y);
                if (_activeChanged != null) _activeChanged(true);
                UpdateDirection(screenPoint);
            }
            base.OnInteractionStart(screenPoint);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyVisualRegion();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyVisualRegion();
        }

        private void ApplyVisualRegion()
        {
            if (Width <= 0 || Height <= 0 || _joy == null) return;
            float outer = Math.Max(20, Math.Min(Width, _joy.OuterSize * Theme.Scale));
            float inner = Math.Max(20, Math.Min(Width, _joy.InnerSize * Theme.Scale));
            float d = _collapsed ? inner : outer;
            RectangleF rect = new RectangleF((Width - d) / 2f, (Height - d) / 2f, d, d);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(rect);
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        protected override void OnPointerMove(Point screenPoint)
        {
            if (!DragEnabled) UpdateDirection(screenPoint);
        }

        protected override void OnInteractionEnd(bool dragged)
        {
            if (dragged)
            {
                float size = Width;
                float nx = (Left + size / 2f - DragBounds.Left) / DragBounds.Width;
                float ny = (Top + size / 2f - DragBounds.Top) / DragBounds.Height;
                float mx = size / 2f / DragBounds.Width;
                float my = size / 2f / DragBounds.Height;
                if (_joy != null)
                {
                    _joy.X = AppConfig.ClampF(nx, mx, 1f - mx);
                    _joy.Y = AppConfig.ClampF(ny, my, 1f - my);
                }
                if (_moved != null) _moved();
                return;
            }
            _engaged = false;
            ConfigStore.Log("[摇杆] release");
            ReleaseDirections();
            if (_activeChanged != null) _activeChanged(false);
            StartSpring();
            RestartHideTimer();
            Invalidate();
        }

        public override void ForceReleaseInteraction()
        {
            // 重建销毁旧窗体前补发摇杆方向键 up，避免移动键残留按下导致人物持续走位；
            // 未处于按住态时直接返回，不干扰自动收起外圈等视觉状态。
            if (!_engaged) return;
            _engaged = false;
            ReleaseDirections();
            if (_activeChanged != null) _activeChanged(false);
            if (_springTimer != null) _springTimer.Stop();
            if (_hideTimer != null) _hideTimer.Stop();
        }

        private void UpdateDirection(Point screenPoint)
        {
            float size = Width;
            float cx = Left + size / 2f;
            float cy = Top + size / 2f;
            JoystickConfig joy = _joy;
            if (joy == null) return;
            float outerD = Math.Min(Width, joy.OuterSize * Theme.Scale);
            float innerD = Math.Min(outerD, joy.InnerSize * Theme.Scale);
            float travel = Math.Max(1f, outerD / 2f - innerD / 2f - outerD * 0.06f);
            float rawX = (screenPoint.X - cx) / travel;
            float rawY = (screenPoint.Y - cy) / travel;
            float rawLen = (float)Math.Sqrt(rawX * rawX + rawY * rawY);
            if (rawLen > 1f)
            {
                rawX /= rawLen;
                rawY /= rawLen;
            }
            float outLen;
            float dx;
            float dy;
            JoystickMath.Apply(joy, rawX, rawY, out dx, out dy, out outLen);
            _knobX = dx;
            _knobY = dy;
            if (outLen < 0.02f)
            {
                ReleaseDirections();
                Invalidate();
                return;
            }
            float angle = (float)(Math.Atan2(dx, -dy) * 180.0 / Math.PI);
            int sector = (int)Math.Round(angle / 45.0);
            if (sector < 0) sector += 8;
            if (sector > 7) sector -= 8;
            _up = sector == 0 || sector == 1 || sector == 7;
            _right = sector == 1 || sector == 2 || sector == 3;
            _down = sector == 3 || sector == 4 || sector == 5;
            _left = sector == 5 || sector == 6 || sector == 7;
            // 按键页：左摇杆按象限离散触发上/下/左/右绑定的键盘/鼠标；手柄页：输出连续摇杆轴。
            if (IsKeyMode()) SendDirectional();
            else SendStick(dx, dy);
            Invalidate();
        }

        // 左摇杆在虚拟按键页走离散方向绑定；右摇杆与手柄页走连续轴。
        private bool IsKeyMode()
        {
            return !_isRight && _config != null && _config.UiPadSide != 1;
        }

        private void SendDirectional()
        {
            SendDir(ref _heldUp, _up, _joy == null ? null : _joy.Up);
            SendDir(ref _heldDown, _down, _joy == null ? null : _joy.Down);
            SendDir(ref _heldLeft, _left, _joy == null ? null : _joy.Left);
            SendDir(ref _heldRight, _right, _joy == null ? null : _joy.Right);
        }

        private void SendDir(ref bool held, bool now, KeyBinding binding)
        {
            if (now == held) return;
            held = now;
            if (binding != null && binding.Kind != BindingKind.None)
            {
                try { InputSender.SendBinding(binding, now, Point.Empty, false); } catch { }
            }
        }

        private void ReleaseDirections()
        {
            _up = false; _down = false; _left = false; _right = false;
            if (IsKeyMode())
            {
                SendDirectional();
            }
            else
            {
                SendStick(0f, 0f);
            }
        }

        private void SendStick(float x, float y)
        {
            try
            {
                if (_isRight) GamepadService.SetRightStick(x, y);
                else GamepadService.SetLeftStick(x, y);
            }
            catch { }
        }

        private void StartSpring()
        {
            if (_springTimer == null) return;
            _springTimer.Stop();
            _springTimer.Start();
        }

        private void SpringTick(object sender, EventArgs e)
        {
            if (_engaged)
            {
                _springTimer.Stop();
                return;
            }
            _knobX *= 0.72f;
            _knobY *= 0.72f;
            if (Math.Abs(_knobX) < 0.01f && Math.Abs(_knobY) < 0.01f)
            {
                _knobX = 0f;
                _knobY = 0f;
                _springTimer.Stop();
            }
            Invalidate();
        }

        protected override void OnDrawContent(Graphics g)
        {
            float d = Width;
            JoystickConfig joy = _joy;
            if (joy == null) return;
            float outerD = Math.Min(d, joy.OuterSize * Theme.Scale);
            float innerD = Math.Min(outerD, joy.InnerSize * Theme.Scale);
            PointF center = new PointF(d / 2f, d / 2f);
            RectangleF outer = new RectangleF(center.X - outerD / 2f, center.Y - outerD / 2f, outerD, outerD);
            Color accent = Theme.Mint;
            bool active = _engaged;
            bool collapsed = _collapsed && !_engaged;

            if (!collapsed)
            {
                using (LinearGradientBrush baseBrush = new LinearGradientBrush(outer,
                    Color.FromArgb(228, 18, 27, 43), Color.FromArgb(238, 7, 11, 18), 90f))
                {
                    g.FillEllipse(baseBrush, outer);
                }
                Theme.DrawCircle(g, outer, Theme.Alpha(accent, active ? 240 : 145), Math.Max(1.5f, outerD * 0.018f));
                Theme.DrawCircle(g, new RectangleF(outer.X + outerD * 0.06f, outer.Y + outerD * 0.06f,
                    outerD * 0.88f, outerD * 0.88f), Color.FromArgb(active ? 55 : 26, 255, 255, 255),
                    Math.Max(1f, outerD * 0.007f));

                // tick marks
                for (int i = 0; i < 8; i++)
                {
                    double ang = i * Math.PI / 4.0;
                    float r1 = outerD * 0.37f;
                    float r2 = outerD * 0.43f;
                    using (Pen p = new Pen(Color.FromArgb(active ? 95 : 55, 200, 220, 240), Math.Max(1f, outerD * 0.008f)))
                    {
                        g.DrawLine(p, center.X + (float)Math.Sin(ang) * r1, center.Y - (float)Math.Cos(ang) * r1,
                            center.X + (float)Math.Sin(ang) * r2, center.Y - (float)Math.Cos(ang) * r2);
                    }
                }

                Font keyFont = Theme.UiFont(Math.Max(5f, outerD * 0.088f), false);
                DrawJoystickLabel(g, "↑", new RectangleF(center.X - outerD * 0.11f, outer.Y + outerD * 0.075f, outerD * 0.22f, outerD * 0.14f),
                    keyFont, active, _up);
                DrawJoystickLabel(g, "↓", new RectangleF(center.X - outerD * 0.11f, outer.Bottom - outerD * 0.215f, outerD * 0.22f, outerD * 0.14f),
                    keyFont, active, _down);
                DrawJoystickLabel(g, "←", new RectangleF(outer.X + outerD * 0.075f, center.Y - outerD * 0.07f, outerD * 0.22f, outerD * 0.14f),
                    keyFont, active, _left);
                DrawJoystickLabel(g, "→", new RectangleF(outer.Right - outerD * 0.295f, center.Y - outerD * 0.07f, outerD * 0.22f, outerD * 0.14f),
                    keyFont, active, _right);
            }

            float travel = collapsed ? 0f : Math.Max(1f, outerD / 2f - innerD / 2f - outerD * 0.06f);
            float kx = collapsed ? 0f : _knobX;
            float ky = collapsed ? 0f : _knobY;
            PointF knobCenter = new PointF(center.X + kx * travel, center.Y + ky * travel);
            RectangleF knob = new RectangleF(knobCenter.X - innerD / 2f, knobCenter.Y - innerD / 2f, innerD, innerD);
            if (active)
            {
                using (GraphicsPath halo = new GraphicsPath())
                {
                    halo.AddEllipse(knob.X - innerD * 0.14f, knob.Y - innerD * 0.14f,
                        knob.Width + innerD * 0.28f, knob.Height + innerD * 0.28f);
                    using (PathGradientBrush glow = new PathGradientBrush(halo))
                    {
                        glow.CenterColor = Color.FromArgb(85, accent);
                        glow.SurroundColors = new Color[] { Color.FromArgb(0, accent) };
                        g.FillPath(glow, halo);
                    }
                }
            }
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(100, 0, 0, 0)))
            {
                g.FillEllipse(shadow, new RectangleF(knob.X, knob.Y + innerD * 0.08f, innerD, innerD));
            }
            using (LinearGradientBrush knobBrush = new LinearGradientBrush(knob,
                Theme.Blend(accent, Color.White, 0.35f), Theme.Blend(accent, Color.Black, 0.38f), 90f))
            {
                g.FillEllipse(knobBrush, knob);
            }
            Theme.DrawCircle(g, knob, Color.FromArgb(240, 235, 255, 252), Math.Max(1.4f, innerD * 0.026f));
            Theme.FillCircle(g, new RectangleF(knobCenter.X - innerD * 0.08f, knobCenter.Y - innerD * 0.08f,
                innerD * 0.16f, innerD * 0.16f), Color.FromArgb(210, 255, 255, 255));
        }

        private void DrawJoystickLabel(Graphics g, string text, RectangleF rect, Font font, bool active, bool pressed)
        {
            if (string.IsNullOrEmpty(text)) text = "—";
            Theme.DrawText(g, text, font, pressed ? Color.White : Color.FromArgb(active ? 190 : 120, 210, 228, 244),
                rect, StringAlignment.Center, StringAlignment.Center, true);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ReleaseDirections();
            if (_activeChanged != null) _activeChanged(false);
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _springTimer != null) _springTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>视角操控悬浮窗：按住后滑动转换为相对鼠标移动，按配置绑定左 / 右 / 中 / 侧键，支持消息注入模式。</summary>
    internal class OverlayMouseLookForm : OverlayFormBase
    {
        private readonly AppConfig _config;
        private readonly Action<bool> _activeChanged;
        private readonly Action _moved;
        private readonly Action<bool> _viewDragging;
        private bool _held;
        private Point _lastPoint;
        private float _visualDx;
        private float _visualDy;
        private bool _messageMode;
        private IntPtr _messageTarget;
        private int _messageButton;
        private int _virtualX;
        private int _virtualY;
        private bool _cursorHidden;
        private int _lastMoveLogTick;

        protected override bool IsViewControl { get { return true; } }
        protected override string ControlKind { get { return "视角"; } }

        public OverlayMouseLookForm(AppConfig config, Rectangle bounds, bool dragEnabled,
            Action<bool> activeChanged, Action moved, Action<bool> viewDragging)
        {
            _config = config;
            _activeChanged = activeChanged;
            _moved = moved;
            _viewDragging = viewDragging;
            IgnoreCaptureLoss = true;
            DragEnabled = dragEnabled;
            DragBounds = bounds;
            MouseLookConfig look = config.MouseLook;
            int w = Math.Max(60, (int)Math.Round(look.Width * Theme.Scale));
            int h = Math.Max(60, (int)Math.Round(look.Height * Theme.Scale));
            Size = new Size(w, h);
            int x = bounds.Left + (int)Math.Round(look.X * bounds.Width) - w / 2;
            int y = bounds.Top + (int)Math.Round(look.Y * bounds.Height) - h / 2;
            if (x < bounds.Left) x = bounds.Left;
            if (y < bounds.Top) y = bounds.Top;
            if (x + w > bounds.Right) x = bounds.Right - w;
            if (y + h > bounds.Bottom) y = bounds.Bottom - h;
            Location = new Point(x, y);
            Opacity = Math.Max(0.1f, Math.Min(1f, look.Opacity / 100f));
        }

        protected override void OnInteractionStart(Point screenPoint)
        {
            if (DragEnabled) return;
            BeginHold(screenPoint);
        }

        public override void ForceReleaseInteraction()
        {
            // 模式切换重建销毁旧窗体前，先结束视角拖拽态并补发右键 up / 结束消息拖拽，
            // 避免游戏侧右键残留按下、新视角窗体等待新拖拽而表现为视角卡死。
            ReleaseHold();
        }

        private void BeginHold(Point screenPoint)
        {
            _held = true;
            _lastPoint = screenPoint;
            ViewHoldActive = true;
            if (_viewDragging != null) _viewDragging(true);
            KeyBinding lookButton = _config.MouseLook.Button;
            _messageButton = lookButton == null ? -1 : (lookButton.Kind == BindingKind.MouseButton ? lookButton.Code : -1);
            _messageMode = _config.MouseLook.MessageInjection;
            ConfigStore.Log("[视角] Begin mode=" + (_messageMode ? "消息" : "物理") + " button=" + _messageButton + " pt=" + screenPoint.X + "," + screenPoint.Y);
            // 两个模式都保持鼠标会话被视角窗独占：手指/光标滑出圆钮后 move 仍投递到视角窗，
            // 不会把残留帧丢给下层摇杆/按键窗，也避免物理模式转出圆钮后视角断流。
            Capture = true;
            // 绑定为「无」：先兜底抬起可能残留的物理/串流合成鼠标左键，避免残留 down 在
            // 游戏侧表现为长按普攻/技能；本次会话自身也不按任何鼠标键。
            if (_messageButton < 0) InputSender.ReleaseMouseButtons();

            if (_messageMode)
            {
                BeginMessageDrag();
                if (_activeChanged != null) _activeChanged(true);
                return;
            }
            // 绑定为「无」(_messageButton < 0) 时只做相对移动视角，不挪光标、不按下任何鼠标键。
            if (_messageButton >= 0)
            {
                Point center = new Point(DragBounds.Left + DragBounds.Width / 2,
                    DragBounds.Top + DragBounds.Height / 2);
                InputSender.SetMousePosition(center.X, center.Y);
                InputSender.SendMouseButton(_messageButton, false, "视角");
            }
            if (_activeChanged != null) _activeChanged(true);
        }

        private void ApplyViewDelta(int dx, int dy)
        {
            float sensitivity = Math.Max(1f, Math.Min(20f, _config.MouseLook.Sensitivity / 100f));
            int mx = (int)Math.Round(dx * sensitivity);
            int my = (int)Math.Round(dy * sensitivity);
            if (Environment.TickCount - _lastMoveLogTick >= 120)
            {
                _lastMoveLogTick = Environment.TickCount;
                ConfigStore.Log("[视角] 位移 dx=" + dx + " dy=" + dy + " mx=" + mx + " my=" + my + " mode=" + (_messageMode ? "消息" : "物理"));
            }
            if (_messageMode)
            {
                MoveMessageDrag(mx, my);
            }
            else if (mx != 0 || my != 0)
            {
                InputSender.MoveMouseRelative(mx, my);
            }

            float limit = Math.Max(20f, Math.Min(Width, Height) * 0.34f);
            _visualDx = Math.Max(-limit, Math.Min(limit, dx));
            _visualDy = Math.Max(-limit, Math.Min(limit, dy));
            Invalidate();
        }

        private void ReleaseLatched()
        {
        }

        protected override void OnPointerMove(Point screenPoint)
        {
            if (!_held || DragEnabled) return;
            int dx = screenPoint.X - _lastPoint.X;
            int dy = screenPoint.Y - _lastPoint.Y;
            _lastPoint = screenPoint;
            if (dx == 0 && dy == 0) return;
            ApplyViewDelta(dx, dy);
        }

        protected override void OnInteractionEnd(bool dragged)
        {
            if (dragged)
            {
                float w = Width;
                float h = Height;
                float nx = (Left + w / 2f - DragBounds.Left) / DragBounds.Width;
                float ny = (Top + h / 2f - DragBounds.Top) / DragBounds.Height;
                _config.MouseLook.X = AppConfig.ClampF(nx, w / 2f / DragBounds.Width, 1f - w / 2f / DragBounds.Width);
                _config.MouseLook.Y = AppConfig.ClampF(ny, h / 2f / DragBounds.Height, 1f - h / 2f / DragBounds.Height);
                if (_moved != null) _moved();
                return;
            }
            ReleaseHold();
        }

        private void ReleaseHold()
        {
            if (!_held) return;
            _held = false;
            ViewHoldActive = false;
            ViewHoldReleaseTick = Environment.TickCount;
            Capture = false;
            // 绑定为「无」的会话结束同样兜底抬起全部鼠标键，确保不残留长按态。
            if (_messageButton < 0) InputSender.ReleaseMouseButtons();
            ConfigStore.Log("[视角] Release mode=" + (_messageMode ? "消息" : "物理") + " button=" + _messageButton);
            if (_viewDragging != null) _viewDragging(false);
            if (_messageMode)
            {
                EndMessageDrag();
                _messageMode = false;
                if (_activeChanged != null) _activeChanged(false);
                _visualDx = 0f;
                _visualDy = 0f;
                Invalidate();
                return;
            }
            if (_messageButton >= 0)
            {
                try { InputSender.SendMouseButton(_messageButton, true, "视角"); } catch { }
            }
            if (_activeChanged != null) _activeChanged(false);
            _visualDx = 0f;
            _visualDy = 0f;
            Invalidate();
            if (!_cursorHidden) return;
            try { ShowCursor(true); _cursorHidden = false; } catch { }
        }

        private IntPtr FindGameWindow()
        {
            try
            {
                string configured = _config == null ? "" : (_config.GameProcessName ?? "");
                string[] names = configured.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                List<string> list = new List<string>();
                for (int i = 0; i < names.Length; i++) if (!list.Contains(names[i])) list.Add(names[i]);
                if (!list.Contains("yysls")) list.Add("yysls");
                for (int i = 0; i < list.Count; i++)
                {
                    Process[] ps = null;
                    try { ps = Process.GetProcessesByName(list[i]); } catch { }
                    if (ps == null) continue;
                    for (int j = 0; j < ps.Length; j++)
                    {
                        try
                        {
                            if (ps[j].MainWindowHandle != IntPtr.Zero)
                            {
                                IntPtr h = ps[j].MainWindowHandle;
                                for (int k = 0; k < ps.Length; k++) ps[k].Dispose();
                                return h;
                            }
                        }
                        catch { }
                    }
                    for (int j = 0; j < ps.Length; j++) try { ps[j].Dispose(); } catch { }
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        private void BeginMessageDrag()
        {
            _messageTarget = IntPtr.Zero;
            _virtualX = 0;
            _virtualY = 0;
            try
            {
                POINT pt = new POINT();
                pt.X = DragBounds.Left + DragBounds.Width / 2;
                pt.Y = DragBounds.Top + DragBounds.Height / 2;
                IntPtr h = FindGameWindow();
                if (h == IntPtr.Zero)
                {
                    h = WindowFromPoint(pt);
                    if (h != IntPtr.Zero) h = GetAncestor(h, 2); // GA_ROOT
                }
                if (h != IntPtr.Zero)
                {
                    _messageTarget = h;
                    POINT client = pt;
                    ScreenToClient(h, ref client);
                    _virtualX = client.X;
                    _virtualY = client.Y;
                    if (_messageButton >= 0)
                        PostMessage(h, ButtonMessage(false), ButtonWParam(), MakeLParam(_virtualX, _virtualY));
                }
            }
            catch { }
            // 消息模式下仍需向游戏发送鼠标按下，让游戏进入拖拽态；
            // 但不再调用 SetMousePosition 物理挪光标——避免与剧情跳过的 SetCursorPos 互相污染。
            if (_messageButton >= 0)
            {
                try { InputSender.SendMouseButton(_messageButton, false, "视角"); } catch { }
            }
            if (!_cursorHidden)
            {
                try { ShowCursor(false); _cursorHidden = true; } catch { }
            }
        }

        private void EndMessageDrag()
        {
            if (_messageTarget != IntPtr.Zero)
            {
                try
                {
                    if (_messageButton >= 0)
                        PostMessage(_messageTarget, ButtonMessage(true), ButtonWParam(), MakeLParam(_virtualX, _virtualY));
                }
                catch { }
                _messageTarget = IntPtr.Zero;
            }
            // 必须补发物理鼠标抬起，否则游戏侧鼠标按钮状态残留按下。
            if (_messageButton >= 0)
            {
                try { InputSender.SendMouseButton(_messageButton, true, "视角"); } catch { }
            }
            if (_cursorHidden)
            {
                try { ShowCursor(true); _cursorHidden = false; } catch { }
            }
        }

        private void MoveMessageDrag(int dx, int dy)
        {
            // 双通道：PostMessage 维持游戏侧"右键按住"状态（很多游戏只看这个判定拖拽），
            // 同时物理相对位移真正驱动镜头旋转。注意：这里只用 SendInput 的相对位移
            // （MOUSEEVENTF_MOVE 不带 ABSOLUTE），不会被剧情跳过的 SetCursorPos 污染。
            // 绑定为「无」时不投递窗口鼠标消息，避免游戏把窗口内移动误判为按压拖拽；
            // 仅靠物理相对位移转动镜头。
            if (_messageButton >= 0 && _messageTarget != IntPtr.Zero)
            {
                _virtualX += dx;
                _virtualY += dy;
                try
                {
                    RECT rc;
                    if (GetClientRect(_messageTarget, out rc))
                    {
                        _virtualX = Math.Max(0, Math.Min(rc.Right - 1, _virtualX));
                        _virtualY = Math.Max(0, Math.Min(rc.Bottom - 1, _virtualY));
                    }
                    PostMessage(_messageTarget, 0x0203, (IntPtr)MoveWParam(), MakeLParam(_virtualX, _virtualY));
                }
                catch { }
            }
            InputSender.MoveMouseRelative(dx, dy);
        }

        private int MoveWParam()
        {
            if (_messageButton < 0) return 0;
            if (_messageButton == 0) return 1;   // 左键按住
            if (_messageButton == 1) return 2;   // 右键按住
            if (_messageButton == 2) return 16;  // 中键按住
            return 1;
        }

        private int ButtonMessage(bool up)
        {
            // 按钮号与 InputSender 口径一致：0=左键 1=右键 2=中键 3/4=X 键；
            // 窗口消息：左键 0x0201/0202，中键 0x0204/0205，右键 0x0207/0208，
            // X 键 0x020B/020C。注意右/中消息号不可互换，否则游戏收不到右键按下、
            // 不进入视角拖拽态。
            if (_messageButton == 0) return up ? 0x0202 : 0x0201;
            if (_messageButton == 2) return up ? 0x0205 : 0x0204;
            if (_messageButton == 1) return up ? 0x0208 : 0x0207;
            return up ? 0x020C : 0x020B;
        }

        private IntPtr ButtonWParam()
        {
            if (_messageButton == 3) return (IntPtr)(1 << 16);
            if (_messageButton == 4) return (IntPtr)(2 << 16);
            return IntPtr.Zero;
        }

        private static IntPtr MakeLParam(int x, int y)
        {
            return (IntPtr)((y << 16) | (x & 0xFFFF));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern int ShowCursor(bool show);
        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyVisualRegion();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyVisualRegion();
        }

        private void ApplyVisualRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = BuildLookPath(new RectangleF(0, 0, Width, Height),
                _config.MouseLook.Shape, Math.Max(8f, Math.Min(Width, Height) * 0.12f)))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
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

        protected override void OnDrawContent(Graphics g)
        {
            MouseLookConfig look = _config.MouseLook;
            RectangleF rect = new RectangleF(1, 1, Width - 2, Height - 2);
            Color accent = Theme.Pink;
            bool active = _held;
            using (GraphicsPath path = BuildLookPath(rect, look.Shape, Math.Max(8f, Math.Min(Width, Height) * 0.12f)))
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(rect,
                    Color.FromArgb(240, 30, 24, 42), Color.FromArgb(242, 13, 15, 25), 90f))
                {
                    g.FillPath(brush, path);
                }
                using (Pen pen = new Pen(Theme.Alpha(accent, active ? 245 : 180),
                    Math.Max(1.3f, Math.Min(Width, Height) * 0.016f)))
                {
                    g.DrawPath(pen, path);
                }
            }
            if (active)
            {
                using (GraphicsPath glow = BuildLookPath(new RectangleF(-3, -3, Width + 6, Height + 6),
                    look.Shape, Math.Max(8f, Math.Min(Width, Height) * 0.14f)))
                using (Pen pen = new Pen(Color.FromArgb(130, accent), 3f))
                {
                    g.DrawPath(pen, glow);
                }
            }

            float d = Math.Min(Width, Height);
            PointF c = new PointF(Width / 2f, Height / 2f);
            float icon = d * 0.17f;
            RectangleF mouse = new RectangleF(c.X - icon * 0.58f, c.Y - icon * 1.2f, icon * 1.16f, icon * 1.6f);
            Theme.DrawRounded(g, mouse, icon * 0.5f, Color.FromArgb(230, 232, 220, 255), 1.4f);
            using (Pen p = new Pen(Color.FromArgb(190, 232, 220, 255), 1.1f))
                g.DrawLine(p, mouse.X + mouse.Width / 2f, mouse.Y + 1, mouse.X + mouse.Width / 2f,
                    mouse.Y + mouse.Height * 0.34f);
            Theme.DrawText(g, "视角", Theme.Semibold(Math.Max(6f, d * 0.105f)), Color.FromArgb(245, 244, 236, 255),
                new RectangleF(Width * 0.08f, c.Y + icon * 0.35f, Width * 0.84f, icon * 1.1f),
                StringAlignment.Center, StringAlignment.Center, true);
            Theme.DrawText(g, MouseLookConfig.ButtonName(look.Button == null ? 1 : (look.Button.Kind == BindingKind.MouseButton ? look.Button.Code : -1)),
                Theme.UiFont(Math.Max(5f, d * 0.065f)), Theme.Alpha(accent, 210),
                new RectangleF(Width * 0.08f, c.Y + icon * 1.55f, Width * 0.84f, icon * 0.9f),
                StringAlignment.Center, StringAlignment.Center, true);

            float arrow = Math.Max(5f, d * 0.07f);
            Color arrowColor = Color.FromArgb(active ? 225 : 110, 255, 242, 252);
            DrawArrow(g, new PointF(c.X, arrow * 1.4f), 0, arrow, arrowColor);
            DrawArrow(g, new PointF(c.X, Height - arrow * 1.4f), 2, arrow, arrowColor);
            DrawArrow(g, new PointF(arrow * 1.4f, c.Y), 3, arrow, arrowColor);
            DrawArrow(g, new PointF(Width - arrow * 1.4f, c.Y), 1, arrow, arrowColor);

            if (active)
            {
                float travel = d * 0.24f;
                float mag = (float)Math.Sqrt(_visualDx * _visualDx + _visualDy * _visualDy);
                PointF dot = mag > 0.1f
                    ? new PointF(c.X + _visualDx / Math.Max(1f, d * 0.38f) * travel,
                        c.Y + _visualDy / Math.Max(1f, d * 0.38f) * travel)
                    : c;
                Theme.FillCircle(g, new RectangleF(dot.X - d * 0.035f, dot.Y - d * 0.035f, d * 0.07f, d * 0.07f), accent);
            }
        }

        private static void DrawArrow(Graphics g, PointF p, int direction, float size, Color color)
        {
            PointF[] points;
            if (direction == 0) points = new PointF[] { new PointF(p.X, p.Y - size * 0.45f), new PointF(p.X - size * 0.45f, p.Y + size * 0.25f), new PointF(p.X + size * 0.45f, p.Y + size * 0.25f) };
            else if (direction == 1) points = new PointF[] { new PointF(p.X + size * 0.45f, p.Y), new PointF(p.X - size * 0.25f, p.Y - size * 0.45f), new PointF(p.X - size * 0.25f, p.Y + size * 0.45f) };
            else if (direction == 2) points = new PointF[] { new PointF(p.X, p.Y + size * 0.45f), new PointF(p.X - size * 0.45f, p.Y - size * 0.25f), new PointF(p.X + size * 0.45f, p.Y - size * 0.25f) };
            else points = new PointF[] { new PointF(p.X - size * 0.45f, p.Y), new PointF(p.X + size * 0.25f, p.Y - size * 0.45f), new PointF(p.X + size * 0.25f, p.Y + size * 0.45f) };
            using (SolidBrush b = new SolidBrush(color)) g.FillPolygon(b, points);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ReleaseHold();
            base.OnFormClosing(e);
        }
    }

    /// <summary>模式选择弹出菜单：点击模式键后在按键下方列出全部模式，选择后切换当前布局并自动收起。</summary>
    internal class OverlayModeMenuForm : Form
    {
        private const int WM_TOUCH = 0x0240;
        private const uint TOUCHEVENTF_UP = 0x0004;

        private readonly AppConfig _config;
        private readonly int _rowHeight;
        private readonly Action<int> _choose;
        private readonly Action _closed;
        private int _hover = -1;
        private Timer _closeTimer;
        private bool _sawTouch;
        private int _lastTouchTick;

        [StructLayout(LayoutKind.Sequential)]
        private struct TOUCHINPUT
        {
            public int x;
            public int y;
            public IntPtr hSource;
            public uint dwID;
            public uint dwFlags;
            public uint dwMask;
            public uint dwTime;
            public IntPtr dwExtraInfo;
            public uint cxContact;
            public uint cyContact;
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterTouchWindow(IntPtr hWnd, uint ulFlags);

        [DllImport("user32.dll")]
        private static extern bool GetTouchInputInfo(IntPtr hTouchInput, int cInputs, [In, Out] TOUCHINPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern bool CloseTouchInputHandle(IntPtr hTouchInput);

        public OverlayModeMenuForm(AppConfig config, Rectangle bounds, int rowHeight, Action<int> choose, Action closed)
        {
            _config = config;
            _rowHeight = rowHeight;
            _choose = choose;
            _closed = closed;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(22, 31, 47);
            DoubleBuffered = true;
            Bounds = bounds;
            Opacity = 0.97;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            _closeTimer = new Timer();
            _closeTimer.Interval = 9000;
            _closeTimer.Tick += delegate { CloseMenu(); };
            _closeTimer.Start();
        }

        public void ShowNoActivate()
        {
            double target = Opacity;
            Opacity = 0f;
            Show();
            Refresh();
            BeginInvoke(new System.Windows.Forms.MethodInvoker(delegate
            {
                try { Opacity = target; } catch { }
            }));
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000;
                cp.ExStyle |= 0x00000080;
                return cp;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (GraphicsPath path = Theme.RoundedPath(new RectangleF(0, 0, Width, Height), 14f))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { RegisterTouchWindow(Handle, 0); } catch { }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            e.Graphics.Clear(BackColor);
            Theme.DrawRounded(e.Graphics, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 14f,
                Color.FromArgb(86, 105, 140), 1f);
            for (int i = 0; i < _config.Modes.Count; i++)
            {
                Rectangle rect = new Rectangle(5, 5 + i * _rowHeight, Width - 10, _rowHeight);
                bool active = i == _config.SelectedMode;
                if (active)
                {
                    Theme.FillRounded(e.Graphics, rect, 8f, Color.FromArgb(80, 82, 139, 255));
                }
                else if (i == _hover)
                {
                    Theme.FillRounded(e.Graphics, rect, 8f, Color.FromArgb(65, 36, 50, 74));
                }
                Theme.DrawText(e.Graphics, _config.Modes[i].Name,
                    active ? Theme.Semibold(Math.Max(8f, _rowHeight * 0.32f)) : Theme.UiFont(Math.Max(8f, _rowHeight * 0.32f), false),
                    active ? Color.White : Theme.TextSoft,
                    new RectangleF(rect.X + 13, rect.Y, rect.Width - 30, rect.Height),
                    StringAlignment.Near, StringAlignment.Center, true);
                if (active)
                {
                    Theme.FillCircle(e.Graphics, new RectangleF(rect.Right - 16, rect.Y + rect.Height / 2f - 3, 6, 6), Theme.Mint);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int index = (e.Y - 5) / _rowHeight;
            if (index < 0 || index >= _config.Modes.Count) index = -1;
            if (index != _hover)
            {
                _hover = index;
                Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (IsTouchOriginMouse() && (_sawTouch || Environment.TickCount - _lastTouchTick < 800)) return;
            ChooseAt(e.Location);
            base.OnMouseUp(e);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetMessageExtraInfo();

        private static bool IsTouchOriginMouse()
        {
            long extra = GetMessageExtraInfo().ToInt64();
            return (extra & 0xFFFFFF00L) == 0xFF515700L;
        }

        private void ChooseAt(Point location)
        {
            int index = (location.Y - 5) / _rowHeight;
            if (index >= 0 && index < _config.Modes.Count)
            {
                CloseMenu();
                if (_choose != null) _choose(index);
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEACTIVATE = 0x0021;
            const int WM_ACTIVATE = 0x0006;
            const int WM_NCACTIVATE = 0x0086;
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)3; // MA_NOACTIVATE
                return;
            }
            if ((m.Msg == WM_ACTIVATE || m.Msg == WM_NCACTIVATE) && m.WParam != IntPtr.Zero)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WM_TOUCH)
            {
                _lastTouchTick = Environment.TickCount;
                _sawTouch = true;
                int count = m.WParam.ToInt32() & 0xFFFF;
                if (count > 0 && count <= 16)
                {
                    TOUCHINPUT[] inputs = new TOUCHINPUT[count];
                    if (GetTouchInputInfo(m.LParam, count, inputs, Marshal.SizeOf(typeof(TOUCHINPUT))))
                    {
                        try
                        {
                            for (int i = 0; i < count; i++)
                            {
                                if ((inputs[i].dwFlags & TOUCHEVENTF_UP) != 0)
                                {
                                    Point screen = new Point(inputs[i].x / 100, inputs[i].y / 100);
                                    ChooseAt(PointToClient(screen));
                                }
                            }
                        }
                        finally
                        {
                            CloseTouchInputHandle(m.LParam);
                        }
                    }
                }
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        private void CloseMenu()
        {
            if (_closeTimer != null) _closeTimer.Stop();
            if (_closed != null) _closed();
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_closeTimer != null) _closeTimer.Stop();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _closeTimer != null) _closeTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class OverlayViewMoveHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        private readonly Action<int, int> _move;
        private HookProc _proc;
        private IntPtr _hook;
        private POINT _last;
        private bool _hasLast;

        public OverlayViewMoveHook(Action<int, int> move)
        {
            _move = move;
        }

        public void Start(Point screen)
        {
            _last.X = screen.X;
            _last.Y = screen.Y;
            _hasLast = true;
            if (_hook != IntPtr.Zero) return;
            _proc = HookCallback;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        }

        public void Stop()
        {
            if (_hook == IntPtr.Zero) return;
            try { UnhookWindowsHookEx(_hook); } catch { }
            _hook = IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam.ToInt32() == WM_MOUSEMOVE)
            {
                MSLLHOOKSTRUCT data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                int dx = 0, dy = 0;
                if (_hasLast)
                {
                    dx = data.pt.X - _last.X;
                    dy = data.pt.Y - _last.Y;
                }
                _last = data.pt;
                _hasLast = true;
                if (data.dwExtraInfo.ToInt64() != InputSender.InputMagic)
                {
                    if ((dx != 0 || dy != 0) && _move != null)
                    {
                        try { _move(dx, dy); } catch { }
                    }
                    // Swallow the real touch-promoted mouse movement so the
                    // system pointer does not run across the desktop.
                    return (IntPtr)1;
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Stop();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string moduleName);
    }

    internal sealed class OverlayMouseReleaseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_XBUTTONUP = 0x020C;

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        private readonly Action _onRelease;
        private HookProc _proc;
        private IntPtr _hook;

        public OverlayMouseReleaseHook(Action onRelease)
        {
            _onRelease = onRelease;
        }

        public void Start()
        {
            if (_hook != IntPtr.Zero) return;
            _proc = HookCallback;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) ConfigStore.Log("全局松开钩子安装失败: " + Marshal.GetLastWin32Error());
        }

        public void Stop()
        {
            if (_hook == IntPtr.Zero) return;
            try { UnhookWindowsHookEx(_hook); } catch { }
            _hook = IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                MSLLHOOKSTRUCT data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                if (data.dwExtraInfo.ToInt64() != InputSender.InputMagic)
                {
                    int msg = wParam.ToInt32();
                    if (msg == WM_LBUTTONUP || msg == WM_RBUTTONUP || msg == WM_MBUTTONUP || msg == WM_XBUTTONUP)
                    {
                        try { if (_onRelease != null) _onRelease(); } catch { }
                    }
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Stop();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string moduleName);
    }
}
