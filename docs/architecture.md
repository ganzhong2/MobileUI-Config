# 架构说明

## 1. 总体分层

程序是一个 C# WinForms 单进程桌面应用，按职责分为五层，源码全部位于 `src/`：

```text
┌─────────────────────────────────────────────────────────────┐
│  设置界面层（MainForm + 各 SettingsPanel + EditorPanel）        │
│  读取模型 → 渲染卡片面板 → 用户修改 → 回写模型 → 触发保存        │
├─────────────────────────────────────────────────────────────┤
│  预览层（PhonePreview、ModeVisibilityStrip、各 Pad* 面板）       │
│  手机框内实时预览布局、拖拽定位、选中高亮，与真机悬浮层互不耦合   │
├─────────────────────────────────────────────────────────────┤
│  真机悬浮层（Overlay.cs 内各 Overlay*Form）                     │
│  游戏前台时创建置顶窗口，处理按压 / 长按 / 双击 / 摇杆 / 视角     │
├─────────────────────────────────────────────────────────────┤
│  输入注入层（InputSender、GamepadService、InputAssistService）  │
│  把交互事件转换为 SendInput / PostMessage / ViGEm 手柄事件      │
├─────────────────────────────────────────────────────────────┤
│  模型与服务层（Models、ConfigStore、RuntimeService、各 Scan）    │
│  配置持久化、进程 / 串流检测、图片识别、内存扫描、剧情跳过、震动  │
└─────────────────────────────────────────────────────────────┘
```

## 2. 数据流

1. **加载**：程序启动由 `Program.cs` 做单实例校验，`MainForm` 调用 `ConfigStore.Load()` 反序列化 `AppConfig`；配置缺失字段由 `Normalize()` 补齐，损坏文件先备份再回退默认。
2. **绑定**：`MainForm` 组装标题栏、游戏库栏、启动卡片、预览框与右侧各设置面板；`EditorPanel` 把当前模式 / 选中按键双向绑定到设置卡片。
3. **编辑**：用户在预览框拖拽控件调整位置，或在右侧面板修改绑定、尺寸、透明度；改动写回 `AppConfig` 对应对象，松开控件或切换标签时自动保存。
4. **运行时**：`RuntimeService` 轮询游戏进程与串流会话，判断游戏是否处于前台；前台时 `RuntimeOverlay` 创建 / 重建悬浮窗口，后台时 `SetSuspended` 销毁窗口并释放输入。
5. **注入**：悬浮层控件识别按压手势后调用 `InputSender.SendBinding`，分发为键盘 / 鼠标 / 滚轮事件；手柄绑定转交 `GamepadService` 上报 ViGEm 虚拟手柄。

## 3. 两套页面解耦设计

虚拟按键页与虚拟手柄页是两个**独立配置根**，不共用设置卡片与参数：

- `MainForm.Root` 按 `_padSide` 返回 `_config`（按键页）或 `_config.Gamepad`（手柄页）；
- 摇杆、方向键、系统显示键 / 模式键动作、视角操控等参数分别挂在各自配置根下；
- 图片识别项目 `ImageProjects`、内存扫描项目 `MemoryProjects` 同样挂在当前页面配置根上；项目列表与界面预览的 `Modes` 相互独立，仅项目内 `TargetMode` 指向目标模式；
- 切换页面时只换绑上下文根，不复制、不共享字段；
- 预览层选中态、悬浮层控件、设置面板卡片都跟随当前页面上下文重建。

## 4. 关键模块职责表

