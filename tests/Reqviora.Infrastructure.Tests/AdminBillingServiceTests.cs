using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;
using Xunit;

namespace Reqviora.Infrastructure.Tests;

public sealed class AdminBillingServiceTests
{
    [Fact]
    public async Task Billing_summary_separates_currencies_and_statuses()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        var options =
            new DbContextOptionsBuilder<ReqvioraDbContext>()
                .UseSqlite(connection)
                .Options;

        await using var db =
            new ReqvioraDbContext(options);

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
            Name = "Billing test",
            OwnerId = owner.Id
        };

        db.AddRange(owner, project);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;

        ProjectPaymentOrder Create(
            decimal amount,
            string currency,
            ProjectPaymentStatus status)
        {
            var order = ProjectPaymentOrder.Create(
                project.Id,
                owner.Id,
                amount,
                currency,
                now);

            if (status != ProjectPaymentStatus.Pending)
            {
                order.ApplyVerifiedNotification(
                    order.OrderReference,
                    Guid.NewGuid().ToString("N"),
                    amount,
                    currency,
                    status,
                    now);
            }

            return order;
        }

        var paidUsd = Create(
            9.99m, "USD",
            ProjectPaymentStatus.Succeeded);

        db.ProjectPaymentOrders.AddRange(
            paidUsd,
            Create(9.99m, "USD",
                ProjectPaymentStatus.Pending),
            Create(9.99m, "USD",
                ProjectPaymentStatus.Failed),
            Create(9.99m, "USD",
                ProjectPaymentStatus.Cancelled),
            Create(9.99m, "USD",
                ProjectPaymentStatus.ChargedBack),
            Create(3000m, "LKR",
                ProjectPaymentStatus.Succeeded));

        await db.SaveChangesAsync();

        var service = new AdminBillingService(db);

        var summary = await service.SummaryAsync();

        Assert.Equal(2, summary.Count);

        var usd = Assert.Single(summary,
            x => x.Currency == "USD");

        Assert.Equal(5, usd.TotalOrders);
        Assert.Equal(1, usd.SuccessfulOrders);
        Assert.Equal(1, usd.PendingOrders);
        Assert.Equal(1, usd.FailedOrders);
        Assert.Equal(1, usd.CancelledOrders);
        Assert.Equal(1, usd.ChargebackOrders);
        Assert.Equal(9.99m, usd.SuccessfulOrderAmount);
        Assert.Equal(9.99m, usd.ChargebackAmount);

        var lkr = Assert.Single(summary,
            x => x.Currency == "LKR");

        Assert.Equal(3000m, lkr.SuccessfulOrderAmount);

        var page = await service.ListAsync(
            1, 2);

        Assert.Equal(6, page.Total);
        Assert.Equal(2, page.Items.Count);

        var filtered = await service.ListAsync(
            1, 20, project.Id,
            "succeeded", "USD");

        Assert.Equal(1, filtered.Total);

        Assert.Equal(
            paidUsd.Id,
            Assert.Single(filtered.Items).PaymentId);

        var details = await service.FindAsync(
            paidUsd.Id);

        Assert.NotNull(details);
        Assert.Equal("succeeded", details.Status);
        Assert.Equal(owner.Id, details.PayerId);

        Assert.Null(await service.FindAsync(
            Guid.NewGuid()));

        var unrelated = await service.ListAsync(
            1, 20, Guid.NewGuid());

        Assert.Empty(unrelated.Items);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ListAsync(
                1, 20, status: "invalid"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ListAsync(
                1, 20, currency: "XYZ"));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.ListAsync(1, 101));
    }
}
