using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Common.Models;
using Reqviora.Application.Features.Authentication.DTOs;
using Reqviora.Application.Features.Authentication.Interfaces;
using Reqviora.Domain.Entities;

namespace Reqviora.Application.Features.Authentication.Services;

public sealed class AuthService(IUserRepository users, ITokenService tokens) : IAuthService
{
    public async Task<Result<AuthResponseDto>> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        var user = await users.GetByEmailAsync(request.Email, cancellationToken);
        if (user is null || user.PasswordHash != request.Password)
            return Result<AuthResponseDto>.Failure("Invalid email or password.");

        return Result<AuthResponseDto>.Success(ToResponse(user));
    }

    public async Task<Result<AuthResponseDto>> RegisterAsync(RegisterRequestDto request, CancellationToken cancellationToken = default)
    {
        if (await users.GetByEmailAsync(request.Email, cancellationToken) is not null)
            return Result<AuthResponseDto>.Failure("Email is already registered.");

        var user = new User { Email = request.Email, PasswordHash = request.Password, DisplayName = request.DisplayName };
        await users.AddAsync(user, cancellationToken);
        return Result<AuthResponseDto>.Success(ToResponse(user));
    }

    private AuthResponseDto ToResponse(User user) => new(user.Id, user.Email, user.DisplayName, tokens.CreateAccessToken(user));
}
