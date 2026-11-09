using System.Text;
using System.Text.Json.Serialization;
using Gameya.Api.Endpoints;
using Gameya.Api.Infrastructure;
using Gameya.Application;
using Gameya.Application.Abstractions;
using Gameya.Infrastructure;
using Gameya.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// A failure while the host is being built happens before Serilog exists and IIS discards console output, so the
// exception is appended to logs/startup-error.log under the content root before it is rethrown.
var contentRoot = AppContext.BaseDirectory;
try
{
    var builder = WebApplication.CreateBuilder(args);
    contentRoot = builder.Environment.ContentRootPath;

    // Secrets come from a file in the content root (never wwwroot), resolved against the host's content root
    // rather than the working directory, which IIS sets differently. The source sits right after the appsettings
    // files, so environment variables, the command line and host settings still override it.
    {
        var sources = builder.Configuration.Sources;
        var lastAppSettings = -1;
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is Microsoft.Extensions.Configuration.Json.JsonConfigurationSource json
                && Path.GetFileName(json.Path)?.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) == true)
            {
                lastAppSettings = i;
            }
        }

        sources.Insert(lastAppSettings + 1, new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
        {
            FileProvider = builder.Environment.ContentRootFileProvider,
            Path = "secrets.json",
            Optional = true,
            ReloadOnChange = false
        });
    }

    // Kestrel adds its Server header after OnStarting callbacks run, so it can only be switched off here.
    builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

    builder.Services.AddGameyaForwardedHeaders(builder.Configuration);
    builder.AddObservability();
    builder.Services.AddBackgroundJobs(builder.Configuration);
    builder.Services.AddGameyaHealthChecks();
    builder.Services.AddGameyaSpaCompression();

    builder.Services.AddApplication(builder.Configuration);
    builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

    builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
        c.ProblemDetails.Extensions["traceId"] = c.HttpContext.GetCorrelationId());
    builder.Services.AddExceptionHandler<ProblemDetailsMapping>();
    builder.Services.AddOtpRateLimiting();

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidAudience = builder.Configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SigningKey"]!)),
                NameClaimType = "sub",
                RoleClaimType = "role",
                ClockSkew = TimeSpan.FromSeconds(30)
            };
        });
    builder.Services.AddAuthorization();

    var app = builder.Build();

    // `--migrate` is for the local compose stack only: apply EF migrations (this creates the database), then keep running.
    // It must run before anything touches Hangfire storage, which needs the database to exist.
    if (args.Contains("--migrate"))
    {
        if (!app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException("--migrate is only allowed in the Development environment.");
        }

        var migrator = app.Configuration.GetConnectionString("Migrator")
            ?? throw new InvalidOperationException("ConnectionStrings:Migrator is not configured.");
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>();
        AppDbContext.Configure(dbOptions, migrator);
        await using (var db = new AppDbContext(dbOptions.Options))
        {
            await db.Database.MigrateAsync();
        }
    }

    app.UseForwardedHeaders();
    app.UseGameyaSecurityHeaders();
    app.UseObservability();
    app.UseExceptionHandler();
    app.UseGameyaSpaStaticFiles();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapGameyaHealth();
    app.MapAuthEndpoints();
    app.MapMeEndpoints();
    app.MapFileEndpoints();
    if (app.Environment.IsDevelopmentOrE2E())
    {
        app.MapDevEndpoints();
    }

    if (app.Environment.IsDevelopment())
    {
        app.MapDevHangfireDashboard();
    }

    // Last, so every real endpoint wins over the SPA shell.
    app.MapGameyaSpaFallback();

    app.Run();
}
// HostAbortedException is how EF tooling stops the host on purpose after Build(); it is not a startup failure.
catch (Exception ex) when (ex is not HostAbortedException)
{
    StartupErrorLog.Write(contentRoot, ex);
    throw;
}

// Exposed for WebApplicationFactory<Program> in the integration tests.
public partial class Program;
