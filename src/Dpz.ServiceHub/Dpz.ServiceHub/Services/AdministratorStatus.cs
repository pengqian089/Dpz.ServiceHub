using System.Runtime.Versioning;
using System.Security.Principal;

namespace Dpz.ServiceHub.Services;

internal static class AdministratorStatus
{
    public static bool IsAdministrator => OperatingSystem.IsWindows() && IsWindowsAdministrator();

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
