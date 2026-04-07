namespace Dpz.ServiceHub.Models;

/// <summary>
/// 服务运行状态
/// </summary>
public enum ServiceStatus
{
    /// <summary>
    /// 已停止
    /// </summary>
    Stopped,

    /// <summary>
    /// 正在启动
    /// </summary>
    Starting,

    /// <summary>
    /// 运行中
    /// </summary>
    Running,

    /// <summary>
    /// 正在停止
    /// </summary>
    Stopping,

    /// <summary>
    /// 外部进程（不是本工具启动的）
    /// </summary>
    External
}
