using Microsoft.Extensions.Options;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Infrastructure.Tests;

public sealed class EmailServiceTests
{
    [Fact]
    public async Task Transient_failures_are_retried_with_the_same_envelope()
    {
        var transport = new ScriptedTransport(
            _ => Task.FromException(new EmailDeliveryException("SMTP_421", true)),
            _ => Task.FromException(new EmailDeliveryException("SMTP_451", true)),
            _ => Task.CompletedTask);
        var service = Service(transport, attempts: 3);

        await service.SendAsync("person@example.test", "Verify", "token-bearing body");

        Assert.Equal(3, transport.Attempts);
        Assert.All(transport.Envelopes, envelope =>
        {
            Assert.Equal("person@example.test", envelope.To);
            Assert.Equal("Verify", envelope.Subject);
            Assert.Equal("token-bearing body", envelope.Body);
        });
    }

    [Fact]
    public async Task Permanent_failure_is_not_retried()
    {
        var transport = new ScriptedTransport(
            _ => Task.FromException(new EmailDeliveryException("SMTP_550", false)));

        var failure = await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            Service(transport, attempts: 3).SendAsync("person@example.test", "Verify", "body"));

        Assert.Equal("SMTP_550", failure.ErrorCode);
        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public async Task Provider_timeout_is_bounded_and_reported_without_message_content()
    {
        var transport = new ScriptedTransport(token => Task.Delay(Timeout.InfiniteTimeSpan, token));

        var failure = await Assert.ThrowsAsync<EmailDeliveryException>(() =>
            Service(transport, attempts: 1, timeoutSeconds: 1)
                .SendAsync("person@example.test", "Verify", "sensitive-token"));

        Assert.Equal("SMTP_TIMEOUT", failure.ErrorCode);
        Assert.DoesNotContain("sensitive-token", failure.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_retried_or_converted()
    {
        var transport = new ScriptedTransport(token => Task.Delay(Timeout.InfiniteTimeSpan, token));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(transport, attempts: 3).SendAsync(
                "person@example.test", "Verify", "body", cancellation.Token));

        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public async Task Invalid_envelope_is_rejected_before_transport()
    {
        var transport = new ScriptedTransport(_ => Task.CompletedTask);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(transport, attempts: 1).SendAsync("", "Verify", "body"));
        Assert.Equal(0, transport.Attempts);
    }

    private static EmailService Service(
        IEmailTransport transport,
        int attempts,
        int timeoutSeconds = 5) => new(
        transport,
        Options.Create(new SmtpOptions
        {
            MaxAttempts = attempts,
            TimeoutSeconds = timeoutSeconds,
            BaseRetryMilliseconds = 0
        }));

    private sealed class ScriptedTransport(params Func<CancellationToken, Task>[] steps) : IEmailTransport
    {
        public int Attempts { get; private set; }
        public List<EmailEnvelope> Envelopes { get; } = [];

        public Task SendAsync(EmailEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            var index = Math.Min(Attempts++, steps.Length - 1);
            return steps[index](cancellationToken);
        }
    }
}
