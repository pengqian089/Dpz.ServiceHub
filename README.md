# Dpz.ServiceHub

一个基于 **Avalonia** 的跨平台桌面工具，用于 **本地开发环境服务管理与实时日志观测**。  
帮助开发者快速启动、停止、监控多个服务（ASP.NET Core、控制台程序、PowerShell脚本等），并通过内置的 **xterm.js** 终端实时查看带 ANSI 颜色的日志输出。

---

## ✨ 项目简介

在开发环境中，通常需要同时启动多个服务（Web、API、任务调度、认证中心、缓存服务等）。  
手动在多个终端窗口中启动这些项目不仅繁琐，还难以集中监控日志输出和服务状态。

**Dpz.ServiceHub** 旨在解决这一痛点：  
- 提供统一的桌面控制面板，集中管理所有开发服务
- 使用 WebView + xterm.js 实现专业级终端体验，完整支持 ANSI 转义序列（颜色、样式、256色、RGB真彩色）
- 一键启动/停止单个或全部服务
- 实时查看彩色日志输出，就像在真实终端中一样
- 托盘图标支持，最小化后不占用任务栏

---

## 🚀 功能特性

### 核心功能

| 功能 | 描述 |
|------|------|
| **服务管理** | 启动、停止、重启单个或全部服务，支持自动启动配置 |
| **专业终端** | 基于 xterm.js 的 WebView 终端，完整支持 ANSI 转义序列 |
| **ANSI 颜色支持** | 显示彩色日志（16色、256色、RGB真彩色）、文本样式（粗体、斜体、下划线等） |
| **实时日志输出** | 增量更新机制，大量日志输出无闪烁，性能优异 |
| **状态监控** | 实时显示服务运行状态（Running / Stopped / Starting） |
| **命令交互** | 支持向服务进程发送命令或输入 |
| **配置管理** | 可视化配置服务路径、启动参数、工作目录、环境变量、端口等 |
| **托盘图标** | 最小化到系统托盘，支持快速显示/退出 |
| **外部进程检测** | 自动检测外部启动的服务进程并关联监控 |

### 终端特性

| 特性 | 说明 |
|------|------|
| **VS Code 风格滚动条** | 半透明滚动条，暗色主题友好，支持悬停高亮 |
| **文本选择与复制** | 选中文本后按 **Ctrl+C** 即可复制（无需右键菜单） |
| **URL 自动识别** | 自动识别并高亮显示 URL，可点击打开 |
| **10000 行缓冲** | 支持大量历史日志滚动查看 |
| **清空输出** | 一键清空当前服务的日志输出 |
| **防抖机制** | 50ms 防抖延迟，避免快速更新导致的闪烁和重复 |

### 环境变量自动配置

为确保各类程序正确输出 ANSI 颜色，ServiceHub 会自动设置以下环境变量：

**通用设置**：
- `TERM=xterm-256color`
- `CLICOLOR_FORCE=1`
- `FORCE_COLOR=1`

**.NET / ASP.NET Core**：
- `DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION=1`
- `Logging__Console__FormatterName=Simple`
- `Logging__Console__FormatterOptions__ColorBehavior=Enabled`
- `ASPNETCORE_LOGGING__CONSOLE__DISABLECOLORS=false`

**PowerShell**：
- 自动设置 `[Console]::OutputEncoding = UTF-8`
- 避免中文乱码

---

## 🧩 支持的服务类型

ServiceHub 设计为通用的进程管理工具，可以启动和监控任何类型的命令行程序：

| 服务类型 | 示例 | 着色支持 |
|----------|------|----------|
| **ASP.NET Core 应用** | Web API、MVC、Blazor、Minimal API | ✅ 完整支持（需配置 Simple Formatter） |
| **.NET 控制台程序** | Garnet、自定义工具 | ✅ 完整支持 |
| **PowerShell 脚本** | 测试脚本、自动化任务 | ✅ 完整支持（自动 UTF-8 编码） |
| **Node.js 应用** | Express、NestJS、React 开发服务器 | ✅ 完整支持 |
| **Python 应用** | FastAPI、Django、Flask | ✅ 完整支持 |
| **Java 应用** | Spring Boot、Tomcat | ✅ 完整支持 |
| **任意可执行文件** | exe、bat、cmd、sh | ✅ 取决于程序本身 |

### 示例服务配置

**Garnet（Redis 替代）**：
- 可执行文件: `C:\Tools\Garnet\garnet.exe`
- 参数: `--port 6379`
- 工作目录: `C:\Tools\Garnet`

**ASP.NET Core Web API**：
- 可执行文件: `dotnet`
- 参数: `run --no-build`
- 工作目录: `C:\Projects\MyApi`
- 端口: `5000, 5001`

