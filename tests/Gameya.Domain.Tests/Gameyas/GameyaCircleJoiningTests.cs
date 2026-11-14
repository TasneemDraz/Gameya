using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using Gameya.Domain.Users;
using Shouldly;

namespace Gameya.Domain.Tests.Gameyas;

public class GameyaCircleJoiningTests
{
    private static readonly DateTimeOffset Now = GameyaTestData.Now;
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid U = Guid.NewGuid();
    private static readonly PhoneNumber P = PhoneNumber.Parse("01012345678");

    private static GameyaCircle Recruiting(GameyaSettings? settings = null)
    {
        var g = GameyaCircle.Create(
            Guid.NewGuid(), Org, settings ?? GameyaTestData.Settings(), 1, GameyaTestData.Today, Now);
        g.OpenForRecruiting("ABC234", GameyaTestData.Today);
        return g;
    }

    private static DomainException Thrown(Action act, string code) => GameyaTestData.Thrown(act, code);

    private static ShareRequest Share(params int[] prefs) => new(prefs);

    private static PhoneNumber Phone(int n) => PhoneNumber.Parse($"0101000{n:0000}");

    private static void FieldFails(Action act, string field)
    {
        var ex = Thrown(act, ErrorCodes.VALIDATION_FAILED);
        ex.Params.ShouldNotBeNull();
        ex.Params["field"].ShouldBe(field);
    }

    [Fact]
    public void Request_then_approve_creates_membership_and_reduces_available()
    {
        var g = Recruiting();

        var r = g.RequestToJoin(U, [Share(2), Share()]);

        r.Status.ShouldBe(JoinRequestStatus.Pending);
        r.AcceptedTermsVersion.ShouldBe(1);
        r.UserId.ShouldBe(U);
        r.GameyaId.ShouldBe(g.Id);
        g.JoinRequests.ShouldHaveSingleItem().ShouldBe(r);
        g.AvailableShares.ShouldBe(9);
        var requested = g.DomainEvents.OfType<JoinRequested>().ShouldHaveSingleItem();
        requested.TargetType.ShouldBe("JoinRequest");
        requested.TargetId.ShouldBe(r.Id.ToString());

        var m = g.ApproveJoinRequest(r.Id, Now);

        m.Status.ShouldBe(MembershipStatus.Active);
        m.UserId.ShouldBe(U);
        m.Shares.Count.ShouldBe(2);
        m.Shares[0].Preferences.ShouldBe([2]);
        m.Shares[1].Preferences.ShouldBeEmpty();
        g.Memberships.ShouldContain(m);
        r.Status.ShouldBe(JoinRequestStatus.Approved);
        g.AvailableShares.ShouldBe(7);
        g.DomainEvents.OfType<JoinRequestApproved>().ShouldHaveSingleItem().TargetId.ShouldBe(r.Id.ToString());
    }

    [Fact]
    public void Approve_more_than_available_fails_NOT_ENOUGH_SHARES_with_available_count()
    {
        var g = Recruiting();
        var r = g.RequestToJoin(U, [Share(), Share()]);
        for (var i = 0; i < 4; i++)
            g.InviteByPhone(Phone(i), null, 2);
        g.AvailableShares.ShouldBe(1);

        var ex = Thrown(() => g.ApproveJoinRequest(r.Id, Now), ErrorCodes.NOT_ENOUGH_SHARES);

        ex.Params.ShouldNotBeNull();
        ex.Params["available"].ShouldBe(1);
        r.Status.ShouldBe(JoinRequestStatus.Pending);
    }

