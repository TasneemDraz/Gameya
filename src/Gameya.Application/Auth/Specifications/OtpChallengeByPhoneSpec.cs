using Ardalis.Specification;
using Gameya.Domain.Auth;
using Gameya.Domain.Users;

namespace Gameya.Application.Auth.Specifications;

public sealed class OtpChallengeByPhoneSpec : Specification<OtpChallenge>
{
    public OtpChallengeByPhoneSpec(PhoneNumber phone) => Query.Where(c => c.Phone == phone);
}
