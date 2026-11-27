using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Reqviora.Application.Features.Billing;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;

namespace Reqviora.Infrastructure.Services;

public sealed class BillingService(ReqvioraDbContext db, IOptions<BillingOptions> options, TimeProvider clock,
    IPayHereRefundGateway refunds) : IBillingService
{
    private static readonly string[] FinalStatuses =
        [PaymentStatuses.Succeeded, PaymentStatuses.Refunded, PaymentStatuses.Disputed];
    private BillingOptions Settings => options.Value;

    public async Task<BillingPricingResponse> GetPricingAsync(Guid? projectId, Guid userId, CancellationToken ct)
    {
        if (projectId.HasValue) await RequireReadAsync(projectId.Value, userId, ct);
        var price = projectId.HasValue ? await ActivePriceAsync(projectId.Value, ct) : null;
        var amount = price?.AmountMinor ?? Settings.DefaultProjectPriceUsdMinor;
        return new(amount, "USD", Money(amount), GatewayConfigured() && amount > 0, price is not null);
    }

    public async Task<BillingCheckoutResponse> CreateCheckoutAsync(Guid projectId, Guid userId,
        BillingCheckoutContact contact, CancellationToken ct)
    {
        await RequireOwnerAsync(projectId, userId, ct);
        ValidateContact(contact);
        if (!GatewayConfigured()) throw new InvalidOperationException("PayHere checkout is not configured.");
        if (await db.ProjectEntitlements.AsNoTracking().AnyAsync(x => x.ProjectId == projectId && x.Status == "Active", ct))
            throw new InvalidOperationException("The project already has an active entitlement.");

        var price = await ActivePriceAsync(projectId, ct);
        var amountMinor = price?.AmountMinor ?? Settings.DefaultProjectPriceUsdMinor;
        if (amountMinor <= 0) throw new InvalidOperationException("Project pricing requires business approval.");
        var now = clock.GetUtcNow();
        var order = new PaymentOrder
        {
            OrderNumber = $"REQ-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..50].ToUpperInvariant(),
            ProjectId = projectId, RequestedBy = userId, AmountMinor = amountMinor, Currency = "USD",
            Status = PaymentStatuses.Pending, CreatedAt = now, UpdatedAt = now,
            ExpiresAt = now.AddMinutes(Settings.CheckoutExpiryMinutes)
        };
        db.PaymentOrders.Add(order);
        Audit(order.Id, userId, "CheckoutCreated", "Accepted", null, now);
        await db.SaveChangesAsync(ct);

        var amount = amountMinor / 100m;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["merchant_id"] = Settings.MerchantId,
            ["return_url"] = Settings.ReturnUrl,
            ["cancel_url"] = Settings.CancelUrl,
            ["notify_url"] = Settings.NotifyUrl,
            ["order_id"] = order.OrderNumber,
            ["items"] = "REQVIORA project entitlement",
            ["currency"] = "USD",
            ["amount"] = amount.ToString("0.00", CultureInfo.InvariantCulture),
            ["first_name"] = Clean(contact.FirstName, 100, nameof(contact.FirstName)),
            ["last_name"] = Clean(contact.LastName, 100, nameof(contact.LastName)),
            ["email"] = Clean(contact.Email, 320, nameof(contact.Email)),
            ["phone"] = Clean(contact.Phone, 40, nameof(contact.Phone)),
            ["address"] = Clean(contact.Address, 300, nameof(contact.Address)),
            ["city"] = Clean(contact.City, 100, nameof(contact.City)),
            ["country"] = Clean(contact.Country, 100, nameof(contact.Country)),
            ["custom_1"] = projectId.ToString("D"),
            ["hash"] = PayHereSecurity.CreateCheckoutHash(Settings.MerchantId, order.OrderNumber, amount,
                "USD", Settings.MerchantSecret)
        };
        return new(order.Id, order.OrderNumber, Settings.Sandbox
            ? "https://sandbox.payhere.lk/pay/checkout" : "https://www.payhere.lk/pay/checkout", fields, order.ExpiresAt);
    }

    public async Task<PaymentDetails> GetProjectStatusAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        await RequireReadAsync(projectId, userId, ct);
        var order = await db.PaymentOrders.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("No payment exists for the project.");
        return await MapAsync(order, ct);
    }

    public async Task<PaymentHistoryPage> GetProjectHistoryAsync(Guid projectId, Guid userId, int page, int pageSize,
        CancellationToken ct)
    {
        await RequireReadAsync(projectId, userId, ct);
        return await PageAsync(db.PaymentOrders.AsNoTracking().Where(x => x.ProjectId == projectId), page, pageSize, ct);
    }

    public async Task<PaymentDetails> GetPaymentAsync(Guid paymentId, Guid userId, bool administrator,
        CancellationToken ct)
    {
        var order = await db.PaymentOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == paymentId, ct)
            ?? throw new KeyNotFoundException("The payment was not found.");
        if (!administrator) await RequireReadAsync(order.ProjectId, userId, ct);
        return await MapAsync(order, ct);
    }

    public async Task<PaymentNotificationResult> ProcessNotificationAsync(PayHereNotification notification,
        CancellationToken ct)
    {
        if (!GatewayConfigured()) throw new InvalidOperationException("PayHere checkout is not configured.");
        if (!string.Equals(notification.MerchantId, Settings.MerchantId, StringComparison.Ordinal) ||
            !PayHereSecurity.VerifyNotification(notification.MerchantId, notification.OrderId, notification.Amount,
                notification.Currency, notification.StatusCode, notification.Signature, Settings.MerchantSecret))
            throw new UnauthorizedAccessException("The payment notification signature is invalid.");
        if (!TryMinor(notification.Amount, out var amountMinor))
            throw new ArgumentException("The payment amount is invalid.");

        var eventKey = Sha256(string.Join('|', notification.MerchantId, notification.OrderId,
            notification.PaymentId, notification.Amount, notification.Currency, notification.StatusCode,
            notification.Signature.ToUpperInvariant()));
        var prior = await db.PaymentTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.EventKey == eventKey, ct);
        if (prior is not null) return new(prior.PaymentOrderId, prior.Status, true);

        var order = await db.PaymentOrders.SingleOrDefaultAsync(x => x.OrderNumber == notification.OrderId, ct)
            ?? throw new KeyNotFoundException("The payment order was not found.");
        if (order.AmountMinor != amountMinor || order.Currency != notification.Currency || notification.Currency != "USD")
            throw new ArgumentException("The payment amount or currency does not match the order.");
        if (!string.IsNullOrWhiteSpace(order.ProviderPaymentId) && order.ProviderPaymentId != notification.PaymentId)
            throw new InvalidOperationException("The order is already associated with another provider payment.");

        var next = Status(notification.StatusCode);
        if (!CanTransition(order.Status, next))
            throw new InvalidOperationException("The payment status transition is not allowed.");
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        order.ProviderPaymentId ??= Clean(notification.PaymentId, 100, nameof(notification.PaymentId));
        order.Status = next;
        order.UpdatedAt = now;
        order.ConcurrencyToken = Guid.NewGuid();
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            PaymentOrderId = order.Id, ProviderPaymentId = order.ProviderPaymentId, EventKey = eventKey,
            Status = next, ProviderStatusCode = notification.StatusCode, AmountMinor = amountMinor,
            Currency = notification.Currency, PaymentMethod = Optional(notification.Method, 30),
            StatusMessage = Optional(notification.StatusMessage, 500), PayloadSha256 = eventKey, ReceivedAt = now
        });
        if (next == PaymentStatuses.Succeeded)
        {
            var entitlement = await db.ProjectEntitlements.SingleOrDefaultAsync(x => x.ProjectId == order.ProjectId, ct);
            if (entitlement is null)
                db.ProjectEntitlements.Add(new ProjectEntitlement
                    { ProjectId = order.ProjectId, PaymentOrderId = order.Id, GrantedAt = now, Status = "Active" });
            else if (entitlement.Status != "Active")
            {
                entitlement.Status = "Active"; entitlement.PaymentOrderId = order.Id;
                entitlement.GrantedAt = now; entitlement.RevokedAt = null;
            }
        }
        else if (next == PaymentStatuses.Disputed)
            await RevokeEntitlementAsync(order.ProjectId, now, ct);
        Audit(order.Id, null, "NotificationProcessed", next, notification.StatusCode, now);
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var duplicate = await db.PaymentTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.EventKey == eventKey, ct);
            if (duplicate is not null) return new(duplicate.PaymentOrderId, duplicate.Status, true);
            throw;
        }
        return new(order.Id, next, false);
    }

    public Task<PaymentHistoryPage> SearchAdminAsync(string? status, Guid? projectId, int page, int pageSize,
        CancellationToken ct)
    {
        var query = db.PaymentOrders.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == NormalizeStatus(status));
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId);
        return PageAsync(query, page, pageSize, ct);
    }

    public async Task<PaymentSummary> GetSummaryAsync(CancellationToken ct)
    {
        var rows = await db.PaymentOrders.AsNoTracking().GroupBy(_ => 1).Select(g => new
        {
            Pending = g.Count(x => x.Status == PaymentStatuses.Pending),
            Processing = g.Count(x => x.Status == PaymentStatuses.Processing),
            Succeeded = g.Count(x => x.Status == PaymentStatuses.Succeeded),
            Failed = g.Count(x => x.Status == PaymentStatuses.Failed),
            Cancelled = g.Count(x => x.Status == PaymentStatuses.Cancelled),
            Refunded = g.Count(x => x.Status == PaymentStatuses.Refunded),
            Disputed = g.Count(x => x.Status == PaymentStatuses.Disputed),
            Amount = g.Where(x => x.Status == PaymentStatuses.Succeeded).Sum(x => x.AmountMinor)
        }).SingleOrDefaultAsync(ct);
        return rows is null ? new(0, 0, 0, 0, 0, 0, 0, 0, "USD") :
            new(rows.Pending, rows.Processing, rows.Succeeded, rows.Failed, rows.Cancelled, rows.Refunded,
                rows.Disputed, rows.Amount, "USD");
    }

    public async Task<PaymentDetails> RequestRefundAsync(Guid paymentId, string reason, Guid administratorId,
        CancellationToken ct)
    {
        reason = Clean(reason, 500, nameof(reason));
        var order = await db.PaymentOrders.SingleOrDefaultAsync(x => x.Id == paymentId, ct)
            ?? throw new KeyNotFoundException("The payment was not found.");
        if (order.Status != PaymentStatuses.Succeeded || string.IsNullOrWhiteSpace(order.ProviderPaymentId))
            throw new InvalidOperationException("Only a verified successful payment can be refunded.");
        var refund = await db.PaymentRefunds.SingleOrDefaultAsync(x => x.PaymentOrderId == order.Id, ct);
        if (refund?.Status == "Succeeded") throw new InvalidOperationException("The payment has already been refunded.");
        if (refund?.Status == "Processing") throw new InvalidOperationException("A refund is already being processed.");
        var now = clock.GetUtcNow();
        if (refund is null)
        {
            refund = new PaymentRefund { PaymentOrderId = order.Id, AmountMinor = order.AmountMinor,
                Currency = order.Currency, Reason = reason, RequestedBy = administratorId, RequestedAt = now };
            db.PaymentRefunds.Add(refund);
        }
        else
        {
            refund.Reason = reason; refund.RequestedBy = administratorId; refund.RequestedAt = now;
            refund.CompletedAt = null; refund.ProviderRefundId = null;
        }
        refund.Status = "Processing";
        await db.SaveChangesAsync(ct);
        try
        {
            refund.ProviderRefundId = await refunds.RefundAsync(order.ProviderPaymentId, reason, order.AmountMinor, ct);
            refund.Status = "Succeeded"; refund.CompletedAt = clock.GetUtcNow();
            order.Status = PaymentStatuses.Refunded; order.UpdatedAt = refund.CompletedAt.Value;
            await RevokeEntitlementAsync(order.ProjectId, refund.CompletedAt.Value, ct);
            Audit(order.Id, administratorId, "RefundCompleted", "Succeeded", null, refund.CompletedAt.Value);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            refund.Status = "Failed"; refund.CompletedAt = clock.GetUtcNow();
            Audit(order.Id, administratorId, "RefundCompleted", "Failed", null, refund.CompletedAt.Value);
            await db.SaveChangesAsync(ct); throw;
        }
        return await MapAsync(order, ct);
    }

    public async Task<BillingPricingResponse> SetProjectPriceAsync(Guid projectId, long amountMinor,
        Guid administratorId, CancellationToken ct)
    {
        if (amountMinor <= 0 || amountMinor > 100_000_000)
            throw new ArgumentOutOfRangeException(nameof(amountMinor), "The approved USD price is out of range.");
        if (!await db.Projects.AsNoTracking().AnyAsync(x => x.Id == projectId, ct))
            throw new KeyNotFoundException("The project was not found.");

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var current = await db.ProjectPricing.SingleOrDefaultAsync(
            x => x.ProjectId == projectId && x.IsActive, ct);
        if (current is not null)
        {
            current.IsActive = false;
            current.EffectiveUntil = now;
            await db.SaveChangesAsync(ct);
        }

        db.ProjectPricing.Add(new ProjectPricing { ProjectId = projectId, AmountMinor = amountMinor,
            Currency = "USD", IsActive = true, EffectiveFrom = now, CreatedBy = administratorId });
        Audit(null, administratorId, "ProjectPriceChanged", "Accepted", projectId.ToString("D"), now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(amountMinor, "USD", Money(amountMinor), GatewayConfigured(), true);
    }

    private async Task<ProjectPricing?> ActivePriceAsync(Guid projectId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // SQLite cannot translate DateTimeOffset range comparisons. The filtered unique index guarantees
        // at most one active row per project, so evaluate only its effective window in managed code.
        var active = await db.ProjectPricing.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.IsActive, ct);
        return active is not null && active.EffectiveFrom <= now &&
            (active.EffectiveUntil is null || active.EffectiveUntil > now) ? active : null;
    }

    private async Task RequireOwnerAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        if (!await db.Projects.AsNoTracking().AnyAsync(x => x.Id == projectId && x.OwnerId == userId, ct))
            throw new UnauthorizedAccessException("Project owner access is required.");
    }

    private async Task RequireReadAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        var allowed = await db.Projects.AsNoTracking().AnyAsync(x => x.Id == projectId && x.OwnerId == userId, ct) ||
            await db.ProjectMembers.AsNoTracking().AnyAsync(x => x.ProjectId == projectId && x.UserId == userId, ct);
        if (!allowed) throw new UnauthorizedAccessException("Project access is required.");
    }

    private async Task<PaymentHistoryPage> PageAsync(IQueryable<PaymentOrder> query, int page, int pageSize,
        CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(page));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var items = new List<PaymentDetails>(rows.Length);
        foreach (var row in rows) items.Add(await MapAsync(row, ct));
        return new(items, page, pageSize, total);
    }

    private async Task<PaymentDetails> MapAsync(PaymentOrder x, CancellationToken ct) => new(x.Id, x.OrderNumber,
        x.ProjectId, x.RequestedBy, x.AmountMinor, x.Currency, x.Status, x.ProviderPaymentId, x.CreatedAt,
        x.UpdatedAt, x.ExpiresAt, await db.ProjectEntitlements.AsNoTracking()
            .AnyAsync(e => e.ProjectId == x.ProjectId && e.Status == "Active", ct));

    private async Task RevokeEntitlementAsync(Guid projectId, DateTimeOffset now, CancellationToken ct)
    {
        var entitlement = await db.ProjectEntitlements.SingleOrDefaultAsync(x => x.ProjectId == projectId, ct);
        if (entitlement is not null) { entitlement.Status = "Revoked"; entitlement.RevokedAt = now; }
    }

    private void Audit(Guid? orderId, Guid? actor, string action, string outcome, string? details, DateTimeOffset now) =>
        db.PaymentAuditRecords.Add(new PaymentAuditRecord { PaymentOrderId = orderId, ActorId = actor,
            Action = action, Outcome = outcome, Details = Optional(details, 500), OccurredAt = now });

    private bool GatewayConfigured() => Settings.Enabled &&
        !string.IsNullOrWhiteSpace(Settings.MerchantId) && !string.IsNullOrWhiteSpace(Settings.MerchantSecret) &&
        Secure(Settings.ReturnUrl) && Secure(Settings.CancelUrl) && Secure(Settings.NotifyUrl);
    private static bool Secure(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);
    private static string Money(long amountMinor) => (amountMinor / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    private static bool TryMinor(string value, out long minor)
    {
        minor = 0;
        return decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) &&
            amount > 0 && decimal.Round(amount, 2) == amount &&
            decimal.Truncate(amount * 100) <= long.MaxValue && (minor = (long)(amount * 100)) > 0;
    }
    private static string Status(string code) => code switch { "2" => PaymentStatuses.Succeeded,
        "0" => PaymentStatuses.Processing, "-1" => PaymentStatuses.Cancelled, "-2" => PaymentStatuses.Failed,
        "-3" => PaymentStatuses.Disputed, _ => throw new ArgumentException("Unsupported PayHere status code.") };
    private static bool CanTransition(string current, string next) => current == next ||
        (!FinalStatuses.Contains(current) && next is PaymentStatuses.Processing or PaymentStatuses.Succeeded or
            PaymentStatuses.Failed or PaymentStatuses.Cancelled or PaymentStatuses.Disputed) ||
        (current == PaymentStatuses.Succeeded && next == PaymentStatuses.Disputed);
    private static string NormalizeStatus(string value) => new[] { PaymentStatuses.Pending, PaymentStatuses.Processing,
        PaymentStatuses.Succeeded, PaymentStatuses.Failed, PaymentStatuses.Cancelled, PaymentStatuses.Refunded,
        PaymentStatuses.Disputed }.FirstOrDefault(x => x.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException("Unsupported payment status.");
    private static void ValidateContact(BillingCheckoutContact c)
    {
        _ = Clean(c.FirstName, 100, nameof(c.FirstName)); _ = Clean(c.LastName, 100, nameof(c.LastName));
        _ = Clean(c.Email, 320, nameof(c.Email)); _ = Clean(c.Phone, 40, nameof(c.Phone));
        _ = Clean(c.Address, 300, nameof(c.Address)); _ = Clean(c.City, 100, nameof(c.City));
        _ = Clean(c.Country, 100, nameof(c.Country));
        if (!c.Email.Contains('@', StringComparison.Ordinal)) throw new ArgumentException("A valid email is required.");
    }
    private static string Clean(string? value, int max, string name) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > max || value.Any(char.IsControl)
            ? throw new ArgumentException($"{name} is invalid.", name) : value.Trim();
    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null :
        Clean(value, max, nameof(value));
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
