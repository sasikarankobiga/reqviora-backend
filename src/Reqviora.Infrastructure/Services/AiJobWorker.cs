using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Reqviora.Application.Common.Interfaces;

namespace Reqviora.Infrastructure.Services;

public sealed class AiJobWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AiJobOptions> options,
    TimeProvider clock,
    ILogger<AiJobWorker> logger,
    AiWorkerSignal? signal = null) : BackgroundService
{
    private readonly string _leaseOwner =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromMilliseconds(Math.Clamp(
            options.Value.PollIntervalMilliseconds, 100, 30_000));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessOneAsync(stoppingToken);
                if (!processed) await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                signal?.Record(clock.GetUtcNow(), false);
                logger.LogError(exception, "The durable AI job worker iteration failed.");
                await Task.Delay(delay, stoppingToken);
            }
        }
    }

    public async Task<bool> ProcessOneAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAiJobRepository>();
        var minimumLease = TimeSpan.FromSeconds(Math.Clamp(options.Value.LeaseSeconds, 30, 3600));
        var job = await repository.TryLeaseNextAsync(
            _leaseOwner,
            clock.GetUtcNow(),
            minimumLease,
            stoppingToken);
        signal?.Record(clock.GetUtcNow(), true);
        if (job is null) return false;

        using var execution = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        execution.CancelAfter(TimeSpan.FromSeconds(job.TimeoutSeconds));
        using var monitorStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var monitor = MonitorCancellationAsync(job.Id, execution, monitorStop.Token);
        try
        {
            var ai = scope.ServiceProvider.GetRequiredService<IAIService>();
            var result = await ai.CompleteAsync(job.Prompt, execution.Token);
            if (result.Length > options.Value.MaxResultCharacters)
                throw new InvalidDataException("The AI provider result exceeded the configured limit.");

            if (await repository.IsCancellationRequestedAsync(job.Id, stoppingToken))
                await repository.CancelLeasedAsync(job.Id, _leaseOwner, clock.GetUtcNow(), stoppingToken);
            else if (!await repository.CompleteAsync(
                job.Id, _leaseOwner, result, clock.GetUtcNow(), stoppingToken))
            {
                if (await repository.IsCancellationRequestedAsync(job.Id, stoppingToken))
                    await repository.CancelLeasedAsync(job.Id, _leaseOwner, clock.GetUtcNow(), stoppingToken);
                else
                    logger.LogWarning("AI job {JobId} completion lost its lease.", job.Id);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Leave the running row untouched. Its lease expiry makes it recoverable after restart.
        }
        catch (Exception exception)
        {
            var cancellationRequested = await CancellationRequestedAsync(job.Id, stoppingToken);
            if (cancellationRequested)
            {
                await repository.CancelLeasedAsync(job.Id, _leaseOwner, clock.GetUtcNow(), stoppingToken);
            }
            else
            {
                var timedOut = exception is OperationCanceledException;
                var retryDelay = Backoff(job.AttemptCount);
                var disposition = await repository.FailAsync(
                    job.Id,
                    _leaseOwner,
                    timedOut ? "timeout" : "provider_error",
                    timedOut ? "AI processing timed out." : "AI provider request failed.",
                    clock.GetUtcNow(),
                    retryDelay,
                    stoppingToken);
                logger.LogWarning(exception,
                    "AI job {JobId} attempt {Attempt} ended with {Disposition}.",
                    job.Id, job.AttemptCount, disposition);
            }
        }
        finally
        {
            monitorStop.Cancel();
            try { await monitor; }
            catch (OperationCanceledException) { }
        }

        return true;
    }

    private TimeSpan Backoff(int attempt)
    {
        var baseSeconds = Math.Clamp(options.Value.BaseRetrySeconds, 1, 300);
        var maxSeconds = Math.Clamp(options.Value.MaxRetrySeconds, baseSeconds, 3600);
        var exponent = Math.Clamp(attempt - 1, 0, 20);
        return TimeSpan.FromSeconds(Math.Min(maxSeconds, baseSeconds * Math.Pow(2, exponent)));
    }

    private async Task MonitorCancellationAsync(
        Guid jobId,
        CancellationTokenSource execution,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (await CancellationRequestedAsync(jobId, cancellationToken))
            {
                execution.Cancel();
                return;
            }
        }
    }

    private async Task<bool> CancellationRequestedAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAiJobRepository>()
            .IsCancellationRequestedAsync(jobId, cancellationToken);
    }
}
