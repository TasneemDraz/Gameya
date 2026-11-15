using System.Security.Cryptography;
using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Gameyas.Specifications;

namespace Gameya.Application.Gameyas;

public interface IInviteCodeGenerator
{
    string Next();
}

/// <summary>Six characters from an alphabet without look-alikes (no 0/O, 1/I/L), from a cryptographic RNG.</summary>
public sealed class InviteCodeGenerator : IInviteCodeGenerator
{
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int Length = 6;

    public string Next() => string.Create(Length, 0, static (chars, _) =>
    {
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
    });
}

internal static class InviteCodes
{
    private const int MaxAttempts = 5;

    /// <summary>
    /// A code no gam'eya uses yet, retrying a few times on a collision. The filtered unique index on
    /// invite_code stays the backstop for a race (409 CONCURRENCY_CONFLICT).
    /// </summary>
    public static async Task<string> NextFreeAsync(
        IAppDbContext db, IInviteCodeGenerator generator, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var code = generator.Next();
            if (attempt == MaxAttempts || !await db.Gameyas.AnyAsync(new GameyaByInviteCodeSpec(code), ct))
            {
                return code;
            }
        }
    }
}
