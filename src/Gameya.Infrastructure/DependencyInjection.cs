using Gameya.Application.Abstractions;
using Gameya.Infrastructure.Persistence;
using Gameya.Infrastructure.Security;
using Gameya.Infrastructure.Sms;
using Gameya.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gameya.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Development and E2E are the only environments that may expose OTP codes.</summary>
    public static bool IsDevelopmentOrE2E(this IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("E2E");

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

        var hashKey = configuration["Security:HashKey"];
        if (string.IsNullOrWhiteSpace(hashKey))
            throw new InvalidOperationException("Security:HashKey is not configured.");
        services.AddSingleton<ISecretHasher>(new HmacSecretHasher(hashKey));

        var jwt = configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        if (jwt.SigningKey.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be configured with at least 32 characters.");
        services.AddSingleton(jwt);
        services.AddSingleton<ITokenService, JwtTokenService>();

        AddSms(services, configuration, environment);

        return services;
    }

    private static void AddSms(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        switch (configuration["Sms:Provider"])
        {
            case "Logging" when environment.IsDevelopmentOrE2E():
                services.AddSingleton<DevOtpStore>();
                services.AddSingleton<ISmsSender, LoggingSmsSender>();
                break;
            case "SmsGate":
                services.Configure<SmsGateOptions>(configuration.GetSection(SmsGateOptions.Section));
                services.AddHttpClient<ISmsSender, SmsGateSmsSender>(c => c.Timeout = SmsGateOptions.Timeout);
                break;
            case var other:
                throw new InvalidOperationException(
                    $"Sms:Provider '{other}' is not supported here. Use 'SmsGate', or 'Logging' in Development/E2E.");
        }
    }
}
