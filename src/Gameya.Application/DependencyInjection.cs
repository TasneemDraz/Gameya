using FluentValidation;
using Gameya.Application.Abstractions;
using Gameya.Application.Behaviors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gameya.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.LicenseKey = configuration["MediatR:LicenseKey"];
            cfg.RegisterServicesFromAssembly(assembly);
            // Order: Validation, Authorization, Transaction, ActivityLog.
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
            cfg.AddOpenBehavior(typeof(ActivityLogBehavior<,>));
        });
        services.AddValidatorsFromAssembly(assembly);
        services.AddScoped<Auth.SessionIssuer>();
        services.AddScoped<Users.VerificationEmailSender>();
        services.AddScoped<Users.VerifiedEmailResolver>();
        services.AddScoped<TransactionCallbacks>();
        services.AddScoped<ITransactionCallbacks>(sp => sp.GetRequiredService<TransactionCallbacks>());

        return services;
    }
}
