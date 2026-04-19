using System.Text.Json;
using Dpz.ServiceHub.Models;
using Serilog;

namespace Dpz.ServiceHub.Services;

public sealed class AppSettingsStore
{
    private readonly string _settingsPath;

    public AppSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? GetDefaultPath();
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return settings ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load app settings from {SettingsPath}.", _settingsPath);
            return new AppSettings();
        }
    }

    public void Update(Action<AppSettings> updater)
    {
        var settings = Load();
        updater(settings);
        Save(settings);
    }

    private void Save(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }
            );

            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to persist app settings to {SettingsPath}.", _settingsPath);
        }
    }

    public static string GetDefaultPath()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Dpz.ServiceHub"
        );
        return Path.Combine(appDataPath, "appsettings.json");
    }
}
