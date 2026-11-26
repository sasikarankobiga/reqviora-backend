namespace Reqviora.Application.Features.Authentication.DTOs;

public sealed record RegistrationResponseDto(
    Guid UserId,
    string Email,
    string DisplayName,
    bool EmailVerified,
    bool VerificationEmailSent);
