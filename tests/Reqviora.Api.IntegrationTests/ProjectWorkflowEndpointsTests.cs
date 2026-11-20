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
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Application.Features.UIGeneration.Blueprint.Validation;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class ProjectWorkflowEndpointsTests : IDisposable
{
    private readonly WorkflowApiFactory _factory = new();

    [Theory]
    [InlineData("/api/projects/{0}/drafts/current")]
    [InlineData("/api/projects/{0}/blueprints/current")]
    [InlineData("/api/projects/{0}/requirements")]
    [InlineData("/api/projects/{0}/features")]
    [InlineData("/api/projects/{0}/screens")]
    [InlineData("/api/projects/{0}/navigation")]
    [InlineData("/api/projects/{0}/approvals")]
    [InlineData("/api/projects/{0}/exports")]
    [InlineData("/api/projects/{0}/overview")]
    public async Task Sensitive_routes_require_authentication(string route)
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(string.Format(route, _factory.Project.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/projects/{0}/drafts/current")]
    [InlineData("/api/projects/{0}/blueprints/current")]
    [InlineData("/api/projects/{0}/requirements")]
    [InlineData("/api/projects/{0}/features")]
    [InlineData("/api/projects/{0}/screens")]
    [InlineData("/api/projects/{0}/navigation")]
    [InlineData("/api/projects/{0}/approvals")]
    [InlineData("/api/projects/{0}/exports")]
    [InlineData("/api/projects/{0}/overview")]
    public async Task Other_users_cannot_read_project_content(string route)
    {
        using var client = CreateClient(_factory.OtherUser);
        using var response = await client.GetAsync(string.Format(route, _factory.Project.Id));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Other_users_cannot_write_project_workflows()
    {
        using var client = CreateClient(_factory.OtherUser);
        using var draft = await client.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/drafts/current",
            new { draft = new { unauthorized = true }, expectedConcurrencyToken = (Guid?)null });
        Assert.Equal(HttpStatusCode.Forbidden, draft.StatusCode);

        using var approval = await client.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/approvals",
            new { expectedConcurrencyToken = Guid.NewGuid(), validationRunId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Forbidden, approval.StatusCode);

        using var export = await client.PostAsync(
            $"/api/projects/{_factory.Project.Id}/exports/from-revision/{Guid.NewGuid()}",
            null);
        Assert.Equal(HttpStatusCode.Forbidden, export.StatusCode);
    }

    [Fact]
    public async Task Draft_body_is_required()
    {
        using var client = CreateClient(_factory.Owner);
        using var response = await client.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/drafts/current",
            new { expectedConcurrencyToken = (Guid?)null });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Owner_can_read_workspace_and_project_overview()
    {
        using var client = CreateClient(_factory.Owner);
        using var workspaces = await client.GetAsync("/api/workspaces");
        Assert.True(workspaces.StatusCode == HttpStatusCode.OK, await workspaces.Content.ReadAsStringAsync());
        var workspaceItems = await workspaces.Content.ReadFromJsonAsync<WorkspaceResponse[]>();
        Assert.Single(workspaceItems ?? []);

        using var workspace = await client.GetAsync($"/api/workspaces/{_factory.Workspace.Id}");
        Assert.Equal(HttpStatusCode.OK, workspace.StatusCode);

        using var overview = await client.GetAsync($"/api/projects/{_factory.Project.Id}/overview");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        var item = await overview.Content.ReadFromJsonAsync<ProjectOverviewResponse>();
        Assert.Equal(_factory.Workspace.Id, item?.WorkspaceId);
    }

    [Fact]
    public async Task Draft_save_resume_history_and_stale_token_are_enforced()
    {
        using var client = CreateClient(_factory.Owner);
        var path = $"/api/projects/{_factory.Project.Id}/drafts/current";

        using var first = await client.PutAsJsonAsync(path, new
        {
            draft = new { title = "First" },
            expectedConcurrencyToken = (Guid?)null
        });
        Assert.True(first.StatusCode == HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var saved = await first.Content.ReadFromJsonAsync<ProjectDraftResponse>();
        Assert.NotNull(saved);

        using var resumed = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);

        using var updated = await client.PutAsJsonAsync(path, new
        {
            draft = new { title = "Second" },
            expectedConcurrencyToken = saved.ConcurrencyToken
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var stale = await client.PutAsJsonAsync(path, new
        {
            draft = new { title = "Stale" },
            expectedConcurrencyToken = saved.ConcurrencyToken
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var history = await client.GetAsync($"/api/projects/{_factory.Project.Id}/drafts/versions");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var versions = await history.Content.ReadFromJsonAsync<BlueprintVersionResponse[]>();
        Assert.Equal(2, versions?.Length);
    }

    [Fact]
    public async Task Blueprint_graph_approval_and_verified_export_are_available_to_owner()
    {
        using var client = CreateClient(_factory.Owner);
        var blueprint = LoadBlueprint(_factory.Project.Id);

        using var save = await client.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/blueprints/current",
            new { draft = new { secondBrain = new { } }, blueprint, expectedConcurrencyToken = (Guid?)null });
        Assert.True(save.StatusCode == HttpStatusCode.OK, await save.Content.ReadAsStringAsync());
        var draft = await save.Content.ReadFromJsonAsync<ProjectDraftResponse>();
        Assert.NotNull(draft);

        using var currentBlueprint = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/blueprints/current");
        Assert.Equal(HttpStatusCode.OK, currentBlueprint.StatusCode);
        using var blueprintVersions = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/blueprints/versions");
        Assert.Equal(HttpStatusCode.OK, blueprintVersions.StatusCode);

        using var features = await client.GetAsync($"/api/projects/{_factory.Project.Id}/features");
        Assert.Equal(HttpStatusCode.OK, features.StatusCode);
        Assert.NotEmpty(await features.Content.ReadFromJsonAsync<FeatureResponse[]>() ?? []);

        using var requirements = await client.GetAsync($"/api/projects/{_factory.Project.Id}/requirements");
        Assert.Equal(HttpStatusCode.OK, requirements.StatusCode);
        Assert.NotEmpty(await requirements.Content.ReadFromJsonAsync<RequirementResponse[]>() ?? []);
        using var requirement = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/requirements/{blueprint.Requirements[0].Id}");
        Assert.Equal(HttpStatusCode.OK, requirement.StatusCode);

        using var feature = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/features/{blueprint.Features[0].Id}");
        Assert.Equal(HttpStatusCode.OK, feature.StatusCode);
        using var screens = await client.GetAsync($"/api/projects/{_factory.Project.Id}/screens");
        Assert.Equal(HttpStatusCode.OK, screens.StatusCode);
        using var screen = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/screens/{blueprint.Screens[0].Id}");
        Assert.Equal(HttpStatusCode.OK, screen.StatusCode);
        using var navigation = await client.GetAsync($"/api/projects/{_factory.Project.Id}/navigation");
        Assert.Equal(HttpStatusCode.OK, navigation.StatusCode);

        using var validationResponse = await client.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/blueprints/validate",
            new { expectedConcurrencyToken = draft.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, validationResponse.StatusCode);
        var validation = await validationResponse.Content.ReadFromJsonAsync<ValidationRunResponse>();
        Assert.NotNull(validation);
        Assert.True(validation.Passed);
        using var validationDetails = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/blueprints/validations/{validation.Id}");
        Assert.Equal(HttpStatusCode.OK, validationDetails.StatusCode);

        using var approvalResponse = await client.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/approvals",
            new { expectedConcurrencyToken = draft.ConcurrencyToken, validationRunId = validation.Id });
        Assert.Equal(HttpStatusCode.Created, approvalResponse.StatusCode);
        var approval = await approvalResponse.Content.ReadFromJsonAsync<ApprovalResponse>();
        Assert.NotNull(approval);
        using var approvals = await client.GetAsync($"/api/projects/{_factory.Project.Id}/approvals");
        Assert.Equal(HttpStatusCode.OK, approvals.StatusCode);
        using var approvalDetails = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/approvals/{approval.RevisionId}");
        Assert.Equal(HttpStatusCode.OK, approvalDetails.StatusCode);
        using var approvedBlueprints = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/blueprints/approved");
        Assert.Equal(HttpStatusCode.OK, approvedBlueprints.StatusCode);
        using var approvedBlueprint = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/blueprints/approved/{approval.RevisionId}");
        Assert.Equal(HttpStatusCode.OK, approvedBlueprint.StatusCode);

        using var lockedDraftResponse = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/drafts/current");
        var lockedDraft = await lockedDraftResponse.Content.ReadFromJsonAsync<ProjectDraftResponse>();
        Assert.NotNull(lockedDraft);
        using var lockedWrite = await client.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/drafts/current",
            new { draft = new { forbiddenAfterApproval = true }, expectedConcurrencyToken = lockedDraft.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, lockedWrite.StatusCode);

        using var exportResponse = await client.PostAsync(
            $"/api/projects/{_factory.Project.Id}/exports/from-revision/{approval.RevisionId}",
            null);
        Assert.Equal(HttpStatusCode.Created, exportResponse.StatusCode);
        var export = await exportResponse.Content.ReadFromJsonAsync<ExportMetadataResponse>();
        Assert.NotNull(export);
        Assert.Equal(64, export.ArchiveSha256.Length);

        using var exportList = await client.GetAsync($"/api/projects/{_factory.Project.Id}/exports");
        Assert.Equal(HttpStatusCode.OK, exportList.StatusCode);
        using var exportDetails = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/exports/{export.ExportId}");
        Assert.Equal(HttpStatusCode.OK, exportDetails.StatusCode);

        using var download = await client.GetAsync(
            $"/api/projects/{_factory.Project.Id}/exports/{export.ExportId}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/zip", download.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await download.Content.ReadAsByteArrayAsync());

        using var nextDraft = await client.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/drafts/start-after-approval",
            new { expectedConcurrencyToken = lockedDraft.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, nextDraft.StatusCode);
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient(User user)
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.CreateAccessToken(user).Token);
        return client;
    }

    private static BlueprintDocument LoadBlueprint(Guid projectId)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "blueprint.valid.v1.json");
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        json["projectId"] = projectId.ToString();
        return BlueprintJsonContract.Parse(json.ToJsonString());
    }

    private sealed class WorkflowApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        public User Owner { get; } = NewUser("owner@example.test");
        public User OtherUser { get; } = NewUser("other@example.test");
        public Workspace Workspace { get; }
        public Project Project { get; }

        public WorkflowApiFactory()
        {
            Workspace = new Workspace { Name = "Owner workspace", OwnerUserId = Owner.Id };
            Project = new Project
            {
                Name = "Workflow",
                Description = "API tests",
                OwnerId = Owner.Id,
                WorkspaceId = Workspace.Id
            };
            _connection.Open();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            db.Database.EnsureCreated();
            db.Users.AddRange(Owner, OtherUser);
            db.Workspaces.Add(Workspace);
            db.Projects.Add(Project);
            db.SaveChanges();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:ReqvioraDatabase", "Host=localhost;Database=unused");
            builder.UseSetting("Jwt:Key", "workflow-test-signing-key-at-least-32-characters");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ReqvioraDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ReqvioraDbContext>>();
                services.RemoveAll<ReqvioraDbContext>();
                services.AddDbContext<ReqvioraDbContext>(options => options.UseSqlite(_connection));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                _connection.Dispose();
        }

        private static User NewUser(string email) => new()
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "not-used-in-tests",
            DisplayName = email
        };
    }
}
