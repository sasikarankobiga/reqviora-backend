using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.Mcp;
using Reqviora.Application.Features.Validation;
using Reqviora.Application.Features.PlatformSettings;
using Reqviora.Application.Features.Incidents;
using Reqviora.Application.Features.AdminProfile;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class McpGrantEndpointsTests
{
    [Fact]
    public async Task Owner_creates_revision_bound_grant_and_secret_is_returned_only_once()
    {
        using var factory = new McpApiFactory();
        using var owner = factory.Authenticated(factory.Owner);

        using var created = await owner.PostAsJsonAsync($"/api/projects/{factory.Project.Id}/mcp/grants", new
        {
            approvedRevisionId = factory.Revision.Id,
            lifetimeMinutes = 30
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdText = await created.Content.ReadAsStringAsync();
        using var payload = JsonDocument.Parse(createdText);
        var credential = payload.RootElement.GetProperty("credential").GetString();
        var grantId = payload.RootElement.GetProperty("grant").GetProperty("id").GetGuid();
        Assert.False(string.IsNullOrWhiteSpace(credential));

        var stored = factory.ReadGrant(grantId);
        Assert.NotEqual(credential, stored.SecretHash);
        Assert.Equal(64, stored.SecretHash.Length);

        using var listed = await owner.GetAsync($"/api/projects/{factory.Project.Id}/mcp/grants");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var listText = await listed.Content.ReadAsStringAsync();
        Assert.DoesNotContain("credential", listText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", listText, StringComparison.OrdinalIgnoreCase);

        using var outsider = factory.Authenticated(factory.OtherUser);
        using var hidden = await outsider.GetAsync($"/api/projects/{factory.Project.Id}/mcp/grants");
        Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);

        using var allowed = new HttpRequestMessage(HttpMethod.Post, "/api/mcp/v1/tools/call")
        {
            Content = JsonContent.Create(new { name = "reqviora.project.overview", projectId = factory.Project.Id })
        };
        allowed.Headers.Add("X-MCP-Grant", credential);
        using var allowedResponse = await owner.SendAsync(allowed);
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);

        using var crossed = new HttpRequestMessage(HttpMethod.Post, "/api/mcp/v1/tools/call")
        {
            Content = JsonContent.Create(new { name = "reqviora.project.overview", projectId = factory.OtherProject.Id })
        };
        crossed.Headers.Add("X-MCP-Grant", credential);
        using var crossedResponse = await owner.SendAsync(crossed);
        Assert.Equal(HttpStatusCode.Forbidden, crossedResponse.StatusCode);
    }

    [Fact]
    public async Task Grant_reads_its_immutable_approved_snapshot_not_a_newer_draft()
    {
        using var factory = new McpApiFactory();
        var created = await factory.CreateGrantAsync();
        using var owner = factory.Authenticated(factory.Owner);
        using var call = new HttpRequestMessage(HttpMethod.Post, "/api/mcp/v1/tools/call")
        {
            Content = JsonContent.Create(new { name = "reqviora.blueprint.current", projectId = factory.Project.Id })
        };
        call.Headers.Add("X-MCP-Grant", created.Credential);

        using var response = await owner.SendAsync(call);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("approved-snapshot", body, StringComparison.Ordinal);
        Assert.DoesNotContain("mutable-draft", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_directory_is_protected_and_emergency_revocation_is_audited_and_idempotent()
    {
        using var factory = new McpApiFactory();
        var created = await factory.CreateGrantAsync();
        using var normal = factory.Authenticated(factory.OtherUser);
        using var forbidden = await normal.GetAsync("/api/admin/mcp/grants");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var admin = factory.Authenticated(factory.Admin);
        using var listed = await admin.GetAsync("/api/admin/mcp/grants?state=active&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var listText = await listed.Content.ReadAsStringAsync();
        Assert.Contains(created.Grant.Id.ToString(), listText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", listText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", listText, StringComparison.OrdinalIgnoreCase);

        var route = $"/api/admin/mcp/grants/{created.Grant.Id}/revoke";
        using var revoked = await admin.PostAsJsonAsync(route, new { reason = "Suspected credential exposure" });
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        using var repeated = await admin.PostAsJsonAsync(route, new { reason = "Duplicate emergency request" });
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Single(factory.SecurityAuditEvents(created.Grant.Id));

        using var owner = factory.Authenticated(factory.Owner);
        using var call = new HttpRequestMessage(HttpMethod.Post, "/api/mcp/v1/tools/call")
        {
            Content = JsonContent.Create(new { name = "reqviora.project.overview", projectId = factory.Project.Id })
        };
        call.Headers.Add("X-MCP-Grant", created.Credential);
        using var response = await owner.SendAsync(call);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Expired_grant_is_rejected_without_changing_persisted_history()
    {
        using var factory = new McpApiFactory();
        var created = await factory.CreateGrantAsync(TimeSpan.FromMinutes(5));
        factory.Clock.Advance(TimeSpan.FromMinutes(6));

        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMcpGrantService>();
        Assert.False(await service.ValidateAsync(created.Credential, factory.Project.Id, factory.Owner.Id));
        var metadata = await service.FindAdminAsync(created.Grant.Id);
        Assert.Equal("expired", metadata!.State);
        Assert.Null(metadata.RevokedAt);
    }

    [Fact]
    public async Task Export_diagnostics_are_admin_only_filtered_and_metadata_only()
    {
        using var factory = new McpApiFactory();
        var jobId = factory.AddExportDiagnostic("Failed");
        using var normal = factory.Authenticated(factory.OtherUser);
        using var forbidden = await normal.GetAsync("/api/admin/exports/jobs");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var admin = factory.Authenticated(factory.Admin);
        using var listed = await admin.GetAsync("/api/admin/exports/jobs?status=Failed&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var text = await listed.Content.ReadAsStringAsync();
        Assert.Contains(jobId.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("archive", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("manifest", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storagePath", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack", text, StringComparison.OrdinalIgnoreCase);

        using var summary = await admin.GetAsync("/api/admin/exports/summary");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        using var json = JsonDocument.Parse(await summary.Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetProperty("failed").GetInt32());
    }

    [Fact]
    public async Task Validation_health_aggregates_utc_days_and_hides_private_finding_details()
    {
        using var factory = new McpApiFactory();
        using var owner = factory.Authenticated(factory.Owner);
        using var validation = await owner.PostAsync($"/api/projects/{factory.Project.Id}/builder/validate", null);
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);

        using var scope = factory.Services.CreateScope();
        var health = scope.ServiceProvider.GetRequiredService<IValidationHealth>();
        await health.RecordAsync(ValidationWorkflow.Blueprint, TimeSpan.FromMilliseconds(15), [], default);
        await health.RecordAsync(ValidationWorkflow.Blueprint, TimeSpan.FromMilliseconds(25),
            [new("BLUEPRINT_SCHEMA", "$"), new("BROKEN_REFERENCE", "$.navigation.private-marker"),
                new("UNSUPPORTED_COMPONENT", "$.components.private-marker")], default);

        using var denied = await owner.GetAsync("/api/admin/validation/summary");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var admin = factory.Authenticated(factory.Admin);
        using var summary = await admin.GetAsync("/api/admin/validation/summary");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        var body = await summary.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-marker", body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(3, json.RootElement.GetProperty("attempts").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("succeeded").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("failed").GetInt32());
        var errors = json.RootElement.GetProperty("errors");
        foreach (var category in new[] { "schema", "brokenReferences", "unsupportedComponents", "navigation" })
            Assert.Equal(1, errors.GetProperty(category).GetInt32());

        using var trends = await admin.GetAsync("/api/admin/validation/trends?from=2000-01-01&to=2000-01-02");
        Assert.Equal(HttpStatusCode.OK, trends.StatusCode);
        Assert.Empty((await trends.Content.ReadFromJsonAsync<ValidationDay[]>())!);
    }

    [Theory]
    [InlineData(false, "disabled")]
    [InlineData(true, "unavailable")]
    public async Task Ai_queue_monitoring_uses_persisted_metadata_and_current_configuration(bool enabled, string expected)
    {
        using var factory = new McpApiFactory(enabled);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
        var now = factory.Clock.GetUtcNow();
        var job = new AiJob
        {
            ProjectId = factory.Project.Id, OwnerId = factory.Owner.Id,
            Prompt = "private-test-prompt", Result = "private-test-output", ErrorMessage = "private-test-error",
            IdempotencyKey = "first-job", State = AiJobState.Running, AttemptCount = 3,
            CreatedAt = now.AddMinutes(-5), LeaseExpiresAtUnixTimeMilliseconds = now.AddSeconds(-1).ToUnixTimeMilliseconds()
        };
        db.AiJobs.Add(job);
        await db.SaveChangesAsync();

        using var owner = factory.Authenticated(factory.Owner);
        using var forbidden = await owner.GetAsync("/api/admin/ai-jobs/health");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var admin = factory.Authenticated(factory.Admin);
        var payload = await admin.GetStringAsync("/api/admin/ai-jobs?status=processing&type=completion&pageSize=1");
        Assert.DoesNotContain("private-test-", payload);
        var page = JsonSerializer.Deserialize<AdminAiJobPage>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(job.Id, Assert.Single(page.Items).Id);
        var stats = await admin.GetFromJsonAsync<AiQueueStatistics>("/api/admin/ai-jobs/statistics");
        Assert.Equal(1, stats!.Processing);
        Assert.Equal(1, stats.StaleJobs);
        Assert.Equal(1, stats.ExpiredLeases);
        Assert.Equal(2, stats.RetryAttempts);
        var health = await admin.GetFromJsonAsync<AiQueueHealth>("/api/admin/ai-jobs/health");
        Assert.Equal(expected, health!.ProviderStatus);
        Assert.Equal("degraded", health.QueueStatus);
    }

    [Fact]
    public async Task Settings_are_allowlisted_versioned_audited_and_enforced_on_upload()
    {
        using var factory = new McpApiFactory();
        using var admin = factory.Authenticated(factory.Admin);
        using var owner = factory.Authenticated(factory.Owner);
        const string route = "/api/admin/settings/document-upload-max-bytes";
        using var forbidden = await owner.GetAsync(route);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        foreach (var key in new[] { "jwt-signing-key", "mcp-enabled", "ai-enabled" })
        {
            using var rejected = await admin.PutAsJsonAsync($"/api/admin/settings/{key}",
                new { value = 1, expectedRevision = 0, reason = "Disallowed setting" });
            Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
        }
        using var invalid = await admin.PutAsJsonAsync(route,
            new { value = 0, expectedRevision = 0, reason = "Invalid limit" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var first = await admin.PutAsJsonAsync(route,
            new { value = 4, expectedRevision = 0, reason = "First upload limit" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var stale = await admin.PutAsJsonAsync(route,
            new { value = 12, expectedRevision = 0, reason = "Stale edit" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var second = await admin.PutAsJsonAsync(route,
            new { value = 8, expectedRevision = 1, reason = "Second upload limit" });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var rollback = await admin.PostAsJsonAsync(route + "/rollback",
            new { targetRevision = 1, expectedRevision = 2, reason = "Restore first limit" });
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);
        var history = await admin.GetFromJsonAsync<SettingHistoryEntry[]>(route + "/history");
        Assert.NotNull(history);
        Assert.Equal(new[] { 3, 2, 1 }, history.Select(x => x.Revision));
        Assert.Equal(1, history[0].RolledBackTo);
        Assert.Equal(8, history[0].PreviousValue);
        Assert.Equal(4, history[0].Value);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("eight"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "test.txt");
        using var oversized = await owner.PostAsync($"/api/projects/{factory.Project.Id}/documents", form);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
        Assert.Equal(3, await db.AuditEvents.CountAsync(x => x.Path.StartsWith("/security/platform-settings/")));
    }

    [Fact]
    public async Task Incidents_are_admin_only_searchable_audited_and_follow_valid_transitions()
    {
        using var factory = new McpApiFactory();
        using var normal = factory.Authenticated(factory.Owner);
        using var forbidden = await normal.GetAsync("/api/admin/incidents");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var admin = factory.Authenticated(factory.Admin);
        using var credentialLeak = await admin.PostAsJsonAsync("/api/admin/incidents", new
        {
            category = "api_key=do-not-store", priority = "High", correlationId = "trace-unsafe"
        });
        Assert.Equal(HttpStatusCode.BadRequest, credentialLeak.StatusCode);

        using var createdResponse = await admin.PostAsJsonAsync("/api/admin/incidents", new
        {
            category = "Export", priority = "Critical", correlationId = "trace-123"
        });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = await createdResponse.Content.ReadFromJsonAsync<IncidentDetails>();
        Assert.NotNull(created);
        Assert.StartsWith("INC-", created.Reference);
        Assert.Equal("Open", created.Status);
        using var secondCreateResponse = await admin.PostAsJsonAsync("/api/admin/incidents", new
        {
            category = "Authentication", priority = "Low", correlationId = "trace-456"
        });
        var secondCreated = await secondCreateResponse.Content.ReadFromJsonAsync<IncidentDetails>();
        Assert.Equal(HttpStatusCode.Created, secondCreateResponse.StatusCode);
        Assert.NotNull(secondCreated);
        Assert.NotEqual(created.Reference, secondCreated.Reference);

        using var invalidClose = await admin.PutAsJsonAsync($"/api/admin/incidents/{created.Id}/status", new
        {
            status = "Closed", expectedConcurrencyToken = created.ConcurrencyToken
        });
        Assert.Equal(HttpStatusCode.Conflict, invalidClose.StatusCode);

        using var assignedResponse = await admin.PutAsJsonAsync($"/api/admin/incidents/{created.Id}/assignment", new
        {
            assignedAdminId = factory.Admin.Id, expectedConcurrencyToken = created.ConcurrencyToken
        });
        Assert.Equal(HttpStatusCode.OK, assignedResponse.StatusCode);
        var assigned = await assignedResponse.Content.ReadFromJsonAsync<IncidentDetails>();
        Assert.NotNull(assigned);
        Assert.Equal(factory.Admin.Id, assigned.AssignedAdminId);

        using var stale = await admin.PutAsJsonAsync($"/api/admin/incidents/{created.Id}/status", new
        {
            status = "InProgress", expectedConcurrencyToken = created.ConcurrencyToken
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var progressResponse = await admin.PutAsJsonAsync($"/api/admin/incidents/{created.Id}/status", new
        {
            status = "InProgress", expectedConcurrencyToken = assigned.ConcurrencyToken
        });
        var progress = await progressResponse.Content.ReadFromJsonAsync<IncidentDetails>();
        Assert.Equal(HttpStatusCode.OK, progressResponse.StatusCode);
        Assert.NotNull(progress);
        using var resolvedResponse = await admin.PutAsJsonAsync($"/api/admin/incidents/{created.Id}/status", new
        {
            status = "Resolved", resolutionNotes = "Export worker recovered after retry.",
            expectedConcurrencyToken = progress.ConcurrencyToken
        });
        var resolved = await resolvedResponse.Content.ReadFromJsonAsync<IncidentDetails>();
        Assert.Equal(HttpStatusCode.OK, resolvedResponse.StatusCode);
        Assert.NotNull(resolved);

        var page = await admin.GetFromJsonAsync<IncidentPage>(
            "/api/admin/incidents?search=trace-123&priority=critical&status=resolved&page=1&pageSize=10");
        Assert.NotNull(page);
        Assert.Equal(1, page.Total);
        Assert.Equal(created.Id, Assert.Single(page.Items).Id);
        var summary = await admin.GetFromJsonAsync<IncidentSummary>("/api/admin/incidents/summary");
        Assert.NotNull(summary);
        Assert.Equal(1, summary.Resolved);
        Assert.Equal(1, summary.Critical);
        var history = await admin.GetFromJsonAsync<IncidentHistoryEntry[]>(
            $"/api/admin/incidents/{created.Id}/history");
        Assert.NotNull(history);
        Assert.Equal(4, history.Length);
        Assert.DoesNotContain(JsonSerializer.Serialize(history), "password", StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
        Assert.Equal(4, await db.AuditEvents.CountAsync(x => x.Path.StartsWith($"/security/incidents/{created.Id}/")));
    }

    [Fact]
    public async Task Admin_profile_sessions_are_bound_redacted_and_individually_revocable()
    {
        using var factory = new McpApiFactory();
        using var normal = factory.Authenticated(factory.Owner);
        using var forbidden = await normal.GetAsync("/api/admin/profile");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var firstSession = factory.Session(factory.Admin);
        var secondSession = factory.Session(factory.Admin);
        using var first = firstSession.Client;
        using var second = secondSession.Client;
        using var profileResponse = await first.GetAsync("/api/admin/profile");
        Assert.True(profileResponse.StatusCode == HttpStatusCode.OK, await profileResponse.Content.ReadAsStringAsync());
        var profile = await profileResponse.Content.ReadFromJsonAsync<AdminProfileDetails>();
        Assert.NotNull(profile);
        Assert.True(profile.MfaEnabled);

        using var escalation = await first.PutAsJsonAsync("/api/admin/profile", new
        {
            displayName = "Escalation rejected", role = "SuperAdministrator"
        });
        Assert.Equal(HttpStatusCode.BadRequest, escalation.StatusCode);
        using var updatedResponse = await first.PutAsJsonAsync("/api/admin/profile", new
        {
            displayName = "Security Administrator"
        });
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = await updatedResponse.Content.ReadFromJsonAsync<AdminProfileDetails>();
        Assert.Equal("Security Administrator", updated!.DisplayName);

        var sessionsText = await first.GetStringAsync("/api/admin/profile/sessions");
        Assert.DoesNotContain("tokenHash", sessionsText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", sessionsText, StringComparison.OrdinalIgnoreCase);
        var sessions = JsonSerializer.Deserialize<AdminSessionDetails[]>(sessionsText,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(sessions);
        Assert.Equal(2, sessions.Length);
        Assert.Equal(firstSession.Id, Assert.Single(sessions, x => x.IsCurrent).Id);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            db.LoginAudits.Add(new LoginAudit { UserId = factory.Admin.Id,
                NormalizedEmail = factory.Admin.NormalizedEmail, IpAddress = "127.0.0.1", DeviceId = "test-browser",
                Succeeded = true, OtpRequired = true, OccurredAt = factory.Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }
        var history = await first.GetFromJsonAsync<AdminLoginHistoryEntry[]>("/api/admin/profile/login-history");
        Assert.NotNull(history);
        Assert.True(Assert.Single(history).OtpRequired);
        var securityText = await first.GetStringAsync("/api/admin/profile/security");
        Assert.DoesNotContain("password", securityText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mfaSecret", securityText, StringComparison.OrdinalIgnoreCase);

        using var revoked = await first.DeleteAsync($"/api/admin/profile/sessions/{secondSession.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using var rejected = await second.GetAsync("/api/admin/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        using var stillActive = await first.GetAsync("/api/admin/profile");
        Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);

        var thirdSession = factory.Session(factory.Admin);
        using var third = thirdSession.Client;
        using var revokeOthers = await first.PostAsync("/api/admin/profile/sessions/revoke-others", null);
        Assert.Equal(HttpStatusCode.OK, revokeOthers.StatusCode);
        using var thirdRejected = await third.GetAsync("/api/admin/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, thirdRejected.StatusCode);
        using var firstStillActive = await first.GetAsync("/api/admin/profile");
        Assert.Equal(HttpStatusCode.OK, firstStillActive.StatusCode);
    }

    private sealed class McpApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection =
            new($"Data Source=admin-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False");
        private readonly string _jwtSigningKey =
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        public MutableTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
        public User Owner { get; } = User("owner@mcp.test");
        public User OtherUser { get; } = User("user@mcp.test");
        public User Admin { get; } = User("admin@mcp.test", mfa: true);
        public Project Project { get; }
        public Project OtherProject { get; }
        public ApprovedRevision Revision { get; private set; } = null!;

        private readonly bool _aiEnabled;

        public McpApiFactory(bool aiEnabled = false)
        {
            _aiEnabled = aiEnabled;
            Project = new Project { Name = "Approved", Description = "MCP", OwnerId = Owner.Id };
            OtherProject = new Project { Name = "Other", Description = "MCP", OwnerId = Owner.Id };
            _connection.Open();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            db.Database.EnsureCreated();
            var role = new Role { Name = "Administrator" };
            Admin.RoleId = role.Id;
            db.Roles.Add(role);
            db.Users.AddRange(Owner, OtherUser, Admin);
            db.Projects.AddRange(Project, OtherProject);
            db.SaveChanges();

            var draft = new ProjectDraft { ProjectId = Project.Id, DraftJson = "{}", Version = 1 };
            db.ProjectDrafts.Add(draft);
            db.SaveChanges();
            var version = new DesignDraftVersion
            {
                ProjectDraftId = draft.Id,
                ProjectId = Project.Id,
                Version = 1,
                DraftJson = "{}",
                BlueprintJson = "{\"source\":\"mutable-draft\"}",
                SavedAt = Clock.GetUtcNow()
            };
            db.DesignDraftVersions.Add(version);
            db.SaveChanges();
            var validation = new ValidationRun
            {
                DraftVersionId = version.Id,
                Passed = true,
                ValidatedAt = Clock.GetUtcNow()
            };
            db.ValidationRuns.Add(validation);
            db.SaveChanges();
            Revision = new ApprovedRevision
            {
                ProjectId = Project.Id,
                DraftVersionId = version.Id,
                ValidationRunId = validation.Id,
                ApprovedBy = Owner.Id,
                ApprovedAt = Clock.GetUtcNow(),
                SnapshotJson = "{\"source\":\"approved-snapshot\"}"
            };
            db.ApprovedRevisions.Add(Revision);
            db.SaveChanges();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:ReqvioraDatabase", "Host=localhost;Database=unused");
            builder.UseSetting("Jwt:Key", _jwtSigningKey);
            builder.UseSetting("Mcp:Enabled", "true");
            builder.UseSetting("AI:Enabled", _aiEnabled.ToString());
            builder.UseSetting("AI:ApiKey", string.Empty);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<DbContextOptions<ReqvioraDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ReqvioraDbContext>>();
                services.RemoveAll<ReqvioraDbContext>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddDbContext<ReqvioraDbContext>(options => options.UseSqlite(_connection.ConnectionString));
            });
        }

        public HttpClient Authenticated(User user)
        {
            var client = CreateClient();
            using var scope = Services.CreateScope();
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", tokens.CreateAccessToken(user).Token);
            return client;
        }

        public (HttpClient Client, Guid Id) Session(User user)
        {
            var client = CreateClient();
            using var scope = Services.CreateScope();
            var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
            var refresh = tokens.CreateRefreshToken();
            var entity = new RefreshToken
            {
                UserId = user.Id, TokenHash = refresh.TokenHash, CreatedAt = Clock.GetUtcNow(),
                ExpiresAt = refresh.ExpiresAt
            };
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            db.RefreshTokens.Add(entity);
            db.SaveChanges();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", tokens.CreateAccessToken(user, entity.Id).Token);
            return (client, entity.Id);
        }

        public async Task<McpGrantCreated> CreateGrantAsync(TimeSpan? lifetime = null)
        {
            using var scope = Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IMcpGrantService>().CreateAsync(
                Project.Id, Owner.Id, Revision.Id, lifetime ?? TimeSpan.FromMinutes(30));
        }

        public McpAccessGrant ReadGrant(Guid grantId)
        {
            using var scope = Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>()
                .McpAccessGrants.AsNoTracking().Single(grant => grant.Id == grantId);
        }

        public AuditEvent[] SecurityAuditEvents(Guid grantId)
        {
            using var scope = Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>().AuditEvents.AsNoTracking()
                .Where(item => item.Path == $"/security/mcp/grants/{grantId}/revoked").ToArray();
        }

        public Guid AddExportDiagnostic(string status)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            var job = new ExportJobDiagnostic
            {
                ProjectId = Project.Id,
                ApprovedRevisionId = Revision.Id,
                ExportType = "SecondBrain",
                Status = status,
                CreatedAt = Clock.GetUtcNow(),
                StartedAt = Clock.GetUtcNow(),
                CompletedAt = Clock.GetUtcNow(),
                DurationMilliseconds = 12,
                FailureCode = status == "Failed" ? "EXPORT_STORAGE_FAILED" : null,
                FailureCategory = status == "Failed" ? "Storage" : null,
                IntegrityStatus = "Unknown"
            };
            db.ExportJobDiagnostics.Add(job);
            db.SaveChanges();
            return job.Id;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _connection.Dispose();
        }

        private static User User(string email, bool mfa = false) => new()
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "not-used",
            DisplayName = email,
            EmailVerified = true,
            MfaEnabled = mfa
        };
    }

    private sealed class MutableTimeProvider(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }
}
