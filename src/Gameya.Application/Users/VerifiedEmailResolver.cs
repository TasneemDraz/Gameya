using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Users.Specifications;

namespace Gameya.Application.Users;

/// <summary>The only way to obtain an address for notification email: unverified or deleted accounts yield null.</summary>
public sealed class VerifiedEmailResolver(IAppDbContext db)
{
    public Task<string?> ResolveAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(new VerifiedEmailSpec(userId), cancellationToken);
}