    [Fact]
    public void Request_rules()
    {
        var g = Recruiting();
        Thrown(() => g.RequestToJoin(Org, [Share()]), ErrorCodes.ALREADY_MEMBER);

        g.ApproveJoinRequest(g.RequestToJoin(U, [Share()]).Id, Now);
        Thrown(() => g.RequestToJoin(U, [Share()]), ErrorCodes.ALREADY_MEMBER);

        var other = Guid.NewGuid();
        g.RequestToJoin(other, [Share()]);
        Thrown(() => g.RequestToJoin(other, [Share()]), ErrorCodes.REQUEST_ALREADY_PENDING);

        var third = Guid.NewGuid();
        Thrown(() => g.RequestToJoin(third, [Share(), Share(), Share()]), ErrorCodes.MAX_SHARES_EXCEEDED);
        FieldFails(() => g.RequestToJoin(third, []), "shareCount");

        var draft = GameyaCircle.Create(
            Guid.NewGuid(), Org, GameyaTestData.Settings(), 1, GameyaTestData.Today, Now);
        Thrown(() => draft.RequestToJoin(third, [Share()]), ErrorCodes.INVALID_STATUS_TRANSITION);
    }

    [Fact]
    public void Request_when_full_or_short_of_shares()
    {
        var full = Recruiting();
        for (var i = 0; i < 4; i++)
            full.InviteByPhone(Phone(i), null, 2);
        full.InviteByPhone(Phone(9), null, 1);
        full.AvailableShares.ShouldBe(0);
        Thrown(() => full.RequestToJoin(U, [Share()]), ErrorCodes.GAMEYA_FULL);

        var short_ = Recruiting();
        for (var i = 0; i < 4; i++)
            short_.InviteByPhone(Phone(i), null, 2);
        short_.AvailableShares.ShouldBe(1);
        var ex = Thrown(() => short_.RequestToJoin(U, [Share(), Share()]), ErrorCodes.NOT_ENOUGH_SHARES);
        ex.Params.ShouldNotBeNull();
        ex.Params["available"].ShouldBe(1);
    }

    [Theory]
    [InlineData(OrderMethod.Manual, new[] { 1 })]
    [InlineData(OrderMethod.ByRequest, new[] { 1, 2, 3, 4 })]
    [InlineData(OrderMethod.ByRequest, new[] { 2, 2 })]
    [InlineData(OrderMethod.ByRequest, new[] { 0 })]
    [InlineData(OrderMethod.ByRequest, new[] { 11 })]
    public void Preference_validation(OrderMethod method, int[] prefs)
    {
        var g = Recruiting(GameyaTestData.Settings() with { OrderMethod = method });

        Thrown(() => g.RequestToJoin(U, [Share(prefs)]), ErrorCodes.INVALID_PREFERENCES);
    }

    [Fact]
    public void Valid_preferences_are_accepted_under_ByRequest()
    {
        var g = Recruiting();

        g.RequestToJoin(U, [Share(1, 5, 10)]).Shares.ShouldHaveSingleItem().Preferences.ShouldBe([1, 5, 10]);
    }

    [Fact]
    public void Withdraw_only_own_pending_request()
    {
        var g = Recruiting();
        var r = g.RequestToJoin(U, [Share()]);

        Thrown(() => g.WithdrawJoinRequest(Guid.NewGuid()), ErrorCodes.GAMEYA_NOT_FOUND);
        g.WithdrawJoinRequest(U);

        r.Status.ShouldBe(JoinRequestStatus.Withdrawn);
        g.DomainEvents.OfType<JoinRequestWithdrawn>().ShouldHaveSingleItem().TargetId.ShouldBe(r.Id.ToString());
        Thrown(() => g.WithdrawJoinRequest(U), ErrorCodes.GAMEYA_NOT_FOUND);
    }

