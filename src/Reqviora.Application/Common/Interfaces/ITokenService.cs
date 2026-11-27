using Reqviora.Domain.Entities;

namespace Reqviora.Application.Common.Interfaces;

public interface ITokenService
{
    AccessTokenResult CreateAccessToken(User user);
    AccessTokenResult CreateAccessToken(User user, Guid sessionId) => CreateAccessToken(user);
    RefreshTokenResult CreateRefreshToken();
    string HashRefreshToken(string token);
}

public static class TokenClaimTypes
{
    public const string SessionId = "reqviora_session_id";
    public const string AdministratorMfa = "reqviora_administrator_mfa";
}

public sealed record AccessTokenResult(string Token, DateTimeOffset ExpiresAt);
public sealed record RefreshTokenResult(string Token, string TokenHash, DateTimeOffset ExpiresAt);
