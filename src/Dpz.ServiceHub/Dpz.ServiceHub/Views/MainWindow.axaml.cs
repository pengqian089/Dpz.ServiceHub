using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Dpz.ServiceHub.Models;
using Dpz.ServiceHub.Services;
using Dpz.ServiceHub.ViewModels;
using Serilog;

namespace Dpz.ServiceHub.Views;

public partial class MainWindow : Window
{
    private readonly AppSettingsStore _appSettingsStore = new();
    private readonly NativeWebView? _consoleWebView;
    private readonly TrayIcon? _trayIcon;
    private MainWindowViewModel? _viewModel;
    private ServiceInfo? _selectedService;
    private bool _allowClose;
    private bool _isClosingFlowRunning;
    private bool _minimizeToTray;
    private bool _terminalReady;
    private string _lastRenderedOutput = string.Empty;
    private bool _isUpdatingTerminal;
    private DispatcherTimer? _updateDebounceTimer;
    private string _pendingOutput = string.Empty;
    private CancellationTokenSource? _shutdownFlowCancellationTokenSource;

    public MainWindow()
    {
        InitializeComponent();
        RestoreWindowBounds();

        _consoleWebView = this.FindControl<NativeWebView>("ConsoleWebView");
        if (_consoleWebView != null)
        {
            // 加载终端HTML页面
            var terminalHtmlPath = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "terminal.html"
            );

            if (File.Exists(terminalHtmlPath))
            {
                // 使用file://协议加载本地HTML文件
                var fileUri = new Uri($"file:///{terminalHtmlPath.Replace("\\", "/")}");
                _consoleWebView.Navigate(fileUri);
            }
        }

