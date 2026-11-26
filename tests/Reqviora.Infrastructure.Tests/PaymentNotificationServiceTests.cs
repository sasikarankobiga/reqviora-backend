using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.Billing;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;
using Xunit;

namespace Reqviora.Infrastructure.Tests;

public sealed class PaymentNotificationServiceTests
{
    [Fact]
    public async Task Notification_is_persisted_and_replay_safe()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options =
            new DbContextOptionsBuilder<ReqvioraDbContext>()
                .UseSqlite(connection)
                .Options;

        await using var db = new ReqvioraDbContext(options);

        await db.Database.EnsureCreatedAsync();

        var owner = new User
        {
            Email = "owner@example.invalid",
            NormalizedEmail = "OWNER@EXAMPLE.INVALID",
            DisplayName = "Fixture Owner",
            PasswordHash = "fixture"
        };

        var project = new Project
        {
            Name = "Billing project",
            OwnerId = owner.Id
        };

        db.AddRange(owner, project);
        await db.SaveChangesAsync();

        var order = ProjectPaymentOrder.Create(
            project.Id,
            owner.Id,
            9.99m,
            "USD",
            DateTimeOffset.UtcNow);

        var another = ProjectPaymentOrder.Create(
            project.Id,
            owner.Id,
            9.99m,
            "USD",
            DateTimeOffset.UtcNow);

        db.ProjectPaymentOrders.AddRange(order, another);
        await db.SaveChangesAsync();

        var service = new PaymentNotificationService(
            db, TimeProvider.System);

        var notification = new VerifiedPaymentNotification(
            order.Id,
            "TEST-PAYMENT-001",
            9.99m,
            "USD",
            ProjectPaymentStatus.Succeeded);

        Assert.Equal(
            PaymentNotificationResult.PaymentMismatch,
            await service.ApplyAsync(
                notification with { Amount = 0.01m }));

        Assert.Equal(
            PaymentNotificationResult.PaymentMismatch,
            await service.ApplyAsync(
                notification with { Currency = "LKR" }));

        Assert.Equal(
            PaymentNotificationResult.OrderNotFound,
            await service.ApplyAsync(
                notification with { OrderId = Guid.NewGuid() }));

        Assert.Equal(
            PaymentNotificationResult.Applied,
            await service.ApplyAsync(notification));

        db.ChangeTracker.Clear();

        var stored = await db.ProjectPaymentOrders
            .SingleAsync(x => x.Id == order.Id);

        Assert.True(stored.IsSettled);

        var token = stored.ConcurrencyToken;

        Assert.Equal(
            PaymentNotificationResult.DuplicateOrStale,
            await service.ApplyAsync(notification));

        Assert.Equal(token, stored.ConcurrencyToken);

        Assert.Equal(
            PaymentNotificationResult.PaymentMismatch,
            await service.ApplyAsync(
                notification with { OrderId = another.Id }));

        Assert.Equal(
            PaymentNotificationResult.DuplicateOrStale,
            await service.ApplyAsync(notification with
            {
                Status = ProjectPaymentStatus.Failed
            }));

        Assert.True(stored.IsSettled);

        Assert.Equal(
            PaymentNotificationResult.Applied,
            await service.ApplyAsync(notification with
            {
                Status = ProjectPaymentStatus.ChargedBack
            }));

        Assert.False(stored.IsSettled);

        Assert.Equal(
            PaymentNotificationResult.DuplicateOrStale,
            await service.ApplyAsync(notification));

        db.ChangeTracker.Clear();

        stored = await db.ProjectPaymentOrders
            .SingleAsync(x => x.Id == order.Id);

        Assert.Equal(
            ProjectPaymentStatus.ChargedBack,
            stored.Status);
    }
}
