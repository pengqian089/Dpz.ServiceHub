using System.Collections.ObjectModel;
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
    /// 带颜色的输出行
    /// </summary>
    public ObservableCollection<ConsoleOutputLine> OutputLines { get; } = [new ConsoleOutputLine()];

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

    /// <summary>
    /// 输出日志最大行数
    /// </summary>
    public int MaxOutputLines { get; set; } = 1000;

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
        OutputText += AnsiColorParser.RemoveAnsiCodes(text);

        var parsedLines = AnsiColorParser.ParseToLines(text);
        for (var i = 0; i < parsedLines.Count; i++)
        {
            if (OutputLines.Count == 0)
            {
                OutputLines.Add(new ConsoleOutputLine());
            }

            var targetLine = OutputLines[^1];
            foreach (var segment in parsedLines[i])
            {
                targetLine.Segments.Add(segment);
            }

            if (i < parsedLines.Count - 1)
            {
                OutputLines.Add(new ConsoleOutputLine());
            }
        }

        // 限制输出行数
        while (OutputLines.Count > MaxOutputLines)
        {
            OutputLines.RemoveAt(0);
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
            OutputLines.Clear();
            OutputLines.Add(new ConsoleOutputLine());
            LastUpdateTime = DateTime.Now;
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            OutputText = string.Empty;
            OutputLines.Clear();
            OutputLines.Add(new ConsoleOutputLine());
            LastUpdateTime = DateTime.Now;
        });
    }
}
