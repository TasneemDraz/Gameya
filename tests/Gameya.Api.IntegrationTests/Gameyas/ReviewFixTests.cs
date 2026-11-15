using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using Microsoft.EntityFrameworkCore;
using static Gameya.Api.IntegrationTests.Gameyas.GameyaSeed;

namespace Gameya.Api.IntegrationTests.Gameyas;

/// <summary>
/// A person both invited and asking to join, the caller's own join requests, what the organizer and members may
/// learn about non-members, account deletion with pending requests and invites, and the endpoint defaults. The auth
/// endpoints allow ten calls per factory, so each test logs in at most five users.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ReviewFixTests : IDisposable
{
    private readonly DatabaseFixture _db;
    private readonly GameyaApiFactory _factory;
    private readonly HttpClient _organizer;
    private readonly List<HttpClient> _clients = [];

    public ReviewFixTests(DatabaseFixture db)
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

    private async Task<(HttpClient Client, Guid UserId)> UserAsync(string? phone = null, string? name = null)
    {
        var client = _factory.CreateClient();
        _clients.Add(client);
        var userId = await TestApi.LoginAsync(_factory, client, phone);
        if (name is not null)
        {
            await Ok(await client.PutAsJsonAsync("/api/me/profile", new { fullName = name, language = "en" }));
        }

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

    private static async Task Ok(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.NoContent) =>
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());

    private static async Task ShouldFailWith(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
        (await TestApi.JsonAsync(response)).GetProperty("code").GetString().ShouldBe(code);
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        await Ok(response, HttpStatusCode.OK);
        return await TestApi.JsonAsync(response);
    }

    private static Task<HttpResponseMessage> JoinAsync(HttpClient client, string code, int shares = 1) =>
        client.PostAsJsonAsync($"/api/invite/{code}/join", JoinBody(shares));

    private Task<HttpResponseMessage> InviteAsync(Guid gameyaId, string phone, int shareCount = 1) =>
        _organizer.PostAsJsonAsync($"/api/gameyas/{gameyaId}/invites", new { phone, shareCount });

    private async Task<Guid> InviteIdAsync(Guid gameyaId, string phone, int shareCount = 1)
    {
        var response = await InviteAsync(gameyaId, phone, shareCount);
        await Ok(response, HttpStatusCode.Created);
        return (await TestApi.JsonAsync(response)).GetProperty("id").GetGuid();
    }

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

    [Fact]
    public async Task Approving_a_request_revokes_the_invite_sent_to_the_requesters_phone()
    {
        var (id, code) = await RecruitingAsync();
        var phone = TestApi.NewPhone();
        // Invited before signing up, so the invite has no user id and matches by phone only.
        var inviteId = await InviteIdAsync(id, phone, shareCount: 2);
        var (person, personId) = await UserAsync(phone);

        // The preview sends an invited visitor to their invite instead of the join form.
        var preview = await GetJsonAsync(person, $"/api/invite/{code}");
        preview.GetProperty("relationship").GetString().ShouldBe("Invited");
        preview.GetProperty("inviteId").GetGuid().ShouldBe(inviteId);

        await Ok(await JoinAsync(person, code));
        await Ok(await ApproveAsync(id, await PendingRequestIdAsync(id, personId)));

        await using var ctx = _db.CreateAppContext();
        var stored = await LoadAsync(ctx, id);
        stored.PendingInvites.Single(i => i.Id == inviteId).Status.ShouldBe(InviteStatus.Revoked);
        stored.ReservedShares.ShouldBe(0);
        stored.AvailableShares.ShouldBe(8);
        (await ctx.ActivityLog.CountAsync(a => a.GameyaId == id && a.Action == "INVITE_REVOKED")).ShouldBe(1);

        // Now a member: the invite is gone from their list and the preview says so.
        (await GetJsonAsync(person, "/api/me/invites")).GetArrayLength().ShouldBe(0);
        (await GetJsonAsync(person, $"/api/invite/{code}")).GetProperty("relationship").GetString().ShouldBe("Member");
    }

    [Fact]
    public async Task Inviting_someone_who_already_asked_to_join_is_409_INVITEE_REQUEST_PENDING()
    {
        var (id, code) = await RecruitingAsync();
        var phone = TestApi.NewPhone();
        var (requester, _) = await UserAsync(phone);
        await Ok(await JoinAsync(requester, code));

        await ShouldFailWith(await InviteAsync(id, phone), HttpStatusCode.Conflict, ErrorCodes.INVITEE_REQUEST_PENDING);

        await using var ctx = _db.CreateAppContext();
        (await ctx.PendingInvites.CountAsync(i => i.GameyaId == id)).ShouldBe(0);
    }

    [Fact]
    public async Task My_join_requests_lists_only_the_callers_pending_requests()
    {
        var (id, code) = await RecruitingAsync(new { name = "Neighbours circle" });
        var (requester, _) = await UserAsync();
        var (other, _) = await UserAsync();
        await Ok(await JoinAsync(requester, code, shares: 2));

        var mine = (await GetJsonAsync(requester, "/api/me/join-requests")).EnumerateArray().ShouldHaveSingleItem();
        mine.GetProperty("gameyaId").GetGuid().ShouldBe(id);
        mine.GetProperty("name").GetString().ShouldBe("Neighbours circle");
        mine.GetProperty("status").GetString().ShouldBe("Pending");
        mine.GetProperty("shareCount").GetInt32().ShouldBe(2);
        mine.GetProperty("needsReaccept").GetBoolean().ShouldBeFalse();
        (await GetJsonAsync(other, "/api/me/join-requests")).GetArrayLength().ShouldBe(0);
        (await GetJsonAsync(_organizer, "/api/me/join-requests")).GetArrayLength().ShouldBe(0);

        await Ok(await _organizer.PutAsJsonAsync(
            $"/api/gameyas/{id}",
            GameyaTestApi.Body(GameyaTestApi.Today(), new { name = "Neighbours circle", contributionAmount = 2000 })));
        (await GetJsonAsync(requester, "/api/me/join-requests"))[0].GetProperty("needsReaccept").GetBoolean()
            .ShouldBeTrue();

        await Ok(await requester.PostAsync($"/api/gameyas/{id}/reaccept", null));
        (await GetJsonAsync(requester, "/api/me/join-requests"))[0].GetProperty("needsReaccept").GetBoolean()
            .ShouldBeFalse();

        await Ok(await requester.DeleteAsync($"/api/gameyas/{id}/join-requests/mine"));
        (await GetJsonAsync(requester, "/api/me/join-requests")).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_organizer_sees_no_name_or_account_for_a_pending_invite()
    {
        var (id, _) = await RecruitingAsync();
        var phone = TestApi.NewPhone();
        var (invitee, inviteeId) = await UserAsync(phone, "Invited Iman");

        var inviteId = await InviteIdAsync(id, phone);

        var row = (await GetJsonAsync(_organizer, $"/api/gameyas/{id}/invites")).EnumerateArray().ShouldHaveSingleItem();
        row.TryGetProperty("registeredName", out _).ShouldBeFalse(row.ToString());
        row.ToString().ShouldNotContain("Invited Iman");
        row.ToString().ShouldNotContain(inviteeId.ToString());
        var activity = await GetJsonAsync(_organizer, $"/api/gameyas/{id}/activity");
        activity.ToString().ShouldNotContain(inviteeId.ToString());
        activity.ToString().ShouldNotContain("registeredUserId");

        // A decline does not reveal who declined either.
        await Ok(await invitee.PostAsync($"/api/me/invites/{inviteId}/decline", null));
        var declined = (await GetJsonAsync(_organizer, $"/api/gameyas/{id}/activity")).GetProperty("items")
            .EnumerateArray().Single(e => e.GetProperty("action").GetString() == "INVITE_DECLINED");
        declined.GetProperty("actorName").ValueKind.ShouldBe(JsonValueKind.Null);
        declined.ToString().ShouldNotContain(inviteeId.ToString());
    }

    [Fact]
    public async Task Members_do_not_see_join_request_entries_or_removal_notes()
    {
        var (id, code) = await RecruitingAsync();
        var (member, memberId) = await UserAsync();
        var (requester, requesterId) = await UserAsync(name: "Requester Rana");
        var (removed, removedId) = await UserAsync();

        await Ok(await JoinAsync(member, code));
        await Ok(await ApproveAsync(id, await PendingRequestIdAsync(id, memberId)));
        await Ok(await JoinAsync(removed, code));
        await Ok(await ApproveAsync(id, await PendingRequestIdAsync(id, removedId)));
        await Ok(await _organizer.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/gameyas/{id}/members/{removedId}")
        {
            Content = JsonContent.Create(new { reason = "Did not pay" }),
        }));
        await Ok(await JoinAsync(requester, code));
        await Ok(await requester.DeleteAsync($"/api/gameyas/{id}/join-requests/mine"));
        await Ok(await JoinAsync(requester, code));
        await Ok(await _organizer.PostAsJsonAsync(
            $"/api/gameyas/{id}/join-requests/{await PendingRequestIdAsync(id, requesterId)}/reject",
            new { reason = "Family only" }));

        static List<JsonElement> Items(JsonElement page) => [.. page.GetProperty("items").EnumerateArray()];
        static string Action(JsonElement e) => e.GetProperty("action").GetString()!;

        var asOrganizer = Items(await GetJsonAsync(_organizer, $"/api/gameyas/{id}/activity"));
        asOrganizer.Single(e => Action(e) == "JOIN_REQUEST_REJECTED").GetProperty("note").GetString()
            .ShouldBe("Family only");
        asOrganizer.Single(e => Action(e) == "MEMBER_REMOVED").GetProperty("note").GetString().ShouldBe("Did not pay");
        asOrganizer.Count(e => Action(e) == "JOIN_REQUESTED").ShouldBe(4);
        asOrganizer.Any(e => Action(e) == "JOIN_REQUEST_WITHDRAWN"
            && e.GetProperty("actorName").GetString() == "Requester Rana").ShouldBeTrue();

        var asMember = Items(await GetJsonAsync(member, $"/api/gameyas/{id}/activity"));
        asMember.Select(Action).ShouldNotContain("JOIN_REQUESTED");
        asMember.Select(Action).ShouldNotContain("JOIN_REQUEST_WITHDRAWN");
        asMember.Select(Action).ShouldNotContain("JOIN_REQUEST_REJECTED");
        asMember.Single(e => Action(e) == "MEMBER_REMOVED").GetProperty("note").ValueKind.ShouldBe(JsonValueKind.Null);
        asMember.Count(e => Action(e) == "JOIN_REQUEST_APPROVED").ShouldBe(2);
        foreach (var e in asMember)
        {
            e.ToString().ShouldNotContain("Requester Rana");
            e.ToString().ShouldNotContain("Family only");
        }
    }

    [Fact]
    public async Task Deleting_an_account_withdraws_its_requests_and_declines_its_invites()
    {
        var (first, firstCode) = await RecruitingAsync();
        var second = await GameyaTestApi.CreateAsync(_organizer);
        await GameyaTestApi.OpenAsync(_db, _organizer, second);
        var phone = TestApi.NewPhone();
        var invite = await InviteIdAsync(second, phone);
        var (person, personId) = await UserAsync(phone);
        await Ok(await JoinAsync(person, firstCode));

        await Ok(await person.DeleteAsync("/api/me"));

        await using var ctx = _db.CreateAppContext();
        (await ctx.JoinRequests.SingleAsync(r => r.GameyaId == first && r.UserId == personId)).Status
            .ShouldBe(JoinRequestStatus.Withdrawn);
        (await ctx.PendingInvites.SingleAsync(i => i.Id == invite)).Status.ShouldBe(InviteStatus.Declined);
        (await LoadAsync(ctx, second)).AvailableShares.ShouldBe(9);
        (await ctx.ActivityLog.CountAsync(a => a.GameyaId == first && a.Action == "JOIN_REQUEST_WITHDRAWN"))
            .ShouldBe(1);
        (await ctx.ActivityLog.CountAsync(a => a.GameyaId == second && a.Action == "INVITE_DECLINED")).ShouldBe(1);
    }

    [Fact]
    public async Task A_request_left_by_a_deleted_account_cannot_be_approved()
    {
        var (id, _) = await RecruitingAsync();
        var ghost = NewUser();
        Guid requestId;
        await using (var ctx = _db.CreateAppContext())
        {
            ctx.Users.Add(ghost);
            var gameya = await LoadAsync(ctx, id);
            requestId = gameya.RequestToJoin(ghost.Id, OneShare()).Id;
            await ctx.SaveChangesAsync();
            // Deleted without the handler, as an account deleted before requests were withdrawn would be.
            ghost.Delete(Now);
            await ctx.SaveChangesAsync();
        }

        await ShouldFailWith(
            await ApproveAsync(id, requestId), HttpStatusCode.Conflict, ErrorCodes.REQUESTER_ACCOUNT_DELETED);

        await using var after = _db.CreateAppContext();
        (await LoadAsync(after, id)).Memberships.ShouldNotContain(m => m.UserId == ghost.Id);
    }

    [Fact]
    public async Task Join_is_rate_limited()
    {
        var (joiner, _) = await UserAsync();
        string code;
        await using (var ctx = _db.CreateAppContext())
        {
            do
            {
                code = UniqueCode();
            }
            while (await ctx.Gameyas.AnyAsync(g => g.InviteCode == code));
        }

        for (var i = 0; i < 60; i++)
        {
            (await JoinAsync(joiner, code)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        await ShouldFailWith(await JoinAsync(joiner, code), HttpStatusCode.TooManyRequests, ErrorCodes.RATE_LIMITED);
    }

    [Fact]
    public async Task Create_defaults_grace_days_and_max_shares_and_requires_the_order_method()
    {
        await TestApi.LoginAsync(_factory, _organizer);
        var body = GameyaTestApi.ValidBody(GameyaTestApi.Today());
        body.Remove("graceDays");
        body.Remove("maxSharesPerMember");

        var created = await _organizer.PostAsJsonAsync("/api/gameyas", body);
        await Ok(created, HttpStatusCode.Created);
        var id = (await TestApi.JsonAsync(created)).GetProperty("id").GetGuid();
        await using (var ctx = _db.CreateAppContext())
        {
            var stored = await ctx.Gameyas.SingleAsync(g => g.Id == id);
            stored.GraceDays.ShouldBe(3);
            stored.MaxSharesPerMember.ShouldBe(2);
        }

        var noOrder = GameyaTestApi.ValidBody(GameyaTestApi.Today());
        noOrder.Remove("orderMethod");
        var response = await _organizer.PostAsJsonAsync("/api/gameyas", noOrder);
        await ShouldFailWith(response, HttpStatusCode.BadRequest, ErrorCodes.VALIDATION_FAILED);
        (await TestApi.JsonAsync(response)).GetProperty("errors").TryGetProperty("orderMethod", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Reasons_are_trimmed_before_their_length_is_checked()
    {
        var (id, code) = await RecruitingAsync();
        var (requester, requesterId) = await UserAsync();
        await Ok(await JoinAsync(requester, code));
        var padded = "  " + new string('x', 500) + "  ";

        await Ok(await _organizer.PostAsJsonAsync(
            $"/api/gameyas/{id}/join-requests/{await PendingRequestIdAsync(id, requesterId)}/reject",
            new { reason = "   " }));
        await Ok(await _organizer.PostAsJsonAsync($"/api/gameyas/{id}/cancel", new { reason = padded }));

        await using var ctx = _db.CreateAppContext();
        (await ctx.JoinRequests.SingleAsync(r => r.GameyaId == id && r.UserId == requesterId)).RejectReason
            .ShouldBeNull();
        (await ctx.Gameyas.SingleAsync(g => g.Id == id)).CancelReason.ShouldBe(new string('x', 500));
        (await ctx.ActivityLog.SingleAsync(a => a.GameyaId == id && a.Action == "JOIN_REQUEST_REJECTED")).Note
            .ShouldBeNull();
    }
}
