using System.Text;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Reqviora.Application.Common.Interfaces;
using Reqviora.Application.Features.Authentication.Interfaces;
using Reqviora.Application.Features.Projects.Interfaces;
using Reqviora.Application.Features.RequirementReview;
using Reqviora.Application.Features.AIOrchestration;
using Reqviora.Application.Features.VoiceProcessing;
using Reqviora.Application.Features.DocumentIntelligence;
using Reqviora.Infrastructure.Authentication;
using Reqviora.Infrastructure.Authorization;
using Reqviora.Infrastructure.Persistence;
using Reqviora.Infrastructure.Repositories;
using Reqviora.Infrastructure.Services;
using Reqviora.Infrastructure.Security;
using Reqviora.Review.Persistence;

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

        var databaseProvider = configuration["Database:Provider"] ?? "PostgreSql";
        services.AddDbContext<ReqvioraDbContext>(options =>
        {
            if (databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly("Reqviora.SqlServer.Persistence"));
            else
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsAssembly(typeof(ReqvioraDbContext).Assembly.FullName));
        });

        var healthChecks = services.AddHealthChecks()
            .AddDbContextCheck<ReqvioraDbContext>(
                name: databaseProvider,
                tags: ["ready"])
            .AddCheck<ReviewSchemaHealthCheck>("review-schema", tags: ["ready"],
                timeout: TimeSpan.FromSeconds(15));

        var smtp = configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>() ?? new SmtpOptions();
        if (!string.IsNullOrWhiteSpace(smtp.Host))
            healthChecks.AddCheck("smtp", new TcpEndpointHealthCheck(smtp.Host, smtp.Port), tags: ["ready"]);
        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        if (storage.RequireAntivirus)
            healthChecks.AddCheck("antivirus", new TcpEndpointHealthCheck(storage.ClamAvHost, storage.ClamAvPort), tags: ["ready"]);

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
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var subject = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? context.Principal?.FindFirstValue("sub");
                        var presented = context.Principal?.FindFirstValue(SessionFingerprint.ClaimType);
                        if (!Guid.TryParse(subject, out var userId) || string.IsNullOrWhiteSpace(presented))
                        {
                            context.Fail("The session is no longer valid.");
                            return;
                        }

                        var db = context.HttpContext.RequestServices.GetRequiredService<ReqvioraDbContext>();
                        var state = await db.Users.AsNoTracking()
                            .Where(user => user.Id == userId)
                            .Select(user => new
                            {
                                user.SessionVersion,
                                user.Email,
                                RoleName = user.RoleId == null ? null : db.Roles
                                    .Where(role => role.Id == user.RoleId)
                                    .Select(role => role.Name)
                                    .SingleOrDefault()
                            })
                            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                        if (state is null || !SessionFingerprint.Matches(presented, state.SessionVersion))
                        {
                            context.Fail("The session is no longer valid.");
                            return;
                        }

                        if (context.Principal?.Identity is ClaimsIdentity identity)
                        {
                            if (!string.IsNullOrWhiteSpace(state.RoleName) && !identity.HasClaim(ClaimTypes.Role, state.RoleName))
                                identity.AddClaim(new Claim(ClaimTypes.Role, state.RoleName));
                            var adminEmails = configuration.GetSection("Security:AdminEmails").Get<string[]>() ?? [];
                            if (adminEmails.Contains(state.Email, StringComparer.OrdinalIgnoreCase) &&
                                !identity.HasClaim(ClaimTypes.Role, "Administrator"))
                                identity.AddClaim(new Claim(ClaimTypes.Role, "Administrator"));
                        }
                    },
                    OnChallenge = async context =>
                    {
                        if (context.Response.HasStarted) return;
                        context.HandleResponse();
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/problem+json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(new
                        {
                            type = "https://httpstatuses.com/401",
                            title = "Authentication is required.",
                            status = StatusCodes.Status401Unauthorized,
                            instance = context.Request.Path.Value,
                            correlationId = context.HttpContext.TraceIdentifier
                        }), context.HttpContext.RequestAborted);
                    },
                    OnForbidden = async context =>
                    {
                        if (context.Response.HasStarted) return;
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/problem+json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(new
                        {
                            type = "https://httpstatuses.com/403",
                            title = "Authorization is denied.",
                            status = StatusCodes.Status403Forbidden,
                            instance = context.Request.Path.Value,
                            correlationId = context.HttpContext.TraceIdentifier
                        }), context.HttpContext.RequestAborted);
                    }
                };
            });
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
            options.AddPolicy("Administrator", policy =>
                policy.RequireAuthenticatedUser().RequireRole("Admin", "Administrator"));
        });
        services.AddReqvioraDataProtection(configuration);
        services.AddScoped<LoginSecurityService>();
        services.AddScoped<ILoginSecurityService>(sp => sp.GetRequiredService<LoginSecurityService>());

        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        var ai = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
        services.AddHttpClient<IAIService, AIService>(client =>
        {
            client.BaseAddress = new Uri(ai.Endpoint, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(ai.TimeoutSeconds, 10, 600));
        });
        services.AddScoped<IStructuredAiProvider, StructuredAiProvider>();
        services.AddHttpClient<IVoiceTranscriber, OpenAiCompatibleVoiceTranscriber>(client =>
        {
            client.BaseAddress = new Uri(ai.Endpoint, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(ai.TimeoutSeconds, 10, 600));
        });
        services.AddSingleton<AIBackgroundQueue>();
        services.AddSingleton<IAIJobQueue>(sp => sp.GetRequiredService<AIBackgroundQueue>());
        services.AddHostedService(sp => sp.GetRequiredService<AIBackgroundQueue>());
        services.AddHostedService<RetentionWorker>();

        return services
            .AddScoped<IUserRepository, UserRepository>()
            .AddScoped<IProjectRepository, ProjectRepository>()
            .AddScoped<IRequirementReviewAccessPolicy, ProjectOwnerRequirementReviewAccessPolicy>()
            .AddScoped<IRequirementReviewStore>(_ => databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                ? new SqlServerRequirementReviewStore(connectionString)
                : new PostgresRequirementReviewStore(connectionString))
            .AddScoped<IClarificationStore>(_ => new ClarificationStore(connectionString,
                databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)))
            .AddScoped<IRequirementCandidateStore>(_ => new RequirementCandidateStore(connectionString,
                databaseProvider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)))
            .AddScoped<IUnitOfWork, UnitOfWork>()
            .AddScoped<ProjectDraftService>()
            .AddScoped<ProjectExportService>()
            .AddScoped<ITokenService, JwtTokenService>()
            .AddScoped<IEmailService, EmailService>()
            .AddScoped<IDocumentTextParser, DocumentTextParser>()
            .AddScoped<IFileStorageService, FileStorageService>();
    }
}
