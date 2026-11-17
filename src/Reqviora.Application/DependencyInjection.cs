using Microsoft.Extensions.DependencyInjection;
using Reqviora.Application.Features.Authentication.Interfaces;
using Reqviora.Application.Features.Authentication.Services;
using Reqviora.Application.Features.Projects.Interfaces;
using Reqviora.Application.Features.Projects.Services;

namespace Reqviora.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services) => services
        .AddScoped<IAuthService, AuthService>()
        .AddScoped<IProjectService, ProjectService>();
}
