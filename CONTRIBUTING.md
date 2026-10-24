# 贡献指南

感谢你对本项目感兴趣。本仓库是个人开发者封存的 v1.0.0 作品，作者**不承诺持续维护**，
但仍欢迎 issue 反馈与改进型 PR。提交前请先阅读本说明。

## 能提交什么

- Bug 复现步骤清晰的 issue（附上操作系统版本、两个版本中哪一个、相关日志 `MobileUIConfig.log`）；
- 不破坏现有行为的小修复、界面文案与文档改进；
- 补充注释、整理目录结构、完善 README / docs 的 PR。

## 不建议提交

- 要求适配某款新游戏、新系统的定制化功能；
- 绕过游戏反作弊、修改游戏内存或破坏公平竞技的功能；
- 大幅重构架构、更换 UI 框架或升级语言版本的改动（本项目标准版受 C# 5 语法面约束）。

## 开发环境

- 标准版：Windows + .NET Framework 4.8 自带 `csc.exe`，无需 Visual Studio；
- 独立版：.NET 8 SDK；
- 建议使用 Visual Studio 2022 / Rider / VS Code，仓库内 `.editorconfig` 已统一缩进、换行与命名风格，编辑器应自动遵守。

## 本地编译验证

提交 PR 前请确保两个版本都能编译通过：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-standard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-selfcontained.ps1
```

- 标准版已知不阻断警告：`Controls.cs` 中 `CS0162 检测到无法访问的代码`；
- 独立版已知不阻断警告：`WFAC010`（manifest 高 DPI 设置提示）、离线环境下的 NuGet 元数据拉取提示；
- 不要在 PR 中引入新的编译错误或阻断性警告。

## 代码约定

- 缩进 4 个空格，换行 CRLF，UTF-8 编码（见 `.editorconfig`）；
- 不改动现有公开类型、字段与 XML 序列化节点名，避免旧配置文件无法加载；
- 虚拟按键页与虚拟手柄页配置根保持解耦，不要把两套设置重新耦合到同一字段；
- 新增配置字段时在 `Models.cs` 中给出默认值，并在 `Normalize()` 中补齐旧配置缺失字段；
- 注释使用简体中文，核心类型与关键方法补中文行注释即可，不必逐行铺陈。

## 提交信息

- 使用简体中文或英文短句，说明「改了什么、为什么改」；
- 一次提交只做一件事，避免把功能改动与格式化混在一起；
- 建议前缀：`fix:` 修复、`feat:` 新功能、`docs:` 文档、`refactor:` 重构、`chore:` 工程杂项。

## 协议

提交的代码视为在 **GPL-3.0** 协议下授权给本项目；你保留自己代码的著作权，
但合并后的整体作品继续受 GPL-3.0 约束，二次分发必须保持开源并提供源码。
