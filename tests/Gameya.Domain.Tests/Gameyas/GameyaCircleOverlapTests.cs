using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using Gameya.Domain.Users;
using Shouldly;

namespace Gameya.Domain.Tests.Gameyas;

/// <summary>
/// A person who both asked to join and was invited, a requester whose account is deleted, and reason cleanup.
/// </summary>
public class GameyaCircleOverlapTests
{
    private static readonly DateTimeOffset Now = GameyaTestData.Now;
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid U = Guid.NewGuid();
    private static readonly PhoneNumber P = PhoneNumber.Parse("01012345678");

    private static GameyaCircle Recruiting()
    {
        var g = GameyaCircle.Create(Guid.NewGuid(), Org, GameyaTestData.Settings(), 1, GameyaTestData.Today, Now);
        g.OpenForRecruiting("ABC234", GameyaTestData.Today);
        return g;
    }

    private static ShareRequest Share() => new([]);

    private static DomainException Thrown(Action act, string code) => GameyaTestData.Thrown(act, code);

    [Fact]
    public void Approving_a_request_revokes_the_same_users_pending_invite_by_user_id()
    {
        var g = Recruiting();
        var invite = g.InviteByPhone(P, U, 2);
        var r = g.RequestToJoin(U, [Share()]);
        g.ClearDomainEvents();

        g.ApproveJoinRequest(r.Id, Now, P);

        invite.Status.ShouldBe(InviteStatus.Revoked);
        g.ReservedShares.ShouldBe(0);
        g.AvailableShares.ShouldBe(8);
        g.DomainEvents.OfType<InviteRevoked>().ShouldHaveSingleItem().InviteId.ShouldBe(invite.Id);
    }

    [Fact]
    public void Approving_a_request_revokes_an_invite_sent_to_the_requesters_phone_before_signup()
    {
        var g = Recruiting();
        var invite = g.InviteByPhone(P, null, 1);
        var other = g.InviteByPhone(PhoneNumber.Parse("01099999999"), null, 1);
        var r = g.RequestToJoin(U, [Share()]);

        g.ApproveJoinRequest(r.Id, Now, P);

        invite.Status.ShouldBe(InviteStatus.Revoked);
        other.Status.ShouldBe(InviteStatus.Pending);
    }

    [Fact]
    public void The_requesters_own_invite_counts_as_free_capacity_when_approving()
    {
        var g = Recruiting();
        for (var i = 0; i < 3; i++)
            g.InviteByPhone(PhoneNumber.Parse($"0101000{i:0000}"), null, 2);
        var r = g.RequestToJoin(U, [Share(), Share()]);
        // Sent to the phone before its owner signed up, so it carries no user id.
        g.InviteByPhone(P, null, 2);
        g.AvailableShares.ShouldBe(1);

        g.ApproveJoinRequest(r.Id, Now, P).Shares.Count.ShouldBe(2);

        g.AvailableShares.ShouldBe(1);
    }

    [Fact]
    public void A_failed_approval_leaves_the_invite_pending()
    {
        var g = Recruiting();
        var invite = g.InviteByPhone(P, U, 1);
        var r = g.RequestToJoin(U, [Share(), Share()]);
        for (var i = 0; i < 3; i++)
            g.InviteByPhone(PhoneNumber.Parse($"0101000{i:0000}"), null, 2);
        // Nothing is free now; U's own invite releases only one of the two shares asked for.
        g.InviteByPhone(PhoneNumber.Parse("01010009999"), null, 2);

        Thrown(() => g.ApproveJoinRequest(r.Id, Now, P), ErrorCodes.NOT_ENOUGH_SHARES);

        invite.Status.ShouldBe(InviteStatus.Pending);
        r.Status.ShouldBe(JoinRequestStatus.Pending);
    }

    [Fact]
    public void Inviting_a_user_who_already_asked_to_join_is_refused()
    {
        var g = Recruiting();
        g.RequestToJoin(U, [Share()]);

        Thrown(() => g.InviteByPhone(P, U, 1), ErrorCodes.INVITEE_REQUEST_PENDING);

        g.PendingInvites.ShouldBeEmpty();
    }

