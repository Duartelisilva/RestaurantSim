using MediatR;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;

namespace RestaurantSim.Application.EventHandlers;

public sealed class GroupLeftStatsHandler : INotificationHandler<GroupLeft>
{
    private readonly IRestaurantRepository _repository;

    public GroupLeftStatsHandler(IRestaurantRepository repository)
    {
        _repository = repository;
    }

    public async Task Handle(GroupLeft notification, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(notification.RestaurantId, cancellationToken);
        if (restaurant is null) return;

        restaurant.DailyStats.CustomersServed += notification.PartySize;
        // Weighted by PartySize — a party-of-6's average Mood counts 6x more
        // toward the day's overall AvgFinalMood than a party-of-1's, matching
        // "average across actual customers," not "average across visits."
        restaurant.DailyStats.WeightedFinalMoodSum += notification.FinalAverageMood * notification.PartySize;

        await _repository.SaveAsync(restaurant, cancellationToken);
    }
}
