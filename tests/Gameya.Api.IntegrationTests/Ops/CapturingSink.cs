using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Gameya.Api.IntegrationTests.Ops;

/// <summary>Test Serilog sink. Registered as a DI <see cref="ILogEventSink"/>, which the host adds to its logger.</summary>
public sealed class CapturingSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyCollection<LogEvent> Events => [.. _events];

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    public static string SourceContext(LogEvent e) =>
        e.Properties.TryGetValue("SourceContext", out var v) ? v.ToString().Trim('"') : "";

    /// <summary>Everything a sink could write for the event: the rendered message, every property and the exception.</summary>
    public static string FullText(LogEvent e) =>
        e.RenderMessage() + "\n" + string.Join("\n", e.Properties.Select(p => $"{p.Key}={p.Value}")) + "\n" + e.Exception;
}
