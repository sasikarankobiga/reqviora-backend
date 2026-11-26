namespace Reqviora.Infrastructure.Authentication;

internal static class SessionFingerprint
{
    public const string ClaimType = "session_ver";

    public static string FromUser(Reqviora.Domain.Entities.User user) =>
        user.SessionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static bool Matches(string presented, int currentVersion) =>
        int.TryParse(presented, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var version) &&
        version == currentVersion;
}
