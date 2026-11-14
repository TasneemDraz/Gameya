using System.Text.Json;
using System.Text.Json.Nodes;
using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using Gameya.Domain.Users;
using Shouldly;

namespace Gameya.Domain.Tests.Gameyas;

public class GameyaCircleLifecycleTests
{
    private static readonly DateTimeOffset Now = GameyaTestData.Now;
    private static readonly DateOnly Today = GameyaTestData.Today;
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid U = Guid.NewGuid();

    private static GameyaCircle Draft(GameyaSettings? settings = null, int organizerShares = 1) =>
        GameyaCircle.Create(Guid.NewGuid(), Org, settings ?? GameyaTestData.Settings(), organizerShares, Today, Now);

    private static GameyaCircle Recruiting(GameyaSettings? settings = null, int organizerShares = 1)
    {
        var g = Draft(settings, organizerShares);
        g.OpenForRecruiting("ABC234", Today);
        return g;
    }

    private static DomainException Thrown(Action act, string code) => GameyaTestData.Thrown(act, code);

    private static ShareRequest Share(params int[] prefs) => new(prefs);

    private static Membership Join(GameyaCircle g, Guid userId, params ShareRequest[] shares)
    {
        var r = g.RequestToJoin(userId, shares);
        return g.ApproveJoinRequest(r.Id, Now);
    }

    private static JsonNode Details(GameyaCircle g)
    {
        var e = g.DomainEvents.OfType<GameyaSettingsChanged>().ShouldHaveSingleItem();
        return JsonSerializer.SerializeToNode(e.Details)!;
    }

    private static Membership OrganizerMembership(GameyaCircle g) =>
        g.Memberships.Single(m => m.UserId == Org && m.Status == MembershipStatus.Active);

    [Theory]
    [InlineData("ContributionAmount")]
    [InlineData("ShareCount")]
    [InlineData("Frequency")]
    public void Money_fields_bump_terms_while_Recruiting_and_organizer_auto_reaccepts(string field)
    {
        var g = Recruiting();
        Join(g, U, Share());
        var s = GameyaTestData.Settings();
        var changed = field switch
        {
            "ContributionAmount" => s with { ContributionAmount = 1_500m },
            "ShareCount" => s with { ShareCount = 12 },
            _ => s with { Frequency = GameyaFrequency.Weekly },
        };

        g.UpdateSettings(changed, 1, Today, Now);

        g.TermsVersion.ShouldBe(2);
        g.MembershipNeedsReaccept(U).ShouldBeTrue();
        g.MembershipNeedsReaccept(Org).ShouldBeFalse();
        Details(g)["termsVersion"]!.GetValue<int>().ShouldBe(2);
        var change = Details(g)["changed"]![char.ToLowerInvariant(field[0]) + field[1..]]!;
        change["old"].ShouldNotBeNull();
        change["new"].ShouldNotBeNull();
    }

    [Fact]
    public void Other_fields_and_Draft_changes_never_bump_terms()
    {
        var g = Recruiting();
        g.UpdateSettings(GameyaTestData.Settings() with { Name = "Renamed circle" }, 1, Today, Now);
        g.TermsVersion.ShouldBe(1);
        Details(g).AsObject().ContainsKey("termsVersion").ShouldBeFalse();

        var d = Draft();
        d.UpdateSettings(GameyaTestData.Settings() with { ContributionAmount = 2_000m }, 1, Today, Now);
        d.TermsVersion.ShouldBe(1);
    }

    [Fact]
    public void Unchanged_settings_raise_no_event()
    {
        var g = Recruiting();
        g.UpdateSettings(GameyaTestData.Settings(), 1, Today, Now);
        g.DomainEvents.OfType<GameyaSettingsChanged>().ShouldBeEmpty();
    }

    [Fact]
    public void Changing_amount_logs_old_and_new_amount()
    {
        var g = Recruiting();

        g.UpdateSettings(GameyaTestData.Settings() with { ContributionAmount = 1_500m }, 1, Today, Now);

        var change = Details(g)["changed"]!["contributionAmount"]!;
        change["old"]!.GetValue<decimal>().ShouldBe(1000m);
        change["new"]!.GetValue<decimal>().ShouldBe(1500m);
        g.ContributionAmount.ShouldBe(1500m);
    }

