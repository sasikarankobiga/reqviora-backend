using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
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

        var jwtSection = configuration.GetSection(JwtOptions.SectionName);
        var jwtOptions = jwtSection.Get<JwtOptions>()
            ?? throw new InvalidOperationException("JWT configuration is required.");
        if (string.IsNullOrWhiteSpace(jwtOptions.Issuer) ||
            string.IsNullOrWhiteSpace(jwtOptions.Audience) ||
            jwtOptions.Key.Length < 32 ||
            jwtOptions.AccessTokenMinutes is < 1 or > 60 ||
            jwtOptions.RefreshTokenDays is < 1 or > 90)
        {
            throw new InvalidOperationException(
                "JWT configuration is invalid. Use a secret key of at least 32 characters and supported token lifetimes.");
        }

        services.Configure<JwtOptions>(jwtSection);
        services.AddSingleton(TimeProvider.System);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimTypes.Name,
                    RoleClaimType = ClaimTypes.Role
                };
            });
        services.AddAuthorization();

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
