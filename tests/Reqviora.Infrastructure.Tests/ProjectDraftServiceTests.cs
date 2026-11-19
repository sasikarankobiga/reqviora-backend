
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;
using Xunit;

namespace Reqviora.Infrastructure.Tests;

public class ProjectDraftServiceTests
{
    // Creates a temporary SQLite database for testing.
    // Foreign keys are disabled only for these isolated service tests.
    private static async Task<(
        SqliteConnection Connection,
        ReqvioraDbContext Context)> CreateDatabaseAsync()
    {
        var connection = new SqliteConnection(
            "Data Source=:memory:;Foreign Keys=False");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ReqvioraDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ReqvioraDbContext(options);

        await context.Database.EnsureCreatedAsync();

        return (connection, context);
    }

    // TEST 1: A non-existing draft should return null.
    [Fact]
    public async Task ResumeAsync_WhenDraftDoesNotExist_ReturnsNull()
    {
        var (connection, context) = await CreateDatabaseAsync();

        await using var dbConnection = connection;
        await using var dbContext = context;

        var service = new ProjectDraftService(context);

        var result = await service.ResumeAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    // TEST 2: A new draft should be saved successfully.
    [Fact]
    public async Task SaveAsync_CreatesNewDraft()
    {
        var (connection, context) = await CreateDatabaseAsync();

        await using var dbConnection = connection;
        await using var dbContext = context;

        var service = new ProjectDraftService(context);
        var projectId = Guid.NewGuid();

        var draft = await service.SaveAsync(
            projectId,
            "{\"step\":1}",
            null);

        Assert.Equal(projectId, draft.ProjectId);
        Assert.Equal("{\"step\":1}", draft.DraftJson);
        Assert.Equal(1, draft.Version);
        Assert.NotEqual(Guid.Empty, draft.ConcurrencyToken);

        Assert.Equal(1, await context.ProjectDrafts.CountAsync());
    }

    // TEST 3: A saved draft should be resumed.
    [Fact]
    public async Task ResumeAsync_ReturnsSavedDraft()
    {
        var (connection, context) = await CreateDatabaseAsync();

        await using var dbConnection = connection;
        await using var dbContext = context;

        var service = new ProjectDraftService(context);
        var projectId = Guid.NewGuid();

        await service.SaveAsync(
            projectId,
            "{\"project\":\"REQVIORA\"}",
            null);

        var resumed = await service.ResumeAsync(projectId);

        Assert.NotNull(resumed);
        Assert.Equal(projectId, resumed.ProjectId);
        Assert.Equal(
            "{\"project\":\"REQVIORA\"}",
            resumed.DraftJson);
        Assert.Equal(1, resumed.Version);
    }

    // TEST 4: Updating with the correct token increases version.
    [Fact]
    public async Task SaveAsync_WithCorrectToken_UpdatesDraft()
    {
        var (connection, context) = await CreateDatabaseAsync();

        await using var dbConnection = connection;
        await using var dbContext = context;

        var service = new ProjectDraftService(context);
        var projectId = Guid.NewGuid();

        var original = await service.SaveAsync(
            projectId,
            "{\"step\":1}",
            null);

        var oldToken = original.ConcurrencyToken;

        var updated = await service.SaveAsync(
            projectId,
            "{\"step\":2}",
            oldToken);

        Assert.Equal(2, updated.Version);
        Assert.Equal("{\"step\":2}", updated.DraftJson);
        Assert.NotEqual(oldToken, updated.ConcurrencyToken);
        Assert.Equal(1, await context.ProjectDrafts.CountAsync());
    }

    // TEST 5: An outdated token should cause a concurrency error.
    [Fact]
    public async Task SaveAsync_WithStaleToken_ThrowsConcurrencyException()
    {
        var (connection, context) = await CreateDatabaseAsync();

        await using var dbConnection = connection;
        await using var dbContext = context;

        var service = new ProjectDraftService(context);
        var projectId = Guid.NewGuid();

        var original = await service.SaveAsync(
            projectId,
            "{\"step\":1}",
            null);

        var oldToken = original.ConcurrencyToken;

        await service.SaveAsync(
            projectId,
            "{\"step\":2}",
            oldToken);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            async () =>
            {
                await service.SaveAsync(
                    projectId,
                    "{\"step\":3}",
                    oldToken);
            });
    }
}
