using System.Text.Json;
using Gameya.Application.Abstractions;
using Gameya.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gameya.Api.Infrastructure;

/// <summary><c>GET /health</c>: database, files and hangfire. Responses carry statuses only, never exception text.</summary>
public static class HealthChecks
{
    public static IServiceCollection AddGameyaHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database")
            .AddCheck<FilesHealthCheck>("files")
            .AddCheck<HangfireHealthCheck>("hangfire");
        return services;
    }

    public static IEndpointRouteBuilder MapGameyaHealth(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, new
                {
                    status = report.Status.ToString(),
                    entries = report.Entries.ToDictionary(
                        e => e.Key,
                        e => new { status = e.Value.Status.ToString(), durationMs = (long)e.Value.Duration.TotalMilliseconds })
                }, cancellationToken: context.RequestAborted);
            }
        });
        return app;
    }
}

internal sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await db.Database.CanConnectAsync(ct)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Cannot connect to the database.");
}

/// <summary>The configured file storage (local folder or S3 bucket) must accept a write.</summary>
internal sealed class FilesHealthCheck(IStorageHealthProbe probe) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await probe.IsWritableAsync(ct)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The file storage is not writable.");
}

/// <summary>Reads the job statistics, which proves the Hangfire storage (schema <c>hangfire</c>) is reachable.</summary>
internal sealed class HangfireHealthCheck(JobStorage storage) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var stats = storage.GetMonitoringApi().GetStatistics();
            return Task.FromResult(HealthCheckResult.Healthy(data: new Dictionary<string, object>
            {
                ["servers"] = stats.Servers,
                ["failed"] = stats.Failed
            }));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Hangfire storage is unreachable."));
        }
    }
}
