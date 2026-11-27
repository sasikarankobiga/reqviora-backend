using Reqviora.Domain.Common;

namespace Reqviora.Domain.Entities;

public enum ProjectPaymentStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3,
    ChargedBack = 4
}

public sealed class ProjectPaymentOrder : BaseEntity
{
    private ProjectPaymentOrder()
    {
    }

    public Guid ProjectId { get; private set; }
    public Guid PayerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;

    public string OrderReference => Id.ToString("N");

    public string? GatewayPaymentId { get; private set; }

    public ProjectPaymentStatus Status { get; private set; }
        = ProjectPaymentStatus.Pending;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid ConcurrencyToken { get; private set; }
        = Guid.NewGuid();

    public bool IsSettled =>
        Status == ProjectPaymentStatus.Succeeded;

    public static ProjectPaymentOrder Create(
        Guid projectId,
        Guid payerId,
        decimal amount,
        string currency,
        DateTimeOffset now)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException(
                "Project is required.", nameof(projectId));

        if (payerId == Guid.Empty)
            throw new ArgumentException(
                "Payer is required.", nameof(payerId));

        if (amount <= 0 ||
            amount > 999999999.99m ||
            decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount));
        }

        if (currency is not ("USD" or "LKR"))
            throw new ArgumentException(
                "Unsupported currency.", nameof(currency));

        return new ProjectPaymentOrder
        {
            ProjectId = projectId,
            PayerId = payerId,
            Amount = amount,
            Currency = currency,
            CreatedAt = now.ToUniversalTime(),
            UpdatedAt = now.ToUniversalTime()
        };
    }

    // Call only after server-side gateway verification.
    // This domain method does not authenticate webhooks.
    public bool ApplyVerifiedNotification(
        string orderReference,
        string gatewayPaymentId,
        decimal amount,
        string currency,
        ProjectPaymentStatus nextStatus,
        DateTimeOffset now)
    {
        if (!string.Equals(
                OrderReference,
                orderReference,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Payment order mismatch.");
        }

        if (Amount != amount || Currency != currency)
            throw new InvalidOperationException(
                "Payment amount or currency mismatch.");

        if (string.IsNullOrWhiteSpace(gatewayPaymentId) ||
            gatewayPaymentId.Length > 128 ||
            gatewayPaymentId != gatewayPaymentId.Trim())
        {
            throw new ArgumentException(
                "Invalid payment reference.",
                nameof(gatewayPaymentId));
        }

        if (!Enum.IsDefined(nextStatus))
            throw new ArgumentOutOfRangeException(
                nameof(nextStatus));

        if (GatewayPaymentId is not null &&
            GatewayPaymentId != gatewayPaymentId)
        {
            throw new InvalidOperationException(
                "Gateway payment reference mismatch.");
        }

        if (Status == nextStatus &&
            GatewayPaymentId == gatewayPaymentId)
        {
            return false;
        }

        if (!CanTransition(Status, nextStatus))
            return false;

        GatewayPaymentId = gatewayPaymentId;
        Status = nextStatus;
        UpdatedAt = now.ToUniversalTime();
        ConcurrencyToken = Guid.NewGuid();

        return true;
    }

    private static bool CanTransition(
        ProjectPaymentStatus current,
        ProjectPaymentStatus next)
    {
        return current switch
        {
            ProjectPaymentStatus.Pending => true,

            ProjectPaymentStatus.Failed or
            ProjectPaymentStatus.Cancelled =>
                next is ProjectPaymentStatus.Succeeded
                    or ProjectPaymentStatus.ChargedBack,

            ProjectPaymentStatus.Succeeded =>
                next == ProjectPaymentStatus.ChargedBack,

            ProjectPaymentStatus.ChargedBack => false,

            _ => false
        };
    }
}
