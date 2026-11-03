using Gameya.Application.Abstractions;
using Gameya.Infrastructure.Email;
using Gameya.Infrastructure.Files;
using Gameya.Infrastructure.Participation;
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
        services.AddSingleton<IActiveParticipationChecker, NoParticipationChecker>();

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
        AddEmail(services, configuration, environment);
        AddFiles(services, configuration, environment);

        return services;
    }

    /// <summary>
    /// <c>Storage:UrlSigningKey</c> (at least 32 bytes) and <c>Storage:RootPath</c> are required. Development and
    /// E2E fall back to a per-process random key (links live five minutes) and a folder under the content root.
    /// </summary>
    private static void AddFiles(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var signingKey = configuration["Storage:UrlSigningKey"];
        if (string.IsNullOrEmpty(signingKey) && environment.IsDevelopmentOrE2E())
            signingKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        if (string.IsNullOrEmpty(signingKey) || System.Text.Encoding.UTF8.GetByteCount(signingKey) < 32)
            throw new InvalidOperationException("Storage:UrlSigningKey must be configured with at least 32 bytes.");

        var rootPath = configuration["Storage:RootPath"];
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            if (!environment.IsDevelopmentOrE2E())
                throw new InvalidOperationException("Storage:RootPath is not configured.");
            rootPath = Path.Combine(environment.ContentRootPath, ".data", "files");
        }

        services.AddSingleton(sp => new SignedFileUrls(signingKey, sp.GetRequiredService<IClock>()));
        services.AddSingleton<IFileStore>(sp =>
            new LocalFileStore(Path.GetFullPath(rootPath), sp.GetRequiredService<SignedFileUrls>()));
        services.AddSingleton<IImageProcessor, MagickImageProcessor>();
    }

    /// <summary>
    /// One SMTP sender everywhere. Outside Development and E2E the connection must use STARTTLS.
    /// </summary>
    private static void AddEmail(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection(EmailOptions.Section).Get<EmailOptions>() ?? new EmailOptions();
        if (string.IsNullOrWhiteSpace(options.From) || string.IsNullOrWhiteSpace(options.Smtp.Host))
            throw new InvalidOperationException("Email:From and Email:Smtp:Host must be configured.");
        if (!options.Smtp.UseStartTls && !environment.IsDevelopmentOrE2E())
            throw new InvalidOperationException("Email:Smtp:UseStartTls must be true outside Development and E2E.");
        if (string.IsNullOrWhiteSpace(configuration["App:WebBaseUrl"]))
            throw new InvalidOperationException("App:WebBaseUrl must be configured.");

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Section));
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
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
