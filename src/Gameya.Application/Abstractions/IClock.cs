namespace Gameya.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
