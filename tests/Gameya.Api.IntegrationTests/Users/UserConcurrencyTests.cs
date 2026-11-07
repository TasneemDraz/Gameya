using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Gameya.Api.IntegrationTests.Users;

[Collection(DatabaseCollection.Name)]
public sealed class UserConcurrencyTests(DatabaseFixture db)
{
    [Fact]
    public async Task Updating_the_same_user_from_two_contexts_throws_DbUpdateConcurrencyException()
    {
        var now = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
        var phone = PhoneNumber.Parse("01" + "0125"[Random.Shared.Next(4)] + Random.Shared.Next(0, 100_000_000).ToString("D8"));
        var user = User.Register(Guid.CreateVersion7(), phone, Language.Ar, now);
        await using (var seed = db.CreateAppContext())
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
        }

        await using var first = db.CreateAppContext();
        await using var second = db.CreateAppContext();
        var inFirst = await first.Users.SingleAsync(u => u.Id == user.Id);
        var inSecond = await second.Users.SingleAsync(u => u.Id == user.Id);

        inFirst.RequestEmailChange("a@gameya.test", "hash-a", now.AddMinutes(1));
        await first.SaveChangesAsync();
        inSecond.RequestEmailChange("b@gameya.test", "hash-b", now.AddMinutes(1));

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
}
