using Ardalis.Specification.EntityFrameworkCore;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Users;

namespace Gameya.Application.Tests;

public class VerifiedEmailResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private static User NewUser(string phone) => User.Register(Guid.NewGuid(), PhoneNumber.Parse(phone), Language.En, Now);

    private static User Verified(string phone, string email)
    {
        var user = NewUser(phone);
        user.RequestEmailChange(email, "hash", Now);
        user.VerifyEmail("hash", Now);
        return user;
    }

    private static string? Resolve(IEnumerable<User> users, Guid id) =>
        new[] { new VerifiedEmailSpec(id) }
            .SelectMany(spec => SpecificationEvaluator.Default.GetQuery(users.AsQueryable(), spec))
            .FirstOrDefault();

    [Fact]
    public void Returns_the_email_once_verified()
    {
        var user = Verified("01012345678", "a@example.com");
        Resolve([user], user.Id).ShouldBe("a@example.com");
    }

    [Fact]
    public void Returns_null_while_the_email_is_only_pending()
    {
        var user = NewUser("01012345678");
        user.RequestEmailChange("a@example.com", "hash", Now);
        Resolve([user], user.Id).ShouldBeNull();
    }

    [Fact]
    public void Returns_null_for_a_deleted_account()
    {
        var user = Verified("01012345678", "a@example.com");
        user.Delete(Now);
        Resolve([user], user.Id).ShouldBeNull();
    }

    [Fact]
    public void Returns_null_for_an_unknown_user()
    {
        var user = Verified("01012345678", "a@example.com");
        Resolve([user], Guid.NewGuid()).ShouldBeNull();
    }
}
