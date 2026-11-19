
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class BlueprintVersionService
{
    private readonly ReqvioraDbContext _dbContext;

    public BlueprintVersionService(ReqvioraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BlueprintVersion> CreateVersionAsync(
        Guid blueprintId,
        string definitionJson,
        string? changeSummary = null,
        CancellationToken cancellationToken = default)
    {
        if (blueprintId == Guid.Empty)
        {
            throw new ArgumentException(
                "Blueprint ID is required.");
        }

        if (string.IsNullOrWhiteSpace(definitionJson))
        {
            throw new ArgumentException(
                "Blueprint JSON is required.");
        }

        // Validate JSON
        try
        {
            using var document = JsonDocument.Parse(definitionJson);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    "Blueprint JSON must be an object.");
            }
        }
        catch (JsonException)
        {
            throw new ArgumentException(
                "Invalid Blueprint JSON.");
        }

        // Check existing Blueprint
        var blueprintExists = await _dbContext.Blueprints
            .AnyAsync(b => b.Id == blueprintId, cancellationToken);

        if (!blueprintExists)
        {
            throw new InvalidOperationException(
                "Blueprint not found.");
        }

        // Find next version number
        var latestVersion = await _dbContext
            .Set<BlueprintVersion>()
            .Where(v => v.BlueprintId == blueprintId)
            .MaxAsync(
                v => (int?)v.VersionNumber,
                cancellationToken) ?? 0;

        var newVersion = new BlueprintVersion
        {
            BlueprintId = blueprintId,
            VersionNumber = latestVersion + 1,
            DefinitionJson = definitionJson,
            ChangeSummary = changeSummary,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Set<BlueprintVersion>().Add(newVersion);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return newVersion;
    }
}
