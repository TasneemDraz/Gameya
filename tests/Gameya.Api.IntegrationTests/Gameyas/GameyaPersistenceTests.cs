using Gameya.Api.Infrastructure;
using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Domain.Gameyas;
using Gameya.Domain.Users;
using Gameya.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using static Gameya.Api.IntegrationTests.Gameyas.GameyaSeed;

namespace Gameya.Api.IntegrationTests.Gameyas;

[Collection(DatabaseCollection.Name)]
public sealed class GameyaPersistenceTests(DatabaseFixture db)
{
    [Fact]
    public async Task Aggregate_round_trips_with_preferences_and_enums_as_strings()
    {
        var organizer = NewUser();
        var requester = NewUser();
        var invitee = UniquePhone();
        var gameya = Recruiting(organizer.Id);
        var request = gameya.RequestToJoin(requester.Id, Shares([2, 5]));
        var invite = gameya.InviteByPhone(invitee, null, 2);
        await SeedAsync(gameya, organizer, requester);

        await using var read = db.CreateAppContext();
        var stored = await LoadAsync(read, gameya.Id);
        stored.OrganizerId.ShouldBe(organizer.Id);
        stored.Status.ShouldBe(GameyaStatus.Recruiting);
        stored.InviteCode.ShouldBe(gameya.InviteCode);
        stored.Name.ShouldBe("Family circle");
        stored.ContributionAmount.ShouldBe(1000m);
        stored.Currency.ShouldBe("EGP");
        stored.StartDate.ShouldBe(Settings.StartDate);
        stored.OrderMethod.ShouldBe(OrderMethod.ByRequest);
        stored.PayoutOffsetDays.ShouldBeNull();
        stored.TermsVersion.ShouldBe(1);
        stored.AssignedShares.ShouldBe(1);
        stored.ReservedShares.ShouldBe(2);
        stored.Memberships.ShouldHaveSingleItem().Shares.ShouldHaveSingleItem().Preferences.ShouldBeEmpty();
        var storedRequest = stored.JoinRequests.ShouldHaveSingleItem();
        storedRequest.Status.ShouldBe(JoinRequestStatus.Pending);
        storedRequest.Shares.ShouldHaveSingleItem().Preferences.ShouldBe([2, 5]);
        var storedInvite = stored.PendingInvites.ShouldHaveSingleItem();
        storedInvite.Phone.ShouldBe(invitee);
        storedInvite.ShareCount.ShouldBe(2);

        (await ScalarAsync($"SELECT status AS [Value] FROM gameyas WHERE id = '{gameya.Id}'")).ShouldBe("Recruiting");
        (await ScalarAsync($"SELECT frequency AS [Value] FROM gameyas WHERE id = '{gameya.Id}'")).ShouldBe("Monthly");
        (await ScalarAsync($"SELECT status AS [Value] FROM memberships WHERE gameya_id = '{gameya.Id}'")).ShouldBe("Active");
        (await ScalarAsync($"SELECT preferences AS [Value] FROM join_request_shares WHERE join_request_id = '{request.Id}'"))
            .ShouldBe("[2,5]");
        (await ScalarAsync($"SELECT status AS [Value] FROM pending_invites WHERE id = '{invite.Id}'")).ShouldBe("Pending");
        (await ScalarAsync($"SELECT phone AS [Value] FROM pending_invites WHERE id = '{invite.Id}'")).ShouldBe(invitee.E164);
    }

    [Fact]
    public async Task Two_contexts_changing_children_of_one_gameya_conflict()
    {
        var organizer = NewUser();
        var first = NewUser();
        var second = NewUser();
        var gameya = Recruiting(organizer.Id);
        var firstRequest = gameya.RequestToJoin(first.Id, Shares([], []));
        var secondRequest = gameya.RequestToJoin(second.Id, OneShare());
        await SeedAsync(gameya, organizer, first, second);

        await using var one = db.CreateAppContext();
        await using var two = db.CreateAppContext();
        var inOne = await LoadAsync(one, gameya.Id);
        var inTwo = await LoadAsync(two, gameya.Id);
        inOne.ApproveJoinRequest(firstRequest.Id, Now);
        inTwo.ApproveJoinRequest(secondRequest.Id, Now);

        await one.SaveChangesAsync();
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => two.SaveChangesAsync());

