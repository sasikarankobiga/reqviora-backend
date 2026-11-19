
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class BlueprintVersionService
{
    private readonly ReqvioraDbContext _dbContext;

    public BlueprintVersionService(
        ReqvioraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BlueprintVersion> CreateVersionAsync(
        Guid blueprintId,
        string definitionJson,
        string? changeSummary = null,
        CancellationToken cancellationToken = default)
    {
        // Validate Blueprint ID
        if (blueprintId == Guid.Empty)
        {
            throw new ArgumentException(
                "Blueprint ID is required.");
        }

        // Validate JSON input
        if (string.IsNullOrWhiteSpace(definitionJson))
        {
            throw new ArgumentException(
                "Blueprint JSON is required.");
        }

        // Validate JSON format
        try
        {
            using var document =
                JsonDocument.Parse(definitionJson);

            if (document.RootElement.ValueKind
                != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    "Blueprint JSON must be an object.");
            }
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(
                "Invalid Blueprint JSON.",
                nameof(definitionJson),
                ex);
        }

        // Validate Change Summary length
        if (changeSummary?.Length > 1000)
        {
            throw new ArgumentException(
                "Change summary cannot exceed 1000 characters.");
        }

        // Check Blueprint exists
        var blueprintExists = await _dbContext.Blueprints
            .AnyAsync(
                b => b.Id == blueprintId,
                cancellationToken);

        if (!blueprintExists)
        {
            throw new InvalidOperationException(
                "Blueprint not found.");
        }

        // Get latest version number
        var latestVersion = await _dbContext.BlueprintVersions
            .Where(v => v.BlueprintId == blueprintId)
            .MaxAsync(
                v => (int?)v.VersionNumber,
                cancellationToken) ?? 0;

        // Create next version
        var newVersion = new BlueprintVersion
        {
            BlueprintId = blueprintId,
            VersionNumber = latestVersion + 1,
            DefinitionJson = definitionJson,
            ChangeSummary = changeSummary,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.BlueprintVersions.Add(newVersion);

        try
        {
            // Save new version
            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName:
                        "IX_BlueprintVersions_BlueprintId_VersionNumber"
            })
        {
            // Remove failed entity from EF tracking
            _dbContext.Entry(newVersion).State =
                EntityState.Detached;

            throw new InvalidOperationException(
                "Another user created the same blueprint version. " +
                "Please retry saving.",
                ex);
        }

        return newVersion;
    }
}
