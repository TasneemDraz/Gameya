using FluentValidation;
using Gameya.Application.Abstractions;
using Gameya.Application.Common.Specifications;
using Gameya.Application.Security;
using Gameya.Application.Users.Specifications;
using Gameya.Domain.Common;
using Gameya.Domain.Users;
using MediatR;

namespace Gameya.Application.Users;

[RequiresAuth]
public sealed record UpdateProfileCommand(string FullName, string Language) : ICommand<Unit>;

public sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileValidator() =>
        RuleFor(c => c.Language).Must(l => l is "ar" or "en").WithErrorCode("INVALID_LANGUAGE");
}

public sealed class UpdateProfileHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : IRequestHandler<UpdateProfileCommand, Unit>
{
    public async Task<Unit> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        var user = await db.Users.FirstOrDefaultAsync(new UserByIdSpec(userId), cancellationToken)
            ?? throw new DomainException(ErrorCodes.UNAUTHENTICATED);

        // The domain enforces the 2-60 character name rule (NAME_INVALID).
        user.SetProfile(request.FullName, Enum.Parse<Language>(request.Language, ignoreCase: true), clock.UtcNow);
        return Unit.Value;
    }
}
