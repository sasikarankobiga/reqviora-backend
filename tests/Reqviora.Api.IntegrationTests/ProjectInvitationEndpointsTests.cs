using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.Sharing;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class ProjectInvitationEndpointsTests
{
    [Fact]
    public async Task Invitation_acceptance_is_email_bound_atomic_and_replay_safe()
    {
        using var factory = new InvitationApiFactory();
        using var owner = factory.AuthenticatedClient(factory.Owner);
        using var recipient = factory.AuthenticatedClient(factory.Recipient);

        using var created = await owner.PostAsJsonAsync($"/api/projects/{factory.Project.Id}/invitations",
            new { recipientEmail = "  RECIPIENT@example.test ", role = "editor" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var invitation = await created.Content.ReadFromJsonAsync<ProjectInvitationDetails>();
        Assert.NotNull(invitation);
        Assert.Equal("Editor", invitation.Role);

        var token = factory.Email.LastToken();
        var stored = factory.ReadInvitation(invitation.Id);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), stored.TokenHash);
        Assert.DoesNotContain(token, stored.TokenHash, StringComparison.Ordinal);
        Assert.All(factory.ReadAuditEvents(), audit =>
        {
            Assert.DoesNotContain(token, audit.Path, StringComparison.Ordinal);
            Assert.DoesNotContain(stored.TokenHash, audit.Path, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(token, await created.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var listed = await owner.GetAsync($"/api/projects/{factory.Project.Id}/invitations");
        var listing = await listed.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.DoesNotContain(token, listing, StringComparison.Ordinal);
        Assert.DoesNotContain("tokenHash", listing, StringComparison.OrdinalIgnoreCase);

        using var accepted = await recipient.PostAsJsonAsync("/api/project-invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("Editor", factory.ReadMember(factory.Project.Id, factory.Recipient.Id)?.Role);

        using var replay = await recipient.PostAsJsonAsync("/api/project-invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Equal(1, factory.CountMembers(factory.Project.Id, factory.Recipient.Id));
        using var duplicateMember = await owner.PostAsJsonAsync(
            $"/api/projects/{factory.Project.Id}/invitations",
            new { recipientEmail = factory.Recipient.Email, role = "Viewer" });
        Assert.Equal(HttpStatusCode.Conflict, duplicateMember.StatusCode);
    }

    [Fact]
    public async Task Creation_is_owner_only_and_acceptance_rejects_wrong_or_unverified_recipient()
    {
        using var factory = new InvitationApiFactory();
        using var nonOwner = factory.AuthenticatedClient(factory.Other);
        using var denied = await nonOwner.PostAsJsonAsync($"/api/projects/{factory.Project.Id}/invitations",
            new { recipientEmail = factory.Recipient.Email, role = "Viewer" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var owner = factory.AuthenticatedClient(factory.Owner);
        using var created = await owner.PostAsJsonAsync($"/api/projects/{factory.Project.Id}/invitations",
            new { recipientEmail = factory.Recipient.Email, role = "Viewer" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var token = factory.Email.LastToken();

        using var wrongRecipient = factory.AuthenticatedClient(factory.Other);
        using var wrong = await wrongRecipient.PostAsJsonAsync("/api/project-invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);

        factory.SetRecipientVerified(false);
        using var unverified = factory.AuthenticatedClient(factory.Recipient);
        using var rejected = await unverified.PostAsJsonAsync("/api/project-invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Null(factory.ReadMember(factory.Project.Id, factory.Recipient.Id));
    }

    [Fact]
    public async Task Existing_membership_route_cannot_bypass_invitation_acceptance()
    {
        using var factory = new InvitationApiFactory();
        using var owner = factory.AuthenticatedClient(factory.Owner);
        using var response = await owner.PutAsJsonAsync(
            $"/api/projects/{factory.Project.Id}/members/{factory.Recipient.Id}", new { role = "Viewer" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Null(factory.ReadMember(factory.Project.Id, factory.Recipient.Id));
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    [InlineData("Member")]
    public async Task Creation_allows_only_viewer_or_editor_roles(string role)
    {
        using var factory = new InvitationApiFactory();
        using var owner = factory.AuthenticatedClient(factory.Owner);
        using var response = await owner.PostAsJsonAsync($"/api/projects/{factory.Project.Id}/invitations",
            new { recipientEmail = factory.Recipient.Email, role });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Email.Count);
    }

    [Fact]
    public async Task Invitation_management_is_rate_limited_per_authenticated_user()
    {
        using var factory = new InvitationApiFactory();
        using var owner = factory.AuthenticatedClient(factory.Owner);
        var path = $"/api/projects/{factory.Project.Id}/invitations";
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await owner.PostAsJsonAsync(path,
                new { recipientEmail = factory.Recipient.Email, role = "Owner" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var limited = await owner.PostAsJsonAsync(path,
            new { recipientEmail = factory.Recipient.Email, role = "Owner" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Duplicate_pending_invitation_and_cross_project_access_are_rejected()
    {
        using var factory = new InvitationApiFactory();
        using var owner = factory.AuthenticatedClient(factory.Owner);
        using var first = await owner.PostAsJsonAsync($"/api/projects/{factory.Project.Id}/invitations",
            new { recipientEmail = factory.Recipient.Email, role = "Viewer" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var duplicate = await owner.PostAsJsonAsync($"/api/projects/{factory.Project.Id}/invitations",
            new { recipientEmail = "RECIPIENT@EXAMPLE.TEST", role = "Editor" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var crossProject = await owner.GetAsync($"/api/projects/{factory.OtherProject.Id}/invitations");
        Assert.Equal(HttpStatusCode.Forbidden, crossProject.StatusCode);
        Assert.Equal(1, factory.CountInvitations(factory.Project.Id));
    }

    [Fact]
    public async Task Revoked_and_expired_tokens_cannot_be_accepted()
    {
        using var factory = new InvitationApiFactory();
        using var owner = factory.AuthenticatedClient(factory.Owner);
        using var recipient = factory.AuthenticatedClient(factory.Recipient);
        using var created = await owner.PostAsJsonAsync($"/api/projects/{factory.Project.Id}/invitations",
            new { recipientEmail = factory.Recipient.Email, role = "Viewer" });
        var invitation = await created.Content.ReadFromJsonAsync<ProjectInvitationDetails>();
        var revokedToken = factory.Email.LastToken();
        using var revoked = await owner.PostAsync(
            $"/api/projects/{factory.Project.Id}/invitations/{invitation!.Id}/revoke", null);
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        using var revokedAccept = await recipient.PostAsJsonAsync("/api/project-invitations/accept",
            new { token = revokedToken });
        Assert.Equal(HttpStatusCode.Conflict, revokedAccept.StatusCode);

        using var second = await owner.PostAsJsonAsync($"/api/projects/{factory.Project.Id}/invitations",
            new { recipientEmail = factory.Recipient.Email, role = "Viewer" });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var expiredToken = factory.Email.LastToken();
        factory.ExpireLatestInvitation();
        using var expiredAccept = await recipient.PostAsJsonAsync("/api/project-invitations/accept",
            new { token = expiredToken });
        Assert.Equal(HttpStatusCode.Conflict, expiredAccept.StatusCode);
    }

    [Fact]
    public async Task Resend_invalidates_the_old_token_and_the_new_token_can_be_accepted()
    {
        using var factory = new InvitationApiFactory();
        using var owner = factory.AuthenticatedClient(factory.Owner);
        using var recipient = factory.AuthenticatedClient(factory.Recipient);
        using var created = await owner.PostAsJsonAsync($"/api/projects/{factory.Project.Id}/invitations",
            new { recipientEmail = factory.Recipient.Email, role = "Editor" });
        var invitation = await created.Content.ReadFromJsonAsync<ProjectInvitationDetails>();
        var oldToken = factory.Email.LastToken();

        using var resent = await owner.PostAsync(
            $"/api/projects/{factory.Project.Id}/invitations/{invitation!.Id}/resend", null);
        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
        var newToken = factory.Email.LastToken();
        Assert.NotEqual(oldToken, newToken);
        using var oldAccept = await recipient.PostAsJsonAsync("/api/project-invitations/accept",
            new { token = oldToken });
        Assert.Equal(HttpStatusCode.Conflict, oldAccept.StatusCode);
        using var newAccept = await recipient.PostAsJsonAsync("/api/project-invitations/accept",
            new { token = newToken });
        Assert.Equal(HttpStatusCode.OK, newAccept.StatusCode);
        Assert.Equal(2, factory.CountInvitations(factory.Project.Id));
    }

    [Fact]
    public async Task Rejection_is_final_and_never_creates_membership()
    {
        using var factory = new InvitationApiFactory();
        using var owner = factory.AuthenticatedClient(factory.Owner);
        using var recipient = factory.AuthenticatedClient(factory.Recipient);
        using var created = await owner.PostAsJsonAsync($"/api/projects/{factory.Project.Id}/invitations",
            new { recipientEmail = factory.Recipient.Email, role = "Viewer" });
        var token = factory.Email.LastToken();
        using var rejected = await recipient.PostAsJsonAsync("/api/project-invitations/reject", new { token });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        using var replay = await recipient.PostAsJsonAsync("/api/project-invitations/accept", new { token });
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.Null(factory.ReadMember(factory.Project.Id, factory.Recipient.Id));
    }

    [Fact]
    public async Task Concurrent_acceptance_creates_exactly_one_membership()
    {
        using var factory = new InvitationApiFactory();
        using var owner = factory.AuthenticatedClient(factory.Owner);
        using var created = await owner.PostAsJsonAsync($"/api/projects/{factory.Project.Id}/invitations",
            new { recipientEmail = factory.Recipient.Email, role = "Viewer" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var token = factory.Email.LastToken();
        using var first = factory.AuthenticatedClient(factory.Recipient);
        using var second = factory.AuthenticatedClient(factory.Recipient);

        var responses = await Task.WhenAll(
            first.PostAsJsonAsync("/api/project-invitations/accept", new { token }),
            second.PostAsJsonAsync("/api/project-invitations/accept", new { token }));
        try
        {
            Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(1, factory.CountMembers(factory.Project.Id, factory.Recipient.Id));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    private sealed class InvitationApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection =
            new($"Data Source=invitation-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False;Default Timeout=10");

        public User Owner { get; } = User("owner@example.test", true);
        public User Recipient { get; } = User("recipient@example.test", true);
        public User Other { get; } = User("other@example.test", true);
        public Project Project { get; }
        public Project OtherProject { get; }
        public CapturingEmailService Email { get; } = new();

        public InvitationApiFactory()
        {
            Project = new Project { Name = "Invitation project", Description = "Test", OwnerId = Owner.Id };
            OtherProject = new Project { Name = "Other project", Description = "Test", OwnerId = Other.Id };
            _connection.Open();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            db.Database.EnsureCreated();
            db.Users.AddRange(Owner, Recipient, Other);
            db.Projects.AddRange(Project, OtherProject);
            db.SaveChanges();
        }

        public HttpClient AuthenticatedClient(User user)
        {
            var client = CreateClient();
            using var scope = Services.CreateScope();
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", tokens.CreateAccessToken(user).Token);
            return client;
        }

        public ProjectMember? ReadMember(Guid projectId, Guid userId)
        {
            using var scope = Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>().ProjectMembers.AsNoTracking()
                .SingleOrDefault(member => member.ProjectId == projectId && member.UserId == userId);
        }

        public int CountMembers(Guid projectId, Guid userId)
        {
            using var scope = Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>().ProjectMembers.AsNoTracking()
                .Count(member => member.ProjectId == projectId && member.UserId == userId);
        }

        public int CountInvitations(Guid projectId)
        {
            using var scope = Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>().ProjectInvitations.AsNoTracking()
                .Count(invitation => invitation.ProjectId == projectId);
        }

        public ProjectInvitation ReadInvitation(Guid invitationId)
        {
            using var scope = Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>().ProjectInvitations.AsNoTracking()
                .Single(invitation => invitation.Id == invitationId);
        }

        public AuditEvent[] ReadAuditEvents()
        {
            using var scope = Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>().AuditEvents.AsNoTracking().ToArray();
        }

        public void SetRecipientVerified(bool verified)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            db.Users.Single(user => user.Id == Recipient.Id).EmailVerified = verified;
            db.SaveChanges();
        }

        public void ExpireLatestInvitation()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            db.ProjectInvitations.OrderByDescending(invitation => invitation.CreatedAtUnixTimeMilliseconds)
                .First().ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            db.SaveChanges();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:ReqvioraDatabase", "Host=localhost;Database=unused");
            builder.UseSetting("Jwt:Key", "invitation-test-signing-key-at-least-32-characters");
            builder.UseSetting("AccountLinks:BaseUrl", "https://app.example.test");
            builder.UseSetting("Invitations:ExpirationHours", "1");
            builder.UseSetting("Security:AuditEnabled", "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
                services.RemoveAll<DbContextOptions<ReqvioraDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ReqvioraDbContext>>();
                services.RemoveAll<ReqvioraDbContext>();
                services.RemoveAll<IEmailService>();
                services.AddDbContext<ReqvioraDbContext>(options => options.UseSqlite(_connection.ConnectionString));
                services.AddSingleton<IEmailService>(Email);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection.Dispose();
        }

        private static User User(string email, bool verified) => new()
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = email.Split('@')[0],
            PasswordHash = "not-used-in-tests",
            EmailVerified = verified
        };
    }

    private sealed class CapturingEmailService : IEmailService
    {
        private readonly ConcurrentQueue<string> _bodies = new();
        public int Count => _bodies.Count;

        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
        {
            _bodies.Enqueue(body);
            return Task.CompletedTask;
        }

        public string LastToken()
        {
            var body = _bodies.Last();
            var link = body[(body.LastIndexOf("Open: ", StringComparison.Ordinal) + 6)..];
            var query = new Uri(link).Query.TrimStart('?');
            return Uri.UnescapeDataString(query.Split('=', 2)[1]);
        }
    }

}
