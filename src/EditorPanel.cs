// ============================================================================
// EditorPanel.cs —— 右侧配置面板（手机预览旁的卡片式编辑区）
// ----------------------------------------------------------------------------
// 本类是设置窗口右侧配置区的核心面板，负责把当前选中模式 / 叠加模式 / 虚拟按键
// 绑定到一系列设置卡片：按键名称、短按 / 长按 / 双击绑定、外观与行为（尺寸、不透明度、
// 图标、跟随隐藏、联动保持）、摇杆与方向键参数、视角操控、手柄布局选择等。
//
// 两条独立上下文根互不串味：
//   · 主模式（_mode）与叠加模式（_overlayMode）二选一，由 SetMode / SetOverlayMode 切换；
//   · 虚拟按键页与虚拟手柄页由 SetPadSide 切换，各自持有独立配置。
// 面板只读写模型对象（Model 层），不直接派发输入；真机行为由 Overlay.cs 完成。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace MobileUiConfig
{
    /// <summary>右侧卡片式编辑器：当前模式 / 选中按键与各类设置卡片之间的双向绑定与刷新。</summary>
    public class EditorPanel : CardPanel
    {
        private ModeConfig _mode;
        private OverlayModeConfig _overlayMode;
        private VirtualKey _selected;
        private bool _binding;

        // 当前编辑态按键列表：主模式与叠加模式二选一，互不串味。
        private List<VirtualKey> CurrentKeys
        {
            get
            {
                if (_mode != null) return _mode.Keys;
                if (_overlayMode != null) return _overlayMode.Keys;
                return null;
            }
        }

        public bool EditingOverlay
        {
            get { return _overlayMode != null; }
        }

        public OverlayModeConfig CurrentOverlay
        {
            get { return _overlayMode; }
        }

        private readonly ModernButton _addButton;
        private ModernButton _expandAllRightButton;
        private AppConfig _config;
        private readonly Panel _chipFlow;
        private readonly SmoothScrollPane _sections;
        private readonly Panel _sectionContent;
        private readonly EmptyStateView _emptyView;
        // 叠加态无按键时排在「叠加模式」卡片下方的空状态提示（区别于主态全屏空态）。
        private EmptyStateView _emptyOverlayView;

        private readonly List<KeyChip> _chips = new List<KeyChip>();
        private readonly List<StickChip> _stickChips = new List<StickChip>();
        private int _selectedStickKind = -1;
        private int _dividerY = 170;

        private SectionPanel _basicSection;
        private RoundTextBox _nameInput;
        private ModernSelect _shortSelect;
        private Label _shortHelp;
        // 按键页（_padSide=false）使用键盘/鼠标捕获框；手柄页（true）使用手柄下拉与弹窗选择。两套控件并存，按页显隐。
        private bool _padSide;
        private KeyCaptureBox _shortCapture;
        private KeyCaptureBox _longCapture;

        // 右侧卡片折叠 + 长按拖拽重排协调器（从「长按触发」开始纳入）。
        private CardDock _dock;

        private SectionPanel _longSection;
        private ToggleSwitch _longToggle;
        private ModernButton _longPickButton;
        private StepperBox _longDuration;
        private Label _longDurationHint;
        private Label _longHelp;

        // 双击触发：与长按触发平行的独立绑定槽位；按键页走键盘捕获，手柄页走手柄弹窗。
        private SectionPanel _doubleSection;
        private ToggleSwitch _doubleToggle;
        private ModernButton _doublePickButton;
        private KeyCaptureBox _doubleCapture;
        private Label _doubleHelp;

        private SectionPanel _appearanceSection;
        private StepperBox _sizeStep;
        private ModernSlider _sizeSlider;
        private StepperBox _opacityStep;
        private ModernSlider _opacitySlider;
        private ImagePreviewBox _imagePreview;
        private ModernButton _chooseImage;
        private ModernButton _clearImage;
        private Label _imageHint;
        private ToggleSwitch _hideToggle;
        private Label _hideLabel;

        private SectionPanel _followSection;
        private ToggleSwitch _followToggle;
        private ModernSelect _followSelect;
        private Label _followLabel;
        private Label _followHelp;

        // 多模态触控反馈卡片：按键级视觉 / 音效两套配置，同一张卡片内纵向分区。
        private SectionPanel _feedbackSection;
        // 视觉反馈
        private ToggleSwitch _visualToggle;
        private ModernSelect _visualSelect;
        private Panel _visualColorSwatch;
        private Label _visualColorNameLabel;
        private StepperBox _visualIntensityStep;
        private StepperBox _visualDurationStep;
        private StepperBox _visualScaleStep;
        // 音效反馈
        private ToggleSwitch _soundToggle;
        private ModernSelect _soundSelect;
        private StepperBox _soundVolumeStep;
        private ModernButton _chooseSound;
        private ModernButton _testSound;
        private Label _soundFileLabel;

        // 震动反馈（按键级，未开启时回落全局震动配置）。
        private ToggleSwitch _rumbleToggle;
        private ModernSelect _rumbleSelect;
        private StepperBox _rumbleLeftStep;
        private StepperBox _rumbleRightStep;
        private StepperBox _rumbleDurationStep;
        private RoundTextBox _rumblePatternBox;
        private ModernButton _rumbleTestButton;

        private Control _cooldownPanel;
        // 标题栏「删除按键」按钮：置于「新增按键」右侧，删除当前选中虚拟键。
        private ModernButton _deleteButton;

        // 叠加层呼出：挂在主模式单个虚拟按键上，点击唤出指定叠加模式按键层。
        private SectionPanel _overlaySection;
        private ToggleSwitch _overlayToggle;
        private ModernSelect _overlaySelect;
        private StepperBox _overlayHideStep;
        private Label _overlayHideHint;
        private Label _overlayHelp;
        private List<OverlayModeConfig> _overlayModes;

        // 叠加模式级卡片：仅在叠加模式编辑态显示，控制启停与预览单独显示。
        private SectionPanel _overlayModeSection;
        private ToggleSwitch _overlayEnabledToggle;
        private ToggleSwitch _soloToggle;
        private Label _soloHelp;

        public event Action<VirtualKey> SelectionChanged;
        public event Action KeyModified;
        public event Action ImageChanged;
        public event Action AddRequested;
        public event Action<VirtualKey> DeleteRequested;
        public event Action<string, ToastKind> Notify;
        // 布局选择弹窗回调：清空当前布局、单键添加、整套添加指定厂商预设。
        public event Action ClearLayoutRequested;
        public event Action<PadSlot> AddSlotRequested;
        public event Action<int> AddPresetRequested;
        // 右侧摇杆/方向键条目点击：0=左摇杆，1=右摇杆，2=方向键；联动左侧摇杆设置面板。
        public event Action<int> StickSelected;
        // 条目右上角小按钮：切换对应摇杆/方向键的启用（隐藏/恢复）状态。
        public event Action<int> StickToggle;

        public EditorPanel(AppConfig config)
        {
            _config = config;
            Radius = 16;
            BackColor = Theme.Surface;
            BorderColor = Theme.StrokeSoft;

            _addButton = new ModernButton();
            _addButton.Text = "新增按键";
            _addButton.Icon = "＋";
            _addButton.Style = ModernButtonStyle.Soft;
            _addButton.Font = Theme.UiFont(8.8f, false);
            _addButton.Click += delegate
            {
                // 按键页：直接新增一个普通虚拟按键；手柄页：弹出手柄布局选择窗。
                if (!_padSide)
                {
                    Action h = AddRequested;
                    if (h != null) h();
                    return;
                }
                int layout = _mode != null ? _mode.PadLayout : 0;
                PadLayoutPickerDialog.Show(this, layout,
                    delegate(PadSlot slot)
                    {
                        Action<PadSlot> h = AddSlotRequested;
                        if (h != null) h(slot);
                    },
                    delegate
                    {
                        Action h = ClearLayoutRequested;
                        if (h != null) h();
                    },
                    delegate(int layoutIdx)
                    {
                        Action<int> h = AddPresetRequested;
                        if (h != null) h(layoutIdx);
                    });
            };
            Controls.Add(_addButton);

            _deleteButton = new ModernButton();
            _deleteButton.Text = "删除按键";
            _deleteButton.Style = ModernButtonStyle.Danger;
            _deleteButton.Font = Theme.UiFont(8.8f, false);
            _deleteButton.Enabled = false;
            _deleteButton.Click += delegate
            {
                if (_selected == null) return;
                Action<VirtualKey> h = DeleteRequested;
                if (h != null) h(_selected);
            };
            Controls.Add(_deleteButton);

            // 一键展开右侧全部卡片。
            _expandAllRightButton = new ModernButton();
            _expandAllRightButton.Text = "全部展开";
            _expandAllRightButton.Style = ModernButtonStyle.Soft;
            _expandAllRightButton.Font = Theme.UiFont(8.8f, false);
            _expandAllRightButton.Click += delegate
            {
                if (_dock == null) return;
                if (_dock.AnyCollapsed) _dock.ExpandAll();
                else _dock.CollapseAll();
                RefreshRightExpandButton();
            };
            Controls.Add(_expandAllRightButton);

            _chipFlow = new Panel();
            _chipFlow.AutoScroll = false;
            _chipFlow.BackColor = Theme.Surface;
            _chipFlow.Padding = new Padding(0);
            _chipFlow.Margin = new Padding(0);
            _chipFlow.HandleCreated += delegate
            {
                try { NativeMethods.SetWindowTheme(_chipFlow.Handle, "DarkMode_Explorer", null); }
                catch { }
            };
            Controls.Add(_chipFlow);

            _sections = new SmoothScrollPane();
            _sections.BackColor = Theme.Surface;
            Controls.Add(_sections);

            _sectionContent = new Panel();
            _sectionContent.BackColor = Theme.Surface;
            _sections.SetContent(_sectionContent);

            _dock = new CardDock(_sectionContent);
            _dock.Bind(LayoutSections, SyncDockOrder, OnRightCardStateChanged);

            _emptyView = new EmptyStateView();
            Controls.Add(_emptyView);

            _emptyOverlayView = new EmptyStateView();
            _emptyOverlayView.SetTexts("本叠加层还没有手柄按键", "点击右上角「新增手柄」创建叠加层控件");
            _emptyOverlayView.Height = Theme.S(260);

            BuildBasicSection();
            BuildLongSection();
            BuildDoubleSection();
            BuildAppearanceSection();
            BuildFeedbackSection();
            BuildFollowSection();
            BuildOverlaySection();
            BuildOverlayModeSection();
            // 从「长按触发」起纳入折叠 + 长按拖拽；基础信息卡片不纳入。
            // 折叠态从配置恢复，首次进入默认全部折叠；centerY 为单行标题栏中心线基准。
            _dock.Register(_longSection, 0, 18, Theme.S(206), true, true, _config.RightCardCollapsed[0], null);
            _dock.Register(_doubleSection, 1, 18, Theme.S(150), true, true, _config.RightCardCollapsed[1], null);
            _dock.Register(_appearanceSection, 2, 18, Theme.S(350), true, true, _config.RightCardCollapsed[2], null);
            _dock.Register(_feedbackSection, 3, 18, Theme.S(580), true, true, _config.RightCardCollapsed[3], null);
            _dock.Register(_followSection, 4, 18, Theme.S(188), true, true, _config.RightCardCollapsed[4], null);
            _dock.Register(_overlaySection, 5, 18, Theme.S(232), true, true, _config.RightCardCollapsed[5], null);
            // 叠加态空态提示排在卡片序列末尾（Z-order），由布局函数参与堆叠。
            _sectionContent.Controls.Add(_emptyOverlayView);
            RefreshRightExpandButton();
        }

        /// <summary>把编辑器切换到主模式上下文：清空叠加模式引用，重建按键标签并绑定选中按键的全部卡片。</summary>
        public void SetMode(ModeConfig mode, VirtualKey selected)
        {
            _mode = mode;
            _selectedStickKind = -1;
            _overlayMode = null;
            _selected = selected;
            CooldownSettingsPanel cooldown = _cooldownPanel as CooldownSettingsPanel;
            if (cooldown != null) cooldown.SetKey(selected);
            RebuildChips();
            BindSelected();
            BindOverlayModeSection();
            UpdateVisibility();
            if (_sections != null) _sections.ScrollToTop();
            Invalidate();
        }

        // 绑定叠加模式按键列表：叠加层按键保留除「叠加层呼出」外全部功能卡片。
        public void SetOverlayMode(OverlayModeConfig overlay, VirtualKey selected)
        {
            _overlayMode = overlay;
            _mode = null;
            _selectedStickKind = -1;
            _selected = selected;
            CooldownSettingsPanel cooldown = _cooldownPanel as CooldownSettingsPanel;
            if (cooldown != null) cooldown.SetKey(selected);
            RebuildChips();
            BindSelected();
            BindOverlayModeSection();
            UpdateVisibility();
            if (_sections != null) _sections.ScrollToTop();
            Invalidate();
        }

        /// <summary>选中某个虚拟按键：刷新右侧外观 / 行为卡片，notify=true 时对外抛出 SelectionChanged 事件通知预览层同步高亮。</summary>
        public void SelectKey(VirtualKey key, bool notify)
        {
            if (ReferenceEquals(_selected, key))
            {
                SelectChip(key);
                return;
            }
            _selected = key;
            BindSelected();
            SelectChip(key);
            UpdateVisibility();
            Invalidate();
            if (notify)
            {
                Action<VirtualKey> h = SelectionChanged;
                if (h != null) h(key);
            }
        }

        public VirtualKey SelectedKey
        {
            get { return _selected; }
        }

        public void RefreshChips()
        {
            RebuildChips();
        }

        // 构造期句柄尚未创建时完成首屏绑定，父链当时不可见会导致堆叠循环跳过高度累加；
        // 窗口显示、父链可见后由主窗体调用本方法，沿用当前模式/叠加层/选中按键重绑并重排，不改变选中态。
        public void RebuildAfterShow()
        {
            RebuildChips();
            BindSelected();
            BindOverlayModeSection();
            UpdateVisibility();
            if (_sections != null) _sections.ScrollToTop();
            Invalidate();
        }

        public void RefreshSelectedAppearance()
        {
            if (_selected == null) return;
            _imagePreview.SetImage(LoadSelectedImage());
        }

        public void FocusName()
        {
            if (_nameInput == null || !_nameInput.Visible) return;
            _nameInput.InnerTextBox.Focus();
            _nameInput.InnerTextBox.SelectAll();
        }

        private void BuildBasicSection()
        {
            _basicSection = NewSection("基础信息", "设置显示名称和短按绑定按键", 216);
            _nameInput = new RoundTextBox();
            _nameInput.Placeholder = "例如：普攻、跳跃、技能";
            _nameInput.FillColor = Theme.SurfaceAlt;
            _nameInput.TextChanged += NameInputOnTextChanged;
            _nameInput.InnerTextBox.LostFocus += delegate
            {
                if (_selected != null && _nameInput.Text.Trim().Length == 0)
                {
                    _nameInput.Text = "按键";
                }
            };
            _basicSection.Controls.Add(_nameInput);

            Label nameLabel = NewFieldLabel("按键名称");
            nameLabel.Name = "nameLabel";
            _basicSection.Controls.Add(nameLabel);

            Label shortLabel = NewFieldLabel("绑定按键 · 短按");
            shortLabel.Name = "shortLabel";
            _basicSection.Controls.Add(shortLabel);

            // 手柄页：手柄按键下拉选择。
            _shortSelect = new ModernSelect();
            _shortSelect.SelectionChanged += ShortCaptureOnBindingChanged;
            _shortSelect.Visible = false;
            _basicSection.Controls.Add(_shortSelect);

            // 按键页：键盘 / 鼠标捕获框（左侧捕获 + 右侧打开虚拟键盘）。
            _shortCapture = new KeyCaptureBox();
            _shortCapture.BindingChanged += ShortCaptureOnKeyBindingChanged;
            _basicSection.Controls.Add(_shortCapture);

            _shortHelp = NewHelpLabel("点击输入框后按下任意键绑定；右键可切换鼠标键。");
            _basicSection.Controls.Add(_shortHelp);

            _basicSection.Resize += delegate { LayoutBasicSection(); };
            _sectionContent.Controls.Add(_basicSection);
        }

        private void BuildLongSection()
        {
            _longSection = NewSection("长按触发", "可选：按住时触发独立按键", 206);
            _longToggle = new ToggleSwitch();
            _longToggle.CheckedChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.LongPressEnabled = _longToggle.Checked;
                UpdateLongEnabled();
                RaiseModified();
            };
            _longSection.Controls.Add(_longToggle);

            Label longLabel = NewFieldLabel("长按按键");
            longLabel.Name = "longLabel";
            _longSection.Controls.Add(longLabel);

            // 手柄页：弹出手柄按键选择窗。
            _longPickButton = new ModernButton();
            _longPickButton.Style = ModernButtonStyle.Secondary;
            _longPickButton.Radius = 9;
            _longPickButton.Click += LongPickButtonOnClick;
            _longPickButton.Visible = false;
            _longSection.Controls.Add(_longPickButton);

            // 按键页：键盘 / 鼠标捕获框。
            _longCapture = new KeyCaptureBox();
            _longCapture.BindingChanged += LongCaptureOnKeyBindingChanged;
            _longSection.Controls.Add(_longCapture);

            Label durLabel = NewFieldLabel("触发时长");
            durLabel.Name = "durLabel";
            _longSection.Controls.Add(durLabel);

            _longDuration = new StepperBox();
            _longDuration.Minimum = 200;
            _longDuration.Maximum = 3000;
            _longDuration.Step = 50;
            _longDuration.ValueChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.LongPressMs = _longDuration.Value;
                RaiseModified();
            };
            _longSection.Controls.Add(_longDuration);

            _longDurationHint = NewHelpLabel("毫秒");
            _longDurationHint.ForeColor = Color.White;
            _longDurationHint.Font = Theme.UiFont(8.0f, false);
            _longDurationHint.TextAlign = ContentAlignment.MiddleCenter;
            _longSection.Controls.Add(_longDurationHint);

            _longHelp = NewHelpLabel("开启后：短按触发上方按键，按住达到时长触发长按按键。");
            _longSection.Controls.Add(_longHelp);

            _longSection.Resize += delegate { LayoutLongSection(); };
            _sectionContent.Controls.Add(_longSection);
        }

        private void BuildDoubleSection()
        {
            _doubleSection = NewSection("双击触发", "可选：300ms 内连续两次按下触发独立按键", 150);
            _doubleToggle = new ToggleSwitch();
            _doubleToggle.CheckedChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.DoublePressEnabled = _doubleToggle.Checked;
                UpdateDoubleEnabled();
                RaiseModified();
            };
            _doubleSection.Controls.Add(_doubleToggle);

            Label doubleLabel = NewFieldLabel("双击按键");
            doubleLabel.Name = "doubleLabel";
            _doubleSection.Controls.Add(doubleLabel);

            // 手柄页：弹出手柄按键选择窗。
            _doublePickButton = new ModernButton();
            _doublePickButton.Style = ModernButtonStyle.Secondary;
            _doublePickButton.Radius = 9;
            _doublePickButton.Click += DoublePickButtonOnClick;
            _doublePickButton.Visible = false;
            _doubleSection.Controls.Add(_doublePickButton);

            // 按键页：键盘 / 鼠标捕获框。
            _doubleCapture = new KeyCaptureBox();
            _doubleCapture.BindingChanged += DoubleCaptureOnKeyBindingChanged;
            _doubleSection.Controls.Add(_doubleCapture);

            _doubleHelp = NewHelpLabel("开启后：300ms 内连续两次按下触发双击按键，单击与长按互不干扰。");
            _doubleSection.Controls.Add(_doubleHelp);

            _doubleSection.Resize += delegate { LayoutDoubleSection(); };
            _sectionContent.Controls.Add(_doubleSection);
        }

        private void BuildAppearanceSection()
        {
            _appearanceSection = NewSection("外观与行为", "尺寸、不透明度、透明 PNG 图标和隐藏行为", 350);

            Label sizeLabel = NewFieldLabel("按键尺寸");
            sizeLabel.Name = "sizeLabel";
            _appearanceSection.Controls.Add(sizeLabel);

            _sizeStep = new StepperBox();
            _sizeStep.Minimum = 40;
            _sizeStep.Maximum = 360;
            _sizeStep.Step = 1;
            _sizeStep.ValueChanged += delegate
            {
                if (_binding) return;
                DpadConfig d = DpadTarget();
                if (d != null)
                {
                    d.Size = ClampDpadSize(_sizeStep.Value);
                    _sizeSlider.SetValueSilent(d.Size);
                    RaiseModified();
                    return;
                }
                if (_selected == null) return;
                _selected.Size = _sizeStep.Value;
                _sizeSlider.SetValueSilent(_selected.Size);
                RaiseModified();
            };
            _appearanceSection.Controls.Add(_sizeStep);

            Label pxLabel = NewHelpLabel("px");
            pxLabel.Name = "pxLabel";
            pxLabel.ForeColor = Color.White;
            pxLabel.TextAlign = ContentAlignment.MiddleCenter;
            _appearanceSection.Controls.Add(pxLabel);

            _sizeSlider = new ModernSlider();
            _sizeSlider.Minimum = 40;
            _sizeSlider.Maximum = 360;
            _sizeSlider.ValueChanged += delegate
            {
                if (_binding) return;
                DpadConfig d = DpadTarget();
                if (d != null)
                {
                    d.Size = ClampDpadSize(_sizeSlider.Value);
                    _sizeStep.SetValueSilent(d.Size);
                    RaiseModified();
                    return;
                }
                if (_selected == null) return;
                _selected.Size = _sizeSlider.Value;
                _sizeStep.SetValueSilent(_selected.Size);
                RaiseModified();
            };
            _appearanceSection.Controls.Add(_sizeSlider);

            Label opacityLabel = NewFieldLabel("按键不透明度");
            opacityLabel.Name = "opacityLabel";
            _appearanceSection.Controls.Add(opacityLabel);

            _opacityStep = new StepperBox();
            _opacityStep.Minimum = 10;
            _opacityStep.Maximum = 100;
            _opacityStep.Step = 1;
            _opacityStep.ValueChanged += delegate
            {
                if (_binding) return;
                DpadConfig d = DpadTarget();
                if (d != null)
                {
                    d.Opacity = ClampDpadOpacity(_opacityStep.Value);
                    _opacitySlider.SetValueSilent(d.Opacity);
                    RaiseModified();
                    return;
                }
                if (_selected == null) return;
                _selected.Opacity = _opacityStep.Value;
                _opacitySlider.SetValueSilent(_selected.Opacity);
                RaiseModified();
            };
            _appearanceSection.Controls.Add(_opacityStep);

            Label percentLabel = NewHelpLabel("%");
            percentLabel.Name = "percentLabel";
            percentLabel.ForeColor = Color.White;
            percentLabel.TextAlign = ContentAlignment.MiddleCenter;
            _appearanceSection.Controls.Add(percentLabel);

            _opacitySlider = new ModernSlider();
            _opacitySlider.Minimum = 10;
            _opacitySlider.Maximum = 100;
            _opacitySlider.ValueChanged += delegate
            {
                if (_binding) return;
                DpadConfig d = DpadTarget();
                if (d != null)
                {
                    d.Opacity = ClampDpadOpacity(_opacitySlider.Value);
                    _opacityStep.SetValueSilent(d.Opacity);
                    RaiseModified();
                    return;
                }
                if (_selected == null) return;
                _selected.Opacity = _opacitySlider.Value;
                _opacityStep.SetValueSilent(_selected.Opacity);
                RaiseModified();
            };
            _appearanceSection.Controls.Add(_opacitySlider);

            Label imageLabel = NewFieldLabel("按键图标");
            imageLabel.Name = "imageLabel";
            _appearanceSection.Controls.Add(imageLabel);

            _imagePreview = new ImagePreviewBox();
            _appearanceSection.Controls.Add(_imagePreview);

            _chooseImage = new ModernButton();
            _chooseImage.Text = "选择 PNG";
            _chooseImage.Style = ModernButtonStyle.Secondary;
            _chooseImage.Height = 34;
            _chooseImage.Click += delegate { ChooseImage(); };
            _appearanceSection.Controls.Add(_chooseImage);

            _clearImage = new ModernButton();
            _clearImage.Text = "移除图标";
            _clearImage.Style = ModernButtonStyle.Ghost;
            _clearImage.Height = 32;
            _clearImage.Click += delegate { ClearImage(); };
            _appearanceSection.Controls.Add(_clearImage);

            _imageHint = NewHelpLabel("支持透明 PNG；未设置图标时显示按键名称。");
            _imageHint.Name = "imageHint";
            _appearanceSection.Controls.Add(_imageHint);

            _hideLabel = NewFieldLabel("跟随隐藏模式隐藏");
            _hideLabel.Name = "hideLabel";
            _hideLabel.Font = Theme.UiFont(8.8f, false);
            _hideLabel.ForeColor = Theme.TextSoft;
            _appearanceSection.Controls.Add(_hideLabel);

            _hideToggle = new ToggleSwitch();
            _hideToggle.CheckedChanged += delegate
            {
                if (_binding) return;
                DpadConfig d = DpadTarget();
                if (d != null)
                {
                    d.HideWithHidden = _hideToggle.Checked;
                    RaiseModified();
                    return;
                }
                if (_selected == null) return;
                _selected.HideWithHidden = _hideToggle.Checked;
                RaiseModified();
            };
            _appearanceSection.Controls.Add(_hideToggle);

            Label hideHelp = NewHelpLabel("关闭后，该按键在「隐藏」状态下仍然保持显示。");
            hideHelp.Name = "hideHelp";
            _appearanceSection.Controls.Add(hideHelp);

            _appearanceSection.Resize += delegate { LayoutAppearanceSection(); };
            _sectionContent.Controls.Add(_appearanceSection);
        }

        private void BuildFeedbackSection()
        {
            _feedbackSection = NewSection("多模态触控反馈", "按下时的视觉、音效与震动反馈，每个按键独立配置", 580);

            // ---------- 视觉反馈 ----------
            Label visualTitle = NewFieldLabel("视觉反馈");
            visualTitle.Font = Theme.UiFont(8.8f, true);
            visualTitle.ForeColor = Theme.TextSoft;
            visualTitle.Name = "visualTitle";
            _feedbackSection.Controls.Add(visualTitle);

            _visualToggle = new ToggleSwitch();
            _visualToggle.CheckedChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.VisualFeedbackEnabled = _visualToggle.Checked;
                UpdateFeedbackEnabled();
                RaiseModified();
            };
            _feedbackSection.Controls.Add(_visualToggle);

            _visualSelect = new ModernSelect();
            _visualSelect.SelectionChanged += delegate
            {
                if (_binding || _selected == null) return;
                int id;
                if (int.TryParse(_visualSelect.SelectedId, out id)) _selected.VisualPreset = id;
                UpdateFeedbackEnabled();
                RaiseModified();
            };
            _feedbackSection.Controls.Add(_visualSelect);

            _visualColorSwatch = new Panel();
            _visualColorSwatch.Name = "visualColorSwatch";
            _visualColorSwatch.Cursor = Cursors.Hand;
            _visualColorSwatch.Click += delegate { ChooseVisualColor(); };
            _feedbackSection.Controls.Add(_visualColorSwatch);

            _visualColorNameLabel = NewFieldLabel("白色");
            _visualColorNameLabel.Name = "visualColorNameLabel";
            _feedbackSection.Controls.Add(_visualColorNameLabel);

            Label intensityLabel = NewFieldLabel("强度");
            intensityLabel.Name = "intensityLabel";
            _feedbackSection.Controls.Add(intensityLabel);

            _visualIntensityStep = new StepperBox();
            _visualIntensityStep.Minimum = 0;
            _visualIntensityStep.Maximum = 100;
            _visualIntensityStep.Step = 1;
            _visualIntensityStep.ValueChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.VisualIntensity = _visualIntensityStep.Value;
                RaiseModified();
            };
            _feedbackSection.Controls.Add(_visualIntensityStep);

            Label intensityUnit = NewHelpLabel("%");
            intensityUnit.Name = "intensityUnit";
            intensityUnit.ForeColor = Color.White;
            intensityUnit.TextAlign = ContentAlignment.MiddleCenter;
            _feedbackSection.Controls.Add(intensityUnit);

            Label durationLabel = NewFieldLabel("时长");
            durationLabel.Name = "durationLabel";
            _feedbackSection.Controls.Add(durationLabel);

            _visualDurationStep = new StepperBox();
            _visualDurationStep.Minimum = 100;
            _visualDurationStep.Maximum = 1000;
            _visualDurationStep.Step = 10;
            _visualDurationStep.ValueChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.VisualDurationMs = _visualDurationStep.Value;
                RaiseModified();
            };
            _feedbackSection.Controls.Add(_visualDurationStep);

            Label durationUnit = NewHelpLabel("ms");
            durationUnit.Name = "durationUnit";
            durationUnit.ForeColor = Color.White;
            durationUnit.TextAlign = ContentAlignment.MiddleCenter;
            _feedbackSection.Controls.Add(durationUnit);

            Label scaleLabel = NewFieldLabel("缩放");
            scaleLabel.Name = "scaleLabel";
            _feedbackSection.Controls.Add(scaleLabel);

            _visualScaleStep = new StepperBox();
            _visualScaleStep.Minimum = 80;
            _visualScaleStep.Maximum = 100;
            _visualScaleStep.Step = 1;
            _visualScaleStep.ValueChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.VisualScalePercent = _visualScaleStep.Value;
                RaiseModified();
            };
            _feedbackSection.Controls.Add(_visualScaleStep);

            Label scaleUnit = NewHelpLabel("%");
            scaleUnit.Name = "scaleUnit";
            scaleUnit.ForeColor = Color.White;
            scaleUnit.TextAlign = ContentAlignment.MiddleCenter;
            _feedbackSection.Controls.Add(scaleUnit);

            // ---------- 音效反馈 ----------
            Label soundTitle = NewFieldLabel("音效反馈");
            soundTitle.Font = Theme.UiFont(8.8f, true);
            soundTitle.ForeColor = Theme.TextSoft;
            soundTitle.Name = "soundTitle";
            _feedbackSection.Controls.Add(soundTitle);

            _soundToggle = new ToggleSwitch();
            _soundToggle.CheckedChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.SoundEnabled = _soundToggle.Checked;
                UpdateFeedbackEnabled();
                RaiseModified();
            };
            _feedbackSection.Controls.Add(_soundToggle);

            _soundSelect = new ModernSelect();
            _soundSelect.SelectionChanged += delegate
            {
                if (_binding || _selected == null) return;
                int id;
                if (int.TryParse(_soundSelect.SelectedId, out id)) _selected.SoundPreset = id;
                UpdateFeedbackEnabled();
                RaiseModified();
            };
            _feedbackSection.Controls.Add(_soundSelect);

            Label volumeLabel = NewFieldLabel("音量");
            volumeLabel.Name = "volumeLabel";
            _feedbackSection.Controls.Add(volumeLabel);

            _soundVolumeStep = new StepperBox();
            _soundVolumeStep.Minimum = 0;
            _soundVolumeStep.Maximum = 100;
            _soundVolumeStep.Step = 1;
            _soundVolumeStep.ValueChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.SoundVolume = _soundVolumeStep.Value;
                RaiseModified();
            };
            _feedbackSection.Controls.Add(_soundVolumeStep);

            Label volumeUnit = NewHelpLabel("%");
            volumeUnit.Name = "volumeUnit";
            volumeUnit.ForeColor = Color.White;
            volumeUnit.TextAlign = ContentAlignment.MiddleCenter;
            _feedbackSection.Controls.Add(volumeUnit);

            _chooseSound = new ModernButton();
            _chooseSound.Text = "选择音效";
            _chooseSound.Style = ModernButtonStyle.Secondary;
            _chooseSound.Height = 34;
            _chooseSound.Click += delegate { ChooseSoundFile(); };
            _feedbackSection.Controls.Add(_chooseSound);

            _testSound = new ModernButton();
            _testSound.Text = "试听";
            _testSound.Style = ModernButtonStyle.Ghost;
            _testSound.Height = 34;
            _testSound.Click += delegate { TestSound(); };
            _feedbackSection.Controls.Add(_testSound);

            _soundFileLabel = NewHelpLabel("未选择自定义音效文件");
            _soundFileLabel.Name = "soundFileLabel";
            _feedbackSection.Controls.Add(_soundFileLabel);

            // ---------- 震动反馈 ----------
            Label rumbleTitle = NewFieldLabel("震动反馈");
            rumbleTitle.Font = Theme.UiFont(8.8f, true);
            rumbleTitle.ForeColor = Theme.TextSoft;
            rumbleTitle.Name = "rumbleTitle";
            _feedbackSection.Controls.Add(rumbleTitle);

            _rumbleToggle = new ToggleSwitch();
            _rumbleToggle.CheckedChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.RumbleEnabled = _rumbleToggle.Checked;
                UpdateFeedbackEnabled();
                RaiseModified();
            };
            _feedbackSection.Controls.Add(_rumbleToggle);

            _rumbleSelect = new ModernSelect();
            _rumbleSelect.SelectionChanged += delegate
            {
                if (_binding || _selected == null) return;
                int id;
                if (int.TryParse(_rumbleSelect.SelectedId, out id)) _selected.RumblePreset = id;
                UpdateFeedbackEnabled();
                RaiseModified();
            };
            _feedbackSection.Controls.Add(_rumbleSelect);

            Label rleftLabel = NewFieldLabel("左马达");
            rleftLabel.Name = "rleftLabel";
            _feedbackSection.Controls.Add(rleftLabel);
            Label rrightLabel = NewFieldLabel("右马达");
            rrightLabel.Name = "rrightLabel";
            _feedbackSection.Controls.Add(rrightLabel);
            Label rdurationLabel = NewFieldLabel("时长");
            rdurationLabel.Name = "rdurationLabel";
            _feedbackSection.Controls.Add(rdurationLabel);
            Label rleftUnit = NewHelpLabel("%");
            rleftUnit.Name = "rleftUnit";
            rleftUnit.ForeColor = Color.White;
            rleftUnit.TextAlign = ContentAlignment.MiddleCenter;
            _feedbackSection.Controls.Add(rleftUnit);
            Label rrightUnit = NewHelpLabel("%");
            rrightUnit.Name = "rrightUnit";
            rrightUnit.ForeColor = Color.White;
            rrightUnit.TextAlign = ContentAlignment.MiddleCenter;
            _feedbackSection.Controls.Add(rrightUnit);
            Label rdurationUnit = NewHelpLabel("ms");
            rdurationUnit.Name = "rdurationUnit";
            rdurationUnit.ForeColor = Color.White;
            rdurationUnit.TextAlign = ContentAlignment.MiddleCenter;
            _feedbackSection.Controls.Add(rdurationUnit);

            _rumbleLeftStep = new StepperBox();
            _rumbleLeftStep.Minimum = 0;
            _rumbleLeftStep.Maximum = 100;
            _rumbleLeftStep.Step = 1;
            _rumbleLeftStep.ValueChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.RumbleLeftMotor = _rumbleLeftStep.Value;
                RaiseModified();
            };
            _feedbackSection.Controls.Add(_rumbleLeftStep);

            _rumbleRightStep = new StepperBox();
            _rumbleRightStep.Minimum = 0;
            _rumbleRightStep.Maximum = 100;
            _rumbleRightStep.Step = 1;
            _rumbleRightStep.ValueChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.RumbleRightMotor = _rumbleRightStep.Value;
                RaiseModified();
            };
            _feedbackSection.Controls.Add(_rumbleRightStep);

            _rumbleDurationStep = new StepperBox();
            _rumbleDurationStep.Minimum = 50;
            _rumbleDurationStep.Maximum = 3000;
            _rumbleDurationStep.Step = 10;
            _rumbleDurationStep.ValueChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.RumbleDurationMs = _rumbleDurationStep.Value;
                RaiseModified();
            };
            _feedbackSection.Controls.Add(_rumbleDurationStep);

            Label rumblePatternTitle = NewFieldLabel("自定义节奏");
            rumblePatternTitle.Name = "rumblePatternTitle";
            rumblePatternTitle.Font = Theme.UiFont(8.8f, true);
            _feedbackSection.Controls.Add(rumblePatternTitle);

            Label rumblePatternHint = NewHelpLabel("格式：左马达:右马达:时长(毫秒)，逗号分段；例：100:80:120,0:0:90,70:70:180");
            rumblePatternHint.Name = "rumblePatternHint";
            _feedbackSection.Controls.Add(rumblePatternHint);

            _rumblePatternBox = new RoundTextBox();
            _rumblePatternBox.Placeholder = "先在上方节奏中选择「自定义节奏」后在此编排";
            _rumblePatternBox.TextChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.RumblePattern = _rumblePatternBox.Text;
                RaiseModified();
            };
            _feedbackSection.Controls.Add(_rumblePatternBox);

            _rumbleTestButton = new ModernButton();
            _rumbleTestButton.Text = "试听";
            _rumbleTestButton.Style = ModernButtonStyle.Secondary;
            _rumbleTestButton.Height = 34;
            _rumbleTestButton.Click += delegate { TestKeyRumble(); };
            _feedbackSection.Controls.Add(_rumbleTestButton);

            // 反馈卡片内标签统一提亮：单位（%、ms 等）保持亮白，字段名 / 颜色名 /
            // 说明文字改用 TextSoft，避免暗色背景上 Muted / Faint 文字与背景融为一体。
            foreach (Control ctl in _feedbackSection.Controls)
            {
                Label lab = ctl as Label;
                if (lab == null) continue;
                if (lab.Name.EndsWith("Unit"))
                {
                    lab.ForeColor = Color.White;
                }
                else
                {
                    lab.ForeColor = Theme.TextSoft;
                }
            }
            // 自定义节奏小标题与格式说明在暗背景上单独提亮，避免被默认灰色融进背景。
            rumblePatternTitle.ForeColor = Theme.Text;
            rumblePatternHint.ForeColor = Theme.TextSoft;

            _feedbackSection.Resize += delegate { LayoutFeedbackSection(); };
            _sectionContent.Controls.Add(_feedbackSection);
        }

        private void BuildFollowSection()
        {
            _followSection = NewSection("联动保持长按", "松开本键时，如果所选控件仍在操作，则继续保持按住，直到该控件停止", 188);

            _followToggle = new ToggleSwitch();
            _followToggle.CheckedChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.FollowControlEnabled = _followToggle.Checked;
                if (_selected.FollowControlEnabled && string.IsNullOrEmpty(_selected.FollowControlId))
                {
                    _selected.FollowControlId = "joystick";
                }
                UpdateFollowEnabled();
                RaiseModified();
            };
            _followSection.Controls.Add(_followToggle);

            _followLabel = NewFieldLabel("对象控件");
            _followLabel.Name = "followLabel";
            _followSection.Controls.Add(_followLabel);

            _followSelect = new ModernSelect();
            _followSelect.SelectionChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.FollowControlId = _followSelect.SelectedId;
                RaiseModified();
            };
            _followSection.Controls.Add(_followSelect);

            _followHelp = NewHelpLabel("示例：操控摇杆时按住 Shift，松开手指后 Shift 保持按下；摇杆停止操作后自动松开。");
            _followHelp.Name = "followHelp";
            _followSection.Controls.Add(_followHelp);

            _followSection.Resize += delegate { LayoutFollowSection(); };
            _sectionContent.Controls.Add(_followSection);
        }

        // 叠加层呼出卡片：主模式单个虚拟按键可配置为点击唤出指定叠加模式按键层。
        private void BuildOverlaySection()
        {
            _overlaySection = NewSection("叠加层呼出", "可选：点击此虚拟键唤出叠加模式按键层", 232);

            _overlayToggle = new ToggleSwitch();
            _overlayToggle.CheckedChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.OverlayEnabled = _overlayToggle.Checked;
                UpdateOverlayEnabled();
                RaiseModified();
            };
            _overlaySection.Controls.Add(_overlayToggle);

            Label overlayLabel = NewFieldLabel("呼出叠加模式");
            overlayLabel.Name = "overlayLabel";
            _overlaySection.Controls.Add(overlayLabel);

            _overlaySelect = new ModernSelect();
            _overlaySelect.SelectionChanged += delegate
            {
                if (_binding || _selected == null) return;
                int idx;
                if (int.TryParse(_overlaySelect.SelectedId, out idx)) _selected.OverlayTargetMode = idx;
                RaiseModified();
            };
            _overlaySection.Controls.Add(_overlaySelect);

            Label hideLabel = NewFieldLabel("无操作隐藏");
            hideLabel.Name = "overlayHideLabel";
            _overlaySection.Controls.Add(hideLabel);

            _overlayHideStep = new StepperBox();
            _overlayHideStep.Minimum = 0;
            _overlayHideStep.Maximum = 60;
            _overlayHideStep.Step = 1;
            _overlayHideStep.ValueChanged += delegate
            {
                if (_binding || _selected == null) return;
                _selected.OverlayAutoHideSeconds = _overlayHideStep.Value;
                RaiseModified();
            };
            _overlaySection.Controls.Add(_overlayHideStep);

            _overlayHideHint = NewHelpLabel("秒");
            _overlayHideHint.ForeColor = Color.White;
            _overlayHideHint.Font = Theme.UiFont(8.0f, false);
            _overlayHideHint.TextAlign = ContentAlignment.MiddleCenter;
            _overlaySection.Controls.Add(_overlayHideHint);

            _overlayHelp = NewHelpLabel("可不绑定物理按键：触摸此虚拟键即可唤出叠加层；叠加层按键盖在主层之上，摇杆与视角仍跟随主模式。无操作 N 秒后自动隐藏，0 表示常驻。");
            _overlaySection.Controls.Add(_overlayHelp);

            _overlaySection.Resize += delegate { LayoutOverlaySection(); };
            _sectionContent.Controls.Add(_overlaySection);
        }

        public void SetOverlayModes(List<OverlayModeConfig> modes)
        {
            _overlayModes = modes;
            RefreshOverlayOptions();
        }

        // 切换顶层页面（按键/手柄）时换绑折叠记忆配置根，两套折叠状态互不串。
        public void SetContextRoot(AppConfig root)
        {
            _config = root;
        }

        // 页面切换入口：false=虚拟按键（键盘/鼠标绑定），true=虚拟手柄（手柄按键绑定）。两套控件并存，按页显隐。
        public void SetPadSide(bool gamepad)
        {
            _padSide = gamepad;
            if (_addButton != null) _addButton.Text = gamepad ? "新增手柄" : "新增按键";

            if (_basicSection != null)
            {
                _basicSection.Hint = gamepad ? "设置显示名称和短按手柄键" : "设置显示名称和短按绑定按键";
                Label shortLabel = _basicSection.Controls["shortLabel"] as Label;
                if (shortLabel != null) shortLabel.Text = gamepad ? "绑定手柄键 · 短按" : "绑定按键 · 短按";
                if (_shortSelect != null) _shortSelect.Visible = gamepad;
                if (_shortCapture != null) _shortCapture.Visible = !gamepad;
                if (_shortHelp != null) _shortHelp.Text = gamepad
                    ? "从下拉列表选择对应的虚拟手柄按键。"
                    : "点击输入框后按下任意键绑定；右键可切换鼠标键。";
            }
            if (_longSection != null)
            {
                Label longLabel = _longSection.Controls["longLabel"] as Label;
                if (longLabel != null) longLabel.Text = gamepad ? "长按手柄键" : "长按按键";
                if (_longPickButton != null) _longPickButton.Visible = gamepad;
                if (_longCapture != null) _longCapture.Visible = !gamepad;
            }
            if (_doubleSection != null)
            {
                Label doubleLabel = _doubleSection.Controls["doubleLabel"] as Label;
                if (doubleLabel != null) doubleLabel.Text = gamepad ? "双击手柄键" : "双击按键";
                if (_doublePickButton != null) _doublePickButton.Visible = gamepad;
                if (_doubleCapture != null) _doubleCapture.Visible = !gamepad;
            }
            if (_emptyView != null)
            {
                _emptyView.SetTexts(gamepad ? "当前模式还没有手柄按键" : "当前模式还没有按键",
                    gamepad ? "点击右上角「新增手柄」创建第一个控件" : "点击右上角「新增按键」创建第一个按键");
            }
            if (_emptyOverlayView != null)
            {
                _emptyOverlayView.SetTexts(gamepad ? "本叠加层还没有手柄按键" : "本叠加层还没有按键",
                    gamepad ? "点击右上角「新增手柄」创建叠加层控件" : "点击右上角「新增按键」创建叠加层按键");
            }
            RebuildChips();
            BindSelected();
            UpdateVisibility();
            LayoutBasicSection();
            LayoutLongSection();
            LayoutDoubleSection();
            Invalidate();
        }

        private void RefreshOverlayOptions()
        {
            List<SelectOption> options = new List<SelectOption>();
            if (_overlayModes != null)
            {
                for (int i = 0; i < _overlayModes.Count; i++)
                {
                    OverlayModeConfig m = _overlayModes[i];
                    string name = m == null || string.IsNullOrEmpty(m.Name) ? "叠加" + (i + 1) : m.Name;
                    options.Add(new SelectOption(i.ToString(CultureInfo.InvariantCulture), name));
                }
            }
            string selectedId = _selected == null ? "0" : _selected.OverlayTargetMode.ToString(CultureInfo.InvariantCulture);
            bool exists = false;
            for (int i = 0; i < options.Count; i++) if (options[i].Id == selectedId) exists = true;
            if (!exists)
            {
                selectedId = options.Count > 0 ? options[0].Id : "0";
                if (_selected != null) _selected.OverlayTargetMode = 0;
            }
            _overlaySelect.SetItems(options, selectedId);
        }

        // 视觉 / 音效两族预设下拉项（固定五项，编号与模型层 Normalize 区间一致）。
        private void RefreshFeedbackOptions()
        {
            List<SelectOption> visual = new List<SelectOption>();
            visual.Add(new SelectOption("0", "按下缩放"));
            visual.Add(new SelectOption("1", "高亮闪白"));
            visual.Add(new SelectOption("2", "边框发光"));
            visual.Add(new SelectOption("3", "颜色脉冲"));
            visual.Add(new SelectOption("4", "不透明度脉冲"));
            int vp = _selected == null ? 1 : _selected.VisualPreset;
            _visualSelect.SetItems(visual, vp.ToString(CultureInfo.InvariantCulture));

            List<SelectOption> sound = new List<SelectOption>();
            sound.Add(new SelectOption("0", "轻咔哒"));
            sound.Add(new SelectOption("1", "确认音"));
            sound.Add(new SelectOption("2", "错误音"));
            sound.Add(new SelectOption("3", "长音"));
            sound.Add(new SelectOption("4", "自定义文件"));
            int sp = _selected == null ? 0 : _selected.SoundPreset;
            _soundSelect.SetItems(sound, sp.ToString(CultureInfo.InvariantCulture));

            List<SelectOption> rumble = new List<SelectOption>();
            rumble.Add(new SelectOption("0", "单次脉冲"));
            rumble.Add(new SelectOption("1", "心跳"));
            rumble.Add(new SelectOption("2", "提醒·三短"));
            rumble.Add(new SelectOption("3", "冲击·渐弱"));
            rumble.Add(new SelectOption("4", "连发"));
            rumble.Add(new SelectOption("5", "自定义节奏"));
            int rp = _selected == null ? 0 : _selected.RumblePreset;
            _rumbleSelect.SetItems(rumble, rp.ToString(CultureInfo.InvariantCulture));
        }

        private void UpdateOverlayEnabled()
        {
            bool on = _selected != null && _selected.OverlayEnabled;
            _overlaySelect.Enabled = on;
            _overlayHideStep.Enabled = on;
            _overlayHelp.ForeColor = on ? Theme.Faint : Theme.Alpha(Theme.Faint, 80);
            Label overlayLabel = _overlaySection.Controls["overlayLabel"] as Label;
            Label hideLabel = _overlaySection.Controls["overlayHideLabel"] as Label;
            if (overlayLabel != null) overlayLabel.ForeColor = on ? Theme.Muted : Theme.Faint;
            if (hideLabel != null) hideLabel.ForeColor = on ? Theme.Muted : Theme.Faint;
        }

        // 叠加模式级卡片：启停本层、预览单独显示开关，仅叠加模式编辑态可见。
        private void BuildOverlayModeSection()
        {
            _overlayModeSection = NewSection("叠加模式", "本层启停与预览显示方式", 158);

            Label enabledLabel = NewFieldLabel("启用本叠加层");
            enabledLabel.Name = "enabledLabel";
            _overlayModeSection.Controls.Add(enabledLabel);

            _overlayEnabledToggle = new ToggleSwitch();
            _overlayEnabledToggle.CheckedChanged += delegate
            {
                if (_binding || _overlayMode == null) return;
                _overlayMode.Enabled = _overlayEnabledToggle.Checked;
                RaiseModified();
            };
            _overlayModeSection.Controls.Add(_overlayEnabledToggle);

            Label soloLabel = NewFieldLabel("在下方预览中单独显示");
            soloLabel.Name = "soloLabel";
            _overlayModeSection.Controls.Add(soloLabel);

            _soloToggle = new ToggleSwitch();
            _soloToggle.CheckedChanged += delegate
            {
                if (_binding || _overlayMode == null) return;
                _overlayMode.SoloInPreview = _soloToggle.Checked;
                RaiseModified();
            };
            _overlayModeSection.Controls.Add(_soloToggle);

            _soloHelp = NewHelpLabel("勾选后预览只显示本叠加层按键、隐藏主模式按键；关闭时两层按键共同叠加显示。摇杆与视角始终跟随主模式。");
            _overlayModeSection.Controls.Add(_soloHelp);

            _overlayModeSection.Resize += delegate { LayoutOverlayModeSection(); };
            _sectionContent.Controls.Add(_overlayModeSection);
        }

        private void BindOverlayModeSection()
        {
            _binding = true;
            try
            {
                bool has = _overlayMode != null;
                _overlayEnabledToggle.SetCheckedSilent(has && _overlayMode.Enabled);
                _soloToggle.SetCheckedSilent(has && _overlayMode.SoloInPreview);
            }
            finally
            {
                _binding = false;
            }
        }

        private SectionPanel NewSection(string title, string hint, int height)
        {
            SectionPanel section = new SectionPanel();
            section.Title = title;
            section.Hint = hint;
            section.Height = Theme.S(height);
            section.Width = Theme.S(300);
            section.Margin = new Padding(0, 0, 0, Theme.S(10));
            section.TitleLeftPad = 34f;
            return section;
        }

        private Label NewFieldLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.BackColor = Theme.SurfaceAlt;
            label.ForeColor = Theme.Muted;
            label.Font = Theme.UiFont(8.2f, false);
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private Label NewHelpLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.BackColor = Theme.SurfaceAlt;
            label.ForeColor = Theme.Faint;
            label.Font = Theme.UiFont(7.7f, false);
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private void RebuildChips()
        {
            _chipFlow.SuspendLayout();
            for (int i = 0; i < _chips.Count; i++)
            {
                _chips[i].Dispose();
            }
            _chips.Clear();
            for (int i = 0; i < _stickChips.Count; i++)
            {
                _stickChips[i].Dispose();
            }
            _stickChips.Clear();
            float s = Theme.Scale;
            List<VirtualKey> keys = CurrentKeys;
            if (keys != null)
            {
                for (int i = 0; i < keys.Count; i++)
                {
                    VirtualKey key = keys[i];
                    KeyChip chip = new KeyChip();
                    chip.SetKey(key, i);
                    chip.Selected = ReferenceEquals(key, _selected);
                    chip.Width = (int)(92 * s);
                    chip.Height = (int)(46 * s);
                    chip.Margin = new Padding(0, 0, (int)(6 * s), (int)(7 * s));
                    chip.Click += ChipOnClick;
                    _chips.Add(chip);
                    _chipFlow.Controls.Add(chip);
                }
            }
            // 左右摇杆与方向键条目仅手柄页展示；按键页只保留普通按键 chips。
            if (_padSide && _mode != null)
            {
                AddStickChip(0, "左摇杆", _mode.Joystick != null && _mode.Joystick.Enabled, _selectedStickKind == 0);
                AddStickChip(1, "右摇杆", _mode.JoystickR != null && _mode.JoystickR.Enabled, _selectedStickKind == 1);
                AddStickChip(2, "方向键", _mode.Dpad != null && _mode.Dpad.Enabled, _selectedStickKind == 2);
            }
            _chipFlow.ResumeLayout();
            LayoutEditor();
            Invalidate();
        }

        private void AddStickChip(int kind, string title, bool enabled, bool selected)
        {
            float s = Theme.Scale;
            StickChip chip = new StickChip(kind, title, enabled, selected);
            chip.Width = (int)(92 * s);
            chip.Height = (int)(46 * s);
            chip.Margin = new Padding(0, 0, (int)(6 * s), (int)(7 * s));
            chip.Click += delegate
            {
                Action<int> h = StickSelected;
                if (h != null) h(kind);
            };
            chip.HideClick += delegate
            {
                Action<int> t = StickToggle;
                if (t != null) t(kind);
            };
            _stickChips.Add(chip);
            _chipFlow.Controls.Add(chip);
        }

        // 手机预览或右侧条目选中摇杆/方向键整体：记录选中态并重绘高亮，右侧面板切到对应参数。
        public void SelectStick(int kind)
        {
            _selectedStickKind = kind;
            _selected = null;
            for (int i = 0; i < _stickChips.Count; i++)
            {
                _stickChips[i].Selected = _stickChips[i].Kind == kind;
                _stickChips[i].Invalidate();
            }
            BindSelectedStick(kind);
            UpdateVisibility();
            Invalidate();
        }

        // 方向键在右侧「外观与行为」卡片调圆盘尺寸 / 不透明度；左右摇杆参数在左侧「手柄摇杆」卡片调节。
        private void BindSelectedStick(int kind)
        {
            if (kind == 2 && _mode != null && _mode.Dpad != null)
            {
                DpadConfig d = _mode.Dpad;
                _addButton.Enabled = true;
                _deleteButton.Enabled = false;
                _binding = true;
                try
                {
                    // 方向键圆盘尺寸范围 80–400，普通按键为 40–360。
                    _sizeStep.Minimum = 80;
                    _sizeStep.Maximum = 400;
                    _sizeSlider.Minimum = 80;
                    _sizeSlider.Maximum = 400;
                    _sizeStep.SetValueSilent(d.Size);
                    _sizeSlider.SetValueSilent(d.Size);
                    _opacityStep.SetValueSilent(d.Opacity);
                    _opacitySlider.SetValueSilent(d.Opacity);
                    _hideToggle.SetCheckedSilent(d.HideWithHidden);
                    _imagePreview.SetImage(null);
                }
                finally
                {
                    _binding = false;
                }
            }
            else
            {
                _sizeStep.Minimum = 40;
                _sizeStep.Maximum = 360;
                _sizeSlider.Minimum = 40;
                _sizeSlider.Maximum = 360;
                BindSelected();
            }
        }

        public void ClearStickSelection()
        {
            if (_selectedStickKind < 0) return;
            _selectedStickKind = -1;
            for (int i = 0; i < _stickChips.Count; i++)
            {
                _stickChips[i].Selected = false;
                _stickChips[i].Invalidate();
            }
        }

        private void ChipOnClick(object sender, EventArgs e)
        {
            KeyChip chip = sender as KeyChip;
            if (chip == null || chip.Key == null) return;
            ClearStickSelection();
            SelectKey(chip.Key, true);
        }

        private void SelectChip(VirtualKey key)
        {
            for (int i = 0; i < _chips.Count; i++)
            {
                _chips[i].Selected = ReferenceEquals(_chips[i].Key, key);
                _chips[i].Invalidate();
            }
        }

        private void BindSelected()
        {
            _binding = true;
            try
            {
                bool has = _selected != null;
                // 普通按键尺寸量程 40–360；方向键 80–400 在 BindSelectedStick 内单独切换。
                _sizeStep.Minimum = 40;
                _sizeStep.Maximum = 360;
                _sizeSlider.Minimum = 40;
                _sizeSlider.Maximum = 360;
                _addButton.Enabled = true;
                _deleteButton.Enabled = has;
                if (!has)
                {
                    _nameInput.Text = "";
                    if (_padSide)
                    {
                        _shortSelect.SetItems(BuildPadOptions(), "none");
                        _longPickButton.Text = "未绑定";
                        _doublePickButton.Text = "未绑定";
                    }
                    else
                    {
                        _shortCapture.SetBindingSilent(new KeyBinding());
                        _longCapture.SetBindingSilent(new KeyBinding());
                        _doubleCapture.SetBindingSilent(new KeyBinding());
                    }
                    _longToggle.SetCheckedSilent(false);
                    _longDuration.SetValueSilent(650);
                    _doubleToggle.SetCheckedSilent(false);
                    _sizeStep.SetValueSilent(152);
                    _sizeSlider.SetValueSilent(152);
                    _opacityStep.SetValueSilent(100);
                    _opacitySlider.SetValueSilent(100);
                    _hideToggle.SetCheckedSilent(true);
                    _followToggle.SetCheckedSilent(false);
                    _followSelect.SetItems(new List<SelectOption>(), "joystick");
                    _overlayToggle.SetCheckedSilent(false);
                    _overlaySelect.SetItems(new List<SelectOption>(), "0");
                    _overlayHideStep.SetValueSilent(0);
                    _imagePreview.SetImage(null);
                    // 多模态反馈重置为默认值。
                    RefreshFeedbackOptions();
                    _visualToggle.SetCheckedSilent(false);
                    _visualIntensityStep.SetValueSilent(70);
                    _visualDurationStep.SetValueSilent(180);
                    _visualScaleStep.SetValueSilent(90);
                    _visualColorSwatch.BackColor = Color.White;
                    _visualColorNameLabel.Text = "#FFFFFF";
                    _soundToggle.SetCheckedSilent(false);
                    _soundVolumeStep.SetValueSilent(80);
                    _soundFileLabel.Text = "未选择自定义音效文件";
                    _rumbleToggle.SetCheckedSilent(false);
                    _rumbleLeftStep.SetValueSilent(60);
                    _rumbleRightStep.SetValueSilent(60);
                    _rumbleDurationStep.SetValueSilent(150);
                    _rumblePatternBox.Text = "";
                    UpdateFeedbackEnabled();
                    return;
                }

                _nameInput.Text = _selected.Name;
                if (_padSide)
                {
                    _shortSelect.SetItems(BuildPadOptions(), EncodePadBinding(_selected.ShortPress));
                    _longPickButton.Text = LongBindingText();
                    _doublePickButton.Text = DoubleBindingText();
                }
                else
                {
                    _shortCapture.SetBindingSilent(_selected.ShortPress);
                    _longCapture.SetBindingSilent(_selected.LongPress);
                    _doubleCapture.SetBindingSilent(_selected.DoublePress);
                }
                _longToggle.SetCheckedSilent(_selected.LongPressEnabled);
                _longDuration.SetValueSilent(_selected.LongPressMs);
                _doubleToggle.SetCheckedSilent(_selected.DoublePressEnabled);
                _sizeStep.SetValueSilent(_selected.Size);
                _sizeSlider.SetValueSilent(_selected.Size);
                _opacityStep.SetValueSilent(_selected.Opacity);
                _opacitySlider.SetValueSilent(_selected.Opacity);
                _hideToggle.SetCheckedSilent(_selected.HideWithHidden);
                _followToggle.SetCheckedSilent(_selected.FollowControlEnabled);
                RefreshFollowOptions();
                _overlayToggle.SetCheckedSilent(_selected.OverlayEnabled);
                RefreshOverlayOptions();
                _overlayHideStep.SetValueSilent(_selected.OverlayAutoHideSeconds);
                _imagePreview.SetImage(LoadSelectedImage());
                // 多模态反馈绑定。
                RefreshFeedbackOptions();
                _visualToggle.SetCheckedSilent(_selected.VisualFeedbackEnabled);
                _visualIntensityStep.SetValueSilent(_selected.VisualIntensity);
                _visualDurationStep.SetValueSilent(_selected.VisualDurationMs);
                _visualScaleStep.SetValueSilent(_selected.VisualScalePercent);
                SyncColorSwatch();
                _soundToggle.SetCheckedSilent(_selected.SoundEnabled);
                _soundVolumeStep.SetValueSilent(_selected.SoundVolume);
                SyncSoundFileLabel();
                _rumbleToggle.SetCheckedSilent(_selected.RumbleEnabled);
                _rumbleLeftStep.SetValueSilent(_selected.RumbleLeftMotor);
                _rumbleRightStep.SetValueSilent(_selected.RumbleRightMotor);
                _rumbleDurationStep.SetValueSilent(_selected.RumbleDurationMs);
                _rumblePatternBox.Text = _selected.RumblePattern;
                UpdateLongEnabled();
                UpdateDoubleEnabled();
                UpdateFollowEnabled();
                UpdateOverlayEnabled();
                UpdateFeedbackEnabled();
            }
            finally
            {
                _binding = false;
            }
        }

        private Image LoadSelectedImage()
        {
            if (_selected == null || string.IsNullOrEmpty(_selected.ImageFile)) return null;
            return ConfigStore.LoadImageCopy(_selected.ImageFile);
        }

        private void UpdateLongEnabled()
        {
            bool on = _selected != null && _selected.LongPressEnabled;
            if (_padSide) _longPickButton.Enabled = on;
            else if (_longCapture != null) _longCapture.Enabled = on;
            _longDuration.Enabled = on;
            _longHelp.ForeColor = on ? Theme.Faint : Theme.Alpha(Theme.Faint, 80);
            Label longLabel = _longSection.Controls["longLabel"] as Label;
            Label durLabel = _longSection.Controls["durLabel"] as Label;
            if (longLabel != null) longLabel.ForeColor = on ? Theme.Muted : Theme.Faint;
            if (durLabel != null) durLabel.ForeColor = on ? Theme.Muted : Theme.Faint;
        }

        private void UpdateDoubleEnabled()
        {
            bool on = _selected != null && _selected.DoublePressEnabled;
            if (_padSide) _doublePickButton.Enabled = on;
            else if (_doubleCapture != null) _doubleCapture.Enabled = on;
            _doubleHelp.ForeColor = on ? Theme.Faint : Theme.Alpha(Theme.Faint, 80);
            Label doubleLabel = _doubleSection.Controls["doubleLabel"] as Label;
            if (doubleLabel != null) doubleLabel.ForeColor = on ? Theme.Muted : Theme.Faint;
        }

        private void RefreshFollowOptions()
        {
            List<SelectOption> options = new List<SelectOption>();
            options.Add(new SelectOption("joystick", "摇杆"));
            if (_mode != null && _mode.Keys != null)
            {
                for (int i = 0; i < _mode.Keys.Count; i++)
                {
                    VirtualKey key = _mode.Keys[i];
                    if (key == null || ReferenceEquals(key, _selected)) continue;
                    string name = string.IsNullOrEmpty(key.Name) ? "未命名按键" : key.Name;
                    options.Add(new SelectOption(key.Id, "按键 · " + name));
                }
            }            string selectedId = _selected == null ? "joystick" : _selected.FollowControlId;
            bool exists = false;
            for (int i = 0; i < options.Count; i++) if (options[i].Id == selectedId) exists = true;
            if (!exists)
            {
                selectedId = "joystick";
                if (_selected != null) _selected.FollowControlId = "joystick";
            }
            _followSelect.SetItems(options, selectedId);
        }

        private void UpdateFollowEnabled()
        {
            bool on = _selected != null && _selected.FollowControlEnabled;
            _followSelect.Enabled = on;
            _followLabel.ForeColor = on ? Theme.Muted : Theme.Faint;
            _followHelp.ForeColor = on ? Theme.Faint : Theme.Alpha(Theme.Faint, 80);
        }

        // 多模态反馈：视觉、音效两个分区开关分别联动各自子控件；缩放仅对「按下缩放」
        // 预设启用，浏览/试听/文件名仅在自定义音效时可操作。
        private void UpdateFeedbackEnabled()
        {
            bool vOn = _selected != null && _selected.VisualFeedbackEnabled;
            bool sOn = _selected != null && _selected.SoundEnabled;
            bool rOn = _selected != null && _selected.RumbleEnabled;

            _visualSelect.Enabled = vOn;
            _visualColorSwatch.Enabled = vOn;
            _visualIntensityStep.Enabled = vOn;
            _visualDurationStep.Enabled = vOn;
            bool scaleOn = vOn && _selected != null && _selected.VisualPreset == 0;
            _visualScaleStep.Enabled = scaleOn;
            // 字段名 / 单位标签保持启用亮色，不随开关关闭被系统置灰降暗。

            _soundSelect.Enabled = sOn;
            _soundVolumeStep.Enabled = sOn;
            bool customSound = sOn && _selected != null && _selected.SoundPreset == 4;
            _chooseSound.Enabled = customSound;
            _testSound.Enabled = sOn;

            _rumbleSelect.Enabled = rOn;
            _rumbleLeftStep.Enabled = rOn;
            _rumbleRightStep.Enabled = rOn;
            _rumbleDurationStep.Enabled = rOn;
            _rumbleTestButton.Enabled = rOn;
            bool customRumble = rOn && _selected != null && _selected.RumblePreset == 5;
            // 输入框启用跟随震动开关；可编辑性仅在选「自定义节奏」时解锁，其余节奏只读并显示占位说明。
            _rumblePatternBox.Enabled = rOn;
            _rumblePatternBox.ReadOnly = !customRumble;
            _rumblePatternBox.Placeholder = customRumble
                ? "左:右:毫秒，逗号分段；例：100:80:120,0:0:90,70:70:180"
                : "先在上方节奏中选择「自定义节奏」后在此编排";
            // 静态说明标题/注释恒启用，不随震动开关被系统置灰；每次刷新强制重设亮色，防止颜色被重置。
            Label patternTitle = _feedbackSection.Controls["rumblePatternTitle"] as Label;
            Label patternHint = _feedbackSection.Controls["rumblePatternHint"] as Label;
            if (patternTitle != null)
            {
                patternTitle.Enabled = true;
                patternTitle.ForeColor = Theme.Text;
            }
            if (patternHint != null)
            {
                patternHint.Enabled = true;
                patternHint.ForeColor = Theme.TextSoft;
            }
        }

        private void ChooseVisualColor()
        {
            if (_selected == null) return;
            using (ColorDialog dlg = new ColorDialog())
            {
                dlg.Color = ParseFeedbackColor(_selected.VisualColor);
                dlg.FullOpen = true;
                dlg.AnyColor = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Color c = dlg.Color;
                _selected.VisualColor = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                SyncColorSwatch();
                RaiseModified();
            }
        }

        private void SyncColorSwatch()
        {
            if (_visualColorSwatch == null || _selected == null) return;
            _visualColorSwatch.BackColor = ParseFeedbackColor(_selected.VisualColor);
            _visualColorNameLabel.Text = _selected.VisualColor.ToUpperInvariant();
        }

        private static Color ParseFeedbackColor(string hex)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return Color.White;
                string h = hex.TrimStart('#');
                if (h.Length == 6)
                {
                    int r = Convert.ToInt32(h.Substring(0, 2), 16);
                    int g = Convert.ToInt32(h.Substring(2, 2), 16);
                    int b = Convert.ToInt32(h.Substring(4, 2), 16);
                    return Color.FromArgb(r, g, b);
                }
            }
            catch { }
            return Color.White;
        }

        private void ChooseSoundFile()
        {
            if (_selected == null) return;
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择按键音效（WAV / MP3）";
                dlg.Filter = "音频文件 (*.wav;*.mp3;*.wma;*.m4a;*.ogg)|*.wav;*.mp3;*.wma;*.m4a;*.ogg";
                dlg.Multiselect = false;
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string imported = ConfigStore.ImportSound(dlg.FileName);
                if (imported.Length == 0)
                {
                    Action<string, ToastKind> n = Notify;
                    if (n != null) n("导入音效失败，请检查文件是否有效。", ToastKind.Error);
                    return;
                }
                ConfigStore.DeleteSound(_selected.SoundFile);
                _selected.SoundFile = imported;
                SyncSoundFileLabel();
                RaiseModified();
            }
        }

        private void SyncSoundFileLabel()
        {
            if (_soundFileLabel == null || _selected == null) return;
            if (string.IsNullOrEmpty(_selected.SoundFile))
            {
                _soundFileLabel.Text = "未选择自定义音效文件";
            }
            else
            {
                string name = _selected.SoundFile;
                if (name.Length > 30) name = name.Substring(0, 28) + "…";
                _soundFileLabel.Text = name;
            }
        }

        private void TestSound()
        {
            if (_selected == null) return;
            FeedbackSound.Play(_selected);
        }

        // 按当前选中按键的震动参数试听一次；不依赖该按键开关，便于配置时即时确认节奏。
        private void TestKeyRumble()
        {
            if (_selected == null) return;
            RumbleConfig temp = new RumbleConfig();
            temp.Enabled = true;
            temp.Preset = _selected.RumblePreset;
            temp.Pattern = _selected.RumblePattern;
            temp.LeftMotor = _selected.RumbleLeftMotor;
            temp.RightMotor = _selected.RumbleRightMotor;
            temp.DurationMs = _selected.RumbleDurationMs;
            RumbleService.PulseFrom(temp);
        }

        private void UpdateVisibility()
        {
            bool hasKey = _selected != null;
            bool dpadSelected = _selectedStickKind == 2;
            bool stickSelected = _selectedStickKind >= 0;
            bool isMain = _mode != null;
            // 普通按键：基础信息、长按、外观、联动、反馈、技能冷却；方向键只保留外观与行为（尺寸/不透明度/随模式隐藏）；
            // 左右摇杆参数在左侧「手柄摇杆」卡片调节，右侧普通面板全部隐藏。
            _basicSection.Visible = hasKey;
            _longSection.Visible = hasKey;
            _doubleSection.Visible = hasKey;
            _appearanceSection.Visible = hasKey || dpadSelected;
            _feedbackSection.Visible = hasKey;
            _followSection.Visible = hasKey;
            // 叠加层呼出配置挂在主模式按键上；叠加模式按键自身不显示本卡片。
            _overlaySection.Visible = hasKey && isMain;
            // 叠加模式启停/单独显示卡片不再在按键设置面板显示（模型字段与预览逻辑保留）。
            _overlayModeSection.Visible = false;
            if (_cooldownPanel != null) _cooldownPanel.Visible = hasKey;
            // 方向键不使用 PNG 图标，隐藏图标行。
            if (_imagePreview != null) _imagePreview.Visible = !dpadSelected;
            if (_chooseImage != null) _chooseImage.Visible = !dpadSelected;
            if (_clearImage != null) _clearImage.Visible = !dpadSelected;
            if (_imageHint != null) _imageHint.Visible = !dpadSelected;
            // 主态无按键：全屏空态；叠加态无按键：叠加空态提示。摇杆/方向键选中不算空态。
            _emptyView.Visible = !hasKey && !stickSelected && _overlayMode == null;
            _emptyOverlayView.Visible = _overlayMode != null && !hasKey;
            List<VirtualKey> keys = CurrentKeys;
            _chipFlow.Visible = keys != null && keys.Count > 0;
            // 叠加态即使本层无按键，也要露出叠加空态提示。
            _sections.Visible = hasKey || dpadSelected || _overlayMode != null;
            if (!hasKey && !stickSelected && _overlayMode == null)
            {
                _emptyView.BringToFront();
            }
            LayoutEditor();
            LayoutSections();
            // 启动首次绑定时尚未挂到已显示父级，子控件 Visible 沿父链读到 false，会跳过堆叠；
            // 延后一帧在消息循环里再排一次，确保父链可见、尺寸就绪后卡片按序展开，不堆在同一 Top。
            // 句柄尚未创建时不能 BeginInvoke，跳过延迟重排，交给 Show 后的 Resize 流程纠正。
            if (IsHandleCreated)
            {
                BeginInvoke(new System.Windows.Forms.MethodInvoker(delegate
                {
                    LayoutEditor();
                    LayoutSections();
                }));
            }
        }

        private void NameInputOnTextChanged(object sender, EventArgs e)
        {
            if (_binding || _selected == null) return;
            string name = _nameInput.Text.Trim();
            _selected.Name = name.Length == 0 ? "" : name;
            for (int i = 0; i < _chips.Count; i++)
            {
                if (ReferenceEquals(_chips[i].Key, _selected))
                {
                    _chips[i].RefreshKey();
                    break;
                }
            }
            RaiseModified();
        }

        private void ShortCaptureOnBindingChanged(object sender, EventArgs e)
        {
            if (_binding || _selected == null) return;
            _selected.ShortPress = DecodePadBinding(_shortSelect.SelectedId);
            for (int i = 0; i < _chips.Count; i++)
            {
                if (ReferenceEquals(_chips[i].Key, _selected))
                {
                    _chips[i].RefreshKey();
                    break;
                }
            }
            RaiseModified();
        }

        // 按键页：短按键盘 / 鼠标捕获写入。
        private void ShortCaptureOnKeyBindingChanged(object sender, EventArgs e)
        {
            if (_binding || _selected == null) return;
            _selected.ShortPress = _shortCapture.Binding;
            for (int i = 0; i < _chips.Count; i++)
            {
                if (ReferenceEquals(_chips[i].Key, _selected))
                {
                    _chips[i].RefreshKey();
                    break;
                }
            }
            RaiseModified();
        }

        // 按键页：长按键盘 / 鼠标捕获写入。
        private void LongCaptureOnKeyBindingChanged(object sender, EventArgs e)
        {
            if (_binding || _selected == null) return;
            _selected.LongPress = _longCapture.Binding;
            RaiseModified();
        }

        private int CurrentLayout()
        {
            return _mode != null ? _mode.PadLayout : 0;
        }

        private string LongBindingText()
        {
            if (_selected == null) return "未绑定";
            string label = PadButtons.BindingLabel(_selected.LongPress, CurrentLayout());
            return string.IsNullOrEmpty(label) ? "未绑定" : label;
        }

        private void LongPickButtonOnClick(object sender, EventArgs e)
        {
            if (_binding || _selected == null) return;
            KeyBinding picked = PadBindingPickerDialog.Show(this, CurrentLayout(), _selected.LongPress);
            if (picked == null) return;
            _selected.LongPress = picked;
            _longPickButton.Text = LongBindingText();
            RaiseModified();
        }

        private void DoubleCaptureOnKeyBindingChanged(object sender, EventArgs e)
        {
            if (_binding || _selected == null) return;
            _selected.DoublePress = _doubleCapture.Binding;
            RaiseModified();
        }

        private string DoubleBindingText()
        {
            if (_selected == null) return "未绑定";
            string label = PadButtons.BindingLabel(_selected.DoublePress, CurrentLayout());
            return string.IsNullOrEmpty(label) ? "未绑定" : label;
        }

        private void DoublePickButtonOnClick(object sender, EventArgs e)
        {
            if (_binding || _selected == null) return;
            KeyBinding picked = PadBindingPickerDialog.Show(this, CurrentLayout(), _selected.DoublePress);
            if (picked == null) return;
            _selected.DoublePress = picked;
            _doublePickButton.Text = DoubleBindingText();
            RaiseModified();
        }

        // 当前模式厂商布局下的手柄按键选项（按钮位 + 左右扳机）。
        private List<SelectOption> BuildPadOptions()
        {
            int layout = _mode != null ? _mode.PadLayout : 0;
            List<SelectOption> list = new List<SelectOption>();
            list.Add(new SelectOption("none", "未绑定"));
            AddPadOption(list, layout, PadButtons.A, 0);
            AddPadOption(list, layout, PadButtons.B, 0);
            AddPadOption(list, layout, PadButtons.X, 0);
            AddPadOption(list, layout, PadButtons.Y, 0);
            AddPadOption(list, layout, PadButtons.LeftShoulder, 0);
            AddPadOption(list, layout, PadButtons.RightShoulder, 0);
            list.Add(new SelectOption("lt", PadButtons.VendorTrigger(layout, PadButtons.ModifierLeftTrigger)));
            list.Add(new SelectOption("rt", PadButtons.VendorTrigger(layout, PadButtons.ModifierRightTrigger)));
            AddPadOption(list, layout, PadButtons.DPadUp, 0);
            AddPadOption(list, layout, PadButtons.DPadDown, 0);
            AddPadOption(list, layout, PadButtons.DPadLeft, 0);
            AddPadOption(list, layout, PadButtons.DPadRight, 0);
            AddPadOption(list, layout, PadButtons.Start, 0);
            AddPadOption(list, layout, PadButtons.Back, 0);
            AddPadOption(list, layout, PadButtons.LeftThumb, 0);
            AddPadOption(list, layout, PadButtons.RightThumb, 0);
            return list;
        }

        private void AddPadOption(List<SelectOption> list, int layout, ushort code, int modifier)
        {
            string symbol = PadButtons.VendorSymbol(layout, code);
            list.Add(new SelectOption("b" + code, symbol));
        }

        private static string EncodePadBinding(KeyBinding b)
        {
            if (b == null || b.Kind != BindingKind.Gamepad) return "none";
            if (b.Modifiers == PadButtons.ModifierLeftTrigger) return "lt";
            if (b.Modifiers == PadButtons.ModifierRightTrigger) return "rt";
            return "b" + b.Code;
        }

        private static KeyBinding DecodePadBinding(string id)
        {
            if (string.IsNullOrEmpty(id) || id == "none") return new KeyBinding();
            if (id == "lt") return new KeyBinding(BindingKind.Gamepad, 0, PadButtons.ModifierLeftTrigger);
            if (id == "rt") return new KeyBinding(BindingKind.Gamepad, 0, PadButtons.ModifierRightTrigger);
            if (id.StartsWith("b"))
            {
                int code;
                if (int.TryParse(id.Substring(1), out code)) return new KeyBinding(BindingKind.Gamepad, code);
            }
            return new KeyBinding();
        }

        private void ChooseImage()
        {
            if (_selected == null) return;
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择透明 PNG 图标";
                dlg.Filter = "PNG 透明图片 (*.png)|*.png";
                dlg.Multiselect = false;
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    using (Image test = Image.FromFile(dlg.FileName))
                    {
                        if (test.Width <= 0 || test.Height <= 0) throw new InvalidOperationException();
                    }
                }
                catch
                {
                    Action<string, ToastKind> n = Notify;
                    if (n != null) n("无法读取该图片，请选择有效的 PNG 文件。", ToastKind.Error);
                    return;
                }
                string imported = ConfigStore.ImportImage(dlg.FileName);
                if (imported.Length == 0)
                {
                    Action<string, ToastKind> n = Notify;
                    if (n != null) n("图片导入失败，请检查文件权限。", ToastKind.Error);
                    return;
                }
                _selected.ImageFile = imported;
                _imagePreview.SetImage(LoadSelectedImage());
                RaiseModified();
                Action imageChanged = ImageChanged;
                if (imageChanged != null) imageChanged();
            }
        }

        private void ClearImage()
        {
            if (_selected == null || string.IsNullOrEmpty(_selected.ImageFile)) return;
            _selected.ImageFile = "";
            _imagePreview.SetImage(null);
            RaiseModified();
            Action imageChanged = ImageChanged;
            if (imageChanged != null) imageChanged();
        }

        private void OnRightCardStateChanged(CardDockItem item)
        {
            if (_config != null && item != null &&
                item.Slot >= 0 && item.Slot < _config.RightCardCollapsed.Count)
            {
                _config.RightCardCollapsed[item.Slot] = item.Collapsed;
                RaiseModified();
            }
            RefreshRightExpandButton();
        }

        private void RefreshRightExpandButton()
        {
            if (_dock == null || _expandAllRightButton == null) return;
            _expandAllRightButton.Text = _dock.AnyCollapsed ? "全部展开" : "全部收起";
        }

        // 当前选中方向键对应的整体配置；未选中方向键时返回 null，外观回调回落到普通按键分支。
        private DpadConfig DpadTarget()
        {
            if (_selectedStickKind != 2 || _mode == null || _mode.Dpad == null) return null;
            return _mode.Dpad;
        }

        private static int ClampDpadSize(int v)
        {
            return v < 80 ? 80 : (v > 400 ? 400 : v);
        }

        private static int ClampDpadOpacity(int v)
        {
            return v < 10 ? 10 : (v > 100 ? 100 : v);
        }

        private void RaiseModified()
        {
            Action h = KeyModified;
            if (h != null) h();
            Invalidate();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            LayoutEditor();
        }

        private void LayoutEditor()
        {
            if (_addButton == null || _deleteButton == null || _chipFlow == null || _sections == null) return;
            if (_expandAllRightButton == null || _emptyView == null) return;
            float s = Theme.Scale;
            int pad = (int)(16 * s);
            int headerH = (int)(58 * s);
            int chipH = PreferredChipHeight();
            int expandW = (int)(110 * s);
            int deleteW = (int)(100 * s);
            int addW = (int)(106 * s);
            int gap = (int)(8 * s);
            _expandAllRightButton.SetBounds(Width - pad - expandW, (int)(14 * s), expandW, (int)(34 * s));
            _deleteButton.SetBounds(_expandAllRightButton.Left - gap - deleteW, (int)(14 * s), deleteW, (int)(34 * s));
            _addButton.SetBounds(_deleteButton.Left - gap - addW, (int)(14 * s), addW, (int)(34 * s));
            _chipFlow.SetBounds(pad, headerH, Math.Max(20, Width - pad * 2), chipH);
            // 手动 5 列网格：FlowPanel 已关闭自动换行，直接按行列坐标定位，避免取整把第 5 项挤到下一行。
            int flowW = Math.Max(20, Width - pad * 2);
            int perRow = 5;
            int colGap = (int)(6 * s);
            int rowGap = (int)(7 * s);
            int rowH = (int)(53 * s);
            int colW = (flowW - colGap * (perRow - 1)) / perRow;
            int chipCount = _chipFlow.Controls.Count;
            for (int i = 0; i < chipCount; i++)
            {
                Control c = _chipFlow.Controls[i];
                int col = i % perRow;
                int row = i / perRow;
                c.Margin = Padding.Empty;
                c.SetBounds(col * (colW + colGap), row * (rowH + rowGap), colW, rowH);
            }
            _chipFlow.PerformLayout();
            _dividerY = headerH + chipH + (int)(2 * s);
            int sectionTop = headerH + chipH + (int)(8 * s);
            _sections.SetBounds(pad, sectionTop, Math.Max(40, Width - pad * 2),
                Math.Max(80, Height - sectionTop - (int)(14 * s)));
            _emptyView.SetBounds(pad, sectionTop, Math.Max(40, Width - pad * 2),
                Math.Max(80, Height - sectionTop - pad));

            LayoutSections();
            Invalidate();
        }

        private int PreferredChipHeight()
        {
            // 固定一排 5 个，最多 4 行全部展开不滚动；条目数 = 普通按键 + 左右摇杆 + 方向键。
            int n = _chipFlow == null ? 0 : _chipFlow.Controls.Count;
            if (n == 0) return 0;
            float s = Theme.Scale;
            int perRow = 5;
            int rows = (n + perRow - 1) / perRow;
            if (rows > 4) rows = 4;
            int rowH = (int)(53 * s);
            int rowGap = (int)(7 * s);
            return rows * rowH + (rows - 1) * rowGap + (int)(2 * s);
        }

        public void AttachCooldown(Control panel)
        {
            if (panel == null || _sectionContent == null) return;
            _cooldownPanel = panel;
            panel.Height = Theme.S(258);
            SectionPanel cooldownSection = panel as SectionPanel;
            if (cooldownSection != null) cooldownSection.TitleLeftPad = 34f;
            _sectionContent.Controls.Add(panel);
            if (_dock != null)
            {
                bool collapsed = _config != null && _config.RightCardCollapsed.Count > 6
                    ? _config.RightCardCollapsed[6] : true;
                _dock.Register(panel, 6, 18, Theme.S(258), true, true, collapsed, null);
            }
            LayoutSections();
        }

        private void SyncDockOrder()
        {
            if (_dock == null || _basicSection == null) return;
            _sectionContent.Controls.SetChildIndex(_basicSection, 0);
            int index = 1;
            foreach (CardDockItem item in _dock.Items)
            {
                _sectionContent.Controls.SetChildIndex(item.Card, index);
                index++;
            }
        }

        private void LayoutSections()
        {
            if (_sections == null || _sectionContent == null) return;
            SyncDockOrder();
            float s = Theme.Scale;
            int width = Math.Max(160, _sections.ContentWidth - (int)(4 * s));
            int y = (int)(2 * s);
            for (int i = 0; i < _sectionContent.Controls.Count; i++)
            {
                Control c = _sectionContent.Controls[i];
                if (object.Equals(c.Tag, "dockInsertLine")) continue;
                // 隐藏项也重置位置与宽度，避免残留旧坐标；但不参与堆叠高度。
                c.SetBounds((int)(2 * s), y, width, c.Height);
                if (!c.Visible) continue;
                y += c.Height + (int)(10 * s);
            }
            _sectionContent.Height = Math.Max(10, y + (int)(4 * s));
            _sections.UpdateScrollMetrics();
        }

        public SmoothScrollPane SectionPane
        {
            get { return _sections; }
        }

        private void LayoutBasicSection()
        {
            if (_nameInput == null) return;
            float s = Theme.Scale;
            int x = (int)(16 * s);
            int w = Math.Max(80, _basicSection.Width - (int)(32 * s));
            _basicSection.Controls["nameLabel"].SetBounds(x, (int)(54 * s), w, (int)(18 * s));
            _nameInput.SetBounds(x, (int)(74 * s), w, (int)(40 * s));
            _basicSection.Controls["shortLabel"].SetBounds(x, (int)(122 * s), w, (int)(18 * s));
            if (_shortSelect != null) _shortSelect.SetBounds(x, (int)(142 * s), w, (int)(44 * s));
            if (_shortCapture != null) _shortCapture.SetBounds(x, (int)(142 * s), w, (int)(44 * s));
            _shortHelp.SetBounds(x, (int)(190 * s), w, (int)(18 * s));
        }

        private void LayoutLongSection()
        {
            if (_longPickButton == null && _longCapture == null) return;
            float s = Theme.Scale;
            int x = (int)(16 * s);
            int w = Math.Max(80, _longSection.Width - (int)(32 * s));
            _longToggle.SetBounds(_longSection.Width - (int)(104 * s), (int)(10 * s), (int)(46 * s), (int)(26 * s));
            _longSection.Controls["longLabel"].SetBounds(x, (int)(52 * s), w, (int)(18 * s));
            if (_longPickButton != null) _longPickButton.SetBounds(x, (int)(72 * s), w, (int)(44 * s));
            if (_longCapture != null) _longCapture.SetBounds(x, (int)(72 * s), w, (int)(44 * s));
            _longSection.Controls["durLabel"].SetBounds(x, (int)(126 * s), (int)(110 * s), (int)(34 * s));
            _longDuration.SetBounds(_longSection.Width - (int)(172 * s), (int)(125 * s), (int)(112 * s), (int)(36 * s));
            _longDurationHint.SetBounds(_longSection.Width - (int)(52 * s), (int)(126 * s), (int)(36 * s), (int)(34 * s));
            _longHelp.SetBounds(x, (int)(174 * s), w, (int)(18 * s));
        }

        private void LayoutDoubleSection()
        {
            if (_doublePickButton == null && _doubleCapture == null) return;
            float s = Theme.Scale;
            int x = (int)(16 * s);
            int w = Math.Max(80, _doubleSection.Width - (int)(32 * s));
            _doubleToggle.SetBounds(_doubleSection.Width - (int)(104 * s), (int)(10 * s), (int)(46 * s), (int)(26 * s));
            _doubleSection.Controls["doubleLabel"].SetBounds(x, (int)(52 * s), w, (int)(18 * s));
            if (_doublePickButton != null) _doublePickButton.SetBounds(x, (int)(72 * s), w, (int)(44 * s));
            if (_doubleCapture != null) _doubleCapture.SetBounds(x, (int)(72 * s), w, (int)(44 * s));
            _doubleHelp.SetBounds(x, (int)(126 * s), w, (int)(18 * s));
        }

        private void LayoutAppearanceSection()
        {
            if (_sizeStep == null) return;
            float s = Theme.Scale;
            int x = (int)(16 * s);
            int w = Math.Max(80, _appearanceSection.Width - (int)(32 * s));
            _appearanceSection.Controls["sizeLabel"].SetBounds(x, (int)(54 * s), (int)(120 * s), (int)(34 * s));
            _sizeStep.SetBounds(_appearanceSection.Width - (int)(156 * s), (int)(50 * s), (int)(112 * s), (int)(36 * s));
            _appearanceSection.Controls["pxLabel"].SetBounds(_appearanceSection.Width - (int)(36 * s), (int)(54 * s), (int)(24 * s), (int)(30 * s));
            _sizeSlider.SetBounds(x, (int)(84 * s), w, (int)(26 * s));

            _appearanceSection.Controls["opacityLabel"].SetBounds(x, (int)(116 * s), (int)(120 * s), (int)(34 * s));
            _opacityStep.SetBounds(_appearanceSection.Width - (int)(156 * s), (int)(112 * s), (int)(112 * s), (int)(36 * s));
            _appearanceSection.Controls["percentLabel"].SetBounds(_appearanceSection.Width - (int)(36 * s), (int)(116 * s), (int)(24 * s), (int)(30 * s));
            _opacitySlider.SetBounds(x, (int)(146 * s), w, (int)(26 * s));

            _appearanceSection.Controls["imageLabel"].SetBounds(x, (int)(184 * s), w, (int)(18 * s));
            _imagePreview.SetBounds(x, (int)(204 * s), (int)(64 * s), (int)(64 * s));
            _chooseImage.SetBounds(x + (int)(76 * s), (int)(204 * s), (int)(92 * s), (int)(34 * s));
            _clearImage.SetBounds(x + (int)(76 * s), (int)(242 * s), (int)(92 * s), (int)(30 * s));
            _imageHint.SetBounds(x + (int)(178 * s), (int)(206 * s), Math.Max(80, w - (int)(178 * s)), (int)(34 * s));

            _hideLabel.SetBounds(x, (int)(288 * s), w - (int)(62 * s), (int)(28 * s));
            _hideToggle.SetBounds(_appearanceSection.Width - (int)(62 * s), (int)(288 * s), (int)(46 * s), (int)(26 * s));
            _appearanceSection.Controls["hideHelp"].SetBounds(x, (int)(316 * s), w, (int)(18 * s));
        }

        private void LayoutFeedbackSection()
        {
            if (_visualSelect == null) return;
            float s = Theme.Scale;
            int x = (int)(16 * s);
            int w = Math.Max(80, _feedbackSection.Width - (int)(32 * s));
            ControlCollection c = _feedbackSection.Controls;

            // 视觉反馈分区
            c["visualTitle"].SetBounds(x, (int)(44 * s), w - (int)(60 * s), (int)(20 * s));
            _visualToggle.SetBounds(_feedbackSection.Width - (int)(62 * s), (int)(42 * s), (int)(46 * s), (int)(26 * s));
            _visualSelect.SetBounds(x, (int)(70 * s), w, (int)(38 * s));
            _visualColorSwatch.SetBounds(x, (int)(118 * s), (int)(28 * s), (int)(28 * s));
            _visualColorNameLabel.SetBounds((int)(52 * s), (int)(120 * s), (int)(64 * s), (int)(24 * s));
            c["intensityLabel"].SetBounds((int)(124 * s), (int)(120 * s), (int)(52 * s), (int)(24 * s));
            _visualIntensityStep.SetBounds((int)(180 * s), (int)(118 * s), (int)(108 * s), (int)(34 * s));
            c["intensityUnit"].SetBounds((int)(294 * s), (int)(120 * s), (int)(24 * s), (int)(24 * s));
            c["durationLabel"].SetBounds(x, (int)(158 * s), (int)(52 * s), (int)(24 * s));
            _visualDurationStep.SetBounds((int)(72 * s), (int)(156 * s), (int)(108 * s), (int)(34 * s));
            c["durationUnit"].SetBounds((int)(186 * s), (int)(158 * s), (int)(28 * s), (int)(24 * s));
            c["scaleLabel"].SetBounds((int)(226 * s), (int)(158 * s), (int)(52 * s), (int)(24 * s));
            _visualScaleStep.SetBounds((int)(282 * s), (int)(156 * s), (int)(108 * s), (int)(34 * s));
            c["scaleUnit"].SetBounds((int)(396 * s), (int)(158 * s), (int)(24 * s), (int)(24 * s));

            // 音效反馈分区
            c["soundTitle"].SetBounds(x, (int)(202 * s), w - (int)(60 * s), (int)(20 * s));
            _soundToggle.SetBounds(_feedbackSection.Width - (int)(62 * s), (int)(200 * s), (int)(46 * s), (int)(26 * s));
            _soundSelect.SetBounds(x, (int)(228 * s), w, (int)(38 * s));
            c["volumeLabel"].SetBounds(x, (int)(276 * s), (int)(52 * s), (int)(24 * s));
            _soundVolumeStep.SetBounds((int)(72 * s), (int)(274 * s), (int)(108 * s), (int)(34 * s));
            c["volumeUnit"].SetBounds((int)(186 * s), (int)(276 * s), (int)(24 * s), (int)(24 * s));
            _chooseSound.SetBounds((int)(220 * s), (int)(274 * s), (int)(108 * s), (int)(34 * s));
            _testSound.SetBounds((int)(336 * s), (int)(274 * s), (int)(84 * s), (int)(34 * s));
            _soundFileLabel.SetBounds(x, (int)(314 * s), w, (int)(24 * s));

            // 震动反馈分区
            c["rumbleTitle"].SetBounds(x, (int)(340 * s), w - (int)(60 * s), (int)(20 * s));
            _rumbleToggle.SetBounds(_feedbackSection.Width - (int)(62 * s), (int)(338 * s), (int)(46 * s), (int)(26 * s));
            _rumbleSelect.SetBounds(x, (int)(366 * s), w, (int)(38 * s));

            int rgap = (int)(10 * s);
            int rcolW = (w - rgap * 2) / 3;
            int col2 = x + rcolW + rgap;
            int col3 = x + (rcolW + rgap) * 2;
            c["rleftLabel"].SetBounds(x, (int)(414 * s), rcolW, (int)(18 * s));
            c["rrightLabel"].SetBounds(col2, (int)(414 * s), rcolW, (int)(18 * s));
            c["rdurationLabel"].SetBounds(col3, (int)(414 * s), rcolW, (int)(18 * s));
            _rumbleLeftStep.SetBounds(x, (int)(434 * s), rcolW - (int)(26 * s), (int)(34 * s));
            c["rleftUnit"].SetBounds(x + rcolW - (int)(24 * s), (int)(436 * s), (int)(24 * s), (int)(24 * s));
            _rumbleRightStep.SetBounds(col2, (int)(434 * s), rcolW - (int)(26 * s), (int)(34 * s));
            c["rrightUnit"].SetBounds(col2 + rcolW - (int)(24 * s), (int)(436 * s), (int)(24 * s), (int)(24 * s));
            _rumbleDurationStep.SetBounds(col3, (int)(434 * s), rcolW - (int)(28 * s), (int)(34 * s));
            c["rdurationUnit"].SetBounds(col3 + rcolW - (int)(26 * s), (int)(436 * s), (int)(28 * s), (int)(24 * s));

            c["rumblePatternTitle"].SetBounds(x, (int)(478 * s), w, (int)(18 * s));
            _rumbleTestButton.SetBounds(x, (int)(502 * s), (int)(84 * s), (int)(34 * s));
            _rumblePatternBox.SetBounds(x + (int)(96 * s), (int)(502 * s), w - (int)(96 * s), (int)(34 * s));
            c["rumblePatternHint"].SetBounds(x, (int)(542 * s), w, (int)(18 * s));
        }

        private void LayoutFollowSection()
        {
            if (_followSelect == null) return;
            float s = Theme.Scale;
            int x = (int)(16 * s);
            int w = Math.Max(80, _followSection.Width - (int)(32 * s));
            _followToggle.SetBounds(_followSection.Width - (int)(104 * s), (int)(10 * s), (int)(46 * s), (int)(26 * s));
            _followLabel.SetBounds(x, (int)(52 * s), w, (int)(18 * s));
            _followSelect.SetBounds(x, (int)(72 * s), w, (int)(40 * s));
            _followHelp.SetBounds(x, (int)(120 * s), w, (int)(44 * s));
        }

        private void LayoutOverlaySection()
        {
            if (_overlaySelect == null) return;
            float s = Theme.Scale;
            int x = (int)(16 * s);
            int w = Math.Max(80, _overlaySection.Width - (int)(32 * s));
            _overlayToggle.SetBounds(_overlaySection.Width - (int)(104 * s), (int)(10 * s), (int)(46 * s), (int)(26 * s));
            _overlaySection.Controls["overlayLabel"].SetBounds(x, (int)(52 * s), w, (int)(18 * s));
            _overlaySelect.SetBounds(x, (int)(72 * s), w, (int)(40 * s));
            _overlaySection.Controls["overlayHideLabel"].SetBounds(x, (int)(122 * s), (int)(120 * s), (int)(34 * s));
            _overlayHideStep.SetBounds(_overlaySection.Width - (int)(156 * s), (int)(125 * s), (int)(112 * s), (int)(36 * s));
            _overlayHideHint.SetBounds(_overlaySection.Width - (int)(36 * s), (int)(126 * s), (int)(24 * s), (int)(34 * s));
            _overlayHelp.SetBounds(x, (int)(172 * s), w, (int)(48 * s));
        }

        private void LayoutOverlayModeSection()
        {
            if (_overlayEnabledToggle == null) return;
            float s = Theme.Scale;
            int x = (int)(16 * s);
            int w = Math.Max(80, _overlayModeSection.Width - (int)(32 * s));
            _overlayEnabledToggle.SetBounds(_overlayModeSection.Width - (int)(62 * s), (int)(50 * s), (int)(46 * s), (int)(26 * s));
            _overlayModeSection.Controls["enabledLabel"].SetBounds(x, (int)(52 * s), w, (int)(18 * s));
            _soloToggle.SetBounds(_overlayModeSection.Width - (int)(62 * s), (int)(92 * s), (int)(46 * s), (int)(26 * s));
            _overlayModeSection.Controls["soloLabel"].SetBounds(x, (int)(94 * s), w, (int)(18 * s));
            _soloHelp.SetBounds(x, (int)(122 * s), w, (int)(32 * s));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Theme.SetupGraphics(e.Graphics);
            float s = Theme.Scale;
            int x = (int)(20 * s);
            Theme.FillRounded(e.Graphics, new RectangleF(x, 16 * s, 3 * s, 30 * s), 1.5f * s, Theme.Accent);
            string panelTitle = _padSide ? "虚拟手柄" : "虚拟按键";
            string unit = _padSide ? "个手柄键" : "个按键";
            Theme.DrawText(e.Graphics, panelTitle, Theme.Semibold(12f), Theme.Text,
                new RectangleF(x + 12 * s, 8 * s, Width - x - 150 * s, 30 * s), StringAlignment.Near, StringAlignment.Center, true);
            string sub;
            int keyCount = CurrentKeys == null ? 0 : CurrentKeys.Count;
            if (_mode != null)
                sub = _mode.Name + "  ·  " + keyCount + " " + unit;
            else if (_overlayMode != null)
                sub = "叠加 · " + _overlayMode.Name + "  ·  " + keyCount + " " + unit;
            else
                sub = "未选择模式";
            Theme.DrawText(e.Graphics, sub, Theme.UiFont(8.1f, false), Theme.Muted,
                new RectangleF(x + 12 * s, 32 * s, Width - x - 150 * s, 20 * s), StringAlignment.Near, StringAlignment.Center, true);

            if (CurrentKeys != null && CurrentKeys.Count > 0)
            {
                using (Pen pen = new Pen(Theme.StrokeSoft, 1f))
                {
                    e.Graphics.DrawLine(pen, x, _dividerY, Width - x, _dividerY);
                }
            }
        }
    }

    public class KeyChip : Control
    {
        private VirtualKey _key;
        private int _index;
        private bool _selected;
        private bool _hover;
        private bool _pressed;
        private Image _image;

        public KeyChip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            Font = Theme.UiFont(8.2f, false);
        }

        public VirtualKey Key
        {
            get { return _key; }
        }

        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        public void SetKey(VirtualKey key, int index)
        {
            _key = key;
            _index = index;
            RefreshKey();
        }

        public void RefreshKey()
        {
            if (_image != null)
            {
                _image.Dispose();
                _image = null;
            }
            if (_key != null && !string.IsNullOrEmpty(_key.ImageFile))
            {
                _image = ConfigStore.LoadImageCopy(_key.ImageFile);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            float s = Theme.Scale;
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Color accent = Theme.AccentFor(_index);
            Color fill = _selected ? Theme.Alpha(accent, 28) : (_hover ? Theme.SurfaceHi : Theme.SurfaceAlt);
            Theme.FillRounded(e.Graphics, rect, 10f * s, fill);
            Theme.DrawRounded(e.Graphics, rect, 10f * s, _selected ? Theme.Alpha(accent, 210) : Theme.StrokeSoft, _selected ? 1.4f : 1f);

            RectangleF thumb = new RectangleF(8 * s, 9 * s, 28 * s, 28 * s);
            Theme.FillRounded(e.Graphics, thumb, 8f * s, Theme.Alpha(accent, 45));
            if (_image != null)
            {
                Theme.DrawImageContained(e.Graphics, _image, new RectangleF(thumb.X + 4 * s, thumb.Y + 4 * s,
                    thumb.Width - 8 * s, thumb.Height - 8 * s));
            }
            else
            {
                string initial = _key == null || string.IsNullOrEmpty(_key.Name) ? "?" : _key.Name.Substring(0, 1);
                Theme.DrawText(e.Graphics, initial, Theme.Semibold(9f), Theme.Blend(accent, Color.White, 0.45f),
                    thumb, StringAlignment.Center, StringAlignment.Center, false);
            }

            string name = _key == null || string.IsNullOrEmpty(_key.Name) ? "未命名" : _key.Name;
            Theme.DrawText(e.Graphics, name, Theme.Semibold(8.1f), Theme.Text,
                new RectangleF(42 * s, 7 * s, Width - 48 * s, 18 * s), StringAlignment.Near, StringAlignment.Center, true);
            string binding = _key == null || _key.ShortPress == null ? "未绑定" : _key.ShortPress.DisplayName;
            Theme.DrawText(e.Graphics, binding, Theme.UiFont(6.7f, false), Theme.Muted,
                new RectangleF(42 * s, 25 * s, Width - 48 * s, 15 * s), StringAlignment.Near, StringAlignment.Center, true);
            if (_pressed)
            {
                Theme.FillRounded(e.Graphics, rect, 10f * s, Color.FromArgb(42, 255, 255, 255));
                Theme.DrawRounded(e.Graphics, new RectangleF(2, 2, Width - 4, Height - 4), 9f * s,
                    Color.FromArgb(85, 255, 255, 255), 1.2f);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _image != null)
            {
                _image.Dispose();
                _image = null;
            }
            base.Dispose(disposing);
        }
    }

    // 右侧摇杆条目卡片：左 / 右摇杆不属于按键列表，单独展示启用状态，点击联动左侧摇杆设置面板。
    public class StickChip : Control
    {
        private readonly int _kind;
        private readonly string _title;
        private readonly bool _enabled;
        private bool _selected;
        private bool _hover;
        private bool _suppressClick;
        private RectangleF _hideRect;

        public int Kind { get { return _kind; } }
        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        public event Action<StickChip> HideClick;

        public StickChip(int kind, string title, bool enabled, bool selected)
        {
            _kind = kind;
            _title = title;
            _enabled = enabled;
            _selected = selected;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            Font = Theme.UiFont(8.2f, false);
        }

        private RectangleF HideButtonRect(float s)
        {
            return new RectangleF(Width - 20 * s, 6 * s, 16 * s, 16 * s);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            float s = Theme.Scale;
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Color accent = _kind == 0 ? Theme.Accent : (_kind == 1 ? Theme.Mint : Theme.Violet);
            Color fill = (_hover || _selected) ? Theme.SurfaceHi : Theme.SurfaceAlt;
            Theme.FillRounded(e.Graphics, rect, 10f * s, fill);
            Theme.DrawRounded(e.Graphics, rect, 10f * s,
                _selected ? accent : (_enabled ? Theme.Alpha(accent, 200) : Theme.StrokeSoft),
                _selected ? 2.2f : (_enabled ? 1.4f : 1f));

            RectangleF thumb = new RectangleF(8 * s, 9 * s, 28 * s, 28 * s);
            Theme.FillRounded(e.Graphics, thumb, 8f * s, Theme.Alpha(accent, _enabled ? 45 : 18));
            Theme.FillCircle(e.Graphics,
                new RectangleF(thumb.X + thumb.Width / 2f - 4 * s, thumb.Y + thumb.Height / 2f - 4 * s,
                    8 * s, 8 * s), _enabled ? accent : Theme.Faint);

            _hideRect = HideButtonRect(s);
            // 右上角小按钮：启用态为 ×（隐藏），隐藏态为 ↺（恢复）。
            Color btnColor = _enabled ? Theme.Muted : Theme.Mint;
            Theme.FillRounded(e.Graphics, _hideRect, 6f * s, Theme.SurfaceHi);
            float cx = _hideRect.X + _hideRect.Width / 2f;
            float cy = _hideRect.Y + _hideRect.Height / 2f;
            float r = 3.4f * s;
            using (Pen pen = new Pen(btnColor, 1.1f * s))
            {
                if (_enabled)
                {
                    e.Graphics.DrawLine(pen, cx - r, cy - r, cx + r, cy + r);
                    e.Graphics.DrawLine(pen, cx + r, cy - r, cx - r, cy + r);
                }
                else
                {
                    e.Graphics.DrawEllipse(pen, cx - r, cy - r, r * 2f, r * 2f);
                    e.Graphics.DrawLine(pen, cx, cy - r, cx, cy - r - 2.4f * s);
                }
            }

            Theme.DrawText(e.Graphics, _title, Theme.Semibold(8.1f), Theme.Text,
                new RectangleF(42 * s, 7 * s, Width - 62 * s, 18 * s),
                StringAlignment.Near, StringAlignment.Center, true);
            Theme.DrawText(e.Graphics, _enabled ? "已启用" : "已隐藏",
                Theme.UiFont(6.7f, false), _enabled ? Theme.Muted : Theme.Faint,
                new RectangleF(42 * s, 25 * s, Width - 62 * s, 15 * s),
                StringAlignment.Near, StringAlignment.Center, true);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_hideRect.Contains(e.Location))
            {
                _suppressClick = true;
                Action<StickChip> h = HideClick;
                if (h != null) h(this);
                return;
            }
            base.OnMouseDown(e);
        }

        protected override void OnClick(EventArgs e)
        {
            if (_suppressClick)
            {
                _suppressClick = false;
                return;
            }
            base.OnClick(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }
    }

    public class ImagePreviewBox : Control
    {
        private Image _image;

        public ImagePreviewBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.SurfaceAlt;
        }

        public void SetImage(Image image)
        {
            if (_image != null) _image.Dispose();
            _image = image;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (GraphicsPath clip = Theme.RoundedPath(rect, 10f))
            {
                GraphicsState state = e.Graphics.Save();
                e.Graphics.SetClip(clip);
                int cell = Math.Max(6, Width / 8);
                for (int y = 0; y < Height; y += cell)
                {
                    for (int x = 0; x < Width; x += cell)
                    {
                        bool light = ((x / cell) + (y / cell)) % 2 == 0;
                        using (SolidBrush b = new SolidBrush(light ? Color.FromArgb(32, 44, 64) : Color.FromArgb(24, 34, 50)))
                        {
                            e.Graphics.FillRectangle(b, x, y, cell, cell);
                        }
                    }
                }
                if (_image != null)
                {
                    Theme.DrawImageContained(e.Graphics, _image, new RectangleF(5, 5, Width - 10, Height - 10));
                }
                else
                {
                    Theme.DrawText(e.Graphics, "PNG", Theme.Semibold(8f), Theme.Faint,
                        rect, StringAlignment.Center, StringAlignment.Center, false);
                }
                e.Graphics.Restore(state);
            }
            Theme.DrawRounded(e.Graphics, rect, 10f, Theme.Stroke, 1f);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _image != null)
            {
                _image.Dispose();
                _image = null;
            }
            base.Dispose(disposing);
        }
    }

    public class EmptyStateView : Control
    {
        private string _titleText = "当前模式还没有手柄按键";
        private string _subText = "点击右上角「新增手柄」创建第一个控件";

        public EmptyStateView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Surface;
        }

        public void SetTexts(string title, string sub)
        {
            _titleText = title == null ? "" : title;
            _subText = sub == null ? "" : sub;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.SetupGraphics(e.Graphics);
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (Pen pen = new Pen(Theme.StrokeSoft, 1f))
            {
                pen.DashStyle = DashStyle.Dash;
                pen.DashPattern = new float[] { 5f, 5f };
                using (GraphicsPath path = Theme.RoundedPath(rect, 16f))
                {
                    e.Graphics.DrawPath(pen, path);
                }
            }
            float cx = Width / 2f;
            float cy = Height / 2f - 30f;
            using (SolidBrush b = new SolidBrush(Theme.Alpha(Theme.Accent, 24)))
            {
                e.Graphics.FillEllipse(b, cx - 30, cy - 30, 60, 60);
            }
            Theme.DrawCircle(e.Graphics, new RectangleF(cx - 20, cy - 20, 40, 40), Theme.Alpha(Theme.Accent, 160), 1.5f);
            Theme.DrawText(e.Graphics, "＋", Theme.UiFont(17f, false), Theme.Accent,
                new RectangleF(cx - 20, cy - 20, 40, 40), StringAlignment.Center, StringAlignment.Center, false);
            Theme.DrawText(e.Graphics, _titleText, Theme.Semibold(10.5f), Theme.Text,
                new RectangleF(20, cy + 48, Width - 40, 26), StringAlignment.Center, StringAlignment.Center, true);
            Theme.DrawText(e.Graphics, _subText, Theme.UiFont(8.4f, false), Theme.Muted,
                new RectangleF(20, cy + 74, Width - 40, 24), StringAlignment.Center, StringAlignment.Center, true);
        }
    }
}