| 文件 | 职责 |
| --- | --- |
| `Program.cs` | 程序入口、单实例互斥锁、后台启动参数解析、悬浮层测试参数 |
| `Models.cs` | 全部配置实体、枚举、默认布局、归一化与 XML 序列化结构（数据核心） |
| `ConfigStore`（位于 `Models.cs`） | XML 加载 / 保存、默认值补齐、损坏备份、资源目录定位与日志写入 |
| `MainForm.cs` | 主设置窗口、面板装配、页面切换、游戏启动、托盘菜单、前后台联动 |
| `EditorPanel.cs` | 右侧卡片编辑器：模式 / 叠加模式 / 选中按键绑定、卡片显隐与刷新 |
| `PhonePreview.cs` | 手机框预览、横竖屏、按键与摇杆拖拽、模式菜单、选中高亮 |
| `Overlay.cs` | 真机悬浮层总控 `RuntimeOverlay` 与按键 / 系统键 / 摇杆 / 视角 / 菜单 / 提示窗各子类 |
| `InputSender.cs` | 键盘 / 鼠标 / 滚轮事件注入、按压状态跟踪、抑制键表、残留按键兜底释放 |
| `GamepadService.cs` | ViGEm 客户端动态加载、Xbox360 / DS4 目标创建、按钮 / 扳机 / 轴上报、通道切换 |
| `RuntimeService.cs` | 游戏进程监控、串流会话检测、前台 / 后台状态判定、开机启动项管理 |
| `StreamDetector.cs` | 串流进程名单匹配 + Established TCP 连接识别 |
| `FeatureAuto.cs` | 特征图框选截图、彩色 HSV + 结构梯度双路融合多尺度匹配、锚点邻域搜索、命中状态机与自动切换 |
| `MemoryScanService.cs` | 内存指针链读取、基址 / 偏移 / 目标值判定、命中切换 |
| `CooldownScanService.cs` | 技能冷却扫描与结果展示 |
| `StorySkipService.cs` | 剧情参考图匹配、目标顺序 / 随机推进、自动点击 |
| `InputAssistService.cs` | 鼠标 / 触摸辅助钩子、内部事件标记放行、空白区域穿透与拦截判定 |
| `RumbleService.cs` | XInput 震动槽位探测、马达节奏播放与状态归零 |
| `ProfileStore.cs` | 多游戏配置保存、`.mobui` 分享包压缩导出与导入恢复 |
| `JoystickMath.cs` | 摇杆死区剔除、曲线变形、灵敏度缩放的共享计算，预览与真机同一口径 |
| `PadCatalog`（位于 `Models.cs`） | 三套厂商布局的按钮位码、修饰位与弹窗 / 手机画面归一化坐标 |
| `Theme.cs` | 配色、字号缩放、圆角绘制与通用视觉规范 |
| `Controls.cs` | 开关、滑块、步进器、选择器、按键录制框、滚动面板等基础控件 |

## 5. 生命周期与线程要点

- 全部 UI 控件运行在主窗体 UI 线程；后台轮询服务在计时器回调中采集状态，需要刷新界面时回到 UI 线程。
- 悬浮层窗口以 `ShowNoActivate` 方式显示，不抢占游戏焦点；`ReassertTopMost` 定时重断言置顶层级。
- `InputSender` 内部用锁保护按压状态集合；`ReleaseAll` 在挂起、退出、后台切换时统一兜底释放键盘、鼠标与手柄，避免修饰键卡在按下态。
- 配置只在真正发生变化时写入磁盘；切换游戏标签前自动保存当前配置。

## 6. 扩展指引

- 新增一个虚拟按键参数：先在 `Models.cs` 的 `VirtualKey` / `ModeConfig` 增加字段与默认值，再在 `EditorPanel.cs` 对应卡片补双向绑定，最后在 `Overlay.cs` 的 `OverlayKeyForm` 与 `PhonePreview.cs` 读取同一字段渲染。
- 新增一个设置服务：参照 `MemoryScanService.cs` 的进程检测、命中判定与冷却状态机写法，在 `MainForm` 生命周期里注册启停。
- 新增手柄按键：在 `PadCatalog` 登记位码与坐标，在 `GamepadService` 补对应上报路径，不要在两个页面硬编码位码。
