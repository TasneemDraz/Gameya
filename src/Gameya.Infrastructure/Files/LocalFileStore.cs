using System.Text.RegularExpressions;
using Gameya.Application.Abstractions;

namespace Gameya.Infrastructure.Files;

/// <summary>
/// Stores files under <c>{rootPath}/{container}/{key}</c>. Containers come from an allowlist and keys must
/// match a strict pattern, so a caller-controlled value can never address a path outside the root.
/// </summary>
public sealed partial class LocalFileStore(string rootPath, SignedFileUrls urls) : IFileStore
{
    private static readonly string[] Containers = [FileContainers.ProfilePhotos, FileContainers.Proofs];

    public async Task UploadAsync(
        string container, string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var path = ResolvePath(container, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write to a temp file in the same directory, then rename: readers never see a partial file.
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(target, cancellationToken);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public Task DeleteAsync(string container, string key, CancellationToken cancellationToken)
    {
        File.Delete(ResolvePath(container, key));
        return Task.CompletedTask;
    }

    public Task<Stream?> OpenReadAsync(string container, string key, CancellationToken cancellationToken)
    {
        var path = ResolvePath(container, key);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Uri GetReadUrl(string container, string key, TimeSpan lifetime)
    {
        ResolvePath(container, key);
        return urls.GetReadUrl(container, key, lifetime);
    }

    public bool IsReadUrlValid(string container, string key, long expiresAtUnixSeconds, string signature) =>
        IsAllowedLocation(container, key) && urls.IsValid(container, key, expiresAtUnixSeconds, signature);

    private static bool IsAllowedLocation(string container, string key) =>
        Containers.Contains(container, StringComparer.Ordinal) && KeyPattern().IsMatch(key);

    private string ResolvePath(string container, string key) =>
        IsAllowedLocation(container, key)
            ? Path.Combine(rootPath, container, key)
            : throw new ArgumentException("Container or key is not allowed.");

    [GeneratedRegex(@"^[0-9a-f-]{36}/[0-9a-f-]{36}\.jpg\z")]
    private static partial Regex KeyPattern();
}
