using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Security;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Users;

[RequiresAuth]
public sealed record GetMeQuery : IQuery<MeDto>;

public sealed class GetMeHandler(IAppDbContext db, ICurrentUser currentUser, IFileStore fileStore)
    : IRequestHandler<GetMeQuery, MeDto>
{
    public async Task<MeDto> Handle(GetMeQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var row = await db.Users.FirstOrDefaultAsync(new MeRowSpec(userId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);

        return new MeDto(
            row.Id,
            row.FullName,
            row.PhoneE164,
            row.Email,
            row.EmailVerifiedAt is not null,
            row.PendingEmail,
            row.PhotoKey is null
                ? null
                : fileStore.GetReadUrl(FileContainers.ProfilePhotos, row.PhotoKey, FileContainers.ReadUrlLifetime),
            row.Language.ToString().ToLowerInvariant(),
            row.ProfileComplete);
    }
}
