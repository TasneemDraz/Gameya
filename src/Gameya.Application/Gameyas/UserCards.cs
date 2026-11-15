using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Users.Specifications;

namespace Gameya.Application.Gameyas;

/// <summary>
/// Loads names, phones and photo keys for the people a query mentions, in one extra round trip. Phones stay raw
/// here; every handler turns them into a <c>DisplayPhone</c> through <c>PhoneDisplay</c>.
/// </summary>
internal static class UserCards
{
    public static async Task<IReadOnlyDictionary<Guid, UserCardRow>> LoadAsync(
        IAppDbContext db, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, UserCardRow>();
        }

        var rows = await db.Users.ListAsync(new UserCardsByIdsSpec(ids), ct);
        return rows.ToDictionary(r => r.Id);
    }

    public static string? NameOf(this IReadOnlyDictionary<Guid, UserCardRow> cards, Guid? userId) =>
        userId is { } id && cards.TryGetValue(id, out var card) ? card.FullName : null;

    /// <summary>A short-lived signed URL, as for the caller's own profile photo.</summary>
    public static Uri? PhotoUrl(IFileStore fileStore, UserCardRow? card) =>
        card?.PhotoKey is { } key
            ? fileStore.GetReadUrl(FileContainers.ProfilePhotos, key, FileContainers.ReadUrlLifetime)
            : null;
}
