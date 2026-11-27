using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.Billing;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class PaymentRefundService(
    ReqvioraDbContext db,
    TimeProvider clock) : IPaymentRefundService
{
    private IQueryable<ProjectPaymentOrder> Owned(
        Guid projectId, Guid paymentId, Guid actorId) =>
        db.ProjectPaymentOrders.AsNoTracking().Where(x =>
            x.Id == paymentId &&
            x.ProjectId == projectId &&
            x.PayerId == actorId &&
            db.Projects.Any(p =>
                p.Id == projectId && p.OwnerId == actorId));

    public async Task<RefundResult> RequestAsync(
        Guid projectId, Guid paymentId, Guid actorId,
        string reason, CancellationToken ct = default)
    {
        if (!PaymentRefundRequest.ValidReason(reason))
            return new("invalid_request");

        var order = await Owned(projectId, paymentId, actorId)
            .SingleOrDefaultAsync(ct);

        if (order is null)
            return new("not_found");

        var existing = await db.PaymentRefundRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.PaymentId == paymentId, ct);

        if (existing is not null)
            return new("already_requested", Map(existing));

        if (!order.IsSettled)
            return new("not_eligible");

        var item = PaymentRefundRequest.Create(
            paymentId, actorId, reason, clock.GetUtcNow());

        db.PaymentRefundRequests.Add(item);

        try
        {
            await db.SaveChangesAsync(ct);
            return new("created", Map(item));
        }
        catch (DbUpdateException)
        {
            db.Entry(item).State = EntityState.Detached;
            return new("retry_required");
        }
    }

    public async Task<RefundRequestItem?> FindAsync(
        Guid projectId, Guid paymentId, Guid actorId,
        CancellationToken ct = default)
    {
        if (!await Owned(projectId, paymentId, actorId).AnyAsync(ct))
            return null;

        var item = await db.PaymentRefundRequests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.PaymentId == paymentId, ct);

        return item is null ? null : Map(item);
    }

    public async Task<RefundRequestPage> ListAdminAsync(
        int page, int pageSize,
        CancellationToken ct = default)
    {
        if (page is < 1 or > 1000000 ||
            pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(page));

        var query = db.PaymentRefundRequests.AsNoTracking();
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.PaymentId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize).ToArrayAsync(ct);

        return new(page, pageSize, total, rows.Select(Map).ToArray());
    }

    public async Task<RefundResult> ReviewAsync(
        Guid paymentId, Guid actorId, string decision,
        CancellationToken ct = default)
    {
        if (actorId == Guid.Empty ||
            decision is not ("reviewed" or "rejected"))
            return new("invalid_request");

        var item = await db.PaymentRefundRequests
            .SingleOrDefaultAsync(x => x.PaymentId == paymentId, ct);

        if (item is null)
            return new("not_found");

        if (item.Status != "requested")
            return new("already_reviewed", Map(item));

        if (decision == "reviewed" &&
            !await db.ProjectPaymentOrders.AnyAsync(x =>
                x.Id == paymentId &&
                x.Status == ProjectPaymentStatus.Succeeded, ct))
            return new("not_eligible");

        if (!item.Review(actorId, decision, clock.GetUtcNow()))
            return new("already_reviewed", Map(item));

        try
        {
            await db.SaveChangesAsync(ct);
            return new("review_completed", Map(item));
        }
        catch (DbUpdateException)
        {
            db.Entry(item).State = EntityState.Detached;
            return new("retry_required");
        }
    }

    private static RefundRequestItem Map(PaymentRefundRequest item) =>
        new(item.PaymentId, item.Reason, item.Status,
            item.RequestedAt, item.ReviewedAt);
}
