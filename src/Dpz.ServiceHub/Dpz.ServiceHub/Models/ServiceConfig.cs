using System.Text.Json.Serialization;

namespace Dpz.ServiceHub.Models;

/// <summary>
/// 服务配置信息
/// </summary>
public sealed class ServiceConfig
{
    /// <summary>
    /// 服务唯一标识
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 服务显示名称
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 工作目录
    /// </summary>
    [JsonPropertyName("workingDirectory")]
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 可执行文件或命令
    /// </summary>
    [JsonPropertyName("executable")]
    public string Executable { get; set; } = "dotnet";

    /// <summary>
    /// 命令行参数
    /// </summary>
    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty;

    /// <summary>
    /// 服务地址（可空）
    /// </summary>
    [JsonPropertyName("serviceUrl")]
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// 监听端口列表（用于检测服务是否运行）
    /// </summary>
    [JsonPropertyName("ports")]
    public List<int> Ports { get; set; } = [];

    /// <summary>
    /// 环境变量
    /// </summary>
    [JsonPropertyName("environmentVariables")]
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];

    /// <summary>
    /// 是否自动启动
    /// </summary>
    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; }

    /// <summary>
    /// 排序顺序
    /// </summary>
    [JsonPropertyName("order")]
    public int Order { get; set; }
}
