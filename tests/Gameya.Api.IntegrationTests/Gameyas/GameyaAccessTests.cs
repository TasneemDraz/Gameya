using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Application.Gameyas;
using static Gameya.Api.IntegrationTests.Gameyas.GameyaSeed;

namespace Gameya.Api.IntegrationTests.Gameyas;

[Collection(DatabaseCollection.Name)]
public sealed class GameyaAccessTests(DatabaseFixture db)
{
    [Fact]
    public async Task Roles_come_from_the_organizer_and_active_memberships()
    {
        var organizer = NewUser();
        var member = NewUser();
        var leaver = NewUser();
        var outsider = NewUser();
        var gameya = Recruiting(organizer.Id);
        gameya.ApproveJoinRequest(gameya.RequestToJoin(member.Id, OneShare()).Id, Now);
        gameya.ApproveJoinRequest(gameya.RequestToJoin(leaver.Id, OneShare()).Id, Now);
        gameya.Leave(leaver.Id);
        await using (var seed = db.CreateAppContext())
        {
            seed.Users.AddRange(organizer, member, leaver, outsider);
            seed.Gameyas.Add(gameya);
            await seed.SaveChangesAsync();
        }

        await using var ctx = db.CreateAppContext();
        var access = new GameyaAccess(ctx);

        (await access.GetRoleAsync(gameya.Id, organizer.Id, default)).ShouldBe(GameyaRole.Organizer);
        (await access.GetRoleAsync(gameya.Id, member.Id, default)).ShouldBe(GameyaRole.Member);
        (await access.GetRoleAsync(gameya.Id, leaver.Id, default)).ShouldBe(GameyaRole.None);
        (await access.GetRoleAsync(gameya.Id, outsider.Id, default)).ShouldBe(GameyaRole.None);
        (await access.GetRoleAsync(Guid.NewGuid(), organizer.Id, default)).ShouldBe(GameyaRole.None);
    }
}
