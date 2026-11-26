using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Security;

namespace Reqviora.Infrastructure.Tests;

public sealed class LoginSecurityServiceTests
{
    [Fact]
    public async Task Configured_admin_requires_mfa_enrollment_before_login()
    {
        await using var database = await PersistenceDatabase.CreateAsync("sqlite");
        await using var db = database.Open();
        var protection = new EphemeralDataProtectionProvider();
        var user = new User
        {
            Email = "admin@example.test",
            NormalizedEmail = "ADMIN@EXAMPLE.TEST",
            DisplayName = "Admin",
            PasswordHash = "unused"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:AdminEmails:0"] = user.Email
            })
            .Build();
        var service = new LoginSecurityService(db, protection, configuration, TimeProvider.System);

        Assert.True(await service.RequiresOtpAsync(user, "127.0.0.1", "test", CancellationToken.None));
    }

    [Fact]
    public async Task Known_device_is_allowed_but_new_device_requires_otp_after_enrollment()
    {
        await using var database = await PersistenceDatabase.CreateAsync("sqlite");
        await using var db = database.Open();
        var protection = new EphemeralDataProtectionProvider();
        var user = UserWithMfa(protection);
        db.Users.Add(user);
        db.LoginAudits.Add(new LoginAudit
        {
            UserId = user.Id,
            NormalizedEmail = user.NormalizedEmail,
            IpAddress = "127.0.0.1",
            DeviceId = "known-device",
            Succeeded = true,
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        await db.SaveChangesAsync();
        var service = Create(db, protection);

        Assert.False(await service.RequiresOtpAsync(user, "127.0.0.1", "known-device", default));
        Assert.True(await service.RequiresOtpAsync(user, "127.0.0.1", "new-device", default));
    }

    [Fact]
    public async Task Three_recent_failures_require_otp_even_on_known_device()
    {
        await using var database = await PersistenceDatabase.CreateAsync("sqlite");
        await using var db = database.Open();
        var protection = new EphemeralDataProtectionProvider();
        var user = UserWithMfa(protection);
        db.Users.Add(user);
        db.LoginAudits.Add(new LoginAudit
        {
            UserId = user.Id,
            NormalizedEmail = user.NormalizedEmail,
            IpAddress = "127.0.0.1",
            DeviceId = "known",
            Succeeded = true,
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-2)
        });
        for (var index = 0; index < 3; index++)
            db.LoginAudits.Add(new LoginAudit
            {
                UserId = user.Id,
                NormalizedEmail = user.NormalizedEmail,
                IpAddress = "127.0.0.1",
                DeviceId = "known",
                Succeeded = false,
                OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            });
        await db.SaveChangesAsync();

        Assert.True(await Create(db, protection).RequiresOtpAsync(user, "127.0.0.1", "known", default));
    }

    [Fact]
    public async Task Recovery_code_is_single_use()
    {
        await using var database = await PersistenceDatabase.CreateAsync("sqlite");
        await using var db = database.Open();
        var protection = new EphemeralDataProtectionProvider();
        var user = UserWithMfa(protection);
        const string recovery = "abcd1234ef";
        user.RecoveryCodeHashes = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(recovery)));
        var service = Create(db, protection);

        Assert.True(service.ValidateOtp(user, recovery));
        Assert.Empty(user.RecoveryCodeHashes);
        Assert.False(service.ValidateOtp(user, recovery));
    }

    [Fact]
    public async Task Authenticator_code_cannot_be_replayed_for_login()
    {
        await using var database = await PersistenceDatabase.CreateAsync("sqlite");
        await using var db = database.Open();
        var protection = new EphemeralDataProtectionProvider();
        var user = UserWithMfa(protection);
        user.MfaSecretProtected = protection.CreateProtector("Reqviora.MfaSecret.v1")
            .Protect(Convert.ToBase64String(Encoding.ASCII.GetBytes("12345678901234567890")));
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var clock = new FixedClock(DateTimeOffset.FromUnixTimeSeconds(59));
        var service = new LoginSecurityService(db, protection,
            new ConfigurationBuilder().Build(), clock);
        // RFC 6238 SHA-1 test vector truncated to six digits.
        Assert.True(await service.VerifyLoginOtpAsync(user, "local", "device", "287082", default));
        Assert.False(await service.VerifyLoginOtpAsync(user, "local", "device", "287082", default));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task Refresh_token_cannot_be_rotated_twice_from_stale_contexts()
    {
        await using var database = await PersistenceDatabase.CreateAsync("sqlite");
        await using var first = database.Open();
        var user = UserWithMfa(new EphemeralDataProtectionProvider());
        first.Users.Add(user);
        var token = new RefreshToken { UserId = user.Id, TokenHash = new string('a', 64), ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        first.RefreshTokens.Add(token);
        await first.SaveChangesAsync();
        await using var second = database.Open();
        var stale = await second.RefreshTokens.SingleAsync();
        token.RevokedAt = DateTimeOffset.UtcNow;
        await first.SaveChangesAsync();
        stale.RevokedAt = DateTimeOffset.UtcNow;
        stale.ReplacedByTokenHash = new string('b', 64);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Recovery_code_removal_cannot_be_overwritten_from_stale_context()
    {
        await using var database = await PersistenceDatabase.CreateAsync("sqlite");
        await using var first = database.Open();
        var protection = new EphemeralDataProtectionProvider();
        var user = UserWithMfa(protection);
        const string recovery = "abcd1234ef";
        user.RecoveryCodeHashes = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(recovery)));
        first.Users.Add(user);
        await first.SaveChangesAsync();
        await using var second = database.Open();
        var stale = await second.Users.SingleAsync();
        Assert.True(Create(first, protection).ValidateOtp(user, recovery));
        Assert.True(Create(second, protection).ValidateOtp(stale, recovery));
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    private static LoginSecurityService Create(
        Persistence.ReqvioraDbContext db,
        IDataProtectionProvider protection) => new(
            db,
            protection,
            new ConfigurationBuilder().AddInMemoryCollection().Build(),
            TimeProvider.System);

    private static User UserWithMfa(IDataProtectionProvider protection) => new()
    {
        Email = "security@example.test",
        NormalizedEmail = "SECURITY@EXAMPLE.TEST",
        DisplayName = "Security Test",
        PasswordHash = "not-used-in-this-test",
        MfaEnabled = true,
        MfaSecretProtected = protection.CreateProtector("Reqviora.MfaSecret.v1")
            .Protect(Convert.ToBase64String(RandomNumberGenerator.GetBytes(20)))
    };
}
