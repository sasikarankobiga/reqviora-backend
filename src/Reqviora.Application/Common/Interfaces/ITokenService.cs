using Reqviora.Domain.Entities;

namespace Reqviora.Application.Common.Interfaces;

public interface ITokenService
{
    string CreateAccessToken(User user);
}
