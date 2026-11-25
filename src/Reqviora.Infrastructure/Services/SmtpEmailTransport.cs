using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Reqviora.Application.Common.Interfaces;

namespace Reqviora.Infrastructure.Services;

public sealed class SmtpEmailTransport(IOptions<SmtpOptions> options) : IEmailTransport
{
    public async Task SendAsync(EmailEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var value = options.Value;
        if (string.IsNullOrWhiteSpace(value.Host) || string.IsNullOrWhiteSpace(value.FromAddress))
            throw new InvalidOperationException(
                "SMTP host and sender must be configured through secrets or environment variables.");

        using var message = new MailMessage
        {
            From = new MailAddress(value.FromAddress, value.FromName),
            Subject = envelope.Subject,
            Body = envelope.Body,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(envelope.To));
        using var client = new SmtpClient(value.Host, value.Port)
        {
            EnableSsl = value.UseSsl,
            Credentials = string.IsNullOrWhiteSpace(value.UserName)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(value.UserName, value.Password)
        };
        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (SmtpException exception)
        {
            var status = (int)exception.StatusCode;
            throw new EmailDeliveryException(
                $"SMTP_{status}",
                isTransient: status is >= 400 and < 500 || exception.StatusCode == SmtpStatusCode.GeneralFailure,
                exception);
        }
    }
}
