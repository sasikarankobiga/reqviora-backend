using Reqviora.Api.Middleware;
using Reqviora.Application;
using Reqviora.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddApplication();
builder.Services.AddInfrastructure();

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program;