    [Fact]
    public void Enums_and_dates_are_logged_as_strings()
    {
        var g = Recruiting();
        var s = GameyaTestData.Settings();

        g.UpdateSettings(s with { Frequency = GameyaFrequency.Weekly, StartDate = s.StartDate.AddDays(1) }, 1, Today, Now);

        var changed = Details(g)["changed"]!;
        changed["frequency"]!["old"]!.GetValue<string>().ShouldBe("Monthly");
        changed["frequency"]!["new"]!.GetValue<string>().ShouldBe("Weekly");
        changed["startDate"]!["new"]!.GetValue<string>().ShouldBe(s.StartDate.AddDays(1).ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void Share_count_cannot_go_below_assigned_plus_reserved()
    {
        var g = Recruiting();
        Join(g, U, Share(), Share());
        g.InviteByPhone(PhoneNumber.Parse("01012345678"), null, 2);
        g.AssignedShares.ShouldBe(3);
        g.ReservedShares.ShouldBe(2);

        Thrown(() => g.UpdateSettings(GameyaTestData.Settings() with { ShareCount = 4 }, 1, Today, Now),
            ErrorCodes.SHARES_BELOW_ASSIGNED);
        g.UpdateSettings(GameyaTestData.Settings() with { ShareCount = 5 }, 1, Today, Now);
        g.ShareCount.ShouldBe(5);
    }

    [Fact]
    public void Lowering_share_count_trims_out_of_range_preferences()
    {
        var g = Recruiting();
        var m = Join(g, U, Share(8, 2));
        var other = Guid.NewGuid();
        var r = g.RequestToJoin(other, [Share(9)]);

        g.UpdateSettings(GameyaTestData.Settings() with { ShareCount = 7 }, 1, Today, Now);

        m.Shares[0].Preferences.ShouldBe([2]);
        r.Shares[0].Preferences.ShouldBeEmpty();
    }

    [Fact]
    public void Unchanged_past_start_date_is_allowed_but_a_changed_one_must_be_today_or_later()
    {
        var s = GameyaTestData.Settings() with { StartDate = Today };
        var g = Recruiting(s);
        var later = Today.AddDays(3);

        g.UpdateSettings(s with { Name = "Renamed circle" }, 1, later, Now);
        g.Name.ShouldBe("Renamed circle");

        Thrown(() => g.UpdateSettings(s with { StartDate = Today.AddDays(1) }, 1, later, Now),
            ErrorCodes.START_DATE_IN_PAST);
    }

    [Fact]
    public void Toggling_participation_adds_or_ends_the_organizer_membership()
    {
        var g = Recruiting();
        var original = OrganizerMembership(g);

        g.UpdateSettings(GameyaTestData.Settings() with { OrganizerParticipates = false }, 0, Today, Now);

        original.Status.ShouldBe(MembershipStatus.Left);
        g.AvailableShares.ShouldBe(10);
        g.OrganizerParticipates.ShouldBeFalse();

        g.UpdateSettings(GameyaTestData.Settings(), 1, Today, Now);

        var again = OrganizerMembership(g);
        again.ShouldNotBe(original);
        again.AcceptedTermsVersion.ShouldBe(g.TermsVersion);
        g.AvailableShares.ShouldBe(9);
    }

    [Fact]
    public void Changing_organizer_shares_adds_or_removes_member_shares()
    {
        var g = Recruiting();

        g.UpdateSettings(GameyaTestData.Settings(), 2, Today, Now);
        OrganizerMembership(g).Shares.Count.ShouldBe(2);
        Details(g)["changed"]!["organizerShares"]!["new"]!.GetValue<int>().ShouldBe(2);

        g.ClearDomainEvents();
        g.UpdateSettings(GameyaTestData.Settings(), 1, Today, Now);
        OrganizerMembership(g).Shares.Count.ShouldBe(1);

        var four = GameyaTestData.Settings() with { ShareCount = 4 };
        var full = Recruiting(four);
        Join(full, U, Share(), Share());
        Join(full, Guid.NewGuid(), Share());
        Thrown(() => full.UpdateSettings(four, 2, Today, Now), ErrorCodes.NOT_ENOUGH_SHARES);
    }

    [Fact]
    public void Update_settings_is_rejected_after_cancel()
    {
        var g = Recruiting();
        g.Cancel(null, Now);
        Thrown(() => g.UpdateSettings(GameyaTestData.Settings() with { Name = "Renamed circle" }, 1, Today, Now),
            ErrorCodes.INVALID_STATUS_TRANSITION);
    }

    [Fact]
    public void Approve_on_stale_terms_fails_TERMS_NOT_ACCEPTED_until_requester_reaccepts()
    {
        var g = Recruiting();
        var r = g.RequestToJoin(U, [Share()]);
        g.UpdateSettings(GameyaTestData.Settings() with { ContributionAmount = 1_500m }, 1, Today, Now);

        Thrown(() => g.ApproveJoinRequest(r.Id, Now), ErrorCodes.TERMS_NOT_ACCEPTED);

        g.ReacceptTerms(U);

        r.AcceptedTermsVersion.ShouldBe(2);
        g.ApproveJoinRequest(r.Id, Now).Status.ShouldBe(MembershipStatus.Active);
        var e = g.DomainEvents.OfType<TermsReaccepted>().ShouldHaveSingleItem();
        e.TargetType.ShouldBe("JoinRequest");
    }

    [Fact]
    public void Reaccept_clears_flag()
    {
        var g = Recruiting();
        var m = Join(g, U, Share());
        g.UpdateSettings(GameyaTestData.Settings() with { ShareCount = 12 }, 1, Today, Now);
        g.MembershipNeedsReaccept(U).ShouldBeTrue();

        g.ReacceptTerms(U);

        g.MembershipNeedsReaccept(U).ShouldBeFalse();
        m.AcceptedTermsVersion.ShouldBe(2);
        var e = g.DomainEvents.OfType<TermsReaccepted>().ShouldHaveSingleItem();
        e.TargetType.ShouldBe("Membership");
        e.TargetId.ShouldBe(m.Id.ToString());

        Thrown(() => g.ReacceptTerms(Guid.NewGuid()), ErrorCodes.GAMEYA_NOT_FOUND);
    }

    [Fact]
    public void Organizer_cannot_leave_or_be_removed()
    {
        var g = Recruiting();
        Thrown(() => g.Leave(Org), ErrorCodes.ORGANIZER_CANNOT_LEAVE);
        Thrown(() => g.RemoveMember(Org, "valid reason"), ErrorCodes.ORGANIZER_CANNOT_LEAVE);
    }

    [Fact]
    public void Leave_and_remove_free_shares_and_set_status()
    {
        var g = Recruiting();
        var a = Join(g, U, Share(), Share());
        var other = Guid.NewGuid();
        var b = Join(g, other, Share());
        g.AvailableShares.ShouldBe(6);

        g.Leave(U);
        a.Status.ShouldBe(MembershipStatus.Left);
        g.AvailableShares.ShouldBe(8);
        g.DomainEvents.OfType<MemberLeft>().ShouldHaveSingleItem().TargetType.ShouldBe("Membership");

        Thrown(() => g.RemoveMember(other, "abcd"), ErrorCodes.VALIDATION_FAILED);
        Thrown(() => g.RemoveMember(other, new string('x', 501)), ErrorCodes.VALIDATION_FAILED);
        g.RemoveMember(other, "abcde");
        b.Status.ShouldBe(MembershipStatus.Removed);
        b.RemovalReason.ShouldBe("abcde");
        g.AvailableShares.ShouldBe(9);
        var removed = g.DomainEvents.OfType<MemberRemoved>().ShouldHaveSingleItem();
        removed.Note.ShouldBe("abcde");
        JsonSerializer.SerializeToNode(removed.Details)!["userId"]!.GetValue<Guid>().ShouldBe(other);

        Thrown(() => g.Leave(U), ErrorCodes.GAMEYA_NOT_FOUND);
        Thrown(() => g.RemoveMember(Guid.NewGuid(), "valid reason"), ErrorCodes.GAMEYA_NOT_FOUND);
    }

    [Fact]
    public void Leave_remove_and_reaccept_need_Recruiting()
    {
        var g = Draft();
        Thrown(() => g.Leave(U), ErrorCodes.INVALID_STATUS_TRANSITION);
        Thrown(() => g.RemoveMember(U, "valid reason"), ErrorCodes.INVALID_STATUS_TRANSITION);
        Thrown(() => g.ReacceptTerms(U), ErrorCodes.INVALID_STATUS_TRANSITION);
    }

    [Fact]
    public void Cancel_rejects_requests_revokes_invites_and_keeps_the_code()
    {
        var g = Recruiting();
        var request = g.RequestToJoin(U, [Share()]);
        var invite = g.InviteByPhone(PhoneNumber.Parse("01012345678"), null, 1);

        g.Cancel("changed plans", Now);

        g.Status.ShouldBe(GameyaStatus.Cancelled);
        g.CancelledAt.ShouldBe(Now);
        g.CancelReason.ShouldBe("changed plans");
        g.InviteCode.ShouldBe("ABC234");
        request.Status.ShouldBe(JoinRequestStatus.Rejected);
        invite.Status.ShouldBe(InviteStatus.Revoked);
        g.DomainEvents.OfType<GameyaCancelled>().ShouldHaveSingleItem().Note.ShouldBe("changed plans");

        Thrown(() => g.Cancel(null, Now), ErrorCodes.INVALID_STATUS_TRANSITION);
        Thrown(() => g.Leave(U), ErrorCodes.INVALID_STATUS_TRANSITION);
    }

    [Fact]
    public void Cancel_works_from_Draft_and_validates_reason_length()
    {
        var g = Draft();
        Thrown(() => g.Cancel(new string('x', 501), Now), ErrorCodes.VALIDATION_FAILED);
        g.Cancel(null, Now);
        g.Status.ShouldBe(GameyaStatus.Cancelled);
    }
}
