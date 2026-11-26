using System.Data;
using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.Billing;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class ProjectCheckoutOrders(
    ReqvioraDbContext db,
    TimeProvider clock) : IProjectCheckoutOrders
{
    public async Task<CheckoutOrderResult> CreateAsync(
        Guid projectId,
        Guid actorId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken = default)
    {
        await using var transaction =
            await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var ownsProject = await db.Projects
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == projectId &&
                     x.OwnerId == actorId,
                cancellationToken);

        if (!ownsProject)
            return new(CheckoutOrderResultCode.NotFound);

        var user = await db.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == actorId,
                cancellationToken);

        if (user is null ||
            user.IsDisabled ||
            !user.EmailVerified ||
            string.IsNullOrWhiteSpace(user.Email))
        {
            return new(CheckoutOrderResultCode.NotEligible);
        }

        if (await db.ProjectPaymentOrders.AnyAsync(
            x => x.ProjectId == projectId &&
                 x.Status == ProjectPaymentStatus.Succeeded,
            cancellationToken))
        {
            return new(CheckoutOrderResultCode.AlreadyPaid);
        }

        if (await db.ProjectPaymentOrders.AnyAsync(
            x => x.ProjectId == projectId &&
                 x.Status == ProjectPaymentStatus.Pending,
            cancellationToken))
        {
            return new(CheckoutOrderResultCode.PendingPayment);
        }

        var order = ProjectPaymentOrder.Create(
            projectId,
            actorId,
            amount,
            currency,
            clock.GetUtcNow());

        db.ProjectPaymentOrders.Add(order);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new(
            CheckoutOrderResultCode.Created,
            order,
            user.Email);
    }
}
