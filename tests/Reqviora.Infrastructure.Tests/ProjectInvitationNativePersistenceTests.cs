using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Repositories;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Infrastructure.Tests;

public sealed class ProjectInvitationNativePersistenceTests
{
    [NativeInvitationDatabaseFact]
    public async Task Configured_native_providers_allow_only_one_concurrent_acceptance()
    {
        var providers = new List<string>();
        if (Environment.GetEnvironmentVariable("REQVIORA_TEST_POSTGRES") is not null) providers.Add("postgres");
        if (Environment.GetEnvironmentVariable("REQVIORA_TEST_SQLSERVER") is not null) providers.Add("sqlserver");
        foreach (var provider in providers)
            await VerifyProviderAsync(provider);
    }

    private static async Task VerifyProviderAsync(string provider)
    {
        await using var database = await PersistenceDatabase.CreateAsync(provider);
        var owner = User("native-owner@example.test");
        var recipient = User("native-recipient@example.test");
        var project = new Project { Name = "Native invitations", Description = provider, OwnerId = owner.Id };
        var email = new CapturingEmail();
        await using (var seed = database.Open())
        {
            seed.Users.AddRange(owner, recipient);
            seed.Projects.Add(project);
            await seed.SaveChangesAsync();
            var service = Service(seed, email);
            await service.CreateAsync(project.Id, owner.Id, recipient.Email, "Editor", CancellationToken.None);
        }

        var token = email.Token;
        await using var firstDb = database.Open();
        await using var secondDb = database.Open();
        var first = AcceptAsync(Service(firstDb, email), token, recipient.Id);
        var second = AcceptAsync(Service(secondDb, email), token, recipient.Id);
        var outcomes = await Task.WhenAll(first, second);

        Assert.Equal(1, outcomes.Count(outcome => outcome));
        await using var verify = database.Open();
        Assert.Equal(1, await verify.ProjectMembers.CountAsync(member =>
            member.ProjectId == project.Id && member.UserId == recipient.Id));
        Assert.Equal(ProjectInvitationStatuses.Accepted,
            await verify.ProjectInvitations.Where(invitation => invitation.ProjectId == project.Id)
                .Select(invitation => invitation.Status).SingleAsync());
    }

    private static async Task<bool> AcceptAsync(ProjectInvitationService service, string token, Guid recipientId)
    {
        try
        {
            await service.AcceptAsync(token, recipientId, CancellationToken.None);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static ProjectInvitationService Service(
        Reqviora.Infrastructure.Persistence.ReqvioraDbContext db, IEmailService email) => new(
        new ProjectInvitationRepository(db), email,
        Options.Create(new ProjectInvitationOptions { ExpirationHours = 1 }),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AccountLinks:BaseUrl"] = "https://app.example.test"
        }).Build(), TimeProvider.System);

    private static User User(string email) => new()
    {
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        DisplayName = email,
        PasswordHash = "not-used",
        EmailVerified = true
    };

    private sealed class CapturingEmail : IEmailService
    {
        public string Token { get; private set; } = "";

        public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
        {
            var link = body[(body.LastIndexOf("Open: ", StringComparison.Ordinal) + 6)..];
            Token = Uri.UnescapeDataString(new Uri(link).Query.TrimStart('?').Split('=', 2)[1]);
            return Task.CompletedTask;
        }
    }
}

public sealed class NativeInvitationDatabaseFactAttribute : FactAttribute
{
    public NativeInvitationDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("REQVIORA_TEST_POSTGRES") is null &&
            Environment.GetEnvironmentVariable("REQVIORA_TEST_SQLSERVER") is null)
            Skip = "Native invitation concurrency requires a configured disposable database.";
    }
}
