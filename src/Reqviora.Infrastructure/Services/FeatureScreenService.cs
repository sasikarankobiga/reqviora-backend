
using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class FeatureScreenService
{
    private readonly ReqvioraDbContext _dbContext;

    public FeatureScreenService(ReqvioraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<FeatureScreen> LinkAsync(
        Guid featureId,
        Guid screenId,
        CancellationToken cancellationToken = default)
    {
        if (featureId == Guid.Empty || screenId == Guid.Empty)
        {
            throw new ArgumentException(
                "Feature ID and Screen ID are required.");
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

        var screen = await _dbContext.Screens
            .AsNoTracking()
            .SingleOrDefaultAsync(
                s => s.Id == screenId,
                cancellationToken);

        if (screen is null)
        {
            throw new InvalidOperationException(
                "Screen not found.");
        }

        if (feature.ProjectId != screen.ProjectId)
        {
            throw new InvalidOperationException(
                "Feature and Screen must belong to the same project.");
        }

        var existingLink = await _dbContext.FeatureScreens
            .SingleOrDefaultAsync(
                link => link.FeatureId == featureId &&
                        link.ScreenId == screenId,
                cancellationToken);

        if (existingLink is not null)
        {
            return existingLink;
        }

        var newLink = new FeatureScreen
        {
            FeatureId = featureId,
            ScreenId = screenId
        };

        _dbContext.FeatureScreens.Add(newLink);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return newLink;
    }
}
