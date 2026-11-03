using Gameya.Application.Abstractions;
using Gameya.Infrastructure.Persistence;
using Gameya.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gameya.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Runtime uses the restricted <c>ConnectionStrings:App</c> role; <c>ConnectionStrings:Migrator</c>
    /// is only for the migration runner.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("App")
            ?? throw new InvalidOperationException("ConnectionStrings:App is not configured.");

        services.AddDbContext<AppDbContext>(options =>
        {
            AppDbContext.Configure(options, connectionString);
            if (environment.IsDevelopment()) options.EnableDetailedErrors();
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
