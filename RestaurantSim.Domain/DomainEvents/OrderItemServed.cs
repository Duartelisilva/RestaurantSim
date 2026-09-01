using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

public sealed record OrderItemServed(
    Guid RestaurantId,
    int OrderItemId,
    int GroupId,
    float QualityAtServe,
    float MoodBonus,
    long Tick) : INotification;
