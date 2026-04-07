using System.Globalization;
using Avalonia.Data.Converters;
using Dpz.ServiceHub.Models;

namespace Dpz.ServiceHub.Converters;

/// <summary>
/// 服务状态到 CSS 类名的转换器
/// </summary>
public sealed class StatusToClassConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ServiceStatus status)
        {
            return "status-stopped";
        }

        return status switch
        {
            ServiceStatus.Stopped => "status-stopped",
            ServiceStatus.Running => "status-running",
            ServiceStatus.Starting => "status-starting",
            ServiceStatus.Stopping => "status-stopping",
            ServiceStatus.External => "status-external",
            _ => "status-stopped",
        };
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    )
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 检查服务状态是否为已停止
/// </summary>
public sealed class StatusIsStoppedConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ServiceStatus status)
        {
            return false;
        }

        return status == ServiceStatus.Stopped;
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    )
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 检查服务状态是否为运行中
/// </summary>
public sealed class StatusIsRunningConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ServiceStatus status)
        {
            return false;
        }

        return status == ServiceStatus.Running || status == ServiceStatus.External;
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    )
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 启动按钮是否启用（已停止且未执行中）
/// </summary>
public sealed class CanStartConverter : IMultiValueConverter
{
    public object? Convert(
        IList<object?> values,
        Type targetType,
        object? parameter,
        CultureInfo culture
    )
    {
        if (values.Count != 2)
        {
            return false;
        }

        var status = values[0] as ServiceStatus?;
        var isExecuting = values[1] as bool?;

        if (!status.HasValue || !isExecuting.HasValue)
        {
            return false;
        }

        return status.Value == ServiceStatus.Stopped && !isExecuting.Value;
    }
}

/// <summary>
/// 停止/重启/查看按钮是否启用（运行中或外部进程，且未执行中）
/// </summary>
public sealed class CanStopConverter : IMultiValueConverter
{
    public object? Convert(
        IList<object?> values,
        Type targetType,
        object? parameter,
        CultureInfo culture
    )
    {
        if (values.Count != 2)
        {
            return false;
        }

        var status = values[0] as ServiceStatus?;
        var isExecuting = values[1] as bool?;

        if (!status.HasValue || !isExecuting.HasValue)
        {
            return false;
        }

        return (status.Value == ServiceStatus.Running || status.Value == ServiceStatus.External)
            && !isExecuting.Value;
    }
}

/// <summary>
/// 字符串是否有值
/// </summary>
public sealed class StringHasValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string text && !string.IsNullOrWhiteSpace(text);
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    )
    {
        throw new NotImplementedException();
    }
}
