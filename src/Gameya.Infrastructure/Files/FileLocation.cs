using System.Text.RegularExpressions;
using Gameya.Application.Abstractions;

namespace Gameya.Infrastructure.Files;

/// <summary>
/// The shared rule for what a file store may address: containers come from an allowlist and keys must match
/// a strict pattern, so a caller-controlled value can never reach a path or object outside the store.
/// </summary>
public static partial class FileLocation
{
    private static readonly string[] Containers = [FileContainers.ProfilePhotos, FileContainers.Proofs];

    public static bool IsAllowed(string container, string key) =>
        Containers.Contains(container, StringComparer.Ordinal) && KeyPattern().IsMatch(key);

    [GeneratedRegex(@"^[0-9a-f-]{36}/[0-9a-f-]{36}\.jpg\z")]
    private static partial Regex KeyPattern();
}
