using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Dpz.ServiceHub.Models;

public sealed class FrontendBuildProfile : INotifyPropertyChanged
{
    private string _name = "新构建";

    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            OnPropertyChanged();
        }
    }

    [JsonPropertyName("workingDirectory")]
    public string WorkingDirectory { get; set; } = string.Empty;

    [JsonPropertyName("executable")]
    public string Executable { get; set; } = "pwsh";

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty;

    [JsonPropertyName("artifactPaths")]
    public List<string> ArtifactPaths { get; set; } = [];

    [JsonPropertyName("defaultRemotePrefix")]
    public string DefaultRemotePrefix { get; set; } = string.Empty;

    [JsonPropertyName("s3Endpoint")]
    public string S3Endpoint { get; set; } = string.Empty;

    [JsonPropertyName("s3Bucket")]
    public string S3Bucket { get; set; } = string.Empty;

    [JsonPropertyName("s3Region")]
    public string S3Region { get; set; } = string.Empty;

    [JsonPropertyName("s3AccessKeyProtected")]
    public string S3AccessKeyProtected { get; set; } = string.Empty;

    [JsonPropertyName("s3SecretKeyProtected")]
    public string S3SecretKeyProtected { get; set; } = string.Empty;

    [JsonPropertyName("s3ForcePathStyle")]
    public bool S3ForcePathStyle { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
