using Reqviora.Application.Common.Interfaces;
using Reqviora.Domain.Entities;

namespace Reqviora.Infrastructure.Authentication;

public sealed class JwtTokenService : ITokenService
{
    public string CreateAccessToken(User user) =>
        throw new NotSupportedException("Configure signed JWT generation before enabling authentication endpoints.");
}
