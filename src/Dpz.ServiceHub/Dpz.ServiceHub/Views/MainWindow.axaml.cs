using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Dpz.ServiceHub.Models;
using Dpz.ServiceHub.Services;
using Dpz.ServiceHub.ViewModels;
using Serilog;

namespace Dpz.ServiceHub.Views;

public partial class MainWindow : Window
{
    // Avalonia 12 进程内强类型拖拽格式：仅在本进程的 DnD 中传递 ServiceInfo 引用，不做任何序列化。
    private static readonly DataFormat<ServiceInfo> ServiceDragFormat =
        DataFormat.CreateInProcessFormat<ServiceInfo>("Dpz.ServiceHub.ServiceInfo");

    private const double DragThreshold = 5.0;

    private readonly AppSettingsStore _appSettingsStore = new();
    private readonly NativeWebView? _consoleWebView;
    private readonly ListBox? _serviceList;
    private readonly Border? _dropIndicator;
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
    private bool _isInTrayMode;
    private bool _isClosed;

    // 拖拽排序状态
    private Point? _dragStartPosition;
    private ServiceInfo? _potentialDragService;
    private PointerPressedEventArgs? _potentialDragTrigger;
    private bool _isDragging;

    public MainWindow()
    {
        InitializeComponent();
        RestoreWindowBounds();

        _consoleWebView = this.FindControl<NativeWebView>("ConsoleWebView");
        _serviceList = this.FindControl<ListBox>("ServiceList");
        _dropIndicator = this.FindControl<Border>("DropIndicator");

        if (_serviceList != null)
        {
            // Tunnel 阶段拿到指针按下事件，源头若是 Button 则跳过，避免与控制按钮冲突。
            _serviceList.AddHandler(
                InputElement.PointerPressedEvent,
                OnServiceListPointerPressed,
                RoutingStrategies.Tunnel,
                handledEventsToo: true
            );
            _serviceList.AddHandler(
                InputElement.PointerMovedEvent,
                OnServiceListPointerMoved,
                RoutingStrategies.Tunnel,
                handledEventsToo: true
            );
            _serviceList.AddHandler(
                InputElement.PointerReleasedEvent,
                OnServiceListPointerReleased,
                RoutingStrategies.Tunnel,
                handledEventsToo: true
            );
            _serviceList.AddHandler(InputElement.PointerCaptureLostEvent, OnServiceListPointerCaptureLost);
            _serviceList.AddHandler(DragDrop.DragOverEvent, OnServiceListDragOver);
            _serviceList.AddHandler(DragDrop.DropEvent, OnServiceListDrop);
            _serviceList.AddHandler(DragDrop.DragLeaveEvent, OnServiceListDragLeave);
        }

        if (_consoleWebView != null)
        {
            _consoleWebView.Focusable = false;
            _consoleWebView.IsTabStop = false;

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

        Activated += OnWindowActivated;
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

        Activated -= OnWindowActivated;

        base.OnClosed(e);
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        // 避免任务栏恢复时焦点直接落入 WebView2，降低 MoveFocus 触发概率。
        if (_serviceList == null || _isInTrayMode)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                try
                {
                    if (_serviceList.IsVisible && _serviceList.IsEffectivelyEnabled)
                    {
                        _serviceList.Focus();
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to apply activation focus guard.");
                }
            },
            DispatcherPriority.Background
        );
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

            if (_isInTrayMode)
            {
                return;
            }

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
        if (_consoleWebView == null || !_terminalReady || _isUpdatingTerminal || _isInTrayMode)
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
            || _isInTrayMode
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
            _isClosed = true;
            return;
        }

        // 如果是最小化到托盘动作，隐藏窗口
        if (_minimizeToTray)
        {
            e.Cancel = true;
            EnterTrayMode();
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
            _isClosed = true;
            return;
        }

