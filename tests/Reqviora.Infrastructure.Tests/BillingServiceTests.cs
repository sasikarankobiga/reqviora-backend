using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Reqviora.Application.Features.Billing;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;
using Xunit;

namespace Reqviora.Infrastructure.Tests;

public sealed class BillingServiceTests
{
    [Fact]
    public async Task Checkout_uses_server_price_and_never_accepts_client_amount()
    {
        await using var fixture = await BillingFixture.CreateAsync();
        var result = await fixture.Service.CreateCheckoutAsync(fixture.Project.Id, fixture.Owner.Id,
            Contact(), default);

        Assert.Equal("125.00", result.Fields["amount"]);
        Assert.Equal("USD", result.Fields["currency"]);
        Assert.Equal("https://sandbox.payhere.lk/pay/checkout", result.ActionUrl);
        Assert.Equal(12_500, (await fixture.Db.PaymentOrders.SingleAsync()).AmountMinor);
    }

    [Fact]
    public async Task Checkout_rejects_cross_project_user_and_disabled_gateway()
    {
        await using var fixture = await BillingFixture.CreateAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.CreateCheckoutAsync(
            fixture.Project.Id, fixture.Other.Id, Contact(), default));

        await using var disabled = await BillingFixture.CreateAsync(enabled: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => disabled.Service.CreateCheckoutAsync(
            disabled.Project.Id, disabled.Owner.Id, Contact(), default));
    }

    [Fact]
    public async Task Administrator_can_replace_project_price_without_changing_checkout_contract()
    {
        await using var fixture = await BillingFixture.CreateAsync();
        var first = await fixture.Service.SetProjectPriceAsync(
            fixture.Project.Id, 9_999, fixture.Admin.Id, default);
        var second = await fixture.Service.SetProjectPriceAsync(
            fixture.Project.Id, 10_500, fixture.Admin.Id, default);
        var checkout = await fixture.Service.CreateCheckoutAsync(
            fixture.Project.Id, fixture.Owner.Id, Contact(), default);

        Assert.Equal("99.99", first.DisplayAmount);
        Assert.Equal("105.00", second.DisplayAmount);
        Assert.Equal("105.00", checkout.Fields["amount"]);
        Assert.Equal(2, await fixture.Db.ProjectPricing.CountAsync());
        Assert.Single(await fixture.Db.ProjectPricing.Where(x => x.IsActive).ToArrayAsync());
    }

    [Fact]
    public async Task Verified_success_is_idempotent_and_atomically_grants_entitlement()
    {
        await using var fixture = await BillingFixture.CreateAsync();
        var checkout = await fixture.Service.CreateCheckoutAsync(fixture.Project.Id, fixture.Owner.Id, Contact(), default);
        var notification = fixture.Notification(checkout.OrderId, "PAY-100", "125.00", "2");

        var first = await fixture.Service.ProcessNotificationAsync(notification, default);
        var duplicate = await fixture.Service.ProcessNotificationAsync(notification, default);

        Assert.Equal(PaymentStatuses.Succeeded, first.Status);
        Assert.False(first.Duplicate);
        Assert.True(duplicate.Duplicate);
        Assert.Equal(1, await fixture.Db.PaymentTransactions.CountAsync());
        Assert.Equal(1, await fixture.Db.ProjectEntitlements.CountAsync(x => x.Status == "Active"));
        Assert.Contains(await fixture.Db.PaymentAuditRecords.ToArrayAsync(), x => x.Action == "NotificationProcessed");
    }

    [Theory]
    [InlineData("0", "Processing")]
    [InlineData("-1", "Cancelled")]
    [InlineData("-2", "Failed")]
    public async Task Provider_statuses_do_not_grant_entitlement(string code, string expected)
    {
        await using var fixture = await BillingFixture.CreateAsync();
        var checkout = await fixture.Service.CreateCheckoutAsync(fixture.Project.Id, fixture.Owner.Id, Contact(), default);
        var result = await fixture.Service.ProcessNotificationAsync(
            fixture.Notification(checkout.OrderId, $"PAY-{code}", "125.00", code), default);
        Assert.Equal(expected, result.Status);
        Assert.Empty(await fixture.Db.ProjectEntitlements.ToArrayAsync());
    }

    [Fact]
    public async Task Invalid_signature_amount_and_currency_never_create_entitlement()
    {
        await using var fixture = await BillingFixture.CreateAsync();
        var checkout = await fixture.Service.CreateCheckoutAsync(fixture.Project.Id, fixture.Owner.Id, Contact(), default);
        var valid = fixture.Notification(checkout.OrderId, "PAY-101", "125.00", "2");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ProcessNotificationAsync(
            valid with { Signature = new string('0', 32) }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.ProcessNotificationAsync(
            fixture.Notification(checkout.OrderId, "PAY-102", "124.99", "2"), default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.ProcessNotificationAsync(
            fixture.Notification(checkout.OrderId, "PAY-103", "125.00", "2", "LKR"), default));
        Assert.Empty(await fixture.Db.ProjectEntitlements.ToArrayAsync());
    }

    [Fact]
    public async Task Merchant_mismatch_and_unknown_order_are_rejected()
    {
        await using var fixture = await BillingFixture.CreateAsync();
        var checkout = await fixture.Service.CreateCheckoutAsync(fixture.Project.Id, fixture.Owner.Id, Contact(), default);
        var valid = fixture.Notification(checkout.OrderId, "PAY-UNKNOWN", "125.00", "2");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ProcessNotificationAsync(
            valid with { MerchantId = "another-merchant" }, default));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.ProcessNotificationAsync(
            fixture.Notification("REQ-DOES-NOT-EXIST", "PAY-UNKNOWN", "125.00", "2"), default));
    }

    [Fact]
    public async Task Payment_details_are_isolated_from_unrelated_users()
    {
        await using var fixture = await BillingFixture.CreateAsync();
        var checkout = await fixture.Service.CreateCheckoutAsync(fixture.Project.Id, fixture.Owner.Id, Contact(), default);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.GetPaymentAsync(
            checkout.PaymentId, fixture.Other.Id, false, default));
    }

    [Fact]
    public async Task Successful_payment_cannot_be_downgraded_by_late_failure()
    {
        await using var fixture = await BillingFixture.CreateAsync();
        var checkout = await fixture.Service.CreateCheckoutAsync(fixture.Project.Id, fixture.Owner.Id, Contact(), default);
        await fixture.Service.ProcessNotificationAsync(
            fixture.Notification(checkout.OrderId, "PAY-FINAL", "125.00", "2"), default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ProcessNotificationAsync(
            fixture.Notification(checkout.OrderId, "PAY-FINAL", "125.00", "-2"), default));
        Assert.Equal(PaymentStatuses.Succeeded, (await fixture.Db.PaymentOrders.SingleAsync()).Status);
        Assert.Equal("Active", (await fixture.Db.ProjectEntitlements.SingleAsync()).Status);
    }

    [Fact]
    public async Task Chargeback_revokes_entitlement_and_refund_is_admin_only_service_operation()
    {
        await using var fixture = await BillingFixture.CreateAsync();
        var checkout = await fixture.Service.CreateCheckoutAsync(fixture.Project.Id, fixture.Owner.Id, Contact(), default);
        await fixture.Service.ProcessNotificationAsync(fixture.Notification(checkout.OrderId, "PAY-104", "125.00", "2"), default);
        await fixture.Service.ProcessNotificationAsync(fixture.Notification(checkout.OrderId, "PAY-104", "125.00", "-3"), default);
        Assert.Equal("Revoked", (await fixture.Db.ProjectEntitlements.SingleAsync()).Status);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.RequestRefundAsync(
            checkout.PaymentId, "Customer request", fixture.Admin.Id, default));
    }

    [Fact]
    public async Task Successful_refund_is_persisted_and_revokes_entitlement()
    {
        await using var fixture = await BillingFixture.CreateAsync();
        var checkout = await fixture.Service.CreateCheckoutAsync(fixture.Project.Id, fixture.Owner.Id, Contact(), default);
        await fixture.Service.ProcessNotificationAsync(fixture.Notification(checkout.OrderId, "PAY-105", "125.00", "2"), default);

        var result = await fixture.Service.RequestRefundAsync(checkout.PaymentId, "Approved refund",
            fixture.Admin.Id, default);

        Assert.Equal(PaymentStatuses.Refunded, result.Status);
        Assert.Equal("Succeeded", (await fixture.Db.PaymentRefunds.SingleAsync()).Status);
        Assert.Equal("Revoked", (await fixture.Db.ProjectEntitlements.SingleAsync()).Status);
    }

    [Fact]
    public async Task Failed_refund_is_audited_and_can_be_safely_retried()
    {
        var gateway = new FailOnceRefundGateway();
        await using var fixture = await BillingFixture.CreateAsync(refundGateway: gateway);
        var checkout = await fixture.Service.CreateCheckoutAsync(fixture.Project.Id, fixture.Owner.Id, Contact(), default);
        await fixture.Service.ProcessNotificationAsync(
            fixture.Notification(checkout.OrderId, "PAY-RETRY", "125.00", "2"), default);

        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Service.RequestRefundAsync(
            checkout.PaymentId, "Temporary provider failure", fixture.Admin.Id, default));
        Assert.Equal("Failed", (await fixture.Db.PaymentRefunds.SingleAsync()).Status);
        Assert.Equal("Active", (await fixture.Db.ProjectEntitlements.SingleAsync()).Status);

        var result = await fixture.Service.RequestRefundAsync(
            checkout.PaymentId, "Approved retry", fixture.Admin.Id, default);
        Assert.Equal(PaymentStatuses.Refunded, result.Status);
        Assert.Equal(1, await fixture.Db.PaymentRefunds.CountAsync());
        Assert.Equal(2, gateway.Calls);
    }

    private static BillingCheckoutContact Contact() => new("Test", "Owner", "owner@example.test",
        "+94770000000", "1 Test Road", "Colombo", "Sri Lanka");

    private sealed class BillingFixture : IAsyncDisposable
    {
        private const string MerchantId = "merchant-test";
        private const string Secret = "unit-test-merchant-secret";
        private readonly SqliteConnection _connection;
        public ReqvioraDbContext Db { get; }
        public BillingService Service { get; }
        public User Owner { get; }
        public User Other { get; }
        public User Admin { get; }
        public Project Project { get; }

        private BillingFixture(SqliteConnection connection, ReqvioraDbContext db, BillingService service,
            User owner, User other, User admin, Project project)
        { _connection = connection; Db = db; Service = service; Owner = owner; Other = other; Admin = admin; Project = project; }

        public static async Task<BillingFixture> CreateAsync(bool enabled = true,
            IPayHereRefundGateway? refundGateway = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ReqvioraDbContext(new DbContextOptionsBuilder<ReqvioraDbContext>()
                .UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var owner = User("owner@example.test"); var other = User("other@example.test");
            var admin = User("admin@example.test");
            var project = new Project { Name = "Paid project", OwnerId = owner.Id };
            db.Users.AddRange(owner, other, admin); db.Projects.Add(project); await db.SaveChangesAsync();
            var settings = new BillingOptions { Enabled = enabled, Sandbox = true, MerchantId = MerchantId,
                MerchantSecret = Secret, DefaultProjectPriceUsdMinor = 12_500,
                ReturnUrl = "https://app.example.test/payment/return",
                CancelUrl = "https://app.example.test/payment/cancel",
                NotifyUrl = "https://api.example.test/api/billing/payhere/notify", CheckoutExpiryMinutes = 30,
                RefundsEnabled = true, AppId = "configured", AppSecret = "configured" };
            var service = new BillingService(db, Options.Create(settings), TimeProvider.System,
                refundGateway ?? new FakeRefundGateway());
            return new(connection, db, service, owner, other, admin, project);
        }

        public PayHereNotification Notification(string order, string payment, string amount, string status,
            string currency = "USD")
        {
            var secretHash = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes(Secret)));
            var signature = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes(MerchantId + order + amount + currency + status + secretHash)));
            return new(MerchantId, order, payment, amount, currency, status, signature, "VISA", "test event");
        }

        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
        private static User User(string email) => new() { Email = email, NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = email, PasswordHash = "not-used", EmailVerified = true };
    }

    private sealed class FakeRefundGateway : IPayHereRefundGateway
    {
        public Task<string> RefundAsync(string paymentId, string reason, long amountMinor, CancellationToken ct) =>
            Task.FromResult("REFUND-1");
    }

    private sealed class FailOnceRefundGateway : IPayHereRefundGateway
    {
        public int Calls { get; private set; }
        public Task<string> RefundAsync(string paymentId, string reason, long amountMinor, CancellationToken ct)
        {
            Calls++;
            return Calls == 1 ? Task.FromException<string>(new HttpRequestException("Transient test failure")) :
                Task.FromResult("REFUND-RETRY");
        }
    }
}
