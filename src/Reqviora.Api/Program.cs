using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Reqviora.Api.Features.RequirementReview;
using Reqviora.Api.Middleware;
using Reqviora.Application;
using Reqviora.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
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
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors("Frontend");
app.UseAuthentication();
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
    .ExcludeFromDescription();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.Run();

public partial class Program;
