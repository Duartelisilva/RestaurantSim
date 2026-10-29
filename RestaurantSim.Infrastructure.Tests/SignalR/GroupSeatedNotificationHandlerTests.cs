using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using RestaurantSim.Contracts.Events;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Infrastructure.SignalR;

namespace RestaurantSim.Infrastructure.Tests.SignalR;

public sealed class GroupSeatedNotificationHandlerTests
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
        var handler = new GroupSeatedNotificationHandler(hubContext);

        await handler.Handle(new GroupSeated(restaurantId, 7, 3, 120), CancellationToken.None);

        var expected = new GroupSeatedDto(7, 3, 120);
        await proxy.Received(1).SendCoreAsync(
            ClientEventNames.GroupSeated,
            Arg.Is<object?[]>(args => args.Length == 1 && Equals(args[0], expected)),
            Arg.Any<CancellationToken>());
    }
}
