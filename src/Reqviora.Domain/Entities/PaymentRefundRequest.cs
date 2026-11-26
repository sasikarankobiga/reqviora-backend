namespace Reqviora.Domain.Entities;

public sealed class PaymentRefundRequest
{
    private PaymentRefundRequest() { }

    public Guid PaymentId { get; private set; }
    public Guid RequestedBy { get; private set; }
    public string Reason { get; private set; } = "";
    public string Status { get; private set; } = "requested";
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public Guid ConcurrencyToken { get; private set; } = Guid.NewGuid();

    public static bool ValidReason(string reason) =>
        reason is "duplicate_payment" or "service_issue" or "other";

    public static PaymentRefundRequest Create(
        Guid paymentId, Guid actorId,
        string reason, DateTimeOffset now)
    {
        if (paymentId == Guid.Empty || actorId == Guid.Empty ||
            !ValidReason(reason))
            throw new ArgumentException("Invalid refund request.");

        return new PaymentRefundRequest
        {
            PaymentId = paymentId,
            RequestedBy = actorId,
            Reason = reason,
            RequestedAt = now.ToUniversalTime()
        };
    }

    public bool Review(
        Guid actorId, string decision, DateTimeOffset now)
    {
        if (actorId == Guid.Empty ||
            decision is not ("reviewed" or "rejected"))
            throw new ArgumentException("Invalid review.");

        if (Status != "requested")
            return false;

        Status = decision;
        ReviewedBy = actorId;
        ReviewedAt = now.ToUniversalTime();
        ConcurrencyToken = Guid.NewGuid();

        return true;
    }
}
