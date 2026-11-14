using System.Net;
using System.Net.Http.Json;
using Gameya.Api.IntegrationTests.Infrastructure;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using Gameya.Domain.Users;
using Microsoft.EntityFrameworkCore;
using static Gameya.Api.IntegrationTests.Gameyas.GameyaSeed;

namespace Gameya.Api.IntegrationTests.Gameyas;

/// <summary>
/// Join requests, direct invites, leave, remove and re-accept over HTTP. The auth endpoints allow ten calls per
/// factory, so each test logs in at most five users.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class JoiningTests : IDisposable
{
    private readonly DatabaseFixture _db;
    private readonly GameyaApiFactory _factory;
    private readonly HttpClient _organizer;
    private readonly List<HttpClient> _clients = [];

    public JoiningTests(DatabaseFixture db)
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

    private async Task<(HttpClient Client, Guid UserId)> UserAsync(string? phone = null)
    {
        var client = _factory.CreateClient();
        _clients.Add(client);
        var userId = await TestApi.LoginAsync(_factory, client, phone);
        return (client, userId);
    }

    /// <summary>Logs the organizer in, creates the gam'eya (with any overrides) and opens it.</summary>
    private async Task<(Guid Id, string Code)> RecruitingAsync(object? overrides = null)
    {
        await TestApi.LoginAsync(_factory, _organizer);
        var id = await GameyaTestApi.CreateAsync(_organizer, overrides);
        return (id, await GameyaTestApi.OpenAsync(_db, _organizer, id));
    }

    private static object JoinBody(int shares, bool acceptRules = true, bool acceptNoMoney = true) => new
    {
        shares = Enumerable.Range(0, shares).Select(_ => new { preferences = Array.Empty<int>() }).ToArray(),
        acceptRules,
        acceptNoMoney,
    };

    private static async Task Ok(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.NoContent) =>
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());

    private static async Task ShouldFailWith(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status, await response.Content.ReadAsStringAsync());
        (await TestApi.JsonAsync(response)).GetProperty("code").GetString().ShouldBe(code);
    }

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

    /// <summary>Joins with the given client and has the organizer approve the request.</summary>
    private async Task MemberAsync(Guid gameyaId, string code, HttpClient client, Guid userId, int shares = 1)
    {
        await Ok(await JoinAsync(client, code, shares));
        await Ok(await ApproveAsync(gameyaId, await PendingRequestIdAsync(gameyaId, userId)));
    }

    private async Task<Guid> InviteAsync(Guid gameyaId, string phone, int shareCount)
    {
        var response = await _organizer.PostAsJsonAsync($"/api/gameyas/{gameyaId}/invites", new { phone, shareCount });
        await Ok(response, HttpStatusCode.Created);
        return (await TestApi.JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> AcceptAsync(HttpClient client, Guid inviteId, int shares) =>
        client.PostAsJsonAsync($"/api/me/invites/{inviteId}/accept", JoinBody(shares));

    private static Task<HttpResponseMessage> RemoveAsync(HttpClient client, Guid gameyaId, Guid userId, string reason) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/gameyas/{gameyaId}/members/{userId}")
        {
            Content = JsonContent.Create(new { reason }),
        });

    private async Task<int> AvailableAsync(Guid gameyaId)
    {
        await using var ctx = _db.CreateAppContext();
        return (await LoadAsync(ctx, gameyaId)).AvailableShares;
    }

    private static async Task<List<Guid>> MyInviteIdsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/me/invites");
        await Ok(response, HttpStatusCode.OK);
        return [.. (await TestApi.JsonAsync(response)).EnumerateArray().Select(i => i.GetProperty("inviteId").GetGuid())];
    }

    private static string ArabicDigits(string latin) =>
        new([.. latin.Select(c => c is >= '0' and <= '9' ? (char)('٠' + (c - '0')) : c)]);

    [Fact]
    public async Task Join_then_approve_leaves_7_available()
    {
        var (id, code) = await RecruitingAsync();
        var (member, memberId) = await UserAsync();

        await Ok(await JoinAsync(member, code, shares: 2));
        await Ok(await ApproveAsync(id, await PendingRequestIdAsync(id, memberId)));

        await using var ctx = _db.CreateAppContext();
        var stored = await LoadAsync(ctx, id);
        stored.AvailableShares.ShouldBe(7);
        stored.Memberships.Single(m => m.UserId == memberId).Shares.Count.ShouldBe(2);
        stored.JoinRequests.Single(r => r.UserId == memberId).Status.ShouldBe(JoinRequestStatus.Approved);
    }

    [Fact]
    public async Task Approve_beyond_available_is_409_with_available_param()
    {
        var (id, code) = await RecruitingAsync(new { shareCount = 4 });
        var (first, firstId) = await UserAsync();
        var (second, secondId) = await UserAsync();
        await Ok(await JoinAsync(first, code, shares: 2));
        await Ok(await JoinAsync(second, code, shares: 2));
        await Ok(await ApproveAsync(id, await PendingRequestIdAsync(id, firstId)));

        var response = await ApproveAsync(id, await PendingRequestIdAsync(id, secondId));

        await ShouldFailWith(response, HttpStatusCode.Conflict, ErrorCodes.NOT_ENOUGH_SHARES);
        (await TestApi.JsonAsync(response)).GetProperty("params").GetProperty("available").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Join_without_both_checkboxes_is_400()
    {
        var (id, code) = await RecruitingAsync();
        var (member, _) = await UserAsync();

        var response = await member.PostAsJsonAsync(
            $"/api/invite/{code}/join", JoinBody(1, acceptRules: false, acceptNoMoney: false));

        await ShouldFailWith(response, HttpStatusCode.BadRequest, ErrorCodes.VALIDATION_FAILED);
        var errors = (await TestApi.JsonAsync(response)).GetProperty("errors");
        errors.TryGetProperty("acceptRules", out _).ShouldBeTrue(errors.ToString());
        errors.TryGetProperty("acceptNoMoney", out _).ShouldBeTrue(errors.ToString());

        await ShouldFailWith(
            await member.PostAsJsonAsync($"/api/invite/{code}/join", JoinBody(1, acceptNoMoney: false)),
            HttpStatusCode.BadRequest, ErrorCodes.VALIDATION_FAILED);

        await using var ctx = _db.CreateAppContext();
        (await ctx.JoinRequests.CountAsync(r => r.GameyaId == id)).ShouldBe(0);
    }

    [Fact]
    public async Task Join_with_unknown_code_is_404_INVITE_CODE_INVALID()
    {
        var (member, _) = await UserAsync();
        string code;
        await using (var ctx = _db.CreateAppContext())
        {
            do
            {
                code = UniqueCode();
            }
            while (await ctx.Gameyas.AnyAsync(g => g.InviteCode == code));
        }

        await ShouldFailWith(await JoinAsync(member, code), HttpStatusCode.NotFound, ErrorCodes.INVITE_CODE_INVALID);
    }

    [Fact]
    public async Task Stale_requester_can_reaccept_then_be_approved()
    {
        var (id, code) = await RecruitingAsync();
        var (requester, requesterId) = await UserAsync();
        await Ok(await JoinAsync(requester, code));
        await Ok(await _organizer.PutAsJsonAsync(
            $"/api/gameyas/{id}", GameyaTestApi.Body(GameyaTestApi.Today(), new { contributionAmount = 2000 })));
        var requestId = await PendingRequestIdAsync(id, requesterId);

        await ShouldFailWith(await ApproveAsync(id, requestId), HttpStatusCode.Conflict, ErrorCodes.TERMS_NOT_ACCEPTED);

        await Ok(await requester.PostAsync($"/api/gameyas/{id}/reaccept", null));
        await Ok(await ApproveAsync(id, requestId));

        await using var ctx = _db.CreateAppContext();
        var membership = (await LoadAsync(ctx, id)).Memberships.Single(m => m.UserId == requesterId);
        membership.Status.ShouldBe(MembershipStatus.Active);
        membership.AcceptedTermsVersion.ShouldBe(2);
    }

    [Fact]
    public async Task Invite_unregistered_phone_then_signup_and_accept()
    {
        var (id, _) = await RecruitingAsync();
        var phone = TestApi.NewPhone();

        var inviteId = await InviteAsync(id, phone, shareCount: 2);
        (await AvailableAsync(id)).ShouldBe(7);

        var (invitee, inviteeId) = await UserAsync(phone);
        // AC-G8: the invite made before signup is listed on first login.
        (await MyInviteIdsAsync(invitee)).ShouldBe([inviteId]);
        await Ok(await AcceptAsync(invitee, inviteId, shares: 2));
        (await MyInviteIdsAsync(invitee)).ShouldBeEmpty();

        await using var ctx = _db.CreateAppContext();
        var stored = await LoadAsync(ctx, id);
        stored.Memberships.Single(m => m.UserId == inviteeId && m.Status == MembershipStatus.Active)
            .Shares.Count.ShouldBe(2);
        var invite = stored.PendingInvites.Single(i => i.Id == inviteId);
        invite.Status.ShouldBe(InviteStatus.Accepted);
        invite.UserId.ShouldBe(inviteeId);
        stored.AvailableShares.ShouldBe(7);
    }

    [Fact]
    public async Task Invite_phone_typed_with_Arabic_digits_matches_the_user()
    {
        var (id, _) = await RecruitingAsync();
        var phone = TestApi.NewPhone();
        var (invitee, inviteeId) = await UserAsync(phone);

        var inviteId = await InviteAsync(id, ArabicDigits(phone), shareCount: 1);

        await using (var ctx = _db.CreateAppContext())
        {
            var invite = await ctx.PendingInvites.SingleAsync(i => i.Id == inviteId);
            invite.Phone.ShouldBe(PhoneNumber.Parse(phone));
            invite.UserId.ShouldBe(inviteeId);
        }

        // An invite to an existing user is listed for them.
        (await MyInviteIdsAsync(invitee)).ShouldBe([inviteId]);
        await Ok(await AcceptAsync(invitee, inviteId, shares: 1));
        await using var after = _db.CreateAppContext();
        (await LoadAsync(after, id)).Memberships.ShouldContain(m => m.UserId == inviteeId && m.Status == MembershipStatus.Active);
    }

    [Fact]
    public async Task Invite_with_an_invalid_phone_is_400_INVALID_PHONE()
    {
        var (id, _) = await RecruitingAsync();

        var response = await _organizer.PostAsJsonAsync(
            $"/api/gameyas/{id}/invites", new { phone = "12345", shareCount = 1 });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        var errors = (await TestApi.JsonAsync(response)).GetProperty("errors");
        errors.GetProperty("phone")[0].GetString().ShouldBe(ErrorCodes.INVALID_PHONE);
    }

    [Fact]
    public async Task Decline_releases_shares_and_revoke_hides_invite()
    {
        var (id, _) = await RecruitingAsync();
        var declinerPhone = TestApi.NewPhone();
        var revokedPhone = TestApi.NewPhone();
        var (decliner, declinerId) = await UserAsync(declinerPhone);
        var (revoked, _) = await UserAsync(revokedPhone);

        var declined = await InviteAsync(id, declinerPhone, shareCount: 2);
        (await AvailableAsync(id)).ShouldBe(7);
        await Ok(await decliner.PostAsync($"/api/me/invites/{declined}/decline", null));
        (await AvailableAsync(id)).ShouldBe(9);
        (await MyInviteIdsAsync(decliner)).ShouldBeEmpty();

        var revokedInvite = await InviteAsync(id, revokedPhone, shareCount: 1);
        (await MyInviteIdsAsync(revoked)).ShouldBe([revokedInvite]);
        await Ok(await _organizer.DeleteAsync($"/api/gameyas/{id}/invites/{revokedInvite}"));
        (await AvailableAsync(id)).ShouldBe(9);
        (await MyInviteIdsAsync(revoked)).ShouldBeEmpty();
        await ShouldFailWith(
            await AcceptAsync(revoked, revokedInvite, shares: 1), HttpStatusCode.NotFound, ErrorCodes.INVITE_NOT_FOUND);

        // An invite meant for someone else's phone is not found for this user either.
        var other = await InviteAsync(id, TestApi.NewPhone(), shareCount: 1);
        await ShouldFailWith(
            await decliner.PostAsync($"/api/me/invites/{other}/decline", null),
            HttpStatusCode.NotFound, ErrorCodes.INVITE_NOT_FOUND);
        await ShouldFailWith(
            await decliner.PostAsync($"/api/me/invites/{Guid.NewGuid()}/decline", null),
            HttpStatusCode.NotFound, ErrorCodes.INVITE_NOT_FOUND);

        await using var ctx = _db.CreateAppContext();
        var stored = await LoadAsync(ctx, id);
        var declinedRow = stored.PendingInvites.Single(i => i.Id == declined);
        declinedRow.Status.ShouldBe(InviteStatus.Declined);
        declinedRow.UserId.ShouldBe(declinerId);
        stored.PendingInvites.Single(i => i.Id == revokedInvite).Status.ShouldBe(InviteStatus.Revoked);
    }

    [Fact]
    public async Task Outsider_withdrawing_on_a_cancelled_gameya_gets_404()
    {
        var (id, _) = await RecruitingAsync();
        await Ok(await _organizer.PostAsJsonAsync($"/api/gameyas/{id}/cancel", new { reason = (string?)null }));
        var (stranger, _) = await UserAsync();

        await ShouldFailWith(
            await stranger.DeleteAsync($"/api/gameyas/{id}/join-requests/mine"),
            HttpStatusCode.NotFound, ErrorCodes.GAMEYA_NOT_FOUND);
        await ShouldFailWith(
            await stranger.PostAsync($"/api/gameyas/{id}/reaccept", null),
            HttpStatusCode.NotFound, ErrorCodes.GAMEYA_NOT_FOUND);
    }

    [Fact]
    public async Task Accepting_someone_elses_invite_is_404_INVITE_NOT_FOUND()
    {
        var (id, _) = await RecruitingAsync();
        var invitedPhone = TestApi.NewPhone();
        var (invited, _) = await UserAsync(invitedPhone);
        var (other, otherId) = await UserAsync();
        var inviteId = await InviteAsync(id, invitedPhone, shareCount: 1);

        await ShouldFailWith(
            await AcceptAsync(other, inviteId, shares: 1), HttpStatusCode.NotFound, ErrorCodes.INVITE_NOT_FOUND);

        await using (var ctx = _db.CreateAppContext())
        {
            var stored = await LoadAsync(ctx, id);
            stored.PendingInvites.Single(i => i.Id == inviteId).Status.ShouldBe(InviteStatus.Pending);
            stored.Memberships.ShouldNotContain(m => m.UserId == otherId);
        }

        // The intended invitee can still accept it.
        await Ok(await AcceptAsync(invited, inviteId, shares: 1));
    }

    [Fact]
    public async Task Leave_and_remove()
    {
        var (id, code) = await RecruitingAsync();
        var (leaver, leaverId) = await UserAsync();
        var (removed, removedId) = await UserAsync();
        await MemberAsync(id, code, leaver, leaverId);
        await MemberAsync(id, code, removed, removedId);

        await Ok(await leaver.PostAsync($"/api/gameyas/{id}/leave", null));
        await ShouldFailWith(
            await _organizer.PostAsync($"/api/gameyas/{id}/leave", null),
            HttpStatusCode.Conflict, ErrorCodes.ORGANIZER_CANNOT_LEAVE);

        await ShouldFailWith(
            await RemoveAsync(_organizer, id, removedId, "abc"), HttpStatusCode.BadRequest, ErrorCodes.VALIDATION_FAILED);
        // A member cannot remove anyone: the gam'eya is "not found" for organizer-only actions.
        await ShouldFailWith(
            await RemoveAsync(removed, id, removedId, "Leaving myself"), HttpStatusCode.NotFound, ErrorCodes.GAMEYA_NOT_FOUND);
        await Ok(await RemoveAsync(_organizer, id, removedId, "Did not pay"));

        // A former member is now an outsider.
        await ShouldFailWith(
            await leaver.PostAsync($"/api/gameyas/{id}/leave", null), HttpStatusCode.NotFound, ErrorCodes.GAMEYA_NOT_FOUND);

        await using var ctx = _db.CreateAppContext();
        var stored = await LoadAsync(ctx, id);
        stored.Memberships.Single(m => m.UserId == leaverId).Status.ShouldBe(MembershipStatus.Left);
        var removal = stored.Memberships.Single(m => m.UserId == removedId);
        removal.Status.ShouldBe(MembershipStatus.Removed);
        removal.RemovalReason.ShouldBe("Did not pay");
        stored.AvailableShares.ShouldBe(9);
    }

    [Fact]
    public async Task Account_deletion_blocked_while_in_a_Recruiting_gameya()
    {
        var (id, code) = await RecruitingAsync();
        var (member, memberId) = await UserAsync();
        await MemberAsync(id, code, member, memberId);

        await ShouldFailWith(
            await member.DeleteAsync("/api/me"), HttpStatusCode.Conflict, ErrorCodes.ACCOUNT_HAS_ACTIVE_GAMEYAS);
        // The organizer of a Recruiting gam'eya is blocked too.
        await ShouldFailWith(
            await _organizer.DeleteAsync("/api/me"), HttpStatusCode.Conflict, ErrorCodes.ACCOUNT_HAS_ACTIVE_GAMEYAS);

        await Ok(await member.PostAsync($"/api/gameyas/{id}/leave", null));
        await Ok(await member.DeleteAsync("/api/me"));
    }

    [Fact]
    public async Task Every_command_writes_its_activity_entry()
    {
        var (id, code) = await RecruitingAsync();
        var firstPhone = TestApi.NewPhone();
        var invitedPhone = TestApi.NewPhone();
        var revokedPhone = TestApi.NewPhone();
        var (first, firstId) = await UserAsync(firstPhone);
        var (second, secondId) = await UserAsync();
        var (third, thirdId) = await UserAsync();
        var (invited, _) = await UserAsync(invitedPhone);

        // Request, withdraw, request again, rejected; then invited and declined.
        await Ok(await JoinAsync(first, code));
        await Ok(await first.DeleteAsync($"/api/gameyas/{id}/join-requests/mine"));
        await Ok(await JoinAsync(first, code));
        await Ok(await _organizer.PostAsJsonAsync(
            $"/api/gameyas/{id}/join-requests/{await PendingRequestIdAsync(id, firstId)}/reject",
            new { reason = "Full family only" }));
        var declined = await InviteAsync(id, firstPhone, shareCount: 1);
        await Ok(await first.PostAsync($"/api/me/invites/{declined}/decline", null));

        // Approved, re-accepts changed terms, then leaves.
        await MemberAsync(id, code, second, secondId);
        await Ok(await _organizer.PutAsJsonAsync(
            $"/api/gameyas/{id}", GameyaTestApi.Body(GameyaTestApi.Today(), new { contributionAmount = 2000 })));
        await Ok(await second.PostAsync($"/api/gameyas/{id}/reaccept", null));
        await Ok(await second.PostAsync($"/api/gameyas/{id}/leave", null));

        // Approved, then removed.
        await MemberAsync(id, code, third, thirdId);
        await Ok(await RemoveAsync(_organizer, id, thirdId, "Did not pay"));

        // Invited and accepted; an invite to an unregistered phone revoked.
        await Ok(await AcceptAsync(invited, await InviteAsync(id, invitedPhone, shareCount: 1), shares: 1));
        await Ok(await _organizer.DeleteAsync(
            $"/api/gameyas/{id}/invites/{await InviteAsync(id, revokedPhone, shareCount: 1)}"));

        await using var ctx = _db.CreateAppContext();
        var entries = await ctx.ActivityLog.Where(a => a.GameyaId == id).ToListAsync();
        string[] expected =
        [
            "GAMEYA_CREATED", "GAMEYA_OPENED", "GAMEYA_SETTINGS_CHANGED", "JOIN_REQUESTED", "JOIN_REQUEST_WITHDRAWN",
            "JOIN_REQUEST_APPROVED", "JOIN_REQUEST_REJECTED", "MEMBER_INVITED", "INVITE_REVOKED", "INVITE_ACCEPTED",
            "INVITE_DECLINED", "TERMS_REACCEPTED", "MEMBER_LEFT", "MEMBER_REMOVED",
        ];
        entries.Select(e => e.Action).Distinct().ShouldBe(expected, ignoreOrder: true);

        entries.Single(e => e.Action == "JOIN_REQUEST_REJECTED").Note.ShouldBe("Full family only");
        entries.Single(e => e.Action == "MEMBER_REMOVED").Note.ShouldBe("Did not pay");
        entries.Single(e => e.Action == "TERMS_REACCEPTED").ActorUserId.ShouldBe(secondId);

        // Activity details never carry a phone number, in any of its forms.
        foreach (var phone in new[] { firstPhone, invitedPhone, revokedPhone })
        {
            var parsed = PhoneNumber.Parse(phone);
            foreach (var form in new[] { phone, parsed.E164, phone[1..] })
            {
                entries.ShouldAllBe(e => !e.DetailsJson.Contains(form) && (e.Note == null || !e.Note.Contains(form)));
            }
        }
    }
}