        var runningServices = _viewModel.GetManagedRunningServices();
        if (runningServices.Count == 0)
        {
            _isClosed = true;
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
        if (_isClosed)
        {
            Log.Warning("OnShowWindowClicked called but window is already closed. Ignoring.");
            return;
        }

        ExitTrayMode();
        try
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            UpdateTerminalDisplay();
        }
        catch (InvalidOperationException ex)
        {
            Log.Error(ex, "Failed to show window in OnShowWindowClicked. Window may have been closed externally.");
            _isClosed = true;
        }
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
                if (_isClosed)
                {
                    Log.Warning("OnExitClicked called but window is already closed. Ignoring.");
                    return;
                }

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

    private void EnterTrayMode()
    {
        _isInTrayMode = true;
        _updateDebounceTimer?.Stop();

        if (_consoleWebView == null)
        {
            return;
        }

        _consoleWebView.IsEnabled = false;
        _consoleWebView.IsVisible = false;
    }

    private void ExitTrayMode()
    {
        _isInTrayMode = false;

        if (_consoleWebView == null)
        {
            return;
        }

        _consoleWebView.IsVisible = true;
        _consoleWebView.IsEnabled = true;
    }

    // -------------------- 拖拽排序 --------------------

    /// <summary>
    /// 指针按下：记录潜在的拖拽起点。仅当左键、不在按钮内、且命中某个 ListBoxItem 时才追踪。
    /// </summary>
    private void OnServiceListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_serviceList == null)
        {
            return;
        }

        var properties = e.GetCurrentPoint(_serviceList).Properties;
        if (!properties.IsLeftButtonPressed)
        {
            ResetPotentialDrag();
            return;
        }

        // 点击在 Button 上时不触发拖拽，让控件原本的命令逻辑生效。
        if (IsInsideButton(e.Source as Visual))
        {
            ResetPotentialDrag();
            return;
        }

        var listBoxItem = FindAncestor<ListBoxItem>(e.Source as Visual);
        if (listBoxItem?.DataContext is not ServiceInfo serviceInfo)
        {
            ResetPotentialDrag();
            return;
        }

        _potentialDragService = serviceInfo;
        _potentialDragTrigger = e;
        _dragStartPosition = e.GetPosition(_serviceList);
    }

    /// <summary>
    /// 指针移动：超过阈值时正式发起 DoDragDropAsync。
    /// 使用按下事件作为 trigger 满足 Avalonia 12 的 API 约束，并避免一按下就触发拖拽。
    /// </summary>
    private async void OnServiceListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (
            _isDragging
            || _serviceList == null
            || _dragStartPosition is null
            || _potentialDragService is null
            || _potentialDragTrigger is null
        )
        {
            return;
        }

        var properties = e.GetCurrentPoint(_serviceList).Properties;
        if (!properties.IsLeftButtonPressed)
        {
            ResetPotentialDrag();
            return;
        }

        var current = e.GetPosition(_serviceList);
        var delta = current - _dragStartPosition.Value;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        var serviceToDrag = _potentialDragService;
        var trigger = _potentialDragTrigger;
        ResetPotentialDrag();
        _isDragging = true;

        try
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(ServiceDragFormat, serviceToDrag));
            await DragDrop.DoDragDropAsync(trigger, transfer, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Drag-drop operation failed for service {ServiceName}.", serviceToDrag.Config.Name);
        }
        finally
        {
            _isDragging = false;
            HideDropIndicator();
        }
    }

    private void OnServiceListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ResetPotentialDrag();
    }

    private void OnServiceListPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        ResetPotentialDrag();
    }

    /// <summary>
    /// 拖拽悬停：根据指针位置计算插入索引并显示指示线。
    /// </summary>
    private void OnServiceListDragOver(object? sender, DragEventArgs e)
    {
        if (_serviceList == null || !e.DataTransfer.Contains(ServiceDragFormat))
        {
            e.DragEffects = DragDropEffects.None;
            HideDropIndicator();
            return;
        }

        e.DragEffects = DragDropEffects.Move;

        var dropIndex = ComputeDropIndex(e.GetPosition(_serviceList).Y);
        UpdateDropIndicator(dropIndex);
    }

    private void OnServiceListDragLeave(object? sender, RoutedEventArgs e)
    {
        HideDropIndicator();
    }

    /// <summary>
    /// 放下：把服务移动到目标位置，由 ViewModel 负责持久化。
    /// </summary>
    private async void OnServiceListDrop(object? sender, DragEventArgs e)
    {
        try
        {
            if (
                _serviceList == null
                || _viewModel == null
                || !e.DataTransfer.Contains(ServiceDragFormat)
            )
            {
                return;
            }

            var dragged = e.DataTransfer.TryGetValue(ServiceDragFormat);
            if (dragged is null)
            {
                return;
            }

            var dropIndex = ComputeDropIndex(e.GetPosition(_serviceList).Y);
            await _viewModel.MoveServiceAsync(dragged, dropIndex);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to handle drop for service reorder.");
        }
        finally
        {
            HideDropIndicator();
        }
    }

    private void ResetPotentialDrag()
    {
        _dragStartPosition = null;
        _potentialDragService = null;
        _potentialDragTrigger = null;
    }

    /// <summary>
    /// 根据 Y 坐标决定要插入到哪个索引（取每个 item 的中线作为分界）。
    /// 返回值为「移除前」的插入索引：0 表示放到最前；Count 表示放到最后。
    /// </summary>
    private int ComputeDropIndex(double pointerY)
    {
        if (_serviceList == null || _viewModel == null)
        {
            return 0;
        }

        var count = _viewModel.Services.Count;
        for (var i = 0; i < count; i++)
        {
            if (_serviceList.ContainerFromIndex(i) is not Control container)
            {
                continue;
            }

            var topInList = container.TranslatePoint(default, _serviceList);
            if (topInList is null)
            {
                continue;
            }

            var middleY = topInList.Value.Y + container.Bounds.Height / 2;
            if (pointerY < middleY)
            {
                return i;
            }
        }

        return count;
    }

    /// <summary>
    /// 把指示线移动到指定插入索引对应的 Y 位置。
    /// 当 dropIndex == Count 时，指示线显示在最后一项的下边缘。
    /// </summary>
    private void UpdateDropIndicator(int dropIndex)
    {
        if (_dropIndicator is null || _serviceList is null || _viewModel is null)
        {
            return;
        }

        var count = _viewModel.Services.Count;
        if (count == 0)
        {
            HideDropIndicator();
            return;
        }

        double y;
        if (dropIndex >= count)
        {
            if (_serviceList.ContainerFromIndex(count - 1) is not Control lastContainer)
            {
                HideDropIndicator();
                return;
            }

            var topInList = lastContainer.TranslatePoint(default, _serviceList);
            if (topInList is null)
            {
                HideDropIndicator();
                return;
            }

            y = topInList.Value.Y + lastContainer.Bounds.Height - 1;
        }
        else
        {
            if (_serviceList.ContainerFromIndex(dropIndex) is not Control targetContainer)
            {
                HideDropIndicator();
                return;
            }

            var topInList = targetContainer.TranslatePoint(default, _serviceList);
            if (topInList is null)
            {
                HideDropIndicator();
                return;
            }

            y = topInList.Value.Y;
        }

        if (y < 0)
        {
            y = 0;
        }

        _dropIndicator.Margin = new Thickness(0, y, 0, 0);
        _dropIndicator.IsVisible = true;
    }

    private void HideDropIndicator()
    {
        if (_dropIndicator != null)
        {
            _dropIndicator.IsVisible = false;
        }
    }

    private static bool IsInsideButton(Visual? source)
    {
        var current = source;
        while (current != null)
        {
            if (current is Button)
            {
                return true;
            }

            current = current.GetVisualParent();
        }

        return false;
    }

    private static T? FindAncestor<T>(Visual? source)
        where T : class
    {
        var current = source;
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current.GetVisualParent();
        }

        return null;
    }
}
