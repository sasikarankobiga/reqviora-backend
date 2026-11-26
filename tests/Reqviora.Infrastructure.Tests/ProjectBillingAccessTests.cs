using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;
using Xunit;

namespace Reqviora.Infrastructure.Tests;

public sealed class ProjectBillingAccessTests
{
    [Fact]
    public async Task Payment_is_project_bound_and_chargeback_removes_access()
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

        var first = new Project
        {
            Name = "First project",
            OwnerId = owner.Id
        };

        var second = new Project
        {
            Name = "Second project",
            OwnerId = owner.Id
        };

        db.AddRange(owner, first, second);

        await db.SaveChangesAsync();

        var access = new ProjectBillingAccessService(db);

        var initial = await access.ReadAsync(
            first.Id, owner.Id);

        Assert.NotNull(initial);
        Assert.False(initial.HasSuccessfulPayment);

        Assert.Null(await access.ReadAsync(
            first.Id, Guid.NewGuid()));

        var order = ProjectPaymentOrder.Create(
            first.Id,
            owner.Id,
            9.99m,
            "USD",
            DateTimeOffset.UtcNow);

        db.ProjectPaymentOrders.Add(order);

        await db.SaveChangesAsync();

        var pending = await access.ReadAsync(
            first.Id, owner.Id);

        Assert.NotNull(pending);
        Assert.True(pending.HasPendingPayment);
        Assert.False(pending.HasSuccessfulPayment);

        order.ApplyVerifiedNotification(
            order.OrderReference,
            "ENTITLEMENT-TEST-001",
            order.Amount,
            order.Currency,
            ProjectPaymentStatus.Succeeded,
            DateTimeOffset.UtcNow);

        await db.SaveChangesAsync();

        var paid = await access.ReadAsync(
            first.Id, owner.Id);

        Assert.NotNull(paid);
        Assert.True(paid.HasSuccessfulPayment);

        var unrelated = await access.ReadAsync(
            second.Id, owner.Id);

        Assert.NotNull(unrelated);
        Assert.False(unrelated.HasSuccessfulPayment);

        order.ApplyVerifiedNotification(
            order.OrderReference,
            "ENTITLEMENT-TEST-001",
            order.Amount,
            order.Currency,
            ProjectPaymentStatus.ChargedBack,
            DateTimeOffset.UtcNow);

        await db.SaveChangesAsync();

        var revoked = await access.ReadAsync(
            first.Id, owner.Id);

        Assert.NotNull(revoked);
        Assert.False(revoked.HasSuccessfulPayment);
    }
}
