namespace Gameya.Application.Abstractions;

/// <summary>
/// Storage-neutral file store. Containers are a fixed allowlist (<see cref="FileContainers"/>); keys are
/// validated by the implementation. Read access is by short-lived signed URL.
/// </summary>
public interface IFileStore
{
    Task UploadAsync(string container, string key, Stream content, string contentType, CancellationToken cancellationToken);

    Task DeleteAsync(string container, string key, CancellationToken cancellationToken);

    /// <summary>Null when the file does not exist.</summary>
    Task<Stream?> OpenReadAsync(string container, string key, CancellationToken cancellationToken);

    /// <summary>A relative, signed URL that grants read access to one file until <paramref name="lifetime"/> elapses.</summary>
    Uri GetReadUrl(string container, string key, TimeSpan lifetime);

    /// <summary>
    /// True only for a known container, a well-formed key, an unexpired link and a matching signature.
    /// Never throws for hostile input.
    /// </summary>
    bool IsReadUrlValid(string container, string key, long expiresAtUnixSeconds, string signature);
}

public static class FileContainers
{
    public const string ProfilePhotos = "profile-photos";
    public const string Proofs = "proofs";

    public static readonly TimeSpan ReadUrlLifetime = TimeSpan.FromMinutes(5);
}
