namespace Gameya.Api.IntegrationTests.Infrastructure;

internal static class TempDirectory
{
    /// <summary>
    /// Deletes a directory tree, retrying for about two seconds. Under minimal hosting the factory's Dispose can
    /// return before the host's singletons (the Serilog file sink) are disposed on the Program.Main thread, so the
    /// log file stays locked for a few hundred milliseconds.
    /// </summary>
    public static void DeleteWithRetry(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (true)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
        }
    }
}
