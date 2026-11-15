using Gameya.Application.Abstractions;
using Gameya.Domain.Gameyas;
using Gameya.Domain.Users;
using Gameya.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gameya.Api.IntegrationTests.Gameyas;

/// <summary>Builders shared by the persistence tests; every value that hits a unique index is random.</summary>
internal static class GameyaSeed
{
    public static readonly DateOnly Today = new(2026, 11, 14);
    public static readonly DateTimeOffset Now = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);

    public static PhoneNumber UniquePhone() => PhoneNumber.Parse("010" + Random.Shared.Next(10_000_000, 99_999_999));

    public static string UniqueCode() => new(Random.Shared.GetItems("ABCDEFGHJKMNPQRSTUVWXYZ23456789".AsSpan(), 6));

    public static User NewUser() => User.Register(Guid.CreateVersion7(), UniquePhone(), Language.Ar, Now);

    public static readonly GameyaSettings Settings = new(
        "Family circle", null, 1000m, 10, GameyaFrequency.Monthly, Today.AddDays(7), 3,
        PayoutTiming.AfterFullCollection, null, OrderMethod.ByRequest, 2, true, null);

    /// <summary>A Recruiting, ByRequest gam'eya of ten shares in which the organizer holds one.</summary>
    public static GameyaCircle Recruiting(Guid organizerId)
    {
        var gameya = GameyaCircle.Create(Guid.CreateVersion7(), organizerId, Settings, 1, Today, Now);
        gameya.OpenForRecruiting(UniqueCode(), Today);
        return gameya;
    }

    public static ShareRequest[] Shares(params int[][] preferences) => [.. preferences.Select(p => new ShareRequest(p))];

    public static ShareRequest[] OneShare() => [new ShareRequest([])];

    public static Task<GameyaCircle> LoadAsync(AppDbContext db, Guid id) =>
        db.Gameyas
            .Include(g => g.Memberships).ThenInclude(m => m.Shares)
            .Include(g => g.JoinRequests).ThenInclude(r => r.Shares)
            .Include(g => g.PendingInvites)
            .SingleAsync(g => g.Id == id);
}

internal sealed class TestCurrentUser(Guid? userId) : ICurrentUser
{
    public Guid? UserId => userId;
    public bool IsAuthenticated => userId is not null;
    public IReadOnlyCollection<string> Roles => [];
}
