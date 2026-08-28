using System.Text.Json.Serialization;

namespace Dpz.ServiceHub.Models;

public sealed class FrontendBuildSettings
{
    [JsonPropertyName("profiles")]
    public List<FrontendBuildProfile> Profiles { get; set; } = [];
}
