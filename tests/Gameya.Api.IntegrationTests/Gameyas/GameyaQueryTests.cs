using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using Gameya.Domain.Users;
using Microsoft.EntityFrameworkCore;
using static Gameya.Api.IntegrationTests.Gameyas.GameyaSeed;

namespace Gameya.Api.IntegrationTests.Gameyas;

/// <summary>
/// The read side: invite preview, my gam'eyas, my invites, details, members and activity. The auth endpoints allow
/// ten calls per factory, so each test logs in at most five users.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class GameyaQueryTests : IDisposable
{
    private readonly DatabaseFixture _db;
    private readonly GameyaApiFactory _factory;
    private readonly HttpClient _organizer;
    private readonly List<HttpClient> _clients = [];

    public GameyaQueryTests(DatabaseFixture db)
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

    private HttpClient Anonymous()
    {
        var client = _factory.CreateClient();
        _clients.Add(client);
        return client;
    }

    private async Task<(HttpClient Client, Guid UserId)> UserAsync(string? phone = null, string? name = null)
    {
        var client = Anonymous();
        var userId = await TestApi.LoginAsync(_factory, client, phone);
        if (name is not null)
        {
            await NameAsync(client, name);
        }

        return (client, userId);
    }

    private static async Task NameAsync(HttpClient client, string name) =>
        await Ok(await client.PutAsJsonAsync("/api/me/profile", new { fullName = name, language = "en" }));

    private async Task<Guid> LoginOrganizerAsync(string? phone = null, string? name = null)
    {
        var id = await TestApi.LoginAsync(_factory, _organizer, phone);
        if (name is not null)
        {
            await NameAsync(_organizer, name);
        }

        return id;
    }

    private async Task<(Guid Id, string Code)> RecruitingAsync(object? overrides = null)
    {
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

    private static async Task<(JsonElement Json, string Raw)> PreviewAsync(HttpClient client, string code)
    {
        var response = await client.GetAsync($"/api/invite/{code}");
        await Ok(response, HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        return (JsonDocument.Parse(raw).RootElement.Clone(), raw);
    }

    private async Task<Guid> PendingRequestIdAsync(Guid gameyaId, Guid userId)
    {
        await using var ctx = _db.CreateAppContext();
        return await ctx.JoinRequests
            .Where(r => r.GameyaId == gameyaId && r.UserId == userId && r.Status == JoinRequestStatus.Pending)
            .Select(r => r.Id)
            .SingleAsync();
    }

    private async Task MemberAsync(Guid gameyaId, string code, HttpClient client, Guid userId, int shares = 1)
    {
        await Ok(await client.PostAsJsonAsync($"/api/invite/{code}/join", JoinBody(shares)));
        await Ok(await _organizer.PostAsync(
            $"/api/gameyas/{gameyaId}/join-requests/{await PendingRequestIdAsync(gameyaId, userId)}/approve", null));
    }

    private Task<HttpResponseMessage> ChangeAsync(Guid gameyaId, object overrides) =>
        _organizer.PutAsJsonAsync($"/api/gameyas/{gameyaId}", GameyaTestApi.Body(GameyaTestApi.Today(), overrides));

    private async Task<string> UnusedCodeAsync()
    {
        await using var ctx = _db.CreateAppContext();
        string code;
        do
        {
            code = UniqueCode();
        }
        while (await ctx.Gameyas.AnyAsync(g => g.InviteCode == code));

        return code;
    }

    [Fact]
    public async Task Guest_preview_has_no_member_names_and_no_relationship()
    {
        var organizerPhone = TestApi.NewPhone();
        var memberPhone = TestApi.NewPhone();
        await LoginOrganizerAsync(organizerPhone, "Organizer Olfat");
        var (id, code) = await RecruitingAsync(new { rulesText = "Pay by the 5th", description = "Cousins" });
        var (member, memberId) = await UserAsync(memberPhone, "Secret Samir");
        await MemberAsync(id, code, member, memberId);

        var (preview, raw) = await PreviewAsync(Anonymous(), code);

        preview.GetProperty("gameyaId").GetGuid().ShouldBe(id);
        preview.GetProperty("state").GetString().ShouldBe("Open");
        preview.GetProperty("name").GetString().ShouldBe("Family circle");
        preview.GetProperty("description").GetString().ShouldBe("Cousins");
        preview.GetProperty("contributionAmount").GetDecimal().ShouldBe(1000m);
        preview.GetProperty("currency").GetString().ShouldBe("EGP");
        preview.GetProperty("shareCount").GetInt32().ShouldBe(10);
        preview.GetProperty("pot").GetDecimal().ShouldBe(10_000m);
        preview.GetProperty("orderMethod").GetString().ShouldBe("ByRequest");
        preview.GetProperty("maxSharesPerMember").GetInt32().ShouldBe(2);
        preview.GetProperty("availableShares").GetInt32().ShouldBe(8);
        preview.GetProperty("rulesText").GetString().ShouldBe("Pay by the 5th");
        preview.GetProperty("organizerName").GetString().ShouldBe("Organizer Olfat");
        preview.GetProperty("relationship").ValueKind.ShouldBe(JsonValueKind.Null);
        preview.GetProperty("requestNeedsReaccept").GetBoolean().ShouldBeFalse();

        // AC-G2: no member names, no phones (organizer's included), no user ids, no member list at all.
        raw.ShouldNotContain("Secret Samir");
        raw.ShouldNotContain("fullName", Case.Insensitive);
        raw.ShouldNotContain(memberId.ToString());
        foreach (var phone in new[] { organizerPhone, memberPhone })
        {
            raw.ShouldNotContain(phone[1..]);
        }

        // The code is matched however the visitor typed it; an unknown code is a plain 404.
        (await PreviewAsync(Anonymous(), code.ToLowerInvariant())).Json.GetProperty("gameyaId").GetGuid().ShouldBe(id);
        await ShouldFailWith(
            await Anonymous().GetAsync($"/api/invite/{await UnusedCodeAsync()}"),
            HttpStatusCode.NotFound, ErrorCodes.INVITE_CODE_INVALID);
    }

    [Fact]
    public async Task Logged_in_preview_reports_relationship()
    {
        await LoginOrganizerAsync();
        var (id, code) = await RecruitingAsync();
        var (visitor, visitorId) = await UserAsync();

        (await PreviewAsync(visitor, code)).Json.GetProperty("relationship").GetString().ShouldBe("None");

        await Ok(await visitor.PostAsJsonAsync($"/api/invite/{code}/join", JoinBody(1)));
        var pending = (await PreviewAsync(visitor, code)).Json;
        pending.GetProperty("relationship").GetString().ShouldBe("RequestPending");
        pending.GetProperty("requestNeedsReaccept").GetBoolean().ShouldBeFalse();
        // A pending request reserves nothing.
        pending.GetProperty("availableShares").GetInt32().ShouldBe(9);

        await Ok(await ChangeAsync(id, new { contributionAmount = 1500 }));
        (await PreviewAsync(visitor, code)).Json.GetProperty("requestNeedsReaccept").GetBoolean().ShouldBeTrue();

        await Ok(await visitor.PostAsync($"/api/gameyas/{id}/reaccept", null));
        (await PreviewAsync(visitor, code)).Json.GetProperty("requestNeedsReaccept").GetBoolean().ShouldBeFalse();

        await Ok(await _organizer.PostAsync(
            $"/api/gameyas/{id}/join-requests/{await PendingRequestIdAsync(id, visitorId)}/approve", null));
        (await PreviewAsync(visitor, code)).Json.GetProperty("relationship").GetString().ShouldBe("Member");
        (await PreviewAsync(_organizer, code)).Json.GetProperty("relationship").GetString().ShouldBe("Organizer");
    }

    [Fact]
    public async Task Full_and_cancelled_previews()
    {
        await LoginOrganizerAsync();
        var (id, code) = await RecruitingAsync(new { shareCount = 2 });
        var (member, memberId) = await UserAsync();
        await MemberAsync(id, code, member, memberId);

        // AC-G5: the last share is assigned.
        var full = (await PreviewAsync(Anonymous(), code)).Json;
        full.GetProperty("state").GetString().ShouldBe("Full");
        full.GetProperty("availableShares").GetInt32().ShouldBe(0);

        await Ok(await _organizer.PostAsync($"/api/gameyas/{id}/invite-code", null));
        var newCode = await GameyaTestApi.CodeAsync(_db, id);
        newCode.ShouldNotBe(code);
        // The old code looks exactly like one that never existed.
        var old = await Anonymous().GetAsync($"/api/invite/{code}");
        var unknown = await Anonymous().GetAsync($"/api/invite/{await UnusedCodeAsync()}");
        await ShouldFailWith(old, HttpStatusCode.NotFound, ErrorCodes.INVITE_CODE_INVALID);
        await ShouldFailWith(unknown, HttpStatusCode.NotFound, ErrorCodes.INVITE_CODE_INVALID);
        static string WithoutTrace(JsonElement e) =>
            string.Join(",", e.EnumerateObject().Where(p => p.Name != "traceId").Select(p => $"{p.Name}={p.Value}"));
        WithoutTrace(await TestApi.JsonAsync(old)).ShouldBe(WithoutTrace(await TestApi.JsonAsync(unknown)));

        await Ok(await _organizer.PostAsJsonAsync($"/api/gameyas/{id}/cancel", new { reason = "Changed plans" }));
        var cancelled = (await PreviewAsync(Anonymous(), newCode)).Json;
        cancelled.GetProperty("state").GetString().ShouldBe("Cancelled");
        cancelled.GetProperty("gameyaId").GetGuid().ShouldBe(id);
    }

    [Fact]
    public async Task Preview_is_rate_limited()
    {
        var guest = Anonymous();
        var code = await UnusedCodeAsync();

        for (var i = 0; i < 60; i++)
        {
            (await guest.GetAsync($"/api/invite/{code}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        await ShouldFailWith(
            await guest.GetAsync($"/api/invite/{code}"), HttpStatusCode.TooManyRequests, ErrorCodes.RATE_LIMITED);
    }

    [Fact]
    public async Task My_gameyas_lists_roles_and_needs_reaccept()
    {
        await LoginOrganizerAsync();
        var (recruiting, code) = await RecruitingAsync(new { name = "Zeta circle" });
        var draft = await GameyaTestApi.CreateAsync(_organizer, new { name = "Alpha circle" });
        var (cancelled, _) = await RecruitingAsync(new { name = "Beta circle" });
        await Ok(await _organizer.PostAsJsonAsync($"/api/gameyas/{cancelled}/cancel", new { reason = (string?)null }));
        var (member, memberId) = await UserAsync();
        await MemberAsync(recruiting, code, member, memberId, shares: 2);
        await Ok(await ChangeAsync(recruiting, new { name = "Zeta circle", contributionAmount = 2000 }));

        var mine = await GetJsonAsync(_organizer, "/api/gameyas");
        mine.EnumerateArray().Select(g => g.GetProperty("id").GetGuid()).ShouldBe([recruiting, draft, cancelled]);
        mine.EnumerateArray().Select(g => g.GetProperty("status").GetString())
            .ShouldBe(["Recruiting", "Draft", "Cancelled"]);
        mine.EnumerateArray().ShouldAllBe(g => g.GetProperty("role").GetString() == "Organizer");
        mine.EnumerateArray().ShouldAllBe(g => !g.GetProperty("needsReaccept").GetBoolean());
        var first = mine[0];
        first.GetProperty("name").GetString().ShouldBe("Zeta circle");
        first.GetProperty("contributionAmount").GetDecimal().ShouldBe(2000m);
        first.GetProperty("shareCount").GetInt32().ShouldBe(10);
        first.GetProperty("myShareCount").GetInt32().ShouldBe(1);

        var theirs = (await GetJsonAsync(member, "/api/gameyas")).EnumerateArray().ToList();
        var only = theirs.ShouldHaveSingleItem();
        only.GetProperty("id").GetGuid().ShouldBe(recruiting);
        only.GetProperty("role").GetString().ShouldBe("Member");
        only.GetProperty("myShareCount").GetInt32().ShouldBe(2);
        only.GetProperty("needsReaccept").GetBoolean().ShouldBeTrue();

        await Ok(await member.PostAsync($"/api/gameyas/{recruiting}/reaccept", null));
        (await GetJsonAsync(member, "/api/gameyas"))[0].GetProperty("needsReaccept").GetBoolean().ShouldBeFalse();

        // A member who left no longer sees it.
        await Ok(await member.PostAsync($"/api/gameyas/{recruiting}/leave", null));
        (await GetJsonAsync(member, "/api/gameyas")).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task My_invites_lists_pending_invites_for_my_phone()
    {
        await LoginOrganizerAsync(name: "Organizer Olfat");
        var (id, _) = await RecruitingAsync(new { orderMethod = "RandomDraw" });
        var phone = TestApi.NewPhone();
        var (invitee, _) = await UserAsync(phone);
        var (other, _) = await UserAsync();

        var response = await _organizer.PostAsJsonAsync($"/api/gameyas/{id}/invites", new { phone, shareCount = 2 });
        await Ok(response, HttpStatusCode.Created);
        var inviteId = (await TestApi.JsonAsync(response)).GetProperty("id").GetGuid();
        await Ok(await _organizer.PostAsJsonAsync(
            $"/api/gameyas/{id}/invites", new { phone = TestApi.NewPhone(), shareCount = 1 }), HttpStatusCode.Created);

        var invite = (await GetJsonAsync(invitee, "/api/me/invites")).EnumerateArray().ShouldHaveSingleItem();
        invite.GetProperty("inviteId").GetGuid().ShouldBe(inviteId);
        invite.GetProperty("gameyaId").GetGuid().ShouldBe(id);
        invite.GetProperty("name").GetString().ShouldBe("Family circle");
        invite.GetProperty("contributionAmount").GetDecimal().ShouldBe(1000m);
        invite.GetProperty("shareCount").GetInt32().ShouldBe(10);
        invite.GetProperty("roundCount").GetInt32().ShouldBe(10);
        invite.GetProperty("invitedShares").GetInt32().ShouldBe(2);
        invite.GetProperty("orderMethod").GetString().ShouldBe("RandomDraw");
        invite.GetProperty("organizerName").GetString().ShouldBe("Organizer Olfat");

        (await GetJsonAsync(other, "/api/me/invites")).GetArrayLength().ShouldBe(0);

        // A cancelled gam'eya's invites are gone.
        await Ok(await _organizer.PostAsJsonAsync($"/api/gameyas/{id}/cancel", new { reason = (string?)null }));
        (await GetJsonAsync(invitee, "/api/me/invites")).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Members_show_full_phone_to_organizer_and_masked_to_members()
    {
        var organizerPhone = PhoneNumber.Parse(TestApi.NewPhone());
        var memberPhone = PhoneNumber.Parse(TestApi.NewPhone());
        var otherPhone = PhoneNumber.Parse(TestApi.NewPhone());
        var organizerId = await LoginOrganizerAsync(organizerPhone.E164, "Organizer Olfat");
        var (id, code) = await RecruitingAsync();
        var (member, memberId) = await UserAsync(memberPhone.E164, "Member Mona");
        var (other, otherId) = await UserAsync(otherPhone.E164);
        var (outsider, _) = await UserAsync();
        await MemberAsync(id, code, member, memberId, shares: 2);
        await MemberAsync(id, code, other, otherId);

        static Dictionary<Guid, JsonElement> ById(JsonElement list) =>
            list.EnumerateArray().ToDictionary(m => m.GetProperty("userId").GetGuid());
        static string? Phone(JsonElement m) => m.GetProperty("phone").GetString();

        var asOrganizer = ById(await GetJsonAsync(_organizer, $"/api/gameyas/{id}/members"));
        asOrganizer.Count.ShouldBe(3);
        Phone(asOrganizer[memberId]).ShouldBe(memberPhone.E164);
        Phone(asOrganizer[otherId]).ShouldBe(otherPhone.E164);
        asOrganizer[organizerId].GetProperty("isOrganizer").GetBoolean().ShouldBeTrue();
        asOrganizer[organizerId].GetProperty("fullName").GetString().ShouldBe("Organizer Olfat");
        asOrganizer[memberId].GetProperty("isOrganizer").GetBoolean().ShouldBeFalse();
        asOrganizer[memberId].GetProperty("shareCount").GetInt32().ShouldBe(2);
        asOrganizer[memberId].GetProperty("fullName").GetString().ShouldBe("Member Mona");
        asOrganizer[otherId].GetProperty("fullName").ValueKind.ShouldBe(JsonValueKind.Null);

        var asMember = ById(await GetJsonAsync(member, $"/api/gameyas/{id}/members"));
        Phone(asMember[memberId]).ShouldBe(memberPhone.E164);
        Phone(asMember[otherId]).ShouldBe(otherPhone.Masked);
        Phone(asMember[organizerId]).ShouldBe(organizerPhone.Masked);

        await ShouldFailWith(
            await outsider.GetAsync($"/api/gameyas/{id}/members"), HttpStatusCode.NotFound, ErrorCodes.GAMEYA_NOT_FOUND);

        // Organizer-only lists: members get 404, the organizer sees full phones.
        var invitedPhone = PhoneNumber.Parse(TestApi.NewPhone());
        await Ok(await _organizer.PostAsJsonAsync(
            $"/api/gameyas/{id}/invites", new { phone = invitedPhone.E164, shareCount = 1 }), HttpStatusCode.Created);
        var invite = (await GetJsonAsync(_organizer, $"/api/gameyas/{id}/invites")).EnumerateArray().ShouldHaveSingleItem();
        invite.GetProperty("phone").GetString().ShouldBe(invitedPhone.E164);
        invite.GetProperty("shareCount").GetInt32().ShouldBe(1);
        invite.TryGetProperty("registeredName", out _).ShouldBeFalse();
        await ShouldFailWith(
            await member.GetAsync($"/api/gameyas/{id}/invites"), HttpStatusCode.NotFound, ErrorCodes.GAMEYA_NOT_FOUND);

        await Ok(await outsider.PostAsJsonAsync($"/api/invite/{code}/join", JoinBody(1)));
        var request = (await GetJsonAsync(_organizer, $"/api/gameyas/{id}/join-requests"))
            .EnumerateArray().ShouldHaveSingleItem();
        request.GetProperty("phone").GetString().ShouldStartWith("+20");
        request.GetProperty("sharePreferences").GetArrayLength().ShouldBe(1);
        request.GetProperty("termsCurrent").GetBoolean().ShouldBeTrue();
        await ShouldFailWith(
            await member.GetAsync($"/api/gameyas/{id}/join-requests"), HttpStatusCode.NotFound, ErrorCodes.GAMEYA_NOT_FOUND);
    }

    [Fact]
    public async Task Get_gameya_hides_invite_code_from_members_and_returns_terms_diff()
    {
        var organizerId = await LoginOrganizerAsync();
        var (id, code) = await RecruitingAsync();
        var (member, memberId) = await UserAsync();
        var (requester, _) = await UserAsync();
        await MemberAsync(id, code, member, memberId);
        await Ok(await requester.PostAsJsonAsync($"/api/invite/{code}/join", JoinBody(1)));

        // Two money changes: the banner compares against the terms the member accepted (1000), not the middle step.
        await Ok(await ChangeAsync(id, new { contributionAmount = 1500 }));
        await Ok(await ChangeAsync(id, new { contributionAmount = 2000 }));

        var asOrganizer = await GetJsonAsync(_organizer, $"/api/gameyas/{id}");
        asOrganizer.GetProperty("id").GetGuid().ShouldBe(id);
        asOrganizer.GetProperty("inviteCode").GetString().ShouldBe(code);
        asOrganizer.GetProperty("pendingJoinRequestCount").GetInt32().ShouldBe(1);
        asOrganizer.GetProperty("myRole").GetString().ShouldBe("Organizer");
        asOrganizer.GetProperty("myShareCount").GetInt32().ShouldBe(1);
        asOrganizer.GetProperty("needsReaccept").GetBoolean().ShouldBeFalse();
        asOrganizer.GetProperty("termsDiff").ValueKind.ShouldBe(JsonValueKind.Null);
        asOrganizer.GetProperty("status").GetString().ShouldBe("Recruiting");
        asOrganizer.GetProperty("termsVersion").GetInt32().ShouldBe(3);
        asOrganizer.GetProperty("contributionAmount").GetDecimal().ShouldBe(2000m);
        asOrganizer.GetProperty("pot").GetDecimal().ShouldBe(20_000m);
        asOrganizer.GetProperty("totalPerShare").GetDecimal().ShouldBe(20_000m);
        asOrganizer.GetProperty("roundCount").GetInt32().ShouldBe(10);
        asOrganizer.GetProperty("assignedShares").GetInt32().ShouldBe(2);
        asOrganizer.GetProperty("reservedShares").GetInt32().ShouldBe(0);
        asOrganizer.GetProperty("availableShares").GetInt32().ShouldBe(8);
        asOrganizer.GetProperty("currency").GetString().ShouldBe("EGP");
        var start = DateOnly.Parse(asOrganizer.GetProperty("startDate").GetString()!);
        DateOnly.Parse(asOrganizer.GetProperty("endDate").GetString()!).ShouldBe(CairoCalendar.MonthlyDueDate(start, 9));

        var response = await member.GetAsync($"/api/gameyas/{id}");
        await Ok(response, HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain(code);
        var asMember = await TestApi.JsonAsync(response);
        asMember.GetProperty("inviteCode").ValueKind.ShouldBe(JsonValueKind.Null);
        asMember.GetProperty("pendingJoinRequestCount").ValueKind.ShouldBe(JsonValueKind.Null);
        asMember.GetProperty("myRole").GetString().ShouldBe("Member");
        asMember.GetProperty("needsReaccept").GetBoolean().ShouldBeTrue();
        var diff = asMember.GetProperty("termsDiff");
        diff.GetProperty("oldContributionAmount").GetDecimal().ShouldBe(1000m);
        diff.GetProperty("oldShareCount").ValueKind.ShouldBe(JsonValueKind.Null);
        diff.GetProperty("oldFrequency").ValueKind.ShouldBe(JsonValueKind.Null);

        await Ok(await member.PostAsync($"/api/gameyas/{id}/reaccept", null));
        var after = await GetJsonAsync(member, $"/api/gameyas/{id}");
        after.GetProperty("needsReaccept").GetBoolean().ShouldBeFalse();
        after.GetProperty("termsDiff").ValueKind.ShouldBe(JsonValueKind.Null);

        // Only changes after the accepted version count: a later share-count change shows alone.
        await Ok(await ChangeAsync(id, new { contributionAmount = 2000, shareCount = 12 }));
        var later = (await GetJsonAsync(member, $"/api/gameyas/{id}")).GetProperty("termsDiff");
        later.GetProperty("oldShareCount").GetInt32().ShouldBe(10);
        later.GetProperty("oldContributionAmount").ValueKind.ShouldBe(JsonValueKind.Null);

        organizerId.ShouldNotBe(memberId);
    }

    [Fact]
    public async Task Activity_is_paged_newest_first_with_deleted_user_name_null()
    {
        await LoginOrganizerAsync(name: "Organizer Olfat");
        var (id, code) = await RecruitingAsync();
        var (member, memberId) = await UserAsync(name: "Member Mona");
        await MemberAsync(id, code, member, memberId);
        await Ok(await member.PostAsync($"/api/gameyas/{id}/leave", null));
        await Ok(await member.DeleteAsync("/api/me"));
        for (var i = 1; i <= 20; i++)
        {
            await Ok(await ChangeAsync(id, new { name = $"Family circle {i}" }));
        }

        // 25 entries: created, opened, requested, approved, left, then 20 settings changes.
        var first = await GetJsonAsync(_organizer, $"/api/gameyas/{id}/activity?page=1");
        first.GetProperty("page").GetInt32().ShouldBe(1);
        first.GetProperty("hasMore").GetBoolean().ShouldBeTrue();
        var items = first.GetProperty("items").EnumerateArray().ToList();
        items.Count.ShouldBe(20);
        items.ShouldAllBe(e => e.GetProperty("action").GetString() == "GAMEYA_SETTINGS_CHANGED");
        items[0].GetProperty("details").GetProperty("changed").GetProperty("name").GetProperty("new").GetString()
            .ShouldBe("Family circle 20");
        items[0].GetProperty("actorName").GetString().ShouldBe("Organizer Olfat");

        var second = await GetJsonAsync(_organizer, $"/api/gameyas/{id}/activity?page=2");
        second.GetProperty("hasMore").GetBoolean().ShouldBeFalse();
        var rest = second.GetProperty("items").EnumerateArray().ToList();
        rest.Select(e => e.GetProperty("action").GetString())
            .ShouldBe(["MEMBER_LEFT", "JOIN_REQUEST_APPROVED", "JOIN_REQUESTED", "GAMEYA_OPENED", "GAMEYA_CREATED"]);
        rest[0].GetProperty("actorName").ValueKind.ShouldBe(JsonValueKind.Null);
        rest[2].GetProperty("actorName").ValueKind.ShouldBe(JsonValueKind.Null);
        rest[4].GetProperty("actorName").GetString().ShouldBe("Organizer Olfat");

        // The default page is the first; a page past the end is empty.
        (await GetJsonAsync(_organizer, $"/api/gameyas/{id}/activity")).GetProperty("items").GetArrayLength().ShouldBe(20);
        (await GetJsonAsync(_organizer, $"/api/gameyas/{id}/activity?page=3")).GetProperty("items").GetArrayLength()
            .ShouldBe(0);
    }
}
