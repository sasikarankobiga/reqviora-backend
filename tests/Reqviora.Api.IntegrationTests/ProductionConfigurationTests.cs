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
    [InlineData("Storage:RequireAntivirus", "false")]
    [InlineData("Cors:AllowedOrigins:0", "http://app.example.test")]
    [InlineData("DataProtection:KeysPath", "relative-keys")]
    [InlineData("DataProtection:CertificatePassword", "")]
    [InlineData("RateLimiting:Mode", "Unsupported")]
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

    private static IConfiguration Configuration(Dictionary<string, string?>? values = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(values ?? Values()).Build();

    private static Dictionary<string, string?> Values() => new()
    {
        ["Storage:RequireAntivirus"] = "true",
        ["Storage:ClamAvHost"] = "clamav.internal",
        ["Storage:ClamAvPort"] = "3310",
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
        ["Smtp:Host"] = "smtp.example.test",
        ["Smtp:Port"] = "587",
        ["Smtp:UseSsl"] = "true",
        ["Smtp:UserName"] = "mailer",
        ["Smtp:Password"] = "from-secret-store",
        ["Smtp:FromAddress"] = "no-reply@example.test",
        ["AI:Enabled"] = "false"
    };

    private sealed class EnvironmentStub(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Reqviora.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
