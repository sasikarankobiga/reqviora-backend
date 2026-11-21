using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
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
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class KobigaWorkflowEndpointsTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private string Prefix => $"/api/projects/{_factory.Project.Id}/workflow-intelligence";

    [Fact]
    public async Task Authentication_and_owner_access_are_enforced()
    {
        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync(Prefix + "/reviews/REQ-001")).StatusCode);
        using var other = Client(_factory.Other);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await other.GetAsync(Prefix + "/reviews/REQ-001")).StatusCode);
    }

    [Fact]
    public async Task Only_human_confirmed_reviews_can_generate_a_saved_blueprint()
    {
        using var client = Client(_factory.Owner);
        var body = GenerateBody();
        using var rejected = await client.PostAsJsonAsync(Prefix + "/blueprints/generate", body);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        _factory.Store.Confirm(_factory.Owner.Id);
        using var created = await client.PostAsJsonAsync(Prefix + "/blueprints/generate", body);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var result = await created.Content.ReadFromJsonAsync<KobigaWorkflowController.BlueprintWorkflowResponse>();
        Assert.NotNull(result);
        Assert.True(result.Applied);
        Assert.Equal("confirmed", Assert.Single(result.Blueprint.Requirements).Status);
        Assert.Equal(1, result.DraftVersion);

        using var stale = await client.PostAsJsonAsync(Prefix + "/blueprints/generate", body);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        // Preview preserves the previously saved draft and refuses protected screen edits.
        var proposed = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(result.Blueprint))!.AsObject();
        proposed["revision"] = 2;
        proposed["screens"]![0]!["name"] = "Unexpected AI rename";
        var previewBody = new
        {
            expectedConcurrencyToken = result.ConcurrencyToken,
            policy = new { expectedRevision = 1, protectedFeatureIds = Array.Empty<string>(),
                protectedScreenIds = new[] { "SCR-001" }, protectedComponentIds = Array.Empty<string>() },
            proposedBlueprint = proposed
        };
        using var preview = await client.PostAsJsonAsync(Prefix + "/blueprints/regenerate-preview", previewBody);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var fallback = await preview.Content.ReadFromJsonAsync<KobigaWorkflowController.BlueprintWorkflowResponse>();
        Assert.NotNull(fallback);
        Assert.False(fallback.Applied);
        Assert.Equal(result.ConcurrencyToken, fallback.ConcurrencyToken);
    }

    [Fact]
    public async Task Clarification_answers_are_saved_without_confirming_a_requirement()
    {
        using var client = Client(_factory.Owner);
        using var created = await client.PostAsJsonAsync(Prefix + "/clarifications", new
        {
            expectedConcurrencyToken = (Guid?)null,
            requirementId = "REQ-001",
            question = "Should staff also log in?"
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var objectNode = JsonNode.Parse(await created.Content.ReadAsStringAsync())!.AsObject();
        var questionId = Guid.Parse(objectNode["clarification"]!["id"]!.GetValue<string>());
        var token = Guid.Parse(objectNode["concurrencyToken"]!.GetValue<string>());
        using var answered = await client.PostAsJsonAsync(
            Prefix + $"/clarifications/{questionId}/answer", new
            {
                expectedConcurrencyToken = token,
                expectedRevision = 1,
                answer = "Yes, after user confirmation"
            });
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        using var list = await client.GetAsync(Prefix + "/clarifications");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var questions = JsonNode.Parse(await list.Content.ReadAsStringAsync())!.AsArray();
        Assert.True(questions[0]!["isResolved"]!.GetValue<bool>());
        Assert.Equal(RequirementReviewStatus.Suggested, _factory.Store.Current.Status);
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient Client(User user)
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.CreateAccessToken(user).Token);
        return client;
    }

    private static object GenerateBody() => new
    {
        expectedConcurrencyToken = (Guid?)null,
        blueprintId = "BP-TEST-001",
        confirmedRequirementIds = new[] { "REQ-001" },
        features = new[] { new { id = "FEAT-001", name = "Login", requirementIds = new[] { "REQ-001" } } },
        screens = new[] { new { id = "SCR-001", name = "Login", featureIds = new[] { "FEAT-001" } } },
        components = new[] { new { id = "CMP-001", screenId = "SCR-001", type = "Button", variant = "default" } },
        actions = Array.Empty<object>(),
        navigation = Array.Empty<object>()
    };

    private sealed class ApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _sqlite = new("Data Source=:memory:");
        public User Owner { get; } = UserWithEmail("kobiga-owner@example.test");
        public User Other { get; } = UserWithEmail("other-user@example.test");
        public Project Project { get; }
        public ReviewStore Store { get; } = new();

        public ApiFactory()
        {
            Project = new Project { Name = "Kobiga test", OwnerId = Owner.Id, PrimaryPlatform = "web" };
            Store.Seed(Project.Id);
            _sqlite.Open();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            db.Database.EnsureCreated();
            db.Users.AddRange(Owner, Other);
            db.Projects.Add(Project);
            db.SaveChanges();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:ReqvioraDatabase", "Host=localhost;Database=unused");
            builder.UseSetting("Jwt:Key", "kobiga-contract-test-key-with-at-least-32-characters");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ReqvioraDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ReqvioraDbContext>>();
                services.RemoveAll<ReqvioraDbContext>();
                services.RemoveAll<IRequirementReviewStore>();
                services.AddDbContext<ReqvioraDbContext>(options => options.UseSqlite(_sqlite));
                services.AddSingleton<IRequirementReviewStore>(Store);
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _sqlite.Dispose();
        }

        private static User UserWithEmail(string email) => new()
        {
            Email = email, NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "test-only", DisplayName = email
        };
    }

    private sealed class ReviewStore : IRequirementReviewStore
    {
        private Guid _project;
        public ReviewedRequirement Current { get; private set; } = null!;
        public void Seed(Guid project)
        {
            _project = project;
            Current = new ReviewedRequirement("REQ-001", "User can login", "Email and password",
                RequirementReviewStatus.Suggested, 1,
                [new CandidateEvidence("SRC-001", "page 1", "User can login")], null, null);
        }
        public void Confirm(Guid actor) => Current = Current with
        {
            Status = RequirementReviewStatus.Confirmed, Revision = 2,
            ConfirmedBy = actor, ConfirmedAt = DateTimeOffset.UtcNow
        };
        public Task<RequirementReviewSnapshot?> FindAsync(
            Guid projectId, string requirementId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RequirementReviewSnapshot?>(
                projectId == _project && Current.Id == requirementId
                    ? new RequirementReviewSnapshot(projectId, Current) : null);
        public Task<bool> TryCommitAsync(
            Guid projectId, int expectedRevision, RequirementReviewResult result,
            CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}