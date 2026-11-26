using Microsoft.Extensions.Options;
using Reqviora.Application.Common.Interfaces;

namespace Reqviora.Infrastructure.Services;

public sealed class EmailService(
    IEmailTransport transport,
    IOptions<SmtpOptions> options) : IEmailService
{
    public async Task SendAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(to) || to.Length > 320 ||
            string.IsNullOrWhiteSpace(subject) || subject.Length > 300 ||
            string.IsNullOrWhiteSpace(body) || body.Length > 100_000)
            throw new ArgumentException("Invalid email envelope.");

        var value = options.Value;
        var attempts = Math.Clamp(value.MaxAttempts, 1, 5);
        EmailDeliveryException? lastFailure = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(value.TimeoutSeconds, 1, 120)));
            try
            {
                await transport.SendAsync(new(to, subject, body), timeout.Token);
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastFailure = new("SMTP_TIMEOUT", isTransient: true);
            }
            catch (EmailDeliveryException exception) when (exception.IsTransient)
            {
                lastFailure = exception;
            }

            if (attempt < attempts)
            {
                var delay = Math.Min(5000,
                    Math.Max(0, value.BaseRetryMilliseconds) * (1 << (attempt - 1)));
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw lastFailure ?? new EmailDeliveryException("SMTP_DELIVERY_FAILED", isTransient: false);
    }
}