    [Fact]
    public void MEMBER_INVITED_does_not_say_whether_the_phone_has_an_account()
    {
        var g = Recruiting();
        g.InviteByPhone(P, U, 1);

        var invited = g.DomainEvents.OfType<MemberInvited>().ShouldHaveSingleItem();
        var details = invited.Details.ShouldNotBeNull();
        details.GetType().GetProperties().Select(p => p.Name).ShouldBe(["inviteId", "shareCount"], ignoreOrder: true);
    }

    [Fact]
    public void INVITE_DECLINED_details_carry_no_user_id()
    {
        var g = Recruiting();
        var invite = g.InviteByPhone(P, null, 1);
        g.DeclineInvite(invite.Id, U, P);

        var declined = g.DomainEvents.OfType<InviteDeclined>().ShouldHaveSingleItem();
        declined.Details.ShouldNotBeNull().GetType().GetProperties().Select(p => p.Name).ShouldBe(["inviteId"]);
    }

    [Fact]
    public void Approving_the_request_of_a_deleted_account_is_refused()
    {
        var g = Recruiting();
        var r = g.RequestToJoin(U, [Share()]);

        Thrown(() => g.ApproveJoinRequest(r.Id, Now, null, requesterDeleted: true), ErrorCodes.REQUESTER_ACCOUNT_DELETED);

        r.Status.ShouldBe(JoinRequestStatus.Pending);
        g.Memberships.ShouldNotContain(m => m.UserId == U);
    }

    [Fact]
    public void Releasing_a_deleted_account_withdraws_its_requests_and_declines_its_invites()
    {
        var g = Recruiting();
        var r = g.RequestToJoin(U, [Share()]);
        var byPhone = g.InviteByPhone(P, null, 1);
        var other = g.InviteByPhone(PhoneNumber.Parse("01099999999"), null, 1);
        g.ClearDomainEvents();

        g.ReleaseDeletedAccount(U, P);

        r.Status.ShouldBe(JoinRequestStatus.Withdrawn);
        byPhone.Status.ShouldBe(InviteStatus.Declined);
        other.Status.ShouldBe(InviteStatus.Pending);
        g.DomainEvents.OfType<JoinRequestWithdrawn>().ShouldHaveSingleItem().RequestId.ShouldBe(r.Id);
        g.DomainEvents.OfType<InviteDeclined>().ShouldHaveSingleItem().InviteId.ShouldBe(byPhone.Id);
    }

    [Fact]
    public void Releasing_a_deleted_account_matches_an_invite_by_user_id()
    {
        var g = Recruiting();
        var invite = g.InviteByPhone(P, U, 1);

        g.ReleaseDeletedAccount(U, null);

        invite.Status.ShouldBe(InviteStatus.Declined);
    }

    [Theory]
    [InlineData("   ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("  Not a fit  ", "Not a fit")]
    public void Reject_and_cancel_reasons_are_trimmed_and_blank_becomes_null(string? raw, string? stored)
    {
        var g = Recruiting();
        var r = g.RequestToJoin(U, [Share()]);

        g.RejectJoinRequest(r.Id, raw);
        g.Cancel(raw, Now);

        r.RejectReason.ShouldBe(stored);
        g.DomainEvents.OfType<JoinRequestRejected>().ShouldHaveSingleItem().Note.ShouldBe(stored);
        g.CancelReason.ShouldBe(stored);
        g.DomainEvents.OfType<GameyaCancelled>().ShouldHaveSingleItem().Note.ShouldBe(stored);
    }

    [Fact]
    public void Reason_length_is_measured_after_trimming()
    {
        var g = Recruiting();
        var r = g.RequestToJoin(U, [Share()]);
        var padded = "  " + new string('x', 500) + "  ";

        g.RejectJoinRequest(r.Id, padded);
        g.Cancel(padded, Now);

        r.RejectReason.ShouldBe(new string('x', 500));
        g.CancelReason.ShouldBe(new string('x', 500));
    }
}
