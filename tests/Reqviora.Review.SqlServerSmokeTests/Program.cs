using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Review.Persistence;

var connectionString = Environment.GetEnvironmentVariable("REQVIORA_REVIEW_SQLSERVER");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Missing REQVIORA_REVIEW_SQLSERVER environment variable.");
    return 2;
}

// These smoke tests execute DDL and write fixture data. Never target a shared database.
if (new SqlConnectionStringBuilder(connectionString).DataSource.Split(',')[0] is not ("127.0.0.1" or "localhost"))
{
    Console.Error.WriteLine("Review smoke tests require a local disposable database server.");
    return 2;
}

var projectId = Guid.NewGuid();
var requirementId = "REQ-SMOKE-" + Guid.NewGuid().ToString("N").ToUpperInvariant();
var reviewerId = Guid.NewGuid();
var evidence = new List<CandidateEvidence> { new("SRC-SMOKE", "Test document", "Login is required.") };
var passed = 0;
var inserted = false;

void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAILED: " + description);
    passed++;
    Console.WriteLine("[PASS] " + description);
}

void AddIdentity(SqlCommand command)
{
    command.Parameters.Add("project_id", SqlDbType.UniqueIdentifier).Value = projectId;
    command.Parameters.Add("requirement_id", SqlDbType.VarChar, 100).Value = requirementId;
}

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();
var schemaPath = Path.GetFullPath("database/reqviora_review/001_create_review_store.sqlserver.sql");
var script = (await File.ReadAllTextAsync(schemaPath)) + "\nGO\n" +
    await File.ReadAllTextAsync("database/reqviora_review/002_create_clarifications.sqlserver.sql");
foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
{
    if (string.IsNullOrWhiteSpace(batch)) continue;
    await using var schema = new SqlCommand(batch, connection);
    await schema.ExecuteNonQueryAsync();
}

try
{
    var candidateStore = new RequirementCandidateStore(connectionString, sqlServer: true);
    var candidate = new ReviewedRequirement(requirementId, "Login", "User login requirement",
        RequirementReviewStatus.Suggested, 1, evidence, null, null);
    await candidateStore.InsertAsync(projectId, new[] { candidate });
    inserted = true;
    var page = await candidateStore.ListAsync(projectId, 0, 10);
    Check(page.Count == 1 && page[0].Id == requirementId && page[0].Status == RequirementReviewStatus.Suggested,
        "Candidate insert and list share the review table");
    Check((await candidateStore.ListAsync(Guid.NewGuid(), 0, 10)).Count == 0, "Candidate listing isolates projects");
    Check((await candidateStore.ListAsync(projectId, 1, 10)).Count == 0, "Candidate listing honors pagination");

    var clarifications = new ClarificationStore(connectionString, sqlServer: true);
    var question = new ClarificationQuestion(Guid.NewGuid(), requirementId, "Which login method?", null, 1, false);
    await clarifications.InsertAsync(projectId, question);
    Check((await clarifications.ListAsync(projectId, requirementId, 0, 10)).Single() == question, "Clarification is persisted");
    Check(await clarifications.FindAsync(Guid.NewGuid(), requirementId, question.Id) is null, "Clarifications isolate projects");
    var answer = ClarificationService.Answer(question, new(question.Id, 1, "Email"));
    Check(await clarifications.TryAnswerAsync(projectId, answer, 1, reviewerId), "Clarification answer is persisted");
    Check(!await clarifications.TryAnswerAsync(projectId, answer, 1, reviewerId), "Repeated answer is rejected");
    Check(await clarifications.FindAsync(projectId, requirementId, question.Id) == answer, "Resolved answer can be read");

    var store = new SqlServerRequirementReviewStore(connectionString);
    var snapshot = await store.FindAsync(projectId, requirementId);
    Check(snapshot is not null && snapshot.Requirement.Status == RequirementReviewStatus.Suggested &&
        snapshot.Requirement.Evidence.Count == 1, "SQL Server reads suggested requirement");

    var decision = new RequirementReviewDecision(
        requirementId, 1, RequirementDecisionKind.Accept, reviewerId, DateTimeOffset.UtcNow);
    var result = RequirementReviewService.Apply(snapshot!.Requirement, decision);
    // Simulate the draft/approval transaction holding the same project lock.
    await using var approvalTransaction = (SqlTransaction)await connection.BeginTransactionAsync();
    const string approvalLockSql = """
        DECLARE @lock_result int;
        EXEC @lock_result = sys.sp_getapplock
            @Resource = @lock_resource,
            @LockMode = 'Exclusive',
            @LockOwner = 'Transaction',
            @LockTimeout = 15000;
        SELECT @lock_result;
        """;
    await using (var approvalLock = new SqlCommand(approvalLockSql, connection, approvalTransaction))
    {
        approvalLock.Parameters.Add("lock_resource", SqlDbType.NVarChar, 255).Value =
            ProjectReviewLock.SqlServerResource(projectId);
        Check(Convert.ToInt32(await approvalLock.ExecuteScalarAsync()) >= 0,
            "Approval transaction acquires the project review lock");
    }
    var concurrentReview = store.TryCommitAsync(projectId, 1, result);
    await Task.Delay(150);
    Check(!concurrentReview.IsCompleted, "Concurrent review waits for the approval transaction");
    await approvalTransaction.CommitAsync();
    Check(await concurrentReview, "Review revision commits");

    Check((await candidateStore.ListAsync(projectId, 0, 10)).Single().Status == RequirementReviewStatus.Confirmed,
        "Candidate listing reflects the human review decision");

    var updated = await store.FindAsync(projectId, requirementId);
    Check(updated is not null && updated.Requirement.Status == RequirementReviewStatus.Confirmed &&
        updated.Requirement.Revision == 2 && updated.Requirement.ConfirmedBy == reviewerId,
        "Human confirmation is persisted");
    Check(!await store.TryCommitAsync(projectId, 1, result), "Stale revision is rejected");

    const string auditSql = """
        SELECT COUNT(*) FROM reqviora_review.events
        WHERE project_id = @project_id AND requirement_id = @requirement_id AND actor_id = @actor_id;
        """;
    await using (var audit = new SqlCommand(auditSql, connection))
    {
        AddIdentity(audit);
        audit.Parameters.Add("actor_id", SqlDbType.UniqueIdentifier).Value = reviewerId;
        Check(Convert.ToInt32(await audit.ExecuteScalarAsync()) == 1, "One audit event created for reviewer");
    }
    Console.WriteLine($"PASSED: {passed}");
    Console.WriteLine("FAILED: 0");
}
finally
{
    if (inserted)
    {
        const string cleanupSql = """
            DELETE FROM reqviora_review.events WHERE project_id = @project_id AND requirement_id = @requirement_id;
            DELETE FROM reqviora_review.requirements WHERE project_id = @project_id AND requirement_id = @requirement_id;
            """;
        await using var cleanup = new SqlCommand(cleanupSql, connection);
        AddIdentity(cleanup);
        await cleanup.ExecuteNonQueryAsync();
    }
}
return 0;