**PowerShell 测试脚本**：
- 可执行文件: `pwsh`
- 参数: `-File C:\Scripts\test-ansi-colors.ps1`

---

## 🧠 解决的痛点

### 1. 启动繁琐
**问题**：多个项目需要在不同终端窗口中手动启动。  
**解决**：一键启动所有服务，自动配置环境变量，错开启动时间避免端口冲突。

### 2. 日志分散
**问题**：各服务输出在独立控制台窗口中，难以集中查看。  
**解决**：集中显示所有服务日志，保留完整的 ANSI 颜色和格式。

### 3. 日志无颜色
**问题**：重定向输出后，大多数程序会禁用彩色输出。  
**解决**：自动设置 `CLICOLOR_FORCE`、`DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION` 等环境变量，强制启用颜色。

### 4. 状态不可见
**问题**：无法直观看到哪些服务正在运行。  
**解决**：实时状态指示、进程监控、外部进程自动检测。

### 5. 日志查看体验差
**问题**：传统文本框无法正确显示 ANSI 转义序列，日志难以阅读。  
**解决**：使用专业的 xterm.js 终端模拟器，完美还原真实终端体验。

### 6. 快速更新时闪烁
**问题**：大量日志快速输出时，界面会闪烁或重复显示。  
**解决**：50ms 防抖机制 + 增量更新 + 原子操作，确保流畅无闪烁。

---

## 🛠️ 技术栈

### 框架与UI
- **Avalonia 12.0.1** — 跨平台桌面应用框架（支持 Windows、Linux、macOS）
- **Avalonia.Controls.WebView 12.0.0** — WebView 控件，用于嵌入 HTML/JavaScript
- **xterm.js 5.3.0** — 专业的 Web 终端模拟器库
- **xterm-addon-fit 0.8.0** — 终端自适应调整插件
- **xterm-addon-web-links 0.9.0** — URL 自动识别与链接插件
- **Avalonia.Themes.Fluent** — Fluent 设计风格主题

### 架构与工具
- **.NET 10.0** — 最新的 .NET 运行时
- **CommunityToolkit.Mvvm 8.4.2** — MVVM 框架（Source Generators）
- **Avalonia.ReactiveUI 11.3.9** — 响应式编程支持
- **System.Management 10.0.6** — Windows 进程管理（WMI）

### 设计模式
- **MVVM 架构** — ViewModel 与 View 分离，代码清晰易维护
- **Source Generator** — RelayCommand 等通过源生成器自动生成
- **响应式编程** — 使用 ReactiveUI 处理异步数据流
- **主构造函数** — C# 12 语法简化构造函数

### 日志与监控
- **Process API** — 启动、监控和管理外部进程
- **异步流式输出** — `BeginOutputReadLine()` / `BeginErrorReadLine()`
- **防抖定时器** — `DispatcherTimer` 实现 50ms 防抖
- **UTF-8 编码处理** — 自动处理 PowerShell 和 .NET 程序的编码问题

---

## 📦 安装与运行

### 前置要求
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本
- Windows 10/11（或支持 Avalonia 的其他平台）

### 克隆并运行
```bash
git clone https://github.com/yourusername/Dpz.ServiceHub.git
cd Dpz.ServiceHub/src/Dpz.ServiceHub/Dpz.ServiceHub
dotnet restore
dotnet run
```

### 发布独立应用
```bash
# Windows x64
dotnet publish -c Release -r win-x64 --self-contained

# Linux x64
dotnet publish -c Release -r linux-x64 --self-contained

# macOS ARM64
dotnet publish -c Release -r osx-arm64 --self-contained
```

---

## 🎯 使用指南

### 1. 添加服务
1. 点击工具栏的 **"+"** 按钮
2. 填写服务配置：
   - **服务名称**：显示名称（如 "Garnet"）
   - **可执行文件**：程序路径（如 `C:\Tools\Garnet\garnet.exe` 或 `dotnet`）
   - **启动参数**：命令行参数（如 `--port 6379` 或 `run`）
   - **工作目录**：进程的当前目录
   - **服务URL**：可选，用于快速打开浏览器
   - **端口**：可选，用于进程检测
   - **环境变量**：可选，格式 `KEY=VALUE`（每行一个）
3. 点击 **"确定"** 保存

### 2. 启动服务
- **启动单个服务**：点击服务列表中的 ▶️ 按钮
- **启动全部服务**：点击工具栏的 **"全部启动"** 按钮
- **自动启动**：编辑服务配置，勾选 "自动启动"

### 3. 查看日志
1. 点击左侧服务列表选择服务
2. 右侧终端会实时显示该服务的彩色日志
3. 支持鼠标滚动查看历史日志（最多 10000 行）
4. 选中文本后按 **Ctrl+C** 复制

