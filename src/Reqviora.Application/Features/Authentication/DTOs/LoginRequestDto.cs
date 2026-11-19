using System.ComponentModel.DataAnnotations;

namespace Reqviora.Application.Features.Authentication.DTOs;

public sealed record LoginRequestDto(
    [Required, EmailAddress, StringLength(320)] string Email,
    [Required, StringLength(128)] string Password);
