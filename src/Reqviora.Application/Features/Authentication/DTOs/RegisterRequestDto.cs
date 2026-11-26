
using System.ComponentModel.DataAnnotations;

namespace Reqviora.Application.Features.Authentication.DTOs;

public sealed record RegisterRequestDto(
    [Required, EmailAddress, StringLength(320)] string Email,
    [Required, StringLength(128, MinimumLength = 8)] string Password,
    [Required, StringLength(200, MinimumLength = 2)] string DisplayName);
