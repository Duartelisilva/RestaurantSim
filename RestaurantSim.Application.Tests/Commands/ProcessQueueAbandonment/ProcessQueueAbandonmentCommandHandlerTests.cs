using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Commands.ProcessQueueAbandonment;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Scheduling;
using Xunit;

namespace RestaurantSim.Application.Tests.Commands.ProcessQueueAbandonment;

public class ProcessQueueAbandonmentCommandHandlerTests
{
    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPresetRepository<RestaurantConfig> _presetRepository = Substitute.For<IPresetRepository<RestaurantConfig>>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly ProcessQueueAbandonmentCommandHandler _handler;

    public ProcessQueueAbandonmentCommandHandlerTests()
    {
        _handler = new ProcessQueueAbandonmentCommandHandler(_repository, _presetRepository, _publisher);
    }

    [Fact]
    public async Task Handle_RemovesGroup_WhenAverageMoodBelowThreshold()
    {
        var restaurant = BuildRestaurant(reputation: 50f);
        var group = AddGroup(restaurant, groupId: 1, moods: new[] { 5f, 10f }); // avg 7.5, below default 15
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(BuildConfig());

        var result = await _handler.Handle(new ProcessQueueAbandonmentCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        restaurant.CustomerService.Queue.Should().NotContain(group);
        await _publisher.Received(1).Publish(
            Arg.Is<GroupAbandonedQueue>(e => e.GroupId == group.GroupId && e.ReputationAfter == restaurant.Reputation),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AppliesFlatReputationPenalty_WithNoWitnessedScaling()
    {
        var restaurant = BuildRestaurant(reputation: 50f);
        AddGroup(restaurant, groupId: 1, moods: new[] { 5f });
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(BuildConfig());

        await _handler.Handle(new ProcessQueueAbandonmentCommand(restaurant.RestaurantId), CancellationToken.None);

        // Confirmed design: flat penalty, no "witnessed" concept anymore.
        restaurant.Reputation.Should().Be(49.5f);
    }

    [Fact]
    public async Task Handle_AppliesSamePenalty_RegardlessOfOtherGroupsInQueue()
    {
        var restaurantAlone = BuildRestaurant(reputation: 50f);
        AddGroup(restaurantAlone, groupId: 1, moods: new[] { 5f });
        _repository.GetAsync(restaurantAlone.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurantAlone);
        _presetRepository.GetAsync(restaurantAlone.ConfigId, Arg.Any<CancellationToken>()).Returns(BuildConfig());

        var restaurantWithOthers = BuildRestaurant(reputation: 50f);
        AddGroup(restaurantWithOthers, groupId: 1, moods: new[] { 5f }); // abandons
        AddGroup(restaurantWithOthers, groupId: 2, moods: new[] { 90f }); // stays, present but irrelevant now
        _repository.GetAsync(restaurantWithOthers.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurantWithOthers);
        _presetRepository.GetAsync(restaurantWithOthers.ConfigId, Arg.Any<CancellationToken>()).Returns(BuildConfig());

        await _handler.Handle(new ProcessQueueAbandonmentCommand(restaurantAlone.RestaurantId), CancellationToken.None);
        await _handler.Handle(new ProcessQueueAbandonmentCommand(restaurantWithOthers.RestaurantId), CancellationToken.None);

        restaurantAlone.Reputation.Should().Be(restaurantWithOthers.Reputation);
    }

    [Fact]
    public async Task Handle_KeepsGroup_WhenAverageMoodAtOrAboveThreshold()
    {
        var restaurant = BuildRestaurant(reputation: 50f);
        var group = AddGroup(restaurant, groupId: 1, moods: new[] { 20f, 30f }); // avg 25, above default 15
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(BuildConfig());

        var result = await _handler.Handle(new ProcessQueueAbandonmentCommand(restaurant.RestaurantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        restaurant.CustomerService.Queue.Should().Contain(group);
        restaurant.Reputation.Should().Be(50f);
        await _publisher.DidNotReceive().Publish(Arg.Any<GroupAbandonedQueue>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ProcessesMultipleGroups_InOneCall()
    {
        var restaurant = BuildRestaurant(reputation: 50f);
        AddGroup(restaurant, groupId: 1, moods: new[] { 5f });
        AddGroup(restaurant, groupId: 2, moods: new[] { 8f });
        AddGroup(restaurant, groupId: 3, moods: new[] { 90f }); // stays
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(BuildConfig());

        await _handler.Handle(new ProcessQueueAbandonmentCommand(restaurant.RestaurantId), CancellationToken.None);

        restaurant.CustomerService.Queue.Should().HaveCount(1);
        restaurant.CustomerService.Queue[0].GroupId.Should().Be(3);
        restaurant.Reputation.Should().Be(49f); // two abandonments x 0.5 penalty
    }

    [Fact]
    public async Task Handle_NeverDropsReputationBelowZero()
    {
        var restaurant = BuildRestaurant(reputation: 0.2f);
        AddGroup(restaurant, groupId: 1, moods: new[] { 5f });
        _repository.GetAsync(restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(restaurant);
        _presetRepository.GetAsync(restaurant.ConfigId, Arg.Any<CancellationToken>()).Returns(BuildConfig());

        await _handler.Handle(new ProcessQueueAbandonmentCommand(restaurant.RestaurantId), CancellationToken.None);

        restaurant.Reputation.Should().Be(0f);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenRestaurantDoesNotExist()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _handler.Handle(new ProcessQueueAbandonmentCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private static CustomerGroup AddGroup(Restaurant restaurant, int groupId, float[] moods)
    {
        var members = moods.Select((mood, i) => new Customer
        {
            CustomerId = groupId * 10 + i,
            IsAdult = true,
            Mood = mood,
            PatienceLevel = 0.5f,
            Instability = 0.5f,
            SusceptibilityToGroup = 0.5f,
            SpendingTendency = SpendingTendency.Normal,
            IsRegular = false,
            Gender = Gender.Male
        }).ToList();

        var group = new CustomerGroup
        {
            GroupId = groupId,
            PartySize = members.Count,
            Members = members,
            ArrivalTick = 0
        };

        restaurant.CustomerService.Queue.Add(group);
        return group;
    }

    private static Restaurant BuildRestaurant(float reputation) => new()
    {
        RestaurantId = Guid.NewGuid(),
        ConfigId = "default_diner",
        Reputation = reputation,
        Floor = new FloorState(),
        StaffRoster = new StaffRosterState(),
        CustomerService = new CustomerServiceState(),
        KitchenOps = new KitchenOpsState(),
        Events = new EventState()
    };

    private static RestaurantConfig BuildConfig() => new()
    {
        ConfigId = "default_diner",
        StartingCash = 5000m,
        MinutesPerTick = 1,
        TicksPerRealSecond = 1,
        TableLayoutId = "default_diner_layout",
        StaffRosterSeed = Array.Empty<(RestaurantSim.Domain.Staff.StaffRole, int)>(),
        MenuItemIds = Array.Empty<int>(),
        ReputationStart = 20f,
        QueueWaitMoodDecayRate = -0.4f,
        CourseWaitMoodDecayRate = -0.3f,
        NaturalMoodRecoveryRate = 0.5f,
        ProbabilityCheckpointIntervalSeconds = 3f,
        PathReplanMaxWaitSeconds = 5f,
        MaxCallAttempts = 3,
        ActiveMealPeriods = new List<MealPeriodDefinition>(),
        EventModuleIds = Array.Empty<string>(),
        TipBaseRate = 50f,
        TipMoodExponent = 0.75f,
        MaxQueueLength = 12,
        BaseArrivalIntervalSeconds = 120f,
        QueueAbandonmentMoodThreshold = null
    };
}
