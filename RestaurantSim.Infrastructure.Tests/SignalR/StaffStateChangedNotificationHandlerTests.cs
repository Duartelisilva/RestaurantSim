using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Contracts.Snapshot;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.Staff;
using RestaurantSim.Infrastructure.SignalR;

namespace RestaurantSim.Infrastructure.Tests.SignalR;

public sealed class StaffStateChangedNotificationHandlerTests
{
    private readonly Guid _restaurantId = Guid.NewGuid();
    private readonly IHubContext<RestaurantHub> _hubContext = Substitute.For<IHubContext<RestaurantHub>>();
    private readonly IClientProxy _proxy = Substitute.For<IClientProxy>();
    private readonly StaffStateChangedNotificationHandler _handler;

    public StaffStateChangedNotificationHandlerTests()
    {
        var clients = Substitute.For<IHubClients>();
        _hubContext.Clients.Returns(clients);
        clients.Group(_restaurantId.ToString()).Returns(_proxy);
        _handler = new StaffStateChangedNotificationHandler(_hubContext);
    }

    [Fact]
    public async Task Handle_SendsTheStateAndTaskToTheRestaurantGroupAsync()
    {
        await _handler.Handle(
            new StaffStateChanged(_restaurantId, 7, StaffState.Busy, TaskType.CleanTable, 3, 0.25f, 120),
            CancellationToken.None);

        var expected = new StaffStateChangedDto(7, StaffStateNames.Busy, TaskTypeNames.CleanTable, 3, 0.25f, 120);
        await _proxy.Received(1).SendCoreAsync(
            ClientEventNames.StaffStateChanged,
            Arg.Is<object?[]>(args => args.Length == 1 && Equals(args[0], expected)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutATask_SendsNullsAsync()
    {
        await _handler.Handle(
            new StaffStateChanged(_restaurantId, 7, StaffState.Idle, null, null, 0f, 130),
            CancellationToken.None);

        var expected = new StaffStateChangedDto(7, StaffStateNames.Idle, null, null, 0f, 130);
        await _proxy.Received(1).SendCoreAsync(
            ClientEventNames.StaffStateChanged,
            Arg.Is<object?[]>(args => args.Length == 1 && Equals(args[0], expected)),
            Arg.Any<CancellationToken>());
    }
}
