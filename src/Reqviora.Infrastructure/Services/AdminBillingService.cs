using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.Billing;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class AdminBillingService(
    ReqvioraDbContext db) : IAdminBillingService
{
    public async Task<AdminPaymentPage> ListAsync(
        int page,
        int pageSize,
        Guid? projectId = null,
        string? status = null,
        string? currency = null,
        CancellationToken cancellationToken = default)
    {
        if (page is < 1 or > 1000000 ||
            pageSize is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page));
        }

        var query = db.ProjectPaymentOrders
            .AsNoTracking()
            .AsQueryable();

        if (projectId.HasValue)
        {
            query = query.Where(
                x => x.ProjectId == projectId.Value);
        }

        if (status is not null)
        {
            var parsed = status.ToLowerInvariant() switch
            {
                "pending" => ProjectPaymentStatus.Pending,
                "succeeded" => ProjectPaymentStatus.Succeeded,
                "failed" => ProjectPaymentStatus.Failed,
                "cancelled" => ProjectPaymentStatus.Cancelled,
                "chargedback" => ProjectPaymentStatus.ChargedBack,
                _ => throw new ArgumentException(
                    "Invalid payment status.", nameof(status))
            };

            query = query.Where(
                x => x.Status == parsed);
        }

        if (currency is not null)
        {
            if (currency is not ("USD" or "LKR"))
                throw new ArgumentException(
                    "Unsupported currency.", nameof(currency));

            query = query.Where(
                x => x.Currency == currency);
        }

        var total = await query.CountAsync(
            cancellationToken);

        var orders = await query
            .OrderBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        return new(
            page,
            pageSize,
            total,
            orders.Select(Map).ToArray());
    }

    public async Task<AdminPaymentItem?> FindAsync(
        Guid paymentId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.ProjectPaymentOrders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == paymentId,
                cancellationToken);

        return order is null ? null : Map(order);
    }

    public async Task<IReadOnlyList<AdminBillingSummary>>
        SummaryAsync(
            CancellationToken cancellationToken = default)
    {
        var groups = await db.ProjectPaymentOrders
            .AsNoTracking()
            .GroupBy(x => new
            {
                x.Currency,
                x.Status
            })
            .Select(group => new
            {
                group.Key.Currency,
                group.Key.Status,
                Count = group.Count(),
                Amount = group.Sum(x => x.Amount)
            })
            .ToListAsync(cancellationToken);

        return groups.GroupBy(x => x.Currency)
            .OrderBy(x => x.Key)
            .Select(currency => new AdminBillingSummary(
                currency.Key,
                currency.Sum(x => x.Count),
                currency.Where(x =>
                    x.Status == ProjectPaymentStatus.Succeeded)
                    .Sum(x => x.Count),
                currency.Where(x =>
                    x.Status == ProjectPaymentStatus.Pending)
                    .Sum(x => x.Count),
                currency.Where(x =>
                    x.Status == ProjectPaymentStatus.Failed)
                    .Sum(x => x.Count),
                currency.Where(x =>
                    x.Status == ProjectPaymentStatus.Cancelled)
                    .Sum(x => x.Count),
                currency.Where(x =>
                    x.Status == ProjectPaymentStatus.ChargedBack)
                    .Sum(x => x.Count),
                currency.Where(x =>
                    x.Status == ProjectPaymentStatus.Succeeded)
                    .Sum(x => x.Amount),
                currency.Where(x =>
                    x.Status == ProjectPaymentStatus.ChargedBack)
                    .Sum(x => x.Amount)))
            .ToArray();
    }

    private static AdminPaymentItem Map(
        ProjectPaymentOrder order)
    {
        return new(
            order.Id,
            order.ProjectId,
            order.PayerId,
            order.OrderReference,
            order.Amount,
            order.Currency,
            order.Status.ToString().ToLowerInvariant(),
            order.CreatedAt,
            order.UpdatedAt);
    }
}
