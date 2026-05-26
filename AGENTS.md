# Dpz.ServiceHub - Agent Instructions

## Build & Run

```bash
# Solution uses .slnx format
cd src/Dpz.ServiceHub/Dpz.ServiceHub
dotnet run
dotnet build
dotnet publish -c Release -r win-x64 --self-contained
```

No tests exist in the repo; `dotnet test` does nothing.

## Architecture

Single-project Avalonia desktop app (WinExe, .NET 10.0) for managing local dev services with an xterm.js terminal for ANSI log viewing.

- **Entry**: `Program.cs` (single-instance via `Global\Dpz.ServiceHub.Singleton` mutex)
- **MVVM**: `CommunityToolkit.Mvvm` with source generators (`[ObservableProperty]`, `[RelayCommand]` — needs `partial` on classes)
- **View resolution**: `ViewLocator.cs` maps `FooViewModel` -> `FooView` by convention (string replace)
- **DI**: None — `MainWindow` manually instantiates `MainWindowViewModel`, which creates `ServiceManager` and `AppSettingsStore`
- **Terminal**: `Assets/terminal.html` loaded by `Avalonia.Controls.WebView` (xterm.js 5.3.0 from CDN). Marked as `CopyToOutputDirectory=PreserveNewest`, NOT as an AvaloniaResource.

## Framework-Specific Quirks

- **CommunityToolkit.Mvvm source generators** require a design-time build for IDE support. Generated partial methods (e.g. `OnIsStoppingServicesChanged`) must be declared as `partial`.
- **`.slnx` solution format**: use `dotnet build`/`dotnet run` directly on the `.csproj`, not `msbuild` or `devenv .sln`.
- **Single-instance**: the app silently exits if already running. Add `ReleaseMutex()` on close.
- **WebView2 focus exception**: `ArgumentException` with `ICoreWebView2Controller.MoveFocus` in the stack is silently swallowed in `Program.cs`.

## Logging & Config State

- **App logs**: `%APPDATA%/Dpz.ServiceHub/logs/servicehub-\*.log` (Serilog, WARNING minimum, daily rolling, 14 files/10MB each)
- **Service config**: `%APPDATA%/Dpz.ServiceHub/services.json` (typed `List<ServiceConfig>`)
- **App settings**: `%APPDATA%/Dpz.ServiceHub/appsettings.json` (typed `AppSettings`)
- Use structured logging — no string interpolation in log messages.

## Platform Constraints

- **External process detection** (`ServiceManager.cs`) uses WMI (`Win32_Process`) and `cmd.exe /c netstat -ano` — Windows-only. The cross-platform UI/MVVM layer is portable but detection degrades on Linux/macOS.
- Port-based process resolution works through a sentinel: if a port is in use but the PID can't be resolved, `Environment.ProcessId` is used as a placeholder.

## Code Conventions

- `_` prefix for private fields (camelCase); camelCase for params/vars; PascalCase for types/methods/properties
- Async methods MUST end with `Async` and SHOULD include `CancellationToken cancellationToken = default`
- File-scoped namespaces, 4-space indent, 100-char line max
- Always braces on `if`/`for`/`foreach`/`while` blocks (even single-line)
- Prefer primary constructors when only one constructor exists
- No trailing comments; no public fields
- Return empty collections (not null) for collection return types
- Don't re-enumerate `IEnumerable<T>`
