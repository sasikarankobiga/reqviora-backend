
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;
using Xunit;

namespace Reqviora.Infrastructure.Tests;

public sealed class FeatureScreenServiceTests
{
    [Fact]
    public async Task SameProject_CreatesLink()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:;Foreign Keys=False");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ReqvioraDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ReqvioraDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var projectId = Guid.NewGuid();

        var feature = new Feature
        {
            ProjectId = projectId,
            Name = "Login"
        };

        var screen = new Screen
        {
            ProjectId = projectId,
            Name = "Login Screen"
        };

        db.Features.Add(feature);
        db.Screens.Add(screen);
        await db.SaveChangesAsync();

        var service = new FeatureScreenService(db);

        var result = await service.LinkAsync(
            feature.Id, screen.Id);

        Assert.Equal(feature.Id, result.FeatureId);
        Assert.Equal(screen.Id, result.ScreenId);
        Assert.Single(await db.FeatureScreens.ToListAsync());
    }

    [Fact]
    public async Task DifferentProjects_ThrowsException()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:;Foreign Keys=False");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ReqvioraDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ReqvioraDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var feature = new Feature
        {
            ProjectId = Guid.NewGuid(),
            Name = "Login"
        };

        var screen = new Screen
        {
            ProjectId = Guid.NewGuid(),
            Name = "Dashboard"
        };

        db.Features.Add(feature);
        db.Screens.Add(screen);
        await db.SaveChangesAsync();

        var service = new FeatureScreenService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.LinkAsync(feature.Id, screen.Id));
    }

    [Fact]
    public async Task DuplicateLink_ReturnsExisting()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:;Foreign Keys=False");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ReqvioraDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new ReqvioraDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var projectId = Guid.NewGuid();

        var feature = new Feature
        {
            ProjectId = projectId,
            Name = "Profile"
        };

        var screen = new Screen
        {
            ProjectId = projectId,
            Name = "Profile Screen"
        };

        db.Features.Add(feature);
        db.Screens.Add(screen);
        await db.SaveChangesAsync();

        var service = new FeatureScreenService(db);

        var first = await service.LinkAsync(
            feature.Id, screen.Id);

        var second = await service.LinkAsync(
            feature.Id, screen.Id);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.FeatureScreens.CountAsync());
    }
}
