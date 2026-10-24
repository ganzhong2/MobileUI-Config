// ============================================================================
// MainForm.cs —— 主设置窗口（程序生命周期与各面板的总装配点）
// ----------------------------------------------------------------------------
// 本类是程序启动后唯一的设置窗口，职责：
//   · 加载 / 保存 AppConfig，组装标题栏、游戏库栏、启动卡片、手机预览、
//     摇杆 / 视角 / 系统键 / 图片识别 / 内存扫描 / 剧情跳过等全部设置面板；
//   · 维护顶层页面切换（虚拟按键页 ↔ 虚拟手柄页），Root 属性按页返回独立配置根；
//   · 启动游戏进程、管理托盘图标与后台待命、单实例唤起、游戏前台 / 后台状态联动；
//   · 响应窗口尺寸、DPI 缩放与自定义绘制。
// 输入注入见 InputSender.cs，真机悬浮层见 Overlay.cs，配置模型见 Models.cs。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MobileUiConfig
{
    /// <summary>主设置窗口：配置加载保存、面板装配、游戏启动、托盘与前后台状态联动的总入口。</summary>
    public class MainForm : Form
    {
        private AppConfig _config;
        // 顶层页面：0=虚拟按键，1=虚拟手柄；Root 指向当前页配置根，按键页用 _config，
        // 手柄页用 _config.Gamepad，两者互不串数据。
        private int _padSide;
        private AppConfig Root
        {
            get { return _padSide == 1 && _config.Gamepad != null ? _config.Gamepad : _config; }
        }
        private bool _loading;
        private DateTime? _addKeyGuard;
        private readonly List<ModeTab> _modeTabs = new List<ModeTab>();

        private readonly TitleBar _titleBar;
        private readonly GameLibraryBar _gameBar;
        private readonly FeatureBar _featureBar;
        private readonly Panel _content;
        private readonly LaunchCard _launchCard;
        private readonly PreviewCard _previewCard;
        private readonly PhonePreview _preview;
        private readonly JoystickSettingsPanel _joystickSettings;
        private readonly MouseLookSettingsPanel _mouseLookSettings;
        private readonly GlobalFeedbackSettingsPanel _globalFeedbackSettings;
        private readonly SystemKeysSettingsPanel _systemKeys;
        private readonly AutoModeSettingsPanel _autoModeSettings;
        private readonly MemoryScanSettingsPanel _memoryScanSettings;
        private readonly StorySkipSettingsPanel _storySkipSettings;
        private readonly CooldownSettingsPanel _cooldownSettings;
        private InputAssistService _inputAssist;
        private MemoryScanService _memoryScan;
        private CooldownScanService _cooldownScan;
        private StorySkipService _storySkip;
        private bool _gameEndedHandled;
        private readonly SmoothScrollPane _controlDeck;
        private readonly Panel _deckContent;
        // 左侧卡片折叠 + 长按拖拽重排协调器（从「摇杆设置」开始纳入）。
        private CardDock _deckDock;
        private readonly EditorPanel _editor;
        private readonly FlowLayoutPanel _modeFlow;
        private readonly ModernButton _cloneModeButton;
        private readonly ModernButton _addModeButton;
        private readonly ModernButton _renameModeButton;
        private readonly ModernButton _deleteModeButton;
        private readonly FlowLayoutPanel _overlayFlow;
        private readonly ModernButton _cloneOverlayButton;
        private readonly ModernButton _addOverlayButton;
        private readonly ModernButton _renameOverlayButton;
        private readonly ModernButton _deleteOverlayButton;
        private readonly List<ModeTab> _overlayTabs = new List<ModeTab>();
        private bool _editingOverlay;
        private readonly ModernButton _orientationButton;
        private readonly ModernButton _expandAllLeftButton;
        private readonly ToggleSwitch _mainSoloToggle;
        private readonly ToggleSwitch _overlaySoloToggle;
        private readonly Label _mainSoloLabel;
        private readonly Label _overlaySoloLabel;
        private readonly ToggleSwitch _driverVigemToggle;
        private readonly ToggleSwitch _driverHidToggle;
        private readonly Label _driverVigemLabel;
        private readonly Label _driverHidLabel;
        private bool _bindingDriver;
        private readonly ToastView _toast;
        private readonly Timer _saveTimer;
        private readonly Timer _toastTimer;
        private readonly WheelRouter _wheelRouter;
        private readonly TouchScrollFilter _touchScroll;
        private bool _closing;
        private readonly bool _startHidden;
        private readonly bool _overlayTest;
        private RuntimeService _runtime;
        private NotifyIcon _tray;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _trayEnvItem;
        private ToolStripMenuItem _trayOverlayItem;
        private ToolStripMenuItem _trayAutoStartItem;
        private ToolStripMenuItem _trayAutoExitItem;
        private bool _allowExit;
        private bool _syncingAutoStart;
        private bool _syncingUiModes;
        private bool _shutdownRequested;
        private bool _launchOnStart;
        private const int FeatureHotkeyId = 0x4D55;
        private const int StoryHotkeyId = 0x4D56;
        private const int WM_COPYDATA = 0x004A;

        [StructLayout(LayoutKind.Sequential)]
        private struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }
        private bool _featureHotkeyRegistered;
        private bool _storyHotkeyRegistered;
        private bool _capturingFeature;
        private bool _launchingGame;
        private int _lastLaunchTick;
        private FeatureAutoSwitchService _autoSwitch;
        private string _currentProfilePath = "";

        public MainForm() : this(false, false)
        {
        }

        public MainForm(bool startHidden, bool overlayTest) : this(startHidden, overlayTest, null, false)
        {
        }

        /// <summary>
        /// 主构造函数：startHidden=启动后隐藏到托盘；overlayTest=悬浮层测试模式；
        /// profilePath=指定要加载的游戏配置文件（桌面快捷方式传入）；launch=启动后自动拉起游戏。
        /// </summary>
        public MainForm(bool startHidden, bool overlayTest, string profilePath, bool launch)
        {
            _config = ConfigStore.Load();
            _startHidden = startHidden;
            _overlayTest = overlayTest;
            _launchOnStart = launch;
            if (!string.IsNullOrEmpty(profilePath) && File.Exists(profilePath))
            {
                AppConfig loadedProfile = ConfigStore.LoadFrom(profilePath);
                if (loadedProfile != null)
                {
                    _config = loadedProfile;
                    _currentProfilePath = profilePath;
                }
                else
                {
                    ConfigStore.Log("启动配置读取失败: " + profilePath);
                }
            }
            // 特征图/剧情图/按键图等资源按游戏配置名存入同名文件夹。
            ConfigStore.SetAssetsDirForGame(_config == null ? "" : _config.GameName);
            _padSide = _config != null && _config.UiPadSide == 1 ? 1 : 0;
            if (_config != null && _config.Gamepad != null)
            {
                _config.Gamepad.Normalize();
                // 手柄独立根首次为空时补齐三家厂商默认手柄预设，保证手柄页左/右摇杆与
                // 方向键三组参数始终有独立对象可调，且与按键页主根互不共享。
                if (_config.Gamepad.Modes == null || _config.Gamepad.Modes.Count == 0)
                    _config.Gamepad.Modes = AppConfig.CreateDefaultModes();
                AppConfig.SanitizeGamepadRoot(_config.Gamepad);
            }

            Text = "移动端UI";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(Theme.S(1100), Theme.S(680));
            ClientSize = new Size(Theme.S(1480), Theme.S(940));
            MaximizeBox = true;
            MinimizeBox = true;
            ShowIcon = true;
            DoubleBuffered = true;
            KeyPreview = true;
            try
            {
                Icon = AppIcon.LoadIcon();
            }
            catch
            {
            }

            _titleBar = new TitleBar();
            _titleBar.Dock = DockStyle.Top;
            _titleBar.ProjectNameChanged += TitleBarOnProjectNameChanged;

            _gameBar = new GameLibraryBar();
            _gameBar.Dock = DockStyle.Top;
            _gameBar.GameSelected += LoadGameProfile;
            _gameBar.GameExportRequested += delegate(string path)
            {
                if (!string.IsNullOrEmpty(path) && path != _currentProfilePath) LoadGameProfile(path);
                ExportCurrentGame();
            };
            _gameBar.AddRequested += NewGameProfile;
            _gameBar.SaveRequested += SaveCurrentGame;
            _gameBar.ImportRequested += ImportGameProfile;
            _gameBar.ShortcutRequested += CreateGameShortcut;
            _gameBar.RenameRequested += RenameGameProfile;

            _featureBar = new FeatureBar();
            _featureBar.Dock = DockStyle.Top;
            _featureBar.Changed += delegate
            {
                if (_loading || _config == null) return;
                if (_inputAssist != null) _inputAssist.Apply();
                if (_runtime != null) _runtime.RefreshConfig();
                ScheduleSave();
            };

            _content = new Panel();
            _content.Dock = DockStyle.Fill;
            _content.BackColor = Theme.Bg;

            _launchCard = new LaunchCard();
            _launchCard.BrowseRequested += delegate { BrowseProgram(); };
            _launchCard.PasteRequested += delegate { PasteProgramPath(); };
            _launchCard.LaunchRequested += delegate { LaunchGame(); };
            _launchCard.PathInput.TextChanged += delegate { OnLaunchSettingChanged(); };
            _launchCard.ArgsInput.TextChanged += delegate { OnLaunchSettingChanged(); };
            _launchCard.RunAtLoginChanged += delegate { RunAtLoginToggleChanged(); };
            _launchCard.PathInput.InnerTextBox.AllowDrop = true;
            _launchCard.PathInput.InnerTextBox.DragEnter += PathBoxOnDragEnter;
            _launchCard.PathInput.InnerTextBox.DragDrop += PathBoxOnDragDrop;
            _content.Controls.Add(_launchCard);

            _previewCard = new PreviewCard();
            _content.Controls.Add(_previewCard);

            _modeFlow = new FlowLayoutPanel();
            _modeFlow.FlowDirection = FlowDirection.LeftToRight;
            _modeFlow.WrapContents = false;
            _modeFlow.AutoScroll = true;
            _modeFlow.BackColor = Theme.Surface;
            _modeFlow.Padding = new Padding(0);
            _modeFlow.HandleCreated += delegate
            {
                try { NativeMethods.SetWindowTheme(_modeFlow.Handle, "DarkMode_Explorer", null); }
                catch { }
            };
            _previewCard.Controls.Add(_modeFlow);

            _cloneModeButton = new ModernButton();
            _cloneModeButton.Text = "克隆";
            _cloneModeButton.Style = ModernButtonStyle.Ghost;
            _cloneModeButton.Font = Theme.UiFont(8.4f, false);
            _cloneModeButton.Click += delegate { CloneMode(); };
            _previewCard.Controls.Add(_cloneModeButton);

            _addModeButton = new ModernButton();
            _addModeButton.Text = "添加";
            _addModeButton.Icon = "＋";
            _addModeButton.Style = ModernButtonStyle.Soft;
            _addModeButton.Font = Theme.UiFont(8.4f, false);
            _addModeButton.Click += delegate { AddMode(); };
            _previewCard.Controls.Add(_addModeButton);

            _renameModeButton = new ModernButton();
            _renameModeButton.Text = "重命名";
            _renameModeButton.Style = ModernButtonStyle.Ghost;
            _renameModeButton.Font = Theme.UiFont(8.4f, false);
            _renameModeButton.Click += delegate { RenameMode(); };
            _previewCard.Controls.Add(_renameModeButton);

            _deleteModeButton = new ModernButton();
            _deleteModeButton.Text = "删除";
            _deleteModeButton.Style = ModernButtonStyle.Danger;
            _deleteModeButton.Font = Theme.UiFont(8.4f, false);
            _deleteModeButton.Click += delegate { DeleteMode(); };
            _previewCard.Controls.Add(_deleteModeButton);

            // 第二组叠加模式标签条：与主模式独立，只承载一套虚拟按键布局。
            _overlayFlow = new FlowLayoutPanel();
            _overlayFlow.FlowDirection = FlowDirection.LeftToRight;
            _overlayFlow.WrapContents = false;
            _overlayFlow.AutoScroll = true;
            _overlayFlow.BackColor = Theme.Surface;
            _overlayFlow.Padding = new Padding(0);
            _overlayFlow.HandleCreated += delegate
            {
                try { NativeMethods.SetWindowTheme(_overlayFlow.Handle, "DarkMode_Explorer", null); }
                catch { }
            };
            _previewCard.Controls.Add(_overlayFlow);

            _cloneOverlayButton = new ModernButton();
            _cloneOverlayButton.Text = "克隆";
            _cloneOverlayButton.Style = ModernButtonStyle.Ghost;
            _cloneOverlayButton.Font = Theme.UiFont(8.4f, false);
            _cloneOverlayButton.Click += delegate { CloneOverlayMode(); };
            _previewCard.Controls.Add(_cloneOverlayButton);

            _addOverlayButton = new ModernButton();
            _addOverlayButton.Text = "添加";
            _addOverlayButton.Icon = "＋";
            _addOverlayButton.Style = ModernButtonStyle.Soft;
            _addOverlayButton.Font = Theme.UiFont(8.4f, false);
            _addOverlayButton.Click += delegate { AddOverlayMode(); };
            _previewCard.Controls.Add(_addOverlayButton);

            _renameOverlayButton = new ModernButton();
            _renameOverlayButton.Text = "重命名";
            _renameOverlayButton.Style = ModernButtonStyle.Ghost;
            _renameOverlayButton.Font = Theme.UiFont(8.4f, false);
            _renameOverlayButton.Click += delegate { RenameOverlayMode(); };
            _previewCard.Controls.Add(_renameOverlayButton);

            _deleteOverlayButton = new ModernButton();
            _deleteOverlayButton.Text = "删除";
            _deleteOverlayButton.Style = ModernButtonStyle.Danger;
            _deleteOverlayButton.Font = Theme.UiFont(8.4f, false);
            _deleteOverlayButton.Click += delegate { DeleteOverlayMode(); };
            _previewCard.Controls.Add(_deleteOverlayButton);

            _preview = new PhonePreview();
            _preview.Config = _config;
            _preview.GlobalConfig = _config;
            _preview.Orientation = _config.Orientation;
            _preview.ModeSwitchRequested += SelectMode;
            _preview.KeySelected += PreviewOnKeySelected;
            _preview.LayoutChanged += delegate { ScheduleSave(); };
            _preview.ViewStateChanged += delegate { };
            _preview.WheelScrolled += delegate(int delta)
            {
                if (_controlDeck != null) _controlDeck.ScrollBy(delta > 0 ? -Theme.S(56) : Theme.S(56));
            };

            _joystickSettings = new JoystickSettingsPanel();
            _joystickSettings.Changed += delegate
            {
                _preview.Invalidate();
                ScheduleSave();
            };

            // 视角操控卡片：仅虚拟按键页使用；虚拟手柄页隐藏。
            _mouseLookSettings = new MouseLookSettingsPanel();
            _mouseLookSettings.Changed += delegate
            {
                _preview.Invalidate();
                ScheduleSave();
                if (_runtime != null) _runtime.RefreshConfig();
            };

            _globalFeedbackSettings = new GlobalFeedbackSettingsPanel();
            _globalFeedbackSettings.Changed += delegate
            {
                ScheduleSave();
            };

            _systemKeys = new SystemKeysSettingsPanel();
            _systemKeys.Changed += delegate
            {
                _preview.Invalidate();
                ScheduleSave();
                if (_runtime != null) _runtime.RefreshConfig();
            };

            _autoModeSettings = new AutoModeSettingsPanel();
            _autoModeSettings.Changed += delegate
            {
                ScheduleSave();
            };
            _autoModeSettings.HotkeyChanged += delegate { UpdateFeatureHotkeyRegistration(); };
            _autoModeSettings.CaptureRequested += delegate(int slot) { BeginFeatureCapture(slot); };
            _autoModeSettings.SelectRequested += delegate(int slot) { ChooseFeatureImage(slot); };
            _autoModeSettings.ModeSelectRequested += delegate(int index) { SelectImageProject(index); };
            _autoModeSettings.AddModeRequested += delegate { AddImageProject(); };
            _autoModeSettings.RenameModeRequested += delegate { RenameImageProject(); };
            _autoModeSettings.RemoveModeRequested += delegate { DeleteImageProject(); };

            _memoryScanSettings = new MemoryScanSettingsPanel();
            _memoryScanSettings.Changed += delegate
            {
                if (_loading || _config == null) return;
                if (_memoryScan != null) _memoryScan.Apply();
                ScheduleSave();
            };
            _memoryScanSettings.ModeSelectRequested += delegate(int index) { SelectMemoryProject(index); };
            _memoryScanSettings.AddModeRequested += delegate { AddMemoryProject(); };
            _memoryScanSettings.RenameModeRequested += delegate { RenameMemoryProject(); };
            _memoryScanSettings.RemoveModeRequested += delegate { DeleteMemoryProject(); };

            _storySkipSettings = new StorySkipSettingsPanel();
            _storySkipSettings.Changed += delegate
            {
                if (_loading || _config == null) return;
                if (_storySkip != null) _storySkip.Apply();
                ScheduleSave();
            };
            _storySkipSettings.HotkeyChanged += delegate { UpdateFeatureHotkeyRegistration(); };
            _storySkipSettings.CaptureRequested += delegate(int slot, int image) { BeginStoryCapture(slot, image); };
            _storySkipSettings.SelectRequested += delegate(int slot, int image) { ChooseStoryImage(slot, image); };

            _cooldownSettings = new CooldownSettingsPanel();
            _cooldownSettings.Changed += delegate
            {
                if (_loading || _config == null) return;
                ScheduleSave();
            };

            _deckContent = new Panel();
            _deckContent.BackColor = Theme.Surface;
            _deckContent.Controls.Add(_systemKeys);
            _deckContent.Controls.Add(_preview);
            _deckContent.Controls.Add(_joystickSettings);
            _deckContent.Controls.Add(_mouseLookSettings);
            _deckContent.Controls.Add(_globalFeedbackSettings);
            _deckContent.Controls.Add(_autoModeSettings);
            _deckContent.Controls.Add(_memoryScanSettings);
            _deckContent.Controls.Add(_storySkipSettings);

            _deckDock = new CardDock(_deckContent);
            _deckDock.Bind(LayoutDeck, null, OnLeftCardStateChanged);
            // 从「摇杆设置」起纳入折叠 + 长按拖拽；系统按键与手机预览不纳入。
            // 折叠态从配置恢复，首次进入默认全部折叠；centerY 为单行标题栏中心线基准。
            _globalFeedbackSettings.TitleLeftPad = 34f;
            _deckDock.Register(_joystickSettings, 0, 18, Theme.S(506), true, true, Root.LeftCardCollapsed[0], null);
            _deckDock.Register(_mouseLookSettings, 1, 18, Theme.S(240), true, true, Root.LeftCardCollapsed[1], null);
            _deckDock.Register(_globalFeedbackSettings, 2, 18, Theme.S(862), true, true, Root.LeftCardCollapsed[2], null);
            _deckDock.Register(_autoModeSettings, 3, 18, Theme.S(396), true, true, Root.LeftCardCollapsed[3], _autoModeSettings.DockTitleControl);
            _deckDock.Register(_memoryScanSettings, 4, 18, Theme.S(346), true, true, Root.LeftCardCollapsed[4], _memoryScanSettings.DockTitleControl);
            _deckDock.Register(_storySkipSettings, 5, 18, Theme.S(504), true, true, Root.LeftCardCollapsed[5], _storySkipSettings.DockTitleControl);

            _controlDeck = new SmoothScrollPane();
            _controlDeck.BackColor = Theme.Surface;
            _controlDeck.SetContent(_deckContent);
            _controlDeck.Resize += delegate { LayoutDeck(); };
            _previewCard.Controls.Add(_controlDeck);
            WireDeckWheel(_deckContent);

            _orientationButton = new ModernButton();
            _orientationButton.Style = ModernButtonStyle.Secondary;
            _orientationButton.Font = Theme.UiFont(8.2f, false);
            _orientationButton.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) ToggleOrientation();
            };
            _previewCard.Controls.Add(_orientationButton);
            UpdateOrientationButton();

            // 一键展开左侧全部卡片。
            _expandAllLeftButton = new ModernButton();
            _expandAllLeftButton.Style = ModernButtonStyle.Secondary;
            _expandAllLeftButton.Font = Theme.UiFont(8.2f, false);
            _expandAllLeftButton.Text = "全部展开";
            _expandAllLeftButton.Click += delegate
            {
                if (_deckDock == null) return;
                if (_deckDock.AnyCollapsed) _deckDock.ExpandAll();
                else _deckDock.CollapseAll();
                RefreshLeftExpandButton();
            };
            _previewCard.Controls.Add(_expandAllLeftButton);
            RefreshLeftExpandButton();

            // 标题行独立预览开关：主界面 / 叠加层互斥，0=两层共存，1=只显主层，2=只显叠加层。
            _mainSoloLabel = new Label();
            _mainSoloLabel.Text = "主界面独立预览";
            _mainSoloLabel.AutoSize = true;
            _mainSoloLabel.BackColor = Color.Transparent;
            _mainSoloLabel.ForeColor = Theme.Text;
            _mainSoloLabel.Font = Theme.UiFont(8.2f, false);
            _mainSoloLabel.TextAlign = ContentAlignment.MiddleRight;
            _previewCard.Controls.Add(_mainSoloLabel);

            _mainSoloToggle = new ToggleSwitch();
            _mainSoloToggle.CheckedChanged += OnMainSoloToggleChanged;
            _previewCard.Controls.Add(_mainSoloToggle);

            _overlaySoloLabel = new Label();
            _overlaySoloLabel.Text = "叠加层独立预览";
            _overlaySoloLabel.AutoSize = true;
            _overlaySoloLabel.BackColor = Color.Transparent;
            _overlaySoloLabel.ForeColor = Theme.Text;
            _overlaySoloLabel.Font = Theme.UiFont(8.2f, false);
            _overlaySoloLabel.TextAlign = ContentAlignment.MiddleRight;
            _previewCard.Controls.Add(_overlaySoloLabel);

            _overlaySoloToggle = new ToggleSwitch();
            _overlaySoloToggle.CheckedChanged += OnOverlaySoloToggleChanged;
            _previewCard.Controls.Add(_overlaySoloToggle);
            UpdatePreviewSoloControls();

            // 虚拟手柄驱动通道：ViGEmBus(X360/XInput) 与 HID(DS4) 互斥，仅手柄页可见。
            _driverVigemLabel = new Label();
            _driverVigemLabel.Text = "ViGEmBus";
            _driverVigemLabel.AutoSize = true;
            _driverVigemLabel.BackColor = Color.Transparent;
            _driverVigemLabel.ForeColor = Theme.Text;
            _driverVigemLabel.Font = Theme.UiFont(8.2f, false);
            _driverVigemLabel.TextAlign = ContentAlignment.MiddleRight;
            _previewCard.Controls.Add(_driverVigemLabel);

            _driverVigemToggle = new ToggleSwitch();
            _driverVigemToggle.CheckedChanged += OnDriverVigemChanged;
            _previewCard.Controls.Add(_driverVigemToggle);

            _driverHidLabel = new Label();
            _driverHidLabel.Text = "HID";
            _driverHidLabel.AutoSize = true;
            _driverHidLabel.BackColor = Color.Transparent;
            _driverHidLabel.ForeColor = Theme.Text;
            _driverHidLabel.Font = Theme.UiFont(8.2f, false);
            _driverHidLabel.TextAlign = ContentAlignment.MiddleRight;
            _previewCard.Controls.Add(_driverHidLabel);

            _driverHidToggle = new ToggleSwitch();
            _driverHidToggle.CheckedChanged += OnDriverHidChanged;
            _previewCard.Controls.Add(_driverHidToggle);
            UpdateDriverControls();

            _editor = new EditorPanel(_config);
            _content.Controls.Add(_editor);
            _editor.AttachCooldown(_cooldownSettings);
            _editor.SelectionChanged += delegate(VirtualKey key)
            {
                _preview.SelectedKey = key;
                _preview.Invalidate();
                if (_cooldownSettings != null) _cooldownSettings.SetKey(key);
            };
            _editor.KeyModified += delegate
            {
                _preview.Invalidate();
                ScheduleSave();
            };
            _editor.ImageChanged += delegate
            {
                _preview.ReloadImages();
                ScheduleSave();
            };
            _editor.AddRequested += delegate { AddKey(); };
            _editor.DeleteRequested += DeleteKey;
            _editor.ClearLayoutRequested += ClearEditorLayout;
            _editor.AddSlotRequested += AddSlotToLayout;
            _editor.AddPresetRequested += AddPresetToLayout;
                _editor.StickSelected += SelectStick;
                _preview.StickSelected += SelectStick;
                _editor.StickToggle += delegate(int kind)
                {
                    ModeConfig m = Root.ActiveMode;
                    if (m == null) return;
                    if (kind == 0 && m.Joystick != null) m.Joystick.Enabled = !m.Joystick.Enabled;
                    else if (kind == 1 && m.JoystickR != null) m.JoystickR.Enabled = !m.JoystickR.Enabled;
                    else if (kind == 2 && m.Dpad != null) m.Dpad.Enabled = !m.Dpad.Enabled;
                    _editor.RefreshChips();
                    _joystickSettings.SetConfig(m.Joystick, m.JoystickR, m.Dpad);
                    _preview.Invalidate();
                    ScheduleSave();
                };
            _editor.Notify += ShowToast;

            Controls.Add(_content);
            Controls.Add(_gameBar);
            Controls.Add(_featureBar);
            Controls.Add(_titleBar);

            _toast = new ToastView();
            Controls.Add(_toast);
            _toast.BringToFront();

            _saveTimer = new Timer();
            _saveTimer.Interval = 420;
            _saveTimer.Tick += delegate
            {
                _saveTimer.Stop();
                SaveNow(false);
            };

            _toastTimer = new Timer();
            _toastTimer.Interval = 2600;
            _toastTimer.Tick += delegate
            {
                _toastTimer.Stop();
                _toast.Visible = false;
            };

            _loading = true;
            _titleBar.SetPageSideSilent(_padSide);
            ApplyConfigToUi();
            _loading = false;

            _content.Resize += delegate { LayoutContent(); };
            Resize += delegate
            {
                LayoutToast();
                Invalidate();
                if (_runtime != null) _runtime.SetSettingsVisible(WindowState != FormWindowState.Minimized);
                UpdateFeatureHotkeyRegistration();
            };
            Shown += MainFormOnShown;
            FormClosing += MainFormOnFormClosing;
            FormClosed += MainFormOnFormClosed;
            KeyDown += MainFormOnKeyDown;
            VisibleChanged += delegate
            {
                try
                {
                    if (_runtime != null) _runtime.SetSettingsVisible(Visible);
                }
                catch (Exception ex)
                {
                    ConfigStore.Log("切换设置窗口可见性失败: " + ex.Message);
                }
                UpdateFeatureHotkeyRegistration();
            };

            SetupTray();
            SetupRuntime();
            _inputAssist = new InputAssistService(_config, () => Root);
            _memoryScan = new MemoryScanService(_config, () => Root, AutoSwitchMode, OnGameEnded, null);
            _memoryScan.SetPaused(true);
            _cooldownScan = new CooldownScanService(_config, () => Root, OnCooldownSnapshot);
            Func<Rectangle> boundsProvider = delegate
            {
                if (_runtime != null)
                {
                    Rectangle r = _runtime.CurrentBounds;
                    if (r.Width > 100 && r.Height > 100) return r;
                }
                return SystemInformation.VirtualScreen;
            };
            _storySkip = new StorySkipService(_config, () => Root, StoryCanScan, null, boundsProvider);
            _autoSwitch = new FeatureAutoSwitchService(_config, () => Root, AutoCanScan, AutoSwitchMode, null, boundsProvider);
            _launchCard.RunAtLoginChecked = _config.RunAtLogin;
            _titleBar.EnvironmentPill.Click += delegate { RecheckEnvironment(); };
            _titleBar.LocalUiChanged += TitleBarLocalUiChanged;
            _titleBar.StreamUiChanged += TitleBarStreamUiChanged;
            _titleBar.PageSideChanged += TitleBarPageSideChanged;

            LayoutContent();

            _wheelRouter = new WheelRouter(this);
            Application.AddMessageFilter(_wheelRouter);
            _touchScroll = new TouchScrollFilter();
            Application.AddMessageFilter(_touchScroll);
            FormClosed += delegate
            {
                Application.RemoveMessageFilter(_wheelRouter);
                Application.RemoveMessageFilter(_touchScroll);
            };
        }

        public SmoothScrollPane EditorSectionPane
        {
            get { return _editor == null ? null : _editor.SectionPane; }
        }

        /// <summary>托盘菜单“退出”专用：置位允许退出标志后关闭窗口，绕过右上角关闭按钮“隐藏到托盘”的行为。</summary>
        public void ForceExit()
        {
            _allowExit = true;
            Close();
        }

        public void SetOrientationForSnapshot(DisplayOrientation orientation)
        {
            Root.Orientation = orientation;
            _preview.Orientation = orientation;
            UpdateOrientationButton();
            LayoutDeck();
            if (_controlDeck != null) _controlDeck.ScrollToTop();
            _preview.Invalidate();
        }

        public void OpenOverlayMenuForTest()
        {
            if (_runtime != null) _runtime.OpenOverlayMenuForTest();
        }

        public void ApplySnapshotOptions(int mode, int view, bool menu, int scroll)
        {
            SelectMode(mode);
            _preview.SetViewState(view);
            if (menu && view != 1)
            {
                _preview.ActiveModeIndex = Root.SelectedMode;
                _preview.SetModeMenuOpen(true);
            }
            _editor.SectionPane.ScrollBy(scroll);
            _preview.Invalidate();
            _editor.Invalidate();
        }

        public void ScrollDeckForSnapshot(int pixels)
        {
            if (_controlDeck == null) return;
            // 快照时布局可能还没完成，先刷新滚动范围，否则 ScrollBy 会被 0 上限吃掉。
            _controlDeck.UpdateScrollMetrics();
            _controlDeck.ScrollToTop();
            _controlDeck.ScrollBy(pixels);
        }

        private void UpdateOrientationButton()
        {
            if (_orientationButton == null) return;
            _orientationButton.Text = Root.Orientation == DisplayOrientation.Landscape ? "切换为竖屏" : "切换为横屏";
        }

        private bool _bindingSolo;

        // 主界面独立预览：开启只显主层；再次点开关闭回两层共存。
        private void OnMainSoloToggleChanged(object sender, EventArgs e)
        {
            if (_bindingSolo || Root == null) return;
            if (_mainSoloToggle.Checked) Root.PreviewSoloMode = 1;
            else if (Root.PreviewSoloMode == 1) Root.PreviewSoloMode = 0;
            UpdatePreviewSoloControls();
            _preview.Invalidate();
            ScheduleSave();
        }

        // 叠加层独立预览：开启只显叠加层；再次点开关闭回两层共存；与主界面开关互斥。
        private void OnOverlaySoloToggleChanged(object sender, EventArgs e)
        {
            if (_bindingSolo || Root == null) return;
            if (_overlaySoloToggle.Checked) Root.PreviewSoloMode = 2;
            else if (Root.PreviewSoloMode == 2) Root.PreviewSoloMode = 0;
            UpdatePreviewSoloControls();
            _preview.Invalidate();
            ScheduleSave();
        }

        private void UpdatePreviewSoloControls()
        {
            if (_mainSoloToggle == null || Root == null) return;
            _bindingSolo = true;
            try
            {
                _mainSoloToggle.SetCheckedSilent(Root.PreviewSoloMode == 1);
                _overlaySoloToggle.SetCheckedSilent(Root.PreviewSoloMode == 2);
            }
            finally
            {
                _bindingSolo = false;
            }
        }

        // ViGEmBus / HID 驱动通道互斥切换，写入主配置并即时重建虚拟手柄目标。
        private void OnDriverVigemChanged(object sender, EventArgs e)
        {
            if (_bindingDriver) return;
            if (_driverVigemToggle.Checked)
            {
                _bindingDriver = true;
                try { _driverHidToggle.SetCheckedSilent(false); }
                finally { _bindingDriver = false; }
                _config.PadDriverType = GamepadService.DriverVigemBus;
                GamepadService.SetDriver(GamepadService.DriverVigemBus);
                ScheduleSave();
            }
            else
            {
                // 不允许两者同时关闭；另一开关未开启时自动勾回本开关。
                if (!_driverHidToggle.Checked)
                {
                    _bindingDriver = true;
                    try { _driverVigemToggle.SetCheckedSilent(true); }
                    finally { _bindingDriver = false; }
                }
            }
        }

        private void OnDriverHidChanged(object sender, EventArgs e)
        {
            if (_bindingDriver) return;
            if (_driverHidToggle.Checked)
            {
                _bindingDriver = true;
                try { _driverVigemToggle.SetCheckedSilent(false); }
                finally { _bindingDriver = false; }
                _config.PadDriverType = GamepadService.DriverHid;
                GamepadService.SetDriver(GamepadService.DriverHid);
                ScheduleSave();
            }
            else
            {
                if (!_driverVigemToggle.Checked)
                {
                    _bindingDriver = true;
                    try { _driverHidToggle.SetCheckedSilent(true); }
                    finally { _bindingDriver = false; }
                }
            }
        }

        private void UpdateDriverControls()
        {
            if (_driverVigemToggle == null) return;
            bool visible = _padSide == 1;
            _driverVigemToggle.Visible = visible;
            _driverHidToggle.Visible = visible;
            _driverVigemLabel.Visible = visible;
            _driverHidLabel.Visible = visible;
            _bindingDriver = true;
            try
            {
                _driverVigemToggle.SetCheckedSilent(_config.PadDriverType == GamepadService.DriverVigemBus);
                _driverHidToggle.SetCheckedSilent(_config.PadDriverType == GamepadService.DriverHid);
            }
            finally
            {
                _bindingDriver = false;
            }
            GamepadService.SetDriver(_config.PadDriverType);
        }

        private void ToggleOrientation()
        {
            Root.Orientation = Root.Orientation == DisplayOrientation.Landscape
                ? DisplayOrientation.Portrait
                : DisplayOrientation.Landscape;
            _preview.Orientation = Root.Orientation;
            UpdateOrientationButton();
            LayoutDeck();
            if (_controlDeck != null) _controlDeck.ScrollToTop();
            _preview.SetViewState(0);
            ScheduleSave();
            ShowToast(Root.Orientation == DisplayOrientation.Landscape ? "已切换为横屏" : "已切换为竖屏",
                ToastKind.Success);
        }

        private void ApplyConfigToUi()
        {
            _titleBar.SetProjectNameSilent(_config.ProjectName);
            _titleBar.GameName = string.IsNullOrEmpty(_config.GameName) ? "未保存游戏" : _config.GameName;
            // 按键页回填主根三张旧功能卡；手柄页回填手柄独立根两张新功能卡，两页配置互不串用。
            _featureBar.SetConfig(_padSide == 1 ? Root : _config, _padSide == 1);
            if (_inputAssist != null) _inputAssist.Apply();
            _launchCard.ProgramPath = _config.GamePath;
            _launchCard.Arguments = _config.GameArguments;
            _syncingUiModes = true;
            try
            {
                _titleBar.SetUiToggles(_config.LocalOverlayEnabled, _config.StreamOverlayEnabled);
            }
            finally { _syncingUiModes = false; }
            _preview.Config = Root;
            _preview.GlobalConfig = _config;
            _preview.GamepadMode = _padSide == 1;
            _preview.Orientation = Root.Orientation;
            _preview.ActiveModeIndex = Root.SelectedMode;
            _globalFeedbackSettings.SetConfig(Root);
            // 显示键/模式键的开关与短按/长按/双击动作按当前页根独立保存（按键页主根、
            // 手柄页 Gamepad 根），两页互不覆盖；位置坐标仍走主根共用，见 Overlay 与预览层。
            _systemKeys.SetConfig(Root);
            _systemKeys.SetModeRoot(Root);
            _memoryScanSettings.SetConfig(Root);
            _storySkipSettings.SetConfig(Root.StorySkip);
            if (_storySkip != null) _storySkip.Apply();
            _joystickSettings.SetModes(Root.Modes);
            // 视角操控卡片仅虚拟按键页显示；配置挂在全局根 _config.MouseLook，模式显隐跟随当前 Root.Modes。
            _mouseLookSettings.Visible = _padSide == 0;
            _mouseLookSettings.SetConfig(_config.MouseLook);
            _mouseLookSettings.SetModes(Root.Modes);
            _autoModeSettings.SetConfig(Root);
            UpdateOrientationButton();
            UpdatePreviewSoloControls();
            UpdateDriverControls();
            RebuildModeTabs();
            _editingOverlay = false;
            ModeConfig mode = Root.ActiveMode;
            VirtualKey first = mode != null && mode.Keys != null && mode.Keys.Count > 0 ? mode.Keys[0] : null;
            _preview.SetSelectedKeySilent(first);
            _preview.SetSelectedOverlayKeySilent(null);
            _editor.SetContextRoot(Root);
            _editor.SetPadSide(_padSide == 1);
            _editor.SetOverlayModes(Root.OverlayModes);
            _editor.SetMode(mode, first);
            // 先切换摇杆面板模式显隐，再回填当前页模式的摇杆/方向键配置，保证方向绑定框与
            // 显示模式条在按键页正确出现、手柄页三组参数正确绑定到当前手柄根模式。
            _joystickSettings.SetGamepadMode(_padSide == 1);
            _joystickSettings.SetConfig(mode == null ? null : mode.Joystick, mode == null ? null : mode.JoystickR,
                mode == null ? null : mode.Dpad);
            if (_cooldownSettings != null) _cooldownSettings.SetKey(first);
            // 各卡片 Visible 已按当前页切换完毕，末尾统一重排右侧卡片流；否则切回按键页时
            // 视角操控卡片仍按手柄页隐藏时的旧坐标/高度占位，表现为偶发消失，需手动折叠展开才复位。
            LayoutDeck();
            _titleBar.SetSaveState("已自动保存 " + Theme.ShortTime(), Theme.Mint);
            RefreshGameBar();
        }

        private void RefreshGameBar()
        {
            List<GameProfileEntry> profiles = ProfileStore.List();
            if (!string.IsNullOrEmpty(_currentProfilePath))
            {
                bool exists = false;
                for (int i = 0; i < profiles.Count; i++)
                    if (profiles[i].Path == _currentProfilePath) exists = true;
                if (!exists) _currentProfilePath = "";
            }
            if (string.IsNullOrEmpty(_currentProfilePath) && !string.IsNullOrEmpty(_config.GameName))
            {
                for (int i = 0; i < profiles.Count; i++)
                {
                    if (profiles[i].Name == _config.GameName)
                    {
                        _currentProfilePath = profiles[i].Path;
                        break;
                    }
                }
            }
            if (_gameBar != null) _gameBar.SetProfiles(profiles, _currentProfilePath);
            if (_titleBar != null) _titleBar.GameName = string.IsNullOrEmpty(_config.GameName) ? "未保存游戏" : _config.GameName;
        }

        /// <summary>把当前整套配置另存为一个独立游戏配置（弹窗命名后写入 games\ 目录并刷新游戏标签栏）。</summary>
        private void SaveCurrentGame()
        {
            if (_config == null) return;
            string current = _config.GameName;
            if (string.IsNullOrEmpty(current))
                current = string.IsNullOrEmpty(_config.GamePath) ? "新游戏" : Path.GetFileNameWithoutExtension(_config.GamePath);
            string name = PromptDialog.ShowGameName(this, current);
            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0) return;
            GameProfileEntry entry = ProfileStore.Save(name, _config);
            if (entry == null)
            {
                ShowToast("保存游戏配置失败，请检查目录权限", ToastKind.Error);
                return;
            }
            string oldProfilePath = _currentProfilePath;
            // 保存到新的游戏名时，资源文件夹和旧配置一起改名/清理，避免出现两份。
            bool renamed = !string.IsNullOrEmpty(current) &&
                !string.Equals(current, entry.Name, StringComparison.Ordinal);
            if (renamed) ConfigStore.RenameAssetsFolder(current, entry.Name);
            _currentProfilePath = entry.Path;
            ConfigStore.SetAssetsDirForGame(entry.Name);
            if (!string.IsNullOrEmpty(oldProfilePath) &&
                !string.Equals(oldProfilePath, entry.Path, StringComparison.OrdinalIgnoreCase))
            {
                try { if (File.Exists(oldProfilePath)) File.Delete(oldProfilePath); } catch { }
                string oldMirror = ProfileStore.AssetDir(oldProfilePath);
                string newMirror = ProfileStore.AssetDir(entry.Path);
                if (!string.Equals(oldMirror, newMirror, StringComparison.OrdinalIgnoreCase))
                {
                    try { if (Directory.Exists(oldMirror)) Directory.Delete(oldMirror, true); } catch { }
                }
            }
            _titleBar.GameName = entry.Name;
            RefreshGameBar();
            SaveNow(true);
            ShowToast("已保存游戏「" + entry.Name + "」", ToastKind.Success);
        }

        private void CreateGameShortcut(string profilePath)
        {
            if (string.IsNullOrEmpty(profilePath) || !File.Exists(profilePath))
            {
                ShowToast("游戏配置不存在", ToastKind.Error);
                return;
            }
            AppConfig profile = ConfigStore.LoadFrom(profilePath);
            if (profile == null)
            {
                ShowToast("读取游戏配置失败", ToastKind.Error);
                return;
            }
            string baseName = string.IsNullOrEmpty(profile.GameName)
                ? Path.GetFileNameWithoutExtension(profilePath)
                : profile.GameName;
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++) baseName = baseName.Replace(invalid[i], '_');
            baseName = baseName.Trim();
            if (baseName.Length == 0) baseName = "移动端UI";
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string shortcutPath = Path.Combine(desktop, baseName + ".lnk");
            try
            {
                string exe = Application.ExecutablePath;
                string arguments = "--profile \"" + profilePath + "\" --launch";
                string icon = !string.IsNullOrEmpty(profile.GamePath) && File.Exists(profile.GamePath)
                    ? profile.GamePath + ",0"
                    : exe + ",0";

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) throw new InvalidOperationException("WScript.Shell 不可用");
                object shell = Activator.CreateInstance(shellType);
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                    null, shell, new object[] { shortcutPath });
                Type linkType = link.GetType();
                linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { exe });
                linkType.InvokeMember("Arguments", BindingFlags.SetProperty, null, link, new object[] { arguments });
                linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link,
                    new object[] { ConfigStore.ExeDir });
                linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { icon });
                linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link,
                    new object[] { "启动游戏并加载「" + baseName + "」配置" });
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                ShowToast("已在桌面创建快捷方式「" + baseName + "」", ToastKind.Success);
            }
            catch (Exception ex)
            {
                ConfigStore.Log("创建游戏快捷方式失败: " + ex.Message);
                ShowToast("创建快捷方式失败: " + ex.Message, ToastKind.Error);
            }
        }

        private void RenameGameProfile(string profilePath)
        {
            if (string.IsNullOrEmpty(profilePath) || !File.Exists(profilePath))
            {
                ShowToast("游戏配置不存在", ToastKind.Error);
                return;
            }
            PersistCurrentProfile();
            AppConfig profile = ConfigStore.LoadFrom(profilePath);
            if (profile == null)
            {
                ShowToast("读取游戏配置失败", ToastKind.Error);
                return;
            }
            string current = string.IsNullOrEmpty(profile.GameName)
                ? Path.GetFileNameWithoutExtension(profilePath)
                : profile.GameName;
            string name = PromptDialog.ShowGameName(this, current);
            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0) return;
            GameProfileEntry entry = ProfileStore.Save(name, profile);
            if (entry == null)
            {
                ShowToast("重命名失败，请检查目录权限", ToastKind.Error);
                return;
            }
            bool wasActive = profilePath == _currentProfilePath;
            // 改名同步改资源文件夹（活动游戏切换资源目录前先搬文件夹）。
            ConfigStore.RenameAssetsFolder(current, entry.Name);
            if (wasActive)
            {
                _config.GameName = entry.Name;
                _currentProfilePath = entry.Path;
                _titleBar.GameName = entry.Name;
                // 游戏改名后，资源文件夹同步改名为新名称。
                ConfigStore.SetAssetsDirForGame(entry.Name);
                SaveNow(true);
            }
            if (profilePath != entry.Path)
            {
                ProfileStore.CopyAssets(profilePath, entry.Path);
                try { File.Delete(profilePath); } catch { }
                // 旧名称的资源镜像不再需要，删除避免占用和以后误补灌。
                string oldMirror = ProfileStore.AssetDir(profilePath);
                string newMirror = ProfileStore.AssetDir(entry.Path);
                if (!string.Equals(oldMirror, newMirror, StringComparison.OrdinalIgnoreCase))
                {
                    try { if (Directory.Exists(oldMirror)) Directory.Delete(oldMirror, true); } catch { }
                }
            }
            RefreshGameBar();
            ShowToast("已重命名为「" + entry.Name + "」", ToastKind.Success);
        }

        private void NewGameProfile()
        {
            PersistCurrentProfile();
            int count = ProfileStore.List().Count + 1;
            string name = PromptDialog.ShowGameName(this, "新游戏 " + count);
            if (name == null) return;            name = name.Trim();
            if (name.Length == 0) return;
            GameProfileEntry entry = ProfileStore.CreateNew(name);
            if (entry == null)
            {
                ShowToast("新建游戏失败", ToastKind.Error);
                return;
            }
            AppConfig loaded = ConfigStore.LoadFrom(entry.Path);
            if (loaded == null) return;
            _currentProfilePath = entry.Path;
            ApplyProfile(loaded);
            RefreshGameBar();
            ShowToast("已新建游戏「" + entry.Name + "」，可设置程序路径", ToastKind.Success);
        }

        private void OpenProfileShortcut(string path, bool launch)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                ShowToast("快捷方式对应的游戏配置不存在", ToastKind.Error);
                return;
            }
            LoadGameProfile(path);
            if (launch)
            {
                _launchOnStart = false;
                LaunchGame();
            }
        }

        private void PersistCurrentProfile()
        {
            if (_config == null || string.IsNullOrEmpty(_currentProfilePath)) return;
            if (!File.Exists(_currentProfilePath)) return;
            try
            {
                ConfigStore.SaveToIfChanged(_currentProfilePath, _config);
                ProfileStore.SaveAssets(_currentProfilePath, _config);
            }
            catch (Exception ex)
            {
                ConfigStore.Log("切换游戏前保存当前配置失败: " + ex.Message);
            }
        }

        private void LoadGameProfile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            PersistCurrentProfile();
            if (path == _currentProfilePath) return;
            AppConfig loaded = ConfigStore.LoadFrom(path);
            if (loaded == null)
            {
                ShowToast("读取游戏配置失败", ToastKind.Error);
                return;
            }
            _currentProfilePath = path;
            // 切换到该游戏配置的同名资源文件夹（历史镜像会自动补灌）。
            ConfigStore.SetAssetsDirForGame(loaded.GameName);
            ApplyProfile(loaded);
            RefreshGameBar();
            ShowToast("已切换到游戏「" + _config.GameName + "」", ToastKind.Success);
        }

        private void ApplyProfile(AppConfig loaded)
        {
            if (loaded == null) return;
            loaded.Normalize();
            FieldInfo[] fields = typeof(AppConfig).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                fields[i].SetValue(_config, fields[i].GetValue(loaded));
            }
            _config.Normalize();
            if (_config.Gamepad != null)
            {
                _config.Gamepad.Normalize();
                if (_config.Gamepad.Modes == null || _config.Gamepad.Modes.Count == 0)
                    _config.Gamepad.Modes = AppConfig.CreateDefaultModes();
                AppConfig.SanitizeGamepadRoot(_config.Gamepad);
            }
            ConfigStore.SetAssetsDirForGame(_config.GameName);
            _preview.ReloadImages();
            ApplyConfigToUi();
            _preview.Invalidate();
            _editor.Invalidate();
            LayoutContent();
            LayoutDeck();
            if (_controlDeck != null) _controlDeck.ScrollToTop();
            if (_runtime != null) _runtime.RefreshConfig();
            UpdateFeatureHotkeyRegistration();
            ScheduleSave();
        }

        private void ExportCurrentGame()
        {
            if (_config == null) return;
            if (string.IsNullOrEmpty(_config.GameName))
            {
                SaveCurrentGame();
                if (string.IsNullOrEmpty(_config.GameName)) return;
            }
            else
            {
                GameProfileEntry entry = ProfileStore.Save(_config.GameName, _config);
                if (entry != null) _currentProfilePath = entry.Path;
                RefreshGameBar();
            }
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "导出游戏配置";
                dialog.Filter = "移动端UI配置分享包 (*.mobui)|*.mobui|ZIP 压缩包 (*.zip)|*.zip";
                dialog.FileName = ProfileStore.SafeFileName(_config.GameName) + ".mobui";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (ProfileStore.Export(_config, dialog.FileName, ConfigStore.AssetsDir))
                {
                    ShowToast("配置已导出，包含布局和图片", ToastKind.Success);
                }
                else
                {
                    ShowToast("导出失败，请检查目标路径", ToastKind.Error);
                }
            }
        }

        private void ImportGameProfile()
        {
            PersistCurrentProfile();
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "导入游戏配置";
                dialog.Filter = "移动端UI配置分享包 (*.mobui;*.zip)|*.mobui;*.zip|所有文件 (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string error;
                AppConfig imported = ProfileStore.Import(dialog.FileName, out error);
                if (imported == null)
                {
                    ShowToast("导入失败：" + error, ToastKind.Error);
                    return;
                }
                string name = imported.GameName;
                if (string.IsNullOrEmpty(name)) name = Path.GetFileNameWithoutExtension(dialog.FileName);
                imported.ProfileOrder = ProfileStore.NextOrder();
                GameProfileEntry entry = ProfileStore.Save(name, imported);
                if (entry == null)
                {
                    ShowToast("导入后保存游戏配置失败", ToastKind.Error);
                    return;
                }
                _currentProfilePath = entry.Path;
                // 先切到该游戏的专属资源文件夹，再把资源镜像到 games/assets 备份；
                // 导入的资源文件已由 ProfileStore.Import 放进同名文件夹。
                ApplyProfile(imported);
                ProfileStore.SaveAssets(entry.Path, imported);
                RefreshGameBar();
                ShowToast("已导入游戏「" + entry.Name + "」", ToastKind.Success);
            }
        }

        private void LayoutContent()
        {
            if (_content == null || _launchCard == null) return;
            float s = Theme.Scale;
            int padX = (int)(18 * s);
            int padTop = (int)(10 * s);
            int padBottom = (int)(16 * s);
            int gap = (int)(16 * s);
            int width = _content.ClientSize.Width;
            int height = _content.ClientSize.Height;
            if (width <= 0 || height <= 0) return;

            int launchH = (int)(112 * s);
            _launchCard.SetBounds(padX, padTop, Math.Max(200, width - padX * 2), launchH);
            int splitTop = padTop + launchH + (int)(14 * s);
            int splitH = Math.Max(100, height - splitTop - padBottom);
            int available = Math.Max(200, width - padX * 2);
            int previewW = (int)((available - gap) * 0.62f);
            if (previewW < 560) previewW = Math.Min(available - gap - 360, 560);
            if (previewW < 480) previewW = available - gap - 330;
            if (previewW < 300) previewW = available / 2;
            int editorW = available - gap - previewW;

            _previewCard.SetBounds(padX, splitTop, previewW, splitH);
            _editor.SetBounds(padX + previewW + gap, splitTop, editorW, splitH);

            int ppad = (int)(16 * s);
            int modeTop = (int)(52 * s);
            int modeH = (int)(38 * s);
            int btnW = (int)(72 * s);
            // 主模式行：删除最右，重命名次之，添加紧随其后，剩余宽度给标签流；与叠加行排版一致。
            int delW = (int)(72 * s);
            int renW = (int)(76 * s);
            int cloneW = btnW;
            _deleteModeButton.SetBounds(previewW - ppad - delW, modeTop, delW, (int)(34 * s));
            _renameModeButton.SetBounds(_deleteModeButton.Left - (int)(6 * s) - renW, modeTop, renW, (int)(34 * s));
            _addModeButton.SetBounds(_renameModeButton.Left - (int)(6 * s) - btnW, modeTop, btnW, (int)(34 * s));
            _cloneModeButton.SetBounds(_addModeButton.Left - (int)(6 * s) - cloneW, modeTop, cloneW, (int)(34 * s));
            int flowW = Math.Max(100, _cloneModeButton.Left - (int)(10 * s) - ppad);
            _modeFlow.SetBounds(ppad, modeTop, flowW, (int)(38 * s));

            // 叠加模式标签条第二行：删除按钮在最右，重命名次之，添加紧随其后，剩余宽度给标签流。
            int overlayTop = modeTop + modeH + (int)(6 * s);
            _deleteOverlayButton.SetBounds(previewW - ppad - delW, overlayTop, delW, (int)(34 * s));
            _renameOverlayButton.SetBounds(_deleteOverlayButton.Left - (int)(6 * s) - renW, overlayTop, renW, (int)(34 * s));
            _addOverlayButton.SetBounds(_renameOverlayButton.Left - (int)(6 * s) - btnW, overlayTop, btnW, (int)(34 * s));
            _cloneOverlayButton.SetBounds(_addOverlayButton.Left - (int)(6 * s) - cloneW, overlayTop, cloneW, (int)(34 * s));
            int overlayFlowW = Math.Max(100, _cloneOverlayButton.Left - (int)(10 * s) - ppad);
            _overlayFlow.SetBounds(ppad, overlayTop, overlayFlowW, (int)(38 * s));

            int deckTop = overlayTop + modeH + (int)(8 * s);
            _controlDeck.SetBounds(ppad, deckTop, Math.Max(100, previewW - ppad * 2),
                Math.Max(100, splitH - deckTop - Theme.S(4)));
            LayoutDeck();
            int expandW = (int)(110 * s);
            int orientW = (int)(122 * s);
            int orientH = (int)(32 * s);
            _expandAllLeftButton.SetBounds(previewW - ppad - expandW, (int)(11 * s), expandW, orientH);
            _orientationButton.SetBounds(_expandAllLeftButton.Left - (int)(10 * s) - orientW, (int)(11 * s), orientW, orientH);

            // 独立预览开关组：排在竖屏按钮左侧同行；叠加组紧邻按钮，主组在其左，两组互斥。
            int soloToggleW = (int)(46 * s);
            int soloToggleH = (int)(26 * s);
            int soloGap = (int)(6 * s);
            int soloGroupGap = (int)(12 * s);
            int soloGroupRight = _orientationButton.Left - (int)(10 * s);
            _overlaySoloToggle.SetBounds(soloGroupRight - soloToggleW, (int)(14 * s), soloToggleW, soloToggleH);
            Size ovLab = _overlaySoloLabel.GetPreferredSize(new Size(int.MaxValue, orientH));
            _overlaySoloLabel.SetBounds(_overlaySoloToggle.Left - soloGap - ovLab.Width, (int)(11 * s), ovLab.Width, orientH);
            int mainGroupRight = _overlaySoloLabel.Left - soloGroupGap;
            _mainSoloToggle.SetBounds(mainGroupRight - soloToggleW, (int)(14 * s), soloToggleW, soloToggleH);
            Size mainLab = _mainSoloLabel.GetPreferredSize(new Size(int.MaxValue, orientH));
            _mainSoloLabel.SetBounds(_mainSoloToggle.Left - soloGap - mainLab.Width, (int)(11 * s), mainLab.Width, orientH);

            // 驱动通道组（仅手柄页可见）：排在主界面独立预览组左侧，HID 在右、ViGEmBus 在左，两组互斥。
            int drvGroupRight = _mainSoloLabel.Left - soloGroupGap;
            _driverHidToggle.SetBounds(drvGroupRight - soloToggleW, (int)(14 * s), soloToggleW, soloToggleH);
            Size hidLab = _driverHidLabel.GetPreferredSize(new Size(int.MaxValue, orientH));
            _driverHidLabel.SetBounds(_driverHidToggle.Left - soloGap - hidLab.Width, (int)(11 * s), hidLab.Width, orientH);
            int vigGroupRight = _driverHidLabel.Left - soloGroupGap;
            _driverVigemToggle.SetBounds(vigGroupRight - soloToggleW, (int)(14 * s), soloToggleW, soloToggleH);
            Size vigLab = _driverVigemLabel.GetPreferredSize(new Size(int.MaxValue, orientH));
            _driverVigemLabel.SetBounds(_driverVigemToggle.Left - soloGap - vigLab.Width, (int)(11 * s), vigLab.Width, orientH);
        }

        private void LayoutDeck()
        {
            if (_deckContent == null || _controlDeck == null) return;
            int width = Math.Max(120, _deckContent.Width);
            int previewH;
            if (_preview.Orientation == DisplayOrientation.Portrait)
                previewH = Math.Min(Theme.S(700), Math.Max(Theme.S(360), (int)(width * 1.12f)));
            else
                previewH = Math.Min(Theme.S(520), Math.Max(Theme.S(250), (int)(width * 0.58f)));
            _systemKeys.SetBounds(0, 0, width, Theme.S(116));
            int y = Theme.S(116) + Theme.S(10);
            _preview.SetBounds(0, y, width, previewH);
            y += previewH + Theme.S(10);

            // 系统按键与手机预览固定最前；其后按 dock 注册顺序与折叠高度纵向堆叠。
            _deckContent.Controls.SetChildIndex(_systemKeys, 0);
            _deckContent.Controls.SetChildIndex(_preview, 1);
            int z = 2;
            foreach (CardDockItem item in _deckDock.Items)
            {
                _deckContent.Controls.SetChildIndex(item.Card, z);
                z++;
                if (!item.Card.Visible) continue;
                item.Card.SetBounds(0, y, width, item.Card.Height);
                y += item.Card.Height + Theme.S(10);
            }
            _deckContent.Height = y;
            _controlDeck.UpdateScrollMetrics();
        }

        private void WireDeckWheel(Control parent)
        {
            if (parent == null || parent is PhonePreview) return;
            parent.MouseWheel += DeckOnMouseWheel;
            for (int i = 0; i < parent.Controls.Count; i++) WireDeckWheel(parent.Controls[i]);
        }

        private void DeckOnMouseWheel(object sender, MouseEventArgs e)
        {
            if (_controlDeck == null) return;
            _controlDeck.ScrollBy(e.Delta > 0 ? -Theme.S(60) : Theme.S(60));
        }

        public SmoothScrollPane ControlDeck
        {
            get { return _controlDeck; }
        }

        private void LayoutToast()
        {
            if (_toast == null) return;
            _toast.Left = Math.Max(10, (ClientSize.Width - _toast.Width) / 2);
            _toast.Top = Math.Max(74, ClientSize.Height - _toast.Height - 28);
        }

        private void MainFormOnShown(object sender, EventArgs e)
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            ApplyDarkTitleBar();
            Timer darkTimer = new Timer();
            darkTimer.Interval = 700;
            darkTimer.Tick += delegate
            {
                darkTimer.Stop();
                ApplyDarkTitleBar();
                darkTimer.Dispose();
            };
            darkTimer.Start();
            int maxW = Math.Max(MinimumSize.Width, area.Width - 50);
            int maxH = Math.Max(MinimumSize.Height, area.Height - 40);
            if (Width > maxW || Height > maxH)
            {
                Width = Math.Min(Width, maxW);
                Height = Math.Min(Height, maxH);
                Left = area.Left + (area.Width - Width) / 2;
                Top = area.Top + (area.Height - Height) / 2;
            }
            LayoutContent();
            // 构造期首屏绑定时父链尚未显示，卡片堆叠坐标被跳过；窗口显示后重绑并重排右侧参数面板。
            if (_editor != null) _editor.RebuildAfterShow();
            // Persist the current schema once so an old XML is upgraded even if
            // the user never presses Save.
            try { SaveNow(true); } catch { }
            if (_config.RunAtLogin) AutoStart.SetEnabled(true);
            UpdateFeatureHotkeyRegistration();
            if (_autoSwitch != null) _autoSwitch.Start();
            if (_runtime != null)
            {
                _runtime.SetSettingsVisible(WindowState != FormWindowState.Minimized);
                _runtime.Start();
                UpdateEnvironmentUi(_runtime.StreamDetected, _runtime.StreamReason);
                UpdateTrayOverlayState();
                if (_overlayTest)
                {
                    _runtime.EnableTestOverlay();
                    _runtime.Recheck();
                    HideToTray(true);
                }
                else if (_startHidden)
                {
                    HideToTray(true);
                }
            }
            if (_launchOnStart)
            {
                _launchOnStart = false;
                LaunchGame();
            }
        }

        private void MainFormOnFormClosing(object sender, FormClosingEventArgs e)
        {            if (_closing) return;
            if (!_allowExit && e.CloseReason != CloseReason.WindowsShutDown)
            {
                e.Cancel = true;
                try { SaveNow(true); } catch { }
                HideToTray(false);
                return;
            }
            _closing = true;
            _saveTimer.Stop();
            SaveNow(true);
            PersistCurrentProfile();
            UnregisterFeatureHotkey();
            try { if (_autoSwitch != null) { _autoSwitch.Stop(); _autoSwitch.Dispose(); _autoSwitch = null; } } catch { }
            try { if (_memoryScan != null) { _memoryScan.SetPaused(true); _memoryScan.Dispose(); _memoryScan = null; } } catch { }
            try { if (_cooldownScan != null) { _cooldownScan.SetPaused(true); _cooldownScan.Dispose(); _cooldownScan = null; } } catch { }
            try { if (_storySkip != null) { _storySkip.Dispose(); _storySkip = null; } } catch { }
            try { if (_inputAssist != null) { _inputAssist.Dispose(); _inputAssist = null; } } catch { }
            try { if (_runtime != null) { _runtime.Stop(); _runtime = null; } } catch { }
            try { InputSender.ReleaseAll(); } catch { }
            try { RumbleService.Stop(); } catch { }
            if (_tray != null) _tray.Visible = false;
        }

        private void MainFormOnFormClosed(object sender, FormClosedEventArgs e)
        {
            try
            {
                if (_inputAssist != null)
                {
                    _inputAssist.Dispose();
                    _inputAssist = null;
                }
                if (_memoryScan != null)
                {
                    _memoryScan.Dispose();
                    _memoryScan = null;
                }
                if (_cooldownScan != null)
                {
                    _cooldownScan.Dispose();
                    _cooldownScan = null;
                }
                if (_storySkip != null)
                {
                    _storySkip.Dispose();
                    _storySkip = null;
                }
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                    _tray = null;
                }
                if (_trayMenu != null)
                {
                    _trayMenu.Dispose();
                    _trayMenu = null;
                }
            }
            catch { }
        }

        private void UpdateFeatureHotkeyRegistration()
        {
            if (_config == null) return;
            bool settingsOnScreen = Visible && WindowState != FormWindowState.Minimized;
            if (settingsOnScreen || _runtime == null || !_runtime.GameForeground)
            {
                UnregisterFeatureHotkey();
            }
            else
            {
                RegisterFeatureHotkey();
                RegisterStoryHotkey();
            }
        }

        private void RegisterFeatureHotkey()
        {            UnregisterFeatureHotkey();
            AppConfig root = Root;
            if (_config == null || root == null || root.AutoMode == null) return;
            KeyBinding hotkey = root.AutoMode.Hotkey;
            if (hotkey == null || hotkey.Kind != BindingKind.Keyboard || hotkey.Code <= 0) return;
            uint mods = 0;
            Keys m = (Keys)hotkey.Modifiers;
            if ((m & Keys.Control) == Keys.Control) mods |= 0x0002;
            if ((m & Keys.Shift) == Keys.Shift) mods |= 0x0004;
            if ((m & Keys.Alt) == Keys.Alt) mods |= 0x0001;
            bool ok = NativeMethods.RegisterHotKey(Handle, FeatureHotkeyId, mods, hotkey.Code);
            _featureHotkeyRegistered = ok;
            ConfigStore.Log(ok
                ? "截图快捷键已注册: " + hotkey.DisplayName
                : "截图快捷键注册失败，可能被其他程序占用: " + hotkey.DisplayName);
        }

        // 跳过剧情总开关快捷键：与截图快捷键同一注册时机（设置隐藏/最小化且游戏前台），
        // 绑定来自当前页根 Root.StorySkip.Hotkey，两页各自独立保存。
        private void RegisterStoryHotkey()
        {
            AppConfig root = Root;
            if (_config == null || root == null || root.StorySkip == null) return;
            KeyBinding hotkey = root.StorySkip.Hotkey;
            if (hotkey == null || hotkey.Kind != BindingKind.Keyboard || hotkey.Code <= 0) return;
            uint mods = 0;
            Keys m = (Keys)hotkey.Modifiers;
            if ((m & Keys.Control) == Keys.Control) mods |= 0x0002;
            if ((m & Keys.Shift) == Keys.Shift) mods |= 0x0004;
            if ((m & Keys.Alt) == Keys.Alt) mods |= 0x0001;
            bool ok = NativeMethods.RegisterHotKey(Handle, StoryHotkeyId, mods, hotkey.Code);
            _storyHotkeyRegistered = ok;
            ConfigStore.Log(ok
                ? "跳过剧情快捷键已注册: " + hotkey.DisplayName
                : "跳过剧情快捷键注册失败，可能被其他程序占用: " + hotkey.DisplayName);
        }

        private void UnregisterFeatureHotkey()
        {
            if (_featureHotkeyRegistered)
            {
                try { NativeMethods.UnregisterHotKey(Handle, FeatureHotkeyId); } catch { }
                _featureHotkeyRegistered = false;
            }
            if (_storyHotkeyRegistered)
            {
                try { NativeMethods.UnregisterHotKey(Handle, StoryHotkeyId); } catch { }
                _storyHotkeyRegistered = false;
            }
        }

        // 裁剪特征时所用的画面区域：优先游戏窗口（本地=主窗口，串流=前台串流窗口），
        // 与运行时截图口径一致；取不到时退回整屏。
        private Rectangle CurrentCaptureBounds()
        {
            if (_runtime != null)
            {
                Rectangle r = _runtime.CurrentBounds;
                if (r.Width > 100 && r.Height > 100) return r;
            }
            return SystemInformation.VirtualScreen;
        }

        private bool AutoCanScan()
        {
            return _runtime != null && _runtime.GameForeground && !_capturingFeature &&
                   (!Visible || WindowState == FormWindowState.Minimized);
        }

        private bool StoryCanScan()
        {
            AppConfig root = Root;
            if (_runtime == null || root == null || root.StorySkip == null) return false;
            bool stream = _runtime.StreamDetected;
            if (!_runtime.GameForeground) return false;
            if (root.StorySkip.AlwaysEnabled)
            {
                return !_capturingFeature &&
                       (!Visible || WindowState == FormWindowState.Minimized);
            }
            bool modeEnabled = stream ? root.StorySkip.StreamEnabled : root.StorySkip.LocalEnabled;
            return modeEnabled && !_capturingFeature &&
                   (!Visible || WindowState == FormWindowState.Minimized);
        }

        private void AutoSwitchMode(int index, string featureName)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, string>(AutoSwitchMode), new object[] { index, featureName });
                return;
            }
            AppConfig root = Root;
            if (root == null || index < 0 || index >= root.Modes.Count) return;
            if (root.SelectedMode == index) return;
            SelectMode(index);
            try { if (_runtime != null) _runtime.RefreshConfig(); } catch { }
            ScheduleSave();
            ShowToast("识别到 " + featureName + "，已切换到「" + root.Modes[index].Name + "」", ToastKind.Success);
        }

        // 从文件管理器选图没有游戏窗口上下文，无法记录界面锚点；选入前提示用户
        // 优先用游戏内框选，或用全屏截图在原位置按比例裁切，保证缩放基准一致。
        private static void ShowLocalFeatureTip()
        {
            string msg =
                "从本地文件选图不会记录界面锚点，识别时只能整屏搜索，速度慢、容易被背景干扰。\r\n\r\n" +
                "建议：\r\n" +
                "· 优先用「截图」按钮在游戏内直接框选，自动记录位置，只在小范围搜索；\r\n" +
                "· 必须用文件图片时，先截游戏全屏图，在界面相同位置、按原比例裁切后再选入。";
            MessageBox.Show(msg, "从本地文件选择特征图", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ChooseFeatureImage(int slot)
        {
            AppConfig root = Root;
            AutoImageProject project = root == null ? null : root.ActiveImageProject;
            if (project == null || project.Features == null) return;
            if (slot < 0 || slot > 2) slot = 0;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择特征图片";
                dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ShowLocalFeatureTip();
                try
                {
                    using (Bitmap source = new Bitmap(dialog.FileName))
                    using (ImageCropForm crop = new ImageCropForm(source))
                    {
                        if (crop.ShowDialog(this) != DialogResult.OK || crop.ResultBitmap == null) return;
                        string path = ConfigStore.FeaturePath(root.SelectedImageProject, slot);
                        using (Bitmap result = crop.ResultBitmap)
                            result.Save(path, ImageFormat.Png);
                        AutoFeatureConfig feature = project.Features[slot];
                        feature.ImageFile = Path.GetFileName(path);
                        Rectangle capBounds = CurrentCaptureBounds();
                        feature.CaptureWidth = capBounds.Width;
                        feature.CaptureHeight = capBounds.Height;
                        feature.AnchorX = -1f;
                        feature.AnchorY = -1f;
                        feature.Enabled = true;
                        BindAutoPanels();
                        _autoModeSettings.SetStatus("已选择特征" + ModeConfig.ChineseNumber(slot + 1));
                        ScheduleSave();
                        ShowToast("特征" + ModeConfig.ChineseNumber(slot + 1) + " 已选择并裁剪", ToastKind.Success);
                    }
                }
                catch (Exception ex)
                {
                    ConfigStore.Log("选择特征图片失败: " + ex.Message);
                    ShowToast("选择图片失败：" + ex.Message, ToastKind.Error);
                }
            }
        }

        private void BeginFeatureCapture(int slot)
        {            AppConfig root = Root;
            if (_capturingFeature || root == null || root.AutoMode == null) return;
            if (slot < 0 || slot > 2) slot = 0;
            if (root.ActiveImageProject == null) return;
            _capturingFeature = true;
            bool restoreSettings = Visible;
            FormWindowState previousState = WindowState;
            if (restoreSettings) Hide();
            try { if (_runtime != null) _runtime.SetSettingsVisible(true); } catch { }
            int captureSlot = slot;
            int captureProject = root.SelectedImageProject;
            Timer delay = new Timer();
            delay.Interval = 240;
            delay.Tick += delegate
            {
                delay.Stop();
                RunFeatureCapture(captureProject, captureSlot, restoreSettings, previousState);
                delay.Dispose();
            };
            delay.Start();
        }

        private void RunFeatureCapture(int captureProject, int slot, bool restoreSettings, FormWindowState previousState)
        {
            try
            {
                using (FeatureCaptureForm form = new FeatureCaptureForm())
                {
                    if (!form.CaptureOk)
                    {
                        ShowToast("截屏失败：无法读取当前屏幕", ToastKind.Error);
                        return;
                    }
                    if (form.ShowDialog() != DialogResult.OK || form.ResultBitmap == null) return;
                    AppConfig root = Root;
                    if (root.ImageProjects == null || captureProject < 0 || captureProject >= root.ImageProjects.Count) return;
                    AutoImageProject project = root.ImageProjects[captureProject];
                    if (project.Features == null || slot >= project.Features.Count) return;
                    string path = ConfigStore.FeaturePath(captureProject, slot);
                    try
                    {
                        using (Bitmap bmp = form.ResultBitmap)
                        {
                            bmp.Save(path, ImageFormat.Png);
                        }
                    }
                    catch (Exception ex)
                    {
                        ConfigStore.Log("保存特征截图失败: " + ex.Message);
                        ShowToast("保存特征截图失败，请检查目录权限", ToastKind.Error);
                        return;
                    }
                    AutoFeatureConfig feature = project.Features[slot];
                    feature.ImageFile = Path.GetFileName(path);
                    Rectangle capBounds = CurrentCaptureBounds();
                    feature.CaptureWidth = capBounds.Width;
                    feature.CaptureHeight = capBounds.Height;
                    float anchorX = (float)(form.ResultCenterX - capBounds.Left) / (float)capBounds.Width;
                    float anchorY = (float)(form.ResultCenterY - capBounds.Top) / (float)capBounds.Height;
                    feature.AnchorX = (anchorX >= 0f && anchorX <= 1f) ? anchorX : -1f;
                    feature.AnchorY = (anchorY >= 0f && anchorY <= 1f) ? anchorY : -1f;
                    feature.Enabled = true;
                    BindAutoPanels();
                    _autoModeSettings.SetStatus("已保存特征" + ModeConfig.ChineseNumber(slot + 1) + "：" + form.ResultWidth + " × " + form.ResultHeight + " px");
                    ScheduleSave();
                    ShowToast("特征" + ModeConfig.ChineseNumber(slot + 1) + " 已保存（" + form.ResultWidth + " × " + form.ResultHeight + " px）", ToastKind.Success);
                }
            }
            catch (Exception ex)
            {
                ConfigStore.Log("特征截取异常: " + ex.Message);
                ShowToast("特征截取失败：" + ex.Message, ToastKind.Error);
            }
            finally
            {
                _capturingFeature = false;
                if (restoreSettings)
                {
                    if (previousState == FormWindowState.Minimized)
                    {
                        Show();
                        WindowState = FormWindowState.Minimized;
                        try { if (_runtime != null) _runtime.SetSettingsVisible(false); } catch { }
                    }
                    else
                    {
                        ShowSettings();
                    }
                }
                else
                {
                    try { if (_runtime != null) _runtime.SetSettingsVisible(false); } catch { }
                }
            }
        }

        private void ChooseStoryImage(int target, int imageIndex)
        {
            AppConfig root = Root;
            if (root == null || root.StorySkip == null) return;
            if (target < 0 || target > 2) target = 0;
            if (imageIndex < 0 || imageIndex > 2) imageIndex = 0;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择目标 " + (target + 1) + " 的图片 " + (imageIndex + 1);
                dialog.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ShowLocalFeatureTip();
                try
                {
                    using (Bitmap source = new Bitmap(dialog.FileName))
                    using (ImageCropForm crop = new ImageCropForm(source))
                    {
                        if (crop.ShowDialog(this) != DialogResult.OK || crop.ResultBitmap == null) return;
                        string path = ConfigStore.StorySkipPath(target, imageIndex);
                        using (Bitmap result = crop.ResultBitmap)
                            result.Save(path, ImageFormat.Png);
                        StorySkipFeatureConfig feature = root.StorySkip.Features[target];
                        StorySkipImageConfig image = feature.Images[imageIndex];
                        image.ImageFile = Path.GetFileName(path);
                        Rectangle capBounds = CurrentCaptureBounds();
                        image.CaptureWidth = capBounds.Width;
                        image.CaptureHeight = capBounds.Height;
                        image.AnchorX = -1f;
                        image.AnchorY = -1f;
                        feature.Enabled = true;
                        _storySkipSettings.RefreshImage(target);
                        ScheduleSave();
                        ShowToast("目标 " + (target + 1) + " 图片 " + (imageIndex + 1) + " 已选择并裁剪",
                            ToastKind.Success);
                    }
                }
                catch (Exception ex)
                {
                    ConfigStore.Log("选择跳过剧情图片失败: " + ex.Message);
                    ShowToast("选择图片失败：" + ex.Message, ToastKind.Error);
                }
            }
        }

        private void BeginStoryCapture(int slot, int imageIndex)
        {
            AppConfig root = Root;
            if (_capturingFeature || root == null || root.StorySkip == null) return;
            if (slot < 0 || slot > 2) slot = 0;
            if (imageIndex < 0 || imageIndex > 2) imageIndex = 0;
            _capturingFeature = true;
            bool restoreSettings = Visible;
            FormWindowState previousState = WindowState;
            if (restoreSettings) Hide();
            try { if (_runtime != null) _runtime.SetSettingsVisible(true); } catch { }
            int captureSlot = slot;
            int captureImage = imageIndex;
            Timer delay = new Timer();
            delay.Interval = 240;
            delay.Tick += delegate
            {
                delay.Stop();
                RunStoryCapture(captureSlot, captureImage, restoreSettings, previousState);
                delay.Dispose();
            };
            delay.Start();
        }

        private void RunStoryCapture(int slot, int imageIndex, bool restoreSettings, FormWindowState previousState)
        {
            try
            {
                using (FeatureCaptureForm form = new FeatureCaptureForm())
                {
                    if (!form.CaptureOk)
                    {
                        ShowToast("截屏失败：无法读取当前屏幕", ToastKind.Error);
                        return;
                    }
                    if (form.ShowDialog() != DialogResult.OK || form.ResultBitmap == null) return;
                    AppConfig root = Root;
                    if (root == null || root.StorySkip == null) return;
                    StorySkipFeatureConfig feature = root.StorySkip.Features[slot];
                    string path = ConfigStore.StorySkipPath(slot, imageIndex);
                    try
                    {
                        using (Bitmap bmp = form.ResultBitmap) bmp.Save(path, ImageFormat.Png);
                    }
                    catch (Exception ex)
                    {
                        ConfigStore.Log("保存跳过剧情截图失败: " + ex.Message);
                        ShowToast("保存截图失败，请检查目录权限", ToastKind.Error);
                        return;
                    }
                    StorySkipImageConfig image = feature.Images[imageIndex];
                    image.ImageFile = Path.GetFileName(path);
                    Rectangle capBounds = CurrentCaptureBounds();
                    image.CaptureWidth = capBounds.Width;
                    image.CaptureHeight = capBounds.Height;
                    float anchorX = (float)(form.ResultCenterX - capBounds.Left) / (float)capBounds.Width;
                    float anchorY = (float)(form.ResultCenterY - capBounds.Top) / (float)capBounds.Height;
                    image.AnchorX = (anchorX >= 0f && anchorX <= 1f) ? anchorX : -1f;
                    image.AnchorY = (anchorY >= 0f && anchorY <= 1f) ? anchorY : -1f;
                    feature.Enabled = true;
                    _storySkipSettings.RefreshImage(slot);
                    ScheduleSave();
                    ShowToast("目标 " + (slot + 1) + " 图片 " + (imageIndex + 1) + " 已保存（" +
                        form.ResultWidth + " × " + form.ResultHeight + " px）", ToastKind.Success);
                }
            }
            catch (Exception ex)
            {
                ConfigStore.Log("跳过剧情截图异常: " + ex.Message);
                ShowToast("截图失败：" + ex.Message, ToastKind.Error);
            }
            finally
            {
                _capturingFeature = false;
                if (restoreSettings)
                {
                    if (previousState == FormWindowState.Minimized)
                    {
                        Show();
                        WindowState = FormWindowState.Minimized;
                        try { if (_runtime != null) _runtime.SetSettingsVisible(false); } catch { }
                    }
                    else ShowSettings();
                }
                else
                {
                    try { if (_runtime != null) _runtime.SetSettingsVisible(false); } catch { }
                }
            }
        }

        private void HideToTray(bool silent)        {
            if (!Visible && WindowState != FormWindowState.Minimized) return;
            if (_tray != null) _tray.Visible = true;
            try { Hide(); }
            catch (Exception ex) { ConfigStore.Log("隐藏主窗口失败: " + ex.Message); }
            ConfigStore.Log(silent ? "窗口隐藏到托盘" : "关闭按钮 -> 隐藏到托盘");
            if (!silent && _tray != null)
            {
                try
                {
                    _tray.ShowBalloonTip(3000, "移动端UI",
                        "程序仍在托盘运行。双击托盘图标打开设置，右键可退出。", ToolTipIcon.Info);
                }
                catch { }
            }
            try
            {
                if (_runtime != null) _runtime.SetSettingsVisible(false);
            }
            catch (Exception ex)
            {
                ConfigStore.Log("关闭时暂停悬浮层失败: " + ex.Message);
            }
        }

        private void ShowSettings()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            Activate();
            BringToFront();
            if (_runtime != null) _runtime.SetSettingsVisible(true);
        }

        // 启动游戏后最小化到任务栏：窗口全程可见、任务栏按钮保留，不 Hide 到托盘；
        // 最小化期间暂停设置遮挡，恢复悬浮层给游戏操作。
        private void MinimizeToTaskbar()
        {
            ShowInTaskbar = true;
            WindowState = FormWindowState.Minimized;
            if (_tray != null) _tray.Visible = true;
            try
            {
                if (_runtime != null) _runtime.SetSettingsVisible(false);
            }
            catch (Exception ex)
            {
                ConfigStore.Log("启动游戏后最小化任务栏失败: " + ex.Message);
            }
            ConfigStore.Log("启动游戏后窗口最小化到任务栏");
        }

        private void SetupTray()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Renderer = new DarkMenuRenderer();
            _trayMenu.BackColor = Theme.SurfaceAlt;
            _trayMenu.ForeColor = Theme.Text;
            _trayMenu.ShowImageMargin = false;

            ToolStripMenuItem open = new ToolStripMenuItem("打开设置界面");
            open.Click += delegate
            {
                try { _trayMenu.Close(); } catch { }
                BeginInvoke(new System.Windows.Forms.MethodInvoker(ShowSettings));
            };
            _trayMenu.Items.Add(open);

            _trayEnvItem = new ToolStripMenuItem("环境：检测中");
            _trayEnvItem.Enabled = false;
            _trayMenu.Items.Add(_trayEnvItem);

            _trayOverlayItem = new ToolStripMenuItem("悬浮层：等待串流");
            _trayOverlayItem.Enabled = false;
            _trayMenu.Items.Add(_trayOverlayItem);

            ToolStripMenuItem recheck = new ToolStripMenuItem("重新检测串流");
            recheck.Click += delegate { RecheckEnvironment(); };
            _trayMenu.Items.Add(recheck);
            _trayMenu.Items.Add(new ToolStripSeparator());

            _trayAutoStartItem = new ToolStripMenuItem("开机后台待命");
            _trayAutoStartItem.CheckOnClick = false;
            _trayAutoStartItem.Checked = _config.RunAtLogin;
            _trayAutoStartItem.Click += delegate
            {
                bool enabled = !_config.RunAtLogin;
                _launchCard.RunAtLoginChecked = enabled;
                RunAtLoginToggleChanged();
            };
            _trayMenu.Items.Add(_trayAutoStartItem);

            _trayAutoExitItem = new ToolStripMenuItem("游戏结束后自动退出");
            _trayAutoExitItem.CheckOnClick = false;
            _trayAutoExitItem.Checked = _config.AutoExitOnGameEnd;
            _trayAutoExitItem.Click += delegate
            {
                _config.AutoExitOnGameEnd = !_config.AutoExitOnGameEnd;
                _trayAutoExitItem.Checked = _config.AutoExitOnGameEnd;
                ScheduleSave();
            };
            _trayMenu.Items.Add(_trayAutoExitItem);
            _trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate
            {
                // 先关掉菜单、置退出标志并保存，再异步 Close：菜单项 Click 栈内同步 Close 会和
                // 菜单/窗体句柄释放竞争，导致第一次退出被窗口关闭拦截逻辑弹回托盘、要多点几次。
                try { _trayMenu.Close(); } catch { }
                _allowExit = true;
                try { if (_tray != null) _tray.Visible = false; } catch { }
                try { SaveNow(true); } catch { }
                try { BeginInvoke(new System.Windows.Forms.MethodInvoker(delegate { Close(); })); }
                catch { Close(); }
            };
            _trayMenu.Items.Add(exit);

            _tray = new NotifyIcon();
            Icon trayIcon = Icon;
            if (trayIcon == null)
            {
                try { trayIcon = AppIcon.LoadIcon(); }
                catch { }
            }
            _tray.Icon = trayIcon == null ? SystemIcons.Application : trayIcon;
            _tray.Text = "移动端UI";
            // 不挂 ContextMenuStrip：主窗口隐藏在托盘时，系统把菜单归属隐藏宿主，容易被游戏全屏
            // 置顶窗口压到下层，右键看不到菜单。改为右键在鼠标位置手动弹出并前置，左键照旧打开设置。
            _tray.Visible = true;
            _tray.DoubleClick += delegate { BeginInvoke(new System.Windows.Forms.MethodInvoker(ShowSettings)); };
            _tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    BeginInvoke(new System.Windows.Forms.MethodInvoker(ShowSettings));
                }
                else if (e.Button == MouseButtons.Right)
                {
                    try
                    {
                        _trayMenu.Show(Cursor.Position);
                        _trayMenu.BringToFront();
                        _trayMenu.Focus();
                    }
                    catch { }
                }
            };
        }

        private void SetupRuntime()
        {
            _runtime = new RuntimeService(_config);
            _runtime.EnvironmentChanged = delegate(bool stream, string reason)
            {
                UpdateEnvironmentUi(stream, reason);
            };
            _runtime.OnGameEnded = delegate { OnGameEnded(); };
            _runtime.ForegroundChanged = delegate(bool foreground) { OnGameForegroundChanged(foreground); };
            _runtime.OverlayLayoutChanged = delegate
            {
                _preview.Invalidate();
                ScheduleSave();
            };
            _runtime.ModeChangedByOverlay = delegate(int index)
            {
                ApplyOverlayMode(index);
            };
            _runtime.Notify = delegate(string message)
            {
                ShowToast(message, ToastKind.Info);
            };
            _runtime.OverlayStateChanged = delegate
            {
                UpdateTrayOverlayState();
            };
            _runtime.BlockMouseClicksToggled = delegate
            {
                if (_inputAssist == null) return;
                _inputAssist.ToggleBlockMouseClicks();
                bool on = _inputAssist.BlockMouseClicks;
                // 同步到悬浮层空白捕获遮罩：原生触摸不经过低级鼠标钩子，仅靠钩子拦不住，
                // 必须在按键页空白区补一层遮罩吞掉穿透到游戏的触摸/鼠标按下。
                try { if (_runtime != null) _runtime.SetBlockBlankClicks(on); } catch { }
                // 以输入辅助服务切换后的真实状态为准，在悬浮层屏幕正中弹出开/关汉字提示。
                if (_runtime != null)
                    _runtime.ShowOverlayNotice(on
                        ? "拦截鼠标点击：已开启" : "拦截鼠标点击：已关闭");
            };
            _runtime.StorySkipToggled = delegate { ToggleStorySkipNow(); };
        }

        // 切换当前页根「自动跳过剧情」总开关：显示键动作与全局快捷键共用同一入口。
        // 切换后重置识别候选队列、落盘保存，并在游戏画面正中弹出开/关汉字提示。
        private void ToggleStorySkipNow()
        {
            if (_config == null || Root == null || Root.StorySkip == null) return;
            Root.StorySkip.Enabled = !Root.StorySkip.Enabled;
            if (_storySkip != null) _storySkip.Apply();
            // 设置面板若可见，同步顶部总开关控件状态（SetConfig 内部绑定态不触发 Changed）。
            try { _storySkipSettings.SetConfig(Root.StorySkip); } catch { }
            ScheduleSave();
            if (_runtime != null)
                _runtime.ShowOverlayNotice(Root.StorySkip.Enabled
                    ? "跳过剧情：已开启" : "跳过剧情：已关闭");
        }

        private void UpdateTrayOverlayState()
        {
            if (_trayOverlayItem == null) return;
            bool stream = _runtime != null && _runtime.StreamDetected;
            bool active = _runtime != null && _runtime.OverlayActive;
            if (!stream) _trayOverlayItem.Text = "悬浮层：本地模式不加载";
            else if (active) _trayOverlayItem.Text = "悬浮层：已加载";
            else _trayOverlayItem.Text = "悬浮层：等待游戏";
        }

        private void UpdateEnvironmentUi(bool stream, string reason)
        {
            if (_titleBar != null)
            {
                _titleBar.SetEnvironmentMode(stream);
            }
            if (_inputAssist != null && _runtime != null)
            {
                // Global input hooks are only installed while the game is in
                // the foreground, never while sitting on the desktop.
                _inputAssist.SetContextActive(_runtime.GameForeground);
            }
            if (_trayEnvItem != null)
            {
                _trayEnvItem.Text = stream ? "环境：串流模式" : "环境：本地模式";
            }
            if (_tray != null)
            {
                string text = "移动端UI · " + (stream ? "串流模式" : "本地模式");
                _tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            }
            if (stream && _runtime != null && _runtime.OverlayActive)
            {
                if (_trayOverlayItem != null) _trayOverlayItem.Text = "悬浮层：已加载";
            }
            else
            {
                UpdateTrayOverlayState();
            }
        }

        private void RecheckEnvironment()
        {
            if (_titleBar != null) _titleBar.SetEnvironmentChecking();
            if (_runtime != null) _runtime.Recheck();
        }

        private void TitleBarLocalUiChanged(object sender, EventArgs e)
        {
            if (_config == null || _syncingUiModes) return;
            _config.LocalOverlayEnabled = _titleBar.LocalUiChecked;
            ScheduleSave();
            if (_runtime != null) _runtime.RefreshConfig();
        }

        private void TitleBarStreamUiChanged(object sender, EventArgs e)
        {
            if (_config == null || _syncingUiModes) return;
            _config.StreamOverlayEnabled = _titleBar.StreamUiChecked;
            ScheduleSave();
            if (_runtime != null) _runtime.RefreshConfig();
        }

        // 标题栏「虚拟按键 / 虚拟手柄」互斥切换：切换配置根并重载整个编辑界面。
        private void TitleBarPageSideChanged(object sender, EventArgs e)
        {
            if (_config == null || _loading) return;
            _padSide = _titleBar.PageSide;
            _config.UiPadSide = _padSide;
            ApplyConfigToUi();
            ScheduleSave();
            // 真机叠加层按当前页生效根重建：按键页读主根、手柄页读独立手柄根，互不串用。
            if (_runtime != null) _runtime.RefreshConfig();
        }

        private void RunAtLoginToggleChanged()
        {
            if (_syncingAutoStart) return;
            bool enabled = _launchCard.RunAtLoginChecked;
            bool ok = AutoStart.SetEnabled(enabled);
            if (!ok)
            {
                _syncingAutoStart = true;
                _launchCard.RunAtLoginChecked = !enabled;
                _syncingAutoStart = false;
                ShowToast("设置开机后台待命失败，请检查系统权限。", ToastKind.Error);
                return;
            }
            _config.RunAtLogin = enabled;
            if (_trayAutoStartItem != null) _trayAutoStartItem.Checked = enabled;
            ScheduleSave();
            ShowToast(enabled ? "已开启开机后台待命，游戏启动时自动加载串流悬浮层。" : "已关闭开机后台待命。",
                ToastKind.Success);
        }

        private void OnGameForegroundChanged(bool foreground)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                BeginInvoke(new System.Windows.Forms.MethodInvoker(delegate { OnGameForegroundChanged(foreground); }));
                return;
            }
            if (_inputAssist != null) _inputAssist.SetContextActive(foreground);
            if (_memoryScan != null) _memoryScan.SetPaused(!foreground);
            if (!foreground)
            {
                try { InputSender.ReleaseAll(); } catch { }
                try { RumbleService.Stop(); } catch { }
                UnregisterFeatureHotkey();
            }
            else
            {
                UpdateFeatureHotkeyRegistration();
                // 游戏回到前台：设置窗口不再遮挡，复位设置挂起态并重建悬浮层，
                // 修复从设置界面切回游戏后虚拟按键滞留消失的问题。
                try { if (_runtime != null) _runtime.SetSettingsVisible(false); } catch { }
            }
            ConfigStore.Log(foreground
                ? "游戏前台：恢复悬浮层、输入、识别和按键映射"
                : "游戏后台：暂停悬浮层、输入、识别和按键映射");
        }

        private void OnCooldownSnapshot(MemoryCooldownSnapshot snapshot)
        {
            if (snapshot == null) return;
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<MemoryCooldownSnapshot>(OnCooldownSnapshot), snapshot); } catch { }
                return;
            }
            AppConfig root = Root;
            if (root != null && root.Modes != null)
            {
                for (int m = 0; m < root.Modes.Count; m++)
                {
                    ModeConfig mode = root.Modes[m];
                    if (mode == null || mode.Keys == null) continue;
                    for (int i = 0; i < mode.Keys.Count; i++)
                    {
                        VirtualKey key = mode.Keys[i];
                        if (key == null) continue;
                        double seconds;
                        bool valid = snapshot.Values.TryGetValue(key.Id, out seconds) && snapshot.Valid.Contains(key.Id);
                        key.CooldownRemaining = valid ? seconds : 0;
                        key.CooldownValid = valid;
                    }
                }
            }
            if (_cooldownSettings != null) _cooldownSettings.UpdateSnapshot(snapshot);
            if (_runtime != null) _runtime.RefreshCooldownDisplay();
        }

        private void OnGameEnded()
        {            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(OnGameEnded));
                return;
            }
            if (_allowExit || _gameEndedHandled) return;
            _gameEndedHandled = true;
            try { if (_memoryScan != null) { _memoryScan.SetPaused(true); _memoryScan.Apply(); } } catch { }
            try { if (_inputAssist != null) _inputAssist.SetContextActive(false); } catch { }
            try { if (_storySkip != null) _storySkip.Apply(); } catch { }
            try { InputSender.ReleaseAll(); } catch { }
            try { RumbleService.Stop(); } catch { }
            UnregisterFeatureHotkey();
            // 不再 _runtime.Stop()：游戏结束后保留托盘运行，仅标记会话结束，
            // 清空 _gameRunning/_gameEnded 等状态，避免下次启动游戏时被
            // 「游戏已经在运行」卡住
            try { if (_runtime != null) _runtime.MarkSessionEnded(); } catch { }
            if (_config != null && _config.AutoExitOnGameEnd)
            {
                _allowExit = true;
                Close();
                return;
            }
            try { if (_runtime != null) _runtime.SetSettingsVisible(Visible); } catch { }
            ShowToast("游戏已结束，程序仍在托盘运行。", ToastKind.Info);
            UpdateTrayOverlayState();
        }

        private void ApplyOverlayMode(int index)
        {
            if (_config == null || index < 0 || index >= Root.Modes.Count) return;
            _editingOverlay = false;
            Root.SelectedMode = index;
            for (int i = 0; i < _modeTabs.Count; i++) _modeTabs[i].Selected = i == index;
            _preview.ActiveModeIndex = index;
            _preview.SetViewState(0);
            ModeConfig mode = Root.Modes[index];
            VirtualKey first = mode.Keys != null && mode.Keys.Count > 0 ? mode.Keys[0] : null;
            _preview.SetSelectedKeySilent(first);
            _preview.SetSelectedOverlayKeySilent(null);
            _editor.SetOverlayModes(Root.OverlayModes);
            _editor.SetMode(mode, first);
            _joystickSettings.SetConfig(mode == null ? null : mode.Joystick, mode == null ? null : mode.JoystickR,
                mode == null ? null : mode.Dpad);
            if (_cooldownSettings != null) _cooldownSettings.SetKey(first);
            if (index < _modeTabs.Count) _modeFlow.ScrollControlIntoView(_modeTabs[index]);
            BindAutoPanels();
            ScheduleSave();
            PersistCurrentProfile();
        }

        private void MainFormOnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.S)
            {
                e.SuppressKeyPress = true;
                _saveTimer.Stop();
                SaveNow(false);
                ShowToast("配置已保存。", ToastKind.Success);
            }
            else if (e.KeyCode == Keys.F7)
            {
                e.SuppressKeyPress = true;
                ToggleOrientation();
            }
        }

        private void TitleBarOnProjectNameChanged(object sender, EventArgs e)
        {
            if (_loading || _config == null) return;
            _config.ProjectName = _titleBar.ProjectName;
            ScheduleSave();
        }

        private void OnLaunchSettingChanged()
        {
            if (_loading || _config == null) return;
            _config.GamePath = _launchCard.ProgramPath;
            _config.GameArguments = _launchCard.Arguments;
            ScheduleSave();
        }

        private void OnLeftCardStateChanged(CardDockItem item)
        {
            if (_config == null || item == null) return;
            if (item.Slot < 0 || item.Slot >= Root.LeftCardCollapsed.Count) return;
            Root.LeftCardCollapsed[item.Slot] = item.Collapsed;
            ScheduleSave();
            RefreshLeftExpandButton();
        }

        private void RefreshLeftExpandButton()
        {
            if (_deckDock == null || _expandAllLeftButton == null) return;
            _expandAllLeftButton.Text = _deckDock.AnyCollapsed ? "全部展开" : "全部收起";
        }

        private void ScheduleSave()
        {
            if (_loading) return;
            if (_saveTimer == null) return;
            _saveTimer.Stop();
            _saveTimer.Start();
            _titleBar.SetSaveState("正在保存…", Theme.Gold);
        }

        private bool SaveNow(bool silent)
        {
            if (_config == null) return false;
            _config.ProjectName = _titleBar.ProjectName;
            _config.GamePath = _launchCard.ProgramPath;
            _config.GameArguments = _launchCard.Arguments;
            bool ok = ConfigStore.Save(_config);
            if (ok && !string.IsNullOrEmpty(_currentProfilePath) && File.Exists(_currentProfilePath))
            {
                // Keep each game's own XML in sync with the path/args and every
                // other setting, even when the user only edited and switched.
                try { ConfigStore.SaveToIfChanged(_currentProfilePath, _config); } catch { }
                try { ProfileStore.SaveAssets(_currentProfilePath, _config); } catch { }
            }
            if (ok)
            {
                _titleBar.SetSaveState("已自动保存 " + Theme.ShortTime(), Theme.Mint);
            }
            else
            {
                _titleBar.SetSaveState("保存失败，请检查权限", Theme.Danger);
                if (!silent) ShowToast("保存失败，请检查程序目录的写入权限。", ToastKind.Error);
            }
            return ok;
        }

        private void RebuildModeTabs()
        {
            _modeFlow.SuspendLayout();
            for (int i = 0; i < _modeTabs.Count; i++) _modeTabs[i].Dispose();
            _modeTabs.Clear();
            for (int i = 0; i < Root.Modes.Count; i++)
            {
                ModeTab tab = new ModeTab();
                tab.Index = i;
                tab.ModeName = Root.Modes[i].Name;
                tab.Selected = i == Root.SelectedMode;
                tab.SelectedRequested += ModeTabOnSelected;
                tab.RenameRequested += ModeTabOnDoubleClick;
                _modeTabs.Add(tab);
                _modeFlow.Controls.Add(tab);
            }
            _modeFlow.ResumeLayout();
            if (Root.SelectedMode >= 0 && Root.SelectedMode < _modeTabs.Count)
            {
                _modeFlow.ScrollControlIntoView(_modeTabs[Root.SelectedMode]);
            }
            BindAutoPanels();
            if (_joystickSettings != null) _joystickSettings.SetModes(Root.Modes);
            if (_systemKeys != null) _systemKeys.SetModes();
            RebuildOverlayTabs();
        }

        // 重建叠加模式标签条；仅刷新标签与选中态，不改动主模式编辑器绑定。
        private void RebuildOverlayTabs()
        {
            if (_overlayFlow == null || _config == null || Root.OverlayModes == null) return;
            _overlayFlow.SuspendLayout();
            for (int i = 0; i < _overlayTabs.Count; i++) _overlayTabs[i].Dispose();
            _overlayTabs.Clear();
            for (int i = 0; i < Root.OverlayModes.Count; i++)
            {
                ModeTab tab = new ModeTab();
                tab.Index = i;
                tab.ModeName = Root.OverlayModes[i].Name;
                tab.Selected = i == Root.SelectedOverlayMode;
                tab.SelectedRequested += OverlayTabOnSelected;
                tab.RenameRequested += OverlayTabOnDoubleClick;
                _overlayTabs.Add(tab);
                _overlayFlow.Controls.Add(tab);
            }
            _overlayFlow.ResumeLayout();
            if (Root.SelectedOverlayMode >= 0 && Root.SelectedOverlayMode < _overlayTabs.Count)
            {
                _overlayFlow.ScrollControlIntoView(_overlayTabs[Root.SelectedOverlayMode]);
            }
        }

        private void OverlayTabOnSelected(object sender, EventArgs e)
        {
            ModeTab tab = sender as ModeTab;
            if (tab == null) return;
            SelectOverlayMode(tab.Index);
        }

        private void OverlayTabOnDoubleClick(object sender, EventArgs e)
        {
            RenameOverlayMode();
        }

        // 两个自动切换面板都绑定到当前页生效根，保证按键页/手柄页配置互不串味。
        private void BindAutoPanels()
        {
            AppConfig root = Root;
            if (root == null) return;
            if (_autoModeSettings != null) _autoModeSettings.SetConfig(root);
            if (_memoryScanSettings != null) _memoryScanSettings.SetConfig(root);
        }

        private void ModeTabOnSelected(object sender, EventArgs e)
        {
            ModeTab tab = sender as ModeTab;
            if (tab == null) return;
            SelectMode(tab.Index);
        }

        private void ModeTabOnDoubleClick(object sender, EventArgs e)
        {
            RenameMode();
        }

        private void SelectMode(int index)
        {
            if (_config == null || index < 0 || index >= Root.Modes.Count) return;
            _editingOverlay = false;
            Root.SelectedMode = index;
            for (int i = 0; i < _modeTabs.Count; i++) _modeTabs[i].Selected = i == index;
            _preview.ActiveModeIndex = index;
            _preview.SetViewState(0);
            ModeConfig mode = Root.Modes[index];
            VirtualKey first = mode.Keys != null && mode.Keys.Count > 0 ? mode.Keys[0] : null;
            _preview.SetSelectedKeySilent(first);
            _preview.SetSelectedOverlayKeySilent(null);
            _editor.SetOverlayModes(Root.OverlayModes);
            _editor.SetMode(mode, first);
            _joystickSettings.SetConfig(mode == null ? null : mode.Joystick, mode == null ? null : mode.JoystickR,
                mode == null ? null : mode.Dpad);
            if (_cooldownSettings != null) _cooldownSettings.SetKey(first);
            if (index < _modeTabs.Count) _modeFlow.ScrollControlIntoView(_modeTabs[index]);
            BindAutoPanels();
            ScheduleSave();
            PersistCurrentProfile();
        }

        // 切换到叠加模式编辑态：右侧按键面板绑定到选中叠加模式的按键列表。
        private void SelectOverlayMode(int index)
        {
            if (_config == null || Root.OverlayModes == null) return;
            if (index < 0 || index >= Root.OverlayModes.Count) return;
            Root.SelectedOverlayMode = index;
            _editingOverlay = true;
            for (int i = 0; i < _overlayTabs.Count; i++) _overlayTabs[i].Selected = i == index;
            OverlayModeConfig overlay = Root.OverlayModes[index];
            VirtualKey first = overlay.Keys != null && overlay.Keys.Count > 0 ? overlay.Keys[0] : null;
            _preview.SetSelectedOverlayKeySilent(first);
            _editor.SetOverlayMode(overlay, first);
            if (_cooldownSettings != null) _cooldownSettings.SetKey(first);
            if (index < _overlayTabs.Count) _overlayFlow.ScrollControlIntoView(_overlayTabs[index]);
            _editor.SetOverlayModes(Root.OverlayModes);
            ScheduleSave();
            PersistCurrentProfile();
        }

        private void CloneOverlayMode()
        {
            if (Root.OverlayModes.Count >= 12)
            {
                ShowToast("叠加模式最多支持 12 个，可重命名现有模式。", ToastKind.Error);
                return;
            }
            OverlayModeConfig source = Root.ActiveOverlayMode;
            if (source == null)
            {
                ShowToast("请先选择要克隆的叠加模式。", ToastKind.Error);
                return;
            }
            int n = Root.OverlayModes.Count + 1;
            OverlayModeConfig clone = source.CloneLayout("叠加" + ModeConfig.ChineseNumber(n));
            Root.OverlayModes.Add(clone);
            Root.SelectedOverlayMode = Root.OverlayModes.Count - 1;
            RebuildOverlayTabs();
            SelectOverlayMode(Root.SelectedOverlayMode);
            ScheduleSave();
            ShowToast("已克隆「" + source.Name + "」为「" + clone.Name + "」，双击标签可重命名。", ToastKind.Success);
        }

        private void AddOverlayMode()
        {
            if (Root.OverlayModes.Count >= 12)
            {
                ShowToast("叠加模式最多支持 12 个，可重命名现有模式。", ToastKind.Error);
                return;
            }
            int n = Root.OverlayModes.Count + 1;
            OverlayModeConfig overlay = new OverlayModeConfig("叠加" + ModeConfig.ChineseNumber(n));
            Root.OverlayModes.Add(overlay);
            Root.SelectedOverlayMode = Root.OverlayModes.Count - 1;
            RebuildOverlayTabs();
            SelectOverlayMode(Root.SelectedOverlayMode);
            ScheduleSave();
            ShowToast("已添加「" + overlay.Name + "」，双击标签可重命名。", ToastKind.Success);
        }

        private void RenameOverlayMode()
        {
            OverlayModeConfig overlay = Root.ActiveOverlayMode;
            if (overlay == null) return;
            string name = PromptDialog.ShowRename(this, overlay.Name);
            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0) return;
            if (name.Length > 24) name = name.Substring(0, 24);
            overlay.Name = name;
            RebuildOverlayTabs();
            _editor.SetOverlayModes(Root.OverlayModes);
            _editor.SetOverlayMode(overlay, _editor.SelectedKey);
            _preview.Invalidate();
            ScheduleSave();
            ShowToast("叠加模式已重命名为「" + name + "」。", ToastKind.Success);
        }

        private void DeleteOverlayMode()
        {
            if (Root.OverlayModes.Count <= 1)
            {
                ShowToast("至少保留 1 个叠加模式。", ToastKind.Error);
                return;
            }
            OverlayModeConfig overlay = Root.ActiveOverlayMode;
            if (overlay == null) return;
            bool ok = PromptDialog.Confirm(this, "删除叠加模式",
                "确定删除「" + overlay.Name + "」及其全部按键布局吗？此操作不可恢复。", "删除叠加模式");
            if (!ok) return;
            int index = Root.SelectedOverlayMode;
            Root.OverlayModes.Remove(overlay);
            // 清空主按键上指向该叠加模式的呼出绑定，避免索引悬空。
            for (int m = 0; m < Root.Modes.Count; m++)
            {
                if (Root.Modes[m] == null || Root.Modes[m].Keys == null) continue;
                for (int k = 0; k < Root.Modes[m].Keys.Count; k++)
                {
                    VirtualKey key = Root.Modes[m].Keys[k];
                    if (key == null) continue;
                    if (key.OverlayTargetMode > index) key.OverlayTargetMode--;
                    else if (key.OverlayTargetMode == index) key.OverlayEnabled = false;
                }
            }
            Root.SelectedOverlayMode = Math.Max(0, Math.Min(index, Root.OverlayModes.Count - 1));
            RebuildOverlayTabs();
            SelectOverlayMode(Root.SelectedOverlayMode);
            ScheduleSave();
            ShowToast("叠加模式已删除。", ToastKind.Info);
        }

        private void CloneMode()
        {
            if (Root.Modes.Count >= 12)
            {
                ShowToast("最多支持 12 个模式，可重命名现有模式。", ToastKind.Error);
                return;
            }
            ModeConfig source = Root.ActiveMode;
            if (source == null)
            {
                ShowToast("请先选择要克隆的模式。", ToastKind.Error);
                return;
            }
            int n = Root.Modes.Count + 1;
            ModeConfig clone = source.CloneLayout("模式" + ModeConfig.ChineseNumber(n));
            Root.Modes.Add(clone);
            Root.SelectedMode = Root.Modes.Count - 1;
            _editingOverlay = false;
            RebuildModeTabs();
            _preview.ActiveModeIndex = Root.SelectedMode;
            _preview.SetViewState(0);
            _preview.SetSelectedKeySilent(null);
            _preview.SetSelectedOverlayKeySilent(null);
            _editor.SetMode(clone, null);
            _joystickSettings.SetConfig(clone.Joystick, clone.JoystickR, clone.Dpad);
            if (_cooldownSettings != null) _cooldownSettings.SetKey(null);
            ScheduleSave();
            ShowToast("已克隆「" + source.Name + "」为「" + clone.Name + "」，双击标签可重命名。", ToastKind.Success);
        }

        private void AddMode()
        {
            if (Root.Modes.Count >= 12)
            {
                ShowToast("最多支持 12 个模式，可重命名现有模式。", ToastKind.Error);
                return;
            }
            int n = Root.Modes.Count + 1;
            ModeConfig mode = new ModeConfig("模式" + ModeConfig.ChineseNumber(n));
            Root.Modes.Add(mode);
            Root.SelectedMode = Root.Modes.Count - 1;
            _editingOverlay = false;
            RebuildModeTabs();
            _preview.ActiveModeIndex = Root.SelectedMode;
            _preview.SetViewState(0);
            _preview.SetSelectedKeySilent(null);
            _preview.SetSelectedOverlayKeySilent(null);
            _editor.SetMode(mode, null);
            _joystickSettings.SetConfig(mode == null ? null : mode.Joystick, mode == null ? null : mode.JoystickR,
                mode == null ? null : mode.Dpad);
            if (_cooldownSettings != null) _cooldownSettings.SetKey(null);
            ScheduleSave();
            ShowToast("已添加「" + mode.Name + "」，双击标签可重命名。", ToastKind.Success);
        }

        private void RenameMode()
        {
            // 按钮按所属行分流：主行重命名始终操作主模式，先切回主模式编辑态。
            _editingOverlay = false;
            ModeConfig mode = Root.ActiveMode;
            if (mode == null) return;
            string name = PromptDialog.ShowRename(this, mode.Name);
            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0) return;
            if (name.Length > 24) name = name.Substring(0, 24);
            mode.Name = name;
            RebuildModeTabs();
            _preview.SetSelectedOverlayKeySilent(null);
            _editor.SetMode(mode, _editor.SelectedKey);
            _preview.Invalidate();
            ScheduleSave();
            ShowToast("模式已重命名为「" + name + "」。", ToastKind.Success);
        }

        private void DeleteMode()
        {
            // 按钮按所属行分流：主行删除始终操作主模式，先切回主模式编辑态，
            // 不受当前是否正在编辑叠加模式影响。
            _editingOverlay = false;
            if (Root.Modes.Count <= 1)
            {
                ShowToast("至少保留 1 个主模式。", ToastKind.Error);
                return;
            }
            ModeConfig mode = Root.ActiveMode;
            if (mode == null) return;
            bool ok = PromptDialog.Confirm(this, "删除模式",
                "确定删除「" + mode.Name + "」及其全部按键布局、摇杆设置吗？此操作不可恢复。", "删除模式");
            if (!ok) return;
            int index = Root.SelectedMode;
            Root.Modes.Remove(mode);

            // 识别项目与模式内自动切换目标索引联动：指向被删模式的回落到首个模式，其后索引前移。
            for (int i = 0; i < Root.ImageProjects.Count; i++)
            {
                AutoImageProject p = Root.ImageProjects[i];
                if (p == null) continue;
                if (p.TargetMode == index) p.TargetMode = 0;
                else if (p.TargetMode > index) p.TargetMode--;
            }
            for (int i = 0; i < Root.MemoryProjects.Count; i++)
            {
                AutoMemoryProject p = Root.MemoryProjects[i];
                if (p == null) continue;
                if (p.TargetMode == index) p.TargetMode = 0;
                else if (p.TargetMode > index) p.TargetMode--;
            }
            for (int i = 0; i < Root.Modes.Count; i++)
            {
                ModeConfig m = Root.Modes[i];
                if (m == null) continue;
                if (m.AutoImageTargetMode == index) m.AutoImageTargetMode = 0;
                else if (m.AutoImageTargetMode > index) m.AutoImageTargetMode--;
                if (m.AutoMemoryTargetMode == index) m.AutoMemoryTargetMode = 0;
                else if (m.AutoMemoryTargetMode > index) m.AutoMemoryTargetMode--;
            }

            int newIndex = Math.Max(0, Math.Min(index, Root.Modes.Count - 1));
            RebuildModeTabs();
            SelectMode(newIndex);
            ScheduleSave();
            ShowToast("模式已删除。", ToastKind.Info);
        }

        // 图片识别自动切换项目：与界面预览的模式完全独立。
        private void SelectImageProject(int index)
        {
            if (_config == null || Root.ImageProjects == null) return;
            if (index < 0 || index >= Root.ImageProjects.Count) return;
            Root.SelectedImageProject = index;
            BindAutoPanels();
            ScheduleSave();
        }

        private void AddImageProject()
        {
            if (_config == null || Root.ImageProjects == null) return;
            if (Root.ImageProjects.Count >= 12)
            {
                ShowToast("最多支持 12 个项目，可重命名现有项目。", ToastKind.Error);
                return;
            }
            AutoImageProject project = new AutoImageProject();
            project.Normalize(Root.ImageProjects.Count);
            project.Name = "项目" + ModeConfig.ChineseNumber(Root.ImageProjects.Count + 1);
            Root.ImageProjects.Add(project);
            Root.SelectedImageProject = Root.ImageProjects.Count - 1;
            BindAutoPanels();
            ScheduleSave();
            ShowToast("已添加「" + project.Name + "」，双击标签可重命名。", ToastKind.Success);
        }

        private void RenameImageProject()
        {
            AutoImageProject project = _config == null ? null : Root.ActiveImageProject;
            if (project == null) return;
            string name = PromptDialog.ShowRename(this, project.Name);
            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0) return;
            if (name.Length > 24) name = name.Substring(0, 24);
            project.Name = name;
            BindAutoPanels();
            ScheduleSave();
            ShowToast("项目已重命名为「" + name + "」。", ToastKind.Success);
        }

        private void DeleteImageProject()
        {
            if (_config == null || Root.ImageProjects == null) return;
            int index = Root.SelectedImageProject;
            if (index < 0 || index >= Root.ImageProjects.Count) return;
            if (Root.ImageProjects.Count <= 1)
            {
                ShowToast("至少保留一个图片识别项目。", ToastKind.Error);
                return;
            }
            AutoImageProject project = Root.ImageProjects[index];
            bool ok = PromptDialog.Confirm(this, "删除项目",
                "确定删除「" + project.Name + "」吗？该项目的特征配置、阈值与目标模式都会被移除；目录里的图片文件保留。",
                "删除项目");
            if (!ok) return;
            Root.ImageProjects.RemoveAt(index);
            if (Root.SelectedImageProject >= Root.ImageProjects.Count)
                Root.SelectedImageProject = Root.ImageProjects.Count - 1;
            BindAutoPanels();
            ScheduleSave();
            ShowToast("项目「" + project.Name + "」已删除。", ToastKind.Info);
        }

        // 内存扫描自动切换项目：与界面预览的模式完全独立。
        private void SelectMemoryProject(int index)
        {
            if (_config == null || Root.MemoryProjects == null) return;
            if (index < 0 || index >= Root.MemoryProjects.Count) return;
            Root.SelectedMemoryProject = index;
            BindAutoPanels();
            ScheduleSave();
        }

        private void AddMemoryProject()
        {
            if (_config == null || Root.MemoryProjects == null) return;
            if (Root.MemoryProjects.Count >= 12)
            {
                ShowToast("最多支持 12 个项目，可重命名现有项目。", ToastKind.Error);
                return;
            }
            AutoMemoryProject project = new AutoMemoryProject();
            project.Normalize(Root.MemoryProjects.Count);
            project.Name = "项目" + ModeConfig.ChineseNumber(Root.MemoryProjects.Count + 1);
            Root.MemoryProjects.Add(project);
            Root.SelectedMemoryProject = Root.MemoryProjects.Count - 1;
            BindAutoPanels();
            ScheduleSave();
            ShowToast("已添加「" + project.Name + "」，双击标签可重命名。", ToastKind.Success);
        }

        private void RenameMemoryProject()
        {
            AutoMemoryProject project = _config == null ? null : Root.ActiveMemoryProject;
            if (project == null) return;
            string name = PromptDialog.ShowRename(this, project.Name);
            if (name == null) return;
            name = name.Trim();
            if (name.Length == 0) return;
            if (name.Length > 24) name = name.Substring(0, 24);
            project.Name = name;
            BindAutoPanels();
            ScheduleSave();
            ShowToast("项目已重命名为「" + name + "」。", ToastKind.Success);
        }

        private void DeleteMemoryProject()
        {
            if (_config == null || Root.MemoryProjects == null) return;
            int index = Root.SelectedMemoryProject;
            if (index < 0 || index >= Root.MemoryProjects.Count) return;
            if (Root.MemoryProjects.Count <= 1)
            {
                ShowToast("至少保留一个内存扫描项目。", ToastKind.Error);
                return;
            }
            AutoMemoryProject project = Root.MemoryProjects[index];
            bool ok = PromptDialog.Confirm(this, "删除项目",
                "确定删除「" + project.Name + "」吗？该项目的内存地址、偏移与目标模式都会被移除。",
                "删除项目");
            if (!ok) return;
            Root.MemoryProjects.RemoveAt(index);
            if (Root.SelectedMemoryProject >= Root.MemoryProjects.Count)
                Root.SelectedMemoryProject = Root.MemoryProjects.Count - 1;
            BindAutoPanels();
            ScheduleSave();
            ShowToast("项目「" + project.Name + "」已删除。", ToastKind.Info);
        }

        private void AddKey()
        {
            // 防双击：500ms 内只响应一次（Click 在某些重绘场景会被触发两次）
            if (_addKeyGuard != null && DateTime.Now - _addKeyGuard < TimeSpan.FromMilliseconds(500)) return;
            _addKeyGuard = DateTime.Now;

            VirtualKey key = new VirtualKey();
            key.ShortPress = new KeyBinding();
            key.Size = 152;
            key.HideWithHidden = true;

            if (_editingOverlay)
            {
                OverlayModeConfig overlay = Root.ActiveOverlayMode;
                if (overlay == null) return;
                if (overlay.Keys == null) overlay.Keys = new List<VirtualKey>();
                key.Name = "按键" + (overlay.Keys.Count + 1);
                PointF spot = FindFreeSpot(overlay.Keys);
                key.X = spot.X;
                key.Y = spot.Y;
                overlay.Keys.Add(key);
                _preview.SetViewState(0);
                _preview.SetSelectedOverlayKeySilent(key);
                _editor.SetOverlayMode(overlay, key);
                _editor.RefreshChips();
                _editor.SelectKey(key, false);
                ScheduleSave();
                BeginInvoke(new System.Windows.Forms.MethodInvoker(delegate { _editor.FocusName(); }));
                ShowToast("已新增叠加按键，可拖动预览中的按键调整位置。", ToastKind.Info);
                return;
            }

            ModeConfig mode = Root.ActiveMode;
            if (mode == null) return;
            key.Name = "按键" + (mode.Keys.Count + 1);
            PointF spot2 = FindFreeSpot(mode.Keys);
            key.X = spot2.X;
            key.Y = spot2.Y;
            mode.Keys.Add(key);
            _preview.ActiveModeIndex = Root.SelectedMode;
            _preview.SetViewState(0);
            _preview.SetSelectedKeySilent(key);
            _editor.SetMode(mode, key);
            if (_cooldownSettings != null) _cooldownSettings.SetKey(key);
            _editor.RefreshChips();
            _editor.SelectKey(key, false);
            ScheduleSave();
            BeginInvoke(new System.Windows.Forms.MethodInvoker(delegate { _editor.FocusName(); }));
            ShowToast("已新增虚拟按键，可拖动预览中的按键调整位置。", ToastKind.Info);
        }

        // 取当前编辑态（主模式 / 叠加层）的按键集合；mode 与 overlay 二选一非空。
        private List<VirtualKey> CurrentEditorKeys(out ModeConfig mode, out OverlayModeConfig overlay)
        {
            mode = null;
            overlay = null;
            if (_editingOverlay)
            {
                overlay = Root.ActiveOverlayMode;
                return overlay == null ? null : overlay.Keys;
            }
            mode = Root.ActiveMode;
            return mode == null ? null : mode.Keys;
        }

        // 新增主层按键时若仍处于「叠加层独立预览」，新增叠加层按键时若仍处于「主界面独立
        // 预览」，自动退出独立预览回到共存态，避免刚加的按键被 Solo 开关藏在手机预览之外。
        private void ExitConflictingSolo(ModeConfig mode, OverlayModeConfig overlay)
        {
            if (Root == null) return;
            if (mode != null && Root.PreviewSoloMode == 2) Root.PreviewSoloMode = 0;
            if (overlay != null && Root.PreviewSoloMode == 1) Root.PreviewSoloMode = 0;
            UpdatePreviewSoloControls();
        }

        private void SelectEditorKey(VirtualKey key, ModeConfig mode, OverlayModeConfig overlay)
        {
            if (_editingOverlay)
            {
                _preview.SetSelectedOverlayKeySilent(key);
                _editor.SetOverlayMode(overlay, key);
            }
            else
            {
                _preview.ActiveModeIndex = Root.SelectedMode;
                _preview.SetSelectedKeySilent(key);
                _editor.SetMode(mode, key);
            }
            if (_cooldownSettings != null) _cooldownSettings.SetKey(key);
            _editor.RefreshChips();
            if (key != null) _editor.SelectKey(key, false);
        }

        private void ClearEditorLayout()
        {
            ModeConfig mode;
            OverlayModeConfig overlay;
            List<VirtualKey> keys = CurrentEditorKeys(out mode, out overlay);
            if (keys == null) return;
            if (keys.Count == 0 && mode == null)
            {
                ShowToast("当前布局已经是空的。", ToastKind.Info);
                return;
            }
            bool ok = PromptDialog.Confirm(this, "清空当前布局",
                "确定移除当前模式下全部手柄按键，并隐藏左右摇杆吗？此操作不可撤销。", "清空布局");
            if (!ok) return;
            keys.Clear();
            if (mode != null)
            {
                mode.ShowJoystick = false;
                if (mode.Joystick != null) mode.Joystick.Enabled = false;
                if (mode.JoystickR != null) mode.JoystickR.Enabled = false;
                if (mode.Dpad != null) mode.Dpad.Enabled = false;
                _joystickSettings.SetConfig(mode.Joystick, mode.JoystickR, mode.Dpad);
            }
            _preview.SetSelectedKeySilent(null);
            _preview.SetSelectedOverlayKeySilent(null);
            SelectEditorKey(null, mode, overlay);
            ScheduleSave();
            ShowToast("已清空当前布局，左右摇杆已隐藏。", ToastKind.Success);
        }

        private void AddSlotToLayout(PadSlot slot)
        {
            if (slot == null) return;
            ModeConfig mode;
            OverlayModeConfig overlay;
            List<VirtualKey> keys = CurrentEditorKeys(out mode, out overlay);
            if (keys == null) return;
            for (int i = 0; i < keys.Count; i++)
            {
                VirtualKey existing = keys[i];
                if (existing != null && slot.Matches(existing.ShortPress))
                {
                    ShowToast("该手柄按键已经在布局中。", ToastKind.Info);
                    return;
                }
            }
            int layout = mode != null ? mode.PadLayout : 0;
            VirtualKey key = PadCatalog.BuildKey(slot, layout);
            ExitConflictingSolo(mode, overlay);
            keys.Add(key);
            SelectEditorKey(key, mode, overlay);
            ScheduleSave();
            ShowToast("已添加 " + slot.Symbol(layout) + " 按键。", ToastKind.Success);
        }

        private void AddPresetToLayout(int layout)
        {
            ModeConfig mode;
            OverlayModeConfig overlay;
            List<VirtualKey> keys = CurrentEditorKeys(out mode, out overlay);
            if (keys == null) return;
            bool ok = PromptDialog.Confirm(this, "整套添加",
                "将先清空当前布局，再加入所选厂商的完整手柄布局，确定继续吗？", "整套添加");
            if (!ok) return;
            keys.Clear();
            if (mode != null) mode.PadLayout = layout;
            ExitConflictingSolo(mode, overlay);
            PadCatalog.ApplyPresetJoysticks(mode);
            List<VirtualKey> preset = PadCatalog.BuildPreset(layout);
            keys.AddRange(preset);
            VirtualKey first = preset.Count > 0 ? preset[0] : null;
            SelectEditorKey(first, mode, overlay);
            if (mode != null) _joystickSettings.SetConfig(mode.Joystick, mode.JoystickR, mode.Dpad);
            ScheduleSave();
            ShowToast("已整套加入所选厂商手柄布局。", ToastKind.Success);
        }

        private PointF FindFreeSpot(List<VirtualKey> keys)
        {
            float[] xs = new float[] { 0.78f, 0.64f, 0.50f, 0.78f, 0.64f, 0.50f, 0.78f, 0.64f, 0.50f };
            float[] ys = new float[] { 0.58f, 0.72f, 0.86f, 0.72f, 0.86f, 0.58f, 0.86f, 0.58f, 0.72f };
            if (keys == null) return new PointF(0.62f, 0.55f);
            for (int i = 0; i < xs.Length; i++)
            {
                bool clear = true;
                for (int j = 0; j < keys.Count; j++)
                {
                    VirtualKey k = keys[j];
                    if (k == null) continue;
                    float dx = (k.X - xs[i]) * 1080f;
                    float dy = (k.Y - ys[i]) * 2400f;
                    if (Math.Sqrt(dx * dx + dy * dy) < 190f)
                    {
                        clear = false;
                        break;
                    }
                }
                if (clear) return new PointF(xs[i], ys[i]);
            }
            return new PointF(0.62f, 0.55f);
        }

        private void DeleteKey(VirtualKey key)
        {
            if (key == null) return;
            if (_editingOverlay)
            {
                OverlayModeConfig overlay = Root.ActiveOverlayMode;
                if (overlay == null || overlay.Keys == null) return;
                bool ok = PromptDialog.Confirm(this, "删除叠加按键",
                    "确定删除「" + key.Name + "」吗？位置、绑定和图标引用都会被移除。", "删除按键");
                if (!ok) return;
                int index = overlay.Keys.IndexOf(key);
                overlay.Keys.Remove(key);
                VirtualKey next = null;
                if (overlay.Keys.Count > 0)
                {
                    int nextIndex = Math.Min(index, overlay.Keys.Count - 1);
                    next = overlay.Keys[nextIndex];
                }
                _preview.SetSelectedOverlayKeySilent(next);
                _editor.SetOverlayMode(overlay, next);
                ScheduleSave();
                ShowToast("叠加按键已删除。", ToastKind.Info);
                return;
            }

            ModeConfig mode = Root.ActiveMode;
            if (mode == null) return;
            bool ok2 = PromptDialog.Confirm(this, "删除虚拟按键",
                "确定删除「" + key.Name + "」吗？这个按键的位置、绑定和图标引用都会被移除。", "删除按键");
            if (!ok2) return;
            int index2 = mode.Keys.IndexOf(key);
            mode.Keys.Remove(key);
            VirtualKey next2 = null;
            if (mode.Keys.Count > 0)
            {
                int nextIndex = Math.Min(index2, mode.Keys.Count - 1);
                next2 = mode.Keys[nextIndex];
            }
            _preview.SetSelectedKeySilent(next2);
            _editor.SetMode(mode, next2);
            if (_cooldownSettings != null) _cooldownSettings.SetKey(next2);
            ScheduleSave();
            ShowToast("按键已删除。", ToastKind.Info);
        }

        // 手机预览或右侧条目选中左摇杆(0)/右摇杆(1)/方向键(2)：展开左侧摇杆卡片并定位到对应分组。
        private void SelectStick(int kind)
        {
            if (_joystickSettings == null) return;
            // 虚拟按键页：仅左摇杆参与；参数在左侧摇杆设置卡片，不进入右侧手柄摇杆条目流程。
            if (_padSide == 0)
            {
                if (kind != 0) return;
                _deckDock.ExpandCard(_joystickSettings);
                _controlDeck.ScrollControlIntoView(_joystickSettings);
                _joystickSettings.FocusGroup(0);
                return;
            }
            if (kind == 2)
            {
                // 方向键整体参数在右侧「外观与行为」卡片调节，不展开左侧摇杆面板。
                _editor.SelectStick(kind);
                ShowToast("方向键参数已在右侧「外观与行为」卡片中调节。", ToastKind.Info);
                return;
            }
            _deckDock.ExpandCard(_joystickSettings);
            _controlDeck.ScrollControlIntoView(_joystickSettings);
            _joystickSettings.FocusGroup(kind);
            _editor.SelectStick(kind);
            string who = kind == 0 ? "左摇杆" : "右摇杆";
            ShowToast(who + "参数已在左侧「手柄摇杆」卡片中选中。", ToastKind.Info);
        }

        private void PreviewOnKeySelected(VirtualKey key)
        {
            if (key == null) return;
            _editor.ClearStickSelection();
            _editor.SelectKey(key, false);
        }

        private void BrowseProgram()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择要启动的游戏程序";
                dlg.Filter = "程序 (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|所有文件 (*.*)|*.*";
                dlg.CheckFileExists = true;
                string current = (_launchCard.ProgramPath ?? "").Trim().Trim('"');
                try
                {
                    if (current.Length > 0)
                    {
                        string dir = Path.GetDirectoryName(current);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
                    }
                }
                catch
                {
                }
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _launchCard.ProgramPath = dlg.FileName;
                    _config.GamePath = dlg.FileName;
                    ScheduleSave();
                    ShowToast("已选择启动程序。", ToastKind.Success);
                }
            }
        }

        private void PasteProgramPath()
        {
            try
            {
                if (!Clipboard.ContainsText())
                {
                    ShowToast("剪贴板中没有可粘贴的路径。", ToastKind.Error);
                    return;
                }
                string path = Clipboard.GetText().Trim().Trim('"');
                if (path.Length == 0)
                {
                    ShowToast("剪贴板内容为空。", ToastKind.Error);
                    return;
                }
                _launchCard.ProgramPath = path;
                _config.GamePath = path;
                ScheduleSave();
                ShowToast("路径已粘贴。", ToastKind.Success);
            }
            catch (Exception ex)
            {
                ShowToast("粘贴失败：" + ex.Message, ToastKind.Error);
            }
        }

        private void PathBoxOnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            else e.Effect = DragDropEffects.None;
        }

        private void PathBoxOnDragDrop(object sender, DragEventArgs e)
        {
            try
            {
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    _launchCard.ProgramPath = files[0];
                    _config.GamePath = files[0];
                    ScheduleSave();
                }
            }
            catch
            {
            }
        }

        private void LaunchGame()
        {
            string raw = (_launchCard.ProgramPath ?? "").Trim().Trim('"');
            if (raw.Length == 0)
            {
                ShowToast("请先选择或粘贴要启动的游戏程序。", ToastKind.Error);
                BrowseProgram();
                return;
            }
            string path = Environment.ExpandEnvironmentVariables(raw);
            if (!File.Exists(path))
            {
                ShowToast("找不到启动程序，请检查路径是否正确。", ToastKind.Error);
                return;
            }
            if (_launchingGame) return;
            // 上一次会话结束后 _runtime 处于停止状态，先恢复监控再做 GameRunning 检查
            if (_runtime != null) _runtime.RestartMonitoring();
            ConfigStore.Log("启动按钮: GameRunning=" + (_runtime != null && _runtime.GameRunning)
                + " processAlive=" + (_runtime != null && _runtime.GameProcessAlive)
                + " stopped=" + (_runtime != null && _runtime.Stopped)
                + " gameEnded=" + (_runtime != null && _runtime.GameEndedFlag)
                + " gamePid=" + (_runtime != null ? _runtime.GamePid : -1));
            // 只有游戏进程真实存在才拦截；串流软件挂在后台造成的"虚拟存活"不算，
            // 否则 GameViewer/sunshine 常驻会导致永远无法启动游戏。
            if (_runtime != null && _runtime.GameProcessAlive)
            {
                // 游戏可能已被 GameViewer / UU远程 / 加速器自动拉起。
                // 此时不再拒绝，而是直接接管现有进程，恢复正常使用。
                int pid = _runtime.GamePid;
                if (pid <= 0)
                {
                    try
                    {
                        string stem = Path.GetFileNameWithoutExtension(path);
                        if (!string.IsNullOrEmpty(stem))
                        {
                            Process[] ps = Process.GetProcessesByName(stem);
                            if (ps != null && ps.Length > 0) pid = ps[0].Id;
                            if (ps != null) { for (int i = 0; i < ps.Length; i++) { try { ps[i].Dispose(); } catch { } } }
                        }
                    }
                    catch { }
                }
                if (pid > 0)
                {
                    _runtime.NotifyGameLaunched(pid);
                    _config.GamePath = _launchCard.ProgramPath;
                    _config.GameArguments = _launchCard.Arguments;
                    try { _config.GameProcessName = Path.GetFileNameWithoutExtension(path); } catch { }
                    if (Root.Modes != null && Root.Modes.Count > 0)
                    {
                        Root.SelectedMode = 0;
                        for (int i = 0; i < _modeTabs.Count; i++) _modeTabs[i].Selected = i == 0;
                        _preview.ActiveModeIndex = 0;
                        _preview.SetViewState(0);
                        ModeConfig firstMode = Root.Modes[0];
                        VirtualKey firstKey = firstMode.Keys != null && firstMode.Keys.Count > 0 ? firstMode.Keys[0] : null;
                        _preview.SetSelectedKeySilent(firstKey);
                        _editor.SetMode(firstMode, firstKey);
                        if (_cooldownSettings != null) _cooldownSettings.SetKey(firstKey);
                        BindAutoPanels();
                    }
                    _gameEndedHandled = false;
                    ScheduleSave();
                    ShowToast("检测到游戏已在运行（PID " + pid + "），已接管。", ToastKind.Success);
                    ConfigStore.Log("游戏已在运行，接管现有进程 PID=" + pid);
                    if (_config.HideOnGameLaunch) MinimizeToTaskbar();
                    return;
                }
                ShowToast("游戏已经在运行，不会重复启动。", ToastKind.Info);
                return;
            }
            if (Environment.TickCount - _lastLaunchTick < 2500)
            {
                ShowToast("启动请求已发出，请稍候。", ToastKind.Info);
                return;
            }
            _launchingGame = true;
            _lastLaunchTick = Environment.TickCount;
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = path;
                info.Arguments = _launchCard.Arguments ?? "";
                info.UseShellExecute = true;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) info.WorkingDirectory = dir;
                int pid = 0;
                using (Process proc = Process.Start(info))
                {
                    if (proc != null) pid = proc.Id;
                }
                _config.GamePath = _launchCard.ProgramPath;
                _config.GameArguments = _launchCard.Arguments;
                try { _config.GameProcessName = Path.GetFileNameWithoutExtension(path); }
                catch { }
                if (_runtime != null) _runtime.NotifyGameLaunched(pid);
                // 每次启动游戏强制回到第一个模式（主界面），避免上次会话残留的模式二/三
                if (Root.Modes != null && Root.Modes.Count > 0)
                {
                    Root.SelectedMode = 0;
                    for (int i = 0; i < _modeTabs.Count; i++) _modeTabs[i].Selected = i == 0;
                    _preview.ActiveModeIndex = 0;
                    _preview.SetViewState(0);
                    ModeConfig firstMode = Root.Modes[0];
                    VirtualKey firstKey = firstMode.Keys != null && firstMode.Keys.Count > 0 ? firstMode.Keys[0] : null;
                    _preview.SetSelectedKeySilent(firstKey);
                    _editor.SetMode(firstMode, firstKey);
                    if (_cooldownSettings != null) _cooldownSettings.SetKey(firstKey);
                    BindAutoPanels();
                }
                _gameEndedHandled = false;
                ScheduleSave();
                ShowToast("游戏已启动：" + Path.GetFileName(path), ToastKind.Success);
                if (_config.HideOnGameLaunch) MinimizeToTaskbar();
            }
            catch (Exception ex)
            {
                ConfigStore.Log("启动游戏失败: " + ex.Message);
                ShowToast("启动失败：" + ex.Message, ToastKind.Error);
            }
            finally
            {
                _launchingGame = false;
            }
        }

        private void ShowToast(string message, ToastKind kind)
        {
            if (_toast == null) return;
            _toast.ShowToast(message, kind);
            LayoutToast();
            _toast.Visible = true;
            _toast.BringToFront();
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_COPYDATA)
            {
                try
                {
                    COPYDATASTRUCT data = (COPYDATASTRUCT)Marshal.PtrToStructure(m.LParam, typeof(COPYDATASTRUCT));
                    string payload = Marshal.PtrToStringUni(data.lpData, data.cbData / 2);
                    int split = payload.IndexOf('|');
                    if (split > 0)
                    {
                        string path = payload.Substring(0, split);
                        bool launch = payload.Substring(split + 1) == "1";
                        BeginInvoke(new Action(delegate { OpenProfileShortcut(path, launch); }));
                    }
                }
                catch (Exception ex)
                {
                    ConfigStore.Log("处理快捷方式启动请求失败: " + ex.Message);
                }
                m.Result = (IntPtr)1;
                return;
            }
            if (m.Msg == 0x0011) // WM_QUERYENDSESSION
            {
                _shutdownRequested = true;
                base.WndProc(ref m);
                return;
            }
            if (m.Msg == 0x0112 && (m.WParam.ToInt32() & 0xFFF0) == 0xF060 &&
                !_allowExit && !_shutdownRequested) // SC_CLOSE from the native X button
            {
                try { SaveNow(true); } catch { }
                try { BeginInvoke(new Action(delegate { HideToTray(false); })); }
                catch { HideToTray(false); }
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == 0x0010 && !_allowExit && !_shutdownRequested) // WM_CLOSE
            {
                try { SaveNow(true); } catch { }
                try
                {
                    BeginInvoke(new Action(delegate { HideToTray(false); }));
                }
                catch
                {
                    HideToTray(false);
                }
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == 0x0312 && (int)m.WParam == FeatureHotkeyId)
            {
                BeginFeatureCapture(_autoModeSettings == null ? 0 : _autoModeSettings.ActiveSlot);
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == 0x0312 && (int)m.WParam == StoryHotkeyId)
            {
                ToggleStorySkipNow();
                m.Result = IntPtr.Zero;
                return;
            }
            if (Program.ShowSettingsMessage != 0 && (uint)m.Msg == Program.ShowSettingsMessage)
            {
                BeginInvoke(new System.Windows.Forms.MethodInvoker(ShowSettings));
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Theme.Bg))
            {
                e.Graphics.FillRectangle(b, ClientRectangle);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDarkTitleBar();
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                int dark = 1;
                NativeMethods.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
                NativeMethods.DwmSetWindowAttribute(Handle, 19, ref dark, 4);
                int caption = ColorRef(Theme.Bg);
                int text = ColorRef(Color.FromArgb(238, 244, 255));
                int border = ColorRef(Theme.StrokeSoft);
                NativeMethods.DwmSetWindowAttribute(Handle, 35, ref caption, 4);
                NativeMethods.DwmSetWindowAttribute(Handle, 36, ref text, 4);
                NativeMethods.DwmSetWindowAttribute(Handle, 34, ref border, 4);
                int corner = 2;
                NativeMethods.DwmSetWindowAttribute(Handle, 33, ref corner, 4);
            }
            catch
            {
            }
        }

        private static int ColorRef(Color color)
        {
            return color.R | (color.G << 8) | (color.B << 16);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_toast != null) LayoutToast();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_saveTimer != null) _saveTimer.Dispose();
                if (_toastTimer != null) _toastTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public class PreviewCard : CardPanel
    {
        public PreviewCard()
        {
            Radius = 16;
            BackColor = Theme.Surface;
            BorderColor = Theme.StrokeSoft;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Theme.SetupGraphics(e.Graphics);
            float s = Theme.Scale;
            int x = (int)(20 * s);
            Theme.FillRounded(e.Graphics, new RectangleF(x, 15 * s, 3 * s, 28 * s), 1.5f * s, Theme.Mint);
            Theme.DrawText(e.Graphics, "界面预览", Theme.Semibold(12f), Theme.Text,
                new RectangleF(x + 12 * s, 8 * s, 220 * s, 28 * s), StringAlignment.Near, StringAlignment.Center, true);
            Theme.DrawText(e.Graphics, "在手机框内拖动虚拟按键，位置会自动保存", Theme.UiFont(8.1f, false), Theme.Muted,
                new RectangleF(x + 12 * s, 31 * s, Width - x - 30 * s, 18 * s), StringAlignment.Near, StringAlignment.Center, true);
        }
    }

    internal class WheelRouter : IMessageFilter
    {
        private readonly MainForm _form;

        public WheelRouter(MainForm form)
        {
            _form = form;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x020A || _form == null || _form.IsDisposed) return false;
            Form active = Form.ActiveForm;
            if (active != null && active != _form) return false;
            SmoothScrollPane pane = _form.EditorSectionPane;
            int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
            Point screen = Cursor.Position;
            if (pane != null && pane.Visible && pane.IsHandleCreated)
            {
                Rectangle rect = pane.RectangleToScreen(pane.ClientRectangle);
                if (rect.Contains(screen))
                {
                    pane.ScrollBy(delta > 0 ? -72 : 72);
                    return true;
                }
            }
            SmoothScrollPane deck = _form.ControlDeck;
            if (deck != null && deck.Visible && deck.IsHandleCreated)
            {
                Rectangle rect = deck.RectangleToScreen(deck.ClientRectangle);
                if (rect.Contains(screen))
                {
                    deck.ScrollBy(delta > 0 ? -Theme.S(56) : Theme.S(56));
                    return true;
                }
            }
            return false;
        }
    }
}
