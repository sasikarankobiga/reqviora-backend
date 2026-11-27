using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.Billing;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class PaymentNotificationService(
    ReqvioraDbContext db,
    TimeProvider clock) : IPaymentNotificationService
{
    public async Task<ProjectPaymentNotificationResult> ApplyAsync(
        VerifiedPaymentNotification notification,
        CancellationToken cancellationToken = default)
    {
        var order = await db.ProjectPaymentOrders
            .SingleOrDefaultAsync(
                x => x.Id == notification.OrderId,
                cancellationToken);

        if (order is null)
            return ProjectPaymentNotificationResult.OrderNotFound;

        if (order.Amount != notification.Amount ||
            order.Currency != notification.Currency)
        {
            return ProjectPaymentNotificationResult.PaymentMismatch;
        }

        var referenceUsed = await db.ProjectPaymentOrders
            .AsNoTracking()
            .AnyAsync(
                x => x.Id != notification.OrderId &&
                     x.GatewayPaymentId ==
                         notification.GatewayPaymentId,
                cancellationToken);

        if (referenceUsed)
            return ProjectPaymentNotificationResult.PaymentMismatch;

        bool changed;

        try
        {
            changed = order.ApplyVerifiedNotification(
                notification.OrderId.ToString("N"),
                notification.GatewayPaymentId,
                notification.Amount,
                notification.Currency,
                notification.Status,
                clock.GetUtcNow());
        }
        catch (InvalidOperationException)
        {
            return ProjectPaymentNotificationResult.PaymentMismatch;
        }
        catch (ArgumentException)
        {
            return ProjectPaymentNotificationResult.PaymentMismatch;
        }

        if (!changed)
            return ProjectPaymentNotificationResult.DuplicateOrStale;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return ProjectPaymentNotificationResult.Applied;
        }
        catch (DbUpdateException)
        {
            // Avoid accidentally persisting a failed change
            // through another request-scoped component.
            db.Entry(order).State = EntityState.Detached;

            return ProjectPaymentNotificationResult.RetryRequired;
        }
    }
}
