namespace Gameya.Api.Infrastructure;

/// <summary>
/// Records an exception that stopped the host from starting. Serilog does not exist yet at that point and IIS
/// discards console output, so without this a failed start leaves no trace on the server.
/// </summary>
public static class StartupErrorLog
{
    public const string RelativePath = "logs/startup-error.log";

    /// <summary>Appends a UTC timestamp, the exception type and the redacted exception text. Never throws.</summary>
    public static void Write(string contentRoot, Exception ex)
    {
        try
        {
            var path = Path.Combine(contentRoot, "logs", "startup-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // No container exists yet, so IClock is unavailable; TimeProvider.System is the same wall clock.
            var entry = $"{TimeProvider.System.GetUtcNow():O} {ex.GetType().FullName}{Environment.NewLine}"
                + $"{RedactingEnricher.MaskPhones(ex.ToString())}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(path, entry);
        }
        catch (Exception)
        {
            // Best effort: a logging failure must never hide the original startup exception.
        }
    }
}
