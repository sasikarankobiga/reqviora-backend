namespace Reqviora.Application.Common.Interfaces;

public interface IEmailService
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}

public sealed record EmailEnvelope(string To, string Subject, string Body);

public interface IEmailTransport
{
    Task SendAsync(EmailEnvelope envelope, CancellationToken cancellationToken = default);
}

public sealed class EmailDeliveryException(
    string errorCode,
    bool isTransient,
    Exception? innerException = null)
    : Exception("Email delivery failed.", innerException)
{
    public string ErrorCode { get; } = errorCode;
    public bool IsTransient { get; } = isTransient;
}
