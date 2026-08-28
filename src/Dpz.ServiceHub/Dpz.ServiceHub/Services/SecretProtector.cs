using System.Security.Cryptography;
using System.Text;
using Serilog;

namespace Dpz.ServiceHub.Services;

public static class SecretProtector
{
    private static readonly byte[] AdditionalEntropy = SHA256.HashData(
        Encoding.UTF8.GetBytes("Dpz.ServiceHub.FrontendBuild.S3.v1")
    );

    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Secret protection requires Windows DPAPI."
            );
        }

        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectedData.Protect(
            bytes,
            AdditionalEntropy,
            DataProtectionScope.CurrentUser
        );
        return Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrWhiteSpace(protectedBase64))
        {
            return string.Empty;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Secret protection requires Windows DPAPI."
            );
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedBase64);
            var bytes = ProtectedData.Unprotect(
                protectedBytes,
                AdditionalEntropy,
                DataProtectionScope.CurrentUser
            );
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to unprotect a stored frontend-build secret.");
            return string.Empty;
        }
    }
}
