using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Contracts.Snapshot;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Infrastructure.SignalR;

namespace RestaurantSim.Infrastructure.Tests.SignalR;

public sealed class GroupDiningStageChangedNotificationHandlerTests
{
    [Fact]
    public async Task Handle_SendsDtoToTheRestaurantGroupOnlyAsync()
    {
        var restaurantId = Guid.NewGuid();
        var hubContext = Substitute.For<IHubContext<RestaurantHub>>();
        var clients = Substitute.For<IHubClients>();
        var proxy = Substitute.For<IClientProxy>();
        hubContext.Clients.Returns(clients);
        clients.Group(restaurantId.ToString()).Returns(proxy);
        var handler = new GroupDiningStageChangedNotificationHandler(hubContext);

        await handler.Handle(
            new GroupDiningStageChanged(restaurantId, 7, DiningStage.WaitingForFood, DiningVisit.Main, 120),
            CancellationToken.None);

        var expected = new GroupDiningStageChangedDto(7, DiningStageNames.WaitingForFood, DiningVisitNames.Main, 120);
        await proxy.Received(1).SendCoreAsync(
            ClientEventNames.GroupDiningStageChanged,
            Arg.Is<object?[]>(args => args.Length == 1 && Equals(args[0], expected)),
            Arg.Any<CancellationToken>());
    }
}