    [Fact]
    public void Reject_sets_reason_and_frees_nothing()
    {
        var g = Recruiting();
        var r = g.RequestToJoin(U, [Share()]);

        FieldFails(() => g.RejectJoinRequest(r.Id, new string('x', 501)), "reason");
        r.Status.ShouldBe(JoinRequestStatus.Pending);

        g.RejectJoinRequest(r.Id, "Not a fit");

        r.Status.ShouldBe(JoinRequestStatus.Rejected);
        r.RejectReason.ShouldBe("Not a fit");
        g.AvailableShares.ShouldBe(9);
        g.DomainEvents.OfType<JoinRequestRejected>().ShouldHaveSingleItem().Note.ShouldBe("Not a fit");
        Thrown(() => g.RejectJoinRequest(r.Id, null), ErrorCodes.GAMEYA_NOT_FOUND);
        Thrown(() => g.ApproveJoinRequest(Guid.NewGuid(), Now), ErrorCodes.GAMEYA_NOT_FOUND);
    }

    [Fact]
    public void Invite_reserves_shares_and_accept_takes_them()
    {
        var g = Recruiting();

        var invite = g.InviteByPhone(P, null, 2);

        invite.Status.ShouldBe(InviteStatus.Pending);
        g.PendingInvites.ShouldHaveSingleItem().ShouldBe(invite);
        g.ReservedShares.ShouldBe(2);
        g.AvailableShares.ShouldBe(7);
        var invited = g.DomainEvents.OfType<MemberInvited>().ShouldHaveSingleItem();
        invited.TargetType.ShouldBe("PendingInvite");
        invited.TargetId.ShouldBe(invite.Id.ToString());

        var m = g.AcceptInvite(invite.Id, U, P, [Share(1), Share()], Now);

        m.Status.ShouldBe(MembershipStatus.Active);
        m.UserId.ShouldBe(U);
        m.Shares.Count.ShouldBe(2);
        m.Shares[0].Preferences.ShouldBe([1]);
        m.AcceptedTermsVersion.ShouldBe(1);
        g.Memberships.ShouldContain(m);
        invite.Status.ShouldBe(InviteStatus.Accepted);
        invite.UserId.ShouldBe(U);
        g.ReservedShares.ShouldBe(0);
        g.AssignedShares.ShouldBe(3);
        g.AvailableShares.ShouldBe(7);
        g.DomainEvents.OfType<InviteAccepted>().ShouldHaveSingleItem().TargetId.ShouldBe(invite.Id.ToString());
    }

    [Fact]
    public void Invite_rules()
    {
        var g = Recruiting();
        var inviteId = g.InviteByPhone(P, null, 1).Id;
        Thrown(() => g.InviteByPhone(P, null, 1), ErrorCodes.REQUEST_ALREADY_PENDING);

        Thrown(() => g.InviteByPhone(Phone(1), Org, 1), ErrorCodes.ALREADY_MEMBER);
        g.ApproveJoinRequest(g.RequestToJoin(U, [Share()]).Id, Now);
        Thrown(() => g.InviteByPhone(Phone(1), U, 1), ErrorCodes.ALREADY_MEMBER);

        Thrown(() => g.InviteByPhone(Phone(1), null, 3), ErrorCodes.MAX_SHARES_EXCEEDED);
        FieldFails(() => g.InviteByPhone(Phone(1), null, 0), "shareCount");
        FieldFails(() => g.InviteByPhone(Phone(1), null, -1), "shareCount");

        for (var i = 2; i < 5; i++)
            g.InviteByPhone(Phone(i), null, 2);
        g.AvailableShares.ShouldBe(1);
        Thrown(() => g.InviteByPhone(Phone(1), null, 2), ErrorCodes.NOT_ENOUGH_SHARES);

        g.RevokeInvite(inviteId);
        g.InviteByPhone(P, null, 1).Status.ShouldBe(InviteStatus.Pending);
    }

