using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Security;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Users;

public sealed record SetPhotoResult(Uri PhotoUrl);

[RequiresAuth]
public sealed record SetPhotoCommand(Stream Content, long Length) : ICommand<SetPhotoResult>;

public sealed class SetPhotoHandler(
    IAppDbContext db, ICurrentUser currentUser, IFileStore fileStore, IImageProcessor images, IClock clock,
    ITransactionCallbacks callbacks)
    : IRequestHandler<SetPhotoCommand, SetPhotoResult>
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int MaxDimension = 512;

    public async Task<SetPhotoResult> Handle(SetPhotoCommand request, CancellationToken cancellationToken)
    {
        if (request.Length > MaxBytes)
        {
            throw new DomainException(ErrorCodes.FILE_TOO_LARGE);
        }

        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var user = await db.Users.FirstOrDefaultAsync(new UserByIdSpec(userId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);

        await using var jpeg = images.ToJpeg(request.Content, MaxDimension);
        var key = $"{userId}/{Guid.CreateVersion7()}.jpg";
        await fileStore.UploadAsync(FileContainers.ProfilePhotos, key, jpeg, "image/jpeg", cancellationToken);

        // The upload is outside the transaction: remove it if the request fails, and drop the old file only once
        // the new key is committed.
        callbacks.OnRolledBack(ct => fileStore.DeleteAsync(FileContainers.ProfilePhotos, key, ct));

        var oldKey = user.PhotoKey;
        user.SetPhoto(key, clock.UtcNow);
        if (oldKey is not null)
        {
            callbacks.OnCommitted(ct => fileStore.DeleteAsync(FileContainers.ProfilePhotos, oldKey, ct));
        }

        return new SetPhotoResult(fileStore.GetReadUrl(FileContainers.ProfilePhotos, key, FileContainers.ReadUrlLifetime));
    }
}
