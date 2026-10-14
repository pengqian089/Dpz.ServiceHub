using System.Text.Json;
using Dpz.ServiceHub.Models;
using Serilog;

namespace Dpz.ServiceHub.Services;

public sealed class FrontendBuildStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _settingsPath;

    public FrontendBuildStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? GetDefaultPath();
    }

    public FrontendBuildSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new FrontendBuildSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<FrontendBuildSettings>(json);
            return settings ?? new FrontendBuildSettings();
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Failed to load frontend build settings from {SettingsPath}.",
                _settingsPath
            );
            return new FrontendBuildSettings();
        }
    }

    public void Save(FrontendBuildSettings settings)
    {
        try
        {
            EnsureDirectory();
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Failed to persist frontend build settings to {SettingsPath}.",
                _settingsPath
            );
        }
    }

    public async Task SaveAsync(
        FrontendBuildSettings settings,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            EnsureDirectory();
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(_settingsPath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Warning(
                ex,
                "Failed to persist frontend build settings to {SettingsPath}.",
                _settingsPath
            );
        }
    }

    public static string GetDefaultPath()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Dpz.ServiceHub"
        );
        return Path.Combine(appDataPath, "frontend-builds.json");
    }

    private void EnsureDirectory()
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
