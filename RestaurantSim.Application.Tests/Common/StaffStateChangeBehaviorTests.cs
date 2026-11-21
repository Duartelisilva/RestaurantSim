using FluentAssertions;
using MediatR;
using NSubstitute;
using RestaurantSim.Application.Common;
using RestaurantSim.Application.Tests.Support;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Staff;
using Xunit;

namespace RestaurantSim.Application.Tests.Common;

public class StaffStateChangeBehaviorTests
{
    private sealed record ScopedCommand(Guid RestaurantId) : IRequest<string>, IRestaurantScopedRequest;

    private sealed record UnscopedCommand : IRequest<string>;

    private readonly IRestaurantRepository _repository = Substitute.For<IRestaurantRepository>();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly StaffStateChangeBehavior<ScopedCommand, string> _behavior;
    private readonly Restaurant _restaurant;

    public StaffStateChangeBehaviorTests()
    {
        _behavior = new StaffStateChangeBehavior<ScopedCommand, string>(_repository, _publisher);
        _restaurant = TestFloors.Row(0, 0);
        _restaurant.StaffRoster.Staff.Add(Staff(1, StaffRole.Busser));
        _restaurant.StaffRoster.Staff.Add(Staff(2, StaffRole.Cook));
        _repository.GetAsync(_restaurant.RestaurantId, Arg.Any<CancellationToken>()).Returns(_restaurant);
    }

    [Fact]
    public async Task AStaffMemberWhoStartsATask_IsPublished_WithTheStateAfterTheCommandAsync()
    {
        var result = await RunAsync(() =>
        {
            var busser = _restaurant.StaffRoster.Staff[0];
            busser.State = StaffState.Busy;
            busser.CurrentTaskType = TaskType.CleanTable;
            busser.CurrentTaskTargetId = 3;
            busser.Stats.Set(StatType.Fatigue, 0.25f);
        });

        result.Should().Be("done");
        await _publisher.Received(1).Publish(
            Arg.Is<StaffStateChanged>(e => e.StaffId == 1
                && e.State == StaffState.Busy
                && e.TaskType == TaskType.CleanTable
                && e.TaskTargetId == 3
                && e.Fatigue == 0.25f
                && e.Tick == _restaurant.SimulationTick
                && e.RestaurantId == _restaurant.RestaurantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NothingChanged_PublishesNothingAsync()
    {
        await RunAsync(() => { });

        await _publisher.DidNotReceiveWithAnyArgs().Publish(default(StaffStateChanged)!, default);
    }

    [Fact]
    public async Task OnlyFatigueChanged_PublishesNothing_BecauseItMovesEveryTickAsync()
    {
        await RunAsync(() => _restaurant.StaffRoster.Staff[0].Stats.Set(StatType.Fatigue, 0.9f));

        await _publisher.DidNotReceiveWithAnyArgs().Publish(default(StaffStateChanged)!, default);
    }

    [Fact]
    public async Task FinishingATask_IsPublished_WithNoTaskAsync()
    {
        var busser = _restaurant.StaffRoster.Staff[0];
        busser.State = StaffState.Busy;
        busser.CurrentTaskType = TaskType.CleanTable;
        busser.CurrentTaskTargetId = 3;

        await RunAsync(() =>
        {
            busser.State = StaffState.Idle;
            busser.CurrentTaskType = null;
            busser.CurrentTaskTargetId = null;
        });

        await _publisher.Received(1).Publish(
            Arg.Is<StaffStateChanged>(e => e.StaffId == 1 && e.State == StaffState.Idle && e.TaskType == null && e.TaskTargetId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnlyTheStaffWhoChanged_ArePublishedAsync()
    {
        await RunAsync(() => _restaurant.StaffRoster.Staff[1].State = StaffState.OffShift);

        await _publisher.Received(1).Publish(Arg.Any<StaffStateChanged>(), Arg.Any<CancellationToken>());
        await _publisher.Received(1).Publish(Arg.Is<StaffStateChanged>(e => e.StaffId == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStaffMemberAddedByTheCommand_IsPublishedAsync()
    {
        await RunAsync(() => _restaurant.StaffRoster.Staff.Add(Staff(3, StaffRole.Waiter)));

        await _publisher.Received(1).Publish(Arg.Is<StaffStateChanged>(e => e.StaffId == 3), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EventsComeAfterTheCommandHasRunAsync()
    {
        var order = new List<string>();
        _publisher.Publish(Arg.Any<StaffStateChanged>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            order.Add("publish");
            return Task.CompletedTask;
        });

        await RunAsync(() =>
        {
            order.Add("handler");
            _restaurant.StaffRoster.Staff[0].State = StaffState.Busy;
        });

        order.Should().Equal("handler", "publish");
    }

    [Fact]
    public async Task ARequestWithoutARestaurant_PassesThroughUntouchedAsync()
    {
        var behavior = new StaffStateChangeBehavior<UnscopedCommand, string>(_repository, _publisher);

        var result = await behavior.Handle(new UnscopedCommand(), _ => Task.FromResult("ok"), CancellationToken.None);

        result.Should().Be("ok");
        await _repository.DidNotReceiveWithAnyArgs().GetAsync(default, default);
    }

    [Fact]
    public async Task AnUnknownRestaurant_JustReturnsTheResponseAsync()
    {
        _repository.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Restaurant?)null);

        var result = await _behavior.Handle(new ScopedCommand(Guid.NewGuid()), _ => Task.FromResult("done"), CancellationToken.None);

        result.Should().Be("done");
        await _publisher.DidNotReceiveWithAnyArgs().Publish(default(StaffStateChanged)!, default);
    }

    private async Task<string> RunAsync(Action commandBody)
    {
        return await _behavior.Handle(
            new ScopedCommand(_restaurant.RestaurantId),
            _ =>
            {
                commandBody();
                return Task.FromResult("done");
            },
            CancellationToken.None);
    }

    private static StaffMember Staff(int id, StaffRole role) => new()
    {
        StaffId = id, Name = $"{role} {id}", Role = role, State = StaffState.Idle,
        Stats = new StaffStats(), AssignedMealPeriods = Array.Empty<string>()
    };
}
