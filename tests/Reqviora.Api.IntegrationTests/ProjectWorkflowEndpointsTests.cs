using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Reqviora.Api.Features.ProjectWorkflow;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.VoiceProcessing;
using Reqviora.Application.Features.DocumentIntelligence;
using Reqviora.Infrastructure.Services;
using Reqviora.Application.Features.Projects.DTOs;
using Reqviora.Application.Features.UIGeneration.Blueprint.Models;
using Reqviora.Application.Features.UIGeneration.Blueprint.Validation;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class ProjectWorkflowEndpointsTests : IDisposable
{
    private readonly WorkflowApiFactory _factory = new();

    [Fact]
    public async Task Openapi_and_liveness_documents_are_available_for_frontend_integration()
    {
        using var client = _factory.CreateClient();
        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        Assert.StartsWith("3.", document["openapi"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.NotNull(document["components"]!["securitySchemes"]!["Bearer"]);
        var paths = document["paths"]!.AsObject();
        Assert.Contains("/api/projects/{projectId}/builder", paths.Select(path => path.Key));
        Assert.Contains("/api/projects/{projectId}/builder/steps/theme-brand", paths.Select(path => path.Key));
        Assert.Contains("/api/projects/{projectId}/builder/steps/review-preview", paths.Select(path => path.Key));
        Assert.NotNull(paths["/api/projects/{projectId}/builder/steps/theme-brand"]!["put"]!["responses"]!["409"]);
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
    [InlineData("/api/projects/{0}/builder")]
    public async Task Sensitive_routes_require_authentication(string route)
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(string.Format(route, _factory.Project.Id));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
    }

    [Fact]
    public async Task Authorization_exception_is_problem_details_and_is_audited_with_final_status()
    {
        using var client = CreateClient(_factory.OtherUser);
        var path = $"/api/projects/{_factory.Project.Id}/drafts/current";

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.Single(values);
        Assert.Contains(_factory.AuditEvents(), audit =>
            audit.UserId == _factory.OtherUser.Id && audit.Path == path && audit.StatusCode == 403);
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
    [InlineData("/api/projects/{0}/builder")]
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
    public async Task Owner_can_update_an_accepted_members_viewer_and_editor_permissions()
    {
        _factory.AddAcceptedMember(_factory.OtherUser, "Viewer");
        using var owner = CreateClient(_factory.Owner);
        using var viewer = CreateClient(_factory.OtherUser);

        using var addViewer = await owner.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/members/{_factory.OtherUser.Id}",
            new { role = "viewer" });
        Assert.Equal(HttpStatusCode.OK, addViewer.StatusCode);

        using var project = await viewer.GetAsync($"/api/projects/{_factory.Project.Id}");
        Assert.Equal(HttpStatusCode.OK, project.StatusCode);
        using var overview = await viewer.GetAsync($"/api/projects/{_factory.Project.Id}/overview");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        using var viewerWrite = await viewer.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/drafts/current",
            new { draft = new { step = "viewer" }, expectedConcurrencyToken = (Guid?)null });
        Assert.Equal(HttpStatusCode.Forbidden, viewerWrite.StatusCode);
        using var viewerMetadataWrite = await viewer.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}",
            new { name = "Blocked", description = "Blocked" });
        Assert.Equal(HttpStatusCode.NotFound, viewerMetadataWrite.StatusCode);

        using var promote = await owner.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/members/{_factory.OtherUser.Id}",
            new { role = "EDITOR" });
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);
        using var editorWrite = await viewer.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/drafts/current",
            new { draft = new { step = "editor" }, expectedConcurrencyToken = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, editorWrite.StatusCode);

        using var remove = await owner.DeleteAsync(
            $"/api/projects/{_factory.Project.Id}/members/{_factory.OtherUser.Id}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        using var deniedAgain = await viewer.GetAsync($"/api/projects/{_factory.Project.Id}/overview");
        Assert.Equal(HttpStatusCode.Forbidden, deniedAgain.StatusCode);
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
    public async Task Pending_ai_and_disabled_mcp_are_not_available()
    {
        using var client = CreateClient(_factory.Owner);
        using var aiJob = await client.PostAsJsonAsync("/api/ai/jobs", new { prompt = "test" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, aiJob.StatusCode);

        using var extraction = await client.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/input-intelligence/requirements/extract",
            new { languageCode = "en", inputText = "test", evidence = Array.Empty<object>(), consentGranted = true });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, extraction.StatusCode);

        using var mcp = await client.GetAsync("/api/mcp/v1/catalog");
        Assert.Equal(HttpStatusCode.NotFound, mcp.StatusCode);
    }

    [Fact]
    public async Task Durable_ai_job_api_enforces_project_access_idempotency_and_cancellation()
    {
        using var factory = new WorkflowApiFactory(enableAiForTest: true);
        using var owner = CreateClient(factory.Owner, factory);
        using var missingKey = await owner.PostAsJsonAsync(
            "/api/ai/jobs", new { projectId = factory.Project.Id, prompt = "Generate" });
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);

        using var outsider = CreateClient(factory.OtherUser, factory);
        using var deniedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/ai/jobs")
        {
            Content = JsonContent.Create(new { projectId = factory.Project.Id, prompt = "Denied" })
        };
        deniedRequest.Headers.Add("Idempotency-Key", "outsider-1");
        using var denied = await outsider.SendAsync(deniedRequest);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/ai/jobs")
        {
            Content = JsonContent.Create(new { projectId = factory.Project.Id, prompt = "Generate" })
        };
        createRequest.Headers.Add("Idempotency-Key", "owner-1");
        using var created = await owner.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var first = await created.Content.ReadFromJsonAsync<AiJobCreatedPayload>();
        Assert.NotNull(first);
        Assert.True(first.Created);
        Assert.Equal("pending", first.State);

        using var replayRequest = new HttpRequestMessage(HttpMethod.Post, "/api/ai/jobs")
        {
            Content = JsonContent.Create(new { projectId = factory.Project.Id, prompt = "Changed retry body" })
        };
        replayRequest.Headers.Add("Idempotency-Key", "owner-1");
        using var replay = await owner.SendAsync(replayRequest);
        var second = await replay.Content.ReadFromJsonAsync<AiJobCreatedPayload>();
        Assert.Equal(first.Id, second!.Id);
        Assert.False(second.Created);

        using var get = await owner.GetAsync($"/api/ai/jobs/{first.Id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var persisted = await get.Content.ReadFromJsonAsync<AiJobStatus>();
        Assert.Equal(factory.Project.Id, persisted!.ProjectId);
        Assert.Equal("pending", persisted.State);

        using var cancel = await owner.PostAsync($"/api/ai/jobs/{first.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.Accepted, cancel.StatusCode);
        var cancelled = await cancel.Content.ReadFromJsonAsync<AiJobStatus>();
        Assert.Equal("cancelled", cancelled!.State);
        Assert.NotNull(cancelled.CancellationRequestedAt);
    }

    [Fact]
    public async Task Voice_transcription_requires_explicit_consent_before_provider_availability()
    {
        using var client = CreateClient(_factory.Owner);
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent([0x52, 0x49, 0x46, 0x46]), "audio", "voice.wav" },
            { new StringContent("en"), "languageCode" },
            { new StringContent("5"), "durationSeconds" },
            { new StringContent("false"), "consentGranted" }
        };
        form.First().Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

        using var response = await client.PostAsync(
            $"/api/projects/{_factory.Project.Id}/input-intelligence/voice/transcribe", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Voice_transcript_persists_supports_review_and_becomes_immutable()
    {
        using var factory = new WorkflowApiFactory(enableAiForTest: true);
        using var owner = CreateClient(factory.Owner, factory);
        using var outsider = CreateClient(factory.OtherUser, factory);
        using var form = VoiceForm(consent: true);
        var root = $"/api/projects/{factory.Project.Id}/input-intelligence/voice/transcripts";

        using var transcribed = await owner.PostAsync(
            $"/api/projects/{factory.Project.Id}/input-intelligence/voice/transcribe", form);
        Assert.Equal(HttpStatusCode.OK, transcribed.StatusCode);
        var draft = await transcribed.Content.ReadFromJsonAsync<TranscriptDraft>();
        Assert.NotNull(draft);
        Assert.Equal(1, draft.Revision);
        Assert.Equal(1, factory.Storage.Saves);

        using var denied = await outsider.GetAsync($"{root}/{draft.TranscriptId}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var fetched = await owner.GetAsync($"{root}/{draft.TranscriptId}");
        var initial = await fetched.Content.ReadFromJsonAsync<VoiceTranscriptPayload>();
        Assert.Equal("draft", initial!.Status);
        Assert.Single(initial.Segments);

        using var edited = await owner.PutAsJsonAsync($"{root}/{draft.TranscriptId}",
            new { expectedRevision = 1, editedText = "Edited Tamil and English transcript" });
        var edit = await edited.Content.ReadFromJsonAsync<VoiceTranscriptPayload>();
        Assert.Equal(2, edit!.Revision);
        Assert.Equal("Edited Tamil and English transcript", edit.EditableText);
        using var stale = await owner.PutAsJsonAsync($"{root}/{draft.TranscriptId}",
            new { expectedRevision = 1, editedText = "Stale edit" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var reviewed = await owner.PostAsJsonAsync($"{root}/{draft.TranscriptId}/review",
            new { expectedRevision = 2, acknowledgedByUser = true });
        var review = await reviewed.Content.ReadFromJsonAsync<VoiceTranscriptPayload>();
        Assert.Equal("reviewed", review!.Status);
        Assert.Equal(3, review.Revision);
        Assert.NotNull(review.ReviewedAt);
        using var immutable = await owner.PutAsJsonAsync($"{root}/{draft.TranscriptId}",
            new { expectedRevision = 3, editedText = "Must not change" });
        Assert.Equal(HttpStatusCode.Conflict, immutable.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
        Assert.Equal("reviewed", (await db.VoiceTranscripts.SingleAsync()).Status);
        Assert.Single(await db.VoiceTranscriptSegments.ToArrayAsync());
    }

    [Fact]
    public async Task Failed_voice_provider_removes_uncommitted_private_audio()
    {
        using var factory = new WorkflowApiFactory(enableAiForTest: true);
        factory.Voice.FailNext = true;
        using var owner = CreateClient(factory.Owner, factory);
        using var form = VoiceForm(consent: true);

        using var response = await owner.PostAsync(
            $"/api/projects/{factory.Project.Id}/input-intelligence/voice/transcribe", form);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, factory.Storage.Deletes);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>().VoiceTranscripts);
    }

    [Fact]
    public async Task Mismatched_voice_provider_source_is_rejected_and_audio_is_removed()
    {
        using var factory = new WorkflowApiFactory(enableAiForTest: true);
        factory.Voice.MismatchSourceNext = true;
        using var owner = CreateClient(factory.Owner, factory);
        using var form = VoiceForm(consent: true);

        using var response = await owner.PostAsync(
            $"/api/projects/{factory.Project.Id}/input-intelligence/voice/transcribe", form);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, factory.Storage.Deletes);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>().VoiceTranscripts);
    }

    [Fact]
    public async Task Text_extraction_requires_explicit_consent_and_project_access()
    {
        using var owner = CreateClient(_factory.Owner);
        using var missingConsent = await owner.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/input-intelligence/requirements/extract",
            new { languageCode = "en", inputText = "Users need sign in", evidence = Array.Empty<object>(), consentGranted = false });
        Assert.Equal(HttpStatusCode.BadRequest, missingConsent.StatusCode);

        using var other = CreateClient(_factory.OtherUser);
        using var foreignProject = await other.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/input-intelligence/requirements/extract",
            new { languageCode = "en", inputText = "Users need sign in", evidence = Array.Empty<object>(), consentGranted = true });
        Assert.Equal(HttpStatusCode.Forbidden, foreignProject.StatusCode);
    }

    [Fact]
    public async Task Extraction_persists_suggested_candidates_and_lists_only_for_project_members()
    {
        using var factory = new WorkflowApiFactory(enableAiForTest: true);
        using var owner = CreateClient(factory.Owner, factory);
        var path = $"/api/projects/{factory.Project.Id}";
        var input = new
        {
            languageCode = "en", inputText = "Users need sign in", consentGranted = true,
            evidence = new[] { new { sourceId = "SRC-INPUT", locator = "line 1", pageNumber = (int?)null,
                startOffset = 0, endOffset = 18, quote = "Users need sign in" } }
        };
        using var response = await owner.PostAsJsonAsync(path + "/input-intelligence/requirements/extract", input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RequirementExtractionPayload>();
        var candidate = Assert.Single(payload!.Candidates);
        Assert.NotEqual("REQ-LOGIN", candidate.Id);
        using var list = await owner.GetAsync(path + "/requirements/candidates");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var entries = await list.Content.ReadFromJsonAsync<ReviewedRequirement[]>();
        var saved = Assert.Single(entries!);
        Assert.Equal(candidate.Id, saved.Id);
        Assert.Equal(RequirementReviewStatus.Suggested, saved.Status);
        Assert.Equal(1, saved.Revision);
        Assert.Null(saved.ConfirmedBy);
        using var outsider = CreateClient(factory.OtherUser, factory);
        using var denied = await outsider.GetAsync(path + "/requirements/candidates");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var invalidPage = await owner.GetAsync(path + "/requirements/candidates?limit=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
        using var second = await owner.PostAsJsonAsync(path + "/input-intelligence/requirements/extract", input);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondPayload = await second.Content.ReadFromJsonAsync<RequirementExtractionPayload>();
        Assert.NotEqual(candidate.Id, Assert.Single(secondPayload!.Candidates).Id);
        Assert.Equal(2, factory.CandidateStore.Items.Count);
    }

    [Fact]
    public async Task Manual_candidate_clarification_and_review_share_project_scoped_records()
    {
        using var owner = CreateClient(_factory.Owner);
        using var outsider = CreateClient(_factory.OtherUser);
        var path = $"/api/projects/{_factory.Project.Id}/requirements/candidates";
        var manual = new { title = "Login", description = "Users need sign in" };
        using var denied = await outsider.PostAsJsonAsync(path + "/manual", manual);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var invalid = await owner.PostAsJsonAsync(path + "/manual", new { title = " ", description = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var created = await owner.PostAsJsonAsync(path + "/manual", manual);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var candidate = (await created.Content.ReadFromJsonAsync<ReviewedRequirement>())!;
        Assert.Equal(RequirementReviewStatus.Suggested, candidate.Status);
        Assert.Null(candidate.ConfirmedBy);
        var detail = path + "/" + candidate.Id;
        using var get = await owner.GetAsync(detail);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var forbidden = await outsider.GetAsync(detail);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var clarifications = detail + "/clarifications";
        using var question = await owner.PostAsJsonAsync(clarifications, new { question = "Which login method?" });
        Assert.Equal(HttpStatusCode.Created, question.StatusCode);
        var saved = (await question.Content.ReadFromJsonAsync<ClarificationQuestion>())!;
        var answer = clarifications + "/" + saved.Id + "/answer";
        using var deniedAnswer = await outsider.PostAsJsonAsync(answer, new { expectedRevision = 1, answer = "Email" });
        Assert.Equal(HttpStatusCode.Forbidden, deniedAnswer.StatusCode);
        using var wrongRevision = await owner.PostAsJsonAsync(answer, new { expectedRevision = 2, answer = "Email" });
        Assert.Equal(HttpStatusCode.Conflict, wrongRevision.StatusCode);
        using var resolved = await owner.PostAsJsonAsync(answer, new { expectedRevision = 1, answer = "Email" });
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        Assert.True((await resolved.Content.ReadFromJsonAsync<ClarificationQuestion>())!.IsResolved);
        using var repeated = await owner.PostAsJsonAsync(answer, new { expectedRevision = 1, answer = "Other" });
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        using var list = await owner.GetAsync(clarifications);
        Assert.Equal("Email", Assert.Single((await list.Content.ReadFromJsonAsync<ClarificationQuestion[]>())!).Answer);
        using var beforeReview = await owner.GetAsync(detail);
        Assert.Equal(RequirementReviewStatus.Suggested, (await beforeReview.Content.ReadFromJsonAsync<ReviewedRequirement>())!.Status);
        using var review = await owner.PostAsJsonAsync($"/api/projects/{_factory.Project.Id}/requirements/{candidate.Id}/review",
            new { expectedRevision = 1, decision = "accept" });
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        using var afterReview = await owner.GetAsync(detail);
        var confirmed = (await afterReview.Content.ReadFromJsonAsync<ReviewedRequirement>())!;
        Assert.Equal(RequirementReviewStatus.Confirmed, confirmed.Status);
        Assert.Equal(_factory.Owner.Id, confirmed.ConfirmedBy);
    }

    [Fact]
    public async Task Flutter_project_contract_round_trips_all_nine_fields()
    {
        using var client = CreateClient(_factory.Owner);
        using var created = await client.PostAsJsonAsync("/api/projects", new
        {
            name = "Mobile Banking",
            description = "Flutter client",
            industry = "Finance",
            targetAudience = "Retail customers",
            projectType = "Mobile app",
            primaryPlatform = "MOBILE",
            technologyPreferences = "Flutter, .NET",
            constraints = "WCAG AA",
            integrations = "Open Banking"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var project = await created.Content.ReadFromJsonAsync<ProjectResponseDto>();
        Assert.NotNull(project);
        Assert.Equal("mobile", project.PrimaryPlatform);

        using var updated = await client.PutAsJsonAsync($"/api/projects/{project.Id}", new
        {
            name = "Mobile Banking V2",
            description = "Flutter integration verified",
            industry = "Fintech",
            targetAudience = "SME customers",
            projectType = "Cross-platform app",
            primaryPlatform = "WEB",
            technologyPreferences = "Flutter Web, .NET",
            constraints = "Offline-first",
            integrations = "Payments"
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await updated.Content.ReadFromJsonAsync<ProjectResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(new[]
        {
            "Mobile Banking V2", "Flutter integration verified", "Fintech", "SME customers",
            "Cross-platform app", "web", "Flutter Web, .NET", "Offline-first", "Payments"
        }, new[]
        {
            body.Name, body.Description, body.Industry, body.TargetAudience, body.ProjectType,
            body.PrimaryPlatform, body.TechnologyPreferences, body.Constraints, body.Integrations
        });
    }

    [Fact]
    public async Task Document_upload_is_owner_scoped_validated_and_does_not_expose_storage_path()
    {
        using var owner = CreateClient(_factory.Owner);
        using var validForm = DocumentForm("requirements.txt", "text/plain");
        using var uploaded = await owner.PostAsync(
            $"/api/projects/{_factory.Project.Id}/documents",
            validForm);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var responseBody = await uploaded.Content.ReadAsStringAsync();
        Assert.DoesNotContain("storagePath", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, _factory.Storage.Saves);
        var documentId = JsonNode.Parse(responseBody)!["id"]!.GetValue<Guid>();
        using var parsed = await owner.PostAsync($"/api/projects/{_factory.Project.Id}/documents/{documentId}/parse", null);
        Assert.Equal(HttpStatusCode.OK, parsed.StatusCode);
        Assert.Contains("REQVIORA requirements", await parsed.Content.ReadAsStringAsync());
        using var parsedAgain = await owner.PostAsync(
            $"/api/projects/{_factory.Project.Id}/documents/{documentId}/parse", null);
        Assert.Equal(HttpStatusCode.OK, parsedAgain.StatusCode);
        Assert.Contains("REQVIORA requirements", await parsedAgain.Content.ReadAsStringAsync());
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            var persisted = await db.ProjectDocuments.AsNoTracking().SingleAsync(item => item.Id == documentId);
            Assert.Equal("completed", persisted.ParsingStatus);
            Assert.Equal("text/plain", persisted.MediaType);
            Assert.NotNull(persisted.Sha256);
            Assert.NotNull(persisted.ParsedAt);
            var segment = await db.ProjectDocumentSegments.AsNoTracking()
                .SingleAsync(item => item.ProjectDocumentId == documentId);
            Assert.Equal("REQVIORA requirements", segment.Text);
            Assert.Equal(documentId.ToString("D"), segment.SourceId);
        }
        using var listed = await owner.GetAsync($"/api/projects/{_factory.Project.Id}/documents/{documentId}");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var listedBody = await listed.Content.ReadAsStringAsync();
        Assert.DoesNotContain("storagePath", listedBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("completed", listedBody);
        Assert.DoesNotContain("sha256", listedBody, StringComparison.OrdinalIgnoreCase);
        using var downloaded = await owner.GetAsync(
            $"/api/projects/{_factory.Project.Id}/documents/{documentId}/download");
        Assert.Equal(HttpStatusCode.OK, downloaded.StatusCode);
        Assert.Equal("REQVIORA requirements", await downloaded.Content.ReadAsStringAsync());

        using var duplicateForm = DocumentForm("requirements-copy.txt", "text/plain");
        using var duplicate = await owner.PostAsync(
            $"/api/projects/{_factory.Project.Id}/documents", duplicateForm);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(1, _factory.Storage.Saves);
        using var outsider = CreateClient(_factory.OtherUser);
        using var forbiddenParse = await outsider.PostAsync($"/api/projects/{_factory.Project.Id}/documents/{documentId}/parse", null);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenParse.StatusCode);
        using var forbiddenDownload = await outsider.GetAsync(
            $"/api/projects/{_factory.Project.Id}/documents/{documentId}/download");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenDownload.StatusCode);

        using var mismatchedForm = DocumentForm("requirements.exe", "text/plain");
        using var mismatched = await owner.PostAsync(
            $"/api/projects/{_factory.Project.Id}/documents",
            mismatchedForm);
        Assert.Equal(HttpStatusCode.BadRequest, mismatched.StatusCode);
        Assert.Equal(1, _factory.Storage.Saves);

        _factory.Storage.RejectNext = true;
        using var infectedForm = DocumentForm("infected.txt", "text/plain", "simulated infected content");
        using var infected = await owner.PostAsync(
            $"/api/projects/{_factory.Project.Id}/documents",
            infectedForm);
        Assert.Equal(HttpStatusCode.BadRequest, infected.StatusCode);
        Assert.Equal(1, _factory.Storage.Saves);

        using var other = CreateClient(_factory.OtherUser);
        using var foreignForm = DocumentForm("requirements.txt", "text/plain");
        using var foreign = await other.PostAsync(
            $"/api/projects/{_factory.Project.Id}/documents",
            foreignForm);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(1, _factory.Storage.Saves);
    }

    [Fact]
    public async Task Scanned_pdf_is_ocr_processed_and_persisted_with_source_evidence()
    {
        using var factory = new WorkflowApiFactory(enableOcrForTest: true);
        using var owner = CreateClient(factory.Owner, factory);
        using var form = DocumentForm("scanned.pdf", "application/pdf", "%PDF-scanned-test");
        using var uploaded = await owner.PostAsync($"/api/projects/{factory.Project.Id}/documents", form);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var documentId = JsonNode.Parse(await uploaded.Content.ReadAsStringAsync())!["id"]!.GetValue<Guid>();

        using var parsed = await owner.PostAsync(
            $"/api/projects/{factory.Project.Id}/documents/{documentId}/parse", null);

        Assert.Equal(HttpStatusCode.OK, parsed.StatusCode);
        Assert.Contains("OCR recovered requirement", await parsed.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
        var document = await db.ProjectDocuments.AsNoTracking().SingleAsync(item => item.Id == documentId);
        var segment = await db.ProjectDocumentSegments.AsNoTracking()
            .SingleAsync(item => item.ProjectDocumentId == documentId);
        Assert.Equal("completed", document.ParsingStatus);
        Assert.Equal(documentId.ToString("D"), segment.SourceId);
        Assert.Equal("page:1:ocr:offset:0", segment.Locator);
        Assert.Equal(1, factory.Ocr.Calls);
    }

    [Fact]
    public async Task Ocr_outage_preserves_retryable_requires_ocr_state()
    {
        using var factory = new WorkflowApiFactory(enableOcrForTest: true);
        factory.Ocr.Fail = true;
        using var owner = CreateClient(factory.Owner, factory);
        using var form = DocumentForm("scanned.pdf", "application/pdf", "%PDF-scanned-test");
        using var uploaded = await owner.PostAsync($"/api/projects/{factory.Project.Id}/documents", form);
        var documentId = JsonNode.Parse(await uploaded.Content.ReadAsStringAsync())!["id"]!.GetValue<Guid>();

        using var parsed = await owner.PostAsync(
            $"/api/projects/{factory.Project.Id}/documents/{documentId}/parse", null);

        Assert.Equal(HttpStatusCode.OK, parsed.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
        var document = await db.ProjectDocuments.AsNoTracking().SingleAsync(item => item.Id == documentId);
        Assert.Equal("requires_ocr", document.ParsingStatus);
        Assert.Contains("OCR unavailable", document.ParsingWarningsJson);
        Assert.False(await db.ProjectDocumentSegments.AnyAsync(item => item.ProjectDocumentId == documentId));

        factory.Ocr.Fail = false;
        using var retried = await owner.PostAsync(
            $"/api/projects/{factory.Project.Id}/documents/{documentId}/parse", null);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        db.ChangeTracker.Clear();
        document = await db.ProjectDocuments.AsNoTracking().SingleAsync(item => item.Id == documentId);
        Assert.Equal("completed", document.ParsingStatus);
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
        var second = await updated.Content.ReadFromJsonAsync<ProjectDraftResponse>();
        Assert.NotNull(second);

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

        using var restored = await client.PostAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/drafts/versions/1/restore",
            new { expectedConcurrencyToken = second.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        var restoredDraft = await restored.Content.ReadFromJsonAsync<ProjectDraftResponse>();
        Assert.Equal(3, restoredDraft?.Version);
        Assert.Equal("First", restoredDraft?.Draft.GetProperty("title").GetString());
        using var restoredHistory = await client.GetAsync($"/api/projects/{_factory.Project.Id}/drafts/versions");
        Assert.Equal(3, (await restoredHistory.Content.ReadFromJsonAsync<BlueprintVersionResponse[]>())?.Length);
    }

    [Fact]
    public async Task Guided_builder_persists_validates_and_versions_all_eight_typed_steps()
    {
        using var factory = new WorkflowApiFactory();
        using var client = CreateClient(factory.Owner, factory);
        Guid? token = null;

        token = await SaveBuilderStep(client, factory.Project.Id, "identity-audience", new
        {
            projectName = "Reqviora", tagline = "Design with evidence", description = "Guided product design",
            category = "SaaS", industry = "Software", problemStatement = "Teams lose traceability",
            goals = new[] { "Preserve traceability" }, targetAudience = new[] { "Product teams" },
            personas = new[] { new { id = "PER-1", name = "Designer", description = "Creates flows", goals = new[] { "Ship safely" } } },
            roles = new[] { new { id = "ROLE-1", name = "Designer", description = "Edits designs" } },
            platforms = new[] { "web" }, scope = "MVP", constraints = new[] { "WCAG AA" },
            assumptions = new[] { "Authenticated users" }, successCriteria = new[] { "All requirements covered" },
            sourceReferences = new[] { new { sourceId = "SRC-1", locator = "page:1", quote = "Traceable design" } }
        }, token);
        token = await SaveBuilderStep(client, factory.Project.Id, "features-screens", new
        {
            requirements = new[] { new { id = "REQ-1", text = "Users create designs", priority = "must", confirmed = true } },
            features = new[] { new { id = "FEAT-1", name = "Designer", description = "Design workspace", requirementIds = new[] { "REQ-1" } } },
            screens = new[] { new { id = "SCR-1", name = "Workspace", purpose = "Edit designs", featureIds = new[] { "FEAT-1" }, allowedRoleIds = new[] { "ROLE-1" } } }
        }, token);
        token = await SaveBuilderStep(client, factory.Project.Id, "navigation", new
        {
            initialScreenId = "SCR-1", routes = Array.Empty<object>()
        }, token);
        token = await SaveBuilderStep(client, factory.Project.Id, "theme-brand", new
        {
            palette = new Dictionary<string, string> { ["primary"] = "#112233" },
            semanticColors = new Dictionary<string, string> { ["brand"] = "primary" },
            typography = new[] { new { token = "body", fontFamily = "Inter", size = 16, weight = 400, lineHeight = 1.5 } },
            spacing = new Dictionary<string, double> { ["sm"] = 8 },
            borders = Array.Empty<object>(), elevation = Array.Empty<object>(),
            breakpoints = new[] { new { name = "mobile", minWidth = 0 } },
            brandAssets = Array.Empty<object>(),
            accessibility = new { minimumContrast = "AA", reducedMotion = true, keyboardNavigation = true, screenReaderLabels = true }
        }, token);
        token = await SaveBuilderStep(client, factory.Project.Id, "components-layout", new
        {
            registry = new[] { new { type = "Text", variants = new[] { "default" }, supportedProperties = new[] { "text" }, allowsChildren = false } },
            components = new[] { new
            {
                id = "CMP-1", screenId = "SCR-1", type = "Text", variant = "default", parentId = (string?)null,
                childIds = Array.Empty<string>(), properties = new Dictionary<string, string> { ["text"] = "Welcome" },
                layout = new { mode = "stack", columns = 1, gapToken = "sm", breakpoint = (string?)null },
                responsiveLayouts = new Dictionary<string, object> { ["mobile"] = new { mode = "stack", columns = 1, gapToken = "sm", breakpoint = "mobile" } },
                @protected = false, editableProperties = new[] { "text" }
            } }
        }, token);
        token = await SaveBuilderStep(client, factory.Project.Id, "interactions-states", new
        {
            formFields = Array.Empty<object>(), actions = Array.Empty<object>(),
            states = new[] { new { id = "STATE-1", screenId = "SCR-1", kind = "default", description = "Ready", visibleComponentIds = new[] { "CMP-1" } } },
            transitions = Array.Empty<object>()
        }, token);
        token = await SaveBuilderStep(client, factory.Project.Id, "ai-design-workspace", new
        {
            blueprintSchemaVersion = "1.0", protectedComponentIds = Array.Empty<string>(),
            editableProperties = new Dictionary<string, IReadOnlyList<string>> { ["CMP-1"] = new[] { "text" } },
            alternatives = Array.Empty<object>(), autosaveEnabled = true, autosaveSeconds = 30,
            undoVersionIds = Array.Empty<string>(), redoVersionIds = Array.Empty<string>(), requirementTraceIds = new[] { "REQ-1" }
        }, token);
        var tokenBeforeReview = token;
        token = await SaveBuilderStep(client, factory.Project.Id, "review-preview", new
        {
            screens = new[] { new { screenId = "SCR-1", title = "Workspace", componentIds = new[] { "CMP-1" }, stateIds = new[] { "STATE-1" } } },
            navigation = Array.Empty<object>(), decisions = Array.Empty<object>(),
            requirementCount = 1, coveredRequirementCount = 1, readyForApproval = true
        }, token);

        using var validation = await client.PostAsync($"/api/projects/{factory.Project.Id}/builder/validate", null);
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);
        Assert.True((await validation.Content.ReadFromJsonAsync<JsonObject>())!["passed"]!.GetValue<bool>());
        using var resumed = await client.GetAsync($"/api/projects/{factory.Project.Id}/builder");
        var body = JsonNode.Parse(await resumed.Content.ReadAsStringAsync())!;
        Assert.Equal("Reqviora", body["builder"]!["identityAudience"]!["projectName"]!.GetValue<string>());
        Assert.Equal(8, body["draftVersion"]!.GetValue<int>());

        using var stale = await client.PutAsJsonAsync($"/api/projects/{factory.Project.Id}/builder/steps/review-preview", new
        {
            data = body["builder"]!["reviewPreview"], expectedConcurrencyToken = tokenBeforeReview
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.NotEqual(tokenBeforeReview, token);
    }

    [Fact]
    public async Task Guided_builder_rejects_broken_cross_step_references_without_saving()
    {
        using var factory = new WorkflowApiFactory();
        using var client = CreateClient(factory.Owner, factory);
        var token = await SaveBuilderStep(client, factory.Project.Id, "identity-audience", new
        {
            projectName = "Reqviora", tagline = "", description = "Builder", category = "SaaS", industry = "Software",
            problemStatement = "Traceability", goals = new[] { "Validate" }, targetAudience = new[] { "Teams" },
            personas = Array.Empty<object>(), roles = new[] { new { id = "ROLE-1", name = "Owner", description = "Owner" } },
            platforms = new[] { "web" }, scope = "MVP", constraints = Array.Empty<string>(), assumptions = Array.Empty<string>(),
            successCriteria = new[] { "Valid" }, sourceReferences = Array.Empty<object>()
        }, null);
        using var invalid = await client.PutAsJsonAsync(
            $"/api/projects/{factory.Project.Id}/builder/steps/features-screens", new
            {
                data = new
                {
                    requirements = new[] { new { id = "REQ-1", text = "Requirement", priority = "must", confirmed = true } },
                    features = new[] { new { id = "FEAT-1", name = "Feature", description = "Feature", requirementIds = new[] { "REQ-MISSING" } } },
                    screens = new[] { new { id = "SCR-1", name = "Screen", purpose = "Screen", featureIds = new[] { "FEAT-MISSING" }, allowedRoleIds = new[] { "ROLE-MISSING" } } }
                },
                expectedConcurrencyToken = token
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var resumed = await client.GetAsync($"/api/projects/{factory.Project.Id}/builder");
        var body = JsonNode.Parse(await resumed.Content.ReadAsStringAsync())!;
        Assert.Null(body["builder"]!["featuresScreens"]);
        Assert.Equal(1, body["draftVersion"]!.GetValue<int>());
    }

    [Fact]
    public async Task Generic_draft_endpoint_cannot_bypass_guided_builder_validation()
    {
        using var client = CreateClient(_factory.Owner);
        using var response = await client.PutAsJsonAsync(
            $"/api/projects/{_factory.Project.Id}/drafts/current", new
            {
                draft = new
                {
                    guidedBuilder = new
                    {
                        schemaVersion = "9.9",
                        themeBrand = new { palette = new Dictionary<string, string> { ["bad"] = "red" } }
                    }
                },
                expectedConcurrencyToken = (Guid?)null
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Initial_blueprint_generation_uses_confirmed_review_records_and_preserves_draft()
    {
        using var owner = CreateClient(_factory.Owner);
        using var outsider = CreateClient(_factory.OtherUser);
        using var anonymous = _factory.CreateClient();
        var path = $"/api/projects/{_factory.Project.Id}";
        var generate = path + "/blueprints/generate";
        var ids = new List<string>();
        foreach (var title in new[] { "Login", "Dashboard" })
        {
            using var created = await owner.PostAsJsonAsync(path + "/requirements/candidates/manual",
                new { title, description = "Requirement for " + title });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            ids.Add((await created.Content.ReadFromJsonAsync<ReviewedRequirement>())!.Id);
        }
        using var noAuth = await anonymous.PostAsJsonAsync(generate, new { requirementIds = ids });
        Assert.Equal(HttpStatusCode.Unauthorized, noAuth.StatusCode);
        using var denied = await outsider.PostAsJsonAsync(generate, new { requirementIds = ids });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var missing = await owner.PostAsJsonAsync(generate, new { requirementIds = new[] { "REQ-MISSING" } });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var duplicate = await owner.PostAsJsonAsync(generate, new { requirementIds = new[] { ids[0], ids[0] } });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var unconfirmed = await owner.PostAsJsonAsync(generate, new { requirementIds = ids });
        Assert.Equal(HttpStatusCode.Conflict, unconfirmed.StatusCode);
        using var absent = await owner.GetAsync(path + "/blueprints/current");
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);

        foreach (var id in ids)
        {
            using var review = await owner.PostAsJsonAsync(path + $"/requirements/{id}/review",
                new { expectedRevision = 1, decision = "accept" });
            Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        }
        using var first = await owner.PutAsJsonAsync(path + "/drafts/current",
            new { draft = new { title = "Preserve my builder input" } });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var initial = (await first.Content.ReadFromJsonAsync<ProjectDraftResponse>())!;
        using var stale = await owner.PostAsJsonAsync(generate, new { requirementIds = ids });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var generated = await owner.PostAsJsonAsync(generate,
            new { requirementIds = ids, expectedConcurrencyToken = initial.ConcurrencyToken });
        Assert.True(generated.StatusCode == HttpStatusCode.OK, await generated.Content.ReadAsStringAsync());
        var saved = (await generated.Content.ReadFromJsonAsync<ProjectDraftResponse>())!;
        Assert.Equal("Preserve my builder input", saved.Draft.GetProperty("title").GetString());
        Assert.Equal(initial.Version + 1, saved.Version);
        Assert.NotEqual(initial.ConcurrencyToken, saved.ConcurrencyToken);
        Assert.Null(saved.ApprovedRevisionId);
        using var current = await owner.GetAsync(path + "/blueprints/current");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        var blueprint = (await current.Content.ReadFromJsonAsync<BlueprintVersionResponse>())!.Blueprint!;
        Assert.Equal("draft", blueprint.Lifecycle);
        Assert.Equal(ids.ToArray(), blueprint.Requirements.Select(x => x.Id).ToArray());
        Assert.All(blueprint.Requirements, x => Assert.Equal(_factory.Owner.Id.ToString(), x.ConfirmedBy));
        Assert.Equal(2, blueprint.Features.Count);
        Assert.Equal(2, blueprint.Screens.Count);
        Assert.Equal(5, blueprint.Components.Count);
        Assert.Single(blueprint.Navigation);
        Assert.True(BlueprintContractValidator.Validate(blueprint).IsValid);
        using var validate = await owner.PostAsJsonAsync(path + "/blueprints/validate",
            new { expectedConcurrencyToken = saved.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.OK, validate.StatusCode);
        Assert.True((await validate.Content.ReadFromJsonAsync<ValidationRunResponse>())!.Passed);
        using var repeat = await owner.PostAsJsonAsync(generate,
            new { requirementIds = ids, expectedConcurrencyToken = saved.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        using var regenerated = await owner.PostAsJsonAsync(path + "/blueprints/regenerate",
            new { expectedConcurrencyToken = saved.ConcurrencyToken, expectedRevision = blueprint.Revision });
        Assert.True(regenerated.StatusCode == HttpStatusCode.OK, await regenerated.Content.ReadAsStringAsync());
        var newDraft = (await regenerated.Content.ReadFromJsonAsync<ProjectDraftResponse>())!;
        Assert.Equal(saved.Version + 1, newDraft.Version);
        Assert.Equal("Preserve my builder input", newDraft.Draft.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Regeneration_preserves_protected_items_and_persists_only_valid_new_revisions()
    {
        using var owner = CreateClient(_factory.Owner);
        using var outsider = CreateClient(_factory.OtherUser);
        var path = $"/api/projects/{_factory.Project.Id}";
        var regenerate = path + "/blueprints/regenerate";
        using var created = await owner.PostAsJsonAsync(path + "/requirements/candidates/manual",
            new { title = "Dashboard", description = "Show project status" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<ReviewedRequirement>())!.Id;
        using var accepted = await owner.PostAsJsonAsync(path + $"/requirements/{id}/review",
            new { expectedRevision = 1, decision = "accept" });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var generated = await owner.PostAsJsonAsync(path + "/blueprints/generate",
            new { requirementIds = new[] { id } });
        Assert.True(generated.StatusCode == HttpStatusCode.OK, await generated.Content.ReadAsStringAsync());
        var saved = (await generated.Content.ReadFromJsonAsync<ProjectDraftResponse>())!;
        var original = (await owner.GetFromJsonAsync<BlueprintVersionResponse>(path + "/blueprints/current"))!.Blueprint!;
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        var proposal = System.Text.Json.JsonSerializer.SerializeToNode(original, options)!;
        proposal["revision"] = original.Revision + 1;
        var componentId = original.Components[0].Id;
        proposal["components"]![0]!["props"]!["text"] = "Custom title";
        proposal["components"]![0]!["props"]!["locked"] = true;
        object Request(Guid token, string[]? editable = null) => new
        {
            expectedConcurrencyToken = token, expectedRevision = original.Revision,
            editableComponentIds = editable, proposal
        };
        using var forbidden = await outsider.PostAsJsonAsync(regenerate, Request(saved.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var stale = await owner.PostAsJsonAsync(regenerate, Request(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var unknown = await owner.PostAsJsonAsync(regenerate, Request(saved.ConcurrencyToken, ["CMP-MISSING"]));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        using var protectedChange = await owner.PostAsJsonAsync(regenerate, Request(saved.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Conflict, protectedChange.StatusCode);
        var unchanged = (await owner.GetFromJsonAsync<BlueprintVersionResponse>(path + "/blueprints/current"))!;
        Assert.Equal(saved.Version, unchanged.Version);
        using var applied = await owner.PostAsJsonAsync(regenerate, Request(saved.ConcurrencyToken, [componentId]));
        Assert.True(applied.StatusCode == HttpStatusCode.OK, await applied.Content.ReadAsStringAsync());
        var updated = (await applied.Content.ReadFromJsonAsync<ProjectDraftResponse>())!;
        Assert.Equal(saved.Version + 1, updated.Version);
        Assert.NotEqual(saved.ConcurrencyToken, updated.ConcurrencyToken);
        var current = (await owner.GetFromJsonAsync<BlueprintVersionResponse>(path + "/blueprints/current"))!.Blueprint!;
        Assert.Equal(original.Revision + 1, current.Revision);
        Assert.Equal("Custom title", current.Components[0].Props["text"].GetString());
        Assert.Equal(original.BlueprintId, current.BlueprintId);
        var history = (await owner.GetFromJsonAsync<BlueprintVersionResponse[]>(path + "/blueprints/versions"))!;
        Assert.Equal(2, history.Length);
        Assert.Equal(original.Components[0].Props["text"].GetString(), history[1].Blueprint!.Components[0].Props["text"].GetString());

        // Locked items remain protected even if explicitly selected as editable.
        proposal["revision"] = current.Revision + 1;
        proposal["components"]![0]!["props"]!["text"] = "Overwrite locked title";
        using var locked = await owner.PostAsJsonAsync(regenerate, new
        {
            expectedConcurrencyToken = updated.ConcurrencyToken, expectedRevision = current.Revision,
            editableComponentIds = new[] { componentId }, proposal
        });
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        // The deterministic fallback must also reject overwriting locked edits.
        using var fallback = await owner.PostAsJsonAsync(regenerate, new
        {
            expectedConcurrencyToken = updated.ConcurrencyToken, expectedRevision = current.Revision,
            editableComponentIds = new[] { componentId }
        });
        Assert.Equal(HttpStatusCode.Conflict, fallback.StatusCode);
        Assert.Equal(updated.Version,
            (await owner.GetFromJsonAsync<BlueprintVersionResponse>(path + "/blueprints/current"))!.Version);
    }

    [Fact]
    public async Task Blueprint_graph_approval_and_verified_export_are_available_to_owner()
    {
        using var client = CreateClient(_factory.Owner);
        var blueprint = LoadBlueprint(_factory.Project.Id, _factory.Owner.Id);
        SeedFixtureReview(blueprint, _factory.Project.Id, _factory.Owner.Id);

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

    [Fact]
    public async Task Blueprint_confirmation_must_match_project_review_at_save_and_approval()
    {
        using var client = CreateClient(_factory.Owner);
        var projectId = _factory.Project.Id;
        var path = $"/api/projects/{projectId}";
        var blueprint = LoadBlueprint(projectId, _factory.Owner.Id);
        var saveBody = new { draft = new { }, blueprint, expectedConcurrencyToken = (Guid?)null };
        using var missing = await client.PutAsJsonAsync(path + "/blueprints/current", saveBody);
        Assert.Equal(HttpStatusCode.Conflict, missing.StatusCode);
        SeedFixtureReview(blueprint, Guid.NewGuid(), _factory.Owner.Id);
        using var foreign = await client.PutAsJsonAsync(path + "/blueprints/current", saveBody);
        Assert.Equal(HttpStatusCode.Conflict, foreign.StatusCode);
        SeedFixtureReview(blueprint, projectId, _factory.OtherUser.Id);
        using var forgedActor = await client.PutAsJsonAsync(path + "/blueprints/current", saveBody);
        Assert.Equal(HttpStatusCode.Conflict, forgedActor.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path + "/drafts/current")).StatusCode);
        _factory.CandidateStore.Items.RemoveAll(x => x.ProjectId == projectId);
        SeedFixtureReview(blueprint, projectId, _factory.Owner.Id);
        var index = _factory.CandidateStore.Items.FindIndex(x => x.ProjectId == projectId);
        var validRecord = _factory.CandidateStore.Items[index].Requirement;
        foreach (var altered in new[]
        {
            validRecord with { Description = "Different confirmed content" },
            validRecord with { ConfirmedAt = validRecord.ConfirmedAt!.Value.AddMinutes(1) },
            validRecord with { Evidence = [new CandidateEvidence("OTHER", "section 1", "Different evidence")] },
            validRecord with { Status = RequirementReviewStatus.Suggested, ConfirmedBy = null, ConfirmedAt = null }
        })
        {
            _factory.CandidateStore.Items[index] = (projectId, altered);
            using var mismatch = await client.PutAsJsonAsync(path + "/blueprints/current", saveBody);
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        }
        _factory.CandidateStore.Items[index] = (projectId, validRecord);
        using var saved = await client.PutAsJsonAsync(path + "/blueprints/current", saveBody);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var draft = (await saved.Content.ReadFromJsonAsync<ProjectDraftResponse>())!;
        using var validationResponse = await client.PostAsJsonAsync(path + "/blueprints/validate",
            new { expectedConcurrencyToken = draft.ConcurrencyToken });
        var validation = (await validationResponse.Content.ReadFromJsonAsync<ValidationRunResponse>())!;
        Assert.True(validation.Passed);
        using var edited = await client.PostAsJsonAsync(path + "/requirements/REQ-001/review",
            new { expectedRevision = 2, decision = "edit", editedTitle = "New title", editedDescription = "New content" });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        using var refused = await client.PostAsJsonAsync(path + "/approvals",
            new { expectedConcurrencyToken = draft.ConcurrencyToken, validationRunId = validation.Id });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using var revalidation = await client.PostAsJsonAsync(path + "/blueprints/validate",
            new { expectedConcurrencyToken = draft.ConcurrencyToken });
        var latest = (await revalidation.Content.ReadFromJsonAsync<ValidationRunResponse>())!;
        Assert.False(latest.Passed);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
        Assert.Empty(await db.ApprovedRevisions.ToArrayAsync());
    }

    private void SeedFixtureReview(BlueprintDocument blueprint, Guid projectId, Guid actor)
    {
        var requirement = blueprint.Requirements[0];
        _factory.CandidateStore.Items.Add((projectId, new ReviewedRequirement(requirement.Id,
            "Catalogue search", "Users can search the school library catalogue.", RequirementReviewStatus.Confirmed,
            2, requirement.Evidence.Select(e => new CandidateEvidence(e.SourceId, e.Locator, e.Quote!)).ToArray(),
            actor, requirement.ConfirmedAt)));
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient CreateClient(User user, WorkflowApiFactory? factory = null)
    {
        factory ??= _factory;
        var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.CreateAccessToken(user).Token);
        return client;
    }

    private static BlueprintDocument LoadBlueprint(Guid projectId, Guid reviewerId)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "blueprint.valid.v1.json");
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        json["projectId"] = projectId.ToString();
        json["requirements"]![0]!["text"] = "Catalogue search\nUsers can search the school library catalogue.";
        json["requirements"]![0]!["confirmedBy"] = reviewerId.ToString();
        return BlueprintJsonContract.Parse(json.ToJsonString());
    }

    private static MultipartFormDataContent DocumentForm(
        string fileName,
        string mediaType,
        string content = "REQVIORA requirements")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(file, "file", fileName);
        return form;
    }

    private static async Task<Guid> SaveBuilderStep(
        HttpClient client, Guid projectId, string step, object data, Guid? expectedToken)
    {
        using var response = await client.PutAsJsonAsync($"/api/projects/{projectId}/builder/steps/{step}",
            new { data, expectedConcurrencyToken = expectedToken });
        Assert.True(response.IsSuccessStatusCode,
            $"Builder step {step} failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["concurrencyToken"]!.GetValue<Guid>();
    }

    private static MultipartFormDataContent VoiceForm(bool consent)
    {
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent([0x52, 0x49, 0x46, 0x46]), "audio", "voice.wav" },
            { new StringContent("ta-LK"), "languageCode" },
            { new StringContent("5"), "durationSeconds" },
            { new StringContent(consent.ToString()), "consentGranted" }
        };
        form.First().Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        return form;
    }

    private sealed record AiJobCreatedPayload(Guid Id, string State, bool Created);
    private sealed record VoiceTranscriptPayload(
        Guid TranscriptId,
        Guid AudioFileId,
        string LanguageCode,
        string OriginalText,
        string EditableText,
        int Revision,
        IReadOnlyList<TranscriptSegment> Segments,
        string Status,
        DateTimeOffset? ReviewedAt);

    private sealed class WorkflowApiFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        public User Owner { get; } = NewUser("owner@example.test");
        public User OtherUser { get; } = NewUser("other@example.test");
        public Workspace Workspace { get; }
        public Project Project { get; }
        public TestFileStorage Storage { get; } = new();
        public TestVoiceTranscriber Voice { get; } = new();
        public TestOcrProvider Ocr { get; } = new();

        private readonly bool _enableAiForTest;
        private readonly bool _enableOcrForTest;
        public TestCandidateStore CandidateStore { get; } = new();

        public WorkflowApiFactory(bool enableAiForTest = false, bool enableOcrForTest = false)
        {
            _enableAiForTest = enableAiForTest;
            _enableOcrForTest = enableOcrForTest;
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
                services.RemoveAll<IHostedService>();
                services.RemoveAll<DbContextOptions<ReqvioraDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ReqvioraDbContext>>();
                services.RemoveAll<ReqvioraDbContext>();
                services.RemoveAll<IFileStorageService>();
                services.AddDbContext<ReqvioraDbContext>(options => options.UseSqlite(_connection));
                services.AddSingleton<IFileStorageService>(Storage);
                services.RemoveAll<IRequirementCandidateStore>();
                services.AddSingleton<IRequirementCandidateStore>(CandidateStore);
                services.RemoveAll<IRequirementReviewStore>();
                services.AddSingleton<IRequirementReviewStore>(CandidateStore);
                services.RemoveAll<IClarificationStore>();
                services.AddSingleton<IClarificationStore>(CandidateStore);
                if (_enableAiForTest)
                {
                    services.PostConfigure<AiOptions>(options => options.Enabled = true);
                    services.RemoveAll<IStructuredAiProvider>();
                    services.AddSingleton<IStructuredAiProvider>(new TestExtractionProvider());
                    services.RemoveAll<IVoiceTranscriber>();
                    services.AddSingleton<IVoiceTranscriber>(Voice);
                }
                if (_enableOcrForTest)
                {
                    services.RemoveAll<IDocumentTextParser>();
                    services.AddSingleton<IDocumentTextParser>(new EmptyDocumentParser());
                    services.RemoveAll<IDocumentOcrProvider>();
                    services.AddSingleton<IDocumentOcrProvider>(Ocr);
                }
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                _connection.Dispose();
        }

        public AuditEvent[] AuditEvents()
        {
            using var scope = Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>()
                .AuditEvents.AsNoTracking().ToArray();
        }

        public void AddAcceptedMember(User user, string role)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ReqvioraDbContext>();
            db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = Project.Id,
                UserId = user.Id,
                Role = role,
                JoinedAtUtc = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        private static User NewUser(string email) => new()
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "not-used-in-tests",
            DisplayName = email
        };

        public sealed class TestExtractionProvider : IStructuredAiProvider
        {
            public Task<AiProviderResponse> ExecuteAsync(AiProviderRequest request, CancellationToken cancellationToken = default)
            {
                Assert.Contains("SRC-INPUT", request.InputText);
                const string json = """
                    {"contractVersion":"1.0","candidates":[{"id":"REQ-LOGIN","title":"Login","description":"Users need sign in","languageCode":"en","confidence":0.9,"evidence":[{"sourceId":"SRC-INPUT","locator":"line 1","quote":"Users need sign in"}]}]}
                    """;
                return Task.FromResult(new AiProviderResponse(request.RequestId, AiRunStatus.Completed, json, null));
            }
        }

        public sealed class TestVoiceTranscriber : IVoiceTranscriber
        {
            public bool FailNext { get; set; }
            public bool MismatchSourceNext { get; set; }

            public Task<TranscriptDraft> TranscribeAsync(
                Reqviora.Application.Features.Documents.VoiceInputRequest request,
                Stream audioContent,
                CancellationToken cancellationToken = default)
            {
                if (FailNext)
                {
                    FailNext = false;
                    throw new InvalidDataException("Simulated provider failure.");
                }
                const string text = "வணக்கம். User needs secure login.";
                var audioFileId = MismatchSourceNext ? Guid.NewGuid() : request.AudioFileId;
                MismatchSourceNext = false;
                return Task.FromResult(new TranscriptDraft(
                    Guid.NewGuid(), audioFileId, request.LanguageCode, text, text, 1,
                    [new TranscriptSegment(0, 5000, text)]));
            }
        }

        public sealed class EmptyDocumentParser : IDocumentTextParser
        {
            public Task<DocumentParseResult> ParseAsync(
                Reqviora.Application.Features.Documents.DocumentInputRequest document,
                Stream fileContent,
                CancellationToken cancellationToken = default) =>
                Task.FromResult(new DocumentParseResult(document.UploadedFileId, [],
                    ["No extractable text was found."]));
        }

        public sealed class TestOcrProvider : IDocumentOcrProvider
        {
            public bool IsEnabled => true;
            public int Calls { get; private set; }
            public bool Fail { get; set; }

            public Task<DocumentParseResult> RecognizeAsync(
                Reqviora.Application.Features.Documents.DocumentInputRequest document,
                Stream fileContent,
                CancellationToken cancellationToken = default)
            {
                Calls++;
                if (Fail)
                    throw new DocumentOcrException("Simulated OCR outage.");
                const string text = "OCR recovered requirement";
                var evidence = new SourceEvidenceRef(document.UploadedFileId.ToString("D"),
                    "page:1:ocr:offset:0", 1, 0, text.Length, text);
                return Task.FromResult(new DocumentParseResult(document.UploadedFileId,
                    [new ParsedDocumentSegment(0, text, evidence)], []));
            }
        }

        public sealed class TestCandidateStore : IRequirementCandidateStore, IRequirementReviewStore, IClarificationStore
        {
            public List<(Guid ProjectId, ReviewedRequirement Requirement)> Items { get; } = new();
            public Task InsertAsync(Guid projectId, IReadOnlyList<ReviewedRequirement> candidates, CancellationToken cancellationToken = default)
            {
                Items.AddRange(candidates.Select(item => (projectId, item)));
                return Task.CompletedTask;
            }
            public Task<IReadOnlyList<ReviewedRequirement>> ListAsync(Guid projectId, int offset, int limit, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<ReviewedRequirement>>(Items.Where(item => item.ProjectId == projectId)
                    .Select(item => item.Requirement).OrderBy(item => item.Id).Skip(offset).Take(limit).ToArray());
            private readonly List<(Guid ProjectId, ClarificationQuestion Question)> _questions = new();
            public Task<RequirementReviewSnapshot?> FindAsync(Guid projectId, string requirementId, CancellationToken cancellationToken = default)
            {
                var item = Items.SingleOrDefault(x => x.ProjectId == projectId && x.Requirement.Id == requirementId).Requirement;
                return Task.FromResult<RequirementReviewSnapshot?>(item is null ? null : new(projectId, item));
            }
            public Task<bool> TryCommitAsync(Guid projectId, int expectedRevision, RequirementReviewResult result, CancellationToken cancellationToken = default)
            {
                var index = Items.FindIndex(x => x.ProjectId == projectId && x.Requirement.Id == result.Requirement.Id && x.Requirement.Revision == expectedRevision);
                if (index < 0) return Task.FromResult(false);
                Items[index] = (projectId, result.Requirement);
                return Task.FromResult(true);
            }
            public Task InsertAsync(Guid projectId, ClarificationQuestion question, CancellationToken cancellationToken = default)
            {
                _questions.Add((projectId, question));
                return Task.CompletedTask;
            }
            public Task<IReadOnlyList<ClarificationQuestion>> ListAsync(Guid projectId, string requirementId, int offset, int limit, CancellationToken cancellationToken = default) =>
                Task.FromResult<IReadOnlyList<ClarificationQuestion>>(_questions.Where(x => x.ProjectId == projectId && x.Question.RequirementId == requirementId)
                    .Select(x => x.Question).OrderBy(x => x.Id).Skip(offset).Take(limit).ToArray());
            public Task<ClarificationQuestion?> FindAsync(Guid projectId, string requirementId, Guid questionId, CancellationToken cancellationToken = default) =>
                Task.FromResult<ClarificationQuestion?>(_questions.SingleOrDefault(x => x.ProjectId == projectId && x.Question.RequirementId == requirementId && x.Question.Id == questionId).Question);
            public Task<bool> TryAnswerAsync(Guid projectId, ClarificationQuestion answered, int expectedRevision, Guid actorId, CancellationToken cancellationToken = default)
            {
                var index = _questions.FindIndex(x => x.ProjectId == projectId && x.Question.RequirementId == answered.RequirementId && x.Question.Id == answered.Id && x.Question.Revision == expectedRevision && !x.Question.IsResolved);
                if (index < 0) return Task.FromResult(false);
                _questions[index] = (projectId, answered);
                return Task.FromResult(true);
            }
        }

        public sealed class TestFileStorage : IFileStorageService
        {
            private readonly Dictionary<string, byte[]> _files = new();
            public int Saves { get; private set; }
            public int Deletes { get; private set; }
            public bool RejectNext { get; set; }

            public async Task<string> SaveAsync(
                Stream content,
                string fileName,
                CancellationToken cancellationToken = default)
            {
                if (RejectNext)
                {
                    RejectNext = false;
                    throw new InvalidDataException("Simulated malware detection.");
                }

                using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, cancellationToken);
                var path = $"private/{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
                _files.Add(path, buffer.ToArray());
                Saves++;
                return path;
            }

            public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult<Stream>(new MemoryStream(_files[storagePath], writable: false));
            }

            public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _files.Remove(storagePath);
                Deletes++;
                return Task.CompletedTask;
            }

            public Task<Uri?> CreateSignedReadUrlAsync(
                string storagePath,
                TimeSpan lifetime,
                CancellationToken cancellationToken = default) => Task.FromResult<Uri?>(null);
        }
    }
}
