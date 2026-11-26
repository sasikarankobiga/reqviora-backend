using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Reqviora.Api.Features.Authentication;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Application.Features.Authentication.DTOs;
using Reqviora.Application.Features.Authentication.Interfaces;

namespace Reqviora.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("authentication")]
public sealed class AuthController(IAuthService authService, ReqvioraDbContext db, EmailVerificationSender verification) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType<RegistrationResponseDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterRequestDto request, CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);
        if (!result.Succeeded)
            return Problem(title: result.Error, statusCode: StatusCodes.Status409Conflict);

        var profile = result.Value!;
        var user = await db.Users.SingleAsync(x => x.Id == profile.UserId, cancellationToken);
        var sent = await verification.SendAsync(user, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            new RegistrationResponseDto(profile.UserId, profile.Email, profile.DisplayName, profile.EmailVerified, sent));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequestDto request, CancellationToken cancellationToken)
    {
        request = request with
        {
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            DeviceId = string.IsNullOrWhiteSpace(request.DeviceId)
                ? Request.Headers.UserAgent.ToString()[..Math.Min(Request.Headers.UserAgent.ToString().Length, 200)]
                : request.DeviceId
        };
        var result = await authService.LoginAsync(request, cancellationToken);
        return result.Succeeded
            ? Ok(result.Value)
            : Problem(title: result.Error, statusCode: StatusCodes.Status401Unauthorized);
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType<AuthResponseDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(RefreshTokenRequestDto request, CancellationToken cancellationToken)
    {
        var result = await authService.RefreshAsync(request, cancellationToken);
        return result.Succeeded
            ? Ok(result.Value)
            : Problem(title: result.Error, statusCode: StatusCodes.Status401Unauthorized);
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshTokenRequestDto request, CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(request, cancellationToken);
        return NoContent();
    }
}
