using Gameya.Api.Infrastructure;
using Gameya.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gameya.Api.IntegrationTests.Ops;

/// <summary>A failed start must leave a trace on the server, because IIS discards console output.</summary>
public sealed class StartupErrorLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gameya-startup-" + Guid.NewGuid().ToString("N"));

    public StartupErrorLogTests() => Directory.CreateDirectory(_root);

    public void Dispose() => TempDirectory.DeleteWithRetry(_root);

    private string LogPath => Path.Combine(_root, "logs", "startup-error.log");

    [Fact]
    public void Failed_production_start_writes_the_exception_to_startup_error_log()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseContentRoot(_root);
            builder.UseSetting("Sms:Provider", "Logging");
        });

        Should.Throw<InvalidOperationException>(() => factory.CreateClient());

        File.Exists(LogPath).ShouldBeTrue();
        File.ReadAllText(LogPath).ShouldContain("System.InvalidOperationException");
    }

    [Fact]
    public void Write_masks_phone_numbers_and_appends()
    {
        var ex = new InvalidOperationException("bad value for 01012345678");

        StartupErrorLog.Write(_root, ex);
        StartupErrorLog.Write(_root, ex);

        var text = File.ReadAllText(LogPath);
        text.ShouldContain("System.InvalidOperationException");
        text.ShouldContain("010****5678");
        text.ShouldNotContain("01012345678");
        text.Split("System.InvalidOperationException").Length.ShouldBeGreaterThan(2);
    }

    [Fact]
    public void Write_never_throws_when_the_path_is_unusable()
    {
        var blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(blocker, "x");

        Should.NotThrow(() => StartupErrorLog.Write(blocker, new InvalidOperationException("boom")));
    }
}
