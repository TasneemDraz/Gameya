namespace Gameya.Api.Infrastructure;

/// <summary>The security headers the API sets itself, because the host has no reverse proxy we control.</summary>
public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; font-src https://fonts.gstatic.com; style-src 'self' https://fonts.googleapis.com 'unsafe-inline'; img-src 'self' blob:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    /// <summary>
    /// Sets the headers in OnStarting, so every response carries them, including error responses written
    /// by the exception handler. HSTS is skipped in Development, where the app runs over plain http.
    /// </summary>
    public static IApplicationBuilder UseGameyaSecurityHeaders(this IApplicationBuilder app)
    {
        var env = app.ApplicationServices.GetRequiredService<IHostEnvironment>();
        var hsts = !env.IsDevelopment();
        return app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                if (hsts)
                {
                    headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
                }

                headers["X-Content-Type-Options"] = "nosniff";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Content-Security-Policy"] = ContentSecurityPolicy;
                return Task.CompletedTask;
            });
            return next(context);
        });
    }
}
