using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ReqvioraDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'ReqvioraDatabase' is required. Configure it with user secrets or an environment variable.");
        }

        services.AddDbContext<ReqvioraDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(ReqvioraDbContext).Assembly.FullName)));

        services.AddHealthChecks()
            .AddDbContextCheck<ReqvioraDbContext>(
                name: "postgresql",
                tags: ["ready"]);

        return services
            .AddScoped<IUserRepository, UserRepository>()
            .AddScoped<IProjectRepository, ProjectRepository>()
            .AddScoped<IUnitOfWork, UnitOfWork>()
            .AddScoped<ITokenService, JwtTokenService>()
            .AddScoped<IEmailService, EmailService>()
            .AddScoped<IFileStorageService, FileStorageService>()
            .AddScoped<IAIService, AIService>();
    }
}
