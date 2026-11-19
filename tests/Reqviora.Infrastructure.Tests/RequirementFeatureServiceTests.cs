
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Reqviora.Domain.Entities;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Services;
using Xunit;

namespace Reqviora.Infrastructure.Tests;

public class RequirementFeatureServiceTests
{
    private static async Task<(
        SqliteConnection Connection,
        ReqvioraDbContext Context)> CreateDatabaseAsync()
    {
        // Temporary database for isolated service tests.
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

    // Test 1: Requirement and Feature belong to same project.
    [Fact]
    public async Task LinkAsync_SameProject_CreatesLink()
    {
        var (connection, context) = await CreateDatabaseAsync();
        await using var dbConnection = connection;
        await using var dbContext = context;

        var projectId = Guid.NewGuid();

        var requirement = new Requirement
        {
            ProjectId = projectId,
            Title = "User Login",
            Description = "User can login"
        };

        var feature = new Feature
        {
            ProjectId = projectId,
            Name = "Authentication"
        };

        context.Requirements.Add(requirement);
        context.Features.Add(feature);
        await context.SaveChangesAsync();

        var service = new RequirementFeatureService(context);

        var result = await service.LinkAsync(
            requirement.Id,
            feature.Id);

        Assert.Equal(requirement.Id, result.RequirementId);
        Assert.Equal(feature.Id, result.FeatureId);
        Assert.Equal(1, await context.RequirementFeatures.CountAsync());
    }

    // Test 2: Different projects must not be linked.
    [Fact]
    public async Task LinkAsync_DifferentProjects_ThrowsException()
    {
        var (connection, context) = await CreateDatabaseAsync();
        await using var dbConnection = connection;
        await using var dbContext = context;

        var requirement = new Requirement
        {
            ProjectId = Guid.NewGuid(),
            Title = "Login Requirement"
        };

        var feature = new Feature
        {
            ProjectId = Guid.NewGuid(),
            Name = "Authentication"
        };

        context.Requirements.Add(requirement);
        context.Features.Add(feature);
        await context.SaveChangesAsync();

        var service = new RequirementFeatureService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.LinkAsync(
                requirement.Id,
                feature.Id));

        Assert.Equal(0, await context.RequirementFeatures.CountAsync());
    }

    // Test 3: Duplicate links must not be created.
    [Fact]
    public async Task LinkAsync_DuplicateLink_ReturnsExistingLink()
    {
        var (connection, context) = await CreateDatabaseAsync();
        await using var dbConnection = connection;
        await using var dbContext = context;

        var projectId = Guid.NewGuid();

        var requirement = new Requirement
        {
            ProjectId = projectId,
            Title = "Register"
        };

        var feature = new Feature
        {
            ProjectId = projectId,
            Name = "Registration"
        };

        context.Requirements.Add(requirement);
        context.Features.Add(feature);
        await context.SaveChangesAsync();

        var service = new RequirementFeatureService(context);

        var first = await service.LinkAsync(
            requirement.Id,
            feature.Id);

        var second = await service.LinkAsync(
            requirement.Id,
            feature.Id);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await context.RequirementFeatures.CountAsync());
    }

    // Test 4: Missing requirement is rejected.
    [Fact]
    public async Task LinkAsync_MissingRequirement_ThrowsException()
    {
        var (connection, context) = await CreateDatabaseAsync();
        await using var dbConnection = connection;
        await using var dbContext = context;

        var service = new RequirementFeatureService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.LinkAsync(
                Guid.NewGuid(),
                Guid.NewGuid()));
    }
}
