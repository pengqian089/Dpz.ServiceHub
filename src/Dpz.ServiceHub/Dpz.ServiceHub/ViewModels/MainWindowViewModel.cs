using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dpz.ServiceHub.Models;
using Dpz.ServiceHub.Services;
using Dpz.ServiceHub.Views;

namespace Dpz.ServiceHub.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly ServiceManager _serviceManager;
    private readonly string _configPath;
    private readonly AppSettingsStore _appSettingsStore;
    private AppSettings _currentSettings;
    private CancellationTokenSource _refreshCancellationTokenSource = new();
    private int _stopOperationCount;

    [ObservableProperty]
    private ServiceInfo? _selectedService;

    [ObservableProperty]
    private string _commandInput = string.Empty;

    [ObservableProperty]
    private bool _isStoppingServices;

    public string StatusBarText => IsStoppingServices ? "停止中..." : "就绪";

    public MainWindowViewModel()
    {
        // 配置文件路径
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Dpz.ServiceHub"
        );
        _configPath = Path.Combine(appDataPath, "services.json");
        _serviceManager = new ServiceManager(_configPath);
        _appSettingsStore = new AppSettingsStore();
        _currentSettings = _appSettingsStore.Load();

        // 加载配置
        _ = InitializeAsync();
    }

    /// <summary>
    /// 服务列表
    /// </summary>
    public ObservableCollection<ServiceInfo> Services => _serviceManager.Services;

    public IReadOnlyList<ServiceInfo> GetManagedRunningServices()
    {
        return Services.Where(IsManagedRunningService).ToList();
    }

    public async Task<bool> StopManagedRunningServicesAsync(
        CancellationToken cancellationToken = default
    )
    {
        var allStopped = true;
        BeginStopProgress();
        var runningServices = GetManagedRunningServices();
        try
        {
            foreach (var service in runningServices)
            {
                service.IsExecuting = true;
                try
                {
                    var stopped = await _serviceManager.StopServiceAsync(
                        service,
                        cancellationToken
                    );
                    allStopped &= stopped;
                }
                finally
                {
                    service.IsExecuting = false;
                }
            }

            return allStopped;
        }
        finally
        {
            EndStopProgress();
        }
    }

    private static bool IsManagedRunningService(ServiceInfo service)
    {
        if (service.IsExternal || service.Process == null)
        {
            return false;
        }

        try
        {
            return !service.Process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 初始化
    /// </summary>
    private async Task InitializeAsync()
    {
        await _serviceManager.LoadConfigAsync();

        // 如果没有服务，添加示例服务
        if (Services.Count == 0)
        {
            _serviceManager.AddService(
                new ServiceConfig
                {
                    Name = "Garnet",
                    WorkingDirectory = @"C:\Users\pengq\Documents\project\garnet\main\GarnetServer",
                    Executable = "dotnet",
                    Arguments = "run -c Release -f net10.0",
                    AutoStart = false,
                    Order = 0,
                }
            );

            await _serviceManager.SaveConfigAsync();
        }

        // 检测外部进程
        await _serviceManager.DetectExternalProcessesAsync(_refreshCancellationTokenSource.Token);

        // 自动启动标记为自动启动的服务
        foreach (var service in Services.Where(s => s.Config.AutoStart))
        {
            await _serviceManager.StartServiceAsync(service);
            await Task.Delay(500); // 错开启动时间
        }

        // 启动定期刷新任务
        _ = StartPeriodicRefreshAsync(_refreshCancellationTokenSource.Token);
    }

    /// <summary>
    /// 定期刷新服务状态
    /// </summary>
    private async Task StartPeriodicRefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var intervalMs = Math.Max(1, _currentSettings.DetectionIntervalSeconds) * 1000;
                await Task.Delay(intervalMs, cancellationToken);

                if (_currentSettings.AutoDetectExternalProcesses)
                {
                    await _serviceManager.DetectExternalProcessesAsync(cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation.
        }
        catch (Exception)
        {
            // Ignore background refresh failures to keep UI responsive.
        }
    }

    partial void OnSelectedServiceChanged(ServiceInfo? value)
    {
        // 当选择服务变化时，OutputLines 已通过绑定自动更新
        // 不需要额外处理
    }

    [RelayCommand]
    private async Task StartServiceAsync(ServiceInfo? serviceInfo)
    {
        if (serviceInfo == null || serviceInfo.IsExecuting)
        {
            return;
        }

        try
        {
            serviceInfo.IsExecuting = true;
            await _serviceManager.StartServiceAsync(serviceInfo);
        }
        finally
        {
            serviceInfo.IsExecuting = false;
        }
    }

    [RelayCommand]
    private async Task StopServiceAsync(ServiceInfo? serviceInfo)
    {
        if (serviceInfo == null || serviceInfo.IsExecuting)
        {
            return;
        }

        BeginStopProgress();
        try
        {
            serviceInfo.IsExecuting = true;
            await _serviceManager.StopServiceAsync(serviceInfo);
        }
        finally
        {
            serviceInfo.IsExecuting = false;
            EndStopProgress();
        }
    }

    [RelayCommand]
    private async Task RestartServiceAsync(ServiceInfo? serviceInfo)
    {
        if (serviceInfo == null || serviceInfo.IsExecuting)
        {
            return;
        }

        try
        {
            serviceInfo.IsExecuting = true;
            await _serviceManager.RestartServiceAsync(serviceInfo);
        }
        finally
        {
            serviceInfo.IsExecuting = false;
        }
    }

    [RelayCommand]
    private async Task StartAllAsync()
    {
        foreach (var service in Services)
        {
            if (service.Status == ServiceStatus.Stopped)
            {
                await _serviceManager.StartServiceAsync(service);
                // 错开启动时间
                await Task.Delay(500);
            }
        }
    }

    [RelayCommand]
    private async Task StopAllAsync()
    {
        BeginStopProgress();
        try
        {
            foreach (var service in Services)
            {
                if (service.Status != ServiceStatus.Stopped)
                {
                    await _serviceManager.StopServiceAsync(service);
                }
            }
        }
        finally
        {
            EndStopProgress();
        }
    }

    [RelayCommand]
    private async Task RebuildAllAsync()
    {
        // TODO: 实现重建逻辑
        // 这里可以调用 dotnet build 命令
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void ClearOutput()
    {
        SelectedService?.ClearOutput();
    }

    [RelayCommand]
    private async Task SendCommandAsync()
    {
        if (SelectedService == null || string.IsNullOrWhiteSpace(CommandInput))
        {
            return;
        }

        await _serviceManager.SendCommandAsync(SelectedService, CommandInput);
        CommandInput = string.Empty;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await _serviceManager.DetectExternalProcessesAsync(_refreshCancellationTokenSource.Token);
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var currentSettings = _appSettingsStore.Load();
        var vm = new SettingsViewModel(currentSettings);
        var window = new SettingsWindow { DataContext = vm };

        var mainWindow = GetMainWindow();
        if (mainWindow == null)
        {
            return;
        }

        await window.ShowDialog(mainWindow);
        if (!window.DialogResult)
        {
            return;
        }

        _appSettingsStore.Update(settings => vm.ApplyTo(settings));
        _currentSettings = _appSettingsStore.Load();

        // 如果检测间隔改变，重启定期刷新任务
        _refreshCancellationTokenSource.Cancel();
        _refreshCancellationTokenSource.Dispose();
        _refreshCancellationTokenSource = new CancellationTokenSource();
        _ = StartPeriodicRefreshAsync(_refreshCancellationTokenSource.Token);
    }

    [RelayCommand]
    private async Task AddServiceAsync()
    {
        var vm = new ServiceEditViewModel();
        var window = new ServiceEditWindow { DataContext = vm };

        var mainWindow = GetMainWindow();
        if (mainWindow != null)
        {
            await window.ShowDialog(mainWindow);

            if (window.DialogResult)
            {
                var config = vm.ToServiceConfig();
                _serviceManager.AddService(config);
                await _serviceManager.SaveConfigAsync();
            }
        }
    }

    [RelayCommand]
    private async Task EditServiceAsync(ServiceInfo? serviceInfo)
    {
        if (serviceInfo == null)
        {
            return;
        }

        var vm = new ServiceEditViewModel(serviceInfo.Config);
        var window = new ServiceEditWindow { DataContext = vm, Title = "编辑服务配置" };

        var mainWindow = GetMainWindow();
        if (mainWindow != null)
        {
            await window.ShowDialog(mainWindow);

            if (window.DialogResult)
            {
                var config = vm.ToServiceConfig();
                serviceInfo.Config.Name = config.Name;
                serviceInfo.Config.WorkingDirectory = config.WorkingDirectory;
                serviceInfo.Config.Executable = config.Executable;
                serviceInfo.Config.Arguments = config.Arguments;
                serviceInfo.Config.ServiceUrl = config.ServiceUrl;
                serviceInfo.Config.Ports = config.Ports;
                serviceInfo.Config.AutoStart = config.AutoStart;
                serviceInfo.NotifyConfigChanged();

                await _serviceManager.SaveConfigAsync();
            }
        }
    }

    [RelayCommand]
    private async Task DeleteServiceAsync(ServiceInfo? serviceInfo)
    {
        if (serviceInfo == null)
        {
            return;
        }

        await _serviceManager.RemoveServiceAsync(serviceInfo);
        await _serviceManager.SaveConfigAsync();
    }

    [RelayCommand]
    private async Task SaveConfigAsync()
    {
        await _serviceManager.SaveConfigAsync();
    }

    [RelayCommand]
    private void SelectService(ServiceInfo? serviceInfo)
    {
        SelectedService = serviceInfo;
    }

    [RelayCommand]
    private void OpenServiceUrl(ServiceInfo? serviceInfo)
    {
        var url = serviceInfo?.Config.ServiceUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        try
        {
            Process.Start(
                new ProcessStartInfo { FileName = uri.ToString(), UseShellExecute = true }
            );
        }
        catch
        {
            // Ignore launch failure.
        }
    }

    private Window? GetMainWindow()
    {
        if (
            Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop
        )
        {
            return desktop.MainWindow;
        }
        return null;
    }

    partial void OnIsStoppingServicesChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusBarText));
    }

    private void BeginStopProgress()
    {
        _stopOperationCount++;
        if (_stopOperationCount == 1)
        {
            IsStoppingServices = true;
        }
    }

    private void EndStopProgress()
    {
        if (_stopOperationCount <= 0)
        {
            _stopOperationCount = 0;
            IsStoppingServices = false;
            return;
        }

        _stopOperationCount--;
        if (_stopOperationCount == 0)
        {
            IsStoppingServices = false;
        }
    }
}
