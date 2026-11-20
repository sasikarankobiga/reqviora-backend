
using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class NavigationEdgeService
{
    private readonly ReqvioraDbContext _dbContext;

    public NavigationEdgeService(ReqvioraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<NavigationEdge> CreateAsync(
        Guid projectId,
        Guid fromScreenId,
        Guid toScreenId,
        string trigger,
        string? condition = null,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty ||
            fromScreenId == Guid.Empty ||
            toScreenId == Guid.Empty)
        {
            throw new ArgumentException(
                "Project ID and Screen IDs are required.");
        }

        if (string.IsNullOrWhiteSpace(trigger))
        {
            throw new ArgumentException(
                "Navigation trigger is required.");
        }

        trigger = trigger.Trim();
        condition = condition?.Trim();

        if (trigger.Length > 150 ||
            condition?.Length > 1000)
        {
            throw new ArgumentException(
                "Navigation fields exceed allowed lengths.");
        }

        var projectExists = await _dbContext.Projects
            .AnyAsync(p => p.Id == projectId, cancellationToken);

        if (!projectExists)
        {
            throw new InvalidOperationException(
                "Project not found.");
        }

        var screens = await _dbContext.Screens
            .AsNoTracking()
            .Where(s =>
                s.Id == fromScreenId ||
                s.Id == toScreenId)
            .ToListAsync(cancellationToken);

        var fromScreen = screens
            .FirstOrDefault(s => s.Id == fromScreenId);

        var toScreen = screens
            .FirstOrDefault(s => s.Id == toScreenId);

        if (fromScreen is null || toScreen is null)
        {
            throw new InvalidOperationException(
                "Source or destination screen not found.");
        }

        if (fromScreen.ProjectId != projectId ||
            toScreen.ProjectId != projectId)
        {
            throw new InvalidOperationException(
                "Both screens must belong to the same project.");
        }

        var existingEdge = await _dbContext.NavigationEdges
            .FirstOrDefaultAsync(
                edge =>
                    edge.ProjectId == projectId &&
                    edge.FromScreenId == fromScreenId &&
                    edge.ToScreenId == toScreenId &&
                    edge.Trigger == trigger &&
                    edge.Condition == condition,
                cancellationToken);

        if (existingEdge is not null)
        {
            return existingEdge;
        }

        var newEdge = new NavigationEdge
        {
            ProjectId = projectId,
            FromScreenId = fromScreenId,
            ToScreenId = toScreenId,
            Trigger = trigger,
            Condition = condition
        };

        _dbContext.NavigationEdges.Add(newEdge);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return newEdge;
    }
}
