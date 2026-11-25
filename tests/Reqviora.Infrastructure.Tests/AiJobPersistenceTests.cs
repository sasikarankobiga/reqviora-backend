using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Repositories;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Infrastructure.Tests;

public sealed class AiJobPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Enqueue_is_durable_idempotent_and_project_scoped()
    {
        await using var database = await PersistenceDatabase.CreateAsync("sqlite");
        await using var db = database.Open();
        var (owner, project) = await SeedProjectAsync(db);
        var service = Service(db, Now);

        var first = await service.EnqueueAsync(owner.Id, project.Id, "Generate requirements", "request-1");
        var replay = await service.EnqueueAsync(owner.Id, project.Id, "A different retry body", "request-1");

        Assert.True(first.Created);
        Assert.False(replay.Created);
        Assert.Equal(first.Job.Id, replay.Job.Id);
        Assert.Equal("pending", replay.Job.State);
        await using var restarted = database.Open();
        Assert.Equal("Generate requirements", (await restarted.AiJobs.FindAsync(first.Job.Id))!.Prompt);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Service(db, Now).EnqueueAsync(Guid.NewGuid(), project.Id, "Denied", "request-2"));
    }

    [Fact]
    public async Task Lease_is_exclusive_and_an_expired_running_job_recovers_after_restart()
    {
        await using var database = await PersistenceDatabase.CreateAsync("sqlite");
        Guid jobId;
        await using (var seed = database.Open())
        {
            var (owner, project) = await SeedProjectAsync(seed);
            var job = NewJob(owner.Id, project.Id, "restart", maxAttempts: 3);
            job.State = AiJobState.Running;
            job.AttemptCount = 1;
            job.LeaseOwner = "stopped-worker";
            job.LeaseExpiresAtUnixTimeMilliseconds = Now.AddMinutes(-1).ToUnixTimeMilliseconds();
            seed.AiJobs.Add(job);
            await seed.SaveChangesAsync();
            jobId = job.Id;
        }

        await using var firstDb = database.Open();
        await using var secondDb = database.Open();
        var recovered = await new AiJobRepository(firstDb).TryLeaseNextAsync(
            "worker-b", Now, TimeSpan.FromSeconds(30));
        var duplicate = await new AiJobRepository(secondDb).TryLeaseNextAsync(
            "worker-c", Now, TimeSpan.FromSeconds(30));

        Assert.NotNull(recovered);
        Assert.Equal(jobId, recovered.Id);
        Assert.Equal(2, recovered.AttemptCount);
        Assert.Equal("worker-b", recovered.LeaseOwner);
        Assert.Null(duplicate);
    }

    [Fact]
    public async Task Failure_uses_bounded_backoff_and_the_last_attempt_is_terminal()
    {
        await using var database = await PersistenceDatabase.CreateAsync("sqlite");
        await using var db = database.Open();
        var (owner, project) = await SeedProjectAsync(db);
        var job = NewJob(owner.Id, project.Id, "retry", maxAttempts: 2);
        db.AiJobs.Add(job);
        await db.SaveChangesAsync();
        var repository = new AiJobRepository(db);

        var first = await repository.TryLeaseNextAsync("worker", Now, TimeSpan.FromSeconds(30));
        Assert.NotNull(first);
        Assert.Equal(AiJobFailureDisposition.Retrying, await repository.FailAsync(
            first.Id, "worker", "provider_error", "AI provider request failed.",
            Now, TimeSpan.FromSeconds(4)));
        var waiting = await repository.GetOwnedAsync(job.Id, owner.Id);
        Assert.Equal(AiJobState.Pending, waiting!.State);
        Assert.Equal(Now.AddSeconds(4).ToUnixTimeMilliseconds(), waiting.AvailableAtUnixTimeMilliseconds);
        Assert.Null(await repository.TryLeaseNextAsync("other", Now.AddSeconds(3), TimeSpan.FromSeconds(30)));

        var second = await repository.TryLeaseNextAsync("other", Now.AddSeconds(4), TimeSpan.FromSeconds(30));
        Assert.NotNull(second);
        Assert.Equal(2, second.AttemptCount);
        Assert.Equal(AiJobFailureDisposition.Failed, await repository.FailAsync(
            second.Id, "other", "provider_error", "AI provider request failed.",
            Now.AddSeconds(4), TimeSpan.FromSeconds(8)));
        var failed = await repository.GetOwnedAsync(job.Id, owner.Id);
        Assert.Equal(AiJobState.Failed, failed!.State);
        Assert.Equal("provider_error", failed.ErrorCode);
        Assert.Equal("AI provider request failed.", failed.ErrorMessage);
    }

    [Fact]
    public async Task Pending_and_running_jobs_support_persistent_cancellation()
    {
        await using var database = await PersistenceDatabase.CreateAsync("sqlite");
        await using var db = database.Open();
        var (owner, project) = await SeedProjectAsync(db);
        var pending = NewJob(owner.Id, project.Id, "pending");
        var running = NewJob(owner.Id, project.Id, "running");
        db.AiJobs.AddRange(pending, running);
        await db.SaveChangesAsync();
        var repository = new AiJobRepository(db);

        var cancelled = await repository.RequestCancellationAsync(pending.Id, owner.Id, Now);
        Assert.Equal(AiJobState.Cancelled, cancelled!.State);
        Assert.NotNull(cancelled.CompletedAt);

        var lease = await repository.TryLeaseNextAsync("worker", Now, TimeSpan.FromSeconds(30));
        Assert.Equal(running.Id, lease!.Id);
        var requested = await repository.RequestCancellationAsync(running.Id, owner.Id, Now.AddSeconds(1));
        Assert.Equal(AiJobState.Running, requested!.State);
        Assert.NotNull(requested.CancellationRequestedAt);
        Assert.True(await repository.CancelLeasedAsync(running.Id, "worker", Now.AddSeconds(2)));
        Assert.Equal(AiJobState.Cancelled, (await repository.GetOwnedAsync(running.Id, owner.Id))!.State);
    }

    [Fact]
    public async Task Worker_persists_success_and_timeout_retry_without_provider_details()
    {
        await using var database = await PersistenceDatabase.CreateAsync("sqlite");
        Guid successfulId;
        Guid timeoutId;
        Guid ownerId;
        await using (var seed = database.Open())
        {
            var (owner, project) = await SeedProjectAsync(seed);
            ownerId = owner.Id;
            var successful = NewJob(owner.Id, project.Id, "success");
            var timeout = NewJob(owner.Id, project.Id, "timeout");
            timeout.AvailableAtUnixTimeMilliseconds = Now.AddMinutes(1).ToUnixTimeMilliseconds();
            seed.AiJobs.AddRange(successful, timeout);
            await seed.SaveChangesAsync();
            successfulId = successful.Id;
            timeoutId = timeout.Id;
        }

        await using (var provider = Provider(database, new FakeAiService("generated")))
        {
            var worker = Worker(provider, Now);
            Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
        }
        await using (var check = database.Open())
        {
            var completed = await new AiJobRepository(check).GetOwnedAsync(successfulId, ownerId);
            Assert.Equal(AiJobState.Completed, completed!.State);
            Assert.Equal("generated", completed.Result);
            var timeout = await check.AiJobs.FindAsync(timeoutId);
            timeout!.AvailableAtUnixTimeMilliseconds = Now.ToUnixTimeMilliseconds();
            await check.SaveChangesAsync();
        }

        await using (var provider = Provider(database, new FakeAiService(cancel: true)))
        {
            var worker = Worker(provider, Now);
            Assert.True(await worker.ProcessOneAsync(CancellationToken.None));
        }
        await using (var check = database.Open())
        {
            var retry = await new AiJobRepository(check).GetOwnedAsync(timeoutId, ownerId);
            Assert.Equal(AiJobState.Pending, retry!.State);
            Assert.Equal("timeout", retry.ErrorCode);
            Assert.Equal("AI processing timed out.", retry.ErrorMessage);
            Assert.DoesNotContain("provider", retry.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Worker_honors_cancellation_that_arrives_with_the_provider_result()
    {
        await using var database = await PersistenceDatabase.CreateAsync("sqlite");
        Guid jobId;
        Guid ownerId;
        await using (var seed = database.Open())
        {
            var (owner, project) = await SeedProjectAsync(seed);
            ownerId = owner.Id;
            var job = NewJob(owner.Id, project.Id, "completion-race");
            seed.AiJobs.Add(job);
            await seed.SaveChangesAsync();
            jobId = job.Id;
        }

        var ai = new CallbackAiService(async () =>
        {
            await using var cancellationDb = database.Open();
            await new AiJobRepository(cancellationDb).RequestCancellationAsync(jobId, ownerId, Now);
        });
        await using (var provider = Provider(database, ai))
            Assert.True(await Worker(provider, Now).ProcessOneAsync(CancellationToken.None));

        await using var check = database.Open();
        var cancelled = await new AiJobRepository(check).GetOwnedAsync(jobId, ownerId);
        Assert.Equal(AiJobState.Cancelled, cancelled!.State);
        Assert.Null(cancelled.Result);
    }

    private static PersistentAiJobService Service(ReqvioraDbContext db, DateTimeOffset now) => new(
        new AiJobRepository(db),
        db,
        Options.Create(new AiOptions { TimeoutSeconds = 120 }),
        Options.Create(new AiJobOptions()),
        new FixedClock(now));

    private static ServiceProvider Provider(PersistenceDatabase database, IAIService ai)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => database.Open());
        services.AddScoped<IAiJobRepository, AiJobRepository>();
        services.AddSingleton(ai);
        return services.BuildServiceProvider();
    }

    private static AiJobWorker Worker(ServiceProvider provider, DateTimeOffset now) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new AiJobOptions
        {
            LeaseSeconds = 30,
            BaseRetrySeconds = 2,
            MaxRetrySeconds = 8
        }),
        new FixedClock(now),
        NullLogger<AiJobWorker>.Instance);

    private static AiJob NewJob(Guid ownerId, Guid projectId, string key, int maxAttempts = 3) => new()
    {
        OwnerId = ownerId,
        ProjectId = projectId,
        Prompt = key,
        IdempotencyKey = key,
        MaxAttempts = maxAttempts,
        TimeoutSeconds = 10,
        AvailableAtUnixTimeMilliseconds = Now.ToUnixTimeMilliseconds(),
        CreatedAt = Now
    };

    private static async Task<(User Owner, Project Project)> SeedProjectAsync(ReqvioraDbContext db)
    {
        var owner = new User
        {
            Email = $"{Guid.NewGuid():N}@example.test",
            NormalizedEmail = $"{Guid.NewGuid():N}@EXAMPLE.TEST",
            DisplayName = "AI job owner",
            PasswordHash = "not-used"
        };
        var project = new Project { OwnerId = owner.Id, Name = "AI project", PrimaryPlatform = "web" };
        db.AddRange(owner, project);
        await db.SaveChangesAsync();
        return (owner, project);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeAiService(string result = "", bool cancel = false) : IAIService
    {
        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default) =>
            cancel ? Task.FromCanceled<string>(new CancellationToken(canceled: true)) : Task.FromResult(result);
    }

    private sealed class CallbackAiService(Func<Task> callback) : IAIService
    {
        public async Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken = default)
        {
            await callback();
            return "must-not-be-persisted";
        }
    }
}
