using MediatR;

namespace Gameya.Application.Abstractions;

/// <summary>Marker for state-changing requests (transaction and activity log apply).</summary>
public interface ICommandMarker;

public interface ICommand<out TResult> : IRequest<TResult>, ICommandMarker;

public interface IQuery<out TResult> : IRequest<TResult>;
