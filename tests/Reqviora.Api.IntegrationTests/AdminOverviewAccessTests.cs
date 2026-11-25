
using Reqviora.Api.Features.Administration;
using Reqviora.Domain.Entities;
using Xunit;

namespace Reqviora.Api.IntegrationTests;

public sealed class AdminOverviewAccessTests
{
    private static User CreateAdmin()
    {
        return new User
        {
            Email = "admin@example.test",
            NormalizedEmail = "ADMIN@EXAMPLE.TEST",
            DisplayName = "Test Admin",
            EmailVerified = true,
            MfaEnabled = true,
            MfaSecretProtected = "protected-test-secret"
        };
    }

    [Fact]
    public void Null_user_must_be_rejected()
    {
        Assert.False(AdminOverviewAccess.IsEligible(
            null,
            ["admin@example.test"]));
    }

    [Fact]
    public void Empty_admin_allowlist_must_be_rejected()
    {
        Assert.False(AdminOverviewAccess.IsEligible(
            CreateAdmin(),
            []));
    }

    [Fact]
    public void Non_admin_user_must_be_rejected()
    {
        Assert.False(AdminOverviewAccess.IsEligible(
            CreateAdmin(),
            ["other@example.test"]));
    }

    [Fact]
    public void Unverified_email_must_be_rejected()
    {
        var user = CreateAdmin();
        user.EmailVerified = false;

        Assert.False(AdminOverviewAccess.IsEligible(
            user,
            ["admin@example.test"]));
    }

    [Fact]
    public void Disabled_mfa_must_be_rejected()
    {
        var user = CreateAdmin();
        user.MfaEnabled = false;

        Assert.False(AdminOverviewAccess.IsEligible(
            user,
            ["admin@example.test"]));
    }

    [Fact]
    public void Missing_mfa_secret_must_be_rejected()
    {
        var user = CreateAdmin();
        user.MfaSecretProtected = null;

        Assert.False(AdminOverviewAccess.IsEligible(
            user,
            ["admin@example.test"]));
    }

    [Fact]
    public void Verified_admin_with_mfa_is_eligible()
    {
        Assert.True(AdminOverviewAccess.IsEligible(
            CreateAdmin(),
            ["admin@example.test"]));
    }

    [Fact]
    public void Email_comparison_is_case_insensitive()
    {
        Assert.True(AdminOverviewAccess.IsEligible(
            CreateAdmin(),
            [" ADMIN@EXAMPLE.TEST "]));
    }
}
