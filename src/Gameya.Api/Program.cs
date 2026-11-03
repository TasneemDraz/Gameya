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

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGameyaForwardedHeaders(builder.Configuration);
builder.AddObservability();
builder.Services.AddBackgroundJobs(builder.Configuration);
builder.Services.AddGameyaHealthChecks();

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

app.UseForwardedHeaders();
app.UseObservability();
app.UseExceptionHandler();
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

// `--migrate` is for the local compose stack only: apply EF migrations as the migrator role, then keep running.
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

app.Run();

// Exposed for WebApplicationFactory<Program> in the integration tests.
public partial class Program;
