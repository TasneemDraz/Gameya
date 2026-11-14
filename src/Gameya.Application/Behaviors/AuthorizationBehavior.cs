using System.Reflection;
using Gameya.Application.Abstractions;
using Gameya.Application.Gameyas;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Behaviors;

public sealed class AuthorizationBehavior<TRequest, TResponse>(ICurrentUser currentUser, IGameyaAccess gameyaAccess)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var access = typeof(TRequest).GetCustomAttributes<AccessAttribute>(inherit: false).ToList();
        if (access.Count != 1)
        {
            // Fail closed: a request must declare exactly one access attribute.
            throw new InvalidOperationException(
                $"{typeof(TRequest).FullName} must declare exactly one access attribute.");
        }

        switch (access[0])
        {
            case RequiresAuthAttribute:
                RequireAuthenticated();
                break;
            case RequiresRoleAttribute r:
                RequireAuthenticated();
                if (!currentUser.Roles.Contains(r.Role))
                {
                    throw new DomainException(ErrorCodes.FORBIDDEN);
                }

                break;
            case RequiresGameyaMemberAttribute:
                await RequireGameyaRoleAsync(request, GameyaRole.Member, cancellationToken);
                break;
            case RequiresGameyaOrganizerAttribute:
                await RequireGameyaRoleAsync(request, GameyaRole.Organizer, cancellationToken);
                break;
        }

        return await next(cancellationToken);
    }

    private void RequireAuthenticated()
    {
        if (!currentUser.IsAuthenticated)
        {
            throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        }
    }

    /// <summary>Anyone below the required role learns nothing: the gam'eya is "not found", never forbidden.</summary>
    private async Task RequireGameyaRoleAsync(TRequest request, GameyaRole required, CancellationToken ct)
    {
        if (request is not IGameyaRequest scoped)
        {
            // Fail closed: a gam'eya attribute is meaningless without the gam'eya id.
            throw new InvalidOperationException(
                $"{typeof(TRequest).FullName} is gam'eya-scoped but does not implement {nameof(IGameyaRequest)}.");
        }

        RequireAuthenticated();
        var role = await gameyaAccess.GetRoleAsync(scoped.GameyaId, currentUser.UserId!.Value, ct);
        if (role < required)
        {
            throw new DomainException(ErrorCodes.GAMEYA_NOT_FOUND);
        }
    }
}
