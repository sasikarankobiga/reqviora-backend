using Microsoft.Extensions.DependencyInjection;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.Authentication.Interfaces;
using Reqviora.Application.Features.Projects.Interfaces;
using Reqviora.Infrastructure.Authentication;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Repositories;
using Reqviora.Infrastructure.Services;

namespace Reqviora.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services) => services
        .AddSingleton<IUserRepository, UserRepository>()
        .AddSingleton<IProjectRepository, ProjectRepository>()
        .AddScoped<IUnitOfWork, UnitOfWork>()
        .AddScoped<ITokenService, JwtTokenService>()
        .AddScoped<IEmailService, EmailService>()
        .AddScoped<IFileStorageService, FileStorageService>()
        .AddScoped<IAIService, AIService>();
}
