# 第三方组件与许可声明

本仓库自身源码以 **GPL-3.0** 发布；仓库内附带的第三方组件遵循其上游原始许可，
本节逐项说明归属、用途与许可口径。

## 1. ViGEm 虚拟手柄组件

### 1.1 随仓携带的原生库

| 文件 | 来源 | 用途 |
| --- | --- | --- |
| `libs/ViGEmClient.dll` | ViGEm 项目客户端库（[ViGEm/ViGEmClient](https://github.com/ViGEm/ViGEmClient)） | 动态加载后创建 Xbox 360 / DualShock 4 虚拟手柄目标，上报按钮、扳机与摇杆轴 |

- 该 DLL 为上游官方编译产物，本项目未修改其二进制内容；
- 客户端库的具体许可条款以 ViGEm 上游仓库 `LICENSE` 文件为准；
- 本项目源码通过 `DllImport` / 动态加载方式调用其导出函数，未将其源码并入本仓库。

### 1.2 需要用户自行安装的总线驱动

使用虚拟手柄功能前，用户需在系统中安装 **ViGEmBus 总线驱动**
（[nefarius/ViGEmBus](https://github.com/nefarius/ViGEmBus)），该驱动由用户从上游
Releases 独立安装，**不随本仓库分发**。

- ViGEmBus 驱动采用 **BSD 3-Clause License**；
- BSD 3-Clause 的主要义务：保留版权声明与免责条款、不得使用上游项目或贡献者名义为
  本软件做背书宣传；上游按「现状」提供软件，不承担担保责任；
- 驱动的安装、卸载与版本升级由用户自行管理，本项目不对驱动兼容性与稳定性做担保。

### 1.3 归属与致谢

ViGEm 项目由 Nefarius Software Solutions / Benjamin Höglinger-Schneider 及社区贡献者开发维护，
特此致谢。本项目与 ViGEm 团队无隶属关系，仅作为其下游调用方。

## 2. Windows 系统 API

源码中通过 `DllImport` 调用的 `user32.dll`、`kernel32.dll`、`dwmapi.dll`、`uxtheme.dll`、`iphlpapi.dll`、`winmm.dll`
等均为 **Microsoft Windows 操作系统自带系统组件**，不属于第三方分发软件，其使用遵循
Windows 系统许可与开发人员使用条款，无需在本仓库内单独附带许可文本。

## 3. .NET / Windows Forms 框架

- 标准版编译目标为 .NET Framework 4.8（Windows 系统组件）；
- 独立版基于 .NET 8 Windows Forms 构建，运行时与框架组件遵循 .NET 官方许可；
- 本仓库不重新分发上述框架的安装包或运行时文件。

## 4. 截图与素材

- `screenshots/` 目录内程序界面截图为本项目自行截取；
- 界面中出现的游戏名称、画面、商标与标识版权归各自权利人所有，本项目与任何游戏厂商、
  平台无关联；
- 按键图标（透明 PNG）由用户在使用过程中自行导入，不随源码仓库分发。

## 5. 许可兼容性说明

- 本项目整体源码以 GPL-3.0 发布；与 BSD 3-Clause 等宽松许可的第三方组件兼容，
  下游可在遵守各自上游条款的前提下合并使用；
- 二次分发本软件时，必须同时满足 GPL-3.0 与上述第三方组件的原始许可义务：
  保留 ViGEm 版权与免责声明、按要求署名，不得移除本仓库 `LICENSE` 中的 GPL-3.0 文本。
