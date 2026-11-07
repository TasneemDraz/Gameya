using System.Text.RegularExpressions;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.SqlServer;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Gameya.Api.Infrastructure;

/// <summary>Structured logging, correlation ids, OpenTelemetry and Hangfire: everything an operator looks at.</summary>
public static class Observability
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string LogFilePathKey = "Observability:LogFilePath";
    public const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    internal const string CorrelationItemKey = "Gameya.CorrelationId";

    /// <summary>The correlation id of the current request: the safe inbound header, or a generated one.</summary>
    public static string GetCorrelationId(this HttpContext context) =>
        context.Items[CorrelationItemKey] as string ?? context.TraceIdentifier;

    /// <summary>
    /// Serilog writes compact JSON to stdout and, when <c>Observability:LogFilePath</c> is set, to a rolling file through the redacting enricher.
    /// DI-registered <see cref="ILogEventSink"/>s are added too, which is how tests capture events.
    /// </summary>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((_, services, logger) =>
        {
            // Every output sink (console and DI-registered ones) sits behind ExceptionRedactingSink, because an
            // enricher cannot change LogEvent.Exception.
            var outputs = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console(new CompactJsonFormatter())
                .ReadFrom.Services(services);
            var logFilePath = builder.Configuration[LogFilePathKey];
            if (!string.IsNullOrWhiteSpace(logFilePath))
            {
                // IIS discards stdout, so production also writes compact JSON to a rolling file.
                // A relative path is resolved against the content root, not the working directory.
                outputs.WriteTo.File(
                    new CompactJsonFormatter(),
                    Path.GetFullPath(logFilePath, builder.Environment.ContentRootPath),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    fileSizeLimitBytes: 10_000_000,
                    rollOnFileSizeLimit: true);
            }

            var outputLogger = outputs.CreateLogger();
            // UseSerilog with preserveStaticLogger does not own the sinks; the container closes them on shutdown.
            services.GetRequiredService<OutputLoggerOwner>().Logger = outputLogger;
            logger
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.With<RedactingEnricher>()
                .WriteTo.Sink(new ExceptionRedactingSink(outputLogger));
        }, preserveStaticLogger: true);

        builder.Services.AddSingleton<OutputLoggerOwner>();

        // Traces and metrics are always collected in-process; nothing leaves the process unless an OTLP endpoint is set.
        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("gameya-api"))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation(o => o.Filter = c => !c.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());
        if (!string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointKey]))
        {
            telemetry.UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>
    /// Hangfire runs in-process on SQL Server schema <c>hangfire</c>, which it creates and upgrades itself on
    /// startup, through the App connection string. No jobs are defined in Foundation; later sub-projects add them.
    /// </summary>
    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("App")
            ?? throw new InvalidOperationException("ConnectionStrings:App is not configured.");

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(
                connectionString,
                new SqlServerStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));
        services.AddHangfireServer(o => o.WorkerCount = 4);
        return services;
    }

    public static WebApplication UseObservability(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging(o =>
        {
            // The host's logger, not the static Log.Logger (which UseSerilog leaves alone), so tests can capture events.
            o.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
            o.GetLevel = (context, _, ex) =>
                ex is not null || context.Response.StatusCode >= 500 ? LogEventLevel.Error
                : context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Debug
                : LogEventLevel.Information;

            // Log the route pattern, never the raw path: file keys and dev OTP phones are path values, and
            // signed-URL query strings (sig) must not be recorded either.
            o.GetMessageTemplateProperties = (context, _, elapsed, status) =>
            [
                new LogEventProperty("RequestMethod", new ScalarValue(context.Request.Method)),
                new LogEventProperty("RequestPath", new ScalarValue(
                    (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched")),
                new LogEventProperty("StatusCode", new ScalarValue(status)),
                new LogEventProperty("Elapsed", new ScalarValue(elapsed))
            ];
        });
        return app;
    }

    /// <summary>The dashboard is mapped only in Development, so it needs no auth; production gets an admin-guarded one later.</summary>
    public static WebApplication MapDevHangfireDashboard(this WebApplication app)
    {
        app.MapHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [new AllowAllDashboardFilter()] });
        return app;
    }

    private sealed class AllowAllDashboardFilter : IDashboardAuthorizationFilter
    {
        public bool Authorize(DashboardContext context) => true;
    }
}

/// <summary>Disposed with the service provider, which flushes and closes the log file.</summary>
internal sealed class OutputLoggerOwner : IDisposable
{
    public Serilog.Core.Logger? Logger { get; set; }

    public void Dispose() => Logger?.Dispose();
}

