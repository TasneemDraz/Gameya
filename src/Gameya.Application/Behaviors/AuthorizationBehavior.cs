using System.Reflection;
using Gameya.Application.Abstractions;
using Gameya.Application.Security;
using Gameya.Domain.Common;
using MediatR;

namespace Gameya.Application.Behaviors;

public sealed class AuthorizationBehavior<TRequest, TResponse>(ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(
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
        }

        return next(cancellationToken);
    }

    private void RequireAuthenticated()
    {
        if (!currentUser.IsAuthenticated)
        {
            throw new DomainException(ErrorCodes.UNAUTHENTICATED);
        }
    }
}
