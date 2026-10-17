using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Infrastructure.SignalR;

namespace RestaurantSim.Infrastructure.Tests.SignalR;

public sealed class RestaurantEventHandlersTests
{
    private readonly Guid _restaurantId = Guid.NewGuid();
    private readonly IHubContext<RestaurantHub> _hubContext = Substitute.For<IHubContext<RestaurantHub>>();
    private readonly IClientProxy _proxy = Substitute.For<IClientProxy>();

    public RestaurantEventHandlersTests()
    {
        var clients = Substitute.For<IHubClients>();
        _hubContext.Clients.Returns(clients);
        clients.Group(_restaurantId.ToString()).Returns(_proxy);
    }

    [Fact]
    public async Task GroupArrived_SendsDto()
    {
        var handler = new GroupArrivedNotificationHandler(_hubContext);

        await handler.Handle(new GroupArrived(_restaurantId, 4, 3, 50), CancellationToken.None);

        await AssertSent(ClientEventNames.GroupArrived, new GroupArrivedDto(4, 3, 50));
    }

    [Fact]
    public async Task GroupAbandonedQueue_SendsDto()
    {
        var handler = new GroupAbandonedQueueNotificationHandler(_hubContext);

        await handler.Handle(new GroupAbandonedQueue(_restaurantId, 4, 19.5f, 60), CancellationToken.None);

        await AssertSent(ClientEventNames.GroupAbandonedQueue, new GroupAbandonedQueueDto(4, 19.5f, 60));
    }

    [Fact]
    public async Task GroupTurnedAway_SendsDtoWithAbsoluteReputation()
    {
        var handler = new GroupTurnedAwayNotificationHandler(_hubContext);

        await handler.Handle(new GroupTurnedAway(_restaurantId, 19.8f, 55), CancellationToken.None);

        await AssertSent(ClientEventNames.GroupTurnedAway, new GroupTurnedAwayDto(19.8f, 55));
    }

    [Fact]
    public async Task BillPaid_SendsDtoWithAbsoluteCash()
    {
        var handler = new BillPaidNotificationHandler(_hubContext);

        await handler.Handle(new BillPaid(_restaurantId, 4, 9, 12.50m, 3.10m, 5012.50m, 220), CancellationToken.None);

        await AssertSent(ClientEventNames.BillPaid, new BillPaidDto(4, 12.50m, 3.10m, 5012.50m, 220));
    }

    [Fact]
    public async Task GroupLeft_SendsDtoWithoutStatisticsFields()
    {
        var handler = new GroupLeftNotificationHandler(_hubContext);

        await handler.Handle(new GroupLeft(_restaurantId, 4, 2, 3, 72f, 200), CancellationToken.None);

        await AssertSent(ClientEventNames.GroupLeft, new GroupLeftDto(4, 2, 200));
    }

    [Fact]
    public async Task TableCleaned_SendsDto()
    {
        var handler = new TableCleanedNotificationHandler(_hubContext);

        await handler.Handle(new TableCleaned(_restaurantId, 5, 210), CancellationToken.None);

        await AssertSent(ClientEventNames.TableCleaned, new TableCleanedDto(5, 210));
    }

    private async Task AssertSent(string method, object expected)
    {
        await _proxy.Received(1).SendCoreAsync(
            method,
            Arg.Is<object?[]>(args => args.Length == 1 && Equals(args[0], expected)),
            Arg.Any<CancellationToken>());
    }
}
