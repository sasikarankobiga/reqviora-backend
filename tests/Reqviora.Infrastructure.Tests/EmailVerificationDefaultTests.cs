using Microsoft.EntityFrameworkCore;

namespace Reqviora.Infrastructure.Tests;

public sealed class EmailVerificationDefaultTests
{
    [Fact]
    public async Task Direct_insert_omitting_email_verified_uses_false_database_default()
    {
        await using var database = await PersistenceDatabase.CreateAsync("sqlite");
        await using var db = database.Open();
        var userId = Guid.NewGuid();

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Users"
                ("Id", "Email", "NormalizedEmail", "PasswordHash", "DisplayName", "MfaEnabled", "CreatedAt")
            VALUES
                ({userId}, {"direct@example.com"}, {"DIRECT@EXAMPLE.COM"}, {"HASH"}, {"Direct Insert"}, {false}, {DateTimeOffset.UtcNow})
            """);

        var emailVerified = await db.Users
            .Where(user => user.Id == userId)
            .Select(user => user.EmailVerified)
            .SingleAsync();

        Assert.False(emailVerified);
    }
}
