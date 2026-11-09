using Gameya.Application.Abstractions;

namespace Gameya.Api.IntegrationTests;

public sealed class MutableClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;
}
