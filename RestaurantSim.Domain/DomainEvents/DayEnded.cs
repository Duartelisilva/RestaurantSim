using MediatR;

namespace RestaurantSim.Domain.DomainEvents;

// Name matches CLAUDE.md's own "Commands Introduced This Session" listing:
// "DayEnded — past-tense fact raised by EndDayCommand."
public sealed record DayEnded(Guid RestaurantId, int NewDayNumber, long Tick) : INotification;
