
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;
using Xunit;

namespace Reqviora.Infrastructure.Tests;

public class ProjectDraftServiceTests
{
    [Fact]
    public async Task ResumeAsync_WhenDraftDoesNotExist_ReturnsNull()
    {
        // Step 1: Create a temporary SQLite database
        await using var connection =
            new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        // Step 2: Configure EF Core
        var options =
            new DbContextOptionsBuilder<ReqvioraDbContext>()
                .UseSqlite(connection)
                .Options;

        await using var dbContext =
            new ReqvioraDbContext(options);

        await dbContext.Database.EnsureCreatedAsync();

        // Step 3: Create service
        var service = new ProjectDraftService(dbContext);

        // Step 4: Try to resume a non-existing draft
        var result = await service.ResumeAsync(Guid.NewGuid());

        // Step 5: Verify result
        Assert.Null(result);
    }
}
