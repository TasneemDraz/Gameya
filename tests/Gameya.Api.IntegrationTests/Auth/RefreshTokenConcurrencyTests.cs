using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Domain.Auth;
using Gameya.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Gameya.Api.IntegrationTests.Auth;

[Collection(DatabaseCollection.Name)]
public sealed class RefreshTokenConcurrencyTests(DatabaseFixture db)
{
    [Fact]
    public async Task Rotating_the_same_token_from_two_contexts_throws_DbUpdateConcurrencyException()
    {
        var now = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);
        var phone = PhoneNumber.Parse("01" + "0125"[Random.Shared.Next(4)] + Random.Shared.Next(0, 100_000_000).ToString("D8"));
        var user = User.Register(Guid.CreateVersion7(), phone, Language.Ar, now);
        var token = RefreshToken.Issue(user.Id, Guid.NewGuid(), "hash-" + Guid.NewGuid(), now);
        await using (var seed = db.CreateAppContext())
        {
            seed.Users.Add(user);
            seed.RefreshTokens.Add(token);
            await seed.SaveChangesAsync();
        }

        await using var first = db.CreateAppContext();
        await using var second = db.CreateAppContext();
        var inFirst = await first.RefreshTokens.SingleAsync(t => t.Id == token.Id);
        var inSecond = await second.RefreshTokens.SingleAsync(t => t.Id == token.Id);

        inFirst.Revoke(now.AddMinutes(1), Guid.NewGuid());
        await first.SaveChangesAsync();
        inSecond.Revoke(now.AddMinutes(1), Guid.NewGuid());

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
}
