using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Authentication;
using Reqviora.Infrastructure.Persistence;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class AccountSecurityEndpointsTests
{
    [Fact]
    public async Task Registration_sends_verification_without_session_and_login_requires_confirmation()
    {
        using var factory = new AccountApiFactory();
        using var client = factory.CreateClient();
        var credentials = new { email = "new@example.test", password = "Initial-Password-123!" };
        using var registered = await client.PostAsJsonAsync("/api/auth/register", new
        { credentials.email, credentials.password, displayName = "New Person" });
        Assert.True(registered.StatusCode == HttpStatusCode.Created, await registered.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await registered.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("verificationEmailSent").GetBoolean());
        Assert.False(json.RootElement.GetProperty("emailVerified").GetBoolean());
        Assert.False(json.RootElement.TryGetProperty("accessToken", out _));
        Assert.False(json.RootElement.TryGetProperty("refreshToken", out _));
        using var blocked = await client.PostAsJsonAsync("/api/auth/login", credentials);
        Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
        using var confirmed = await client.PostAsJsonAsync("/api/account-security/email-verification/confirm", new { token = factory.Email.LastToken() });
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
        using var login = await client.PostAsJsonAsync("/api/auth/login", credentials);
        Assert.True(login.StatusCode == HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Suspicious_login_without_enrolled_mfa_uses_single_use_email_code()
    {
        using var factory = new AccountApiFactory();
        factory.VerifySeedUser();
        using var client = factory.CreateClient();
        var address = factory.User.Email;
        const string password = "Initial-Password-123!";
        async Task<HttpResponseMessage> Login(string device, string? code = null) =>
            await client.PostAsJsonAsync("/api/auth/login", new { email = address, password, deviceId = device, otpCode = code });
        using var first = await Login("known-device");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(0, factory.Email.Count);
        using var challenge = await Login("new-device");
        Assert.Equal(HttpStatusCode.Unauthorized, challenge.StatusCode);
        var otp = factory.Email.LastBody();
        Assert.Matches("^[0-9]{6}$", otp);
        using var wrongDevice = await Login("another-device", otp);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongDevice.StatusCode);
        using var accepted = await Login("new-device", otp);
        Assert.True(accepted.StatusCode == HttpStatusCode.OK, await accepted.Content.ReadAsStringAsync());
        using var replay = await Login("new-device", otp);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using var normal = await Login("new-device");
        Assert.Equal(HttpStatusCode.OK, normal.StatusCode);
    }

    [Fact]
    public async Task Suspicious_login_wrong_code_consumes_challenge_and_expired_code_is_rejected()
    {
        using var factory = new AccountApiFactory();
        factory.VerifySeedUser();
        using var client = factory.CreateClient();
        async Task<HttpResponseMessage> Login(string device, string? code = null) =>
            await client.PostAsJsonAsync("/api/auth/login", new
            { email = factory.User.Email, password = "Initial-Password-123!", deviceId = device, otpCode = code });
        using var first = await Login("known");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var challenge = await Login("new");
        Assert.Equal(HttpStatusCode.Unauthorized, challenge.StatusCode);
        var otp = factory.Email.LastBody();
        var wrongCode = otp == "000000" ? "000001" : "000000";
        using var wrong = await Login("new", wrongCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        using var used = await Login("new", otp);
        Assert.Equal(HttpStatusCode.Unauthorized, used.StatusCode);
        using var second = await Login("new");
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        var secondOtp = factory.Email.LastBody();
        Assert.Equal(2, factory.Email.Count);
        factory.ExpireAccountTokens();
        using var expired = await Login("new", secondOtp);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task Configured_admin_without_mfa_cannot_login_or_receive_email_otp()
    {
        using var factory = new AccountApiFactory(admin: true);
        factory.VerifySeedUser();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new
        { email = factory.User.Email, password = "Initial-Password-123!", deviceId = "known" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.Email.Count);
    }

    [Fact]
    public async Task Admin_bootstrap_credential_expires_and_enrollment_is_single_use()
    {
        using var factory = new AccountApiFactory(admin: true);
        factory.VerifySeedUser();
        using var client = factory.CreateClient();

        using var requested = await client.PostAsJsonAsync("/api/security/admin-mfa/bootstrap/request", new
        { email = factory.User.Email, password = "Initial-Password-123!" });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var expiredToken = factory.Email.LastToken();
        factory.ExpireAccountTokens();
        using var expired = await client.PostAsJsonAsync(
            "/api/security/admin-mfa/provision", new { token = expiredToken });
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);

        using var requestedAgain = await client.PostAsJsonAsync("/api/security/admin-mfa/bootstrap/request", new
        { email = factory.User.Email, password = "Initial-Password-123!" });
        Assert.Equal(HttpStatusCode.Accepted, requestedAgain.StatusCode);
        var token = factory.Email.LastToken();
        using var provisioned = await client.PostAsJsonAsync(
            "/api/security/admin-mfa/provision", new { token });
        Assert.True(provisioned.StatusCode == HttpStatusCode.OK, await provisioned.Content.ReadAsStringAsync());
        using var payload = JsonDocument.Parse(await provisioned.Content.ReadAsStringAsync());
        var secret = payload.RootElement.GetProperty("secret").GetString()!;

        using var repeatedProvision = await client.PostAsJsonAsync(
            "/api/security/admin-mfa/provision", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, repeatedProvision.StatusCode);

        using var verified = await client.PostAsJsonAsync(
            "/api/security/admin-mfa/verify", new { token, code = Totp(secret) });
        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
        Assert.True(factory.ReadUser().MfaEnabled);
        using var replay = await client.PostAsJsonAsync(
            "/api/security/admin-mfa/verify", new { token, code = Totp(secret) });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        using var missingMfa = await client.PostAsJsonAsync("/api/auth/login", new
        { email = factory.User.Email, password = "Initial-Password-123!", deviceId = "admin" });
        Assert.Equal(HttpStatusCode.Unauthorized, missingMfa.StatusCode);
        using var withMfa = await client.PostAsJsonAsync("/api/auth/login", new
        { email = factory.User.Email, password = "Initial-Password-123!", deviceId = "admin", otpCode = Totp(secret) });
        Assert.Equal(HttpStatusCode.OK, withMfa.StatusCode);
    }

    [Fact]
    public async Task Admin_enrollment_rejects_unauthorized_accounts_and_supervised_recovery_revokes_sessions()
    {
        using (var nonAdminFactory = new AccountApiFactory())
        {
            nonAdminFactory.VerifySeedUser();
            using var nonAdminClient = nonAdminFactory.CreateClient();
            using var hidden = await nonAdminClient.PostAsJsonAsync("/api/security/admin-mfa/bootstrap/request", new
            { email = nonAdminFactory.User.Email, password = "Initial-Password-123!" });
            Assert.Equal(HttpStatusCode.Accepted, hidden.StatusCode);
            Assert.Equal(0, nonAdminFactory.Email.Count);
            using var invalid = await nonAdminClient.PostAsJsonAsync(
                "/api/security/admin-mfa/provision", new { token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var authenticated = nonAdminFactory.AuthenticatedClient();
            using var forbiddenRecovery = await authenticated.PostAsJsonAsync(
                "/api/security/admin-mfa/recovery/request", new { adminEmail = nonAdminFactory.User.Email });
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenRecovery.StatusCode);
            Assert.Equal("application/problem+json", forbiddenRecovery.Content.Headers.ContentType?.MediaType);
            Assert.True(forbiddenRecovery.Headers.Contains("X-Correlation-ID"));
        }

        using var factory = new AccountApiFactory(admin: true);
        factory.EnableSeedMfa();
        var targetBeforeRecovery = factory.ReadUser();
        using var targetSession = factory.AuthenticatedClient(targetBeforeRecovery);
        var supervisor = factory.AddSupervisor();
        using var supervisorClient = factory.AuthenticatedClient(supervisor);

        using var requested = await supervisorClient.PostAsJsonAsync(
            "/api/security/admin-mfa/recovery/request", new { adminEmail = factory.User.Email });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var token = factory.Email.LastToken();
        using var provisioned = await supervisorClient.PostAsJsonAsync(
            "/api/security/admin-mfa/provision", new { token });
        Assert.True(provisioned.StatusCode == HttpStatusCode.OK, await provisioned.Content.ReadAsStringAsync());
        using var payload = JsonDocument.Parse(await provisioned.Content.ReadAsStringAsync());
        var secret = payload.RootElement.GetProperty("secret").GetString()!;

        using var revokedAccess = await targetSession.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, revokedAccess.StatusCode);
        Assert.NotNull(factory.ReadSecurityState().RefreshToken.RevokedAt);
        Assert.False(factory.ReadUser().MfaEnabled);

        using var verified = await supervisorClient.PostAsJsonAsync(
            "/api/security/admin-mfa/verify", new { token, code = Totp(secret) });
        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
        Assert.True(factory.ReadUser().MfaEnabled);
        using var replay = await supervisorClient.PostAsJsonAsync(
            "/api/security/admin-mfa/verify", new { token, code = Totp(secret) });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task Verification_resend_is_anonymous_and_does_not_reveal_account_existence()
    {
        using var factory = new AccountApiFactory();
        using var client = factory.CreateClient();
        using var unknown = await client.PostAsJsonAsync("/api/account-security/email-verification/resend", new { email = "missing@example.test" });
        using var known = await client.PostAsJsonAsync("/api/account-security/email-verification/resend", new { email = factory.User.Email });
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        using var confirmed = await client.PostAsJsonAsync("/api/account-security/email-verification/confirm", new { token = factory.Email.LastToken() });
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);
    }

    [Fact]
    public async Task Email_verification_token_is_single_use()
    {
        using var factory = new AccountApiFactory();
        using var client = factory.AuthenticatedClient();

        using var requested = await client.PostAsync("/api/account-security/email-verification/request", null);
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var token = factory.Email.LastToken();

        using var confirmed = await client.PostAsJsonAsync(
            "/api/account-security/email-verification/confirm",
            new { token });
        Assert.True(
            confirmed.StatusCode == HttpStatusCode.NoContent,
            await confirmed.Content.ReadAsStringAsync());

        using var replayed = await client.PostAsJsonAsync(
            "/api/account-security/email-verification/confirm",
            new { token });
        Assert.Equal(HttpStatusCode.BadRequest, replayed.StatusCode);
        Assert.True(factory.ReadUser().EmailVerified);
    }

    [Fact]
    public async Task Password_reset_does_not_enumerate_accounts_and_revokes_sessions()
    {
        using var factory = new AccountApiFactory();
        using var client = factory.CreateClient();

        using var unknown = await client.PostAsJsonAsync(
            "/api/account-security/password/forgot",
            new { email = "missing@example.test" });
        using var known = await client.PostAsJsonAsync(
            "/api/account-security/password/forgot",
            new { email = factory.User.Email });
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);

        var token = factory.Email.LastToken();
        using var reset = await client.PostAsJsonAsync(
            "/api/account-security/password/reset",
            new { token, newPassword = "Updated-Password-123!" });
        Assert.True(
            reset.StatusCode == HttpStatusCode.NoContent,
            await reset.Content.ReadAsStringAsync());

        using var replayed = await client.PostAsJsonAsync(
            "/api/account-security/password/reset",
            new { token, newPassword = "Another-Password-123!" });
        Assert.Equal(HttpStatusCode.BadRequest, replayed.StatusCode);

        var state = factory.ReadSecurityState();
        Assert.Equal(
            PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(state.User, state.User.PasswordHash, "Updated-Password-123!"));
        Assert.NotNull(state.RefreshToken.RevokedAt);
    }

    [Fact]
    public async Task Authenticated_password_change_rehashes_and_revokes_access_and_refresh_sessions()
    {
        using var factory = new AccountApiFactory();
        using var client = factory.AuthenticatedClient();

        using var changed = await client.PostAsJsonAsync(
            "/api/account-security/password/change",
            new { currentPassword = "Initial-Password-123!", newPassword = "Changed-Password-456!" });
        Assert.True(changed.StatusCode == HttpStatusCode.NoContent, await changed.Content.ReadAsStringAsync());

        var state = factory.ReadSecurityState();
        Assert.Equal(
            PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(
                state.User, state.User.PasswordHash, "Changed-Password-456!"));
        Assert.NotNull(state.RefreshToken.RevokedAt);

        using var oldSession = await client.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
        Assert.Contains(factory.ReadAuditEvents(), audit =>
            audit.Path == "/api/account-security/password/change" &&
            audit.StatusCode == StatusCodes.Status204NoContent &&
            audit.UserId == factory.User.Id);
    }

    [Fact]
    public async Task Password_change_rejects_unauthorized_wrong_current_and_invalid_new_password()
    {
        using var factory = new AccountApiFactory();
        using var anonymous = factory.CreateClient();
        using var unauthorized = await anonymous.PostAsJsonAsync(
            "/api/account-security/password/change",
            new { currentPassword = "Initial-Password-123!", newPassword = "Changed-Password-456!" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var client = factory.AuthenticatedClient();
        using var wrongCurrent = await client.PostAsJsonAsync(
            "/api/account-security/password/change",
            new { currentPassword = "Wrong-Password-123!", newPassword = "Changed-Password-456!" });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);

        using var invalidNew = await client.PostAsJsonAsync(
            "/api/account-security/password/change",
            new { currentPassword = "Initial-Password-123!", newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidNew.StatusCode);

        var state = factory.ReadSecurityState();
        Assert.Equal(
            PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(
                state.User, state.User.PasswordHash, "Initial-Password-123!"));
        Assert.Null(state.RefreshToken.RevokedAt);
    }

    [Fact]
    public async Task Email_change_confirms_new_address_and_revokes_sessions()
    {
        using var factory = new AccountApiFactory();
        using var client = factory.AuthenticatedClient();

        using var requested = await client.PostAsJsonAsync(
            "/api/account-security/email-change/request",
            new { newEmail = "updated@example.test" });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var token = factory.Email.LastToken();

        using var confirmed = await client.PostAsJsonAsync(
            "/api/account-security/email-change/confirm",
            new { token });
        Assert.True(
            confirmed.StatusCode == HttpStatusCode.NoContent,
            await confirmed.Content.ReadAsStringAsync());

        var state = factory.ReadSecurityState();
        Assert.Equal("updated@example.test", state.User.Email);
        Assert.Equal("UPDATED@EXAMPLE.TEST", state.User.NormalizedEmail);
        Assert.True(state.User.EmailVerified);
        Assert.NotNull(state.RefreshToken.RevokedAt);
    }

    [Fact]
    public async Task Expired_account_token_is_rejected()
    {
        using var factory = new AccountApiFactory();
        using var client = factory.AuthenticatedClient();
        using var requested = await client.PostAsync("/api/account-security/email-verification/request", null);
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var token = factory.Email.LastToken();
        factory.ExpireAccountTokens();

        using var confirmed = await client.PostAsJsonAsync(
            "/api/account-security/email-verification/confirm",
            new { token });
        Assert.Equal(HttpStatusCode.BadRequest, confirmed.StatusCode);
    }

    [Fact]
    public async Task Authentication_endpoints_are_rate_limited()
    {
        using var factory = new AccountApiFactory();
        using var client = factory.CreateClient();
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 11; attempt++)
        {
            response?.Dispose();
            response = await client.PostAsJsonAsync(
                "/api/account-security/password/forgot",
                new { email = "missing@example.test" });
        }

        using (response)
            Assert.Equal(HttpStatusCode.TooManyRequests, response!.StatusCode);
    }

    [Fact]
    public async Task Admin_dashboard_users_projects_and_audits_are_role_protected_and_paginated()
    {
        using var adminFactory = new AccountApiFactory(admin: true);
        adminFactory.VerifySeedUser();
        adminFactory.SeedAdminData();
        using var anonymous = adminFactory.CreateClient();
        using var unauthorized = await anonymous.GetAsync("/api/admin/dashboard");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var nonAdminFactory = new AccountApiFactory();
        nonAdminFactory.VerifySeedUser();
        using var nonAdmin = nonAdminFactory.AuthenticatedClient();
        using var forbidden = await nonAdmin.GetAsync("/api/admin/dashboard");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var admin = adminFactory.AuthenticatedClient();
        using var dashboard = await admin.GetAsync("/api/admin/dashboard");
        Assert.True(dashboard.StatusCode == HttpStatusCode.OK, await dashboard.Content.ReadAsStringAsync());
        using var users = await admin.GetAsync("/api/admin/users?page=1&pageSize=10&status=enabled");
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);
        Assert.Contains("account@example.test", await users.Content.ReadAsStringAsync());
        using var projects = await admin.GetAsync("/api/admin/projects?page=1&pageSize=10");
        Assert.True(projects.StatusCode == HttpStatusCode.OK, await projects.Content.ReadAsStringAsync());
        Assert.Contains("Admin project", await projects.Content.ReadAsStringAsync());
        using var audits = await admin.GetAsync("/api/admin/audit-events?page=1&pageSize=10");
        Assert.True(audits.StatusCode == HttpStatusCode.OK, await audits.Content.ReadAsStringAsync());
        using var invalidPage = await admin.GetAsync("/api/admin/users?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
    }

    [Fact]
    public async Task Admin_disable_revokes_sessions_blocks_login_and_prevents_self_disable()
    {
        using var factory = new AccountApiFactory(admin: true);
        factory.VerifySeedUser();
        var target = factory.AddRegularUserWithRefresh();
        using var targetClient = factory.AuthenticatedClient(target.User);
        using var admin = factory.AuthenticatedClient();

        using var disabled = await admin.PutAsJsonAsync($"/api/admin/users/{target.User.Id}/status",
            new { disabled = true, reason = "Security investigation" });

        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        var state = factory.ReadUserAndToken(target.User.Id);
        Assert.True(state.User.IsDisabled);
        Assert.Equal("Security investigation", state.User.DisabledReason);
        Assert.NotNull(state.RefreshToken.RevokedAt);
        using var revokedAccess = await targetClient.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, revokedAccess.StatusCode);
        using var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new
        {
            email = target.User.Email,
            password = "Regular-Password-123!"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        using var selfDisable = await admin.PutAsJsonAsync($"/api/admin/users/{factory.User.Id}/status",
            new { disabled = true, reason = "Mistake" });
        Assert.Equal(HttpStatusCode.BadRequest, selfDisable.StatusCode);
    }

    private sealed class AccountApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly bool _admin;

        public User User { get; } = new()
        {
            Email = "account@example.test",
            NormalizedEmail = "ACCOUNT@EXAMPLE.TEST",
            DisplayName = "Account Test",
            EmailVerified = false
        };

        public CapturingEmailService Email { get; } = new();
        public RefreshToken RefreshToken { get; }

        public AccountApiFactory(bool admin = false)
        {
            _admin = admin;
            User.PasswordHash = new PasswordHasher<User>().HashPassword(User, "Initial-Password-123!");
            RefreshToken = new RefreshToken
            {
                UserId = User.Id,
                TokenHash = new string('a', 64),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
            };

            _connection.Open();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            db.Database.EnsureCreated();
            db.Users.Add(User);
            db.RefreshTokens.Add(RefreshToken);
            db.SaveChanges();
        }

        public HttpClient AuthenticatedClient(User? authenticatedUser = null)
        {
            var client = CreateClient();
            using var scope = Services.CreateScope();
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokens.CreateAccessToken(authenticatedUser ?? User).Token);
            return client;
        }

        public User ReadUser()
        {
            using var scope = Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>().Users.AsNoTracking()
                .Single(user => user.Id == User.Id);
        }

        public (User User, RefreshToken RefreshToken) ReadSecurityState()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            return (db.Users.AsNoTracking().Single(user => user.Id == User.Id),
                db.RefreshTokens.AsNoTracking().Single(token => token.UserId == User.Id));
        }

        public AuditEvent[] ReadAuditEvents()
        {
            using var scope = Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>()
                .AuditEvents.AsNoTracking().ToArray();
        }

        public void VerifySeedUser()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            db.Users.Single(user => user.Id == User.Id).EmailVerified = true;
            db.SaveChanges();
        }

        public void EnableSeedMfa()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            var user = db.Users.Single(candidate => candidate.Id == User.Id);
            user.EmailVerified = true;
            user.MfaEnabled = true;
            user.MfaSecretProtected = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Reqviora.MfaSecret.v1")
                .Protect(Convert.ToBase64String(RandomNumberGenerator.GetBytes(20)));
            user.RecoveryCodeHashes = "fixture";
            db.SaveChanges();
        }

        public User AddSupervisor()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            var supervisor = new User
            {
                Email = "supervisor@example.test",
                NormalizedEmail = "SUPERVISOR@EXAMPLE.TEST",
                DisplayName = "Supervisor",
                EmailVerified = true,
                MfaEnabled = true
            };
            supervisor.PasswordHash = new PasswordHasher<User>()
                .HashPassword(supervisor, "Supervisor-Password-123!");
            supervisor.MfaSecretProtected = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("Reqviora.MfaSecret.v1")
                .Protect(Convert.ToBase64String(RandomNumberGenerator.GetBytes(20)));
            supervisor.RecoveryCodeHashes = "fixture";
            db.Users.Add(supervisor);
            db.SaveChanges();
            return supervisor;
        }

        public (User User, RefreshToken RefreshToken) AddRegularUserWithRefresh()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            var user = new User
            {
                Email = "regular@example.test",
                NormalizedEmail = "REGULAR@EXAMPLE.TEST",
                DisplayName = "Regular User",
                EmailVerified = true
            };
            user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Regular-Password-123!");
            var refresh = new RefreshToken
            {
                UserId = user.Id,
                TokenHash = Convert.ToHexString(SHA256.HashData(Guid.NewGuid().ToByteArray())),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
            };
            db.Users.Add(user);
            db.RefreshTokens.Add(refresh);
            db.SaveChanges();
            return (user, refresh);
        }

        public (User User, RefreshToken RefreshToken) ReadUserAndToken(Guid userId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            return (db.Users.AsNoTracking().Single(user => user.Id == userId),
                db.RefreshTokens.AsNoTracking().Single(token => token.UserId == userId));
        }

        public void SeedAdminData()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            var workspace = new Workspace { Name = "Admin workspace", OwnerUserId = User.Id };
            db.Workspaces.Add(workspace);
            db.Projects.Add(new Project
            {
                Name = "Admin project",
                Description = "Visible to administrators",
                OwnerId = User.Id,
                WorkspaceId = workspace.Id
            });
            db.AuditEvents.Add(new AuditEvent
            {
                UserId = User.Id,
                Method = "GET",
                Path = "/seed",
                StatusCode = 200,
                IpAddress = "127.0.0.1",
                OccurredAt = DateTimeOffset.UtcNow
            });
            db.SaveChanges();
        }

        public void ExpireAccountTokens()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            foreach (var token in db.AccountActionTokens) token.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            db.SaveChanges();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:ReqvioraDatabase", "Host=localhost;Database=unused");
            builder.UseSetting("Jwt:Key", "account-test-signing-key-at-least-32-characters");
            builder.UseSetting("AccountLinks:BaseUrl", "https://app.example.test");
            if (_admin) builder.UseSetting("Security:AdminEmails:0", User.Email);
            if (_admin) builder.UseSetting("Security:AdminEmails:1", "supervisor@example.test");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<DbContextOptions<ReqvioraDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ReqvioraDbContext>>();
                services.RemoveAll<ReqvioraDbContext>();
                services.RemoveAll<IEmailService>();
                services.AddDbContext<ReqvioraDbContext>(options => options.UseSqlite(_connection));
                services.AddSingleton<IEmailService>(Email);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection.Dispose();
        }
    }

    private static string Totp(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var buffer = 0;
        var bits = 0;
        var bytes = new List<byte>();
        foreach (var character in base32.TrimEnd('=').ToUpperInvariant())
        {
            var value = alphabet.IndexOf(character);
            if (value < 0) throw new InvalidDataException("Invalid base32 secret.");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits < 8) continue;
            bytes.Add((byte)(buffer >> (bits - 8)));
            bits -= 8;
            buffer &= (1 << bits) - 1;
        }

        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        Span<byte> input = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(input, counter);
        using var hmac = new HMACSHA1(bytes.ToArray());
        var hash = hmac.ComputeHash(input.ToArray());
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) |
            (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    public sealed class CapturingEmailService : IEmailService
    {
        private readonly List<string> _bodies = [];
        public int Count => _bodies.Count;
        public string LastBody() => _bodies[^1];

        public Task SendAsync(
            string to,
            string subject,
            string body,
            CancellationToken cancellationToken = default)
        {
            _bodies.Add(body);
            return Task.CompletedTask;
        }

        public string LastToken()
        {
            var query = new Uri(_bodies[^1]).Query.TrimStart('?');
            var value = query.Split('=', 2)[1];
            return Uri.UnescapeDataString(value);
        }
    }
}
