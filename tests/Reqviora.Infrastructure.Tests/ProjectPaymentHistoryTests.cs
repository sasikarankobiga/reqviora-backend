using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;
using Xunit;

namespace Reqviora.Infrastructure.Tests;

public sealed class ProjectPaymentHistoryTests
{
    [Fact]
    public async Task History_is_private_paginated_and_project_scoped()
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
            DisplayName = "Owner",
            PasswordHash = "fixture"
        };

        var stranger = new User
        {
            Email = "other@example.invalid",
            NormalizedEmail = "OTHER@EXAMPLE.INVALID",
            DisplayName = "Other user",
            PasswordHash = "fixture"
        };

        var first = new Project
        {
            OwnerId = owner.Id,
            Name = "First project"
        };

        var second = new Project
        {
            OwnerId = owner.Id,
            Name = "Second project"
        };

        db.AddRange(owner, stranger, first, second);
        await db.SaveChangesAsync();

        var paid = ProjectPaymentOrder.Create(
            first.Id,
            owner.Id,
            9.99m,
            "USD",
            DateTimeOffset.UtcNow);

        var pending = ProjectPaymentOrder.Create(
            first.Id,
            owner.Id,
            9.99m,
            "USD",
            DateTimeOffset.UtcNow);

        var unrelated = ProjectPaymentOrder.Create(
            second.Id,
            owner.Id,
            9.99m,
            "USD",
            DateTimeOffset.UtcNow);

        paid.ApplyVerifiedNotification(
            paid.OrderReference,
            "HISTORY-TEST-001",
            paid.Amount,
            paid.Currency,
            ProjectPaymentStatus.Succeeded,
            DateTimeOffset.UtcNow);

        db.ProjectPaymentOrders.AddRange(
            paid, pending, unrelated);

        await db.SaveChangesAsync();

        var history =
            new ProjectPaymentHistoryService(db);

        var page = await history.ListAsync(
            first.Id, owner.Id, 1, 1);

        Assert.NotNull(page);
        Assert.Equal(2, page.Total);
        Assert.Single(page.Items);

        var nextPage = await history.ListAsync(
            first.Id, owner.Id, 2, 1);

        Assert.NotNull(nextPage);
        Assert.Single(nextPage.Items);

        Assert.NotEqual(
            page.Items[0].PaymentId,
            nextPage.Items[0].PaymentId);

        Assert.Null(await history.ListAsync(
            first.Id, stranger.Id, 1, 20));

        Assert.Null(await history.FindAsync(
            first.Id, stranger.Id, paid.Id));

        Assert.Null(await history.FindAsync(
            second.Id, owner.Id, paid.Id));

        var successful = await history.FindAsync(
            first.Id, owner.Id, paid.Id);

        Assert.NotNull(successful);
        Assert.True(successful.ReceiptAvailable);
        Assert.Equal("succeeded", successful.Status);
        Assert.Equal(9.99m, successful.Amount);
        Assert.Equal("USD", successful.Currency);

        var awaitingPayment = await history.FindAsync(
            first.Id, owner.Id, pending.Id);

        Assert.NotNull(awaitingPayment);
        Assert.False(awaitingPayment.ReceiptAvailable);

        paid.ApplyVerifiedNotification(
            paid.OrderReference,
            "HISTORY-TEST-001",
            paid.Amount,
            paid.Currency,
            ProjectPaymentStatus.ChargedBack,
            DateTimeOffset.UtcNow);

        await db.SaveChangesAsync();

        var reversed = await history.FindAsync(
            first.Id, owner.Id, paid.Id);

        Assert.NotNull(reversed);
        Assert.Equal("chargedback", reversed.Status);
        Assert.False(reversed.ReceiptAvailable);
    }
}
