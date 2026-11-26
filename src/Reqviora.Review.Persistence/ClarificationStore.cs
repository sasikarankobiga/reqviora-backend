using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Npgsql;
using Reqviora.Application.Features.RequirementReview;

namespace Reqviora.Review.Persistence;

public sealed class ClarificationStore(string connectionString, bool sqlServer) : IClarificationStore
{
    private DbConnection Connection() => sqlServer ? new SqlConnection(connectionString) : new NpgsqlConnection(connectionString);

    public async Task InsertAsync(Guid projectId, ClarificationQuestion question, CancellationToken cancellationToken = default)
    {
        await using var connection = Connection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO reqviora_review.clarifications (project_id, requirement_id, question_id, question, revision)
            VALUES (@project, @requirement, @id, @question, 1);
            """;
        Identity(command, projectId, question.RequirementId, question.Id);
        Add(command, "question", DbType.String, question.Question);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClarificationQuestion>> ListAsync(Guid projectId, string requirementId,
        int offset, int limit, CancellationToken cancellationToken = default)
    {
        if (offset < 0 || limit is < 1 or > 100) throw new ArgumentException("Invalid clarification page.");
        await using var connection = Connection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var page = sqlServer ? "OFFSET @offset ROWS FETCH NEXT @limit ROWS ONLY" : "LIMIT @limit OFFSET @offset";
        command.CommandText = $"""
            SELECT question_id, question, answer, revision FROM reqviora_review.clarifications
            WHERE project_id = @project AND requirement_id = @requirement ORDER BY question_id {page};
            """;
        Add(command, "project", DbType.Guid, projectId);
        Add(command, "requirement", DbType.String, requirementId);
        Add(command, "offset", DbType.Int32, offset);
        Add(command, "limit", DbType.Int32, limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ClarificationQuestion>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(Read(reader, requirementId));
        return result;
    }

    public async Task<ClarificationQuestion?> FindAsync(Guid projectId, string requirementId, Guid questionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = Connection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT question_id, question, answer, revision FROM reqviora_review.clarifications
            WHERE project_id = @project AND requirement_id = @requirement AND question_id = @id;
            """;
        Identity(command, projectId, requirementId, questionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader, requirementId) : null;
    }

    public async Task<bool> TryAnswerAsync(Guid projectId, ClarificationQuestion answered, int expectedRevision,
        Guid actorId, CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty || !answered.IsResolved || string.IsNullOrWhiteSpace(answered.Answer) ||
            answered.Revision != expectedRevision + 1) throw new ArgumentException("Invalid clarification answer.");
        await using var connection = Connection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE reqviora_review.clarifications SET answer = @answer, revision = @next,
                answered_by = @actor, answered_at = @now
            WHERE project_id = @project AND requirement_id = @requirement AND question_id = @id
                AND revision = @expected AND answer IS NULL;
            """;
        Identity(command, projectId, answered.RequirementId, answered.Id);
        Add(command, "answer", DbType.String, answered.Answer);
        Add(command, "next", DbType.Int32, answered.Revision);
        Add(command, "expected", DbType.Int32, expectedRevision);
        Add(command, "actor", DbType.Guid, actorId);
        Add(command, "now", DbType.DateTimeOffset, DateTimeOffset.UtcNow);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static ClarificationQuestion Read(DbDataReader reader, string requirementId) => new(
        reader.GetGuid(0), requirementId, reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.GetInt32(3), !reader.IsDBNull(2));

    private static void Identity(DbCommand command, Guid projectId, string requirementId, Guid questionId)
    {
        Add(command, "project", DbType.Guid, projectId);
        Add(command, "requirement", DbType.String, requirementId);
        Add(command, "id", DbType.Guid, questionId);
    }

    private static void Add(DbCommand command, string name, DbType type, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
