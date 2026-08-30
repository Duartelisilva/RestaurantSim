using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain.KitchenOps;

namespace RestaurantSim.Application.Commands.PlaceOrder;

public sealed record PlaceOrderItemRequest(int CustomerId, int MenuItemId);

// Orders are taken all at once per phase per table (business logic §1.4) — one
// call places every item for the given Phase in a single batch. Drinks can be
// requested any time before the bill; EntryMain/Dessert each gate on the fixed
// sequence (must follow the prior phase) and can only be placed once per group.
public sealed record PlaceOrderCommand(
    Guid RestaurantId,
    int GroupId,
    ItemPhase Phase,
    IReadOnlyList<PlaceOrderItemRequest> Items) : IRequest<Result>, IRestaurantScopedRequest;
