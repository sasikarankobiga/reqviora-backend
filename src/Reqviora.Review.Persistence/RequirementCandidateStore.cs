using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Npgsql;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.RequirementReview;

namespace Reqviora.Review.Persistence;

public sealed class RequirementCandidateStore(string connectionString, bool sqlServer) : IRequirementCandidateStore
{
    private DbConnection OpenConnection() => sqlServer
        ? new SqlConnection(connectionString)
        : new NpgsqlConnection(connectionString);

    public async Task InsertAsync(Guid projectId, IReadOnlyList<ReviewedRequirement> candidates,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || candidates.Count > 100 || candidates.Any(candidate =>
                candidate.Status != RequirementReviewStatus.Suggested || candidate.Revision != 1 ||
                candidate.ConfirmedBy is not null || candidate.ConfirmedAt is not null ||
                string.IsNullOrWhiteSpace(candidate.Id) || candidate.Id.Length > 100 ||
                string.IsNullOrWhiteSpace(candidate.Title) || candidate.Title.Length > 200 ||
                candidate.Description is null || candidate.Description.Length > 4000 ||
                candidate.Evidence is null || candidate.Evidence.Count is < 1 or > 10) ||
            candidates.Select(candidate => candidate.Id).Distinct(StringComparer.Ordinal).Count() != candidates.Count)
            throw new ArgumentException("Only valid new suggested requirements may be inserted.");
        if (candidates.Count == 0) return;

        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var candidate in candidates)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var evidenceExpression = sqlServer ? "@evidence" : "CAST(@evidence AS jsonb)";
            command.CommandText = $"""
                INSERT INTO reqviora_review.requirements
                    (project_id, requirement_id, title, description, status, revision, evidence_json, confirmed_by, confirmed_at)
                VALUES (@project, @id, @title, @description, 'Suggested', 1, {evidenceExpression}, NULL, NULL);
                """;
            Add(command, "project", DbType.Guid, projectId);
            Add(command, "id", DbType.String, candidate.Id);
            Add(command, "title", DbType.String, candidate.Title);
            Add(command, "description", DbType.String, candidate.Description);
            Add(command, "evidence", DbType.String, JsonSerializer.Serialize(candidate.Evidence));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewedRequirement>> ListAsync(Guid projectId, int offset, int limit,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || offset < 0 || limit is < 1 or > 100)
            throw new ArgumentException("Invalid candidate page.");
        await using var connection = OpenConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var evidenceColumn = sqlServer ? "evidence_json" : "evidence_json::text";
        var pagination = sqlServer ? "OFFSET @offset ROWS FETCH NEXT @limit ROWS ONLY" : "LIMIT @limit OFFSET @offset";
        command.CommandText = $"""
            SELECT requirement_id, title, description, status, revision, {evidenceColumn}, confirmed_by, confirmed_at
            FROM reqviora_review.requirements WHERE project_id = @project
            ORDER BY requirement_id {pagination};
            """;
        Add(command, "project", DbType.Guid, projectId);
        Add(command, "offset", DbType.Int32, offset);
        Add(command, "limit", DbType.Int32, limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<ReviewedRequirement>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!Enum.TryParse<RequirementReviewStatus>(reader.GetString(3), out var status) || !Enum.IsDefined(status))
                throw new InvalidDataException("Invalid stored requirement status.");
            results.Add(new ReviewedRequirement(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                status, reader.GetInt32(4),
                JsonSerializer.Deserialize<List<CandidateEvidence>>(reader.GetString(5))
                    ?? throw new InvalidDataException("Invalid stored evidence."),
                reader.IsDBNull(6) ? null : reader.GetGuid(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7)));
        }
        return results;
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
