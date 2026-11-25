
using Reqviora.Domain.Entities;

namespace Reqviora.Api.Features.Administration;

public static class AdminOverviewAccess
{
    public static bool IsEligible(
        User? user,
        IEnumerable<string> adminEmails)
    {
        if (user is null)
            return false;

        if (!user.EmailVerified)
            return false;

        if (!user.MfaEnabled)
            return false;

        if (string.IsNullOrWhiteSpace(
                user.MfaSecretProtected))
            return false;

        if (adminEmails is null)
            return false;

        return adminEmails.Any(email =>
            !string.IsNullOrWhiteSpace(email) &&
            string.Equals(
                email.Trim(),
                user.Email,
                StringComparison.OrdinalIgnoreCase));
    }
}
