namespace Gameya.Application.Gameyas;

/// <summary>A request scoped to one gam'eya; required by the gam'eya access attributes.</summary>
public interface IGameyaRequest
{
    Guid GameyaId { get; }
}
