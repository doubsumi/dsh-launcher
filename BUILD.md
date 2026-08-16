# 构建指南（BUILD.md）

本文件说明如何从源码编译 `DeepSeekHarnessLauncher.exe`。项目采用 **C# + .NET
Framework 4.x**，使用 Windows 自带的 **csc.exe** 编译器，**无需安装任何 SDK**。

---

## 1. 概述

`DeepSeekHarnessLauncher.exe` 是一个**瘦启动器**（GUI 子系统，winexe）：

- 解析命令行（`--visible` / `--monitor <port> <idle>`）；
- 通用查找 dsh（PATH → `npm prefix -g` → `%APPDATA%\npm`）；
- 未安装 dsh 时弹窗询问是否 `npm install -g @deepseek-ai/dsh`；
- 最终调用同目录的 `start-dsh.cmd` 完成实际启动/停止逻辑。

因此 exe **依赖同目录下的脚本**（`start-dsh.cmd` 等），分发时必须整目录打包（见 §8）。

技术选型理由：

| 项 | 选择 | 原因 |
|---|---|---|
| 语言 | C#（.NET Framework 4.x） | Windows 10/11 内置 .NET Framework 4.8，**零运行时依赖、免安装** |
| 编译器 | csc.exe（随 .NET Framework 发布） | 系统自带，无需 Visual Studio / .NET SDK |
| 子系统 | `/target:winexe` | GUI 程序：双击不弹黑窗口 |
| 目标平台 | x64（`Framework64` 路径的 csc） | 现代 Windows 均为 64 位 |

---

## 2. 工具链

### 2.1 csc.exe（本方案使用）

路径（64 位系统）：

```text
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

32 位系统对应：`C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe`

验证可用性：

```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /?
```

### 2.2 其它可用工具链（备选）

| 工具 | 说明 |
|---|---|
| Visual Studio | 新建“类库/控制台应用”项目，目标框架选 .NET Framework 4.x，编译产物相同 |
| .NET SDK（Roslyn） | 若装有 `dotnet` SDK，可用其自带的 `csc.dll` 或 `dotnet build`（需配 .csproj，见 §7） |

本项目为单文件源码，直接用 csc.exe 最简。

---

## 3. 编译命令（本地，与 CI 完全一致）

在项目根目录（`Launcher.cs` 所在目录）执行：

```powershell
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

& $csc /nologo `
  /target:winexe `
  /win32icon:assets\deepseek-whale.ico `
  /out:DeepSeekHarnessLauncher.exe `
  /r:System.dll `
  /r:System.Windows.Forms.dll `
  Launcher.cs
```

CMD 等价形式：

```cmd
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /win32icon:assets\deepseek-whale.ico /out:DeepSeekHarnessLauncher.exe /r:System.Windows.Forms.dll /r:System.dll Launcher.cs
```

> 也可用响应文件（.rsp）组织参数，见 §9.2。

### 3.1 一键构建（可保存为 `build.cmd` 使用）

```cmd
@echo off
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /win32icon:assets\deepseek-whale.ico /out:DeepSeekHarnessLauncher.exe /r:System.Windows.Forms.dll /r:System.dll Launcher.cs
if errorlevel 1 ( echo BUILD FAILED & exit /b 1 )
echo OK: DeepSeekHarnessLauncher.exe
```

---

## 4. 参数详解

| 参数 | 作用 |
|---|---|
| `/nologo` | 不打印编译器横幅 |
| `/target:winexe` | 生成 Windows GUI 可执行文件（无控制台）。若用 `/target:exe` 会附带黑窗口 |
| `/win32icon:<路径>` | 把图标文件嵌入 PE 资源（本项目嵌入官方黑色鲸鱼图标，含 16–256 多尺寸） |
| `/out:<路径>` | 输出文件名 |
| `/r:<程序集>` | 引用程序集：`System.dll`（基础类）、`System.Windows.Forms.dll`（MessageBox 弹窗） |
| `/optimize+` | 启用优化（发布时建议，见 §9.1） |
| `/platform:x64` | 显式指定平台（可选；Framework64 的 csc 默认 AnyCPU，在 64 位进程按 64 位运行） |
| `/langversion:7.3` | 显式指定语言版本（可选；默认即可） |

---

## 5. 验证产物

```powershell
# 1) 文件存在且非空
Get-Item .\DeepSeekHarnessLauncher.exe

# 2) 提取内嵌图标（应得到黑色鲸鱼图标）
Add-Type -AssemblyName System.Drawing
[System.Drawing.Icon]::ExtractAssociatedIcon("$PWD\DeepSeekHarnessLauncher.exe")

