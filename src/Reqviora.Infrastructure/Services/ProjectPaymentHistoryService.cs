using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.Billing;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class ProjectPaymentHistoryService(
    ReqvioraDbContext db) : IProjectPaymentHistory
{
    public async Task<ProjectPaymentHistoryPage?> ListAsync(
        Guid projectId,
        Guid actorId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page is < 1 or > 1000000 ||
            pageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page));
        }

        var ownsProject = await db.Projects
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == projectId &&
                     x.OwnerId == actorId,
                cancellationToken);

        if (!ownsProject)
            return null;

        var query = AuthorizedOrders(
            projectId, actorId);

        var total = await query.CountAsync(
            cancellationToken);

        var rows = await query
            .OrderBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        return new ProjectPaymentHistoryPage(
            page,
            pageSize,
            total,
            rows.Select(Map).ToArray());
    }

    public async Task<PaymentHistoryItem?> FindAsync(
        Guid projectId,
        Guid actorId,
        Guid paymentId,
        CancellationToken cancellationToken = default)
    {
        var order = await AuthorizedOrders(
                projectId, actorId)
            .SingleOrDefaultAsync(
                x => x.Id == paymentId,
                cancellationToken);

        return order is null ? null : Map(order);
    }

    private IQueryable<ProjectPaymentOrder> AuthorizedOrders(
        Guid projectId,
        Guid actorId)
    {
        return db.ProjectPaymentOrders
            .AsNoTracking()
            .Where(x =>
                x.ProjectId == projectId &&
                x.PayerId == actorId &&
                db.Projects.Any(project =>
                    project.Id == x.ProjectId &&
                    project.OwnerId == actorId));
    }

    private static PaymentHistoryItem Map(
        ProjectPaymentOrder order)
    {
        return new PaymentHistoryItem(
            order.Id,
            order.ProjectId,
            order.OrderReference,
            order.Amount,
            order.Currency,
            order.Status.ToString().ToLowerInvariant(),
            order.CreatedAt,
            order.UpdatedAt,
            order.IsSettled);
    }
}
