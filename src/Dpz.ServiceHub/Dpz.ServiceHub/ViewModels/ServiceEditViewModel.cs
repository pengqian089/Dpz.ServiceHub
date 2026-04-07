using CommunityToolkit.Mvvm.ComponentModel;
using Dpz.ServiceHub.Models;

namespace Dpz.ServiceHub.ViewModels;

public sealed partial class ServiceEditViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _workingDirectory = string.Empty;

    [ObservableProperty]
    private string _executable = "dotnet";

    [ObservableProperty]
    private string _arguments = string.Empty;

    [ObservableProperty]
    private string _serviceUrl = string.Empty;

    [ObservableProperty]
    private string _ports = string.Empty;

    [ObservableProperty]
    private bool _autoStart;

    public ServiceEditViewModel()
    {
    }

    public ServiceEditViewModel(ServiceConfig config)
    {
        Name = config.Name;
        WorkingDirectory = config.WorkingDirectory;
        Executable = config.Executable;
        Arguments = config.Arguments;
        ServiceUrl = config.ServiceUrl ?? string.Empty;
        Ports = config.Ports.Count > 0 ? string.Join(", ", config.Ports) : string.Empty;
        AutoStart = config.AutoStart;
    }

    public ServiceConfig ToServiceConfig()
    {
        var ports = ParsePorts(Ports);
        return new ServiceConfig
        {
            Name = Name,
            WorkingDirectory = WorkingDirectory,
            Executable = Executable,
            Arguments = Arguments,
            ServiceUrl = string.IsNullOrWhiteSpace(ServiceUrl) ? null : ServiceUrl.Trim(),
            Ports = ports,
            AutoStart = AutoStart
        };
    }

    private static List<int> ParsePorts(string portsText)
    {
        if (string.IsNullOrWhiteSpace(portsText))
        {
            return [];
        }

        return portsText
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => int.TryParse(s.Trim(), out var port) && port > 0 && port <= 65535)
            .Select(s => int.Parse(s.Trim()))
            .Distinct()
            .OrderBy(p => p)
            .ToList();
    }
}
