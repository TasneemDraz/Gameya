using Gameya.Domain.Common;
using Gameya.Domain.Gameyas;
using Shouldly;

namespace Gameya.Domain.Tests.Gameyas;

public class GameyaCircleCreateTests
{
    private static readonly DateOnly Today = GameyaTestData.Today;
    private static readonly DateTimeOffset Now = GameyaTestData.Now;
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly Guid Org = Guid.NewGuid();

    private static GameyaSettings Settings() => GameyaTestData.Settings();

    private static GameyaCircle Create(GameyaSettings? settings = null, int organizerShares = 1) =>
        GameyaCircle.Create(Id, Org, settings ?? Settings(), organizerShares, Today, Now);

    private static DomainException Thrown(Action act, string code) => GameyaTestData.Thrown(act, code);

    private static void FieldFails(GameyaSettings settings, string field, int organizerShares = 1)
    {
        var ex = Thrown(() => Create(settings, organizerShares), ErrorCodes.VALIDATION_FAILED);
        ex.Params.ShouldNotBeNull();
        ex.Params["field"].ShouldBe(field);
    }

    [Fact]
    public void Create_is_Draft_with_organizer_membership_at_terms_1()
    {
        var g = Create();

        g.Id.ShouldBe(Id);
        g.OrganizerId.ShouldBe(Org);
        g.Currency.ShouldBe("EGP");
        g.Status.ShouldBe(GameyaStatus.Draft);
        g.TermsVersion.ShouldBe(1);
        g.InviteCode.ShouldBeNull();
        var m = g.Memberships.ShouldHaveSingleItem();
        m.UserId.ShouldBe(Org);
        m.GameyaId.ShouldBe(Id);
        m.Shares.Count.ShouldBe(1);
        m.AcceptedTermsVersion.ShouldBe(1);
        m.Status.ShouldBe(MembershipStatus.Active);
        g.AssignedShares.ShouldBe(1);
        g.ReservedShares.ShouldBe(0);
        g.AvailableShares.ShouldBe(9);
        g.IsFull.ShouldBeFalse();

        var e = g.DomainEvents.OfType<GameyaCreated>().ShouldHaveSingleItem();
        e.GameyaId.ShouldBe(Id);
        e.TargetType.ShouldBe("Gameya");
        e.TargetId.ShouldBe(Id.ToString());
    }

    [Fact]
    public void Non_participating_organizer_has_no_membership_and_must_pass_zero_shares()
    {
        var settings = Settings() with { OrganizerParticipates = false };

        var g = Create(settings, organizerShares: 0);
        g.Memberships.ShouldBeEmpty();
        g.AvailableShares.ShouldBe(10);

        FieldFails(settings, "OrganizerShares", organizerShares: 1);
    }

    [Fact]
    public void Participating_organizer_must_pass_at_least_one_share()
    {
        FieldFails(Settings(), "OrganizerShares", organizerShares: 0);
    }

    [Fact]
    public void Organizer_shares_above_max_fail_MAX_SHARES_EXCEEDED()
    {
        Thrown(() => Create(organizerShares: 3), ErrorCodes.MAX_SHARES_EXCEEDED);
    }

    public static TheoryData<string, object?, PayoutTiming> InvalidFields => new()
    {
        { "Name", "ab", PayoutTiming.AfterFullCollection },
        { "Name", new string('a', 61), PayoutTiming.AfterFullCollection },
        { "ContributionAmount", 99m, PayoutTiming.AfterFullCollection },
        { "ContributionAmount", 100.5m, PayoutTiming.AfterFullCollection },
        { "ContributionAmount", 1_000_001m, PayoutTiming.AfterFullCollection },
        { "ShareCount", 1, PayoutTiming.AfterFullCollection },
        { "ShareCount", 51, PayoutTiming.AfterFullCollection },
        { "GraceDays", 11, PayoutTiming.AfterFullCollection },
        { "GraceDays", -1, PayoutTiming.AfterFullCollection },
        { "PayoutOffsetDays", null, PayoutTiming.FixedDate },
        { "PayoutOffsetDays", 11, PayoutTiming.FixedDate },
        { "PayoutOffsetDays", 3, PayoutTiming.AfterFullCollection },
        { "MaxSharesPerMember", 0, PayoutTiming.AfterFullCollection },
        { "MaxSharesPerMember", 6, PayoutTiming.AfterFullCollection },
        { "Description", new string('d', 501), PayoutTiming.AfterFullCollection },
        { "RulesText", new string('r', 2001), PayoutTiming.AfterFullCollection },
    };

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public void Field_rules_are_enforced(string field, object? value, PayoutTiming timing)
    {
        var s = Settings() with { PayoutTiming = timing };
        s = field switch
        {
            "Name" => s with { Name = (string)value! },
            "ContributionAmount" => s with { ContributionAmount = (decimal)value! },
            "ShareCount" => s with { ShareCount = (int)value! },
            "GraceDays" => s with { GraceDays = (int)value! },
            "PayoutOffsetDays" => s with { PayoutOffsetDays = (int?)value },
            "MaxSharesPerMember" => s with { MaxSharesPerMember = (int)value! },
            "Description" => s with { Description = (string)value! },
            "RulesText" => s with { RulesText = (string)value! },
            _ => throw new ArgumentException(field),
        };

        FieldFails(s, field);
    }

