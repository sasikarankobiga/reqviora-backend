namespace Reqviora.Application.Features.Authentication.DTOs;

public sealed record AuthResponseDto(Guid UserId, string Email, string DisplayName, string AccessToken);
