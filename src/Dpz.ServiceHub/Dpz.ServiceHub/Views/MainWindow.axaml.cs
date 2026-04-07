using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using CommunityToolkit.Mvvm.Input;
using Dpz.ServiceHub.Controls;
using Dpz.ServiceHub.Models;
using Dpz.ServiceHub.Services;
using Dpz.ServiceHub.ViewModels;

namespace Dpz.ServiceHub.Views;

public partial class MainWindow : Window
{
    private readonly AppSettingsStore _appSettingsStore = new();
    private readonly TextEditor? _consoleEditor;
    private readonly TrayIcon? _trayIcon;
    private MainWindowViewModel? _viewModel;
    private ServiceInfo? _selectedService;
    private string _lastRenderedOutput = string.Empty;
    private bool _forceFullRender = true;
    private bool _allowClose;
    private bool _isClosingFlowRunning;
    private bool _minimizeToTray;

    public MainWindow()
    {
        InitializeComponent();
        RestoreWindowBounds();

        _consoleEditor = this.FindControl<TextEditor>("ConsoleEditor");
        if (_consoleEditor != null)
        {
            _consoleEditor.Options = new TextEditorOptions { AllowScrollBelowDocument = false };
            _consoleEditor.TextArea.TextView.LineTransformers.Add(new ServiceLogColorizer());
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

        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        PersistWindowBounds();

        // 清理托盘图标
        _trayIcon?.Dispose();

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

        if (_selectedService != null)
        {
            _selectedService.PropertyChanged += OnSelectedServicePropertyChanged;
        }

        _lastRenderedOutput = string.Empty;
        _forceFullRender = true;

        UpdateEditorText();
    }

    private void OnSelectedServicePropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e
    )
    {
        if (e.PropertyName == nameof(ServiceInfo.OutputText))
        {
            UpdateEditorText();
        }
    }

    private void UpdateEditorText()
    {
        if (_consoleEditor == null)
        {
            return;
        }

        var wasAtBottom = IsAtBottom();

        var currentOutput = _selectedService?.OutputText ?? string.Empty;
        if (currentOutput.Length == 0)
        {
            _consoleEditor.Clear();
            _lastRenderedOutput = string.Empty;
            _forceFullRender = false;
            return;
        }

        if (_forceFullRender)
        {
            _consoleEditor.Text = currentOutput;
            _lastRenderedOutput = currentOutput;
            _forceFullRender = false;
        }
        else if (currentOutput.StartsWith(_lastRenderedOutput, StringComparison.Ordinal))
        {
            var delta = currentOutput[_lastRenderedOutput.Length..];
            if (!string.IsNullOrEmpty(delta))
            {
                _consoleEditor.AppendText(delta);
                _lastRenderedOutput = currentOutput;
            }
        }
        else
        {
            _consoleEditor.Text = currentOutput;
            _lastRenderedOutput = currentOutput;
        }

        if (wasAtBottom)
        {
            _consoleEditor.CaretOffset = _consoleEditor.Document?.TextLength ?? 0;
            _consoleEditor.ScrollToEnd();
        }
    }

    private bool IsAtBottom()
    {
        if (_consoleEditor == null)
        {
            return true;
        }

        var maxOffset = Math.Max(0, _consoleEditor.ExtentHeight - _consoleEditor.ViewportHeight);
        return _consoleEditor.VerticalOffset >= maxOffset - 1;
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

        if (_consoleEditor != null)
        {
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
                    await _viewModel.StopManagedRunningServicesAsync();
                }
                catch
                {
                    // 即使停止过程中出现异常，也继续退出，避免窗口卡住无法关闭。
                }
            }

            _allowClose = true;
            Close();
        }
        finally
        {
            IsEnabled = true;
            _isClosingFlowRunning = false;
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
