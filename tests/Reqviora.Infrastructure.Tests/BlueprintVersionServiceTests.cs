
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;
using Xunit;

namespace Reqviora.Infrastructure.Tests;

public sealed class BlueprintVersionServiceTests
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
    public async Task ValidJson_CreatesVersionOne()
    {
        var (connection, db) = await CreateDbAsync();
        await using var conn = connection;
        await using var context = db;

        var blueprint = new Blueprint
        {
            Name = "Student Management"
        };

        db.Blueprints.Add(blueprint);
        await db.SaveChangesAsync();

        var service = new BlueprintVersionService(db);

        var result = await service.CreateVersionAsync(
            blueprint.Id,
            "{\"screens\":[\"Login\"]}",
            "Initial version");

        Assert.Equal(1, result.VersionNumber);
        Assert.Equal(blueprint.Id, result.BlueprintId);
        Assert.Equal(1, await db.BlueprintVersions.CountAsync());
    }

    [Fact]
    public async Task InvalidJson_ThrowsException()
    {
        var (connection, db) = await CreateDbAsync();
        await using var conn = connection;
        await using var context = db;

        var service = new BlueprintVersionService(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateVersionAsync(
                Guid.NewGuid(),
                "{invalid json}"));
    }

    [Fact]
    public async Task MissingBlueprint_ThrowsException()
    {
        var (connection, db) = await CreateDbAsync();
        await using var conn = connection;
        await using var context = db;

        var service = new BlueprintVersionService(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateVersionAsync(
                Guid.NewGuid(),
                "{\"screens\":[]}"));
    }

    [Fact]
    public async Task SecondVersion_IncrementsNumber()
    {
        var (connection, db) = await CreateDbAsync();
        await using var conn = connection;
        await using var context = db;

        var blueprint = new Blueprint
        {
            Name = "Student Management"
        };

        db.Blueprints.Add(blueprint);
        await db.SaveChangesAsync();

        var service = new BlueprintVersionService(db);

        await service.CreateVersionAsync(
            blueprint.Id,
            "{\"screens\":[\"Login\"]}");

        var second = await service.CreateVersionAsync(
            blueprint.Id,
            "{\"screens\":[\"Login\",\"Dashboard\"]}");

        Assert.Equal(2, second.VersionNumber);
        Assert.Equal(2, await db.BlueprintVersions.CountAsync());
    }
}
