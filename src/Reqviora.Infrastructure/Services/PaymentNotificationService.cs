using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.Billing;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class PaymentNotificationService(
    ReqvioraDbContext db,
    TimeProvider clock) : IPaymentNotificationService
{
    public async Task<PaymentNotificationResult> ApplyAsync(
        VerifiedPaymentNotification notification,
        CancellationToken cancellationToken = default)
    {
        var order = await db.ProjectPaymentOrders
            .SingleOrDefaultAsync(
                x => x.Id == notification.OrderId,
                cancellationToken);

        if (order is null)
            return PaymentNotificationResult.OrderNotFound;

        if (order.Amount != notification.Amount ||
            order.Currency != notification.Currency)
        {
            return PaymentNotificationResult.PaymentMismatch;
        }

        var referenceUsed = await db.ProjectPaymentOrders
            .AsNoTracking()
            .AnyAsync(
                x => x.Id != notification.OrderId &&
                     x.GatewayPaymentId ==
                         notification.GatewayPaymentId,
                cancellationToken);

        if (referenceUsed)
            return PaymentNotificationResult.PaymentMismatch;

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
            return PaymentNotificationResult.PaymentMismatch;
        }
        catch (ArgumentException)
        {
            return PaymentNotificationResult.PaymentMismatch;
        }

        if (!changed)
            return PaymentNotificationResult.DuplicateOrStale;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return PaymentNotificationResult.Applied;
        }
        catch (DbUpdateException)
        {
            // Avoid accidentally persisting a failed change
            // through another request-scoped component.
            db.Entry(order).State = EntityState.Detached;

            return PaymentNotificationResult.RetryRequired;
        }
    }
}
