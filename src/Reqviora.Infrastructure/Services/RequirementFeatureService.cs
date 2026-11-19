
using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class RequirementFeatureService
{
    private readonly ReqvioraDbContext _dbContext;

    public RequirementFeatureService(ReqvioraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<RequirementFeature> LinkAsync(
        Guid requirementId,
        Guid featureId,
        CancellationToken cancellationToken = default)
    {
        if (requirementId == Guid.Empty || featureId == Guid.Empty)
        {
            throw new ArgumentException(
                "Requirement ID and Feature ID are required.");
        }

        var requirement = await _dbContext.Requirements
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.Id == requirementId,
                cancellationToken);

        if (requirement is null)
        {
            throw new InvalidOperationException(
                "Requirement not found.");
        }

        var feature = await _dbContext.Features
            .AsNoTracking()
            .SingleOrDefaultAsync(
                f => f.Id == featureId,
                cancellationToken);

        if (feature is null)
        {
            throw new InvalidOperationException(
                "Feature not found.");
        }

        if (requirement.ProjectId != feature.ProjectId)
        {
            throw new InvalidOperationException(
                "Requirement and Feature must belong to the same project.");
        }

        var existingLink = await _dbContext.RequirementFeatures
            .SingleOrDefaultAsync(
                link => link.RequirementId == requirementId &&
                        link.FeatureId == featureId,
                cancellationToken);

        if (existingLink is not null)
        {
            return existingLink;
        }

        var newLink = new RequirementFeature
        {
            RequirementId = requirementId,
            FeatureId = featureId
        };

        _dbContext.RequirementFeatures.Add(newLink);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return newLink;
    }
}