        // 创建托盘图标
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(
                AssetLoader.Open(new Uri("avares://Dpz.ServiceHub/Assets/Dpz.ServiceHub.ico"))
            ),
            ToolTipText = "服务控制面板",
            Menu = new NativeMenu
            {
                Items =
                {
                    new NativeMenuItem
                    {
                        Header = "显示窗口",
                        Command = new RelayCommand(() =>
                            OnShowWindowClicked(null, EventArgs.Empty)
                        ),
                    },
                    new NativeMenuItemSeparator(),
                    new NativeMenuItem
                    {
                        Header = "退出",
                        Command = new RelayCommand(() => OnExitClicked(null, EventArgs.Empty)),
                    },
                },
            },
        };
        _trayIcon.Clicked += (_, _) => OnTrayIconClicked(null, EventArgs.Empty);

        // 初始化防抖定时器（50ms延迟，避免快速更新导致闪烁）
        _updateDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _updateDebounceTimer.Tick += OnDebounceTimerTick;

        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        PersistWindowBounds();

        // 清理定时器
        if (_updateDebounceTimer != null)
        {
            _updateDebounceTimer.Stop();
            _updateDebounceTimer.Tick -= OnDebounceTimerTick;
            _updateDebounceTimer = null;
        }

        // 清理托盘图标
        _trayIcon?.Dispose();

        if (_shutdownFlowCancellationTokenSource != null)
        {
            _shutdownFlowCancellationTokenSource.Cancel();
            _shutdownFlowCancellationTokenSource.Dispose();
            _shutdownFlowCancellationTokenSource = null;
        }

        base.OnClosed(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            AttachSelectedService(_viewModel.SelectedService);
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e
    )
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedService) && _viewModel != null)
        {
            AttachSelectedService(_viewModel.SelectedService);
        }
    }

    private void AttachSelectedService(ServiceInfo? service)
    {
        if (_selectedService != null)
        {
            _selectedService.PropertyChanged -= OnSelectedServicePropertyChanged;
        }

        _selectedService = service;
        _lastRenderedOutput = string.Empty;

        if (_selectedService != null)
        {
            _selectedService.PropertyChanged += OnSelectedServicePropertyChanged;
        }

        // 如果终端已准备就绪，立即更新显示
        if (_terminalReady)
        {
            UpdateTerminalDisplay();
        }
    }

    private void OnSelectedServicePropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e
    )
    {
        if (e.PropertyName == nameof(ServiceInfo.OutputText) && _terminalReady)
        {
            // 使用防抖机制，避免快速连续更新导致闪烁和重复
            _pendingOutput = _selectedService?.OutputText ?? string.Empty;

            // 重启定时器
            _updateDebounceTimer?.Stop();
            _updateDebounceTimer?.Start();
        }
    }

    /// <summary>
    /// 防抖定时器触发 - 执行实际的终端更新
    /// </summary>
    private void OnDebounceTimerTick(object? sender, EventArgs e)
    {
        _updateDebounceTimer?.Stop();

        if (_isUpdatingTerminal)
        {
            return;
        }

        var currentOutput = _pendingOutput;

        // 检测清空操作（OutputText变为空）
        if (string.IsNullOrEmpty(currentOutput) && !string.IsNullOrEmpty(_lastRenderedOutput))
        {
            // 清空终端
            UpdateTerminalDisplay();
            return;
        }

        // 空输出不处理
        if (string.IsNullOrEmpty(currentOutput))
        {
            return;
        }

        // 增量更新
        if (
            !string.IsNullOrEmpty(_lastRenderedOutput)
            && currentOutput.StartsWith(_lastRenderedOutput, StringComparison.Ordinal)
            && currentOutput.Length > _lastRenderedOutput.Length
        )
        {
            var delta = currentOutput[_lastRenderedOutput.Length..];
            WriteToTerminal(delta, forceFullWrite: false);
        }
        else if (currentOutput != _lastRenderedOutput)
        {
            // 内容不连续或完全不同，全量更新
            UpdateTerminalDisplay();
        }
    }

    /// <summary>
    /// WebView 导航完成事件
    /// </summary>
    private void OnWebViewNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        // 导航成功后，终端会通过 invokeCSharpAction('terminal-ready') 通知准备就绪
    }

    /// <summary>
    /// WebView 消息接收事件
    /// </summary>
    private void OnWebViewMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (e.Body == "terminal-ready")
        {
            _terminalReady = true;
            // 终端准备就绪，更新当前选中服务的输出
            UpdateTerminalDisplay();
        }
    }

    /// <summary>
    /// 更新终端显示（全量刷新）
    /// </summary>
    private async void UpdateTerminalDisplay()
    {
        if (_consoleWebView == null || !_terminalReady || _isUpdatingTerminal)
        {
            return;
        }

        try
        {
            _isUpdatingTerminal = true;
            var currentOutput = _selectedService?.OutputText ?? string.Empty;

            // 使用terminalReset+write避免闪烁，比clear+write更流畅
            if (string.IsNullOrEmpty(currentOutput))
            {
                await _consoleWebView.InvokeScript("window.terminalClear()");
                _lastRenderedOutput = string.Empty;
            }
            else
            {
                var escapedText = JsonSerializer.Serialize(currentOutput);
                // 一次性清空并写入，减少闪烁
                await _consoleWebView.InvokeScript(
                    $"window.terminalReset(); window.terminalWrite({escapedText});"
                );
                _lastRenderedOutput = currentOutput;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"更新终端显示失败: {ex.Message}");
            Log.Error(ex, "Failed to update terminal display.");
        }
        finally
        {
            _isUpdatingTerminal = false;
        }
    }

    /// <summary>
    /// 向终端写入文本（增量）
    /// </summary>
    private async void WriteToTerminal(string text, bool forceFullWrite = false)
    {
        if (
            _consoleWebView == null
            || !_terminalReady
            || string.IsNullOrEmpty(text)
            || _isUpdatingTerminal
        )
        {
            return;
        }

        try
        {
            // JavaScript转义
            var escapedText = JsonSerializer.Serialize(text);
            await _consoleWebView.InvokeScript($"window.terminalWrite({escapedText})");

            // 更新已渲染的文本
            if (forceFullWrite)
            {
                _lastRenderedOutput = text;
            }
            else
            {
                _lastRenderedOutput += text;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"写入终端失败: {ex.Message}");
            Log.Error(ex, "Failed to write incremental terminal output.");
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (_allowClose)
        {
            return;
        }

        // 如果是最小化到托盘动作，隐藏窗口
        if (_minimizeToTray)
        {
            e.Cancel = true;
            Hide();
            _minimizeToTray = false;
            return;
        }

        if (_isClosingFlowRunning)
        {
            // 停止流程进行中，屏蔽再次关闭请求，避免状态竞争。
            e.Cancel = true;
            return;
        }

        if (_viewModel == null)
        {
            return;
        }

        var runningServices = _viewModel.GetManagedRunningServices();
        if (runningServices.Count == 0)
        {
            return;
        }

        e.Cancel = true;
        _isClosingFlowRunning = true;
        _ = HandleCloseWithConfirmationAsync(runningServices);
    }

    private async Task HandleCloseWithConfirmationAsync(IReadOnlyList<ServiceInfo> runningServices)
    {
        try
        {
            var dialog = new ShutdownConfirmWindow();
            dialog.SetServiceNames(runningServices.Select(s => s.Config.Name));

            var action = await dialog.ShowDialog<ShutdownAction>(this);
            if (action == ShutdownAction.Cancel)
            {
                return;
            }

            if (action == ShutdownAction.StopServicesAndExit && _viewModel != null)
            {
                IsEnabled = false;
                try
                {
                    _shutdownFlowCancellationTokenSource?.Cancel();
                    _shutdownFlowCancellationTokenSource?.Dispose();
                    _shutdownFlowCancellationTokenSource = new CancellationTokenSource(
                        TimeSpan.FromSeconds(45)
                    );

                    var allStopped = await _viewModel.StopManagedRunningServicesAsync(
                        _shutdownFlowCancellationTokenSource.Token
                    );

                    if (!allStopped || _viewModel.GetManagedRunningServices().Count > 0)
                    {
                        return;
                    }
                }
                catch (OperationCanceledException ex)
                {
                    Log.Warning(ex, "Shutdown flow canceled while stopping services before exit.");
                    return;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to stop all managed services during shutdown flow.");
                    return;
                }
                finally
                {
                    _shutdownFlowCancellationTokenSource?.Dispose();
                    _shutdownFlowCancellationTokenSource = null;
                }
            }

            _allowClose = true;
            Close();
        }
        finally
        {
            if (!_allowClose)
            {
                IsEnabled = true;
                _isClosingFlowRunning = false;
            }
        }
    }

    private void RestoreWindowBounds()
    {
        var settings = _appSettingsStore.Load();

        // 恢复窗口大小
        if (settings.WindowWidth.HasValue && settings.WindowHeight.HasValue)
        {
            Width = Math.Max(MinWidth, settings.WindowWidth.Value);
            Height = Math.Max(MinHeight, settings.WindowHeight.Value);
        }
    }

    private void PersistWindowBounds()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        // 保存窗口大小
        var width = Width;
        var height = Height;

        _appSettingsStore.Update(settings =>
        {
            settings.WindowWidth = width;
            settings.WindowHeight = height;
        });
    }

    /// <summary>
    /// 最小化到托盘按钮点击事件
    /// </summary>
    private void OnMinimizeToTrayClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _minimizeToTray = true;
        Close();
    }

    /// <summary>
    /// 托盘图标点击(单击/双击)事件
    /// </summary>
    private void OnTrayIconClicked(object? sender, EventArgs e)
    {
        OnShowWindowClicked(sender, e);
    }

    /// <summary>
    /// 托盘图标"显示窗口"菜单点击事件
    /// </summary>
    private void OnShowWindowClicked(object? sender, EventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>
    /// 托盘图标"退出"菜单点击事件
    /// </summary>
    private void OnExitClicked(object? sender, EventArgs e)
    {
        // 如果有运行中的服务，触发关闭确认流程
        if (_viewModel != null)
        {
            var runningServices = _viewModel.GetManagedRunningServices();
            if (runningServices.Count > 0)
            {
                // 先显示窗口，然后触发关闭流程
                Show();
                WindowState = WindowState.Normal;
                Activate();
                Close();
                return;
            }
        }

        // 没有运行中的服务，直接退出
        _allowClose = true;
        Close();
    }
}