/// <summary>
/// Reads <c>X-Correlation-Id</c> (accepted only when short and made of safe characters, so it cannot forge log
/// lines or inject headers), generates one otherwise, echoes it in the response and puts it on every log event.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[Observability.CorrelationHeader].FirstOrDefault();
        var id = supplied is not null && SafeId().IsMatch(supplied) ? supplied : Guid.NewGuid().ToString("N");

        context.Items[Observability.CorrelationItemKey] = id;
        // OnStarting, because the exception handler clears headers before it writes its response.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[Observability.CorrelationHeader] = id;
            return Task.CompletedTask;
        });
        System.Diagnostics.Activity.Current?.SetTag("correlation_id", id);

        using (LogContext.PushProperty("CorrelationId", id))
        {
            await next(context);
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._\-]{1,64}\z")]
    private static partial Regex SafeId();
}

/// <summary>
/// Last line of defence for logs: masks Egyptian phone numbers inside string property values and drops properties
/// named <c>code</c>, <c>token</c>, <c>refreshToken</c> or <c>email</c>. The dev SMS sender is exempt: it exists to
/// show the OTP and is only registered in Development and E2E.
/// </summary>
public sealed partial class RedactingEnricher : ILogEventEnricher
{
    private const string DevSmsSenderContext = "Gameya.Infrastructure.Sms.LoggingSmsSender";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Properties.TryGetValue("SourceContext", out var context)
            && context is ScalarValue { Value: DevSmsSenderContext })
        {
            return;
        }

        foreach (var property in logEvent.Properties.ToList())
        {
            if (IsSensitiveName(property.Key))
            {
                logEvent.RemovePropertyIfPresent(property.Key);
                continue;
            }

            var redacted = Redact(property.Value);
            if (!ReferenceEquals(redacted, property.Value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, redacted));
            }
        }
    }

    private static bool IsSensitiveName(string name) => SensitiveName().IsMatch(name);

    private static LogEventPropertyValue Redact(LogEventPropertyValue value) => value switch
    {
        ScalarValue { Value: string s } => MaskPhones(s) is var masked && masked != s ? new ScalarValue(masked) : value,
        StructureValue structure => new StructureValue(
            structure.Properties.Where(p => !IsSensitiveName(p.Name)).Select(p => new LogEventProperty(p.Name, Redact(p.Value))),
            structure.TypeTag),
        SequenceValue sequence => new SequenceValue(sequence.Elements.Select(Redact)),
        DictionaryValue dictionary => new DictionaryValue(
            dictionary.Elements
                .Where(e => !(e.Key.Value is string key && IsSensitiveName(key)))
                .Select(e => KeyValuePair.Create(e.Key, Redact(e.Value)))),
        _ => value
    };

    /// <summary>Same shape as <c>PhoneNumber.Masked</c>: <c>+201012345678</c> and <c>01012345678</c> become <c>010****5678</c>.</summary>
    public static string MaskPhones(string text) =>
        LocalPhone().Replace(InternationalPhone().Replace(text, m => Mask("0" + m.Groups[1].Value)), m => Mask(m.Value));

    private static string Mask(string local) => local[..3] + "****" + local[^4..];

    [GeneratedRegex(@"(?<!\d)\+20(\d{10})(?!\d)")]
    private static partial Regex InternationalPhone();

    [GeneratedRegex(@"(?<!\d)01[0125]\d{8}(?!\d)")]
    private static partial Regex LocalPhone();

    [GeneratedRegex(@"^(code|token|refreshToken|email)\z", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveName();
}

/// <summary>An exception stand-in whose message and full text (type, message, inner exceptions, stack) are already masked.</summary>
public sealed class RedactedException(string typeName, string message, string text) : Exception(message)
{
    public string OriginalType { get; } = typeName;

    public override string ToString() => text;
}

/// <summary>
/// Forwards events to the real sinks after replacing the exception with a phone-masked copy, so no sink ever
/// sees an unmasked phone in exception text. Same exemption as <see cref="RedactingEnricher"/>.
/// </summary>
public sealed class ExceptionRedactingSink(Serilog.ILogger inner) : ILogEventSink, IDisposable
{
    private const string DevSmsSenderContext = "Gameya.Infrastructure.Sms.LoggingSmsSender";

    public void Emit(LogEvent logEvent)
    {
        var exception = logEvent.Exception;
        var exempt = logEvent.Properties.TryGetValue("SourceContext", out var context)
            && context is ScalarValue { Value: DevSmsSenderContext };
        if (exception is null || exempt)
        {
            inner.Write(logEvent);
            return;
        }

        var masked = new RedactedException(
            exception.GetType().FullName ?? "Exception",
            RedactingEnricher.MaskPhones(exception.Message),
            RedactingEnricher.MaskPhones(exception.ToString()));
        inner.Write(new LogEvent(
            logEvent.Timestamp, logEvent.Level, masked, logEvent.MessageTemplate,
            logEvent.Properties.Select(p => new LogEventProperty(p.Key, p.Value)),
            logEvent.TraceId ?? default, logEvent.SpanId ?? default));
    }

    public void Dispose() => (inner as IDisposable)?.Dispose();
}
