using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reqviora.Infrastructure.Security;

namespace Reqviora.Infrastructure.Tests;

public sealed class DataProtectionPersistenceTests
{
    [Fact]
    public void Protected_payload_survives_restart_and_keys_are_encrypted_at_rest()
    {
        using var fixture = new DataProtectionFixture();
        const string value = "mfa-secret-that-must-not-appear-in-the-key-ring";

        string protectedValue;
        using (var first = fixture.CreateProvider())
            protectedValue = first.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Reqviora.MfaSecret.v1").Protect(value);

        using (var restarted = fixture.CreateProvider())
            Assert.Equal(value, restarted.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Reqviora.MfaSecret.v1").Unprotect(protectedValue));

        var keyXml = string.Join(Environment.NewLine,
            Directory.GetFiles(fixture.KeysPath, "*.xml").Select(File.ReadAllText));
        Assert.DoesNotContain(value, keyXml, StringComparison.Ordinal);
        Assert.Contains("encryptedSecret", keyXml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Authorized_instances_share_the_same_encrypted_key_ring()
    {
        using var fixture = new DataProtectionFixture();
        using var first = fixture.CreateProvider();
        using var second = fixture.CreateProvider();
        var firstProtector = first.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Reqviora.LoginEmailOtp.v1");
        var secondProtector = second.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Reqviora.LoginEmailOtp.v1");

        Assert.Equal("instance-a", secondProtector.Unprotect(firstProtector.Protect("instance-a")));
        Assert.Equal("instance-b", firstProtector.Unprotect(secondProtector.Protect("instance-b")));
    }

    private sealed class DataProtectionFixture : IDisposable
    {
        private const string Password = "test-only-certificate-password";
        private readonly string _root = Path.Combine(Path.GetTempPath(), "reqviora-dp-" + Guid.NewGuid().ToString("N"));
        private readonly string _certificatePath;

        public DataProtectionFixture()
        {
            Directory.CreateDirectory(_root);
            KeysPath = Path.Combine(_root, "keys");
            _certificatePath = Path.Combine(_root, "key-encryption.pfx");
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=REQVIORA Data Protection Test", rsa,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllBytes(_certificatePath, certificate.Export(X509ContentType.Pfx, Password));
        }

        public string KeysPath { get; }

        public ServiceProvider CreateProvider()
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:ApplicationName"] = "REQVIORA.Tests",
                ["DataProtection:KeysPath"] = KeysPath,
                ["DataProtection:KeyEncryptionMode"] = "Certificate",
                ["DataProtection:CertificatePath"] = _certificatePath,
                ["DataProtection:CertificatePassword"] = Password
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddReqvioraDataProtection(configuration);
            return services.BuildServiceProvider();
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
