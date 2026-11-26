using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.Billing;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class ProjectBillingAccessService(
    ReqvioraDbContext db) : IProjectBillingAccess
{
    public async Task<ProjectBillingSnapshot?> ReadAsync(
        Guid projectId,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || actorId == Guid.Empty)
            return null;

        var project = await db.Projects
            .AsNoTracking()
            .Where(x => x.Id == projectId &&
                        x.OwnerId == actorId)
            .Select(x => new
            {
                x.Id,
                x.CreatedAt,
                Paid = db.ProjectPaymentOrders.Any(order =>
                    order.ProjectId == x.Id &&
                    order.Status ==
                        ProjectPaymentStatus.Succeeded),
                Pending = db.ProjectPaymentOrders.Any(order =>
                    order.ProjectId == x.Id &&
                    order.Status ==
                        ProjectPaymentStatus.Pending)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (project is null)
            return null;

        return new ProjectBillingSnapshot(
            project.Id,
            project.CreatedAt,
            project.Paid,
            project.Pending);
    }
}
