using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Gameya.Infrastructure.Persistence.Configurations;

/// <summary>Stores a share's round preferences as a JSON array such as <c>[2,5]</c>.</summary>
public static class PreferencesConversion
{
    public const string ColumnType = "nvarchar(32)";

    public static readonly ValueConverter<IReadOnlyList<int>, string> Converter = new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        s => JsonSerializer.Deserialize<int[]>(s, (JsonSerializerOptions?)null) ?? Array.Empty<int>());

    public static readonly ValueComparer<IReadOnlyList<int>> Comparer = new(
        (a, b) => a!.SequenceEqual(b!),
        v => v.Aggregate(0, HashCode.Combine),
        v => v.ToArray());
}
