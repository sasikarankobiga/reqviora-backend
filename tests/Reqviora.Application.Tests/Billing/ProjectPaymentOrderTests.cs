using Reqviora.Domain.Entities;
using Xunit;

namespace Reqviora.Application.Tests.Billing;

public sealed class ProjectPaymentOrderTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.UnixEpoch;

    private static ProjectPaymentOrder Create() =>
        ProjectPaymentOrder.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            9.99m,
            "USD",
            Now);

    private static bool Apply(
        ProjectPaymentOrder order,
        ProjectPaymentStatus status)
    {
        return order.ApplyVerifiedNotification(
            order.OrderReference,
            "TEST-PAYMENT-001",
            order.Amount,
            order.Currency,
            status,
            Now.AddMinutes(1));
    }

    [Fact]
    public void New_order_is_pending_and_not_settled()
    {
        var order = Create();

        Assert.Equal(9.99m, order.Amount);
        Assert.Equal("USD", order.Currency);
        Assert.Equal(ProjectPaymentStatus.Pending, order.Status);
        Assert.False(order.IsSettled);
        Assert.NotEqual(Guid.Empty, order.ProjectId);
        Assert.NotEqual(Guid.Empty, order.PayerId);
    }

    [Fact]
    public void Verified_success_settles_order()
    {
        var order = Create();

        Assert.True(Apply(
            order, ProjectPaymentStatus.Succeeded));

        Assert.True(order.IsSettled);
    }

    [Fact]
    public void Duplicate_notification_is_ignored()
    {
        var order = Create();

        Assert.True(Apply(
            order, ProjectPaymentStatus.Succeeded));

        var token = order.ConcurrencyToken;

        Assert.False(Apply(
            order, ProjectPaymentStatus.Succeeded));

        Assert.Equal(token, order.ConcurrencyToken);
    }

    [Fact]
    public void Pending_notification_cannot_settle_order()
    {
        var order = Create();

        Apply(order, ProjectPaymentStatus.Pending);

        Assert.False(order.IsSettled);
    }

    [Fact]
    public void Failed_payment_does_not_settle_order()
    {
        var order = Create();

        Apply(order, ProjectPaymentStatus.Failed);

        Assert.False(order.IsSettled);
    }

    [Fact]
    public void Cancelled_payment_does_not_settle_order()
    {
        var order = Create();

        Apply(order, ProjectPaymentStatus.Cancelled);

        Assert.False(order.IsSettled);
    }

    [Fact]
    public void Chargeback_removes_settled_status()
    {
        var order = Create();

        Apply(order, ProjectPaymentStatus.Succeeded);
        Apply(order, ProjectPaymentStatus.ChargedBack);

        Assert.False(order.IsSettled);
        Assert.Equal(
            ProjectPaymentStatus.ChargedBack, order.Status);
    }

    [Fact]
    public void Chargeback_cannot_be_reversed_by_late_success()
    {
        var order = Create();

        Apply(order, ProjectPaymentStatus.ChargedBack);

        Assert.False(Apply(
            order, ProjectPaymentStatus.Succeeded));

        Assert.False(order.IsSettled);
    }

    [Fact]
    public void Late_failure_cannot_overwrite_success()
    {
        var order = Create();

        Apply(order, ProjectPaymentStatus.Succeeded);

        Assert.False(Apply(
            order, ProjectPaymentStatus.Failed));

        Assert.True(order.IsSettled);
    }

    [Fact]
    public void Amount_tampering_is_rejected()
    {
        var order = Create();

        Assert.Throws<InvalidOperationException>(() =>
            order.ApplyVerifiedNotification(
                order.OrderReference,
                "TEST-PAYMENT-001",
                0.01m,
                "USD",
                ProjectPaymentStatus.Succeeded,
                Now));

        Assert.False(order.IsSettled);
    }

    [Fact]
    public void Currency_tampering_is_rejected()
    {
        var order = Create();

        Assert.Throws<InvalidOperationException>(() =>
            order.ApplyVerifiedNotification(
                order.OrderReference,
                "TEST-PAYMENT-001",
                9.99m,
                "LKR",
                ProjectPaymentStatus.Succeeded,
                Now));

        Assert.False(order.IsSettled);
    }

    [Fact]
    public void Different_order_reference_is_rejected()
    {
        var order = Create();

        Assert.Throws<InvalidOperationException>(() =>
            order.ApplyVerifiedNotification(
                "WRONG-ORDER",
                "TEST-PAYMENT-001",
                9.99m,
                "USD",
                ProjectPaymentStatus.Succeeded,
                Now));
    }

    [Fact]
    public void Different_gateway_reference_is_rejected()
    {
        var order = Create();

        Apply(order, ProjectPaymentStatus.Succeeded);

        Assert.Throws<InvalidOperationException>(() =>
            order.ApplyVerifiedNotification(
                order.OrderReference,
                "OTHER-PAYMENT",
                9.99m,
                "USD",
                ProjectPaymentStatus.Succeeded,
                Now));
    }

    [Fact]
    public void Invalid_amount_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProjectPaymentOrder.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                0m,
                "USD",
                Now));
    }

    [Fact]
    public void Unsupported_currency_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            ProjectPaymentOrder.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                9.99m,
                "XYZ",
                Now));
    }
}
