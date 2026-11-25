using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;
using Reqviora.Api.Features.RequirementReview;
using Reqviora.Api.Configuration;
using Reqviora.Api.Middleware;
using Reqviora.Application;
using Reqviora.Infrastructure;
using System.Threading.RateLimiting;
using System.Net;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

ProductionConfigurationValidator.Validate(builder.Configuration, builder.Environment);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.RequireHeaderSymmetry = true;
    options.KnownProxies.Clear();
    foreach (var value in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
    {
        if (!IPAddress.TryParse(value, out var address))
            throw new InvalidOperationException($"Invalid trusted proxy address: {value}");
        options.KnownProxies.Add(address);
    }
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static string Partition(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } userId
            ? $"user:{userId}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            Partition(context),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("authentication", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            Partition(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "REQVIORA API",
        Version = "v1",
        Description = "Backend API for the REQVIORA project workspace."
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter the access token returned by the login or refresh endpoint."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.AddApplication();
builder.Services.AddScoped<ReviewApiDecisionBoundary>();
builder.Services.AddScoped<Reqviora.Api.Features.Authentication.EmailVerificationSender>();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (builder.Configuration.GetValue("ForwardedHeaders:Enabled", false))
    app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<AuditMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors("Frontend");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "REQVIORA API v1");
        options.RoutePrefix = "swagger";
    });
}

app.MapGet("/", (IHostEnvironment environment) =>
        environment.IsDevelopment()
            ? Results.Redirect("/swagger")
            : Results.Ok(new
            {
                name = "REQVIORA API",
                status = "running",
                health = "/health/live"
            }))
    .ExcludeFromDescription()
    .AllowAnonymous();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
}).AllowAnonymous();

app.Run();

public partial class Program;