# 3) 查看 PE 头（MZ 魔数）
[System.IO.File]::ReadAllBytes("$PWD\DeepSeekHarnessLauncher.exe")[0..1]   # 应为 77 90 ("MZ")
```

预期产物约 **25 KB**。

---

## 6. CI 构建（GitHub Actions）

仓库内 `.github/workflows/build.yml` 在 `windows-latest` 上执行**与本地完全相同的
csc 命令**，产出 exe 并上传为 artifact，同时作为仓库的 **Build 徽章**来源：

```yaml
- name: Build DeepSeekHarnessLauncher.exe
  shell: cmd
  run: |
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /win32icon:assets\deepseek-whale.ico /out:DeepSeekHarnessLauncher.exe /r:System.Windows.Forms.dll /r:System.dll Launcher.cs
```

> 注意：exe 在 `.gitignore` 中（构建产物不入库），CI 从源码实时构建——保证“源码可复现构建”。

---

## 7. 若改用 .NET SDK / Visual Studio

若团队后续统一用 .NET SDK，可加一个最小 `.csproj`（不改变现有源码）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <OutputType>WinExe</OutputType>
    <ApplicationIcon>assets\deepseek-whale.ico</ApplicationIcon>
    <AssemblyName>DeepSeekHarnessLauncher</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="System.Windows.Forms" />
    <Reference Include="System" />
  </ItemGroup>
</Project>
```

```powershell
dotnet build -c Release
```

产物：`bin\Release\net48\DeepSeekHarnessLauncher.exe`。

---

## 8. 发布与分发（重要）

**exe 不能单独发布**——它运行时依赖同目录的脚本（`start-dsh.cmd`、`stop-dsh.cmd`、
`open-ui.cmd`、`setup.cmd`、`setup-helpers.ps1`、`assets\` 等）。正确做法二选一：

### 8.1 ZIP 便携包（最简单）

打包整个项目目录（**排除** `logs/`、`.git/`、`*.exe` 旧产物）：

```powershell
Compress-Archive -Path .\DeepSeekHarnessLauncher.exe, Launcher.cs, *.cmd, *.ps1, assets, README.md, LICENSE -DestinationPath DeepSeekHarnessLauncher.zip
```

用户解压后运行 `setup.cmd` 即可。

### 8.2 Inno Setup 安装包（推荐正式分发）

在 Inno Setup 脚本中把整个目录安装到
`{userappdata}\DeepSeekHarnessLauncher`（或 `{pf}`），安装完成后
`Run: "{app}\setup.cmd"` 自动创建桌面快捷方式与右键菜单；卸载时
`Run: "{app}\uninstall.cmd"` 恢复系统。Inno Setup 脚本示例见 §9.4。

---

## 9. 进阶

### 9.1 发布优化

```powershell
& $csc /nologo /target:winexe /optimize+ /win32icon:assets\deepseek-whale.ico /out:DeepSeekHarnessLauncher.exe /r:System.Windows.Forms.dll /r:System.dll Launcher.cs
```

### 9.2 响应文件（.rsp）

把所有参数写进 `Launcher.rsp`：

```text
/nologo
/target:winexe
/optimize+
/win32icon:assets\deepseek-whale.ico
/out:DeepSeekHarnessLauncher.exe
/r:System.dll
/r:System.Windows.Forms.dll
Launcher.cs
```

然后：`csc.exe @Launcher.rsp`

### 9.3 版本信息（AssemblyInfo）

csc 直接编译不嵌入文件版本，可在源码加特性，或创建 `AssemblyInfo.cs` 一起编译：

```csharp
using System.Reflection;
[assembly: AssemblyTitle("DeepSeek Harness Launcher")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyCompany("dsh-launcher")]
```

```powershell
& $csc ... Launcher.cs AssemblyInfo.cs
```

### 9.4 Inno Setup 最小脚本（示例）

```ini
[Setup]
AppName=DeepSeek Harness Launcher
AppVersion=1.0.0
DefaultDirName={userappdata}\DeepSeekHarnessLauncher
OutputBaseFilename=DeepSeekHarnessLauncher-Setup

[Files]
Source: "*"; DestDir: "{app}"; Excludes: "logs,*.zip,.git"; Flags: recursesubdirs

[Run]
Filename: "{app}\setup.cmd"; Description: "Create desktop shortcut"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\uninstall.cmd"; Flags: runhidden
```

---

## 10. 常见问题

- **找不到 csc.exe**：确认已安装 .NET Framework 4.x（Win10/11 默认自带）；
  或用 §7 的 .NET SDK 路线。
- **编译报 CS0246（找不到命名空间 System.Windows.Forms）**：缺少 `/r:System.Windows.Forms.dll`。
- **exe 双击没反应**：先确认同目录存在 `start-dsh.cmd`；exe 只负责调度。
