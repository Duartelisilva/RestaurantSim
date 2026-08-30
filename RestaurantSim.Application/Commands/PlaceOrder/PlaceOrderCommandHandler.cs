using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.KitchenOps;

namespace RestaurantSim.Application.Commands.PlaceOrder;

// Now fully re-verifies phase-gating AND item validity before committing, using
// the exact same shared methods the validator calls (OrderPhaseTransition,
// PlaceOrderItemValidation) — matches SeatGroupCommandHandler's level of
// protection, with zero duplicated logic. Resolves the earlier trade-off where
// this handler trusted the pipeline without re-checking.
public sealed class PlaceOrderCommandHandler : IRequestHandler<PlaceOrderCommand, Result>
{
    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<Domain.RestaurantConfig> _presetRepository;
    private readonly IMenuRepository _menuRepository;
    private readonly IPublisher _publisher;

    public PlaceOrderCommandHandler(
        IRestaurantRepository repository,
        IPresetRepository<Domain.RestaurantConfig> presetRepository,
        IMenuRepository menuRepository,
        IPublisher publisher)
    {
        _repository = repository;
        _presetRepository = presetRepository;
        _menuRepository = menuRepository;
        _publisher = publisher;
    }

    public async Task<Result> Handle(PlaceOrderCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var group = restaurant.CustomerService.ActiveGroups.FirstOrDefault(g => g.GroupId == request.GroupId);
        if (group is null)
            return Result.Failure($"Group {request.GroupId} is not currently seated.");

        if (group.ClusterId is null)
            return Result.Failure($"Group {request.GroupId} is active but has no assigned table — data integrity issue.");

        var currentOrder = group.CurrentOrderId is null
            ? null
            : restaurant.KitchenOps.ActiveOrders.FirstOrDefault(o => o.OrderId == group.CurrentOrderId);

        if (!OrderPhaseTransition.IsValid(group, currentOrder, request.Phase))
            return Result.Failure($"Group {request.GroupId} cannot place a {request.Phase} order in its current course phase.");

        var config = await _presetRepository.GetAsync(restaurant.ConfigId, cancellationToken);
        if (config is null)
            return Result.Failure($"RestaurantConfig '{restaurant.ConfigId}' does not exist.");

        if (!await PlaceOrderItemValidation.AllItemsAreValidAsync(restaurant, config, group, request.Phase, request.Items, _menuRepository, cancellationToken))
            return Result.Failure("One or more ordered items are invalid for this group, phase, or the current meal period.");

        // Get-or-create the group's single running Order (entity doc §12 — one
        // Order per group, accumulating items across all phases, not one per phase).
        Order order;
        if (currentOrder is null)
        {
            order = new Order
            {
                OrderId = restaurant.KitchenOps.NextOrderId++,
                GroupId = group.GroupId,
                ClusterId = group.ClusterId.Value,
                Items = new List<OrderItem>(),
                CreatedAtTick = restaurant.SimulationTick
            };
            restaurant.KitchenOps.ActiveOrders.Add(order);
            group.CurrentOrderId = order.OrderId;
        }
        else
        {
            order = currentOrder;
        }

        var newItemIds = new List<int>();

        foreach (var itemRequest in request.Items)
        {
            var menuItem = await _menuRepository.GetByIdAsync(itemRequest.MenuItemId, cancellationToken);
            if (menuItem is null)
                return Result.Failure($"MenuItem {itemRequest.MenuItemId} does not exist.");

            var orderItem = new OrderItem
            {
                OrderItemId = restaurant.KitchenOps.NextOrderItemId++,
                OwnerId = itemRequest.CustomerId,
                MenuItemId = menuItem.MenuItemId,
                Phase = request.Phase,
                PriceAtOrder = menuItem.DefaultPrice, // player-set pricing not wired yet — see pending-doc-changes
                Status = OrderItemStatus.Queued
            };

            order.Items.Add(orderItem);
            newItemIds.Add(orderItem.OrderItemId);
        }

        group.CoursePhase = request.Phase switch
        {
            ItemPhase.Drinks when group.CoursePhase == CoursePhase.Seated => CoursePhase.Drinks,
            ItemPhase.EntryMain when group.CoursePhase == CoursePhase.Drinks => CoursePhase.EntryMain,
            ItemPhase.Dessert when group.CoursePhase == CoursePhase.EntryMain => CoursePhase.Dessert,
            _ => group.CoursePhase
        };

        await _repository.SaveAsync(restaurant, cancellationToken);

        await _publisher.Publish(
            new OrderPlaced(restaurant.RestaurantId, group.GroupId, order.OrderId, request.Phase, newItemIds, restaurant.SimulationTick),
            cancellationToken);

        return Result.Success();
    }
}
