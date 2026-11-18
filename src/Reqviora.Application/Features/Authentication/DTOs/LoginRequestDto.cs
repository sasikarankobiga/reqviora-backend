using System.ComponentModel.DataAnnotations;

namespace Reqviora.Application.Features.Authentication.DTOs;

public sealed record LoginRequestDto(
    [property: Required, EmailAddress, StringLength(320)] string Email,
    [property: Required, StringLength(128)] string Password);
