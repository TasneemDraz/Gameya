using System.Text.Json;
using Gameya.Infrastructure.Files;
using Gameya.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gameya.Api.Infrastructure;

/// <summary><c>GET /health</c>: postgres, files and hangfire. Responses carry statuses only, never exception text.</summary>
public static class HealthChecks
{
    public static IServiceCollection AddGameyaHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres")
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

internal sealed class PostgresHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await db.Database.CanConnectAsync(ct)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Cannot connect to the database.");
}

/// <summary>The storage root must exist (it is created when missing) and accept a write.</summary>
internal sealed class FilesHealthCheck(StorageRoot root) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(root.Path);
            var probe = Path.Combine(root.Path, $".health-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(probe, "ok", ct);
            File.Delete(probe);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return HealthCheckResult.Unhealthy("The files root is not writable.");
        }
    }
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
