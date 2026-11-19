
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;
using Xunit;

namespace Reqviora.Infrastructure.Tests;

public sealed class NavigationEdgeServiceTests
{
    private static async Task<(
        SqliteConnection Connection,
        ReqvioraDbContext Db)> CreateDbAsync()
    {
        var connection = new SqliteConnection(
            "Data Source=:memory:;Foreign Keys=False");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ReqvioraDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new ReqvioraDbContext(options);

        await db.Database.EnsureCreatedAsync();

        return (connection, db);
    }

    [Fact]
    public async Task SameProject_CreatesNavigationEdge()
    {
        var (connection, db) = await CreateDbAsync();
        await using var _ = connection;
        await using var context = db;

        var project = new Project
        {
            Name = "Sample Project"
        };

        var fromScreen = new Screen
        {
            ProjectId = project.Id,
            Name = "Login"
        };

        var toScreen = new Screen
        {
            ProjectId = project.Id,
            Name = "Dashboard"
        };

        db.Projects.Add(project);
        db.Screens.AddRange(fromScreen, toScreen);
        await db.SaveChangesAsync();

        var service = new NavigationEdgeService(db);

        var result = await service.CreateAsync(
            project.Id,
            fromScreen.Id,
            toScreen.Id,
            "Login Success");

        Assert.Equal(project.Id, result.ProjectId);
        Assert.Equal(fromScreen.Id, result.FromScreenId);
        Assert.Equal(toScreen.Id, result.ToScreenId);
        Assert.Single(await db.NavigationEdges.ToListAsync());
    }

    [Fact]
    public async Task DifferentProject_ThrowsException()
    {
        var (connection, db) = await CreateDbAsync();
        await using var _ = connection;
        await using var context = db;

        var project = new Project
        {
            Name = "Project A"
        };

        var fromScreen = new Screen
        {
            ProjectId = project.Id,
            Name = "Login"
        };

        var toScreen = new Screen
        {
            ProjectId = Guid.NewGuid(),
            Name = "Other Project Screen"
        };

        db.Projects.Add(project);
        db.Screens.AddRange(fromScreen, toScreen);
        await db.SaveChangesAsync();

        var service = new NavigationEdgeService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(
                project.Id,
                fromScreen.Id,
                toScreen.Id,
                "Continue"));
    }

    [Fact]
    public async Task DuplicateNavigation_ReturnsExisting()
    {
        var (connection, db) = await CreateDbAsync();
        await using var _ = connection;
        await using var context = db;

        var project = new Project
        {
            Name = "Sample Project"
        };

        var fromScreen = new Screen
        {
            ProjectId = project.Id,
            Name = "Home"
        };

        var toScreen = new Screen
        {
            ProjectId = project.Id,
            Name = "Profile"
        };

        db.Projects.Add(project);
        db.Screens.AddRange(fromScreen, toScreen);
        await db.SaveChangesAsync();

        var service = new NavigationEdgeService(db);

        var first = await service.CreateAsync(
            project.Id,
            fromScreen.Id,
            toScreen.Id,
            "Open Profile");

        var second = await service.CreateAsync(
            project.Id,
            fromScreen.Id,
            toScreen.Id,
            "Open Profile");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.NavigationEdges.CountAsync());
    }

    [Fact]
    public async Task MissingScreen_ThrowsException()
    {
        var (connection, db) = await CreateDbAsync();
        await using var _ = connection;
        await using var context = db;

        var project = new Project
        {
            Name = "Sample Project"
        };

        var fromScreen = new Screen
        {
            ProjectId = project.Id,
            Name = "Home"
        };

        db.Projects.Add(project);
        db.Screens.Add(fromScreen);
        await db.SaveChangesAsync();

        var service = new NavigationEdgeService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateAsync(
                project.Id,
                fromScreen.Id,
                Guid.NewGuid(),
                "Next"));
    }
}
