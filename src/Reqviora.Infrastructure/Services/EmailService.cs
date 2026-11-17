using Reqviora.Application.Common.Interfaces;

namespace Reqviora.Infrastructure.Services;

public sealed class EmailService : IEmailService
{
    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Configure an email provider before sending email.");
}
