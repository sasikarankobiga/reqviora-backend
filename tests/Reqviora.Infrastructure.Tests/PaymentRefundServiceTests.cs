using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;
using Xunit;

namespace Reqviora.Infrastructure.Tests;

public sealed class PaymentRefundServiceTests
{
    [Fact]
    public async Task Refund_review_never_executes_a_payment_refund()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ReqvioraDbContext>()
            .UseSqlite(connection).Options;

        await using var db = new ReqvioraDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var owner = new User
        {
            Email = "owner@example.invalid",
            NormalizedEmail = "OWNER@EXAMPLE.INVALID",
            DisplayName = "Owner",
            PasswordHash = "fixture"
        };

        var admin = new User
        {
            Email = "reviewer@example.invalid",
            NormalizedEmail = "REVIEWER@EXAMPLE.INVALID",
            DisplayName = "Reviewer",
            PasswordHash = "fixture"
        };

        var project = new Project
        {
            OwnerId = owner.Id,
            Name = "Refund project"
        };

        db.AddRange(owner, admin, project);
        await db.SaveChangesAsync();

        var order = ProjectPaymentOrder.Create(
            project.Id, owner.Id, 9.99m,
            "USD", DateTimeOffset.UtcNow);

        db.ProjectPaymentOrders.Add(order);
        await db.SaveChangesAsync();

        var service = new PaymentRefundService(db, TimeProvider.System);

        var pending = await service.RequestAsync(
            project.Id, order.Id, owner.Id, "service_issue");
        Assert.Equal("not_eligible", pending.Code);

        order.ApplyVerifiedNotification(
            order.OrderReference, "REFUND-TEST-001",
            order.Amount, order.Currency,
            ProjectPaymentStatus.Succeeded, DateTimeOffset.UtcNow);

        await db.SaveChangesAsync();

        var unauthorized = await service.RequestAsync(
            project.Id, order.Id, admin.Id, "service_issue");
        Assert.Equal("not_found", unauthorized.Code);

        var created = await service.RequestAsync(
            project.Id, order.Id, owner.Id, "service_issue");
        Assert.Equal("created", created.Code);
        Assert.NotNull(created.Item);
        Assert.False(created.Item.RefundExecuted);

        var duplicate = await service.RequestAsync(
            project.Id, order.Id, owner.Id, "other");
        Assert.Equal("already_requested", duplicate.Code);

        Assert.Null(await service.FindAsync(
            project.Id, order.Id, admin.Id));

        Assert.Null(await service.FindAsync(
            Guid.NewGuid(), order.Id, owner.Id));

        var page = await service.ListAdminAsync(1, 20);
        Assert.Single(page.Items);

        var review = await service.ReviewAsync(
            order.Id, admin.Id, "reviewed");

        Assert.Equal("review_completed", review.Code);
        Assert.NotNull(review.Item);
        Assert.False(review.Item.RefundExecuted);

        var stored = await db.PaymentRefundRequests
            .AsNoTracking().SingleAsync();

        Assert.Equal(admin.Id, stored.ReviewedBy);
        Assert.NotNull(stored.ReviewedAt);

        var repeat = await service.ReviewAsync(
            order.Id, admin.Id, "rejected");

        Assert.Equal("already_reviewed", repeat.Code);
        Assert.True(order.IsSettled);
        Assert.Equal(1, await db.PaymentRefundRequests.CountAsync());
    }
}
