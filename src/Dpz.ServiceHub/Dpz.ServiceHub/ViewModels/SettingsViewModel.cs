using CommunityToolkit.Mvvm.ComponentModel;
using Dpz.ServiceHub.Models;

namespace Dpz.ServiceHub.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _autoDetectExternalProcesses;

    [ObservableProperty]
    private int _detectionIntervalSeconds;

    [ObservableProperty]
    private int _maxOutputLines;

    public SettingsViewModel(AppSettings settings)
    {
        AutoDetectExternalProcesses = settings.AutoDetectExternalProcesses;
        DetectionIntervalSeconds = settings.DetectionIntervalSeconds;
        MaxOutputLines = settings.MaxOutputLines;
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.AutoDetectExternalProcesses = AutoDetectExternalProcesses;
        settings.DetectionIntervalSeconds = Math.Max(1, Math.Min(60, DetectionIntervalSeconds));
        settings.MaxOutputLines = Math.Max(100, Math.Min(10000, MaxOutputLines));
    }
}
