using Ardalis.Specification;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Activity;
using Gameya.Domain.Common;
using Gameya.Domain.Users;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Gameya.Api.IntegrationTests.Infrastructure;

[Collection(DatabaseCollection.Name)]
public class ActivityLogImmutabilityTests(DatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    // Embeds the entity itself in the projection: the only kind of projection EF would track without AsNoTracking.
    private sealed record UserHolder(User User);

    private sealed class UserHolderSpec : Specification<User, UserHolder>
    {
        public UserHolderSpec(Guid id) => Query.Where(u => u.Id == id).Select(u => new UserHolder(u));
    }

    private static string UniquePhone() => "010" + Random.Shared.Next(10_000_000, 99_999_999);

    private async Task<long> InsertEntryAsync(string action)
    {
        await using var db = fixture.CreateAppContext();
        var entry = ActivityLogEntry.From(new TestEvent(action), null, "System", Now);
        db.ActivityLog.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task<SqlException> ExecuteAsAppAsync(string sql)
    {
        await using var conn = new SqlConnection(fixture.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        return await Should.ThrowAsync<SqlException>(() => cmd.ExecuteNonQueryAsync());
    }

    [Fact(Skip = "Task 2")]
    public async Task App_role_cannot_update_activity_log()
    {
        await InsertEntryAsync("A");

        // Task 2 adds the append-only trigger and asserts the error it raises.
        await ExecuteAsAppAsync("UPDATE activity_log SET action = 'X'");
    }

    [Fact(Skip = "Task 2")]
    public async Task App_role_cannot_delete_activity_log()
    {
        await InsertEntryAsync("A");

        // Task 2 adds the append-only trigger and asserts the error it raises.
        await ExecuteAsAppAsync("DELETE FROM activity_log");
    }

    [Fact]
    public async Task Interceptor_throws_when_entry_modified_through_ef()
    {
        var id = await InsertEntryAsync("A");
        await using var db = fixture.CreateAppContext();
        var entry = await db.ActivityLog.SingleAsync(e => e.Id == id);

        db.Entry(entry).Property(e => e.Note).CurrentValue = "changed";

        await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Phone_unique_ignores_deleted_users()
    {
        var phone = PhoneNumber.Parse(UniquePhone());
        await using var db = fixture.CreateAppContext();
        var first = User.Register(Guid.NewGuid(), phone, Language.Ar, Now);
        db.Users.Add(first);
        await db.SaveChangesAsync();

        first.Delete(Now);
        await db.SaveChangesAsync();

        db.Users.Add(User.Register(Guid.NewGuid(), phone, Language.En, Now));
        await db.SaveChangesAsync();

        db.Users.Add(User.Register(Guid.NewGuid(), phone, Language.En, Now));
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Projection_specs_are_untracked()
    {
        var id = Guid.NewGuid();
        await using (var seed = fixture.CreateAppContext())
        {
            seed.Users.Add(User.Register(id, PhoneNumber.Parse(UniquePhone()), Language.Ar, Now));
            await seed.SaveChangesAsync();
        }

        await using var db = fixture.CreateAppContext();
        (await db.Users.ListAsync(new MeRowSpec(id), CancellationToken.None)).Count.ShouldBe(1);
        (await db.Users.ListAsync(new UserHolderSpec(id), CancellationToken.None)).Count.ShouldBe(1);

        // A scalar-only projection (MeRowSpec) is never tracked, so UserHolderSpec is what makes this meaningful:
        // without AsNoTracking in BuildProjectionQuery the entity it carries would be tracked.
        db.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public async Task Entity_specs_are_tracked()
    {
        var id = Guid.NewGuid();
        await using (var seed = fixture.CreateAppContext())
        {
            seed.Users.Add(User.Register(id, PhoneNumber.Parse(UniquePhone()), Language.Ar, Now));
            await seed.SaveChangesAsync();
        }

        await using var db = fixture.CreateAppContext();
        var user = await db.Users.FirstOrDefaultAsync(new UserByIdSpec(id), CancellationToken.None);

        user.ShouldNotBeNull();
        db.Entry(user).State.ShouldBe(EntityState.Unchanged);
    }

    private sealed record TestEvent(string Action) : IActivityEvent
    {
        public Guid? GameyaId => null;
        public Guid? ActorUserIdOverride => null;
        public string TargetType => "User";
        public string TargetId => "t";
        public object? Details => new { k = "v" };
        public string? Note => "n";
    }
}