        await using var read = db.CreateAppContext();
        (await read.Memberships.CountAsync(m => m.GameyaId == gameya.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task Filtered_unique_indexes_hold()
    {
        var organizer = NewUser();
        var member = NewUser();
        var requester = NewUser();
        var leaver = NewUser();
        var phone = UniquePhone();
        var gameya = Recruiting(organizer.Id);
        gameya.ApproveJoinRequest(gameya.RequestToJoin(member.Id, OneShare()).Id, Now);
        gameya.ApproveJoinRequest(gameya.RequestToJoin(leaver.Id, OneShare()).Id, Now);
        gameya.RequestToJoin(requester.Id, OneShare());
        gameya.InviteByPhone(phone, null, 1);
        await SeedAsync(gameya, organizer, member, requester, leaver);

        // A root loaded without its children cannot see the live rows, so only the database stops the duplicates.
        await ShouldViolateAsync(g => g.ApproveJoinRequest(g.RequestToJoin(member.Id, OneShare()).Id, Now));
        await ShouldViolateAsync(g => g.RequestToJoin(requester.Id, OneShare()));
        await ShouldViolateAsync(g => g.InviteByPhone(phone, null, 1));

        await using (var ctx = db.CreateAppContext())
        {
            (await LoadAsync(ctx, gameya.Id)).Leave(leaver.Id);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = db.CreateAppContext())
        {
            var stored = await LoadAsync(ctx, gameya.Id);
            stored.ApproveJoinRequest(stored.RequestToJoin(leaver.Id, OneShare()).Id, Now);
            await ctx.SaveChangesAsync();
        }

        await using var read = db.CreateAppContext();
        (await read.Memberships.CountAsync(m => m.GameyaId == gameya.Id && m.UserId == leaver.Id)).ShouldBe(2);

        async Task ShouldViolateAsync(Action<GameyaCircle> change)
        {
            await using var ctx = db.CreateAppContext();
            change(await ctx.Gameyas.SingleAsync(g => g.Id == gameya.Id));
            var ex = await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
            ex.InnerException.ShouldBeOfType<SqlException>().Number.ShouldBe(2601);
            ProblemDetailsMapping.IsGameyaUniqueConflict(ex).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Changing_a_child_without_its_tracked_root_throws()
    {
        var organizer = NewUser();
        var requester = NewUser();
        var gameya = Recruiting(organizer.Id);
        var request = gameya.RequestToJoin(requester.Id, OneShare());
        await SeedAsync(gameya, organizer, requester);

        // Without the root in the change tracker the row-version check cannot guard the change.
        await using (var ctx = db.CreateAppContext())
        {
            var membership = await ctx.Memberships.SingleAsync(m => m.GameyaId == gameya.Id);
            ctx.Entry(membership).Property(m => m.Status).CurrentValue = MembershipStatus.Left;
            await Should.ThrowAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
        }

        await using (var ctx = db.CreateAppContext())
        {
            var stored = await ctx.JoinRequests.SingleAsync(r => r.Id == request.Id);
            ctx.Entry(stored).Property(r => r.Status).CurrentValue = JoinRequestStatus.Withdrawn;
            await Should.ThrowAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
        }

        await using var read = db.CreateAppContext();
        (await read.Memberships.SingleAsync(m => m.GameyaId == gameya.Id)).Status.ShouldBe(MembershipStatus.Active);
        (await read.JoinRequests.SingleAsync(r => r.Id == request.Id)).Status.ShouldBe(JoinRequestStatus.Pending);
    }

    [Fact]
    public async Task Existing_users_keep_created_at()
    {
        // A database at the migration before AddGameyas, holding a user, is upgraded to the latest schema.
        var database = "gameya_upgrade_" + Guid.NewGuid().ToString("N");
        var id = Guid.CreateVersion7();
        var createdAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        await using var ctx = db.CreateContextFor(database);
        var migrator = ctx.GetService<IMigrator>();
        await migrator.MigrateAsync("20261107153815_ActivityLogAppendOnlyTrigger");
        await ctx.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO users (id, phone, profile_complete, language, is_blocked, created_at, email_send_times, roles)
             VALUES ({id}, {UniquePhone().E164}, 0, 'Ar', 0, {createdAt}, '[]', '[]')
             """);

        await ctx.Database.MigrateAsync();

        var user = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == id);
        user.CreatedAt.ShouldBe(createdAt);
        user.CreatedByUserId.ShouldBeNull();
        user.UpdatedAt.ShouldBeNull();
        user.UpdatedByUserId.ShouldBeNull();
        await ctx.Database.EnsureDeletedAsync();
    }

    private async Task SeedAsync(GameyaCircle gameya, params User[] users)
    {
        await using var ctx = db.CreateAppContext();
        ctx.Users.AddRange(users);
        ctx.Gameyas.Add(gameya);
        await ctx.SaveChangesAsync();
    }

    private async Task<string> ScalarAsync(string sql)
    {
        await using var ctx = db.CreateAppContext();
        return await ctx.Database.SqlQueryRaw<string>(sql).SingleAsync();
    }
}
