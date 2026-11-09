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
using Microsoft.Extensions.Logging;

namespace Gameya.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Development and E2E are the only environments that may expose OTP codes.</summary>
    public static bool IsDevelopmentOrE2E(this IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("E2E");

    /// <summary>
    /// Runtime uses <c>ConnectionStrings:App</c>; <c>ConnectionStrings:Migrator</c> is only for the migration
    /// runner. On SQL Server both may point at the same single login.
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
    /// <c>Storage:UrlSigningKey</c> (at least 32 bytes) is required, and so is <c>Storage:RootPath</c> for the Local provider (the default) or <c>Storage:S3:*</c> for <c>Storage:Provider=S3</c>. Development and
    /// E2E fall back to a per-process random key (links live five minutes) and a folder under the content root.
    /// </summary>
    private static void AddFiles(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var signingKey = configuration["Storage:UrlSigningKey"];
        if (string.IsNullOrEmpty(signingKey) && environment.IsDevelopmentOrE2E())
            signingKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        if (string.IsNullOrEmpty(signingKey) || System.Text.Encoding.UTF8.GetByteCount(signingKey) < 32)
            throw new InvalidOperationException("Storage:UrlSigningKey must be configured with at least 32 bytes.");

        services.AddSingleton(sp => new SignedFileUrls(signingKey, sp.GetRequiredService<IClock>()));

        var provider = configuration["Storage:Provider"];
        if (string.IsNullOrWhiteSpace(provider) || provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
            AddLocalStore(services, configuration, environment);
        else if (provider.Equals("S3", StringComparison.OrdinalIgnoreCase))
            AddS3Store(services, configuration);
        else
            throw new InvalidOperationException("Storage:Provider must be 'Local' or 'S3'.");

        services.AddSingleton<IImageProcessor, MagickImageProcessor>();
    }

    private static void AddLocalStore(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var rootPath = configuration["Storage:RootPath"];
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            if (!environment.IsDevelopmentOrE2E())
                throw new InvalidOperationException("Storage:RootPath is not configured.");
            rootPath = Path.Combine(environment.ContentRootPath, ".data", "files");
        }

        var fullRoot = Path.GetFullPath(rootPath);
        services.AddSingleton(new StorageRoot(fullRoot));
        services.AddSingleton(sp => new LocalFileStore(fullRoot, sp.GetRequiredService<SignedFileUrls>()));
        services.AddSingleton<IFileStore>(sp => sp.GetRequiredService<LocalFileStore>());
        services.AddSingleton<IStorageHealthProbe>(sp => sp.GetRequiredService<LocalFileStore>());
    }

    private static void AddS3Store(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(S3StorageOptions.Section).Get<S3StorageOptions>() ?? new S3StorageOptions();
        Require(options.ServiceUrl, nameof(options.ServiceUrl));
        Require(options.Bucket, nameof(options.Bucket));
        Require(options.AccessKeyId, nameof(options.AccessKeyId));
        Require(options.SecretAccessKey, nameof(options.SecretAccessKey));
        Require(options.Region, nameof(options.Region));

        services.AddSingleton(sp =>
            new S3FileStore(
                S3ClientFactory.Create(options), options.Bucket, sp.GetRequiredService<SignedFileUrls>(),
                sp.GetRequiredService<ILogger<S3FileStore>>()));
        services.AddSingleton<IFileStore>(sp => sp.GetRequiredService<S3FileStore>());
        services.AddSingleton<IStorageHealthProbe>(sp => sp.GetRequiredService<S3FileStore>());

        static void Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Storage:S3:{name} is required when Storage:Provider is 'S3'.");
        }
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
        // The dev OTP endpoint is mapped in Development and E2E whatever the provider, and needs the store.
        // With SmsGate the store simply stays empty, so the endpoint answers NOT_FOUND instead of failing.
        if (environment.IsDevelopmentOrE2E())
        {
            services.AddSingleton<DevOtpStore>();
        }

        switch (configuration["Sms:Provider"])
        {
            case "Logging" when environment.IsDevelopmentOrE2E():
                services.AddSingleton<ISmsSender, LoggingSmsSender>();
                break;
            case "SmsGate":
                // Without credentials every OTP would answer 503 while the host still looks healthy.
                foreach (var key in new[] { "Username", "Password" })
                {
                    if (string.IsNullOrWhiteSpace(configuration[$"{SmsGateOptions.Section}:{key}"]))
                        throw new InvalidOperationException(
                            $"{SmsGateOptions.Section}:{key} is required when Sms:Provider is 'SmsGate'.");
                }

                services.Configure<SmsGateOptions>(configuration.GetSection(SmsGateOptions.Section));
                services.AddHttpClient<ISmsSender, SmsGateSmsSender>(c => c.Timeout = SmsGateOptions.Timeout);
                break;
            case var other:
                throw new InvalidOperationException(
                    $"Sms:Provider '{other}' is not supported here. Use 'SmsGate', or 'Logging' in Development/E2E.");
        }
    }
}