### 4. 停止服务
- **停止单个服务**：点击服务列表中的 ⏹️ 按钮
- **停止全部服务**：点击工具栏的 **"全部停止"** 按钮

### 5. 其他操作
- **重启服务**：停止后再启动
- **清空输出**：点击工具栏的 🗑️ 按钮清空当前日志
- **编辑配置**：右键点击服务 → 编辑
- **删除服务**：右键点击服务 → 删除
- **打开服务URL**：双击服务或点击 URL 按钮

---

## 🧪 ANSI 颜色测试

项目包含两个 PowerShell 测试脚本，用于验证终端的 ANSI 支持：

### test-ansi-colors.ps1
测试各种 ANSI 颜色和样式：
- 基本 16 色（前景/背景）
- 256 色调色板
- RGB 真彩色
- 文本样式（粗体、斜体、下划线、删除线等）
- 模拟 ASP.NET Core 日志输出
- URL 识别测试

**运行方法**：
1. 在 ServiceHub 中添加服务
2. 可执行文件：`pwsh`
3. 参数：`-File test-ansi-colors.ps1`
4. 启动后查看彩色输出

### test-continuous-output.ps1
测试终端性能和防闪烁机制：
```powershell
# 默认配置（500ms 间隔，100 行）
pwsh -File test-continuous-output.ps1

# 快速输出（100ms 间隔，1000 行）
pwsh -File test-continuous-output.ps1 -IntervalMs 100 -MaxLines 1000

# 无限输出
pwsh -File test-continuous-output.ps1 -MaxLines 0
```

**验证内容**：
- ✅ 无闪烁：快速输出时终端不应该闪烁
- ✅ 无重复：每条日志只显示一次
- ✅ 流畅性：大量输出时滚动流畅
- ✅ 颜色正确：不同日志级别有不同颜色

---

## 🎨 终端颜色参考

终端使用 Windows Terminal 配色方案：

| 颜色 | 暗色 | 亮色 | 用途示例 |
|------|------|------|----------|
| Black | `#0c0c0c` | `#767676` | 背景、次要文本 |
| Red | `#c50f1f` | `#e74856` | 错误、异常 |
| Green | `#13a10e` | `#16c60c` | 成功、INFO |
| Yellow | `#c19c00` | `#f9f1a5` | 警告、WARN |
| Blue | `#0037da` | `#3b78ff` | 调试、DEBUG |
| Magenta | `#881798` | `#b4009e` | 特殊标记 |
| Cyan | `#3a96dd` | `#61d6d6` | 时间戳、元数据 |
| White | `#cccccc` | `#f2f2f2` | 普通文本 |

---

## 🤝 贡献

欢迎提交 Issue 和 Pull Request！

### 开发环境设置
```bash
git clone https://github.com/pengqian089/Dpz.ServiceHub.git
cd Dpz.ServiceHub
dotnet restore
```

### 构建
```bash
dotnet build
```

### 运行测试
```bash
dotnet test
```

---

## 📄 许可证

本项目采用 MIT 许可证 - 详见 [LICENSE](LICENSE) 文件

---


## 📏 编码约定
### 命名规范
+ 私有字段成员使用 `_` 前缀，并使用驼峰命名
+ 参数、变量使用驼峰命名
+ 类、结构体、接口、方法、属性、事件等使用 Pascal 命名
+ 异步方法命名应该以 **Async** 结尾
+ 命名空间应该使用 `项目名.目录(.子目录)`
+ 命名空间使用文件作用域命名空间

### 类型与可空性
+ 按照语义严格遵循 Nullable（可空值类型、可空引用类型）
+ 返回单个对象时，需要根据语义返回可空引用类型或者不可空引用类型
+ 返回集合/数组时，除必要外（例如 `byte[]?`），如果没有数据都应该返回一个没有任何项的空数集合/数组
+ 参数类型应该尽可能的抽象，返回值的类型应该尽可能的具体
+ 语义冲突时，入参和出参应该分离，而不是共用一个类型
+ 不应该存在公开的字段

### 代码风格
+ 缩进使用 4 个空格
+ `if` `for` `foreach` `while` 等代码块，即使只有一行代码，也请使用大括号
+ 每行代码的最大长度：100 字符
+ 原则上，不得使用行尾注释
+ 如果只有一个构造函数的情况下，应该使用主构造函数

### 异步与性能
+ 异步方法的签名应该添加 `CancellationToken cancellationToken = default` 参数
+ `IEnumerable<T>` 类型不应该重复枚举

### 日志规范
+ 记录日志时，禁止拼接、内插字符串，而是应该使用结构化日志