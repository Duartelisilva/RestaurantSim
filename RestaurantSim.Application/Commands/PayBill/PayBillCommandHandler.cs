using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.KitchenOps;

namespace RestaurantSim.Application.Commands.PayBill;

public sealed class PayBillCommandHandler : IRequestHandler<PayBillCommand, Result>
{
    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;
    private readonly IPublisher _publisher;

    public PayBillCommandHandler(IRestaurantRepository repository, IPresetRepository<RestaurantConfig> presetRepository, IPublisher publisher)
    {
        _repository = repository;
        _presetRepository = presetRepository;
        _publisher = publisher;
    }

    public async Task<Result> Handle(PayBillCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var group = restaurant.CustomerService.ActiveGroups.FirstOrDefault(g => g.GroupId == request.GroupId);
        if (group is null)
            return Result.Failure($"Group {request.GroupId} is not currently seated.");

        var order = group.CurrentOrderId is null
            ? null
            : restaurant.KitchenOps.ActiveOrders.FirstOrDefault(o => o.OrderId == group.CurrentOrderId);

        if (!OrderPaymentEligibility.IsEligible(group, order))
            return Result.Failure($"Group {request.GroupId} cannot pay its bill right now.");

        var config = await _presetRepository.GetAsync(restaurant.ConfigId, cancellationToken);
        if (config is null)
            return Result.Failure($"RestaurantConfig '{restaurant.ConfigId}' does not exist.");

        // PayerId defaults to the first adult member at generation time
        // (ArrivalGroupGenerator) — defensive fallback here in case it's ever
        // unset for a group constructed some other way.
        var payerId = group.PayerId ?? group.Members.First(m => m.IsAdult).CustomerId;
        var payer = group.Members.First(m => m.CustomerId == payerId);

        // Interpretation call, not explicitly disambiguated in the docs: "Mood
        // sampled at payment time" (business logic §1.1/mechanics doc §6) is
        // read here as the PAYER's own Mood/SpendingTendency, not the group
        // average — whoever is footing the bill is the one deciding the tip.
        // Flagged in pending-doc-changes for confirmation.
        var spendingModifier = payer.SpendingTendency switch
        {
            SpendingTendency.Budget => 0.85,
            SpendingTendency.Generous => 1.15,
            _ => 1.0
        };

        var tipPercentage = Math.Clamp(
            config.TipBaseRate * Math.Pow(payer.Mood / 100.0, config.TipMoodExponent) * spendingModifier,
            0, 60);

        var orderTotal = order!.Items.Sum(i => i.PriceAtOrder);
        var tipAmount = (decimal)(tipPercentage / 100.0) * orderTotal;

        // Revenue only — tip feeds staff Morale, not restaurant Cash (business
        // logic §4). Morale mutation itself isn't performed here; see BillPaid's
        // own comment on why (no tip->Morale formula exists yet anywhere).
        restaurant.Cash += orderTotal;

        group.CoursePhase = CoursePhase.Done;
        group.LifecycleState = GroupLifecycleState.Leaving;

        await _repository.SaveAsync(restaurant, cancellationToken);

        await _publisher.Publish(
            new BillPaid(restaurant.RestaurantId, group.GroupId, order.OrderId, orderTotal, tipAmount, restaurant.Cash, restaurant.SimulationTick),
            cancellationToken);

        return Result.Success();
    }
}
