using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Dpz.ServiceHub.ViewModels;
using Serilog;

namespace Dpz.ServiceHub.Views;

public partial class FrontendBuildWindow : Window
{
    private readonly NativeWebView? _logWebView;
    private FrontendBuildViewModel? _viewModel;
    private bool _terminalReady;

    public FrontendBuildWindow()
    {
        InitializeComponent();
        _logWebView = this.FindControl<NativeWebView>("BuildLogWebView");
        Opened += OnOpened;
        DataContextChanged += OnDataContextChanged;

        if (_logWebView != null)
        {
            _logWebView.Focusable = false;
            _logWebView.IsTabStop = false;

            var terminalHtmlPath = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "terminal.html"
            );
            if (File.Exists(terminalHtmlPath))
            {
                var fileUri = new Uri($"file:///{terminalHtmlPath.Replace("\\", "/")}");
                _logWebView.Navigate(fileUri);
            }
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        UnsubscribeViewModel();
        if (DataContext is FrontendBuildViewModel vm)
        {
            if (vm.IsBusy)
            {
                vm.CancelCommand.Execute(null);
            }

            vm.Persist();
        }

        base.OnClosing(e);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        BindViewModel(DataContext as FrontendBuildViewModel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        BindViewModel(DataContext as FrontendBuildViewModel);
    }

    private void BindViewModel(FrontendBuildViewModel? vm)
    {
        if (ReferenceEquals(_viewModel, vm))
        {
            return;
        }

        UnsubscribeViewModel();
        _viewModel = vm;
        if (_viewModel == null)
        {
            return;
        }

        _viewModel.RequestUploadConfirmationAsync = ConfirmUploadAsync;
        _viewModel.LogChunkReceived += OnLogChunkReceived;
        _viewModel.LogReset += OnLogReset;
    }

    private void UnsubscribeViewModel()
    {
        if (_viewModel == null)
        {
            return;
        }

        _viewModel.LogChunkReceived -= OnLogChunkReceived;
        _viewModel.LogReset -= OnLogReset;
        _viewModel = null;
    }

    private async Task<UploadConfirmResult> ConfirmUploadAsync(string prefix)
    {
        var dialog = new UploadConfirmWindow { RemotePrefix = prefix };
        await dialog.ShowDialog(this);
        return new UploadConfirmResult(dialog.DialogResult, dialog.RemotePrefix);
    }

    private void OnWebViewNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
    }

    private async void OnWebViewMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (e.Body != "terminal-ready")
        {
            return;
        }

        _terminalReady = true;
        var buffer = _viewModel?.LogBuffer;
        if (string.IsNullOrEmpty(buffer))
        {
            await ResetTerminalAsync();
            return;
        }

        await WriteToTerminalAsync(buffer, reset: true);
    }

    private void OnLogChunkReceived(object? sender, string chunk)
    {
        if (!_terminalReady)
        {
            return;
        }

        _ = WriteToTerminalAsync(chunk, reset: false);
    }

    private void OnLogReset(object? sender, EventArgs e)
    {
        if (!_terminalReady)
        {
            return;
        }

        _ = ResetTerminalAsync();
    }

    private async Task ResetTerminalAsync()
    {
        if (_logWebView == null || !_terminalReady)
        {
            return;
        }

        try
        {
            await _logWebView.InvokeScript("window.terminalClear()");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to clear frontend build terminal.");
        }
    }

    private async Task WriteToTerminalAsync(string text, bool reset)
    {
        if (_logWebView == null || !_terminalReady || string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            var escapedText = JsonSerializer.Serialize(text);
            var script = reset
                ? $"window.terminalReset(); window.terminalWrite({escapedText});"
                : $"window.terminalWrite({escapedText})";
            await _logWebView.InvokeScript(script);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to write frontend build terminal output.");
        }
    }

    private async void OnBrowseWorkingDirectoryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FrontendBuildViewModel vm)
        {
            return;
        }

        var result = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "选择工作目录", AllowMultiple = false }
        );

        if (result.Count > 0)
        {
            vm.WorkingDirectory = result[0].Path.LocalPath;
        }
    }

    private async void OnAddArtifactFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FrontendBuildViewModel vm)
        {
            return;
        }

        var result = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "选择产物目录", AllowMultiple = true }
        );

        foreach (var folder in result)
        {
            vm.AddArtifactPath(folder.Path.LocalPath);
        }
    }

    private async void OnAddArtifactFileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FrontendBuildViewModel vm)
        {
            return;
        }

        var result = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions { Title = "选择产物文件", AllowMultiple = true }
        );

        foreach (var file in result)
        {
            vm.AddArtifactPath(file.Path.LocalPath);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
