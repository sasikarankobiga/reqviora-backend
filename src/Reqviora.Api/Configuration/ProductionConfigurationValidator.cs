namespace Reqviora.Api.Configuration;

public static class ProductionConfigurationValidator
{
    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
            return;

        Require(configuration.GetValue<bool>("Storage:RequireAntivirus"),
            "Production requires Storage:RequireAntivirus=true.");
        Require(!string.IsNullOrWhiteSpace(configuration["Storage:ClamAvHost"]) &&
                configuration.GetValue<int>("Storage:ClamAvPort") is > 0 and <= 65535,
            "Production requires a valid ClamAV host and port.");
        Require(Path.IsPathRooted(configuration["Storage:RootPath"] ?? string.Empty),
            "Production requires an absolute private Storage:RootPath.");

        Require(!string.IsNullOrWhiteSpace(configuration["DataProtection:ApplicationName"]),
            "Production requires DataProtection:ApplicationName.");
        Require(Path.IsPathRooted(configuration["DataProtection:KeysPath"] ?? string.Empty),
            "Production requires an absolute persistent DataProtection:KeysPath.");
        var certificateKeys = string.Equals(configuration["DataProtection:KeyEncryptionMode"],
            "Certificate", StringComparison.OrdinalIgnoreCase);
        var dpapiKeys = string.Equals(configuration["DataProtection:KeyEncryptionMode"],
            "Dpapi", StringComparison.OrdinalIgnoreCase);
        Require(certificateKeys || dpapiKeys,
            "Production Data Protection key encryption must use Certificate or Dpapi.");
        if (certificateKeys)
        {
            Require(Path.IsPathRooted(configuration["DataProtection:CertificatePath"] ?? string.Empty),
                "Production certificate key encryption requires an absolute certificate path.");
            Require(!string.IsNullOrWhiteSpace(configuration["DataProtection:CertificatePassword"]),
                "Production certificate password must come from a secret provider.");
        }
        else
        {
            Require(OperatingSystem.IsWindows(), "DPAPI Data Protection key encryption requires Windows.");
        }

        RequireSecureUrl(configuration["AccountLinks:BaseUrl"], "AccountLinks:BaseUrl");
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        Require(origins.Length > 0, "Production requires at least one explicit Cors:AllowedOrigins entry.");
        foreach (var origin in origins)
            RequireSecureUrl(origin, "Cors:AllowedOrigins");

        var forwardedHeaders = configuration.GetValue<bool>("ForwardedHeaders:Enabled");
        var trustedProxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        if (forwardedHeaders)
        {
            Require(trustedProxies.Length > 0,
                "Forwarded headers require at least one explicit trusted proxy.");
            foreach (var proxy in trustedProxies)
                Require(System.Net.IPAddress.TryParse(proxy, out _),
                    "ForwardedHeaders:KnownProxies must contain IP addresses only.");
        }

        var rateLimitMode = configuration["RateLimiting:Mode"] ?? "Local";
        Require(rateLimitMode is "Local" or "DistributedGateway",
            "RateLimiting:Mode must be Local or DistributedGateway.");
        if (rateLimitMode == "DistributedGateway")
            Require(forwardedHeaders && trustedProxies.Length > 0,
                "DistributedGateway rate limiting requires trusted forwarded-header configuration.");

        Require(!string.IsNullOrWhiteSpace(configuration["Smtp:Host"]),
            "Production requires Smtp:Host.");
        Require(configuration.GetValue<int>("Smtp:Port") is > 0 and <= 65535,
            "Production requires a valid Smtp:Port.");
        Require(configuration.GetValue<bool>("Smtp:UseSsl"),
            "Production requires SMTP TLS.");
        Require(!string.IsNullOrWhiteSpace(configuration["Smtp:UserName"]) &&
                !string.IsNullOrWhiteSpace(configuration["Smtp:Password"]) &&
                !string.IsNullOrWhiteSpace(configuration["Smtp:FromAddress"]),
            "Production requires secret-backed SMTP credentials and a sender address.");

        if (configuration.GetValue<bool>("AI:Enabled"))
        {
            RequireSecureUrl(configuration["AI:Endpoint"], "AI:Endpoint");
            Require(!string.IsNullOrWhiteSpace(configuration["AI:ApiKey"]),
                "AI:Enabled requires a secret-backed API key after G0-07 approval.");
        }
    }

    private static void RequireSecureUrl(string? value, string name)
    {
        Require(Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps && !uri.IsLoopback && string.IsNullOrEmpty(uri.UserInfo),
            $"Production requires {name} to be a non-loopback HTTPS URL without embedded credentials.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
