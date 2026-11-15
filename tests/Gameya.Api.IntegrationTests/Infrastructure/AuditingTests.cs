using Gameya.Api.IntegrationTests.Gameyas;
using Gameya.Application.Abstractions;
using Gameya.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static Gameya.Api.IntegrationTests.Gameyas.GameyaSeed;

namespace Gameya.Api.IntegrationTests.Infrastructure;

[Collection(DatabaseCollection.Name)]
public sealed class AuditingTests(DatabaseFixture db)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly MutableClock _clock = new(T0);
    private readonly TestCurrentUser _actor = new(Guid.CreateVersion7());

    private AppDbContext Context(ICurrentUser? actor = null) =>
        db.CreateAppContext(new AuditingInterceptor(_clock, actor ?? _actor));

    [Fact]
    public async Task Insert_sets_Created_and_leaves_Updated_null()
    {
        var organizer = NewUser();
        var gameya = Recruiting(organizer.Id);
        await using (var ctx = Context())
        {
            ctx.Users.Add(organizer);
            ctx.Gameyas.Add(gameya);
            await ctx.SaveChangesAsync();
        }

        await using var read = db.CreateAppContext();
        var user = await read.Users.SingleAsync(u => u.Id == organizer.Id);
        var stored = await LoadAsync(read, gameya.Id);
        var membership = stored.Memberships.ShouldHaveSingleItem();
        var share = membership.Shares.ShouldHaveSingleItem();
        foreach (var (createdAt, createdBy, updatedAt, updatedBy) in new[]
                 {
                     (user.CreatedAt, user.CreatedByUserId, user.UpdatedAt, user.UpdatedByUserId),
                     (stored.CreatedAt, stored.CreatedByUserId, stored.UpdatedAt, stored.UpdatedByUserId),
                     (membership.CreatedAt, membership.CreatedByUserId, membership.UpdatedAt, membership.UpdatedByUserId),
                     (share.CreatedAt, share.CreatedByUserId, share.UpdatedAt, share.UpdatedByUserId),
                 })
        {
            createdAt.ShouldBe(T0);
            createdBy.ShouldBe(_actor.UserId);
            updatedAt.ShouldBeNull();
            updatedBy.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Update_sets_Updated_and_never_overwrites_Created()
    {
        var organizer = NewUser();
        var gameya = Recruiting(organizer.Id);
        await using (var ctx = Context())
        {
            ctx.Users.Add(organizer);
            ctx.Gameyas.Add(gameya);
            await ctx.SaveChangesAsync();
        }

        _clock.Advance(TimeSpan.FromHours(1));
        var editor = new TestCurrentUser(Guid.CreateVersion7());
        await using (var ctx = Context(editor))
        {
            var user = await ctx.Users.SingleAsync(u => u.Id == organizer.Id);
            user.SetProfile("Mona Ali", user.Language, _clock.UtcNow);
            var stored = await ctx.Gameyas.SingleAsync(g => g.Id == gameya.Id);
            stored.RegenerateInviteCode(UniqueCode());
            // Even a direct write to the Created* columns must not reach the database.
            ctx.Entry(user).Property(u => u.CreatedAt).CurrentValue = T0.AddYears(-1);
            ctx.Entry(stored).Property(g => g.CreatedByUserId).CurrentValue = editor.UserId;
            await ctx.SaveChangesAsync();
        }

        await using var read = db.CreateAppContext();
        var savedUser = await read.Users.SingleAsync(u => u.Id == organizer.Id);
        var savedGameya = await read.Gameyas.SingleAsync(g => g.Id == gameya.Id);
        foreach (var (createdAt, createdBy, updatedAt, updatedBy) in new[]
                 {
                     (savedUser.CreatedAt, savedUser.CreatedByUserId, savedUser.UpdatedAt, savedUser.UpdatedByUserId),
                     (savedGameya.CreatedAt, savedGameya.CreatedByUserId, savedGameya.UpdatedAt, savedGameya.UpdatedByUserId),
                 })
        {
            createdAt.ShouldBe(T0);
            createdBy.ShouldBe(_actor.UserId);
            updatedAt.ShouldBe(T0.AddHours(1));
            updatedBy.ShouldBe(editor.UserId);
        }
    }

    [Fact]
    public async Task Anonymous_actions_leave_ByUserId_null()
    {
        var anonymous = new TestCurrentUser(null);
        var newcomer = NewUser();
        await using (var ctx = Context(anonymous))
        {
            ctx.Users.Add(newcomer);
            await ctx.SaveChangesAsync();
            _clock.Advance(TimeSpan.FromMinutes(5));
            newcomer.SetProfile("Mona Ali", newcomer.Language, _clock.UtcNow);
            await ctx.SaveChangesAsync();
        }

        await using var read = db.CreateAppContext();
        var saved = await read.Users.SingleAsync(u => u.Id == newcomer.Id);
        saved.CreatedAt.ShouldBe(T0);
        saved.CreatedByUserId.ShouldBeNull();
        saved.UpdatedAt.ShouldBe(T0.AddMinutes(5));
        saved.UpdatedByUserId.ShouldBeNull();
    }

    [Fact]
    public async Task Child_change_touches_the_root()
    {
        var organizer = NewUser();
        var requester = NewUser();
        var gameya = Recruiting(organizer.Id);
        var request = gameya.RequestToJoin(requester.Id, Shares([2, 5]));
        await using (var ctx = Context())
        {
            ctx.Users.AddRange(organizer, requester);
            ctx.Gameyas.Add(gameya);
            await ctx.SaveChangesAsync();
        }

        _clock.Advance(TimeSpan.FromHours(2));
        var approver = new TestCurrentUser(organizer.Id);
        await using (var ctx = Context(approver))
        {
            var stored = await LoadAsync(ctx, gameya.Id);
            stored.ApproveJoinRequest(request.Id, _clock.UtcNow);
            await ctx.SaveChangesAsync();
        }

        (await RootUpdatedAtAsync(gameya.Id)).ShouldBe((T0.AddHours(2), organizer.Id));

        // Only a grandchild changes: the organizer gains a share, then gives it back (insert, then delete).
        foreach (var organizerShares in new[] { 2, 1 })
        {
            _clock.Advance(TimeSpan.FromHours(1));
            await using var ctx = Context(approver);
            var stored = await LoadAsync(ctx, gameya.Id);
            stored.UpdateSettings(Settings, organizerShares, Today, _clock.UtcNow);
            await ctx.SaveChangesAsync();

            (await RootUpdatedAtAsync(gameya.Id)).ShouldBe((_clock.UtcNow, organizer.Id));
        }
    }

    private async Task<(DateTimeOffset?, Guid?)> RootUpdatedAtAsync(Guid id)
    {
        await using var read = db.CreateAppContext();
        var saved = await read.Gameyas.SingleAsync(g => g.Id == id);
        saved.CreatedAt.ShouldBe(T0);
        return (saved.UpdatedAt, saved.UpdatedByUserId);
    }
}
