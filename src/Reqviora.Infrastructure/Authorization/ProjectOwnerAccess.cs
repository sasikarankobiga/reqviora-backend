using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Authorization;

internal static class ProjectOwnerAccess
{
    internal static async Task<Project> RequireAsync(
        ReqvioraDbContext db, Guid projectId, Guid actorId, CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty || actorId == Guid.Empty)
            throw new UnauthorizedAccessException("A project and verified actor are required.");

        return await db.Projects.AsNoTracking().SingleOrDefaultAsync(
            project => project.Id == projectId && project.OwnerId == actorId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Project owner access is required.");
    }
}
