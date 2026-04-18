using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Dpz.ServiceHub.Models;

/// <summary>
/// 服务运行时信息
/// </summary>
public sealed partial class ServiceInfo : ObservableObject
{
    /// <summary>
    /// 服务配置
    /// </summary>
    public ServiceConfig Config { get; }

    /// <summary>
    /// 关联的进程
    /// </summary>
    public Process? Process { get; set; }

    /// <summary>
    /// 服务状态
    /// </summary>
    [ObservableProperty]
    private ServiceStatus _status = ServiceStatus.Stopped;

    /// <summary>
    /// 控制台输出内容
    /// </summary>
    [ObservableProperty]
    private string _outputText = string.Empty;

    /// <summary>
    /// 是否为外部进程
    /// </summary>
    [ObservableProperty]
    private bool _isExternal;

    /// <summary>
    /// 进程 ID
    /// </summary>
    [ObservableProperty]
    private int? _processId;

    /// <summary>
    /// 最后更新时间
    /// </summary>
    [ObservableProperty]
    private DateTime _lastUpdateTime = DateTime.Now;

    /// <summary>
    /// 是否正在执行操作
    /// </summary>
    [ObservableProperty]
    private bool _isExecuting;

    public ServiceInfo(ServiceConfig config)
    {
        Config = config;
    }

    /// <summary>
    /// 通知配置已更新，刷新绑定到 Config.* 的界面。
    /// </summary>
    public void NotifyConfigChanged()
    {
        OnPropertyChanged(nameof(Config));
        LastUpdateTime = DateTime.Now;
    }

    /// <summary>
    /// 追加输出文本
    /// </summary>
    public void AppendOutput(string text)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            AppendOutputCore(text);
            return;
        }

        Dispatcher.UIThread.Post(() => AppendOutputCore(text));
    }

    private void AppendOutputCore(string text)
    {
        // 保留ANSI代码，让WebView终端处理
        OutputText += text;

        // 限制输出文本长度，防止内存溢出
        const int maxLength = 500000; // 约500KB
        if (OutputText.Length > maxLength)
        {
            // 保留最后的文本
            OutputText = OutputText.Substring(OutputText.Length - maxLength);
        }

        LastUpdateTime = DateTime.Now;
    }

    /// <summary>
    /// 清空输出
    /// </summary>
    public void ClearOutput()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            OutputText = string.Empty;
            LastUpdateTime = DateTime.Now;
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            OutputText = string.Empty;
            LastUpdateTime = DateTime.Now;
        });
    }
}
