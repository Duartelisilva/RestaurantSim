using System.Collections.Concurrent;
using MediatR;

namespace RestaurantSim.Application.Common;

// A command implements this to participate in the per-restaurant lock.
// Requests that don't (e.g. some queries) simply skip locking.
public interface IRestaurantScopedRequest
{
    Guid RestaurantId { get; }
}

// Ordered AHEAD OF ValidationBehavior (architecture doc §11). Held across the
// full validate -> handle -> save sequence. Applies identically to player
// commands, AutoManager commands, and SimulationTick — nothing bypasses it.
public sealed class RestaurantLockBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IRestaurantScopedRequest scoped)
            return await next();

        var semaphore = Locks.GetOrAdd(scoped.RestaurantId, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return await next();
        }
        finally
        {
            semaphore.Release();
        }
    }
}
