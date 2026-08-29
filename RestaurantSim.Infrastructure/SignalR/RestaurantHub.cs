using Microsoft.AspNetCore.SignalR;

namespace RestaurantSim.Infrastructure.SignalR;

// No hub methods yet — client-invoked one-shot requests (initial state, preset
// lists) are added as this grows (architecture doc §3). For now this exists
// purely as a broadcast target for domain-event -> SignalR translation.
public sealed class RestaurantHub : Hub
{
}
