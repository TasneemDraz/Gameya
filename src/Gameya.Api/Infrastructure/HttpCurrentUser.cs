using Gameya.Application.Abstractions;

namespace Gameya.Api.Infrastructure;

/// <summary>Reads the <c>sub</c> and <c>role</c> claims of the authenticated principal.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private System.Security.Claims.ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirst("sub")?.Value, out var id) ? id : null;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true && UserId is not null;

    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll("role").Select(c => c.Value).ToArray() ?? [];
}
