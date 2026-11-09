namespace Gameya.Application.Abstractions;

/// <summary>Checks that the configured file storage accepts a write. Never throws for storage failures.</summary>
public interface IStorageHealthProbe
{
    Task<bool> IsWritableAsync(CancellationToken ct);
}
