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
        Require(configuration.GetValue<int>("Storage:AntivirusMaxAttempts") is >= 1 and <= 5,
            "Storage:AntivirusMaxAttempts must be between 1 and 5.");
        Require(configuration.GetValue<int>("Storage:AntivirusRetryDelayMilliseconds") is >= 0 and <= 5000,
            "Storage:AntivirusRetryDelayMilliseconds must be between 0 and 5000.");
        Require(configuration.GetValue<int>("Storage:AntivirusTimeoutSeconds") is >= 1 and <= 60,
            "Storage:AntivirusTimeoutSeconds must be between 1 and 60.");
        var storageProvider = configuration["Storage:Provider"] ?? "Local";
        Require(storageProvider is "Local" or "Supabase", "Storage:Provider must be Local or Supabase.");
        if (storageProvider == "Local")
            Require(Path.IsPathRooted(configuration["Storage:RootPath"] ?? string.Empty),
                "Production requires an absolute private Storage:RootPath.");
        else
        {
            RequireSecureUrl(configuration["Storage:SupabaseUrl"], "Storage:SupabaseUrl");
            Require(!string.IsNullOrWhiteSpace(configuration["Storage:SupabaseBucket"]) &&
                    !string.IsNullOrWhiteSpace(configuration["Storage:SupabaseServiceKey"]),
                "Supabase storage requires a private bucket name and secret-backed service key.");
            Require(configuration.GetValue<int>("Storage:SignedUrlSeconds") is >= 30 and <= 900,
                "Storage:SignedUrlSeconds must be between 30 and 900.");
        }

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

        Require(configuration.GetValue("Invitations:ExpirationHours", 72) is >= 1 and <= 720,
            "Invitations:ExpirationHours must be between 1 and 720.");

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
        Require(configuration.GetValue<int>("Smtp:TimeoutSeconds") is >= 1 and <= 120,
            "Smtp:TimeoutSeconds must be between 1 and 120.");
        Require(configuration.GetValue<int>("Smtp:MaxAttempts") is >= 1 and <= 5,
            "Smtp:MaxAttempts must be between 1 and 5.");
        Require(configuration.GetValue<int>("Smtp:BaseRetryMilliseconds") is >= 10 and <= 5000,
            "Smtp:BaseRetryMilliseconds must be between 10 and 5000.");

        if (configuration.GetValue<bool>("AI:Enabled"))
        {
            RequireSecureUrl(configuration["AI:Endpoint"], "AI:Endpoint");
            Require(!string.IsNullOrWhiteSpace(configuration["AI:ApiKey"]),
                "AI:Enabled requires a secret-backed API key after G0-07 approval.");
            Require(!string.IsNullOrWhiteSpace(configuration["AI:Model"]) &&
                    !string.IsNullOrWhiteSpace(configuration["AI:TranscriptionModel"]),
                "AI completion and transcription models are required.");
            Require(configuration.GetValue<int>("AI:TimeoutSeconds") is >= 10 and <= 600,
                "AI:TimeoutSeconds must be between 10 and 600.");
            Require(configuration.GetValue<int>("AI:MaxAttempts") is >= 1 and <= 5,
                "AI:MaxAttempts must be between 1 and 5.");
            Require(configuration.GetValue<int>("AI:BaseRetryMilliseconds") is >= 10 and <= 5000,
                "AI:BaseRetryMilliseconds must be between 10 and 5000.");
            Require(configuration.GetValue<int>("AI:MaxResponseCharacters") is >= 1000 and <= 4_000_000,
                "AI:MaxResponseCharacters must be between 1000 and 4000000.");
        }

        if (configuration.GetValue<bool>("OCR:Enabled"))
        {
            RequireSecureUrl(configuration["OCR:Endpoint"], "OCR:Endpoint");
            Require(!string.IsNullOrWhiteSpace(configuration["OCR:ApiKey"]),
                "OCR:Enabled requires a secret-backed API key.");
            Require(!string.IsNullOrWhiteSpace(configuration["OCR:Provider"]) &&
                    !string.IsNullOrWhiteSpace(configuration["OCR:Model"]),
                "OCR provider and model are required.");
            Require(configuration.GetValue<int>("OCR:TimeoutSeconds") is >= 10 and <= 600,
                "OCR:TimeoutSeconds must be between 10 and 600.");
            Require(configuration.GetValue<int>("OCR:MaxAttempts") is >= 1 and <= 5,
                "OCR:MaxAttempts must be between 1 and 5.");
            Require(configuration.GetValue<int>("OCR:BaseRetryMilliseconds") is >= 10 and <= 5000,
                "OCR:BaseRetryMilliseconds must be between 10 and 5000.");
        }

        Require(configuration.GetValue<int>("AIJobs:PollIntervalMilliseconds") is >= 100 and <= 30000,
            "AIJobs:PollIntervalMilliseconds must be between 100 and 30000.");
        Require(configuration.GetValue<int>("AIJobs:LeaseSeconds") is >= 30 and <= 3600,
            "AIJobs:LeaseSeconds must be between 30 and 3600.");
        Require(configuration.GetValue<int>("AIJobs:MaxAttempts") is >= 1 and <= 10,
            "AIJobs:MaxAttempts must be between 1 and 10.");
        var baseRetry = configuration.GetValue<int>("AIJobs:BaseRetrySeconds");
        var maxRetry = configuration.GetValue<int>("AIJobs:MaxRetrySeconds");
        Require(baseRetry is >= 1 and <= 300 && maxRetry >= baseRetry && maxRetry <= 3600,
            "AI job retry settings are invalid.");
        Require(configuration.GetValue<int>("AIJobs:MaxPromptCharacters") is >= 1 and <= 1_000_000,
            "AIJobs:MaxPromptCharacters must be between 1 and 1000000.");
        Require(configuration.GetValue<int>("AIJobs:MaxResultCharacters") is >= 1 and <= 10_000_000,
            "AIJobs:MaxResultCharacters must be between 1 and 10000000.");

        if (configuration.GetValue<bool>("Billing:Enabled"))
        {
            Require(!string.IsNullOrWhiteSpace(configuration["Billing:MerchantId"]) &&
                    !string.IsNullOrWhiteSpace(configuration["Billing:MerchantSecret"]),
                "Billing requires secret-backed PayHere merchant credentials.");
            Require(configuration.GetValue<long>("Billing:DefaultProjectPriceUsdMinor") > 0,
                "Billing requires an approved positive USD project price.");
            RequireSecureUrl(configuration["Billing:ReturnUrl"], "Billing:ReturnUrl");
            RequireSecureUrl(configuration["Billing:CancelUrl"], "Billing:CancelUrl");
            RequireSecureUrl(configuration["Billing:NotifyUrl"], "Billing:NotifyUrl");
            Require(configuration.GetValue<int>("Billing:CheckoutExpiryMinutes") is >= 5 and <= 1440,
                "Billing:CheckoutExpiryMinutes must be between 5 and 1440.");
            if (configuration.GetValue<bool>("Billing:RefundsEnabled"))
                Require(!string.IsNullOrWhiteSpace(configuration["Billing:AppId"]) &&
                        !string.IsNullOrWhiteSpace(configuration["Billing:AppSecret"]),
                    "Automated refunds require secret-backed PayHere Merchant API credentials.");
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