    [Fact]
    public void Accepted_boundaries()
    {
        Create(Settings() with { Name = "  abc  " }).Name.ShouldBe("abc");
        Create(Settings() with { Name = new string('a', 60) });
        Create(Settings() with { ContributionAmount = 100m }).ContributionAmount.ShouldBe(100m);
        Create(Settings() with { ContributionAmount = 1_000_000m }).ContributionAmount.ShouldBe(1_000_000m);
        Create(Settings() with { ShareCount = 2 }).ShareCount.ShouldBe(2);
        Create(Settings() with { ShareCount = 50 }).ShareCount.ShouldBe(50);
        Create(Settings() with { GraceDays = 0 }).GraceDays.ShouldBe(0);
        Create(Settings() with { GraceDays = 10 }).GraceDays.ShouldBe(10);
        Create(Settings() with { MaxSharesPerMember = 1 }).MaxSharesPerMember.ShouldBe(1);
        Create(Settings() with { MaxSharesPerMember = 5 }, organizerShares: 5).MaxSharesPerMember.ShouldBe(5);
        Create(Settings() with { Description = new string('d', 500), RulesText = new string('r', 2000) });
        Create(Settings() with { PayoutTiming = PayoutTiming.FixedDate, PayoutOffsetDays = 0 });
        Create(Settings() with { PayoutTiming = PayoutTiming.FixedDate, PayoutOffsetDays = 10 })
            .PayoutOffsetDays.ShouldBe(10);
    }

    [Fact]
    public void Start_date_before_Cairo_today_fails_START_DATE_IN_PAST()
    {
        Thrown(() => Create(Settings() with { StartDate = Today.AddDays(-1) }), ErrorCodes.START_DATE_IN_PAST);
        Create(Settings() with { StartDate = Today }).StartDate.ShouldBe(Today);
    }

    [Theory]
    [InlineData("2027-01-31", GameyaFrequency.Monthly, 3, "2027-03-31")]
    [InlineData("2027-01-31", GameyaFrequency.Monthly, 2, "2027-02-28")]
    [InlineData("2026-12-01", GameyaFrequency.Weekly, 4, "2026-12-22")]
    public void EndDate_is_due_date_of_last_round(string start, GameyaFrequency f, int shares, string end)
    {
        var g = Create(Settings() with { StartDate = DateOnly.Parse(start), Frequency = f, ShareCount = shares });

        g.EndDate.ShouldBe(DateOnly.Parse(end));
    }

    [Fact]
    public void Pot_and_TotalPerShare_equal_amount_times_shares()
    {
        var g = Create();

        g.Pot.ShouldBe(10_000m);
        g.TotalPerShare.ShouldBe(10_000m);
        g.RoundCount.ShouldBe(10);
    }

    [Fact]
    public void Open_sets_code_and_Recruiting_only_from_Draft()
    {
        var g = Create();
        g.OpenForRecruiting("ABC234", Today);

        g.Status.ShouldBe(GameyaStatus.Recruiting);
        g.InviteCode.ShouldBe("ABC234");
        g.DomainEvents.OfType<GameyaOpened>().ShouldHaveSingleItem().TargetId.ShouldBe(Id.ToString());

        Thrown(() => g.OpenForRecruiting("XYZ234", Today), ErrorCodes.INVALID_STATUS_TRANSITION);
        g.InviteCode.ShouldBe("ABC234");
    }

    [Fact]
    public void Open_with_start_date_in_the_past_fails()
    {
        var g = Create();

        Thrown(() => g.OpenForRecruiting("ABC234", g.StartDate.AddDays(1)), ErrorCodes.START_DATE_IN_PAST);
        g.Status.ShouldBe(GameyaStatus.Draft);
        g.InviteCode.ShouldBeNull();
    }

    [Fact]
    public void Regenerate_replaces_code_only_while_Recruiting()
    {
        var g = Create();
        Thrown(() => g.RegenerateInviteCode("NEW234"), ErrorCodes.INVALID_STATUS_TRANSITION);

        g.OpenForRecruiting("ABC234", Today);
        g.RegenerateInviteCode("NEW234");

        g.InviteCode.ShouldBe("NEW234");
        var e = g.DomainEvents.OfType<InviteCodeRegenerated>().ShouldHaveSingleItem();
        e.Details.ShouldBeNull();
        e.TargetType.ShouldBe("Gameya");
    }
}
