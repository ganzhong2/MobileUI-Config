# 从源码编译

仓库提供两套并行的打包方式，产物功能一致，仅运行时与体积不同。

## 1. 前置条件

| 版本 | 需要的环境 | 说明 |
| --- | --- | --- |
| 标准版 | Windows + .NET Framework 4.8 自带 `csc.exe` | 不需要 Visual Studio、不需要 NuGet 还原 |
| 独立版 | .NET 8 SDK（`dotnet --version` 能输出 8.x） | 首次 `dotnet publish` 会还原少量 Windows Forms 引用程序集 |

两个版本共用 `src/` 下同一套源码，区别只在工程文件与打包脚本。

## 2. 标准版（.NET Framework 4.8）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-standard.ps1
```

脚本行为：

1. 定位 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（64 位不存在时回退 32 位目录）；
2. 纳入 `src/` 目录下全部 `*.cs` 文件（`.bak` / `.old` 等非 `.cs` 文件不参与编译）；
3. 引用 `System`、`System.Core`、`System.Drawing`、`System.Windows.Forms`、`System.Xml`、
   `System.IO.Compression`、`System.IO.Compression.FileSystem`；
4. 以 `/target:winexe /platform:anycpu /optimize+` 编译，附加 `app.ico` 图标、嵌入资源与 `app.manifest`；
5. 产物输出到 `dist/v1.0.0/MobileUI-Config-Standard-v1.0.0.exe`；
6. 复制 `libs/ViGEmClient.dll` 到发布目录，并解除本地构建产物的 Mark-of-the-Web 标记。

### 标准版语法约束

`csc.exe` 是 .NET Framework 4 自带编译器，对应 **C# 5** 语法面。源码中禁止使用：

- `?.` 空条件运算符、`??=` 空合并赋值；
- 表达式成员（`public int X => ...`）、局部函数；
- `nameof(...)`；
- 内联 `out` 变量（`if (int.TryParse(s, out var v))`）；
- `Math.Clamp` 等高版本 BCL API；
- 元组、`async/await` 之外的后续语言特性。

独立版工程文件使用 `LangVersion=latest`，不受此约束，但为保证两套源码一致，新代码仍应遵守上述约束。

## 3. 独立版（.NET 8 自包含单文件）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-selfcontained.ps1
```

脚本行为：

1. 清理中间目录 `publish_selfcontained/`；
2. 执行 `dotnet publish MobileUiConfig.SelfContained.csproj -c Release -r win-x64 --self-contained true`，
   开启 `PublishSingleFile`、`IncludeNativeLibrariesForSelfExtract`、`EnableCompressionInSingleFile`；
3. 从发布目录选取主程序 exe，复制为 `dist/v1.0.0/MobileUI-Config-SelfContained-v1.0.0.exe`；
4. 复制 `libs/ViGEmClient.dll` 到发布目录。

工程文件关键设置见 `MobileUiConfig.SelfContained.csproj`：

- `TargetFramework=net8.0-windows`、`UseWindowsForms=true`、`PlatformTarget=x64`；
- `RuntimeIdentifier=win-x64`，仅生成 64 位 Windows 版本；
- `DebugType=none`、`DebugSymbols=false`，产物不携带调试符号；
- `NoWarn` 压制既有不阻断警告：`CS0162`、`CS0168`、`CS0219`、`CS0649`、`CS1591`。

## 4. 产物清单（`dist/v1.0.0/`）

| 文件 | 说明 |
| --- | --- |
| `MobileUI-Config-Standard-v1.0.0.exe` | 标准版程序，依赖系统 .NET Framework 4.8 |
| `MobileUI-Config-SelfContained-v1.0.0.exe` | 独立版程序，内置 .NET 8 运行时 |
| `ViGEmClient.dll` | ViGEm 客户端原生库，两个版本都需要，必须与 exe 同目录 |

## 5. 常见问题

### 找不到 csc.exe

安装 .NET Framework 4.8 Developer Pack 或运行库；`csc.exe` 随 .NET Framework 4 自带，
位于 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`。

### dotnet publish 提示 NuGet 还原失败

离线环境下偶发元数据拉取警告，不阻断编译；如确为网络问题，配置可用的 NuGet 源后重试。

### 独立版体积很大

自包含单文件会打包整个 .NET 8 运行时，约 63 MB 属正常现象；需要小体积请使用标准版。

### 修改了源码但产物没更新

确认 `dist/v1.0.0/` 内 exe 的修改时间；两个脚本都会覆盖同名产物。`bin/`、`obj/`、
`publish_selfcontained/` 均为中间目录，已被 `.gitignore` 忽略，可随时删除后重新编译。
