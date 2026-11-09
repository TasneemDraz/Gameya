using Gameya.Application.Abstractions;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Files;

public sealed record FileContent(Stream Content, string ContentType);

/// <summary>Public because the signature is the credential.</summary>
[PublicAccess]
public sealed record GetSignedFileQuery(string Container, string Key, long Exp, string Sig) : IQuery<FileContent>;

public sealed class GetSignedFileHandler(IFileStore fileStore) : IRequestHandler<GetSignedFileQuery, FileContent>
{
    public async Task<FileContent> Handle(GetSignedFileQuery request, CancellationToken cancellationToken)
    {
        // Bad signature, expired link, unknown container and malformed key are indistinguishable on purpose.
        if (!fileStore.IsReadUrlValid(request.Container, request.Key, request.Exp, request.Sig))
        {
            throw new DomainException(ErrorCodes.FORBIDDEN);
        }

        var stream = await fileStore.OpenReadAsync(request.Container, request.Key, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NOT_FOUND);

        // Only JPEGs are ever stored today (photos are normalised on upload).
        return new FileContent(stream, "image/jpeg");
    }
}
