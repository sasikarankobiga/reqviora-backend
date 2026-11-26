using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Reqviora.Application.Features.Billing;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;
using Xunit;

namespace Reqviora.Infrastructure.Tests;

public sealed class ProjectCheckoutOrdersTests
{
    [Fact]
    public async Task Owner_can_create_only_one_pending_order()
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
            DisplayName = "Test Owner",
            PasswordHash = "fixture",
            EmailVerified = true
        };

        var project = new Project
        {
            OwnerId = owner.Id,
            Name = "Billing test project"
        };

        db.AddRange(owner, project);
        await db.SaveChangesAsync();

        var service =
            new ProjectCheckoutOrders(db, TimeProvider.System);

        var first = await service.CreateAsync(
            project.Id, owner.Id, 9.99m, "USD");

        Assert.Equal(
            CheckoutOrderResultCode.Created,
            first.Code);

        Assert.NotNull(first.Order);
        Assert.Equal(9.99m, first.Order.Amount);
        Assert.Equal(owner.Id, first.Order.PayerId);

        var duplicate = await service.CreateAsync(
            project.Id, owner.Id, 9.99m, "USD");

        Assert.Equal(
            CheckoutOrderResultCode.PendingPayment,
            duplicate.Code);

        Assert.Equal(
            1, await db.ProjectPaymentOrders.CountAsync());

        var unauthorized = await service.CreateAsync(
            project.Id, Guid.NewGuid(), 9.99m, "USD");

        Assert.Equal(
            CheckoutOrderResultCode.NotFound,
            unauthorized.Code);

        first.Order.ApplyVerifiedNotification(
            first.Order.OrderReference,
            "TEST-CHECKOUT-001",
            9.99m,
            "USD",
            ProjectPaymentStatus.Succeeded,
            DateTimeOffset.UtcNow);

        await db.SaveChangesAsync();

        var alreadyPaid = await service.CreateAsync(
            project.Id, owner.Id, 9.99m, "USD");

        Assert.Equal(
            CheckoutOrderResultCode.AlreadyPaid,
            alreadyPaid.Code);

        Assert.Equal(
            1, await db.ProjectPaymentOrders.CountAsync());
    }

    [Fact]
    public async Task Unverified_owner_cannot_create_order()
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
            Email = "unverified@example.invalid",
            NormalizedEmail = "UNVERIFIED@EXAMPLE.INVALID",
            DisplayName = "Unverified Owner",
            PasswordHash = "fixture",
            EmailVerified = false
        };

        var project = new Project
        {
            OwnerId = owner.Id,
            Name = "Private project"
        };

        db.AddRange(owner, project);
        await db.SaveChangesAsync();

        var service =
            new ProjectCheckoutOrders(db, TimeProvider.System);

        var result = await service.CreateAsync(
            project.Id, owner.Id, 9.99m, "USD");

        Assert.Equal(
            CheckoutOrderResultCode.NotEligible,
            result.Code);

        Assert.Empty(await db.ProjectPaymentOrders.ToListAsync());
    }
}
