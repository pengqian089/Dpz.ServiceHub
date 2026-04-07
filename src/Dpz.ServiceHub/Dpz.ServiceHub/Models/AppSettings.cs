using System.Text.Json.Serialization;

namespace Dpz.ServiceHub.Models;

public sealed class AppSettings
{
    [JsonPropertyName("windowWidth")]
    public double? WindowWidth { get; set; }

    [JsonPropertyName("windowHeight")]
    public double? WindowHeight { get; set; }

    [JsonPropertyName("windowPosX")]
    public int? WindowPosX { get; set; }

    [JsonPropertyName("windowPosY")]
    public int? WindowPosY { get; set; }

    [JsonPropertyName("autoDetectExternalProcesses")]
    public bool AutoDetectExternalProcesses { get; set; } = true;

    [JsonPropertyName("detectionIntervalSeconds")]
    public int DetectionIntervalSeconds { get; set; } = 3;

    [JsonPropertyName("maxOutputLines")]
    public int MaxOutputLines { get; set; } = 1000;
}
