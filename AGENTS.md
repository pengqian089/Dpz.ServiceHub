# Dpz.ServiceHub - Agent 指南

## 构建与运行

```bash
# 解决方案使用 .slnx 格式
cd src/Dpz.ServiceHub/Dpz.ServiceHub
dotnet run
dotnet build
dotnet publish -c Release -r win-x64 --self-contained
```

仓库中没有测试；`dotnet test` 不会做任何事。

## 架构

单项目 Avalonia 桌面应用（WinExe，.NET 10.0），用于管理本地开发服务，并内置 xterm.js 终端用于查看 ANSI 日志。

- **入口**：`Program.cs`（通过 `Global\Dpz.ServiceHub.Singleton` 互斥体实现单实例）
- **MVVM**：使用 `CommunityToolkit.Mvvm` 源生成器（`[ObservableProperty]`、`[RelayCommand]` —— 类上需要 `partial`）
- **视图解析**：`ViewLocator.cs` 按约定映射 `FooViewModel` -> `FooView`（字符串替换）
- **DI**：无 —— `MainWindow` 手动实例化 `MainWindowViewModel`，后者创建 `ServiceManager` 和 `AppSettingsStore`
- **终端**：`Assets/terminal.html` 由 `Avalonia.Controls.WebView` 加载（xterm.js 5.3.0 来自 CDN）。标记为 `CopyToOutputDirectory=PreserveNewest`，而非 AvaloniaResource。

## 框架相关的坑

- **CommunityToolkit.Mvvm 源生成器** 需要设计时构建才能获得 IDE 支持。生成的 partial 方法（例如 `OnIsStoppingServicesChanged`）必须声明为 `partial`。
- **`.slnx` 解决方案格式**：直接在 `.csproj` 上使用 `dotnet build`/`dotnet run`，而不是 `msbuild` 或 `devenv .sln`。
- **单实例**：如果应用已在运行，它会静默退出。关闭时添加 `ReleaseMutex()`。
- **WebView2 焦点异常**：堆栈中包含 `ICoreWebView2Controller.MoveFocus` 的 `ArgumentException` 会在 `Program.cs` 中被静默吞掉。

## 日志与配置状态

- **应用日志**：`%APPDATA%/Dpz.ServiceHub/logs/servicehub-\*.log`（Serilog，最低 WARNING 级别，每日滚动，14 个文件/每个 10MB）
- **服务配置**：`%APPDATA%/Dpz.ServiceHub/services.json`（类型化 `List<ServiceConfig>`）
- **应用设置**：`%APPDATA%/Dpz.ServiceHub/appsettings.json`（类型化 `AppSettings`）
- 使用结构化日志 —— 日志消息中不要用字符串插值。

## 平台约束

- **外部进程检测**（`ServiceManager.cs`）使用 WMI（`Win32_Process`）和 `cmd.exe /c netstat -ano` —— 仅限 Windows。跨平台的 UI/MVVM 层是可移植的，但检测在 Linux/macOS 上会退化。
- 基于端口的进程解析通过一个哨兵值工作：如果端口被占用但无法解析 PID，则使用 `Environment.ProcessId` 作为占位符。

## 代码约定

- 使用 var 接收变量
- 私有字段使用 `_` 前缀（camelCase）；参数/变量使用 camelCase；类型/方法/属性使用 PascalCase
- 异步方法必须以 `Async` 结尾，并应包含 `CancellationToken cancellationToken = default`
- 文件作用域命名空间，4 空格缩进，每行最多 100 字符
- 命名空间应该使用 `项目名.目录(.子目录)`
- `if`/`for`/`foreach`/`while` 块始终使用大括号（即使只有单行）
- 当只有一个构造函数时，优先使用主构造函数
- 不要有行尾注释；不要有公共字段
- 集合返回类型返回空集合（而非 null）
- 不要重复枚举 `IEnumerable<T>`
- 一个 `.cs` 文件中，不应该存在多个类型
- 使用 csharpier 格式化代码
- 写完代码后必须自检是否存在未被使用的方法、变量、类型、using 等，无用的直接删除，不要保留