using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using Dpz.ServiceHub.Models;
using Serilog;

namespace Dpz.ServiceHub.Services;

/// <summary>
/// 服务管理器
/// </summary>
public sealed class ServiceManager(string configFilePath)
{
    private readonly string _configFilePath = configFilePath;
    private readonly ObservableCollection<ServiceInfo> _services = [];

    /// <summary>
    /// 所有服务列表
    /// </summary>
    public ObservableCollection<ServiceInfo> Services => _services;

    /// <summary>
    /// 加载服务配置
    /// </summary>
    public async Task LoadConfigAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                // 创建默认配置文件
                await SaveConfigAsync(cancellationToken);
                return;
            }

            var json = await File.ReadAllTextAsync(_configFilePath, cancellationToken);
            var configs = JsonSerializer.Deserialize<List<ServiceConfig>>(json);

            if (configs == null)
            {
                return;
            }

            _services.Clear();
            foreach (var config in configs.OrderBy(c => c.Order))
            {
                var serviceInfo = new ServiceInfo(config);
                _services.Add(serviceInfo);
            }
        }
        catch (Exception ex)
        {
            // 加载配置失败可能是首次运行或文件损坏，记录日志后继续
            Debug.WriteLine($"加载配置失败: {ex.Message}");
            Log.Error(
                ex,
                "Failed to load service configuration from {ConfigPath}.",
                _configFilePath
            );
        }
    }

    /// <summary>
    /// 保存服务配置
    /// </summary>
    public async Task SaveConfigAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var configs = _services.Select(s => s.Config).ToList();
            var json = JsonSerializer.Serialize(
                configs,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }
            );

            var directory = Path.GetDirectoryName(_configFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(_configFilePath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            // 保存配置失败可能是权限或磁盘问题，记录日志
            Debug.WriteLine($"保存配置失败: {ex.Message}");
            Log.Error(ex, "Failed to save service configuration to {ConfigPath}.", _configFilePath);
        }
    }

    /// <summary>
    /// 启动服务
    /// </summary>
    public async Task<bool> StartServiceAsync(
        ServiceInfo serviceInfo,
        CancellationToken cancellationToken = default
    )
    {
        if (serviceInfo.Status == ServiceStatus.Running)
        {
            return true;
        }

        try
        {
            serviceInfo.Status = ServiceStatus.Starting;
            serviceInfo.ClearOutput();

            var (resolvedFileName, resolvedArguments) = ResolveStartCommand(serviceInfo.Config);

            var startInfo = new ProcessStartInfo
            {
                FileName = resolvedFileName,
                Arguments = resolvedArguments,
                WorkingDirectory = serviceInfo.Config.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            // 添加环境变量
            foreach (var env in serviceInfo.Config.EnvironmentVariables)
            {
                startInfo.EnvironmentVariables[env.Key] = env.Value;
            }

            // 尽可能保留被重定向输出时的 ANSI 颜色
            var ansiKey = "DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION";
            startInfo.EnvironmentVariables[ansiKey] = "1";
            startInfo.EnvironmentVariables["TERM"] = "xterm-256color";
            startInfo.EnvironmentVariables["CLICOLOR_FORCE"] = "1";
            startInfo.EnvironmentVariables["FORCE_COLOR"] = "1";
            startInfo.EnvironmentVariables["DOTNET_CONSOLE_ANSI_COLOR"] = "1";

            // ASP.NET Core 项目单独注入日志颜色配置，避免影响普通控制台程序参数解析。
            var isAspNetCoreService = IsAspNetCoreService(serviceInfo.Config);
            if (isAspNetCoreService)
            {
                startInfo.EnvironmentVariables["Logging__Console__FormatterName"] = "Simple";
                startInfo.EnvironmentVariables[
                    "Logging__Console__FormatterOptions__ColorBehavior"
                ] = "Enabled";
                startInfo.EnvironmentVariables["Logging__Console__DisableColors"] = "false";
                startInfo.EnvironmentVariables["ASPNETCORE_LOGGING__CONSOLE__DISABLECOLORS"] =
                    "false";
            }

            // 确保NO_COLOR未设置（某些工具会检查此变量来禁用颜色）
            if (startInfo.EnvironmentVariables.ContainsKey("NO_COLOR"))
            {
                startInfo.EnvironmentVariables.Remove("NO_COLOR");
            }

            // PowerShell 特殊处理 - 强制UTF-8输出
            if (IsPowerShellHost(resolvedFileName))
            {
                startInfo.EnvironmentVariables["POWERSHELL_TELEMETRY_OPTOUT"] = "1";

                // 如果启动命令是 -File 或 -Command，在前面插入编码设置
                if (!string.IsNullOrEmpty(resolvedArguments))
                {
                    var trimmedArgs = resolvedArguments.Trim();
                    // 检查是否已经包含 -Command 参数
                    if (
                        trimmedArgs.StartsWith("-Command", StringComparison.OrdinalIgnoreCase)
                        || trimmedArgs.StartsWith("-c ", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        // 已经是 -Command，保持原样（脚本已经设置了编码）
                    }
                    else if (trimmedArgs.StartsWith("-File", StringComparison.OrdinalIgnoreCase))
                    {
                        // -File 参数，提取文件路径并包装成 -Command
                        var filePath = trimmedArgs.Substring("-File".Length).Trim();
                        startInfo.Arguments =
                            $"-NoProfile -Command \"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; & '{filePath}'\"";
                    }
                    else if (trimmedArgs.StartsWith("-f ", StringComparison.OrdinalIgnoreCase))
                    {
                        // -f 简写，提取文件路径
                        var filePath = trimmedArgs.Substring("-f".Length).Trim();
                        startInfo.Arguments =
                            $"-NoProfile -Command \"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; & '{filePath}'\"";
                    }
                    else
                    {
                        // 其他情况（直接的脚本路径），包装成 -File
                        // 不需要设置编码，因为脚本已经在开头设置了
                        // startInfo.Arguments = $"-NoProfile -File \"{trimmedArgs}\"";
                    }
                }
            }

            var process = new Process { StartInfo = startInfo };

            // 捕获输出
            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    serviceInfo.AppendOutput($"{e.Data}{Environment.NewLine}");
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    serviceInfo.AppendOutput($"{e.Data}{Environment.NewLine}");
                }
            };

            process.Exited += (sender, e) =>
            {
                serviceInfo.AppendOutput(
                    $"进程已退出，退出代码: {process.ExitCode}{Environment.NewLine}"
                );

                Dispatcher.UIThread.Post(() =>
                {
                    var switchedToPortManagedMode =
                        serviceInfo.Config.Ports.Count > 0
                        && serviceInfo.Config.Ports.Any(IsPortInUse);

                    if (switchedToPortManagedMode)
                    {
                        serviceInfo.Status = ServiceStatus.Running;
                        serviceInfo.Process = null;
                        serviceInfo.ProcessId = Environment.ProcessId;
                        serviceInfo.IsExternal = false;
                        serviceInfo.AppendOutput(
                            $"检测到托管子进程继续运行，已切换为端口托管模式。{Environment.NewLine}"
                        );
                        return;
                    }

                    serviceInfo.Status = ServiceStatus.Stopped;
                    serviceInfo.Process = null;
                    serviceInfo.ProcessId = null;
                    serviceInfo.IsExternal = false;
                });
            };

            process.EnableRaisingEvents = true;

            if (!process.Start())
            {
                serviceInfo.Status = ServiceStatus.Stopped;
                serviceInfo.AppendOutput("启动失败\n");
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            serviceInfo.Process = process;
            serviceInfo.ProcessId = process.Id;
            serviceInfo.Status = ServiceStatus.Running;
            serviceInfo.IsExternal = false;
            serviceInfo.AppendOutput($"服务已启动，进程 ID: {process.Id}{Environment.NewLine}");

            return true;
        }
        catch (Exception ex)
        {
            serviceInfo.Status = ServiceStatus.Stopped;
            serviceInfo.AppendOutput($"启动失败: {ex.Message}{Environment.NewLine}");
            Log.Error(ex, "Failed to start service {ServiceName}.", serviceInfo.Config.Name);
            return false;
        }
    }

    /// <summary>
    /// 停止服务
    /// </summary>
    public async Task<bool> StopServiceAsync(
        ServiceInfo serviceInfo,
        CancellationToken cancellationToken = default
    )
    {
        if (serviceInfo.Status == ServiceStatus.Stopped)
        {
            return true;
        }

        try
        {
            serviceInfo.Status = ServiceStatus.Stopping;
            var isExternalService = serviceInfo.IsExternal;

            // 如果有进程 ID，尝试停止进程
            if (serviceInfo.ProcessId.HasValue)
            {
                // 如果 ProcessId 是当前进程ID，说明是通过端口检测的，需要通过端口停止
                if (serviceInfo.ProcessId.Value == Environment.ProcessId)
                {
                    if (serviceInfo.Config.Ports.Count > 0)
                    {
                        serviceInfo.AppendOutput($"正在通过端口停止服务...{Environment.NewLine}");
                        await StopServiceByPortsAsync(
                            serviceInfo.Config.Ports,
                            serviceInfo,
                            cancellationToken
                        );

                        // 验证端口是否已释放
                        var stillOccupied = serviceInfo.Config.Ports.Any(IsPortInUse);
                        if (stillOccupied)
                        {
                            serviceInfo.Status = ServiceStatus.Running;
                            serviceInfo.AppendOutput(
                                $"停止失败：端口仍被占用{Environment.NewLine}"
                            );
                            return false;
                        }
                    }
                }
                else
                {
                    try
                    {
                        var process = serviceInfo.Process;

                        // 如果没有 Process 对象但有 ProcessId（外部进程），尝试获取进程
                        if (process == null)
                        {
                            try
                            {
                                process = Process.GetProcessById(serviceInfo.ProcessId.Value);
                            }
                            catch (Exception ex)
                            {
                                // 进程已退出或不存在，重置为停止状态
                                Log.Warning(
                                    ex,
                                    "Process not found while stopping service {ServiceName}, PID: {ProcessId}.",
                                    serviceInfo.Config.Name,
                                    serviceInfo.ProcessId.Value
                                );
                                serviceInfo.Process = null;
                                serviceInfo.ProcessId = null;
                                serviceInfo.Status = ServiceStatus.Stopped;
                                serviceInfo.IsExternal = false;
                                var message = $"进程已不存在{Environment.NewLine}";
                                serviceInfo.AppendOutput(message);
                                return true;
                            }
                        }

                        if (process != null && !process.HasExited)
                        {
                            serviceInfo.AppendOutput($"正在停止服务...{Environment.NewLine}");

                            // 尝试优雅关闭（只对非外部进程）
                            if (!serviceInfo.IsExternal)
                            {
                                try
                                {
                                    process.StandardInput?.Close();
                                }
                                catch (Exception ex)
                                {
                                    // 忽略 StandardInput 关闭异常
                                    Log.Warning(
                                        ex,
                                        "Failed to close standard input while stopping service {ServiceName}.",
                                        serviceInfo.Config.Name
                                    );
                                }
                            }

                            // 先尝试优雅退出；若仍在运行则直接终止进程树，避免端口残留占用。
                            if (!process.HasExited)
                            {
                                try
                                {
                                    process.Kill(true);
                                }
                                catch (Exception ex)
                                {
                                    // Kill 可能失败（进程已退出等），交由后续检查处理
                                    Log.Warning(
                                        ex,
                                        "Failed to kill process while stopping service {ServiceName}, PID: {ProcessId}.",
                                        serviceInfo.Config.Name,
                                        process.Id
                                    );
                                }

                                if (!process.HasExited)
                                {
                                    await process.WaitForExitAsync(cancellationToken);
                                    serviceInfo.AppendOutput(
                                        $"已强制终止进程{Environment.NewLine}"
                                    );
                                }
                            }

                            if (!serviceInfo.IsExternal)
                            {
                                process.Dispose();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        serviceInfo.AppendOutput(
                            $"停止进程时出错: {ex.Message}{Environment.NewLine}"
                        );
                        Log.Error(
                            ex,
                            "Failed while stopping service process for {ServiceName}.",
                            serviceInfo.Config.Name
                        );
                    }
                }
            }

            // 仅对外部进程做模糊残留清理，避免误杀同机其他进程内服务。
            if (isExternalService)
            {
                var protectedProcessIds = _services
                    .Where(s => !ReferenceEquals(s, serviceInfo) && s.ProcessId.HasValue)
                    .Select(s => s.ProcessId!.Value)
                    .ToHashSet();

                var lingeringProcessIds = await FindMatchingProcessIdsAsync(
                    serviceInfo.Config,
                    cancellationToken
                );
                foreach (
                    var processId in lingeringProcessIds.Where(pid =>
                        !protectedProcessIds.Contains(pid)
                    )
                )
                {
                    try
                    {
                        var lingering = Process.GetProcessById(processId);
                        if (!lingering.HasExited)
                        {
                            lingering.Kill(true);
                            await lingering.WaitForExitAsync(cancellationToken);
                            serviceInfo.AppendOutput(
                                $"已清理残留进程 PID: {processId}{Environment.NewLine}"
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        // 忽略单个进程清理失败，最终统一校验
                        Log.Warning(
                            ex,
                            "Failed to cleanup lingering process {LingeringProcessId} for service {ServiceName}.",
                            processId,
                            serviceInfo.Config.Name
                        );
                    }
                }

                var stillRunningIds = await FindMatchingProcessIdsAsync(
                    serviceInfo.Config,
                    cancellationToken
                );
                var stillRunning = stillRunningIds.Any(pid => !protectedProcessIds.Contains(pid));
                if (stillRunning)
                {
                    serviceInfo.Status = ServiceStatus.Running;
                    serviceInfo.AppendOutput(
                        $"仍检测到同服务进程存活，端口可能仍被占用。{Environment.NewLine}"
                    );
                    return false;
                }
            }

            serviceInfo.Process = null;
            serviceInfo.ProcessId = null;
            serviceInfo.Status = ServiceStatus.Stopped;
            serviceInfo.IsExternal = false;
            serviceInfo.AppendOutput($"服务已停止{Environment.NewLine}");

            return true;
        }
        catch (OperationCanceledException ex)
        {
            serviceInfo.AppendOutput($"停止已取消{Environment.NewLine}");
            serviceInfo.Status = ServiceStatus.Running;
            Log.Warning(
                ex,
                "Stopping service {ServiceName} was canceled.",
                serviceInfo.Config.Name
            );
            return false;
        }
        catch (Exception ex)
        {
            serviceInfo.AppendOutput($"停止失败: {ex.Message}{Environment.NewLine}");
            serviceInfo.Status = ServiceStatus.Stopped;
            Log.Error(ex, "Failed to stop service {ServiceName}.", serviceInfo.Config.Name);
            return false;
        }
    }

    /// <summary>
    /// 重启服务
    /// </summary>
    public async Task<bool> RestartServiceAsync(
        ServiceInfo serviceInfo,
        CancellationToken cancellationToken = default
    )
    {
        await StopServiceAsync(serviceInfo, cancellationToken);
        return await StartServiceAsync(serviceInfo, cancellationToken);
    }

    /// <summary>
    /// 向服务发送命令
    /// </summary>
    public async Task SendCommandAsync(
        ServiceInfo serviceInfo,
        string command,
        CancellationToken cancellationToken = default
    )
    {
        if (serviceInfo.Process == null || serviceInfo.Process.HasExited)
        {
            return;
        }

        try
        {
            await serviceInfo.Process.StandardInput.WriteLineAsync(command);
            serviceInfo.AppendOutput($"> {command}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            serviceInfo.AppendOutput($"发送命令失败: {ex.Message}{Environment.NewLine}");
            Log.Error(
                ex,
                "Failed to send command to service {ServiceName}.",
                serviceInfo.Config.Name
            );
        }
    }

    /// <summary>
    /// 添加服务
    /// </summary>
    public void AddService(ServiceConfig config)
    {
        config.Id = Guid.NewGuid().ToString();
        config.Order = _services.Count;
        var serviceInfo = new ServiceInfo(config);
        _services.Add(serviceInfo);
    }

    /// <summary>
    /// 删除服务
    /// </summary>
    public async Task<bool> RemoveServiceAsync(
        ServiceInfo serviceInfo,
        CancellationToken cancellationToken = default
    )
    {
        if (serviceInfo.Status != ServiceStatus.Stopped)
        {
            await StopServiceAsync(serviceInfo, cancellationToken);
        }

        return _services.Remove(serviceInfo);
    }

    /// <summary>
    /// 检测外部进程
    /// </summary>
    public async Task DetectExternalProcessesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshots = _services
                .Select(serviceInfo => new ServiceDetectionSnapshot(
                    serviceInfo,
                    serviceInfo.Config,
                    serviceInfo.Status,
                    serviceInfo.IsExecuting,
                    serviceInfo.IsExternal,
                    serviceInfo.ProcessId,
                    serviceInfo.Process != null
                ))
                .ToList();

            var results = await Task.Run(
                () => ServiceSnapshots(snapshots, cancellationToken),
                cancellationToken
            );

            foreach (var result in results)
            {
                switch (result.Action)
                {
                    case ServiceDetectionAction.Clear:
                        result.Service.Process = null;
                        result.Service.ProcessId = null;
                        result.Service.Status = ServiceStatus.Stopped;
                        result.Service.IsExternal = false;
                        if (!string.IsNullOrEmpty(result.Log))
                        {
                            result.Service.AppendOutput(result.Log);
                        }
                        break;

                    case ServiceDetectionAction.SetExternal:
                        result.Service.Process = null;
                        result.Service.ProcessId = result.ProcessId;
                        result.Service.Status = ServiceStatus.External;
                        result.Service.IsExternal = true;
                        if (!string.IsNullOrEmpty(result.Log))
                        {
                            result.Service.AppendOutput(result.Log);
                        }
                        break;
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            // 检测任务被取消（通常是设置更改或应用退出），优雅返回
            Log.Warning(ex, "External process detection task was canceled.");
        }
    }

    /// <summary>
    /// 处理服务快照列表，检测外部进程状态
    /// </summary>
    /// <param name="snapshots">服务快照列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>检测结果列表</returns>
    private List<ServiceDetectionResult> ServiceSnapshots(
        List<ServiceDetectionSnapshot> snapshots,
        CancellationToken cancellationToken
    )
    {
        var results = new List<ServiceDetectionResult>(snapshots.Count);
        try
        {
            foreach (var snapshot in snapshots)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 托管启动/停止/重启期间由应用控制状态，不参与外部进程判定。
                if (snapshot.IsExecuting)
                {
                    continue;
                }

                if (snapshot.IsExternal && snapshot.ProcessId.HasValue)
                {
                    // 如果是当前进程ID，说明是通过端口检测的，需要重新检查端口
                    if (snapshot.ProcessId.Value == Environment.ProcessId)
                    {
                        if (
                            snapshot.Config.Ports.Count > 0
                            && snapshot.Config.Ports.Any(IsPortInUse)
                        )
                        {
                            // 端口仍在使用中，继续保持外部运行状态
                            continue;
                        }

                        // 端口已释放，清除外部进程状态
                        results.Add(
                            new ServiceDetectionResult(
                                snapshot.Service,
                                ServiceDetectionAction.Clear,
                                null,
                                $"外部进程已停止（端口已释放）{Environment.NewLine}"
                            )
                        );
                        continue;
                    }

                    // 常规进程ID检查
                    try
                    {
                        var externalProcess = Process.GetProcessById(snapshot.ProcessId.Value);
                        if (externalProcess.HasExited)
                        {
                            results.Add(
                                new ServiceDetectionResult(
                                    snapshot.Service,
                                    ServiceDetectionAction.Clear,
                                    null,
                                    $"外部进程已退出{Environment.NewLine}"
                                )
                            );
                        }
                    }
                    catch (ArgumentException)
                    {
                        // 进程在检查瞬间退出是常见竞态，按正常清理路径处理，不记录告警。
                        var message = $"外部进程已不存在{Environment.NewLine}";
                        results.Add(
                            new ServiceDetectionResult(
                                snapshot.Service,
                                ServiceDetectionAction.Clear,
                                null,
                                message
                            )
                        );
                    }
                    catch (Exception ex)
                    {
                        // 进程ID无效或进程已退出
                        Log.Warning(
                            ex,
                            "Failed to check external process state for PID: {ProcessId}.",
                            snapshot.ProcessId.Value
                        );
                        var message = $"外部进程已不存在{Environment.NewLine}";
                        results.Add(
                            new ServiceDetectionResult(
                                snapshot.Service,
                                ServiceDetectionAction.Clear,
                                null,
                                message
                            )
                        );
                    }

                    continue;
                }

                if (
                    snapshot.Status == ServiceStatus.Running
                    && !snapshot.IsExternal
                    && (snapshot.HasManagedProcess || snapshot.ProcessId == Environment.ProcessId)
                )
                {
                    continue;
                }

                if (snapshot.Status != ServiceStatus.Stopped)
                {
                    continue;
                }

                var matchedProcess = TryFindExternalProcess(snapshot.Config);
                if (matchedProcess == null)
                {
                    continue;
                }

                // 检查是否通过端口检测（ProcessId == 当前进程ID）
                var isPortDetection = matchedProcess.Id == Environment.ProcessId;
                var logMessage = isPortDetection
                    ? $"检测到外部进程（通过端口检测）{Environment.NewLine}"
                    : $"检测到外部进程 ID: {matchedProcess.Id}{Environment.NewLine}";

                results.Add(
                    new ServiceDetectionResult(
                        snapshot.Service,
                        ServiceDetectionAction.SetExternal,
                        matchedProcess.Id,
                        logMessage
                    )
                );
            }
        }
        catch (OperationCanceledException ex)
        {
            // 检测任务被取消，返回空结果
            Log.Warning(ex, "Service snapshot detection was canceled.");
        }

        return results;
    }

    private Process? TryFindExternalProcess(ServiceConfig config)
    {
        // 优先使用端口检测：如果配置了端口且端口在使用中，认为服务正在外部运行
        if (config.Ports.Count > 0 && config.Ports.Any(IsPortInUse))
        {
            // 尝试通过端口找到进程ID
            var processId = TryGetProcessIdByPort(config.Ports.FirstOrDefault(IsPortInUse));
            if (processId > 0)
            {
                try
                {
                    return Process.GetProcessById(processId);
                }
                catch (Exception ex)
                {
                    // 端口在使用但进程已退出，使用当前进程作为占位符
                    Log.Warning(
                        ex,
                        "Failed to resolve process by port for service {ServiceName}, port {Port}.",
                        config.Name,
                        config.Ports.FirstOrDefault(IsPortInUse)
                    );
                }
            }

            // 无法获取具体进程ID，但端口确实在使用中
            // 使用当前进程作为占位符标记
            return Process.GetCurrentProcess();
        }

        // 降级到进程命令行匹配
        var matchedProcessId = FindMatchingProcessIds(config).FirstOrDefault();
        if (matchedProcessId <= 0)
        {
            return null;
        }

        try
        {
            return Process.GetProcessById(matchedProcessId);
        }
        catch (Exception ex)
        {
            // 进程获取失败，返回 null
            Log.Warning(
                ex,
                "Failed to resolve matched process by PID: {ProcessId}.",
                matchedProcessId
            );
            return null;
        }
    }

    /// <summary>
    /// 检查端口是否被占用
    /// </summary>
    private static bool IsPortInUse(int port)
    {
        try
        {
            var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpListeners = ipProperties.GetActiveTcpListeners();
            return tcpListeners.Any(endpoint => endpoint.Port == port);
        }
        catch (Exception ex)
        {
            // 忽略网络接口查询失败
            Log.Warning(ex, "Failed to inspect TCP listener state for port {Port}.", port);
            return false;
        }
    }

    /// <summary>
    /// 尝试通过端口获取进程ID（仅Windows）
    /// </summary>
    private static int TryGetProcessIdByPort(int port)
    {
        if (!OperatingSystem.IsWindows())
        {
            return -1;
        }

        try
        {
            // 使用 netstat -ano 命令查找占用端口的进程
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c netstat -ano | findstr :{port}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return -1;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // 解析输出，查找监听状态的端口
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.Contains("LISTENING"))
                {
                    var parts = line.Trim()
                        .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && int.TryParse(parts[^1], out int pid) && pid > 0)
                    {
                        return pid;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 命令执行或解析输出失败，返回无效 PID
            Log.Warning(ex, "Failed to query process id by port {Port}.", port);
            return -1;
        }

        return -1;
    }

    /// <summary>
    /// 通过端口查找并终止进程
    /// </summary>
    private static async Task StopServiceByPortsAsync(
        List<int> ports,
        ServiceInfo serviceInfo,
        CancellationToken cancellationToken
    )
    {
        foreach (var port in ports.Where(IsPortInUse))
        {
            var processId = TryGetProcessIdByPort(port);
            if (processId > 0)
            {
                try
                {
                    var process = Process.GetProcessById(processId);
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                        await process.WaitForExitAsync(cancellationToken);
                        serviceInfo.AppendOutput(
                            $"已终止占用端口 {port} 的进程 PID: {processId}{Environment.NewLine}"
                        );
                    }
                }
                catch (Exception ex)
                {
                    serviceInfo.AppendOutput(
                        $"停止占用端口 {port} 的进程失败: {ex.Message}{Environment.NewLine}"
                    );
                    Log.Error(
                        ex,
                        "Failed to stop process {ProcessId} occupying port {Port} for service {ServiceName}.",
                        processId,
                        port,
                        serviceInfo.Config.Name
                    );
                }
            }
        }
    }

    /// <summary>
    /// 进程检测操作类型
    /// </summary>
    private enum ServiceDetectionAction
    {
        None,
        Clear,
        SetExternal,
    }

    /// <summary>
    /// 服务检测快照
    /// </summary>
    private sealed record ServiceDetectionSnapshot(
        ServiceInfo Service,
        ServiceConfig Config,
        ServiceStatus Status,
        bool IsExecuting,
        bool IsExternal,
        int? ProcessId,
        bool HasManagedProcess
    );

    /// <summary>
    /// 服务检测结果
    /// </summary>
    private sealed record ServiceDetectionResult(
        ServiceInfo Service,
        ServiceDetectionAction Action,
        int? ProcessId,
        string? Log
    );

    /// <summary>
    /// 进程快照
    /// </summary>
    private sealed record ProcessSnapshot(
        int ProcessId,
        string? Name,
        string? CommandLine,
        string? ExecutablePath
    );

    private List<int> FindMatchingProcessIds(ServiceConfig config)
    {
        var configFingerprints = BuildConfigFingerprintHashes(config);

        return EnumerateProcessSnapshots()
            .Where(snapshot => snapshot.ProcessId != Environment.ProcessId)
            .Select(snapshot => new
            {
                snapshot.ProcessId,
                FingerprintScore = GetFingerprintMatchScore(configFingerprints, snapshot),
                Score = GetExternalProcessMatchScore(config, snapshot),
            })
            .Where(x => x.FingerprintScore >= 1)
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.FingerprintScore)
            .ThenBy(x => x.ProcessId)
            .Select(x => x.ProcessId)
            .Distinct()
            .ToList();
    }

    private Task<List<int>> FindMatchingProcessIdsAsync(
        ServiceConfig config,
        CancellationToken cancellationToken
    )
    {
        return Task.Run(() => FindMatchingProcessIds(config), cancellationToken);
    }

    private static IReadOnlyList<ProcessSnapshot> EnumerateProcessSnapshots()
    {
        var snapshots = new List<ProcessSnapshot>();
        if (!OperatingSystem.IsWindows())
        {
            return snapshots;
        }

        ManagementObjectCollection? results = null;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, Name, CommandLine, ExecutablePath FROM Win32_Process"
            );
            results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                var processIdObj = obj["ProcessId"];
                if (processIdObj == null)
                {
                    continue;
                }

                var processId = Convert.ToInt32(processIdObj);
                var name = obj["Name"]?.ToString();
                var commandLine = obj["CommandLine"]?.ToString();
                var executablePath = obj["ExecutablePath"]?.ToString();
                snapshots.Add(new ProcessSnapshot(processId, name, commandLine, executablePath));
            }
        }
        catch (Exception ex)
        {
            // WMI 查询失败，返回空集合
            Log.Warning(ex, "Failed to enumerate process snapshots through WMI.");
            return snapshots;
        }
        finally
        {
            results?.Dispose();
        }

        return snapshots;
    }

    private static int GetExternalProcessMatchScore(ServiceConfig config, ProcessSnapshot snapshot)
    {
        var commandLine = snapshot.CommandLine ?? string.Empty;
        var executablePath = snapshot.ExecutablePath ?? string.Empty;
        var processName = snapshot.Name ?? string.Empty;

        var fullText = string.Concat(processName, "\n", commandLine, "\n", executablePath);
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return 0;
        }

        var (resolvedFileName, resolvedArguments) = ResolveStartCommand(config);
        var resolvedExecutableName = Path.GetFileName(resolvedFileName);
        var resolvedExecutableBaseName = Path.GetFileNameWithoutExtension(resolvedExecutableName);
        var isHostExecutable = IsHostExecutable(resolvedExecutableBaseName);

        // 进程可执行文件类型必须与配置一致，否则直接拒绝。
        // 例如 dotnet 服务不应匹配 pwsh 进程，反之亦然。
        var processExeBaseName =
            !string.IsNullOrWhiteSpace(processName) ? Path.GetFileNameWithoutExtension(processName)
            : !string.IsNullOrWhiteSpace(executablePath)
                ? Path.GetFileNameWithoutExtension(executablePath)
            : null;

        if (
            !string.IsNullOrWhiteSpace(resolvedExecutableBaseName)
            && !string.IsNullOrWhiteSpace(processExeBaseName)
            && !processExeBaseName.Equals(
                resolvedExecutableBaseName,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return 0;
        }

        var resolvedScriptPath = GetResolvedScriptPath(config);
        var scriptPathHit = false;
        if (!string.IsNullOrWhiteSpace(resolvedScriptPath))
        {
            var scriptFileName = Path.GetFileName(resolvedScriptPath);
            scriptPathHit =
                ContainsOrdinalIgnoreCase(fullText, resolvedScriptPath)
                || ContainsTokenIgnoreCase(fullText, scriptFileName);
            if (!scriptPathHit)
            {
                return 0;
            }
        }

        var executableHit = false;
        if (!string.IsNullOrWhiteSpace(resolvedExecutableBaseName))
        {
            executableHit =
                ContainsTokenIgnoreCase(fullText, resolvedExecutableBaseName)
                || ContainsTokenIgnoreCase(fullText, resolvedExecutableName);

            if (!isHostExecutable && !executableHit)
            {
                return 0;
            }
        }

        var workingDirectory = config.WorkingDirectory.Trim();
        var workingDirectoryName = Path.GetFileName(
            workingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        );

        var hasWorkingDirectoryHint =
            !string.IsNullOrWhiteSpace(workingDirectory)
            && (
                ContainsPathMatchIgnoreCase(commandLine, workingDirectory)
                || ContainsPathMatchIgnoreCase(executablePath, workingDirectory)
                || (
                    !string.IsNullOrWhiteSpace(workingDirectoryName)
                    && ContainsTokenIgnoreCase(fullText, workingDirectoryName)
                )
            );

        var argumentHints = BuildStrongArgumentHints(resolvedArguments).ToList();
        var argumentHitCount = argumentHints.Count(hint => ContainsTokenIgnoreCase(fullText, hint));

        var identityHints = BuildIdentityHints(config).ToList();
        var identityHitCount = identityHints.Count(hint => ContainsTokenIgnoreCase(fullText, hint));
        var processIdentityHit = identityHints.Any(hint =>
            ContainsTokenIgnoreCase(processName, hint)
            || ContainsTokenIgnoreCase(executablePath, hint)
        );

        var score = 0;
        if (scriptPathHit)
        {
            score += 5;
        }
        if (hasWorkingDirectoryHint)
        {
            score += 3;
        }
        if (executableHit)
        {
            score += 2;
        }
        if (processIdentityHit)
        {
            score += 2;
        }
        score += Math.Min(identityHitCount, 2) * 3;
        score += Math.Min(argumentHitCount, 2) * 2;

        // 使用严格匹配模式（端口检测优先后，这是降级方案）
        var strictSatisfied =
            scriptPathHit
            || (identityHitCount > 0 && (hasWorkingDirectoryHint || processIdentityHit))
            || (identityHitCount > 0 && argumentHitCount > 0)
            || (!isHostExecutable && executableHit && hasWorkingDirectoryHint);

        return strictSatisfied && score >= 6 ? score : 0;
    }

    private static IEnumerable<string> BuildIdentityHints(ServiceConfig config)
    {
        var hints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var folderName = Path.GetFileName(
            config.WorkingDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            )
        );
        if (!string.IsNullOrWhiteSpace(folderName) && folderName.Length >= 4)
        {
            hints.Add(folderName);
        }

        var cleanedName = config.Name.Trim();
        if (!string.IsNullOrWhiteSpace(cleanedName))
        {
            foreach (
                var token in cleanedName.Split(
                    [' ', '(', ')', '[', ']', '-', '_', ':', '：', '/'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            {
                if (token.Length >= 4)
                {
                    hints.Add(token);
                }
            }
        }

        var executable = Path.GetFileNameWithoutExtension(config.Executable);
        if (!string.IsNullOrWhiteSpace(executable) && !IsHostExecutable(executable))
        {
            hints.Add(executable);
        }

        return hints;
    }

    /// <summary>
    /// 判断是否为主机执行文件（dotnet、pwsh、cmd 等）
    /// </summary>
    private static bool IsHostExecutable(string executableBaseName)
    {
        return executableBaseName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            || executableBaseName.Equals("pwsh", StringComparison.OrdinalIgnoreCase)
            || executableBaseName.Equals("powershell", StringComparison.OrdinalIgnoreCase)
            || executableBaseName.Equals("cmd", StringComparison.OrdinalIgnoreCase)
            || executableBaseName.Equals("bash", StringComparison.OrdinalIgnoreCase)
            || executableBaseName.Equals("sh", StringComparison.OrdinalIgnoreCase)
            || executableBaseName.Equals("zsh", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPowerShellHost(string fileName)
    {
        var executableName = Path.GetFileNameWithoutExtension(fileName);
        return string.Equals(executableName, "pwsh", StringComparison.OrdinalIgnoreCase)
            || string.Equals(executableName, "powershell", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsOrdinalIgnoreCase(string source, string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && source.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsTokenIgnoreCase(string source, string token)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var start = 0;
        while (true)
        {
            var index = source.IndexOf(token, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var beforeIndex = index - 1;
            var afterIndex = index + token.Length;
            var beforeOk = beforeIndex < 0 || IsBoundaryChar(source[beforeIndex]);
            var afterOk = afterIndex >= source.Length || IsBoundaryChar(source[afterIndex]);
            if (beforeOk && afterOk)
            {
                return true;
            }

            start = index + token.Length;
        }
    }

    private static bool ContainsPathMatchIgnoreCase(string source, string path)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedSource = source.Replace('/', '\\');
        var normalizedPath = path.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        var start = 0;
        while (true)
        {
            var index = normalizedSource.IndexOf(
                normalizedPath,
                start,
                StringComparison.OrdinalIgnoreCase
            );
            if (index < 0)
            {
                return false;
            }

            var beforeIndex = index - 1;
            var afterIndex = index + normalizedPath.Length;
            var beforeOk = beforeIndex < 0 || IsPathBoundaryChar(normalizedSource[beforeIndex]);
            var afterOk =
                afterIndex >= normalizedSource.Length
                || IsPathBoundaryChar(normalizedSource[afterIndex]);
            if (beforeOk && afterOk)
            {
                return true;
            }

            start = afterIndex;
        }
    }

    /// <summary>
    /// 判断字符是否为路径分隔符
    /// </summary>
    private static bool IsPathBoundaryChar(char ch)
    {
        return char.IsWhiteSpace(ch)
            || ch == '\\'
            || ch == '/'
            || ch == '"'
            || ch == '\''
            || ch == ';'
            || ch == ','
            || ch == ')'
            || ch == ']';
    }

    /// <summary>
    /// 判断字符是否为标识符边界
    /// 注意：. _ - 被视为标识符的一部分
    /// </summary>
    private static bool IsBoundaryChar(char ch)
    {
        return !(char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-');
    }

    private static HashSet<int> BuildConfigFingerprintHashes(ServiceConfig config)
    {
        var hashes = new HashSet<int>();
        var (_, resolvedArguments) = ResolveStartCommand(config);

        foreach (var token in BuildIdentityHints(config))
        {
            AddFingerprintHash(hashes, token);
        }

        foreach (var token in BuildStrongArgumentHints(resolvedArguments))
        {
            AddFingerprintHash(hashes, token);
        }

        var workingDirectoryName = Path.GetFileName(
            config.WorkingDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            )
        );
        AddFingerprintHash(hashes, workingDirectoryName);

        var scriptPath = GetResolvedScriptPath(config);
        if (!string.IsNullOrWhiteSpace(scriptPath))
        {
            AddFingerprintHash(hashes, Path.GetFileNameWithoutExtension(scriptPath));
            AddFingerprintHash(hashes, Path.GetFileName(scriptPath));
        }

        return hashes;
    }

    private static int GetFingerprintMatchScore(HashSet<int> configHashes, ProcessSnapshot snapshot)
    {
        if (configHashes.Count == 0)
        {
            return 0;
        }

        var snapshotHashes = BuildSnapshotFingerprintHashes(snapshot);
        var score = 0;
        foreach (var hash in configHashes)
        {
            if (snapshotHashes.Contains(hash))
            {
                score++;
            }
        }

        return score;
    }

    private static HashSet<int> BuildSnapshotFingerprintHashes(ProcessSnapshot snapshot)
    {
        var hashes = new HashSet<int>();

        AddFingerprintHash(hashes, snapshot.Name);
        AddFingerprintHash(hashes, Path.GetFileNameWithoutExtension(snapshot.Name ?? string.Empty));
        AddFingerprintHash(hashes, snapshot.ExecutablePath);
        AddFingerprintHash(hashes, Path.GetFileName(snapshot.ExecutablePath ?? string.Empty));
        AddFingerprintHash(
            hashes,
            Path.GetFileNameWithoutExtension(snapshot.ExecutablePath ?? string.Empty)
        );

        foreach (var token in TokenizeFingerprintSource(snapshot.CommandLine))
        {
            AddFingerprintHash(hashes, token);
        }

        return hashes;
    }

    private static IEnumerable<string> TokenizeFingerprintSource(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var buffer = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch == '.' || ch == '_' || ch == '-')
            {
                buffer.Append(ch);
                continue;
            }

            if (buffer.Length > 0)
            {
                yield return buffer.ToString();
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString();
        }
    }

    private static void AddFingerprintHash(HashSet<int> hashes, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var normalized = token.Trim().Trim('"', '\'');
        if (normalized.Length < 4)
        {
            return;
        }

        hashes.Add(ComputeStableHash(normalized.ToUpperInvariant()));
    }

    /// <summary>
    /// 计算稳定哈希值（FNV-1a 算法）
    /// </summary>
    private static int ComputeStableHash(string text)
    {
        unchecked
        {
            const int fnvPrime = 16777619;
            var hash = (int)2166136261;
            foreach (var ch in text)
            {
                hash ^= ch;
                hash *= fnvPrime;
            }

            return hash;
        }
    }

    private static IEnumerable<string> BuildStrongArgumentHints(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            yield break;
        }

        foreach (
            var token in arguments.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
        )
        {
            var normalized = token.Trim('"', '\'');
            if (normalized.Length < 4)
            {
                continue;
            }

            if (normalized.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            yield return normalized;
        }
    }

    private static string? GetResolvedScriptPath(ServiceConfig config)
    {
        var executable = config.Executable.Trim();
        var extension = Path.GetExtension(executable);
        if (
            string.Equals(extension, ".ps1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".sh", StringComparison.OrdinalIgnoreCase)
        )
        {
            return ResolvePath(executable, config.WorkingDirectory);
        }

        return null;
    }

    /// <summary>
    /// 解析启动命令，将脚本文件转换为相应的主机命令
    /// </summary>
    private static (string FileName, string Arguments) ResolveStartCommand(ServiceConfig config)
    {
        var executable = config.Executable.Trim();
        var arguments = config.Arguments;
        var extension = Path.GetExtension(executable);

        if (string.Equals(extension, ".ps1", StringComparison.OrdinalIgnoreCase))
        {
            var scriptPath = ResolvePath(executable, config.WorkingDirectory);
            var host = FindExecutableInPath("pwsh") ?? "powershell";
            var hostArguments =
                $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {arguments}".Trim();
            return (host, hostArguments);
        }

        if (
            string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase)
        )
        {
            var scriptPath = ResolvePath(executable, config.WorkingDirectory);
            var cmdArguments = $"/c \"\"{scriptPath}\" {arguments}\"".Trim();
            return ("cmd.exe", cmdArguments);
        }

        if (string.Equals(extension, ".sh", StringComparison.OrdinalIgnoreCase))
        {
            var scriptPath = ResolvePath(executable, config.WorkingDirectory);
            var host = FindExecutableInPath("bash") ?? FindExecutableInPath("sh") ?? "sh";
            var hostArguments = $"\"{scriptPath}\" {arguments}".Trim();
            return (host, hostArguments);
        }

        return (executable, arguments);
    }

    private static string ResolvePath(string path, string workingDirectory)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.Combine(workingDirectory, path);
    }

    private static string? FindExecutableInPath(string fileName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        foreach (var path in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(path, fileName);
                if (OperatingSystem.IsWindows() && !Path.HasExtension(candidate))
                {
                    candidate += ".exe";
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception ex)
            {
                // 路径项无效或不可访问
                Log.Warning(
                    ex,
                    "Invalid or inaccessible PATH segment encountered: {PathSegment}",
                    path
                );
            }
        }

        return null;
    }

    private static bool IsAspNetCoreService(ServiceConfig config)
    {
        var workingDirectory = config.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return false;
        }

        if (File.Exists(Path.Combine(workingDirectory, "appsettings.json")))
        {
            return true;
        }

        if (File.Exists(Path.Combine(workingDirectory, "Properties", "launchSettings.json")))
        {
            return true;
        }

        foreach (var csprojPath in Directory.EnumerateFiles(workingDirectory, "*.csproj"))
        {
            try
            {
                var content = File.ReadAllText(csprojPath);
                if (
                    content.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)
                    || content.Contains(
                        "Microsoft.AspNetCore.App",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                // 忽略单个项目文件读取失败
                Log.Warning(
                    ex,
                    "Failed to inspect project file for ASP.NET detection: {ProjectFile}",
                    csprojPath
                );
            }
        }

        return false;
    }
}
