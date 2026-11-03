using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Security;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Users;

[RequiresAuth]
public sealed record RemovePhotoCommand : ICommand<Unit>;

public sealed class RemovePhotoHandler(IAppDbContext db, ICurrentUser currentUser, IFileStore fileStore, IClock clock)
    : IRequestHandler<RemovePhotoCommand, Unit>
{
    public async Task<Unit> Handle(RemovePhotoCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var user = await db.Users.FirstOrDefaultAsync(new UserByIdSpec(userId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);

        var oldKey = user.PhotoKey;
        user.SetPhoto(null, clock.UtcNow);
        if (oldKey is not null)
        {
            await fileStore.DeleteAsync(FileContainers.ProfilePhotos, oldKey, cancellationToken);
        }

        return Unit.Value;
    }
}
