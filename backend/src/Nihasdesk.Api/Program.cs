using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Nihasdesk.Api;
using Nihasdesk.Api.Middleware;
using Nihasdesk.Infrastructure.Health;

const string DashboardCorsPolicy = "dashboard";
const long MaxRequestBodyBytes = 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    // Default body limit (SEC-21); upload endpoints raise it individually.
    options.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
});

// RFC 9457 Problem Details with a traceId on every error; no stack traces outside Development.
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);

// Exact origins only, never "*" with credentials (SEC-16).
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy(DashboardCorsPolicy, policy => policy
        .WithOrigins(allowedOrigins)
        .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
        .AllowAnyHeader()
        .AllowCredentials()));

builder.Services.AddDependencyHealthChecks(builder.Configuration);

foreach (var module in ModuleCatalog.All)
{
    module.AddServices(builder.Services, builder.Configuration);
}

var app = builder.Build();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors(DashboardCorsPolicy);

// Liveness: the process is up. Readiness: dependencies are reachable.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(HealthCheckExtensions.ReadyTag),
});

foreach (var module in ModuleCatalog.All)
{
    module.MapEndpoints(app);
}

await app.RunAsync();

/// <summary>Entry point; public for integration tests.</summary>
public partial class Program;
