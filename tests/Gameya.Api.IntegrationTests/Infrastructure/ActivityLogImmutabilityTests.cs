using Ardalis.Specification;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Activity;
using Gameya.Domain.Common;
using Gameya.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gameya.Api.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public class ActivityLogImmutabilityTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private sealed class IdProjectionSpec : Specification<User, Guid>
    {
        public IdProjectionSpec() => Query.Select(u => u.Id);
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

    private async Task<PostgresException> ExecuteAsAppAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(fixture.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return await Should.ThrowAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task App_role_cannot_update_activity_log()
    {
        await InsertEntryAsync("A");

        var ex = await ExecuteAsAppAsync("UPDATE activity_log SET action = 'X'");

        ex.SqlState.ShouldBe("42501");
    }

    [Fact]
    public async Task App_role_cannot_delete_activity_log()
    {
        await InsertEntryAsync("A");

        var ex = await ExecuteAsAppAsync("DELETE FROM activity_log");

        ex.SqlState.ShouldBe("42501");
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
        await using (var seed = fixture.CreateAppContext())
        {
            seed.Users.Add(User.Register(Guid.NewGuid(), PhoneNumber.Parse(UniquePhone()), Language.Ar, Now));
            await seed.SaveChangesAsync();
        }

        await using var db = fixture.CreateAppContext();
        var ids = await db.Users.ListAsync(new IdProjectionSpec(), CancellationToken.None);

        ids.ShouldNotBeEmpty();
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
