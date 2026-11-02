using FluentAssertions;
using RestaurantSim.Contracts.Snapshot;
using RestaurantSim.Domain.CustomerService;
using Xunit;

namespace RestaurantSim.Application.Tests.Queries.GetRestaurantSnapshot;

// Contracts cannot see Domain, so the wire names are plain strings. These tests are the
// only thing keeping them identical to the enums the server stringifies into them.
public class DiningNamesTests
{
    [Fact]
    public void DiningStageNames_MatchTheDomainEnumMembers()
    {
        string[] wireNames =
        [
            DiningStageNames.Ordering, DiningStageNames.WaitingForFood, DiningStageNames.Eating,
            DiningStageNames.Paying, DiningStageNames.Leaving
        ];

        wireNames.Should().BeEquivalentTo(Enum.GetNames<DiningStage>());
    }

    [Fact]
    public void DiningVisitNames_MatchTheDomainEnumMembers()
    {
        string[] wireNames =
        [
            DiningVisitNames.Drinks, DiningVisitNames.Main, DiningVisitNames.Refill,
            DiningVisitNames.Dessert, DiningVisitNames.Bill
        ];

        wireNames.Should().BeEquivalentTo(Enum.GetNames<DiningVisit>());
    }
}
