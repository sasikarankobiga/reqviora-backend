using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Reqviora.Application.Features.Authentication.Interfaces;
using Reqviora.Application.Features.Authentication.Services;
using Reqviora.Application.Features.Projects.Interfaces;
using Reqviora.Application.Features.Projects.Services;
using Reqviora.Domain.Entities;

namespace Reqviora.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services) => services
        .AddScoped<IPasswordHasher<User>, PasswordHasher<User>>()
        .AddScoped<IAuthService, AuthService>()
        .AddScoped<IProjectService, ProjectService>();
}