    [Fact]
    public void Accept_or_decline_with_other_phone_fails_INVITE_NOT_FOUND()
    {
        var g = Recruiting();
        var invite = g.InviteByPhone(P, null, 1);
        var other = Phone(1);

        Thrown(() => g.AcceptInvite(invite.Id, U, other, [Share()], Now), ErrorCodes.INVITE_NOT_FOUND);
        Thrown(() => g.DeclineInvite(invite.Id, U, other), ErrorCodes.INVITE_NOT_FOUND);
        Thrown(() => g.AcceptInvite(Guid.NewGuid(), U, P, [Share()], Now), ErrorCodes.INVITE_NOT_FOUND);

        g.RevokeInvite(invite.Id);
        Thrown(() => g.AcceptInvite(invite.Id, U, P, [Share()], Now), ErrorCodes.INVITE_NOT_FOUND);
        Thrown(() => g.DeclineInvite(invite.Id, U, P), ErrorCodes.INVITE_NOT_FOUND);
        Thrown(() => g.RevokeInvite(invite.Id), ErrorCodes.INVITE_NOT_FOUND);
    }

    [Fact]
    public void Accept_requires_one_ShareRequest_per_invited_share()
    {
        var g = Recruiting();
        var invite = g.InviteByPhone(P, null, 2);

        Thrown(() => g.AcceptInvite(invite.Id, U, P, [Share()], Now), ErrorCodes.INVALID_PREFERENCES);

        invite.Status.ShouldBe(InviteStatus.Pending);
    }

    [Fact]
    public void Accepting_an_invite_withdraws_the_users_pending_request()
    {
        var g = Recruiting();
        var r = g.RequestToJoin(U, [Share()]);
        var invite = g.InviteByPhone(P, null, 1);

        g.AcceptInvite(invite.Id, U, P, [Share()], Now);

        r.Status.ShouldBe(JoinRequestStatus.Withdrawn);
        g.Memberships.Count(m => m.UserId == U && m.Status == MembershipStatus.Active).ShouldBe(1);
        Thrown(() => g.ApproveJoinRequest(r.Id, Now), ErrorCodes.GAMEYA_NOT_FOUND);
        g.Memberships.Count(m => m.UserId == U).ShouldBe(1);
    }

    public static TheoryData<string> JoiningOperations() =>
    [
        "RequestToJoin", "WithdrawJoinRequest", "ApproveJoinRequest", "RejectJoinRequest",
        "InviteByPhone", "RevokeInvite", "AcceptInvite", "DeclineInvite",
    ];

    [Theory]
    [MemberData(nameof(JoiningOperations))]
    public void Every_joining_operation_requires_Recruiting(string operation)
    {
        var draft = GameyaCircle.Create(
            Guid.NewGuid(), Org, GameyaTestData.Settings(), 1, GameyaTestData.Today, Now);
        var id = Guid.NewGuid();
        Action act = operation switch
        {
            "RequestToJoin" => () => draft.RequestToJoin(U, [Share()]),
            "WithdrawJoinRequest" => () => draft.WithdrawJoinRequest(U),
            "ApproveJoinRequest" => () => draft.ApproveJoinRequest(id, Now),
            "RejectJoinRequest" => () => draft.RejectJoinRequest(id, null),
            "InviteByPhone" => () => draft.InviteByPhone(P, null, 1),
            "RevokeInvite" => () => draft.RevokeInvite(id),
            "AcceptInvite" => () => draft.AcceptInvite(id, U, P, [Share()], Now),
            "DeclineInvite" => () => draft.DeclineInvite(id, U, P),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        Thrown(act, ErrorCodes.INVALID_STATUS_TRANSITION);
    }

    [Fact]
    public void Decline_and_revoke_release_reserved_shares()
    {
        var g = Recruiting();
        var a = g.InviteByPhone(P, null, 2);
        var b = g.InviteByPhone(Phone(1), null, 1);
        g.AvailableShares.ShouldBe(6);

        g.DeclineInvite(a.Id, U, P);
        g.RevokeInvite(b.Id);

        a.Status.ShouldBe(InviteStatus.Declined);
        b.Status.ShouldBe(InviteStatus.Revoked);
        g.AvailableShares.ShouldBe(9);
        g.DomainEvents.OfType<InviteDeclined>().ShouldHaveSingleItem();
        g.DomainEvents.OfType<InviteRevoked>().ShouldHaveSingleItem();
    }
}
