using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Reqviora.Infrastructure.Security;

public sealed class ReqvioraDataProtectionOptions
{
    public const string SectionName = "DataProtection";

    public string ApplicationName { get; set; } = "REQVIORA";
    public string KeysPath { get; set; } = string.Empty;
    public string KeyEncryptionMode { get; set; } = "Certificate";
    public string CertificatePath { get; set; } = string.Empty;
    public string CertificatePassword { get; set; } = string.Empty;
    public bool DpapiLocalMachine { get; set; }
}

public static class ReqvioraDataProtection
{
    public static IServiceCollection AddReqvioraDataProtection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = configuration.GetSection(ReqvioraDataProtectionOptions.SectionName)
            .Get<ReqvioraDataProtectionOptions>() ?? new ReqvioraDataProtectionOptions();
        var builder = services.AddDataProtection().SetApplicationName(settings.ApplicationName);

        // Development can retain the platform default. Production validation requires
        // an explicit persistent path and key-encryption mechanism.
        if (string.IsNullOrWhiteSpace(settings.KeysPath))
            return services;

        var keysPath = Path.GetFullPath(settings.KeysPath);
        builder.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        if (settings.KeyEncryptionMode.Equals("Certificate", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(settings.CertificatePath))
                throw new InvalidOperationException("Data Protection certificate path is required.");
            var certificatePath = Path.GetFullPath(settings.CertificatePath);
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                settings.CertificatePassword,
                X509KeyStorageFlags.EphemeralKeySet);
            if (!certificate.HasPrivateKey || DateTime.UtcNow >= certificate.NotAfter.ToUniversalTime())
                throw new InvalidOperationException("Data Protection certificate must contain a valid private key.");
            builder.ProtectKeysWithCertificate(certificate);
            return services;
        }

        if (settings.KeyEncryptionMode.Equals("Dpapi", StringComparison.OrdinalIgnoreCase))
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("DPAPI Data Protection key encryption requires Windows.");
            builder.ProtectKeysWithDpapi(settings.DpapiLocalMachine);
            return services;
        }

        throw new InvalidOperationException(
            "Data Protection KeyEncryptionMode must be Certificate or Dpapi when KeysPath is configured.");
    }
}
