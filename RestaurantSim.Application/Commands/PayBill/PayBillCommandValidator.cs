using FluentValidation;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.KitchenOps;

namespace RestaurantSim.Application.Commands.PayBill;

public sealed class PayBillCommandValidator : AbstractValidator<PayBillCommand>
{
    private readonly IRestaurantRepository _repository;

    public PayBillCommandValidator(IRestaurantRepository repository)
    {
        _repository = repository;

        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleFor(x => x.GroupId).GreaterThan(0);

        RuleFor(x => x)
            .MustAsync(GroupCanPayAsync)
            .WithMessage(x => $"Group {x.GroupId} cannot pay its bill right now (not active, no order, or items not yet served).");
    }

    private async Task<bool> GroupCanPayAsync(PayBillCommand command, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(command.RestaurantId, cancellationToken);
        var group = restaurant?.CustomerService.ActiveGroups.FirstOrDefault(g => g.GroupId == command.GroupId);
        if (group is null || restaurant is null) return false;

        var order = group.CurrentOrderId is null
            ? null
            : restaurant.KitchenOps.ActiveOrders.FirstOrDefault(o => o.OrderId == group.CurrentOrderId);

        // Same method the handler calls before committing — single source of truth.
        return OrderPaymentEligibility.IsEligible(group, order);
    }
}
