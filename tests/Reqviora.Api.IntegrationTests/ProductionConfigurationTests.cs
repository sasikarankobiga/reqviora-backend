using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Reqviora.Api.Configuration;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class ProductionConfigurationTests
{
    [Fact]
    public void Complete_secret_backed_production_configuration_is_accepted()
    {
        ProductionConfigurationValidator.Validate(Configuration(), new EnvironmentStub("Production"));
    }

    [Theory]
    [InlineData("Smtp:Password", "")]
    [InlineData("Smtp:UseSsl", "false")]
    [InlineData("Smtp:TimeoutSeconds", "0")]
    [InlineData("Smtp:MaxAttempts", "0")]
    [InlineData("Smtp:BaseRetryMilliseconds", "0")]
    [InlineData("Storage:RequireAntivirus", "false")]
    [InlineData("Storage:AntivirusMaxAttempts", "0")]
    [InlineData("Storage:AntivirusRetryDelayMilliseconds", "5001")]
    [InlineData("Storage:AntivirusTimeoutSeconds", "0")]
    [InlineData("Storage:Provider", "Unsupported")]
    [InlineData("Cors:AllowedOrigins:0", "http://app.example.test")]
    [InlineData("DataProtection:KeysPath", "relative-keys")]
    [InlineData("DataProtection:CertificatePassword", "")]
    [InlineData("RateLimiting:Mode", "Unsupported")]
    [InlineData("AIJobs:MaxAttempts", "0")]
    [InlineData("AIJobs:LeaseSeconds", "10")]
    [InlineData("AIJobs:MaxResultCharacters", "0")]
    [InlineData("Invitations:ExpirationHours", "0")]
    public void Unsafe_production_configuration_is_rejected(string key, string value)
    {
        var values = Values();
        values[key] = value;
        Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationValidator.Validate(Configuration(values), new EnvironmentStub("Production")));
    }

    [Fact]
    public void Enabled_ai_requires_an_approved_https_endpoint_and_secret()
    {
        var values = Values();
        values["AI:Enabled"] = "true";
        values["AI:ApiKey"] = "";
        Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationValidator.Validate(Configuration(values), new EnvironmentStub("Production")));

        values["AI:ApiKey"] = "from-secret-store";
        values["AI:Endpoint"] = "https://ai.example.test/v1/";
        values["AI:Model"] = "approved-completion";
        values["AI:TranscriptionModel"] = "approved-transcription";
        values["AI:TimeoutSeconds"] = "120";
        values["AI:MaxAttempts"] = "3";
        values["AI:BaseRetryMilliseconds"] = "250";
        values["AI:MaxResponseCharacters"] = "2000000";
        ProductionConfigurationValidator.Validate(Configuration(values), new EnvironmentStub("Production"));
    }

    [Theory]
    [InlineData("AI:MaxAttempts", "0")]
    [InlineData("AI:BaseRetryMilliseconds", "0")]
    [InlineData("AI:MaxResponseCharacters", "999")]
    public void Enabled_ai_rejects_unsafe_protocol_limits(string key, string value)
    {
        var values = Values();
        values["AI:Enabled"] = "true";
        values["AI:ApiKey"] = "from-secret-store";
        values["AI:Endpoint"] = "https://ai.example.test/v1/";
        values["AI:Model"] = "approved-completion";
        values["AI:TranscriptionModel"] = "approved-transcription";
        values["AI:TimeoutSeconds"] = "120";
        values["AI:MaxAttempts"] = "3";
        values["AI:BaseRetryMilliseconds"] = "250";
        values["AI:MaxResponseCharacters"] = "2000000";
        values[key] = value;
        Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationValidator.Validate(Configuration(values), new EnvironmentStub("Production")));
    }

    [Fact]
    public void Enabled_ocr_requires_secure_endpoint_secret_and_bounded_retries()
    {
        var values = Values();
        values["OCR:Enabled"] = "true";
        values["OCR:Endpoint"] = "http://ocr.example.test/recognize";
        Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationValidator.Validate(Configuration(values), new EnvironmentStub("Production")));

        values["OCR:Endpoint"] = "https://ocr.example.test/recognize";
        values["OCR:ApiKey"] = "from-secret-store";
        values["OCR:Provider"] = "HttpGateway";
        values["OCR:Model"] = "document-ocr";
        values["OCR:TimeoutSeconds"] = "120";
        values["OCR:MaxAttempts"] = "3";
        values["OCR:BaseRetryMilliseconds"] = "250";
        ProductionConfigurationValidator.Validate(Configuration(values), new EnvironmentStub("Production"));
    }

    [Fact]
    public void Supabase_storage_requires_https_private_bucket_and_secret_backed_service_key()
    {
        var values = Values();
        values["Storage:Provider"] = "Supabase";
        Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationValidator.Validate(Configuration(values), new EnvironmentStub("Production")));

        values["Storage:SupabaseUrl"] = "https://project.supabase.co";
        values["Storage:SupabaseBucket"] = "private-documents";
        values["Storage:SupabaseServiceKey"] = "from-secret-store";
        values["Storage:SignedUrlSeconds"] = "300";
        ProductionConfigurationValidator.Validate(Configuration(values), new EnvironmentStub("Production"));
    }

    [Fact]
    public void Distributed_gateway_rate_limiting_requires_an_explicit_trusted_proxy()
    {
        var values = Values();
        values["RateLimiting:Mode"] = "DistributedGateway";
        Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationValidator.Validate(Configuration(values), new EnvironmentStub("Production")));

        values["ForwardedHeaders:Enabled"] = "true";
        values["ForwardedHeaders:KnownProxies:0"] = "10.0.0.10";
        ProductionConfigurationValidator.Validate(Configuration(values), new EnvironmentStub("Production"));
    }

    [Fact]
    public void Enabled_billing_requires_approved_price_secure_urls_and_merchant_secrets()
    {
        var values = Values();
        values["Billing:Enabled"] = "true";
        Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationValidator.Validate(Configuration(values), new EnvironmentStub("Production")));

        values["Billing:MerchantId"] = "from-secret-store";
        values["Billing:MerchantSecret"] = "from-secret-store";
        values["Billing:DefaultProjectPriceUsdMinor"] = "12500";
        values["Billing:ReturnUrl"] = "https://app.example.test/payment/return";
        values["Billing:CancelUrl"] = "https://app.example.test/payment/cancel";
        values["Billing:NotifyUrl"] = "https://api.example.test/api/billing/payhere/notify";
        values["Billing:CheckoutExpiryMinutes"] = "30";
        ProductionConfigurationValidator.Validate(Configuration(values), new EnvironmentStub("Production"));
    }

    [Fact]
    public void Automated_refunds_require_separate_merchant_api_credentials()
    {
        var values = Values();
        values["Billing:Enabled"] = "true";
        values["Billing:MerchantId"] = "from-secret-store";
        values["Billing:MerchantSecret"] = "from-secret-store";
        values["Billing:DefaultProjectPriceUsdMinor"] = "12500";
        values["Billing:ReturnUrl"] = "https://app.example.test/payment/return";
        values["Billing:CancelUrl"] = "https://app.example.test/payment/cancel";
        values["Billing:NotifyUrl"] = "https://api.example.test/api/billing/payhere/notify";
        values["Billing:CheckoutExpiryMinutes"] = "30";
        values["Billing:RefundsEnabled"] = "true";
        Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationValidator.Validate(Configuration(values), new EnvironmentStub("Production")));

        values["Billing:AppId"] = "from-secret-store";
        values["Billing:AppSecret"] = "from-secret-store";
        ProductionConfigurationValidator.Validate(Configuration(values), new EnvironmentStub("Production"));
    }

    private static IConfiguration Configuration(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? Values()).Build();

    private static Dictionary<string, string?> Values() => new()
    {
        ["Storage:RequireAntivirus"] = "true",
        ["Storage:Provider"] = "Local",
        ["Storage:ClamAvHost"] = "clamav.internal",
        ["Storage:ClamAvPort"] = "3310",
        ["Storage:AntivirusMaxAttempts"] = "2",
        ["Storage:AntivirusRetryDelayMilliseconds"] = "250",
        ["Storage:AntivirusTimeoutSeconds"] = "10",
        ["Storage:RootPath"] = Path.GetFullPath("private-artifacts"),
        ["DataProtection:ApplicationName"] = "REQVIORA",
        ["DataProtection:KeysPath"] = Path.GetFullPath("data-protection-keys"),
        ["DataProtection:KeyEncryptionMode"] = "Certificate",
        ["DataProtection:CertificatePath"] = Path.GetFullPath("secrets/data-protection.pfx"),
        ["DataProtection:CertificatePassword"] = "from-secret-store",
        ["AccountLinks:BaseUrl"] = "https://app.example.test",
        ["Cors:AllowedOrigins:0"] = "https://app.example.test",
        ["ForwardedHeaders:Enabled"] = "false",
        ["RateLimiting:Mode"] = "Local",
        ["AIJobs:PollIntervalMilliseconds"] = "1000",
        ["AIJobs:LeaseSeconds"] = "180",
        ["AIJobs:MaxAttempts"] = "3",
        ["AIJobs:BaseRetrySeconds"] = "2",
        ["AIJobs:MaxRetrySeconds"] = "60",
        ["AIJobs:MaxPromptCharacters"] = "100000",
        ["AIJobs:MaxResultCharacters"] = "2000000",
        ["Smtp:Host"] = "smtp.example.test",
        ["Smtp:Port"] = "587",
        ["Smtp:UseSsl"] = "true",
        ["Smtp:UserName"] = "mailer",
        ["Smtp:Password"] = "from-secret-store",
        ["Smtp:FromAddress"] = "no-reply@example.test",
        ["Smtp:TimeoutSeconds"] = "30",
        ["Smtp:MaxAttempts"] = "3",
        ["Smtp:BaseRetryMilliseconds"] = "250",
        ["AI:Enabled"] = "false",
        ["OCR:Enabled"] = "false"
    };

    private sealed class EnvironmentStub(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Reqviora.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
