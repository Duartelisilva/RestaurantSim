using FluentValidation;
using RestaurantSim.Domain.Abstractions;

namespace RestaurantSim.Application.Commands.SeatGroup;

// Re-checks live state at handle-time (runs inside the per-restaurant lock,
// after RestaurantLockBehavior) — CLAUDE.md's own race-condition example is
// this exact scenario: two seat requests for the same table moments apart.
public sealed class SeatGroupCommandValidator : AbstractValidator<SeatGroupCommand>
{
    private readonly IRestaurantRepository _repository;

    public SeatGroupCommandValidator(IRestaurantRepository repository)
    {
        _repository = repository;

        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleFor(x => x.GroupId).GreaterThan(0);
        RuleFor(x => x.ClusterId).GreaterThan(0);

        RuleFor(x => x)
            .MustAsync(GroupIsQueuedAsync)
            .WithMessage(x => $"Group {x.GroupId} is not currently queued.");

        RuleFor(x => x)
            .MustAsync(ClusterFitsAndIsFreeAsync)
            .WithMessage(x => $"Cluster {x.ClusterId} is not available or does not fit the group.");
    }

    private async Task<bool> GroupIsQueuedAsync(SeatGroupCommand command, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(command.RestaurantId, cancellationToken);
        return restaurant?.CustomerService.Queue.Any(g => g.GroupId == command.GroupId) ?? false;
    }

    private async Task<bool> ClusterFitsAndIsFreeAsync(SeatGroupCommand command, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(command.RestaurantId, cancellationToken);
        if (restaurant is null) return false;

        var group = restaurant.CustomerService.Queue.FirstOrDefault(g => g.GroupId == command.GroupId);
        var cluster = restaurant.Floor.Clusters.FirstOrDefault(c => c.ClusterId == command.ClusterId);
        if (group is null || cluster is null) return false;

        // Same method the handler calls before committing — single source of truth.
        return cluster.CanSeat(group.PartySize, restaurant.Floor.Tables, restaurant.CustomerService.Reservations, restaurant.SimulationTick);
    }
}
