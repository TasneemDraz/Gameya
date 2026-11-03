using Gameya.Domain.Activity;
using Gameya.Domain.Common;
using Gameya.Domain.Users;

namespace Gameya.Domain.Tests;

public class UserTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private static User NewUser() =>
        User.Register(Guid.NewGuid(), PhoneNumber.Parse("01012345678"), Language.Ar, T0);

    private static DomainException Thrown(Action act, string code)
    {
        var ex = Should.Throw<DomainException>(act);
        ex.Code.ShouldBe(code);
        return ex;
    }

    [Fact]
    public void Register_raises_UserRegistered_with_actor_override()
    {
        var u = NewUser();
        var e = u.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<UserRegistered>();
        e.Action.ShouldBe("USER_REGISTERED");
        e.ActorUserIdOverride.ShouldBe(u.Id);
        e.TargetType.ShouldBe("User");
        e.GameyaId.ShouldBeNull();
        u.Roles.ShouldBeEmpty();
        u.CreatedAt.ShouldBe(T0);
    }

    [Theory]
    [InlineData(" a ")]
    [InlineData("")]
    public void Name_must_be_2_to_60_chars_after_trim(string name)
    {
        var u = NewUser();
        Thrown(() => u.SetProfile(name, Language.Ar, T0), ErrorCodes.NAME_INVALID);
        Thrown(() => u.SetProfile(new string('x', 61), Language.Ar, T0), ErrorCodes.NAME_INVALID);
        u.ProfileComplete.ShouldBeFalse();
        u.SetProfile(new string('x', 60), Language.Ar, T0);
    }

    [Fact]
    public void SetProfile_marks_profile_complete_and_raises_ProfileUpdated()
    {
        var u = NewUser();
        u.ClearDomainEvents();
        u.SetProfile("  Mona Ali ", Language.En, T0);
        u.FullName.ShouldBe("Mona Ali");
        u.Language.ShouldBe(Language.En);
        u.ProfileComplete.ShouldBeTrue();
        var e = u.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<ProfileUpdated>();
        e.Action.ShouldBe("PROFILE_UPDATED");
        var json = ActivityLogEntry.From(e, u.Id, "Member", T0).DetailsJson;
        json.ShouldContain("\"old\"");
        json.ShouldContain("\"fullName\":\"Mona Ali\"");
        json.ShouldNotContain("010");
    }

    [Fact]
    public void SetProfile_with_same_values_raises_nothing()
    {
        var u = NewUser();
        u.SetProfile("Mona Ali", Language.Ar, T0);
        u.ClearDomainEvents();
        u.SetProfile(" Mona Ali", Language.Ar, T0.AddMinutes(1));
        u.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Fourth_email_send_within_an_hour_throws_RESEND_LIMIT()
    {
        var u = NewUser();
        u.RequestEmailChange("a@b.com", "h1", T0);
        u.ResendEmailVerification("h2", T0.AddMinutes(10));
        u.ResendEmailVerification("h3", T0.AddMinutes(20));
        var ex = Thrown(() => u.ResendEmailVerification("h4", T0.AddMinutes(30)), ErrorCodes.EMAIL_RESEND_LIMIT);
        ex.Params!["retryAt"].ShouldBe(T0.AddHours(1));
        ex.CommitStateChanges.ShouldBeFalse();
        u.PendingEmailTokenHash.ShouldBe("h3");
        Thrown(() => u.RequestEmailChange("c@d.com", "h5", T0.AddMinutes(30)), ErrorCodes.EMAIL_RESEND_LIMIT);
        u.PendingEmail.ShouldBe("a@b.com");
    }

    [Fact]
    public void Email_send_allowed_again_after_an_hour()
    {
        var u = NewUser();
        u.RequestEmailChange("a@b.com", "h1", T0);
        u.ResendEmailVerification("h2", T0.AddMinutes(10));
        u.ResendEmailVerification("h3", T0.AddMinutes(20));
        u.ResendEmailVerification("h4", T0.AddHours(1));
        u.PendingEmailTokenHash.ShouldBe("h4");
        u.EmailSendTimes.Count.ShouldBe(3);
    }

    [Fact]
    public void Resend_requires_pending_email()
    {
        Thrown(() => NewUser().ResendEmailVerification("h", T0), ErrorCodes.EMAIL_TOKEN_INVALID);
    }

    [Fact]
    public void VerifyEmail_with_expired_token_throws()
    {
        var u = NewUser();
        u.RequestEmailChange("a@b.com", "h1", T0);
        var ex = Thrown(() => u.VerifyEmail("h1", T0.AddHours(24).AddSeconds(1)), ErrorCodes.EMAIL_TOKEN_EXPIRED);
        ex.CommitStateChanges.ShouldBeFalse();
    }

    [Fact]
    public void VerifyEmail_with_wrong_token_throws()
    {
        var u = NewUser();
        Thrown(() => u.VerifyEmail("h1", T0), ErrorCodes.EMAIL_TOKEN_INVALID);
        u.RequestEmailChange("a@b.com", "h1", T0);
        Thrown(() => u.VerifyEmail("nope", T0), ErrorCodes.EMAIL_TOKEN_INVALID);
    }

    [Fact]
    public void VerifyEmail_moves_pending_to_verified_and_keeps_old_until_then()
    {
        var u = NewUser();
        u.RequestEmailChange("old@b.com", "h1", T0);
        u.VerifyEmail("h1", T0.AddMinutes(1));
        u.ClearDomainEvents();

        u.RequestEmailChange("new@b.com", "h2", T0.AddDays(1));
        u.Email.ShouldBe("old@b.com");
        u.PendingEmail.ShouldBe("new@b.com");

        u.VerifyEmail("h2", T0.AddDays(1).AddMinutes(5));
        u.Email.ShouldBe("new@b.com");
        u.EmailVerifiedAt.ShouldBe(T0.AddDays(1).AddMinutes(5));
        u.PendingEmail.ShouldBeNull();
        u.PendingEmailTokenHash.ShouldBeNull();
        u.PendingEmailExpiresAt.ShouldBeNull();
        var e = u.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<EmailVerified>();
        e.Action.ShouldBe("EMAIL_VERIFIED");
        ActivityLogEntry.From(e, u.Id, "Member", T0).DetailsJson.ShouldNotContain("@");
    }

    [Fact]
    public void Delete_clears_personal_data_and_raises_AccountDeleted()
    {
        var u = NewUser();
        u.SetProfile("Mona Ali", Language.Ar, T0);
        u.SetPhoto("photos/x.jpg", T0);
        u.RequestEmailChange("a@b.com", "h1", T0);
        u.ClearDomainEvents();

        u.Delete(T0.AddDays(1));
        u.IsDeleted.ShouldBeTrue();
        u.DeletedAt.ShouldBe(T0.AddDays(1));
        u.Phone.ShouldBeNull();
        u.FullName.ShouldBeNull();
        u.Email.ShouldBeNull();
        u.PendingEmail.ShouldBeNull();
        u.PendingEmailTokenHash.ShouldBeNull();
        u.PendingEmailExpiresAt.ShouldBeNull();
        u.PhotoKey.ShouldBeNull();
        u.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AccountDeleted>().Action.ShouldBe("ACCOUNT_DELETED");
    }

    [Fact]
    public void Block_unblock_and_logout_everywhere()
    {
        var u = NewUser();
        u.Block();
        u.IsBlocked.ShouldBeTrue();
        u.Unblock();
        u.IsBlocked.ShouldBeFalse();
        u.ClearDomainEvents();
        u.MarkLoggedOutEverywhere(T0);
        u.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<LoggedOutAll>().Action.ShouldBe("LOGGED_OUT_ALL");
    }

    [Fact]
    public void ActivityLogEntry_null_details_is_empty_object()
    {
        var u = NewUser();
        u.MarkLoggedOutEverywhere(T0);
        var entry = ActivityLogEntry.From((IActivityEvent)u.DomainEvents[^1], u.Id, "Member", T0);
        entry.DetailsJson.ShouldBe("{}");
        entry.TargetId.ShouldBe(u.Id.ToString());
        entry.ActorUserId.ShouldBe(u.Id);
        entry.OccurredAt.ShouldBe(T0);
    }

    [Fact]
    public void ActivityLogEntry_actor_override_wins()
    {
        var u = NewUser();
        var entry = ActivityLogEntry.From((IActivityEvent)u.DomainEvents[0], null, "System", T0);
        entry.ActorUserId.ShouldBe(u.Id);
    }
}
