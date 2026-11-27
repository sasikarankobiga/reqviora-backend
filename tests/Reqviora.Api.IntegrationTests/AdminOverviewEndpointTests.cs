
using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reqviora.Api.Controllers;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class AdminOverviewEndpointTests
{
    private const string Endpoint = "/api/admin/overview";

    [Fact]
    public async Task Anonymous_request_returns_401()
    {
        using var factory = new AdminApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { code = "123456" });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task Invalid_JWT_returns_401()
    {
        using var factory = new AdminApiFactory();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer", "invalid-token");

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { code = "123456" });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_user_returns_403()
    {
        using var factory = new AdminApiFactory();
        using var client =
            factory.AuthenticatedClient(factory.NonAdmin);

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { code = factory.CurrentOtp() });

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Admin_without_required_security_returns_403(
        bool emailVerified,
        bool mfaEnabled)
    {
        using var factory = new AdminApiFactory(
            emailVerified,
            mfaEnabled);

        using var client =
            factory.AuthenticatedClient(factory.Admin);

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { code = factory.CurrentOtp() });

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    [Fact]
    public async Task Invalid_MFA_code_returns_401()
    {
        using var factory = new AdminApiFactory();
        using var client =
            factory.AuthenticatedClient(factory.Admin);

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { code = "12AB34" });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task Valid_admin_and_MFA_returns_200()
    {
        using var factory = new AdminApiFactory();
        using var client =
            factory.AuthenticatedClient(factory.Admin);

        using var response = await client.PostAsJsonAsync(
            Endpoint,
            new { code = factory.CurrentOtp() });

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var result = await response.Content
            .ReadFromJsonAsync<
                AdminOverviewController.OverviewResponse>();

        Assert.NotNull(result);
        Assert.Equal(2, result.Users);
        Assert.Equal(0, result.Projects);
        Assert.Equal(0, result.Drafts);
        Assert.Equal(0, result.AuditEvents);

        // Sensitive administration response must not be cached.
        Assert.True(
            response.Headers.CacheControl?.NoStore == true);
    }

    private sealed class AdminApiFactory
        : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection =
            new("Data Source=:memory:");

        private readonly byte[] _mfaSecret =
            Encoding.ASCII.GetBytes(
                "REQVIORA-ADMIN-TEST-SECRET-2026");

        public User Admin { get; } = new()
        {
            Email = "admin@example.test",
            NormalizedEmail = "ADMIN@EXAMPLE.TEST",
            DisplayName = "Admin Test",
            EmailVerified = true
        };

        public User NonAdmin { get; } = new()
        {
            Email = "member@example.test",
            NormalizedEmail = "MEMBER@EXAMPLE.TEST",
            DisplayName = "Member Test",
            EmailVerified = true
        };

        public AdminApiFactory(
            bool emailVerified = true,
            bool mfaEnabled = true)
        {
            Admin.EmailVerified = emailVerified;
            Admin.MfaEnabled = mfaEnabled;
            NonAdmin.MfaEnabled = true;

            _connection.Open();

            using var scope = Services.CreateScope();

            var db = scope.ServiceProvider
                .GetRequiredService<ReqvioraDbContext>();

            db.Database.EnsureCreated();

            var protection = scope.ServiceProvider
                .GetRequiredService<IDataProtectionProvider>();

            var protector = protection.CreateProtector(
                "Reqviora.MfaSecret.v1");

            var protectedSecret = protector.Protect(
                Convert.ToBase64String(_mfaSecret));

            Admin.MfaSecretProtected = protectedSecret;
            NonAdmin.MfaSecretProtected = protectedSecret;

            Admin.PasswordHash = "test-password-hash";
            NonAdmin.PasswordHash = "test-password-hash";

            db.Users.AddRange(Admin, NonAdmin);
            db.SaveChanges();
        }

        public HttpClient AuthenticatedClient(User user)
        {
            var client = CreateClient();

            using var scope = Services.CreateScope();

            var tokens = scope.ServiceProvider
                .GetRequiredService<ITokenService>();

            var token = tokens.CreateAccessToken(user).Token;

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer", token);

            return client;
        }

        public string CurrentOtp()
        {
            // RFC 6238-compatible 30-second TOTP.
            var counter =
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

            Span<byte> counterBytes = stackalloc byte[8];

            BinaryPrimitives.WriteInt64BigEndian(
                counterBytes, counter);

            using var hmac = new HMACSHA1(_mfaSecret);

            var hash = hmac.ComputeHash(
                counterBytes.ToArray());

            var offset = hash[^1] & 0x0F;

            var binary =
                ((hash[offset] & 0x7F) << 24) |
                (hash[offset + 1] << 16) |
                (hash[offset + 2] << 8) |
                hash[offset + 3];

            return (binary % 1_000_000).ToString("D6");
        }

        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.UseSetting(
                "ConnectionStrings:ReqvioraDatabase",
                "Host=localhost;Database=unused");

            builder.UseSetting(
                "Jwt:Key",
                "admin-test-signing-key-at-least-32-characters");

            builder.UseSetting(
                "Security:AdminEmails:0",
                "admin@example.test");

            builder.UseSetting(
                "Security:AuditEnabled",
                "false");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<
                    DbContextOptions<ReqvioraDbContext>>();

                services.RemoveAll<
                    IDbContextOptionsConfiguration<
                        ReqvioraDbContext>>();

                services.RemoveAll<ReqvioraDbContext>();

                services.AddDbContext<ReqvioraDbContext>(
                    options => options.UseSqlite(_connection));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
                _connection.Dispose();
        }
    }
}
