namespace Reqviora.Application.Features.Authentication.DTOs;

public sealed record RegisterRequestDto(string Email, string Password, string DisplayName);
