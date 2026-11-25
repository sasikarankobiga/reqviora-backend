
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Review.Persistence;

var connectionString =
    Environment.GetEnvironmentVariable(
        "REQVIORA_REVIEW_PG");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        "Missing REQVIORA_REVIEW_PG environment variable.");
    return 2;
}

// These smoke tests execute DDL and write fixture data. Never target a shared database.
if (new NpgsqlConnectionStringBuilder(connectionString).Host is not ("127.0.0.1" or "localhost"))
{
    Console.Error.WriteLine("Review smoke tests require a local disposable database server.");
    return 2;
}

var projectId = Guid.NewGuid();
var requirementId =
    "REQ-SMOKE-" +
    Guid.NewGuid().ToString("N").ToUpperInvariant();
var reviewerId = Guid.NewGuid();

var evidence = new List<CandidateEvidence>
{
    new("SRC-SMOKE", "Test document", "Login is required.")
};

int passed = 0;
bool inserted = false;

void Check(bool condition, string description)
{
    if (!condition)
        throw new Exception("FAILED: " + description);

    passed++;
    Console.WriteLine("[PASS] " + description);
}

void AddIdentity(NpgsqlCommand command)
{
    command.Parameters.AddWithValue(
        "project_id", NpgsqlDbType.Uuid, projectId);

    command.Parameters.AddWithValue(
        "requirement_id",
        NpgsqlDbType.Varchar,
        requirementId);
}

await using var connection =
    new NpgsqlConnection(connectionString);

await connection.OpenAsync();

var schemaPath = Path.GetFullPath("database/reqviora_review/001_create_review_store.sql");
await using (var schema = new NpgsqlCommand(await File.ReadAllTextAsync(schemaPath), connection))
    await schema.ExecuteNonQueryAsync();
await using (var schema = new NpgsqlCommand(await File.ReadAllTextAsync("database/reqviora_review/002_create_clarifications.sql"), connection))
    await schema.ExecuteNonQueryAsync();

try
{
    // Seed one temporary requirement.
    var candidateStore = new RequirementCandidateStore(connectionString, sqlServer: false);
    var candidate = new ReviewedRequirement(requirementId, "Login", "User login requirement",
        RequirementReviewStatus.Suggested, 1, evidence, null, null);
    await candidateStore.InsertAsync(projectId, new[] { candidate });
    inserted = true;
    var page = await candidateStore.ListAsync(projectId, 0, 10);
    Check(page.Count == 1 && page[0].Id == requirementId && page[0].Status == RequirementReviewStatus.Suggested,
        "Candidate insert and list share the review table");
    Check((await candidateStore.ListAsync(Guid.NewGuid(), 0, 10)).Count == 0, "Candidate listing isolates projects");
    Check((await candidateStore.ListAsync(projectId, 1, 10)).Count == 0, "Candidate listing honors pagination");

    var clarifications = new ClarificationStore(connectionString, sqlServer: false);
    var question = new ClarificationQuestion(Guid.NewGuid(), requirementId, "Which login method?", null, 1, false);
    await clarifications.InsertAsync(projectId, question);
    Check((await clarifications.ListAsync(projectId, requirementId, 0, 10)).Single() == question, "Clarification is persisted");
    Check(await clarifications.FindAsync(Guid.NewGuid(), requirementId, question.Id) is null, "Clarifications isolate projects");
    var answer = ClarificationService.Answer(question, new(question.Id, 1, "Email"));
    Check(await clarifications.TryAnswerAsync(projectId, answer, 1, reviewerId), "Clarification answer is persisted");
    Check(!await clarifications.TryAnswerAsync(projectId, answer, 1, reviewerId), "Repeated answer is rejected");
    Check(await clarifications.FindAsync(projectId, requirementId, question.Id) == answer, "Resolved answer can be read");

    var store = new PostgresRequirementReviewStore(
        connectionString);

    // TEST 1: Read candidate.
    var snapshot = await store.FindAsync(
        projectId, requirementId);

    Check(
        snapshot is not null &&
        snapshot.Requirement.Status ==
            RequirementReviewStatus.Suggested &&
        snapshot.Requirement.Evidence.Count == 1,
        "PostgreSQL reads suggested requirement");

    // TEST 2: Human review and commit.
    var decision = new RequirementReviewDecision(
        requirementId,
        1,
        RequirementDecisionKind.Accept,
        reviewerId,
        DateTimeOffset.UtcNow);

    var result = RequirementReviewService.Apply(
        snapshot!.Requirement,
        decision);

    var saved = await store.TryCommitAsync(
        projectId, 1, result);

    Check(saved, "Review revision commits");

    // TEST 3: Confirmed requirement persisted.
    Check((await candidateStore.ListAsync(projectId, 0, 10)).Single().Status == RequirementReviewStatus.Confirmed,
        "Candidate listing reflects the human review decision");

    var updated = await store.FindAsync(
        projectId, requirementId);

    Check(
        updated is not null &&
        updated.Requirement.Status ==
            RequirementReviewStatus.Confirmed &&
        updated.Requirement.Revision == 2 &&
        updated.Requirement.ConfirmedBy == reviewerId,
        "Human confirmation is persisted");

    // TEST 4: Prevent stale update.
    var stale = await store.TryCommitAsync(
        projectId, 1, result);

    Check(!stale, "Stale revision is rejected");

    // TEST 5: Audit event persisted once.
    const string auditSql = """
        SELECT COUNT(*)
        FROM reqviora_review.events
        WHERE project_id = @project_id
          AND requirement_id = @requirement_id
          AND actor_id = @actor_id;
        """;

    await using (var audit =
        new NpgsqlCommand(auditSql, connection))
    {
        AddIdentity(audit);

        audit.Parameters.AddWithValue(
            "actor_id",
            NpgsqlDbType.Uuid,
            reviewerId);

        var count = Convert.ToInt32(
            await audit.ExecuteScalarAsync());

        Check(
            count == 1,
            "One audit event created for reviewer");
    }

    Console.WriteLine();
    Console.WriteLine($"PASSED: {passed}");
    Console.WriteLine("FAILED: 0");
}
finally
{
    if (inserted)
    {
        // Delete ONLY this test's temporary records.
        const string cleanupSql = """
            DELETE FROM reqviora_review.events
            WHERE project_id = @project_id
              AND requirement_id = @requirement_id;

            DELETE FROM reqviora_review.requirements
            WHERE project_id = @project_id
              AND requirement_id = @requirement_id;
            """;

        await using var cleanup =
            new NpgsqlCommand(cleanupSql, connection);

        AddIdentity(cleanup);

        await cleanup.ExecuteNonQueryAsync();
    }
}

return 0;
