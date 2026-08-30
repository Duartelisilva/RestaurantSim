using FluentValidation;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.KitchenOps;

namespace RestaurantSim.Application.Commands.PlaceOrder;

public sealed class PlaceOrderCommandValidator : AbstractValidator<PlaceOrderCommand>
{
    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;
    private readonly IMenuRepository _menuRepository;

    public PlaceOrderCommandValidator(
        IRestaurantRepository repository,
        IPresetRepository<RestaurantConfig> presetRepository,
        IMenuRepository menuRepository)
    {
        _repository = repository;
        _presetRepository = presetRepository;
        _menuRepository = menuRepository;

        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleFor(x => x.GroupId).GreaterThan(0);
        RuleFor(x => x.Items).NotEmpty();

        RuleFor(x => x)
            .MustAsync(GroupIsActiveAsync)
            .WithMessage(x => $"Group {x.GroupId} is not currently seated.");

        RuleFor(x => x)
            .MustAsync(PhaseTransitionIsValidAsync)
            .WithMessage(x => $"Group {x.GroupId} cannot place a {x.Phase} order in its current course phase.");

        RuleFor(x => x)
            .MustAsync(AllItemsAreValidAsync)
            .WithMessage("One or more ordered items are invalid for this group, phase, or the current meal period.");
    }

    private async Task<bool> GroupIsActiveAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(command.RestaurantId, cancellationToken);
        return restaurant?.CustomerService.ActiveGroups.Any(g => g.GroupId == command.GroupId) ?? false;
    }

    private async Task<bool> PhaseTransitionIsValidAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(command.RestaurantId, cancellationToken);
        var group = restaurant?.CustomerService.ActiveGroups.FirstOrDefault(g => g.GroupId == command.GroupId);
        if (group is null || restaurant is null) return false;

        var currentOrder = group.CurrentOrderId is null
            ? null
            : restaurant.KitchenOps.ActiveOrders.FirstOrDefault(o => o.OrderId == group.CurrentOrderId);

        // Same method the handler calls before committing — single source of truth.
        return OrderPhaseTransition.IsValid(group, currentOrder, command.Phase);
    }

    private async Task<bool> AllItemsAreValidAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(command.RestaurantId, cancellationToken);
        if (restaurant is null) return false;

        var group = restaurant.CustomerService.ActiveGroups.FirstOrDefault(g => g.GroupId == command.GroupId);
        if (group is null) return false;

        var config = await _presetRepository.GetAsync(restaurant.ConfigId, cancellationToken);
        if (config is null) return false;

        // Same method the handler calls before committing — single source of truth.
        return await PlaceOrderItemValidation.AllItemsAreValidAsync(
            restaurant, config, group, command.Phase, command.Items, _menuRepository, cancellationToken);
    }
}
