using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Users;

namespace Gameya.Application.Tests;

public class SpecificationEvaluationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private sealed class NameAndIdSpec : Specification<Domain.Users.User, (Guid, string?)>
    {
        public NameAndIdSpec() =>
            Query.Select(u => new ValueTuple<Guid, string?>(u.Id, u.FullName));
    }

    private static Domain.Users.User NewUser(string phone) =>
        Domain.Users.User.Register(Guid.NewGuid(), PhoneNumber.Parse(phone), Language.Ar, Now);

    [Fact]
    public void ActiveUserByPhoneSpec_excludes_deleted_users()
    {
        var active = NewUser("01012345678");
        var deleted = NewUser("01112345678");
        var deletedPhone = deleted.Phone!;
        deleted.Delete(Now);
        var users = new[] { active, deleted }.AsQueryable();

        SpecificationEvaluator.Default.GetQuery(users, new ActiveUserByPhoneSpec(active.Phone!)).ShouldBe([active]);
        SpecificationEvaluator.Default.GetQuery(users, new ActiveUserByPhoneSpec(deletedPhone)).ShouldBeEmpty();
    }

    [Fact]
    public void ActiveUserByPhoneSpec_excludes_soft_deleted_user_even_if_phone_matches()
    {
        var phone = PhoneNumber.Parse("01012345678");
        var user = Domain.Users.User.Register(Guid.NewGuid(), phone, Language.Ar, Now);
        // Soft-deleted rows may legally retain a phone value in storage; DeletedAt alone must exclude them.
        typeof(Domain.Users.User).GetProperty(nameof(Domain.Users.User.DeletedAt))!.SetValue(user, Now);
        SpecificationEvaluator.Default.GetQuery(new[] { user }.AsQueryable(), new ActiveUserByPhoneSpec(phone))
            .ShouldBeEmpty();
    }

    [Fact]
    public void UserByIdSpec_finds_the_user()
    {
        var a = NewUser("01012345678");
        var b = NewUser("01112345678");
        SpecificationEvaluator.Default.GetQuery(new[] { a, b }.AsQueryable(), new UserByIdSpec(b.Id)).ShouldBe([b]);
    }

    [Fact]
    public void Projection_spec_selects_requested_fields()
    {
        var u = NewUser("01012345678");
        u.SetProfile("Mona Ali", Language.En, Now);
        var rows = SpecificationEvaluator.Default
            .GetQuery(new[] { u }.AsQueryable(), new NameAndIdSpec()).ToList();
        rows.ShouldBe([(u.Id, (string?)"Mona Ali")]);
    }
}
