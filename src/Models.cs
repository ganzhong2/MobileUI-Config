// ============================================================================
// Models.cs —— 配置模型层（整个程序的数据核心）
// ----------------------------------------------------------------------------
// 本文件定义程序持久化到 MobileUIConfig.xml 的全部数据结构：
//   · 枚举：横竖屏、绑定类型、手柄厂商布局、视角形状等；
//   · 绑定描述：KeyBinding（键盘 / 鼠标按键 / 滚轮 / 虚拟手柄按键）；
//   · 按键与模式：VirtualKey（单个虚拟按键）、ModeConfig（一个模式的整套布局）；
//   · 子配置：摇杆、方向键、视角操控、震动、图片识别、内存扫描、剧情跳过、
//     系统显示键 / 模式键、叠加层模式等；
//   · 根配置 AppConfig：串起项目名、进程名、全部模式与全局开关；
//   · 持久化：ConfigStore 负责 XML 读写、默认值补齐、损坏配置备份恢复。
//
// 约定：所有字段均为 public 字段（供 XmlSerializer 直接序列化）；克隆方法
// Clone() 用于模式 / 叠加层之间复制配置；Normalize() 在反序列化后补齐缺失字段、
// 夹紧越界数值，保证旧版本配置升级后仍能正常加载。
// 本文件不包含任何界面绘制与输入注入逻辑，仅承载数据与默认值。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace MobileUiConfig
{
    /// <summary>手机预览与悬浮层的横竖屏方向。</summary>
    public enum DisplayOrientation
    {
        Landscape = 0,
        Portrait = 1
    }

    /// <summary>绑定类型：决定 Code 字段按键盘虚拟键码、鼠标按键、滚轮还是手柄按钮位码解释。</summary>
    public enum BindingKind
    {
        None = 0,
        Keyboard = 1,
        MouseButton = 2,
        MouseWheel = 3,
        // 虚拟手柄（ViGEm Xbox360）：Code 为 XInput 按钮位掩码；
        // Modifiers=1 左扳机(LT)、2 右扳机(RT)；摇杆轴不经过按键绑定，由摇杆控件直接上报。
        Gamepad = 4
    }

    // 虚拟手柄厂商布局：0=微软 Xbox，1=索尼 DualShock，2=任天堂 Switch Pro；
    // 底层统一映射 Xbox360 XInput 位码，仅预览符号与位置按厂商风格绘制。
    public enum PadLayout
    {
        Xbox = 0,
        Sony = 1,
        Nintendo = 2
    }

    /// <summary>视角操控区域的外形：圆形 / 方形 / 长方形。</summary>
    public enum LookShape
    {
        Circle = 0,
        Square = 1,
        Rectangle = 2
    }

    /// <summary>
    /// 一条按键绑定描述。同时用于短按、长按、双击三套绑定，
    /// 由 <see cref="BindingKind"/> 区分物理来源，Code 为对应码值，Modifiers 记录组合键 / 扳机位。
    /// </summary>
    [XmlType("Binding")]
    public class KeyBinding
    {
        public BindingKind Kind;
        public int Code;
        public int Modifiers;

        public KeyBinding()
        {
            Kind = BindingKind.None;
        }

        public KeyBinding(BindingKind kind, int code)
        {
            Kind = kind;
            Code = code;
        }

        public KeyBinding(BindingKind kind, int code, int modifiers)
        {
            Kind = kind;
            Code = code;
            Modifiers = modifiers;
        }

        [XmlIgnore]
        public bool IsEmpty
        {
            get { return Kind == BindingKind.None; }
        }

        public KeyBinding Clone()
        {
            KeyBinding b = new KeyBinding();
            b.Kind = Kind;
            b.Code = Code;
            b.Modifiers = Modifiers;
            return b;
        }

        public string DisplayName
        {
            get
            {
                if (Kind == BindingKind.None) return "未绑定";
                if (Kind == BindingKind.Keyboard)
                {
                    Keys key = (Keys)Code;
                    string name = KeyNames.GetKeyName(key);
                    string prefix = "";
                    if ((Modifiers & (int)Keys.Control) != 0) prefix += "Ctrl + ";
                    if ((Modifiers & (int)Keys.Shift) != 0) prefix += "Shift + ";
                    if ((Modifiers & (int)Keys.Alt) != 0) prefix += "Alt + ";
                    return prefix + name;
                }
                if (Kind == BindingKind.MouseButton)
                {
                    if (Code == 0) return "鼠标左键";
                    if (Code == 1) return "鼠标右键";
                    if (Code == 2) return "鼠标中键";
                    return "鼠标键 " + Code.ToString(CultureInfo.InvariantCulture);
                }
                if (Kind == BindingKind.MouseWheel)
                {
                    return Code > 0 ? "滚轮向上" : "滚轮向下";
                }
                if (Kind == BindingKind.Gamepad)
                {
                    if (Modifiers == 1) return "LT";
                    if (Modifiers == 2) return "RT";
                    return PadButtons.UniversalName(Code);
                }
                return "未绑定";
            }
        }
    }

    public static class KeyNames
    {
        public static string GetKeyName(Keys key)
        {
            if (key == Keys.None) return "未绑定";
            if (key == Keys.Space) return "Space";
            if (key == Keys.Enter) return "Enter";
            if (key == Keys.Escape) return "Esc";
            if (key == Keys.Back) return "Backspace";
            if (key == Keys.Tab) return "Tab";
            if (key == Keys.Delete) return "Delete";
            if (key == Keys.Insert) return "Insert";
            if (key == Keys.Home) return "Home";
            if (key == Keys.End) return "End";
            if (key == Keys.PageUp) return "PageUp";
            if (key == Keys.PageDown) return "PageDown";
            if (key == Keys.Up) return "↑";
            if (key == Keys.Down) return "↓";
            if (key == Keys.Left) return "←";
            if (key == Keys.Right) return "→";
            if (key == Keys.ControlKey) return "Ctrl";
            if (key == Keys.ShiftKey) return "Shift";
            if (key == Keys.Menu) return "Alt";
            if (key == Keys.LWin || key == Keys.RWin) return "Win";
            if (key >= Keys.D0 && key <= Keys.D9)
                return ((int)key - (int)Keys.D0).ToString(CultureInfo.InvariantCulture);
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
                return "Num" + ((int)key - (int)Keys.NumPad0).ToString(CultureInfo.InvariantCulture);
            if (key >= Keys.F1 && key <= Keys.F24)
                return "F" + ((int)key - (int)Keys.F1 + 1).ToString(CultureInfo.InvariantCulture);
            if ((int)key == 0xBC) return ",";
            if (key == Keys.OemPeriod) return ".";
            if (key == Keys.OemQuestion) return "/";
            if (key == Keys.OemSemicolon) return ";";
            if (key == Keys.OemQuotes) return "'";
            if (key == Keys.OemOpenBrackets) return "[";
            if (key == Keys.OemCloseBrackets) return "]";
            if (key == Keys.OemPipe) return "\\";
            if (key == Keys.OemMinus) return "-";
            if ((int)key == 0xBB) return "=";
            if ((int)key == 0xC0) return "`";
            return key.ToString();
        }
    }

    // ViGEm Xbox360 虚拟手柄按钮位码（wButtons 位掩码）。三套厂商布局底层统一用这套位码，
    // 仅预览符号与位置按 PadLayout 风格翻译。Modifiers=1 左扳机 LT、2 右扳机 RT。
    public static class PadButtons
    {
        public const ushort DPadUp = 0x0001;
        public const ushort DPadDown = 0x0002;
        public const ushort DPadLeft = 0x0004;
        public const ushort DPadRight = 0x0008;
        public const ushort Start = 0x0010;
        public const ushort Back = 0x0020;
        public const ushort LeftThumb = 0x0040;
        public const ushort RightThumb = 0x0080;
        public const ushort LeftShoulder = 0x0100;
        public const ushort RightShoulder = 0x0200;
        public const ushort A = 0x1000;
        public const ushort B = 0x2000;
        public const ushort X = 0x4000;
        public const ushort Y = 0x8000;

        public const int ModifierLeftTrigger = 1;
        public const int ModifierRightTrigger = 2;

        public static string UniversalName(int code)
        {
            ushort mask = (ushort)code;
            switch (mask)
            {
                case DPadUp: return "十字键 ↑";
                case DPadDown: return "十字键 ↓";
                case DPadLeft: return "十字键 ←";
                case DPadRight: return "十字键 →";
                case Start: return "菜单";
                case Back: return "返回";
                case LeftThumb: return "左摇杆按";
                case RightThumb: return "右摇杆按";
                case LeftShoulder: return "肩键左";
                case RightShoulder: return "肩键右";
                case A: return "A";
                case B: return "B";
                case X: return "X";
                case Y: return "Y";
                default: return "手柄键 " + mask.ToString("X4");
            }
        }

        // 按厂商布局把同一 Xbox 位码翻译成界面符号；扳机由调用方按 Modifier 处理。
        public static string VendorSymbol(int layout, int code)
        {
            ushort mask = (ushort)code;
            if (layout == (int)PadLayout.Sony)
            {
                switch (mask)
                {
                    case A: return "×";
                    case B: return "○";
                    case X: return "□";
                    case Y: return "△";
                    case LeftShoulder: return "L1";
                    case RightShoulder: return "R1";
                    case Start: return "选项";
                    case Back: return "分享";
                    case LeftThumb: return "L3";
                    case RightThumb: return "R3";
                    case DPadUp: return "↑";
                    case DPadDown: return "↓";
                    case DPadLeft: return "←";
                    case DPadRight: return "→";
                    default: return UniversalName(code);
                }
            }
            if (layout == (int)PadLayout.Nintendo)
            {
                switch (mask)
                {
                    case LeftShoulder: return "L";
                    case RightShoulder: return "R";
                    case Start: return "+";
                    case Back: return "-";
                    case LeftThumb: return "LS";
                    case RightThumb: return "RS";
                    case DPadUp: return "↑";
                    case DPadDown: return "↓";
                    case DPadLeft: return "←";
                    case DPadRight: return "→";
                    default: return UniversalName(code);
                }
            }
            // Xbox 默认
            if (mask == LeftShoulder) return "LB";
            if (mask == RightShoulder) return "RB";
            if (mask == LeftThumb) return "LS";
            if (mask == RightThumb) return "RS";
            return UniversalName(code);
        }

        // 扳机符号按厂商翻译。
        public static string VendorTrigger(int layout, int modifiers)
        {
            if (modifiers != ModifierLeftTrigger && modifiers != ModifierRightTrigger) return "";
            bool left = modifiers == ModifierLeftTrigger;
            if (layout == (int)PadLayout.Sony) return left ? "L2" : "R2";
            if (layout == (int)PadLayout.Nintendo) return left ? "ZL" : "ZR";
            return left ? "LT" : "RT";
        }

        // 绑定文字按厂商布局翻译；非手柄绑定回退通用显示名。
        public static string BindingLabel(KeyBinding b, int layout)
        {
            if (b == null || b.IsEmpty) return "";
            if (b.Kind == BindingKind.Gamepad)
            {
                if (b.Modifiers == ModifierLeftTrigger || b.Modifiers == ModifierRightTrigger)
                    return VendorTrigger(layout, b.Modifiers);
                return VendorSymbol(layout, b.Code);
            }
            return b.DisplayName;
        }
    }

    /// <summary>
    /// 单个虚拟按键：位置 / 尺寸 / 不透明度 / 图标等外观，以及短按、长按、双击三套独立绑定。
    /// 一个模式（<see cref="ModeConfig"/>）由若干 VirtualKey 组成一套悬浮布局。
    /// </summary>
    [XmlType("VirtualKey")]
    public class VirtualKey
    {
        [XmlAttribute]
        public string Id = "";
        public string Name = "";
        public KeyBinding ShortPress = new KeyBinding();
        public bool LongPressEnabled = false;
        public KeyBinding LongPress = new KeyBinding();
        public int LongPressMs = 2000;
        // 双击触发：300ms 内连续两次抬起触发独立绑定；与短按、长按互斥，各自独立保存。
        public bool DoublePressEnabled = false;
        public KeyBinding DoublePress = new KeyBinding();
        public int Size = 152;
        public bool HideWithHidden = true;
        public int Opacity = 100;
        public bool FollowControlEnabled = false;
        public string FollowControlId = "";
        public string ImageFile = "";
        public float X = 0.5f;
        public float Y = 0.5f;

        // 叠加层呼出：开启后触摸此虚拟键唤出选中的叠加模式按键层；
        // ShortPress 留空即纯触摸呼出，不向游戏发送实体按键。
        public bool OverlayEnabled = false;
        public int OverlayTargetMode = 0;      // 呼出的叠加模式索引
        public int OverlayAutoHideSeconds = 0; // 无操作 N 秒后隐藏叠加层，0 = 常驻

        // 多模态触控反馈（按键级，每个虚拟键独立一套配置）。
        // 视觉反馈：0=按下缩放，1=高亮闪白，2=边框发光，3=颜色脉冲，4=不透明度脉冲。
        public bool VisualFeedbackEnabled = false;
        public int VisualPreset = 1;
        public string VisualColor = "#FFFFFF";
        public int VisualIntensity = 70;       // 0～100 强度/透明度
        public int VisualDurationMs = 180;      // 脉冲/缩放回弹时长 100～1000 ms
        public int VisualScalePercent = 90;     // 按下缩放比例 80～100（缩放预设用）

        // 音效反馈：0=轻咔哒，1=确认音，2=错误音，3=长音，4=自定义音效文件。
        public bool SoundEnabled = false;
        public int SoundPreset = 0;
        public string SoundFile = "";
        public int SoundVolume = 80;            // 0～100

        // 震动反馈（按键级独立一套；未开启时回落全局震动配置）。
        // 节奏：0=单次脉冲，1=心跳，2=提醒·三短，3=冲击·渐弱，4=连发，5=自定义节奏。
        public bool RumbleEnabled = false;
        public int RumblePreset = 0;
        public string RumblePattern = "";
        public int RumbleLeftMotor = 60;        // 左（低频）马达 0～100
        public int RumbleRightMotor = 60;       // 右（高频）马达 0～100
        public int RumbleDurationMs = 150;     // 单次脉冲时长 50～3000 ms

        // Optional per-key cooldown memory monitor.
        public bool CooldownScanEnabled = false;
        public string CooldownBaseAddress = "";
        public string CooldownOffsets = "";
        public int CooldownIntervalMs = 200;
        public int CooldownMaxSeconds = 60;
        [XmlIgnore] public double CooldownRemaining = 0;
        [XmlIgnore] public bool CooldownValid = false;

        public VirtualKey()
        {
        }

        public VirtualKey(string name, KeyBinding binding, float x, float y)
        {
            Id = Guid.NewGuid().ToString("N");
            Name = name;
            ShortPress = binding;
            X = x;
            Y = y;
        }

        // 深拷贝按键配置；新按键生成全新 Id，位置、外观、绑定、反馈配置一并复制。
        public VirtualKey Clone()
        {
            VirtualKey k = new VirtualKey();
            k.Id = Guid.NewGuid().ToString("N");
            k.Name = Name;
            k.ShortPress = ShortPress == null ? new KeyBinding() : ShortPress.Clone();
            k.LongPressEnabled = LongPressEnabled;
            k.LongPress = LongPress == null ? new KeyBinding() : LongPress.Clone();
            k.LongPressMs = LongPressMs;
            k.DoublePressEnabled = DoublePressEnabled;
            k.DoublePress = DoublePress == null ? new KeyBinding() : DoublePress.Clone();
            k.Size = Size;
            k.HideWithHidden = HideWithHidden;
            k.Opacity = Opacity;
            k.FollowControlEnabled = FollowControlEnabled;
            k.FollowControlId = FollowControlId;
            k.ImageFile = ImageFile ?? "";
            k.X = X;
            k.Y = Y;
            k.OverlayEnabled = OverlayEnabled;
            k.OverlayTargetMode = OverlayTargetMode;
            k.OverlayAutoHideSeconds = OverlayAutoHideSeconds;
            k.VisualFeedbackEnabled = VisualFeedbackEnabled;
            k.VisualPreset = VisualPreset;
            k.VisualColor = VisualColor;
            k.VisualIntensity = VisualIntensity;
            k.VisualDurationMs = VisualDurationMs;
            k.VisualScalePercent = VisualScalePercent;
            k.SoundEnabled = SoundEnabled;
            k.SoundPreset = SoundPreset;
            k.SoundFile = SoundFile;
            k.SoundVolume = SoundVolume;
            k.RumbleEnabled = RumbleEnabled;
            k.RumblePreset = RumblePreset;
            k.RumblePattern = RumblePattern;
            k.RumbleLeftMotor = RumbleLeftMotor;
            k.RumbleRightMotor = RumbleRightMotor;
            k.RumbleDurationMs = RumbleDurationMs;
            k.CooldownScanEnabled = CooldownScanEnabled;
            k.CooldownBaseAddress = CooldownBaseAddress;
            k.CooldownOffsets = CooldownOffsets;
            k.CooldownIntervalMs = CooldownIntervalMs;
            k.CooldownMaxSeconds = CooldownMaxSeconds;
            return k;
        }

        public void Normalize(int index)
        {
            if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            if (Name == null) Name = "";
            Name = Name.Trim();
            if (Name.Length == 0) Name = "按键" + (index + 1).ToString(CultureInfo.InvariantCulture);
            if (ShortPress == null) ShortPress = new KeyBinding();
            if (LongPress == null) LongPress = new KeyBinding();
            if (DoublePress == null) DoublePress = new KeyBinding();
            if (Size < 40) Size = 40;
            if (Size > 360) Size = 360;
            if (LongPressMs < 200) LongPressMs = 200;
            if (LongPressMs > 3000) LongPressMs = 3000;
            if (Opacity < 10) Opacity = 10;
            if (Opacity > 100) Opacity = 100;
            if (FollowControlId == null) FollowControlId = "";
            FollowControlId = FollowControlId.Trim();
            if (FollowControlEnabled && FollowControlId.Length == 0) FollowControlId = "joystick";
            if (float.IsNaN(X) || X < 0f) X = 0.5f;
            if (float.IsNaN(Y) || Y < 0f) Y = 0.5f;
            if (X > 1f) X = 1f;
            if (Y > 1f) Y = 1f;
            if (ImageFile == null) ImageFile = "";
            if (CooldownBaseAddress == null) CooldownBaseAddress = "";
            if (CooldownOffsets == null) CooldownOffsets = "";
            CooldownBaseAddress = CooldownBaseAddress.Trim();
            CooldownOffsets = CooldownOffsets.Trim();
            if (CooldownIntervalMs < 50) CooldownIntervalMs = 50;
            if (CooldownIntervalMs > 1000) CooldownIntervalMs = 1000;
            if (CooldownMaxSeconds < 1) CooldownMaxSeconds = 60;
            if (CooldownMaxSeconds > 300) CooldownMaxSeconds = 300;
            if (OverlayTargetMode < 0) OverlayTargetMode = 0;
            if (OverlayAutoHideSeconds < 0) OverlayAutoHideSeconds = 0;
            if (OverlayAutoHideSeconds > 60) OverlayAutoHideSeconds = 60;
            // 多模态反馈区间校验。
            if (VisualPreset < 0) VisualPreset = 0;
            if (VisualPreset > 4) VisualPreset = 4;
            if (VisualColor == null) VisualColor = "#FFFFFF";
            VisualColor = VisualColor.Trim();
            if (VisualIntensity < 0) VisualIntensity = 0;
            if (VisualIntensity > 100) VisualIntensity = 100;
            if (VisualDurationMs < 100) VisualDurationMs = 100;
            if (VisualDurationMs > 1000) VisualDurationMs = 1000;
            if (VisualScalePercent < 80) VisualScalePercent = 80;
            if (VisualScalePercent > 100) VisualScalePercent = 100;
            if (SoundPreset < 0) SoundPreset = 0;
            if (SoundPreset > 4) SoundPreset = 4;
            if (SoundFile == null) SoundFile = "";
            SoundFile = SoundFile.Trim();
            if (SoundVolume < 0) SoundVolume = 0;
            if (SoundVolume > 100) SoundVolume = 100;
            // 按键级震动区间校验。
            if (RumblePreset < 0) RumblePreset = 0;
            if (RumblePreset > 5) RumblePreset = 5;
            if (RumblePattern == null) RumblePattern = "";
            RumblePattern = RumblePattern.Trim();
            if (RumbleLeftMotor < 0) RumbleLeftMotor = 0;
            if (RumbleLeftMotor > 100) RumbleLeftMotor = 100;
            if (RumbleRightMotor < 0) RumbleRightMotor = 0;
            if (RumbleRightMotor > 100) RumbleRightMotor = 100;
            if (RumbleDurationMs < 50) RumbleDurationMs = 50;
            if (RumbleDurationMs > 3000) RumbleDurationMs = 3000;
        }
    }

    /// <summary>
    /// 一个模式：模式名称、整套虚拟按键列表、摇杆 / 方向键 / 视角操控、
    /// 图片识别与内存扫描自动切换配置。主模式之间布局互不影响，可通过特征图或内存值自动切换。
    /// </summary>
    [XmlType("Mode")]
    public class ModeConfig
    {
        [XmlAttribute]
        public string Id = "";
        public string Name = "";
        public List<VirtualKey> Keys = new List<VirtualKey>();
        public bool ShowJoystick = true;
        public bool ShowMouseLook = true;
        // 手柄厂商布局：0=Xbox，1=索尼，2=任天堂；仅决定预览符号/位置风格。
        public int PadLayout = 0;

        // 每个模式各自独立的摇杆配置（位置、大小、按键、死区、灵敏度、
        // 曲线、自动收起等全部按模式保存，不再共享全局摇杆设置）。
        public JoystickConfig Joystick = new JoystickConfig();
        // 右摇杆配置：与左摇杆参数体系一致，位置默认右侧；输出手柄右摇杆连续轴。
        public JoystickConfig JoystickR = new JoystickConfig();
        // 方向键整体外观：位置 / 大小 / 不透明度 / 显隐；四向触发仍由 Keys 中四个方向键各自负责。
        public DpadConfig Dpad = new DpadConfig();

        // 以下 Auto* 字段仅用于版本迁移（V6 之前按模式存储的自动切换配置），
        // 新版本统一使用 AppConfig.ImageProjects / MemoryProjects。
        public bool AutoImageEnabled = false;
        public bool AutoImageCrossValidation = true;
        public int AutoImageTargetMode = 0;
        public List<AutoFeatureConfig> AutoFeatures = new List<AutoFeatureConfig>();

        // 每个模式各自独立的唯一内存地址配置。
        public bool AutoMemoryEnabled = false;
        public int AutoMemoryTargetMode = 0;
        public MemoryFeatureConfig AutoMemory = new MemoryFeatureConfig();

        public ModeConfig()
        {
            // 右摇杆默认落在屏幕右侧；左摇杆默认位置由 JoystickConfig 构造值提供。
            if (JoystickR != null)
            {
                JoystickR.X = 0.82f;
                JoystickR.Y = 0.72f;
            }
        }

        public ModeConfig(string name)
        {
            Id = Guid.NewGuid().ToString("N");
            Name = name;
        }

        // 克隆本模式布局：按键配置与摇杆设置整体深拷贝，自动切换规则不随新模式走。
        public ModeConfig CloneLayout(string newName)
        {
            ModeConfig m = new ModeConfig(newName);
            m.ShowJoystick = ShowJoystick;
            m.ShowMouseLook = ShowMouseLook;
            m.PadLayout = PadLayout;
            m.Joystick = Joystick == null ? new JoystickConfig() : Joystick.Clone();
            m.JoystickR = JoystickR == null ? new JoystickConfig() : JoystickR.Clone();
            m.Dpad = Dpad == null ? new DpadConfig() : Dpad.Clone();
            if (Keys != null)
            {
                for (int i = 0; i < Keys.Count; i++)
                {
                    VirtualKey k = Keys[i];
                    if (k != null) m.Keys.Add(k.Clone());
                }
            }
            return m;
        }

        public void Normalize(int index)
        {
            if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            if (Name == null) Name = "";
            Name = Name.Trim();
            if (Name.Length == 0) Name = "模式" + ChineseNumber(index + 1);
            if (Keys == null) Keys = new List<VirtualKey>();
            for (int i = 0; i < Keys.Count; i++)
            {
                if (Keys[i] == null)
                {
                    Keys[i] = new VirtualKey();
                }
                Keys[i].Normalize(i);
            }
            if (AutoFeatures == null) AutoFeatures = new List<AutoFeatureConfig>();
            while (AutoFeatures.Count < 3) AutoFeatures.Add(new AutoFeatureConfig());
            if (AutoFeatures.Count > 3) AutoFeatures.RemoveRange(3, AutoFeatures.Count - 3);
            for (int i = 0; i < AutoFeatures.Count; i++)
            {
                if (AutoFeatures[i] == null) AutoFeatures[i] = new AutoFeatureConfig();
                AutoFeatures[i].Normalize();
            }
            if (AutoMemory == null) AutoMemory = new MemoryFeatureConfig();
            AutoMemory.Normalize();
            if (AutoMemoryTargetMode < 0) AutoMemoryTargetMode = 0;
            if (AutoImageTargetMode < 0) AutoImageTargetMode = 0;
            if (Joystick == null) Joystick = new JoystickConfig();
            Joystick.Normalize();
            if (JoystickR == null) JoystickR = new JoystickConfig();
            JoystickR.Normalize();
            if (Dpad == null) Dpad = new DpadConfig();
            Dpad.Normalize();
            // 方向键整体位置以 DpadConfig 为准；Keys 里四个方向键实体只承担四向触发与真机
            // 热区，围绕 Dpad 中心按标准十字偏移重排，不再用实体平均坐标反向覆盖整体位置，
            // 避免拖动保存后重启被旧实体坐标拉回原位。
            if (Keys != null && Keys.Count > 0)
            {
                for (int i = 0; i < Keys.Count; i++)
                {
                    VirtualKey k = Keys[i];
                    if (k == null || k.ShortPress == null ||
                        k.ShortPress.Kind != BindingKind.Gamepad || !IsDpadCode(k.ShortPress.Code))
                        continue;
                    float ox = 0f, oy = 0f;
                    if (k.ShortPress.Code == PadButtons.DPadUp) { oy = -0.12f; }
                    else if (k.ShortPress.Code == PadButtons.DPadDown) { oy = 0.12f; }
                    else if (k.ShortPress.Code == PadButtons.DPadLeft) { ox = -0.07f; }
                    else if (k.ShortPress.Code == PadButtons.DPadRight) { ox = 0.07f; }
                    k.X = AppConfig.ClampF(Dpad.X + ox, 0.03f, 0.97f);
                    k.Y = AppConfig.ClampF(Dpad.Y + oy, 0.03f, 0.97f);
                }
            }
            if (PadLayout < 0) PadLayout = 0;
            if (PadLayout > 2) PadLayout = 0;
            NormalizeActionButtons();
        }

        // 动作键统一按当前厂商布局回到菱形相切标准位，避免旧配置散坐标导致四键过宽；
        // 用户拖动其他按键不受影响，动作键标准位由预设目录 GetSlots 同源给出。
        private void NormalizeActionButtons()
        {
            if (Keys == null) return;
            bool nintendo = PadLayout == (int)MobileUiConfig.PadLayout.Nintendo;
            const float upX = 0.85f, upY = 0.5031f;
            const float leftX = 0.8053f, leftY = 0.60f;
            const float rightX = 0.8947f, rightY = 0.60f;
            const float downX = 0.85f, downY = 0.6969f;
            for (int i = 0; i < Keys.Count; i++)
            {
                VirtualKey k = Keys[i];
                if (k == null || k.ShortPress == null || k.ShortPress.Kind != BindingKind.Gamepad) continue;
                int code = k.ShortPress.Code;
                if (nintendo)
                {
                    if (code == PadButtons.X) { k.X = upX; k.Y = upY; }
                    else if (code == PadButtons.Y) { k.X = leftX; k.Y = leftY; }
                    else if (code == PadButtons.A) { k.X = rightX; k.Y = rightY; }
                    else if (code == PadButtons.B) { k.X = downX; k.Y = downY; }
                }
                else
                {
                    if (code == PadButtons.Y) { k.X = upX; k.Y = upY; }
                    else if (code == PadButtons.X) { k.X = leftX; k.Y = leftY; }
                    else if (code == PadButtons.B) { k.X = rightX; k.Y = rightY; }
                    else if (code == PadButtons.A) { k.X = downX; k.Y = downY; }
                }
            }
        }

        private static bool IsDpadCode(int code)
        {
            return code == PadButtons.DPadUp || code == PadButtons.DPadDown ||
                   code == PadButtons.DPadLeft || code == PadButtons.DPadRight;
        }

        public static string ChineseNumber(int n)
        {
            string[] digits = new string[] { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十" };
            if (n <= 10) return digits[n];
            if (n < 20) return "十" + digits[n - 10];
            if (n < 100)
            {
                int tens = n / 10;
                int ones = n % 10;
                return digits[tens] + "十" + (ones == 0 ? "" : digits[ones]);
            }
            return n.ToString(CultureInfo.InvariantCulture);
        }
    }

    // 叠加模式：与主模式完全独立的第二组虚拟按键布局。摇杆、鼠标视角
    // 仍跟随当前主模式；叠加层只承载自己的一套虚拟按键。
    [XmlType("OverlayMode")]
    public class OverlayModeConfig
    {
        [XmlAttribute]
        public string Id = "";
        public string Name = "";
        public List<VirtualKey> Keys = new List<VirtualKey>();
        // 叠加模式启停：关闭后真机呼出不挂载本层按键。
        public bool Enabled = true;
        // 勾选后下方手机预览只显示本叠加层按键（隐藏主模式按键）；
        // 关闭时本叠加层按键与主模式按键共同叠加显示。
        public bool SoloInPreview = false;

        public OverlayModeConfig()
        {
        }

        public OverlayModeConfig(string name)
        {
            Id = Guid.NewGuid().ToString("N");
            Name = name;
        }

        // 克隆本叠加层布局：按键配置整体深拷贝，启停与预览单独显示属性一并复制。
        public OverlayModeConfig CloneLayout(string newName)
        {
            OverlayModeConfig o = new OverlayModeConfig(newName);
            o.Enabled = Enabled;
            o.SoloInPreview = SoloInPreview;
            if (Keys != null)
            {
                for (int i = 0; i < Keys.Count; i++)
                {
                    VirtualKey k = Keys[i];
                    if (k != null) o.Keys.Add(k.Clone());
                }
            }
            return o;
        }

        public void Normalize(int index)
        {
            if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            if (Name == null) Name = "";
            Name = Name.Trim();
            if (Name.Length == 0) Name = "叠加" + ModeConfig.ChineseNumber(index + 1);
            if (Keys == null) Keys = new List<VirtualKey>();
            for (int i = 0; i < Keys.Count; i++)
            {
                VirtualKey key = Keys[i];
                if (key != null) key.Normalize(i);
            }
        }
    }

    // 图片识别自动切换项目：与界面预览的模式完全独立，每个项目是一条
    // 独立的切换规则（特征图 + 目标模式）。目标模式指向界面预览的模式。
    [XmlType("ImageProject")]
    public class AutoImageProject
    {
        [XmlAttribute]
        public string Id = "";
        public string Name = "";
        public bool Enabled = false;
        public bool CrossValidation = true;
        public int TargetMode = 0;
        public int HitCount = 1;       // 命中次数：连续匹配几次才触发切换
        public int IntervalMs = 400;   // 扫描间隔（毫秒）
        public int CooldownMs = 2000;  // 切换后冷却（毫秒），切换后一段时间内保持当前模式不回跳
        public float Threshold = 0.85f; // 命中率：相似度达到该值才判定识别成功
        public List<AutoFeatureConfig> Features = new List<AutoFeatureConfig>();

        public void Normalize(int index)
        {
            if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            if (Name == null) Name = "";
            Name = Name.Trim();
            if (Name.Length == 0) Name = "项目" + ModeConfig.ChineseNumber(index + 1);
            if (HitCount < 1) HitCount = 1;
            if (HitCount > 5) HitCount = 5;
            if (IntervalMs < 200) IntervalMs = 200;
            if (IntervalMs > 3000) IntervalMs = 3000;
            if (CooldownMs < 0) CooldownMs = 0;
            if (CooldownMs > 10000) CooldownMs = 10000;
            if (Threshold < 0.5f) Threshold = 0.5f;
            if (Threshold > 1f) Threshold = 1f;
            if (Features == null) Features = new List<AutoFeatureConfig>();
            while (Features.Count < 3) Features.Add(new AutoFeatureConfig());
            if (Features.Count > 3) Features.RemoveRange(3, Features.Count - 3);
            for (int i = 0; i < Features.Count; i++)
            {
                if (Features[i] == null) Features[i] = new AutoFeatureConfig();
                Features[i].Normalize();
            }
            if (TargetMode < 0) TargetMode = 0;
        }
    }

    // 内存扫描自动切换项目：与界面预览的模式完全独立，每个项目扫描
    // 唯一一个内存地址，命中后切换到界面预览的目标模式。
    [XmlType("MemoryProject")]
    public class AutoMemoryProject
    {
        [XmlAttribute]
        public string Id = "";
        public string Name = "";
        public bool Enabled = false;
        public int TargetMode = 0;
        public MemoryFeatureConfig Memory = new MemoryFeatureConfig();

        public void Normalize(int index)
        {
            if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            if (Name == null) Name = "";
            Name = Name.Trim();
            if (Name.Length == 0) Name = "项目" + ModeConfig.ChineseNumber(index + 1);
            if (Memory == null) Memory = new MemoryFeatureConfig();
            Memory.Normalize();
            if (TargetMode < 0) TargetMode = 0;
        }
    }

    [XmlType("DisplayKey")]
    public class DisplayKeyConfig
    {
        public bool Enabled = true;
        public int ShortAction = 0;
        public int LongAction = 3;
        public int DoubleAction = -1;
        public int LongPressMs = 2000;

        public void Normalize()
        {
            if (ShortAction < -1 || ShortAction > 5) ShortAction = 0;
            if (LongAction < -1 || LongAction > 5) LongAction = 3;
            if (DoubleAction < -1 || DoubleAction > 5) DoubleAction = -1;
            if (LongPressMs < 200) LongPressMs = 200;
            if (LongPressMs > 2000) LongPressMs = 2000;
        }
    }

    [XmlType("ModeKey")]
    public class ModeKeyConfig
    {
        public bool Enabled = true;
        public int ShortAction = 0;
        public int LongAction = 0;
        public int DoubleAction = -1;
        public int LongPressMs = 2000;

        public void Normalize(int modeCount)
        {
            NormalizeAction(ref ShortAction, modeCount);
            NormalizeAction(ref LongAction, modeCount);
            NormalizeAction(ref DoubleAction, modeCount);
            if (LongPressMs < 200) LongPressMs = 200;
            if (LongPressMs > 2000) LongPressMs = 2000;
        }

        private static void NormalizeAction(ref int action, int modeCount)
        {
            if (action < -1) action = -1;
            if (action == 0) return;
            if (action > modeCount) action = modeCount;
        }
    }

    /// <summary>视角操控配置：启用开关、外形尺寸、不透明度、绑定鼠标键、灵敏度与消息注入开关。</summary>
    [XmlType("MouseLook")]
    public class MouseLookConfig
    {
        public bool Enabled = true;
        public int Width = 210;
        public int Height = 210;
        public int Opacity = 65;
        public int Sensitivity = 100;
        public bool MessageInjection = false;
        public LookShape Shape = LookShape.Circle;
        public KeyBinding Button = new KeyBinding(BindingKind.MouseButton, 1);
        public bool HideWithHidden = true;
        public float X = 0.85f;
        public float Y = 0.40f;

        public void Normalize()
        {
            if (Width < 60) Width = 60;
            if (Width > 900) Width = 900;
            if (Height < 60) Height = 60;
            if (Height > 900) Height = 900;
            if (Shape == LookShape.Circle || Shape == LookShape.Square)
            {
                if (Height != Width) Height = Width;
            }
            if (Opacity < 10) Opacity = 10;
            if (Opacity > 100) Opacity = 100;
            if (Sensitivity < 100) Sensitivity = 100;
            if (Sensitivity > 2000) Sensitivity = 2000;
            if (Button == null) Button = new KeyBinding(BindingKind.None, 0);
            if (Button.Kind == BindingKind.MouseButton && (Button.Code < 0 || Button.Code > 4)) Button.Code = 1;
            X = AppConfig.ClampF(X, 0.02f, 0.98f);
            Y = AppConfig.ClampF(Y, 0.02f, 0.98f);
        }

        public static string ButtonName(int code)
        {
            if (code < 0) return "无";
            if (code == 0) return "鼠标左键";
            if (code == 1) return "鼠标右键";
            if (code == 2) return "鼠标中键";
            if (code == 3) return "鼠标侧键 1";
            if (code == 4) return "鼠标侧键 2";
            return "鼠标右键";
        }

        public static string ShapeName(LookShape shape)
        {
            if (shape == LookShape.Square) return "方形";
            if (shape == LookShape.Rectangle) return "长方形";
            return "圆形";
        }
    }

    // 全局手柄震动（XInput）：所有虚拟按键按下共用一套配置，经 ViGEm 虚拟手柄由
    // Sunshine 串流控制通道转发到 Moonlight 等客户端，协同振动触发手机机身震动。
    public class RumbleConfig
    {
        public bool Enabled = false;
        public int LeftMotor = 60;        // 左（低频）马达 0～100
        public int RightMotor = 60;       // 右（高频）马达 0～100
        public int DurationMs = 150;      // 单次脉冲时长 50～3000 ms
        // 节奏：0=单次脉冲，1=心跳，2=提醒·三短，3=冲击·渐弱，4=连发，5=自定义节奏。
        public int Preset = 0;
        // 自定义节奏段：左:右:毫秒，逗号分段；0:0:毫秒=停顿；两段写法 强度:毫秒=左右同强度。
        public string Pattern = "";
        public int HoldStrength = 80;     // 持续震动验证强度 0～100

        public void Normalize()
        {
            if (LeftMotor < 0) LeftMotor = 0;
            if (LeftMotor > 100) LeftMotor = 100;
            if (RightMotor < 0) RightMotor = 0;
            if (RightMotor > 100) RightMotor = 100;
            if (DurationMs < 50) DurationMs = 50;
            if (DurationMs > 3000) DurationMs = 3000;
            if (Preset < 0) Preset = 0;
            if (Preset > 5) Preset = 5;
            if (Pattern == null) Pattern = "";
            Pattern = Pattern.Trim();
            if (HoldStrength < 0) HoldStrength = 0;
            if (HoldStrength > 100) HoldStrength = 100;
        }
    }

    // 全局多模态反馈：未在按键上单独配置（或开关关闭）时，视觉 / 音效回落使用本套配置；
    // 按键自身配置优先级高于本套。震动全局配置仍由 RumbleConfig（AppConfig.Rumble）承载。
    public class GlobalFeedbackConfig
    {
        // 视觉反馈：0=按下缩放，1=高亮闪白，2=边框发光，3=颜色脉冲，4=不透明度脉冲。
        public bool VisualEnabled = false;
        public int VisualPreset = 1;
        public string VisualColor = "#FFFFFF";
        public int VisualIntensity = 70;
        public int VisualDurationMs = 180;
        public int VisualScalePercent = 90;

        // 音效反馈：0=轻咔哒，1=确认音，2=错误音，3=长音，4=自定义音效文件。
        public bool SoundEnabled = false;
        public int SoundPreset = 0;
        public string SoundFile = "";
        public int SoundVolume = 80;

        public void Normalize()
        {
            if (VisualPreset < 0) VisualPreset = 0;
            if (VisualPreset > 4) VisualPreset = 4;
            if (VisualColor == null) VisualColor = "#FFFFFF";
            VisualColor = VisualColor.Trim();
            if (VisualIntensity < 0) VisualIntensity = 0;
            if (VisualIntensity > 100) VisualIntensity = 100;
            if (VisualDurationMs < 100) VisualDurationMs = 100;
            if (VisualDurationMs > 1000) VisualDurationMs = 1000;
            if (VisualScalePercent < 80) VisualScalePercent = 80;
            if (VisualScalePercent > 100) VisualScalePercent = 100;
            if (SoundPreset < 0) SoundPreset = 0;
            if (SoundPreset > 4) SoundPreset = 4;
            if (SoundFile == null) SoundFile = "";
            SoundFile = SoundFile.Trim();
            if (SoundVolume < 0) SoundVolume = 0;
            if (SoundVolume > 100) SoundVolume = 100;
        }
    }

    [XmlType("AutoFeature")]
    public class AutoFeatureConfig
    {
        public bool Enabled = false;
        public string ImageFile = "";
        public int ModeIndex = 0;
        public float Threshold = 0.85f;
        public int CaptureWidth = 0;
        public int CaptureHeight = 0;
        // 裁剪矩形中心在当时游戏窗口中的归一化坐标（0~1）；-1 表示旧配置未记录，
        // 识别时退化为全屏搜索。锚点用于把多尺度搜索限定在 UI 元素固定出现的位置。
        public float AnchorX = -1f;
        public float AnchorY = -1f;

        public void Normalize()
        {
            if (ImageFile == null) ImageFile = "";
            ImageFile = ImageFile.Trim();
            if (Threshold < 0.50f) Threshold = 0.50f;
            if (Threshold > 0.98f) Threshold = 0.98f;
            if (ModeIndex < 0) ModeIndex = 0;
            if (CaptureWidth < 0) CaptureWidth = 0;
            if (CaptureHeight < 0) CaptureHeight = 0;
            AnchorX = ClampAnchor(AnchorX);
            AnchorY = ClampAnchor(AnchorY);
        }

        private static float ClampAnchor(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return -1f;
            if (v < -0.99f) return -1f;
            if (v < 0f) return -1f;
            if (v > 1f) return 1f;
            return v;
        }
    }

    [XmlType("StorySkipImage")]
    public class StorySkipImageConfig
    {
        public string ImageFile = "";
        public float Threshold = 0.85f;
        public int CaptureWidth = 0;
        public int CaptureHeight = 0;
        // 裁剪矩形中心归一化坐标（0~1）；-1 表示未记录，全屏搜索。
        public float AnchorX = -1f;
        public float AnchorY = -1f;

        public void Normalize()
        {
            if (ImageFile == null) ImageFile = "";
            ImageFile = ImageFile.Trim();
            if (Threshold < 0.50f) Threshold = 0.50f;
            if (Threshold > 0.98f) Threshold = 0.98f;
            if (CaptureWidth < 0) CaptureWidth = 0;
            if (CaptureHeight < 0) CaptureHeight = 0;
            AnchorX = ClampAnchor(AnchorX);
            AnchorY = ClampAnchor(AnchorY);
        }

        private static float ClampAnchor(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return -1f;
            if (v < -0.99f) return -1f;
            if (v < 0f) return -1f;
            if (v > 1f) return 1f;
            return v;
        }
    }

    [XmlType("StorySkipFeature")]
    public class StorySkipFeatureConfig
    {
        public bool Enabled = false;
        public string ImageFile = "";
        public float Threshold = 0.85f;
        public int CaptureWidth = 0;
        public int CaptureHeight = 0;
        public List<StorySkipImageConfig> Images = new List<StorySkipImageConfig>();

        public void Normalize()
        {
            if (ImageFile == null) ImageFile = "";
            ImageFile = ImageFile.Trim();
            if (Threshold < 0.50f) Threshold = 0.50f;
            if (Threshold > 0.98f) Threshold = 0.98f;
            if (Images == null) Images = new List<StorySkipImageConfig>();
            while (Images.Count < 3) Images.Add(new StorySkipImageConfig());
            if (Images.Count > 3) Images.RemoveRange(3, Images.Count - 3);
            for (int i = 0; i < Images.Count; i++)
            {
                if (Images[i] == null) Images[i] = new StorySkipImageConfig();
                Images[i].Normalize();
            }
            if (!string.IsNullOrEmpty(ImageFile) && string.IsNullOrEmpty(Images[0].ImageFile))
            {
                Images[0].ImageFile = ImageFile;
                Images[0].Threshold = Threshold;
                Images[0].CaptureWidth = CaptureWidth;
                Images[0].CaptureHeight = CaptureHeight;
            }
        }
    }

    [XmlType("StorySkip")]
    public class StorySkipConfig
    {
        public bool Enabled = false;
        public bool LocalEnabled = true;
        public bool StreamEnabled = false;
        public bool AlwaysEnabled = false;
        public bool CrossValidation = true;
        public int HitCount = 1;       // 命中次数：连续匹配几次才点击
        public int IntervalMs = 600;   // 扫描间隔（毫秒）
        public float Threshold = 0.85f; // 命中率：相似度达到该值才判定识别成功
        // 「跳过剧情」全局快捷键：游戏隐藏/最小化后生效，按下切换总开关 Enabled；仅支持键盘，
        // 默认 F9（与图片识别 F8 区分）。两页（按键主根 / 手柄独立根）各自保存、互不影响。
        public KeyBinding Hotkey = new KeyBinding(BindingKind.Keyboard, (int)Keys.F9);
        public List<StorySkipFeatureConfig> Features = new List<StorySkipFeatureConfig>();

        public void Normalize()
        {
            if (Hotkey == null || Hotkey.Kind != BindingKind.Keyboard || Hotkey.Code <= 0)
                Hotkey = new KeyBinding(BindingKind.Keyboard, (int)Keys.F9);
            if (HitCount < 1) HitCount = 1;
            if (HitCount > 5) HitCount = 5;
            if (IntervalMs < 200) IntervalMs = 200;
            if (IntervalMs > 3000) IntervalMs = 3000;
            if (Threshold < 0.5f) Threshold = 0.5f;
            if (Threshold > 1f) Threshold = 1f;
            if (Features == null) Features = new List<StorySkipFeatureConfig>();
            while (Features.Count < 3) Features.Add(new StorySkipFeatureConfig());
            if (Features.Count > 3) Features.RemoveRange(3, Features.Count - 3);
            for (int i = 0; i < Features.Count; i++)
            {
                if (Features[i] == null) Features[i] = new StorySkipFeatureConfig();
                Features[i].Normalize();
            }
        }
    }

    [XmlType("MemoryFeature")]
    public class MemoryFeatureConfig
    {
        public bool Enabled = false;
        public string BaseAddress = "";
        public string Offsets = "";
        public string TargetValue = "";
        public int ModeIndex = 0;

        public void Normalize()
        {
            if (BaseAddress == null) BaseAddress = "";
            if (Offsets == null) Offsets = "";
            if (TargetValue == null) TargetValue = "";
            BaseAddress = BaseAddress.Trim();
            Offsets = Offsets.Trim();
            TargetValue = TargetValue.Trim();
            if (ModeIndex < 0) ModeIndex = 0;
        }
    }

    [XmlType("Cooldown")]
    public class CooldownConfig
    {
        public bool Enabled = false;
        public List<CooldownSlotConfig> Slots = new List<CooldownSlotConfig>();

        public void Normalize()
        {
            if (Slots == null) Slots = new List<CooldownSlotConfig>();
            while (Slots.Count < 4) Slots.Add(new CooldownSlotConfig());
            if (Slots.Count > 4) Slots.RemoveRange(4, Slots.Count - 4);
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i] == null) Slots[i] = new CooldownSlotConfig();
                Slots[i].Normalize(i);
            }
        }
    }

    [XmlType("CooldownSlot")]
    public class CooldownSlotConfig
    {
        public bool Enabled = true;
        public string Name = "";
        public string BaseAddress = "";
        public string Offsets = "";
        public int ValueType = 0; // 0=毫秒整数, 1=秒浮点, 2=秒整数
        public bool Direct = false; // false=指针链, true=基址+偏移直接读取
        public int MaxSeconds = 30;

        public void Normalize(int index)
        {
            if (Name == null) Name = "";
            Name = Name.Trim();
            if (Name.Length == 0) Name = "技能" + (index + 1);
            if (Name.Length > 16) Name = Name.Substring(0, 16);
            if (BaseAddress == null) BaseAddress = "";
            if (Offsets == null) Offsets = "";
            BaseAddress = BaseAddress.Trim();
            Offsets = Offsets.Trim();
            if (ValueType < 0 || ValueType > 2) ValueType = 0;
            if (MaxSeconds < 1) MaxSeconds = 1;
            if (MaxSeconds > 300) MaxSeconds = 300;
        }
    }

    [XmlType("MemoryScan")]
    public class MemoryScanConfig
    {
        public bool Enabled = false;
        public bool CrossValidation = true;
        public int IntervalMs = 700;
        public List<MemoryFeatureConfig> Features = new List<MemoryFeatureConfig>();

        public void Normalize()
        {
            if (Features == null) Features = new List<MemoryFeatureConfig>();
            if (IntervalMs < 50) IntervalMs = 50;
            if (IntervalMs > 1000) IntervalMs = 1000;
            while (Features.Count < 3) Features.Add(new MemoryFeatureConfig());
            if (Features.Count > 3) Features.RemoveRange(3, Features.Count - 3);
            for (int i = 0; i < Features.Count; i++)
            {
                if (Features[i] == null) Features[i] = new MemoryFeatureConfig();
                Features[i].Normalize();
            }
        }
    }

    [XmlType("AutoMode")]
    public class AutoModeConfig
    {
        public bool Enabled = false;
        public bool CrossValidation = true;
        public KeyBinding Hotkey = new KeyBinding(BindingKind.Keyboard, (int)Keys.F8);
        public List<AutoFeatureConfig> Features = new List<AutoFeatureConfig>();

        public void Normalize()
        {
            if (Hotkey == null || Hotkey.Kind != BindingKind.Keyboard || Hotkey.Code <= 0)
                Hotkey = new KeyBinding(BindingKind.Keyboard, (int)Keys.F8);
            if (Features == null) Features = new List<AutoFeatureConfig>();
            while (Features.Count < 3) Features.Add(new AutoFeatureConfig());
            if (Features.Count > 3) Features.RemoveRange(3, Features.Count - 3);
            for (int i = 0; i < Features.Count; i++)
            {
                if (Features[i] == null) Features[i] = new AutoFeatureConfig();
                Features[i].Normalize();
            }
        }
    }

    /// <summary>摇杆配置：大小 / 内外圈 / 不透明度 / 位置 / 死区 / 灵敏度 / 响应曲线与四方向绑定。</summary>
    [XmlType("Joystick")]
    public class JoystickConfig    {
        public bool Enabled = true;
        public int Size = 250;
        public int OuterSize = 200;
        public int InnerSize = 84;
        public int Opacity = 70;
        public bool HideWithHidden = true;
        public float X = 0.18f;
        public float Y = 0.72f;
        // 死区：半径占外圈半径百分比，0～40，区内不产生方向输入。
        public int DeadZone = 12;
        // 灵敏度：50～200，100 为原始强度，按比例放大/缩小输出长度。
        public int Sensitivity = 100;
        // 曲线类型：0 线性，1 加速（凸），2 减速（凹），3 S 型。
        public int CurveType = 0;
        // 曲线弯曲度：0～100，0 接近线性，100 曲线最陡。
        public int CurveStrength = 50;
        // 外圈自动收起秒数：0 常显外圈；1～10 秒无操作后只保留内圈，
        // 点击内圈恢复外圈。
        public int AutoHideSeconds = 0;
        // 方向绑定：仅虚拟按键页摇杆使用（WASD / 鼠标离散触发）；虚拟手柄页摇杆走连续轴，不读取这四个绑定。
        public KeyBinding Up = new KeyBinding();
        public KeyBinding Down = new KeyBinding();
        public KeyBinding Left = new KeyBinding();
        public KeyBinding Right = new KeyBinding();

        public void Normalize()
        {
            if (Opacity < 10) Opacity = 10;
            if (Opacity > 100) Opacity = 100;
            if (Size < 80) Size = 80;
            if (Size > 500) Size = 500;
            if (OuterSize < 40) OuterSize = 40;
            if (OuterSize > Size) OuterSize = Size;
            if (InnerSize < 20) InnerSize = 20;
            if (InnerSize > OuterSize) InnerSize = OuterSize;
            X = AppConfig.ClampF(X, 0.02f, 0.98f);
            Y = AppConfig.ClampF(Y, 0.02f, 0.98f);
            if (DeadZone < 0) DeadZone = 0;
            if (DeadZone > 40) DeadZone = 40;
            if (Sensitivity < 50) Sensitivity = 50;
            if (Sensitivity > 200) Sensitivity = 200;
            if (CurveType < 0) CurveType = 0;
            if (CurveType > 3) CurveType = 3;
            if (CurveStrength < 0) CurveStrength = 0;
            if (CurveStrength > 100) CurveStrength = 100;
            if (AutoHideSeconds < 0) AutoHideSeconds = 0;
            if (AutoHideSeconds > 10) AutoHideSeconds = 10;
            if (Up == null) Up = new KeyBinding();
            if (Down == null) Down = new KeyBinding();
            if (Left == null) Left = new KeyBinding();
            if (Right == null) Right = new KeyBinding();
        }

        public JoystickConfig Clone()
        {
            JoystickConfig j = new JoystickConfig();
            j.Enabled = Enabled;
            j.Size = Size;
            j.OuterSize = OuterSize;
            j.InnerSize = InnerSize;
            j.Opacity = Opacity;
            j.HideWithHidden = HideWithHidden;
            j.X = X;
            j.Y = Y;
            j.DeadZone = DeadZone;
            j.Sensitivity = Sensitivity;
            j.CurveType = CurveType;
            j.CurveStrength = CurveStrength;
            j.AutoHideSeconds = AutoHideSeconds;
            j.Up = Up == null ? null : Up.Clone();
            j.Down = Down == null ? null : Down.Clone();
            j.Left = Left == null ? null : Left.Clone();
            j.Right = Right == null ? null : Right.Clone();
            return j;
        }
    }

    // 方向键整体外观配置：只负责圆盘位置、大小、不透明度与显隐；四向触发仍由模式按键列表中
    // 四个方向键各自承担，本配置不保存方向绑定。
    public class DpadConfig
    {
        public bool Enabled = true;
        // 圆盘直径（基准像素，预览层乘 _screenScale 换算物理尺寸）。
        public int Size = 200;
        public int Opacity = 100;
        public bool HideWithHidden = true;
        public float X = 0.34f;
        public float Y = 0.62f;

        public void Normalize()
        {
            if (Opacity < 10) Opacity = 10;
            if (Opacity > 100) Opacity = 100;
            if (Size < 80) Size = 80;
            if (Size > 400) Size = 400;
            // 旧配置 150 偏小，一次性抬升到默认档位；≥170 的自定义尺寸保留。
            if (Size < 170) Size = 200;
            X = AppConfig.ClampF(X, 0.03f, 0.97f);
            Y = AppConfig.ClampF(Y, 0.03f, 0.97f);
        }

        public DpadConfig Clone()
        {
            DpadConfig d = new DpadConfig();
            d.Enabled = Enabled;
            d.Size = Size;
            d.Opacity = Opacity;
            d.HideWithHidden = HideWithHidden;
            d.X = X;
            d.Y = Y;
            return d;
        }
    }

    /// <summary>
    /// 根配置（对应 MobileUIConfig.xml 的 &lt;MobileUiConfig&gt; 节点）：项目与游戏路径、
    /// 横竖屏、本地 / 串流开关、系统显示键 / 模式键、摇杆与视角、全部主模式与叠加层模式、
    /// 全局震动、自动跳过剧情等。<see cref="ConfigStore"/> 负责它的序列化与版本迁移。
    /// </summary>
    [XmlRoot("MobileUiConfig")]
    public class AppConfig
    {
        public int Version = 15;
        public int ProfileOrder = 0;
        public string ProjectName = "移动端UI";
        public string GameName = "";
        public string GamePath = "";
        public string GameArguments = "";
        public string GameProcessName = "yysls";
        public int SelectedMode = 0;
        public DisplayOrientation Orientation = DisplayOrientation.Landscape;
        public bool HideOnGameLaunch = true;
        public bool StreamOverlayEnabled = true;
        public bool LocalOverlayEnabled = false;
        public bool MouseToTouch = false;
        public bool PinchZoom = false;
        public bool ClickThrough = true;
        // 虚拟手柄页专属：拦截空白区域触摸（手指滑出按键区不落到游戏画面）；屏蔽物理键鼠
        // （手柄模式下吞掉物理键盘与鼠标按钮，避免游戏检测到物理输入切回键鼠 UI）。
        // 两字段挂在当前页配置根上：手柄页写 Gamepad 独立根，按键页不读取、互不影响。
        public bool BlockBlankTouch = false;
        public bool BlockPhysicalInput = false;
        public bool RunAtLogin = false;
        public string StreamProcessList = "moonlight,sunshine,uu,parsec,gameviewer,todesk,anydesk,rustdesk,steamlink,chiaki";

        // Display / hidden / move system key and the mode menu key.
        public float DisplayKeyX = 0.86f;
        public float DisplayKeyY = 0.086f;
        public int DisplayKeySize = 132;
        public float ModeKeyX = 0.86f;
        public float ModeKeyY = 0.176f;
        public int ModeKeySize = 132;
        public DisplayKeyConfig DisplayKey = new DisplayKeyConfig();
        public ModeKeyConfig ModeKey = new ModeKeyConfig();

        public JoystickConfig Joystick = new JoystickConfig();
        public MouseLookConfig MouseLook = new MouseLookConfig();
        public RumbleConfig Rumble = new RumbleConfig();
        public GlobalFeedbackConfig GlobalFeedback = new GlobalFeedbackConfig();
        public AutoModeConfig AutoMode = new AutoModeConfig();
        public MemoryScanConfig MemoryScan = new MemoryScanConfig();
        public CooldownConfig Cooldown = new CooldownConfig();
        public StorySkipConfig StorySkip = new StorySkipConfig();
        public bool AutoExitOnGameEnd = true;

        public List<ModeConfig> Modes = new List<ModeConfig>();

        // 叠加模式组：与主模式完全独立的第二组虚拟按键布局；摇杆、鼠标视角
        // 仍跟随当前主模式，叠加层不另配。SelectedOverlayMode 为预览中当前选中
        // 的叠加模式；真机上由主按键呼出的目标模式由各键 OverlayTargetMode 指定。
        public List<OverlayModeConfig> OverlayModes = new List<OverlayModeConfig>();
        public int SelectedOverlayMode = 0;

        // 预览独立显示（界面预览标题行两个互斥开关）：0=主层与叠加层共存，
        // 1=只显示主界面按键，2=只显示叠加层按键；两开关同时只能开一个。
        public int PreviewSoloMode = 0;

        // 面板卡片折叠状态记忆：左侧 6 张、右侧 6 张，true=折叠；首次进入默认全部折叠，
        // 状态随配置落盘，重启后恢复。列表长度由 Normalize 夹紧到对应卡片数量。
        public List<bool> LeftCardCollapsed = new List<bool>();
        public List<bool> RightCardCollapsed = new List<bool>();

        // 顶层页面切换：0=虚拟按键页，1=虚拟手柄页；两开关互斥，落盘记忆。
        public int UiPadSide = 0;

        // 虚拟手柄驱动通道（界面预览标题行两个互斥开关）：0=ViGEmBus(Xbox360/XInput)，
        // 1=HID(DualShock4/HID 类设备，经同一 ViGEmBus 总线创建)；两开关同时只能开一个。
        public int PadDriverType = 0;

        // 虚拟手柄页独立配置根：与按键页互不影响，随主配置一起落盘；其内部 Gamepad
        // 字段置 null 以避免 XML 序列化递归嵌套。模式/叠加/子配置/项目/折叠态各自归一化。
        public AppConfig Gamepad;

        // 图片识别 / 内存扫描自动切换项目：两个列表互相独立，且与界面预览
        // 的模式（Modes）完全无关；项目内的 TargetMode 才指向界面预览的模式。
        public int SelectedImageProject = 0;
        public int SelectedMemoryProject = 0;
        public List<AutoImageProject> ImageProjects = new List<AutoImageProject>();
        public List<AutoMemoryProject> MemoryProjects = new List<AutoMemoryProject>();

        [XmlIgnore]
        public ModeConfig ActiveMode
        {
            get
            {
                if (Modes == null || Modes.Count == 0) return null;
                int i = SelectedMode;
                if (i < 0) i = 0;
                if (i >= Modes.Count) i = Modes.Count - 1;
                return Modes[i];
            }
        }

        [XmlIgnore]
        public OverlayModeConfig ActiveOverlayMode
        {
            get
            {
                if (OverlayModes == null || OverlayModes.Count == 0) return null;
                int i = SelectedOverlayMode;
                if (i < 0) i = 0;
                if (i >= OverlayModes.Count) i = OverlayModes.Count - 1;
                return OverlayModes[i];
            }
        }

        [XmlIgnore]
        public AutoImageProject ActiveImageProject
        {
            get
            {
                if (ImageProjects == null || ImageProjects.Count == 0) return null;
                int i = SelectedImageProject;
                if (i < 0) i = 0;
                if (i >= ImageProjects.Count) i = ImageProjects.Count - 1;
                return ImageProjects[i];
            }
        }

        [XmlIgnore]
        public AutoMemoryProject ActiveMemoryProject
        {
            get
            {
                if (MemoryProjects == null || MemoryProjects.Count == 0) return null;
                int i = SelectedMemoryProject;
                if (i < 0) i = 0;
                if (i >= MemoryProjects.Count) i = MemoryProjects.Count - 1;
                return MemoryProjects[i];
            }
        }

        public void Normalize()
        {
            if (ProfileOrder < 0) ProfileOrder = 0;
            if (ProjectName == null) ProjectName = "";
            ProjectName = ProjectName.Trim();
            if (ProjectName.Length == 0) ProjectName = "移动端UI";
            if (ProjectName.Length > 60) ProjectName = ProjectName.Substring(0, 60);
            if (GameName == null) GameName = "";
            GameName = GameName.Trim();
            if (GameName.Length > 60) GameName = GameName.Substring(0, 60);
            if (GamePath == null) GamePath = "";
            if (GameArguments == null) GameArguments = "";
            if (GameProcessName == null) GameProcessName = "";
            GameProcessName = GameProcessName.Trim();
            if (GameProcessName.Length == 0) GameProcessName = "yysls";
            if (StreamProcessList == null) StreamProcessList = "";
            if (Orientation != DisplayOrientation.Portrait) Orientation = DisplayOrientation.Landscape;
            if (Modes == null) Modes = new List<ModeConfig>();
            if (Joystick == null) Joystick = new JoystickConfig();
            Joystick.Normalize();
            if (MouseLook == null) MouseLook = new MouseLookConfig();
            MouseLook.Normalize();
            if (Rumble == null) Rumble = new RumbleConfig();
            Rumble.Normalize();
            if (GlobalFeedback == null) GlobalFeedback = new GlobalFeedbackConfig();
            GlobalFeedback.Normalize();
            if (AutoMode == null) AutoMode = new AutoModeConfig();
            AutoMode.Normalize();
            if (MemoryScan == null) MemoryScan = new MemoryScanConfig();
            MemoryScan.Normalize();
            if (Cooldown == null) Cooldown = new CooldownConfig();
            Cooldown.Normalize();
            if (StorySkip == null) StorySkip = new StorySkipConfig();
            StorySkip.Normalize();
            for (int i = 0; i < Modes.Count; i++)
            {
                if (Modes[i] == null) Modes[i] = new ModeConfig();
                Modes[i].Normalize(i);
            }
            if (Version < 7)
            {
                // V6 及以前摇杆是全局一份；V7 起每个模式独立。首次迁移时
                // 把全局摇杆配置深拷贝到每个模式，避免老用户全部归零。
                for (int i = 0; i < Modes.Count; i++)
                {
                    if (Modes[i] == null) continue;
                    Modes[i].Joystick = Joystick.Clone();
                    Modes[i].Joystick.Normalize();
                }
                Version = 7;
            }
            if (Version < 8)
            {
                // V7 及以前没有叠加模式组；V8 起新增独立的第二组虚拟按键布局。
                // 首次迁移生成 5 个空白叠加模式，主模式与现有按键配置原样保留。
                if (OverlayModes == null) OverlayModes = new List<OverlayModeConfig>();
                if (OverlayModes.Count == 0)
                {
                    for (int i = 0; i < 5; i++)
                        OverlayModes.Add(new OverlayModeConfig("叠加" + ModeConfig.ChineseNumber(i + 1)));
                }
                Version = 8;
            }
            if (Version < 9)
            {
                // V8 及以前没有按键级视觉/音效/震动反馈；V9 新增字段，旧配置缺失时
                // 由构造默认值兜底，无需逐条回填，这里只推进版本号。
                Version = 9;
            }
            if (Version < 10)
            {
                // V9 及以前没有预览独立显示开关；V10 新增 PreviewSoloMode，旧配置缺失
                // 时由构造默认值 0（两层共存）兜底，这里只推进版本号。
                Version = 10;
            }
            if (Version < 11)
            {
                // V10 及以前版本配置；V11 只推进版本号，缺失项由构造默认值兜底。
                Version = 11;
            }
            if (Version < 12)
            {
                // V11 及以前震动是按键级音频合成通路；V12 改为全局 XInput 手柄震动。
                // 旧按键级 Haptic* 字段在反序列化时被忽略，新全局 Rumble 由构造默认值兜底。
                Version = 12;
            }
            if (Version < 13)
            {
                // V12 及以前没有全局视觉/音效配置与按键级震动配置；V13 新增 GlobalFeedback
                // 与按键 Rumble* 字段，旧配置缺失由构造默认值（全部关闭）兜底，这里只推进版本号。
                Version = 13;
            }
            if (Version < 14)
            {
                // V13 及以前没有面板卡片折叠状态记忆；V14 新增 LeftCardCollapsed / RightCardCollapsed，
                // 旧配置缺失由 Normalize 补齐为全部折叠（true），这里只推进版本号。
                Version = 14;
            }
            if (Version < 2)
            {
                // Old builds defaulted long-press to 650 ms. Move existing
                // profiles to the new 2 s default once; values set later are
                // preserved because Version will already be 2.
                for (int i = 0; i < Modes.Count; i++)
                {
                    if (Modes[i] == null || Modes[i].Keys == null) continue;
                    for (int k = 0; k < Modes[i].Keys.Count; k++)
                    {
                        VirtualKey key = Modes[i].Keys[k];
                        if (key != null && key.LongPressMs == 650) key.LongPressMs = 2000;
                    }
                }
                Version = 2;
            }
            if (Version < 3)
            {
                // A tap must not become a long press because an old or custom
                // small threshold was saved. Normalize once to 2 s; the editor
                // can still lower it afterwards.
                for (int i = 0; i < Modes.Count; i++)
                {
                    if (Modes[i] == null || Modes[i].Keys == null) continue;
                    for (int k = 0; k < Modes[i].Keys.Count; k++)
                    {
                        VirtualKey key = Modes[i].Keys[k];
                        if (key != null && key.LongPressMs < 2000) key.LongPressMs = 2000;
                    }
                }
                Version = 3;
            }
            if (Modes.Count == 0) Modes.AddRange(CreateDefaultModes());
            if (SelectedMode < 0) SelectedMode = 0;
            if (SelectedMode >= Modes.Count) SelectedMode = Modes.Count - 1;
            if (OverlayModes == null) OverlayModes = new List<OverlayModeConfig>();
            if (OverlayModes.Count == 0)
            {
                for (int i = 0; i < 5; i++)
                    OverlayModes.Add(new OverlayModeConfig("叠加" + ModeConfig.ChineseNumber(i + 1)));
            }
            for (int i = 0; i < OverlayModes.Count; i++)
            {
                if (OverlayModes[i] == null) OverlayModes[i] = new OverlayModeConfig();
                OverlayModes[i].Normalize(i);
            }
            if (SelectedOverlayMode < 0) SelectedOverlayMode = 0;
            if (SelectedOverlayMode >= OverlayModes.Count) SelectedOverlayMode = OverlayModes.Count - 1;
            if (PreviewSoloMode < 0 || PreviewSoloMode > 2) PreviewSoloMode = 0;
            NormalizeCardCollapsed(ref LeftCardCollapsed, 6);
            NormalizeCardCollapsed(ref RightCardCollapsed, 7);
            if (UiPadSide < 0 || UiPadSide > 1) UiPadSide = 0;
            if (PadDriverType < 0 || PadDriverType > 1) PadDriverType = 0;
            // 各按键呼出目标叠加模式索引夹紧到当前叠加模式数量。
            for (int i = 0; i < Modes.Count; i++)
            {
                if (Modes[i] == null || Modes[i].Keys == null) continue;
                for (int k = 0; k < Modes[i].Keys.Count; k++)
                {
                    VirtualKey key = Modes[i].Keys[k];
                    if (key == null) continue;
                    if (key.OverlayTargetMode < 0) key.OverlayTargetMode = 0;
                    if (key.OverlayTargetMode >= OverlayModes.Count)
                        key.OverlayTargetMode = OverlayModes.Count - 1;
                }
            }
            if (DisplayKey == null) DisplayKey = new DisplayKeyConfig();
            DisplayKey.Normalize();
            if (ModeKey == null) ModeKey = new ModeKeyConfig();
            ModeKey.Normalize(Modes.Count);
            // 手柄独立配置根兜底；内部 Gamepad 置 null 防止 XML 序列化递归嵌套。
            if (Gamepad == null) Gamepad = new AppConfig();
            Gamepad.Gamepad = null;
            // 手柄独立根的显示键/模式键动作绑定同样兜底归一化：与主根互不覆盖，
            // 老配置缺失时取构造默认（显示键短按切换/长按进入移动/双击无；模式键短按菜单）。
            if (Gamepad.DisplayKey == null) Gamepad.DisplayKey = new DisplayKeyConfig();
            Gamepad.DisplayKey.Normalize();
            if (Gamepad.ModeKey == null) Gamepad.ModeKey = new ModeKeyConfig();
            Gamepad.ModeKey.Normalize(Gamepad.Modes == null ? 0 : Gamepad.Modes.Count);
            // 手柄独立根跳过剧情配置兜底：快捷键默认 F9、非法绑定回退，与主根互不覆盖。
            if (Gamepad.StorySkip == null) Gamepad.StorySkip = new StorySkipConfig();
            Gamepad.StorySkip.Normalize();
            if (Version < 4)
            {
                // System display/mode keys used a 500 ms long-press; a normal
                // touch can exceed that and accidentally enter move mode.
                DisplayKey.LongPressMs = 2000;
                ModeKey.LongPressMs = 2000;
                Version = 4;
            }
            if (Version < 5)
            {
                // 自动切换从全局一份配置改为每个模式独立配置：把旧的全局配置
                // 迁移到模式一，其他模式保持空白，避免模式之间互相串味。
                if (Modes.Count > 0)
                {
                    ModeConfig first = Modes[0];
                    if (AutoMode != null && AutoMode.Features != null)
                    {
                        first.AutoImageEnabled = AutoMode.Enabled;
                        first.AutoImageCrossValidation = AutoMode.CrossValidation;
                        int target = -1;
                        for (int i = 0; i < AutoMode.Features.Count && i < 3; i++)
                        {
                            AutoFeatureConfig old = AutoMode.Features[i];
                            if (old == null) continue;
                            AutoFeatureConfig feature = first.AutoFeatures[i];
                            feature.Enabled = old.Enabled;
                            feature.ImageFile = old.ImageFile;
                            feature.Threshold = old.Threshold;
                            feature.CaptureWidth = old.CaptureWidth;
                            feature.CaptureHeight = old.CaptureHeight;
                            if (target < 0 && old.Enabled && !string.IsNullOrEmpty(old.ImageFile))
                                target = old.ModeIndex;
                            if (!old.Enabled || string.IsNullOrEmpty(old.ImageFile)) continue;
                            try
                            {
                                string legacy = ConfigStore.LegacyFeaturePath(i);
                                string moved = ConfigStore.FeaturePath(0, i);
                                if (File.Exists(legacy) && !File.Exists(moved))
                                    File.Copy(legacy, moved, true);
                            }
                            catch { }
                        }
                        if (target >= 0) first.AutoImageTargetMode = target;
                    }
                    if (MemoryScan != null && MemoryScan.Features != null)
                    {
                        MemoryFeatureConfig source = null;
                        int target = -1;
                        for (int i = 0; i < MemoryScan.Features.Count && i < 3; i++)
                        {
                            MemoryFeatureConfig candidate = MemoryScan.Features[i];
                            if (candidate == null) continue;
                            if (source == null) source = candidate;
                            if (candidate.Enabled && !string.IsNullOrEmpty(candidate.BaseAddress))
                            {
                                source = candidate;
                                target = candidate.ModeIndex;
                                break;
                            }
                        }
                        if (source != null)
                        {
                            first.AutoMemoryEnabled = MemoryScan.Enabled && source.Enabled;
                            first.AutoMemory.BaseAddress = source.BaseAddress;
                            first.AutoMemory.Offsets = source.Offsets;
                            first.AutoMemory.TargetValue = source.TargetValue;
                            if (target >= 0) first.AutoMemoryTargetMode = target;
                        }
                    }
                }
                Version = 5;
            }
            if (Version < 6)
            {
                // 自动切换从"每个模式一份配置"改为独立的"项目"列表：把各模式下
                // 的旧配置迁移为同序号的项目，特征图文件名不变（索引一致）。
                if (ImageProjects == null || ImageProjects.Count == 0)
                {
                    if (ImageProjects == null) ImageProjects = new List<AutoImageProject>();
                    for (int i = 0; i < Modes.Count; i++)
                    {
                        ModeConfig mode = Modes[i];
                        if (mode == null) continue;
                        AutoImageProject p = new AutoImageProject();
                        p.Normalize(i);
                        p.Name = "项目" + ModeConfig.ChineseNumber(i + 1);
                        p.Enabled = mode.AutoImageEnabled;
                        p.CrossValidation = mode.AutoImageCrossValidation;
                        p.TargetMode = mode.AutoImageTargetMode;
                        if (mode.AutoFeatures != null)
                        {
                            for (int k = 0; k < mode.AutoFeatures.Count && k < p.Features.Count; k++)
                            {
                                if (mode.AutoFeatures[k] == null) continue;
                                p.Features[k] = mode.AutoFeatures[k];
                            }
                        }
                        ImageProjects.Add(p);
                    }
                }
                if (MemoryProjects == null || MemoryProjects.Count == 0)
                {
                    if (MemoryProjects == null) MemoryProjects = new List<AutoMemoryProject>();
                    for (int i = 0; i < Modes.Count; i++)
                    {
                        ModeConfig mode = Modes[i];
                        if (mode == null) continue;
                        AutoMemoryProject p = new AutoMemoryProject();
                        p.Name = "项目" + ModeConfig.ChineseNumber(i + 1);
                        p.Enabled = mode.AutoMemoryEnabled;
                        p.TargetMode = mode.AutoMemoryTargetMode;
                        if (mode.AutoMemory != null) p.Memory = mode.AutoMemory;
                        p.Normalize(i);
                        MemoryProjects.Add(p);
                    }
                }
                Version = 6;
            }
            if (ImageProjects == null) ImageProjects = new List<AutoImageProject>();
            while (ImageProjects.Count < 1) ImageProjects.Add(new AutoImageProject());
            for (int i = 0; i < ImageProjects.Count; i++)
            {
                if (ImageProjects[i] == null) ImageProjects[i] = new AutoImageProject();
                ImageProjects[i].Normalize(i);
            }
            if (MemoryProjects == null) MemoryProjects = new List<AutoMemoryProject>();
            while (MemoryProjects.Count < 1) MemoryProjects.Add(new AutoMemoryProject());
            for (int i = 0; i < MemoryProjects.Count; i++)
            {
                if (MemoryProjects[i] == null) MemoryProjects[i] = new AutoMemoryProject();
                MemoryProjects[i].Normalize(i);
            }
            if (SelectedImageProject < 0) SelectedImageProject = 0;
            if (SelectedImageProject >= ImageProjects.Count) SelectedImageProject = ImageProjects.Count - 1;
            if (SelectedMemoryProject < 0) SelectedMemoryProject = 0;
            if (SelectedMemoryProject >= MemoryProjects.Count) SelectedMemoryProject = MemoryProjects.Count - 1;
            for (int i = 0; i < ImageProjects.Count; i++)
                ImageProjects[i].TargetMode = Clamp(ImageProjects[i].TargetMode, 0, Modes.Count - 1);
            for (int i = 0; i < MemoryProjects.Count; i++)
                MemoryProjects[i].TargetMode = Clamp(MemoryProjects[i].TargetMode, 0, Modes.Count - 1);
            for (int i = 0; i < Modes.Count; i++)
            {
                ModeConfig mode = Modes[i];
                if (mode == null) continue;
                mode.AutoImageTargetMode = Clamp(mode.AutoImageTargetMode, 0, Modes.Count - 1);
                mode.AutoMemoryTargetMode = Clamp(mode.AutoMemoryTargetMode, 0, Modes.Count - 1);
            }
            if (AutoMode != null && AutoMode.Features != null)
            {
                for (int i = 0; i < AutoMode.Features.Count; i++)
                {
                    AutoFeatureConfig feature = AutoMode.Features[i];
                    if (feature == null) continue;
                    if (feature.ModeIndex < 0) feature.ModeIndex = 0;
                    if (feature.ModeIndex >= Modes.Count) feature.ModeIndex = Modes.Count - 1;
                }
            }
            if (MemoryScan != null && MemoryScan.Features != null)
            {
                for (int i = 0; i < MemoryScan.Features.Count; i++)
                {
                    MemoryFeatureConfig feature = MemoryScan.Features[i];
                    if (feature == null) continue;
                    if (feature.ModeIndex < 0) feature.ModeIndex = 0;
                    if (feature.ModeIndex >= Modes.Count) feature.ModeIndex = Modes.Count - 1;
                }
            }

            DisplayKeySize = Clamp(DisplayKeySize, 80, 300);
            ModeKeySize = Clamp(ModeKeySize, 80, 300);
            DisplayKeyX = ClampF(DisplayKeyX, 0.03f, 0.97f);
            DisplayKeyY = ClampF(DisplayKeyY, 0.03f, 0.97f);
            ModeKeyX = ClampF(ModeKeyX, 0.03f, 0.97f);
            ModeKeyY = ClampF(ModeKeyY, 0.03f, 0.97f);
        }

        private static void NormalizeCardCollapsed(ref List<bool> list, int count)
        {
            if (list == null) list = new List<bool>();
            while (list.Count < count) list.Add(true);
            while (list.Count > count) list.RemoveAt(list.Count - 1);
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static float ClampF(float value, float min, float max)
        {
            if (float.IsNaN(value)) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static AppConfig CreateDefault()
        {
            AppConfig c = new AppConfig();
            c.Modes.AddRange(CreateDefaultModes());
            c.Normalize();
            return c;
        }

        // 手柄独立配置根清理：旧版迁移过来的键盘/鼠标/滚轮绑定按键一律移除，
        // 只保留 Gamepad 绑定的手柄键；主按键页配置根不调用本方法，旧键盘布局原样保留。
        public static void SanitizeGamepadRoot(AppConfig root)
        {
            if (root == null) return;
            SanitizeKeyList(root.Modes);
            SanitizeKeyList(root.OverlayModes);
        }

        private static void SanitizeKeyList(List<ModeConfig> modes)
        {
            if (modes == null) return;
            for (int i = 0; i < modes.Count; i++)
            {
                ModeConfig mode = modes[i];
                if (mode == null || mode.Keys == null) continue;
                for (int k = mode.Keys.Count - 1; k >= 0; k--)
                {
                    VirtualKey key = mode.Keys[k];
                    if (key == null || key.ShortPress == null || key.ShortPress.Kind != BindingKind.Gamepad)
                        mode.Keys.RemoveAt(k);
                }
            }
        }

        private static void SanitizeKeyList(List<OverlayModeConfig> modes)
        {
            if (modes == null) return;
            for (int i = 0; i < modes.Count; i++)
            {
                OverlayModeConfig mode = modes[i];
                if (mode == null || mode.Keys == null) continue;
                for (int k = mode.Keys.Count - 1; k >= 0; k--)
                {
                    VirtualKey key = mode.Keys[k];
                    if (key == null || key.ShortPress == null || key.ShortPress.Kind != BindingKind.Gamepad)
                        mode.Keys.RemoveAt(k);
                }
            }
        }

        public static List<ModeConfig> CreateDefaultModes()
        {
            List<ModeConfig> list = new List<ModeConfig>();

            // 模式一：微软 Xbox 布局（底层统一映射 Xbox360 XInput 位码）。
            ModeConfig m1 = new ModeConfig("模式一");
            m1.PadLayout = (int)PadLayout.Xbox;
            m1.Keys = PadCatalog.BuildPreset((int)PadLayout.Xbox);
            PadCatalog.ApplyPresetJoysticks(m1);
            list.Add(m1);

            // 模式二：索尼布局（△○×□ / L1 R1 / L2 R2）。
            ModeConfig m2 = new ModeConfig("模式二");
            m2.PadLayout = (int)PadLayout.Sony;
            m2.Keys = PadCatalog.BuildPreset((int)PadLayout.Sony);
            PadCatalog.ApplyPresetJoysticks(m2);
            list.Add(m2);

            // 模式三：任天堂布局（A/B/X/Y 镜像 / L R / ZL ZR）。
            ModeConfig m3 = new ModeConfig("模式三");
            m3.PadLayout = (int)PadLayout.Nintendo;
            m3.Keys = PadCatalog.BuildPreset((int)PadLayout.Nintendo);
            PadCatalog.ApplyPresetJoysticks(m3);
            list.Add(m3);

            // 模式四、五：空布局，由用户自行添加手柄按键。
            list.Add(new ModeConfig("模式四"));
            list.Add(new ModeConfig("模式五"));

            for (int i = 0; i < list.Count; i++) list[i].Normalize(i);
            return list;
        }
    }

    // 手柄布局目录：供「新增手柄」弹窗按厂商枚举槽位、整套生成预设或单键添加。
    public sealed class PadSlot
    {
        public int Code;
        public int Modifiers;
        // X/Y：弹窗中栏横屏机身面板内的归一化坐标（选型展示用）。
        public float X;
        public float Y;
        // PhoneX/PhoneY：写入手机预览 / 真机布局时的归一化坐标（横屏手机屏 0~1），
        // 与弹窗坐标分离，避免弹窗人体工学位置直接套到手机屏造成按键散落重叠。
        public float PhoneX;
        public float PhoneY;

        public string Symbol(int layout)
        {
            if (Modifiers == PadButtons.ModifierLeftTrigger || Modifiers == PadButtons.ModifierRightTrigger)
                return PadButtons.VendorTrigger(layout, Modifiers);
            return PadButtons.VendorSymbol(layout, Code);
        }

        public KeyBinding ToBinding()
        {
            return new KeyBinding(BindingKind.Gamepad, Code, Modifiers);
        }

        public bool Matches(KeyBinding b)
        {
            if (b == null || b.Kind != BindingKind.Gamepad) return false;
            return b.Code == Code && b.Modifiers == Modifiers;
        }
    }

    /// <summary>
    /// 虚拟手柄按钮目录：按微软 Xbox / 索尼 DualShock / 任天堂 Switch Pro 三套布局，
    /// 登记每个手柄按钮的 XInput 位码、修饰位（LT/RT）以及在弹窗预览与手机画面上的归一化坐标。
    /// </summary>
    public static class PadCatalog
    {
        private static PadSlot Slot(int code, int modifiers, float px, float py, float qx, float qy)
        {
            return new PadSlot { Code = code, Modifiers = modifiers, X = px, Y = py, PhoneX = qx, PhoneY = qy };
        }

        // 弹窗中栏面板坐标（px,py）与手机屏坐标（qx,qy）成对给出；动作键按厂商镜像，
        // 肩键 / 扳机 / 十字键 / 系统键三厂商共用，仅右上排布随厂商微调。
        public static List<PadSlot> GetSlots(int layout)
        {
            List<PadSlot> list = new List<PadSlot>();
            bool nintendo = layout == (int)PadLayout.Nintendo;
            bool sony = layout == (int)PadLayout.Sony;

            // 动作键菱形：手机屏中心 (0.85,0.60)，相邻键斜向中心距 148px（键直径 152）；弹窗面板
            // 宽高比偏小（h/w≈0.488），上 / 下键垂直归一化偏移单独加大，避免上下键与左右键斜向堆叠。
            // Xbox / 索尼 Y(△) 上、X(□) 左、B(○) 右、A(×) 下；任天堂 X 上、Y 左、A 右、B 下。
            if (nintendo)
            {
                list.Add(Slot(PadButtons.X, 0, 0.81f, 0.445f, 0.85f, 0.5031f));
                list.Add(Slot(PadButtons.Y, 0, 0.7464f, 0.58f, 0.8053f, 0.60f));
                list.Add(Slot(PadButtons.A, 0, 0.8736f, 0.58f, 0.8947f, 0.60f));
                list.Add(Slot(PadButtons.B, 0, 0.81f, 0.715f, 0.85f, 0.6969f));
            }
            else
            {
                list.Add(Slot(PadButtons.Y, 0, 0.81f, 0.445f, 0.85f, 0.5031f));
                list.Add(Slot(PadButtons.X, 0, 0.7464f, 0.58f, 0.8053f, 0.60f));
                list.Add(Slot(PadButtons.B, 0, 0.8736f, 0.58f, 0.8947f, 0.60f));
                list.Add(Slot(PadButtons.A, 0, 0.81f, 0.715f, 0.85f, 0.6969f));
            }

            // 左肩键 / 左扳机：左上竖排。
            list.Add(Slot(PadButtons.LeftShoulder, 0, 0.15f, 0.28f, 0.16f, 0.34f));
            list.Add(Slot(0, PadButtons.ModifierLeftTrigger, 0.15f, 0.16f, 0.16f, 0.20f));

            // 右肩键 / 右扳机：Xbox 右上横排（RB 左、RT 右）；索尼 / 任天堂右上竖排（扳机上、肩键下）。
            if (!sony && !nintendo)
            {
                list.Add(Slot(PadButtons.RightShoulder, 0, 0.76f, 0.16f, 0.76f, 0.20f));
                list.Add(Slot(0, PadButtons.ModifierRightTrigger, 0.88f, 0.16f, 0.90f, 0.20f));
            }
            else
            {
                list.Add(Slot(PadButtons.RightShoulder, 0, 0.86f, 0.28f, 0.88f, 0.34f));
                list.Add(Slot(0, PadButtons.ModifierRightTrigger, 0.86f, 0.16f, 0.88f, 0.20f));
            }

            // 十字键四向：弹窗内共享一体圆盘，四向区域按方向命中；手机屏紧凑十字排布，外观由预览层合并绘制。
            list.Add(Slot(PadButtons.DPadUp, 0, 0.35f, 0.58f, 0.34f, 0.50f));
            list.Add(Slot(PadButtons.DPadDown, 0, 0.35f, 0.58f, 0.34f, 0.74f));
            list.Add(Slot(PadButtons.DPadLeft, 0, 0.35f, 0.58f, 0.27f, 0.62f));
            list.Add(Slot(PadButtons.DPadRight, 0, 0.35f, 0.58f, 0.41f, 0.62f));

            // 摇杆下压键 LS / RS（小圆角矩形，位于十字键与右摇杆之间，与右侧装饰摇杆保持间隙）。
            list.Add(Slot(PadButtons.LeftThumb, 0, 0.45f, 0.62f, 0.45f, 0.70f));
            list.Add(Slot(PadButtons.RightThumb, 0, 0.52f, 0.62f, 0.53f, 0.70f));

            // 系统键：返回 / 视图 / 分享 / − 与 菜单 / 选项 / +，顶部居中。
            list.Add(Slot(PadButtons.Back, 0, 0.43f, 0.20f, 0.44f, 0.22f));
            list.Add(Slot(PadButtons.Start, 0, 0.53f, 0.20f, 0.54f, 0.22f));
            return list;
        }

        public static VirtualKey BuildKey(PadSlot slot, int layout)
        {
            VirtualKey key = new VirtualKey(slot.Symbol(layout), slot.ToBinding(), slot.PhoneX, slot.PhoneY);
            key.Size = 152;
            key.HideWithHidden = true;
            return key;
        }

        public static List<VirtualKey> BuildPreset(int layout)
        {
            List<PadSlot> slots = GetSlots(layout);
            List<VirtualKey> keys = new List<VirtualKey>();
            for (int i = 0; i < slots.Count; i++) keys.Add(BuildKey(slots[i], layout));
            return keys;
        }

        // 整套添加时确保主模式双摇杆启用并落到默认横屏位置；摇杆尺寸/死区/灵敏度等仍由摇杆卡片细调。
        public static void ApplyPresetJoysticks(ModeConfig mode)
        {
            if (mode == null) return;
            mode.ShowJoystick = true;
            if (mode.Joystick == null) mode.Joystick = new JoystickConfig();
            if (mode.JoystickR == null) mode.JoystickR = new JoystickConfig();
            mode.Joystick.Enabled = true;
            mode.JoystickR.Enabled = true;
            mode.Joystick.X = 0.16f;
            mode.Joystick.Y = 0.62f;
            mode.JoystickR.X = 0.66f;
            mode.JoystickR.Y = 0.62f;
            if (mode.Dpad == null) mode.Dpad = new DpadConfig();
            mode.Dpad.Enabled = true;
            mode.Dpad.X = 0.34f;
            mode.Dpad.Y = 0.62f;
            mode.Joystick.Normalize();
            mode.JoystickR.Normalize();
            mode.Dpad.Normalize();
        }
    }

    /// <summary>
    /// 配置持久化门面：定位 exe 同级目录与 games\ 资源目录，负责
    /// <see cref="AppConfig"/> 的 XML 加载、保存、默认值补齐（Normalize）、
    /// 损坏配置备份恢复，以及按键图 / 特征图等资源文件的复制与读取。
    /// </summary>
    public static class ConfigStore
    {
        public static readonly string ExeDir = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string ConfigPath = Path.Combine(ExeDir, "MobileUIConfig.xml");
        private static string _assetsDir;
        public static readonly string GamesDir = Path.Combine(ExeDir, "games");

        // 当前游戏配置的专属资源目录：特征图、剧情跳过图、按键图都保存在这里。
        // 默认是 exe 目录（未设置游戏名时的兼容行为）。
        public static string AssetsDir
        {
            get { return string.IsNullOrEmpty(_assetsDir) ? ExeDir : _assetsDir; }
        }

        public static string ImageDir
        {
            get { return Path.Combine(AssetsDir, "key_images"); }
        }

        // 自定义按键音效目录：wav/mp3 由配置面板复制到这里，与按键图资源同目录体系。
        public static string SoundsDir
        {
            get { return Path.Combine(AssetsDir, "sounds"); }
        }

        // 某个游戏配置对应的专属资源文件夹（与游戏名同名的目录）。
        public static string GameAssetsDir(string gameName)
        {
            string clean = gameName == null ? "" : gameName.Trim();
            if (clean.Length == 0) clean = "未保存游戏";
            string safe = ProfileStore.SafeFileName(clean);
            if (safe.Length == 0) safe = "未保存游戏";
            return Path.Combine(ExeDir, safe);
        }

        // 按顶部游戏配置的名称切换专属资源目录（每个游戏配置一个同名文件夹）。
        // 只在从旧版平铺根目录（exe 所在目录）首次升级时搬一次文件；
        // 游戏之间切换绝不搬动其它游戏文件夹里的文件，否则游戏资源会互相串扰。
        public static void SetAssetsDirForGame(string gameName)
        {
            string target = GameAssetsDir(gameName);
            if (string.Equals(AssetsDir.TrimEnd('\\', '/'), target.TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase))
                return;
            string old = AssetsDir;
            try { if (!Directory.Exists(target)) Directory.CreateDirectory(target); } catch { }
            _assetsDir = target;
            bool legacyRoot = string.Equals(old.TrimEnd('\\', '/'), ExeDir.TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
            if (legacyRoot) MigrateAssetFiles(old, target, true);
            // 旧版本把资源备份在 games/assets/<名称>，首次使用该文件夹时补灌缺失文件。
            string mirror = Path.Combine(GamesDir, "assets", Path.GetFileName(target));
            if (Directory.Exists(mirror)) MigrateAssetFiles(mirror, target, false);
        }

        // 游戏配置改名时同步改资源文件夹名（活动/非活动游戏都要处理）。
        public static void RenameAssetsFolder(string oldName, string newName)
        {
            string oldDir = GameAssetsDir(oldName);
            string newDir = GameAssetsDir(newName);
            if (string.Equals(oldDir.TrimEnd('\\', '/'), newDir.TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase))
                return;
            try
            {
                if (!Directory.Exists(oldDir)) return;
                bool moved = false;
                if (!Directory.Exists(newDir))
                {
                    try
                    {
                        Directory.Move(oldDir, newDir);
                        moved = true;
                    }
                    catch { }
                }
                // 目录改名失败（文件被占用等）或新目录已存在时，逐文件搬迁兜底，
                // 确保资源不会留在旧名字的文件夹里丢失关联。
                if (!moved) MigrateAssetFiles(oldDir, newDir, true);
            }
            catch (Exception ex)
            {
                Log("重命名游戏资源文件夹失败: " + ex.Message);
            }
            try
            {
                if (Directory.Exists(oldDir) &&
                    Directory.GetFiles(oldDir).Length == 0 &&
                    Directory.GetDirectories(oldDir).Length == 0)
                    Directory.Delete(oldDir);
            }
            catch { }
        }

        // 把 feature*/skip*/key_images 从 source 搬进 target；move=true 为移动（整理旧目录），
        // false 为复制（仅从镜像补灌缺失文件），已存在的文件不覆盖。
        private static void MigrateAssetFiles(string source, string target, bool move)
        {
            try
            {
                if (string.IsNullOrEmpty(source) || !Directory.Exists(source)) return;
                if (string.Equals(source.TrimEnd('\\', '/'), target.TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase)) return;
                string[] patterns = new string[] { "feature*.png", "skip*.png" };
                for (int p = 0; p < patterns.Length; p++)
                {
                    string[] files;
                    try { files = Directory.GetFiles(source, patterns[p]); }
                    catch { continue; }
                    for (int i = 0; i < files.Length; i++)
                    {
                        try
                        {
                            string dest = Path.Combine(target, Path.GetFileName(files[i]));
                            if (File.Exists(dest)) continue;
                            if (move) File.Move(files[i], dest);
                            else File.Copy(files[i], dest, false);
                        }
                        catch { }
                    }
                }
                string srcKeys = Path.Combine(source, "key_images");
                if (Directory.Exists(srcKeys))
                {
                    string dstKeys = Path.Combine(target, "key_images");
                    string[] files;
                    try { files = Directory.GetFiles(srcKeys); }
                    catch { files = new string[0]; }
                    if (files.Length > 0 && !Directory.Exists(dstKeys))
                    {
                        try { Directory.CreateDirectory(dstKeys); } catch { }
                    }
                    for (int i = 0; i < files.Length; i++)
                    {
                        try
                        {
                            string dest = Path.Combine(dstKeys, Path.GetFileName(files[i]));
                            if (File.Exists(dest)) continue;
                            if (move) File.Move(files[i], dest);
                            else File.Copy(files[i], dest, false);
                        }
                        catch { }
                    }
                    if (move)
                    {
                        try
                        {
                            if (Directory.GetFiles(srcKeys).Length == 0 &&
                                Directory.GetDirectories(srcKeys).Length == 0)
                                Directory.Delete(srcKeys);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool DeleteFile(string fileName);
        public static readonly string LogPath = Path.Combine(ExeDir, "MobileUIConfig.log");

        public static bool SaveTo(string path, AppConfig config)
        {
            try
            {
                config.Normalize();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                XmlSerializer serializer = new XmlSerializer(typeof(AppConfig));
                string tmp = path + ".tmp";
                using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    serializer.Serialize(fs, config);
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (Exception ex)
            {
                Log("保存游戏配置失败: " + ex.Message);
                return false;
            }
        }

        // Writes only when the serialized profile actually changed. Used when
        // switching games so an untouched game does not get rewritten.
        public static bool SaveToIfChanged(string path, AppConfig config)
        {
            try
            {
                config.Normalize();
                XmlSerializer serializer = new XmlSerializer(typeof(AppConfig));
                byte[] data;
                using (MemoryStream ms = new MemoryStream())
                {
                    serializer.Serialize(ms, config);
                    data = ms.ToArray();
                }
                if (File.Exists(path))
                {
                    byte[] old = File.ReadAllBytes(path);
                    if (old.Length == data.Length)
                    {
                        bool same = true;
                        for (int i = 0; i < old.Length; i++)
                        {
                            if (old[i] != data[i]) { same = false; break; }
                        }
                        if (same) return false;
                    }
                }
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                File.WriteAllBytes(tmp, data);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return true;
            }
            catch (Exception ex)
            {
                Log("保存游戏配置失败: " + ex.Message);
                return false;
            }
        }

        public static AppConfig LoadFrom(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                XmlSerializer serializer = new XmlSerializer(typeof(AppConfig));
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    AppConfig cfg = serializer.Deserialize(fs) as AppConfig;
                    if (cfg != null) cfg.Normalize();
                    return cfg;
                }
            }
            catch
            {
                return null;
            }
        }

        // 旧版全局特征图（仅用于版本 5 的迁移）。
        public static string LegacyFeaturePath(int index)
        {
            return Path.Combine(ExeDir, "feature" + (index + 1).ToString(CultureInfo.InvariantCulture) + ".png");
        }

        public static string FeaturePath(int mode, int slot)
        {
            return Path.Combine(AssetsDir, "feature" + (mode + 1).ToString(CultureInfo.InvariantCulture) +
                "_" + (slot + 1).ToString(CultureInfo.InvariantCulture) + ".png");
        }

        public static string StorySkipPath(int index)
        {
            return Path.Combine(AssetsDir, "skip" + (index + 1).ToString(CultureInfo.InvariantCulture) + ".png");
        }

        public static string StorySkipPath(int target, int image)
        {
            return Path.Combine(AssetsDir, "skip" + (target + 1).ToString(CultureInfo.InvariantCulture) +
                "_" + (image + 1).ToString(CultureInfo.InvariantCulture) + ".png");
        }

        public static void EnsureAssets()
        {
            try
            {
                // 首次启动自动解除 exe 的网络文件锁，避免后续每次启动都被 SmartScreen 拦截。
                try { DeleteFile(Application.ExecutablePath + ":Zone.Identifier"); } catch { }
                if (!Directory.Exists(ImageDir)) Directory.CreateDirectory(ImageDir);
                string iconPath = Path.Combine(ExeDir, "app.ico");
                if (!File.Exists(iconPath))
                {
                    using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                    {
                        if (stream != null)
                        {
                            using (FileStream fs = new FileStream(iconPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                byte[] buffer = new byte[8192];
                                int read;
                                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) fs.Write(buffer, 0, read);
                            }
                        }
                    }
                }
                if (!File.Exists(ConfigPath))
                {
                    AppConfig def = AppConfig.CreateDefault();
                    Save(def);
                }
            }
            catch (Exception ex)
            {
                Log("首次释放资源失败: " + ex.Message);
            }
        }

        public static Image LoadFeatureImage(int mode, int slot)
        {
            try
            {
                string path = FeaturePath(mode, slot);
                if (!File.Exists(path)) return null;
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (Image source = Image.FromStream(fs))
                {
                    return new Bitmap(source);
                }
            }
            catch
            {
                return null;
            }
        }

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(AppConfig));
                    using (FileStream fs = new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        AppConfig cfg = serializer.Deserialize(fs) as AppConfig;
                        if (cfg != null)
                        {
                            cfg.Normalize();
                            return cfg;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("读取配置失败: " + ex.Message);
                try
                {
                    if (File.Exists(ConfigPath))
                    {
                        string backup = ConfigPath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                        File.Copy(ConfigPath, backup, true);
                    }
                }
                catch
                {
                }
            }

            AppConfig def = AppConfig.CreateDefault();
            Save(def);
            return def;
        }

        public static bool Save(AppConfig config)
        {
            try
            {
                config.Normalize();
                XmlSerializer serializer = new XmlSerializer(typeof(AppConfig));
                string tmp = ConfigPath + ".tmp";
                using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    serializer.Serialize(fs, config);
                }
                if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
                File.Move(tmp, ConfigPath);
                return true;
            }
            catch (Exception ex)
            {
                Log("保存配置失败: " + ex.Message);
                return false;
            }
        }

        public static string ImportImage(string sourceFile)
        {
            try
            {
                if (!Directory.Exists(ImageDir)) Directory.CreateDirectory(ImageDir);
                string ext = Path.GetExtension(sourceFile);
                if (string.IsNullOrEmpty(ext)) ext = ".png";
                string name = "key_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" +
                              Guid.NewGuid().ToString("N").Substring(0, 6) + ext.ToLowerInvariant();
                string dest = Path.Combine(ImageDir, name);
                File.Copy(sourceFile, dest, true);
                return name;
            }
            catch (Exception ex)
            {
                Log("导入图片失败: " + ex.Message);
                return "";
            }
        }

        public static string GetImagePath(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "";
            string full = Path.Combine(ImageDir, fileName);
            return File.Exists(full) ? full : "";
        }

        public static void DeleteImage(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName)) return;
                string full = Path.Combine(ImageDir, Path.GetFileName(fileName));
                if (File.Exists(full)) File.Delete(full);
            }
            catch
            {
            }
        }

        // 自定义按键音效：复制到 sounds 目录，配置里只存文件名。
        public static string ImportSound(string sourceFile)
        {
            try
            {
                if (!Directory.Exists(SoundsDir)) Directory.CreateDirectory(SoundsDir);
                string ext = Path.GetExtension(sourceFile);
                if (string.IsNullOrEmpty(ext)) ext = ".wav";
                ext = ext.ToLowerInvariant();
                if (ext != ".wav" && ext != ".mp3" && ext != ".wma" && ext != ".m4a" && ext != ".ogg")
                    ext = ".wav";
                string name = "sound_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" +
                              Guid.NewGuid().ToString("N").Substring(0, 6) + ext;
                string dest = Path.Combine(SoundsDir, name);
                File.Copy(sourceFile, dest, true);
                return name;
            }
            catch (Exception ex)
            {
                Log("导入音效失败: " + ex.Message);
                return "";
            }
        }

        public static string GetSoundPath(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "";
            string full = Path.Combine(SoundsDir, Path.GetFileName(fileName));
            return File.Exists(full) ? full : "";
        }

        public static void DeleteSound(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName)) return;
                string full = Path.Combine(SoundsDir, Path.GetFileName(fileName));
                if (File.Exists(full)) File.Delete(full);
            }
            catch
            {
            }
        }

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + "\r\n");
            }
            catch
            {
            }
        }

        public static System.Drawing.Image LoadImageCopy(string fileName)
        {
            string path = GetImagePath(fileName);
            if (path.Length == 0) return null;
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (System.Drawing.Image tmp = System.Drawing.Image.FromStream(fs))
                {
                    return new System.Drawing.Bitmap(tmp);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
