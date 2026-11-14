using System.Net;
using System.Net.Http.Json;
using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Domain.Gameyas;
using Microsoft.EntityFrameworkCore;

namespace Gameya.Api.IntegrationTests.Gameyas;

/// <summary>
/// Cross-cutting acceptance: the whole create-to-member flow, outsider invisibility on every route, and
/// double-tapped writes. The auth endpoints allow about five logins per factory, so each test stays under that.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class GameyaAcceptanceTests : IDisposable
{
    private readonly DatabaseFixture _db;
    private readonly GameyaApiFactory _factory;
    private readonly HttpClient _organizer;
    private readonly List<HttpClient> _clients = [];

    public GameyaAcceptanceTests(DatabaseFixture db)
    {
        _db = db;
        _factory = new GameyaApiFactory(db);
        _organizer = _factory.CreateClient();
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        _organizer.Dispose();
        _factory.Dispose();
    }

    private async Task<(HttpClient Client, Guid UserId)> UserAsync()
    {
        var client = _factory.CreateClient();
        _clients.Add(client);
        var userId = await TestApi.LoginAsync(_factory, client);
        return (client, userId);
    }

    private async Task<(Guid Id, string Code)> RecruitingAsync(object? overrides = null)
    {
        await TestApi.LoginAsync(_factory, _organizer);
        var id = await GameyaTestApi.CreateAsync(_organizer, overrides);
        return (id, await GameyaTestApi.OpenAsync(_db, _organizer, id));
    }

    private static object JoinBody(int shares) => new
    {
        shares = Enumerable.Range(0, shares).Select(_ => new { preferences = Array.Empty<int>() }).ToArray(),
        acceptRules = true,
        acceptNoMoney = true,
    };

    private static Task<HttpResponseMessage> JoinAsync(HttpClient client, string code, int shares = 1) =>
        client.PostAsJsonAsync($"/api/invite/{code}/join", JoinBody(shares));

    private async Task<Guid> PendingRequestIdAsync(Guid gameyaId, Guid userId)
    {
        await using var ctx = _db.CreateAppContext();
        return await ctx.JoinRequests
            .Where(r => r.GameyaId == gameyaId && r.UserId == userId && r.Status == JoinRequestStatus.Pending)
            .Select(r => r.Id)
            .SingleAsync();
    }

    private Task<HttpResponseMessage> ApproveAsync(Guid gameyaId, Guid requestId) =>
        _organizer.PostAsync($"/api/gameyas/{gameyaId}/join-requests/{requestId}/approve", null);

    private static async Task ShouldFailWith(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
        (await TestApi.JsonAsync(response)).GetProperty("code").GetString().ShouldBe(code);
    }

    [Fact]
    public async Task Full_flow_create_open_preview_join_approve_members()
    {
        var (id, code) = await RecruitingAsync();
        var (member, memberId) = await UserAsync();

        var preview = await member.GetAsync($"/api/invite/{code}");
        preview.StatusCode.ShouldBe(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        (await TestApi.JsonAsync(preview)).GetProperty("availableShares").GetInt32().ShouldBe(9);

        (await JoinAsync(member, code, shares: 2)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var approve = await ApproveAsync(id, await PendingRequestIdAsync(id, memberId));
        approve.StatusCode.ShouldBe(HttpStatusCode.NoContent, await approve.Content.ReadAsStringAsync());

        var membersResponse = await _organizer.GetAsync($"/api/gameyas/{id}/members");
        membersResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var members = (await TestApi.JsonAsync(membersResponse)).EnumerateArray().ToList();
        members.Count.ShouldBe(2);
        members.Count(m => m.GetProperty("isOrganizer").GetBoolean()).ShouldBe(1);
        members.Single(m => m.GetProperty("userId").GetGuid() == memberId).GetProperty("shareCount").GetInt32().ShouldBe(2);

        var after = await member.GetAsync($"/api/invite/{code}");
        (await TestApi.JsonAsync(after)).GetProperty("availableShares").GetInt32().ShouldBe(7);
    }

    [Fact]
    public async Task Outsider_gets_404_on_every_gameya_endpoint()
    {
        var (id, _) = await RecruitingAsync();
        var (outsider, _) = await UserAsync();
        var stranger = Guid.NewGuid();

        var routes = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, $"/api/gameyas/{id}", null),
            (HttpMethod.Get, $"/api/gameyas/{id}/activity", null),
            (HttpMethod.Put, $"/api/gameyas/{id}", GameyaTestApi.Body(GameyaTestApi.Today())),
            (HttpMethod.Post, $"/api/gameyas/{id}/open", null),
            (HttpMethod.Post, $"/api/gameyas/{id}/invite-code", null),
            (HttpMethod.Post, $"/api/gameyas/{id}/cancel", new { reason = "valid reason" }),
            (HttpMethod.Get, $"/api/gameyas/{id}/members", null),
            (HttpMethod.Post, $"/api/gameyas/{id}/leave", null),
            (HttpMethod.Post, $"/api/gameyas/{id}/reaccept", null),
            (HttpMethod.Delete, $"/api/gameyas/{id}/members/{stranger}", new { reason = "valid reason" }),
            (HttpMethod.Get, $"/api/gameyas/{id}/join-requests", null),
            (HttpMethod.Post, $"/api/gameyas/{id}/join-requests/{stranger}/approve", null),
            (HttpMethod.Post, $"/api/gameyas/{id}/join-requests/{stranger}/reject", new { reason = "valid reason" }),
            (HttpMethod.Delete, $"/api/gameyas/{id}/join-requests/mine", null),
            (HttpMethod.Get, $"/api/gameyas/{id}/invites", null),
            (HttpMethod.Post, $"/api/gameyas/{id}/invites", new { phone = TestApi.NewPhone(), shareCount = 1 }),
            (HttpMethod.Delete, $"/api/gameyas/{id}/invites/{stranger}", null),
        };

        foreach (var (method, path, body) in routes)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            var response = await outsider.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{method} {path}: {await response.Content.ReadAsStringAsync()}");
            (await TestApi.JsonAsync(response)).GetProperty("code").GetString().ShouldBe("GAMEYA_NOT_FOUND", $"{method} {path}");
        }
    }

    [Fact]
    public async Task Two_concurrent_approvals_for_the_last_share()
    {
        var (id, code) = await RecruitingAsync(new { shareCount = 3 });
        var (first, firstId) = await UserAsync();
        var (second, secondId) = await UserAsync();
        var (third, thirdId) = await UserAsync();
        (await JoinAsync(first, code)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ApproveAsync(id, await PendingRequestIdAsync(id, firstId))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await JoinAsync(second, code)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await JoinAsync(third, code)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var secondRequest = await PendingRequestIdAsync(id, secondId);
        var thirdRequest = await PendingRequestIdAsync(id, thirdId);

        var results = await Task.WhenAll(ApproveAsync(id, secondRequest), ApproveAsync(id, thirdRequest));

        results.Count(r => r.StatusCode == HttpStatusCode.NoContent).ShouldBe(1);
        var loser = results.Single(r => r.StatusCode != HttpStatusCode.NoContent);
        await ShouldFailWith(loser, HttpStatusCode.Conflict, "CONCURRENCY_CONFLICT");

        await using var ctx = _db.CreateAppContext();
        var shares = await ctx.Gameyas.Where(g => g.Id == id)
            .SelectMany(g => g.Memberships).Where(m => m.Status == MembershipStatus.Active)
            .SelectMany(m => m.Shares).CountAsync();
        shares.ShouldBe(3);
    }

    [Fact]
    public async Task Double_tapped_invite_to_the_same_phone()
    {
        var (id, _) = await RecruitingAsync();
        var phone = TestApi.NewPhone();

        var results = await Task.WhenAll(
            _organizer.PostAsJsonAsync($"/api/gameyas/{id}/invites", new { phone, shareCount = 1 }),
            _organizer.PostAsJsonAsync($"/api/gameyas/{id}/invites", new { phone, shareCount = 1 }));

        results.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        var loser = results.Single(r => r.StatusCode != HttpStatusCode.Created);
        loser.StatusCode.ShouldBe(HttpStatusCode.Conflict, await loser.Content.ReadAsStringAsync());

        await using var ctx = _db.CreateAppContext();
        var pending = await ctx.PendingInvites.CountAsync(i => i.GameyaId == id && i.Status == InviteStatus.Pending);
        pending.ShouldBe(1);
    }

    [Fact]
    public async Task Double_tapped_join()
    {
        var (id, code) = await RecruitingAsync();
        var (member, _) = await UserAsync();

        var results = await Task.WhenAll(JoinAsync(member, code), JoinAsync(member, code));

        results.Count(r => r.StatusCode == HttpStatusCode.NoContent).ShouldBe(1);
        var loser = results.Single(r => r.StatusCode != HttpStatusCode.NoContent);
        loser.StatusCode.ShouldBe(HttpStatusCode.Conflict, await loser.Content.ReadAsStringAsync());

        await using var ctx = _db.CreateAppContext();
        (await ctx.JoinRequests.CountAsync(r => r.GameyaId == id && r.Status == JoinRequestStatus.Pending)).ShouldBe(1);
    }
}
