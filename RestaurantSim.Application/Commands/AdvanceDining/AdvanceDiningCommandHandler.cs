using MediatR;
using RestaurantSim.Application.Commands.DeliverOrderItem;
using RestaurantSim.Application.Commands.LeaveTable;
using RestaurantSim.Application.Commands.PayBill;
using RestaurantSim.Application.Commands.PlaceOrder;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.KitchenOps;
using RestaurantSim.Domain.Scheduling;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Application.Commands.AdvanceDining;

// Mechanics doc §17 (draft). Runs inside the per-restaurant lock like every command.
//
// It acts through the existing PlaceOrder / DeliverOrderItem / PayBill / LeaveTable
// handlers, called DIRECTLY rather than through ISender: the lock is not re-entrant, so
// sending another restaurant-scoped command from inside this one would deadlock. Each of
// those handlers re-validates the state it needs (CLAUDE.md: a handler defends itself
// when invoked outside the pipeline), and decide + act + schedule all happen inside this
// one critical section, so "check all, then commit all" holds per group action.
public sealed class AdvanceDiningCommandHandler : IRequestHandler<AdvanceDiningCommand, Result>
{
    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;
    private readonly IMenuRepository _menuRepository;
    private readonly IRandomProvider _random;
    private readonly IRequestHandler<PlaceOrderCommand, Result> _placeOrder;
    private readonly IRequestHandler<DeliverOrderItemCommand, Result> _deliverOrderItem;
    private readonly IRequestHandler<PayBillCommand, Result> _payBill;
    private readonly IRequestHandler<LeaveTableCommand, Result> _leaveTable;

    public AdvanceDiningCommandHandler(
        IRestaurantRepository repository,
        IPresetRepository<RestaurantConfig> presetRepository,
        IMenuRepository menuRepository,
        IRandomProvider random,
        IRequestHandler<PlaceOrderCommand, Result> placeOrder,
        IRequestHandler<DeliverOrderItemCommand, Result> deliverOrderItem,
        IRequestHandler<PayBillCommand, Result> payBill,
        IRequestHandler<LeaveTableCommand, Result> leaveTable)
    {
        _repository = repository;
        _presetRepository = presetRepository;
        _menuRepository = menuRepository;
        _random = random;
        _placeOrder = placeOrder;
        _deliverOrderItem = deliverOrderItem;
        _payBill = payBill;
        _leaveTable = leaveTable;
    }

    public async Task<Result> Handle(AdvanceDiningCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        if (restaurant.CustomerService.ActiveGroups.Count == 0)
            return Result.Success();

        var config = await _presetRepository.GetAsync(restaurant.ConfigId, cancellationToken);
        if (config is null)
            return Result.Failure($"RestaurantConfig '{restaurant.ConfigId}' does not exist.");

        var menu = await _menuRepository.GetAllAsync(cancellationToken);
        var failures = new List<string>();

        // A copy: LeaveTable removes the group from ActiveGroups.
        foreach (var group in restaurant.CustomerService.ActiveGroups.ToList())
            await AdvanceGroupAsync(restaurant, config, menu, group, failures, cancellationToken);

        await _repository.SaveAsync(restaurant, cancellationToken);

        return failures.Count == 0 ? Result.Success() : Result.Failure(string.Join(" | ", failures));
    }

    private async Task AdvanceGroupAsync(
        Restaurant restaurant, RestaurantConfig config, IReadOnlyList<MenuItem> menu,
        CustomerGroup group, List<string> failures, CancellationToken cancellationToken)
    {
        var now = restaurant.SimulationTick;

        if (group.LifecycleState == GroupLifecycleState.Leaving)
        {
            if (now >= group.NextAutoVisitTick)
            {
                var left = await _leaveTable.Handle(new LeaveTableCommand(restaurant.RestaurantId, group.GroupId), cancellationToken);
                if (left.IsFailure)
                    failures.Add(left.Error ?? "LeaveTable failed.");
            }

            return;
        }

        if (group.LifecycleState != GroupLifecycleState.Seated)
            return;

        var order = group.CurrentOrderId is int orderId
            ? restaurant.KitchenOps.ActiveOrders.FirstOrDefault(o => o.OrderId == orderId)
            : null;

        if (order is not null)
            await DeliverDueItemsAsync(restaurant, config, menu, order, cancellationToken);

        if (group.PhaseWaitState == GroupPhaseWaitState.Serving)
        {
            ScheduleNextVisitIfCourseComplete(group, order, config, now);
        }
        else if (now >= group.NextAutoVisitTick)
        {
            // AutoVisitPending or RecheckScheduled. (AwaitingDecision is never stored: the
            // decision is made and resolved inside this same call.)
            await ResolveVisitAsync(restaurant, config, menu, group, order, failures, cancellationToken);
        }
    }

    // ---------- delivery ----------

    // Pickup items (drinks) are attempted every tick; DeliverOrderItem's own fetch-time
    // gate rejects them until they are due, so that rule is not duplicated here. Station
    // items are the kitchen's business until they are Ready.
    private async Task DeliverDueItemsAsync(
        Restaurant restaurant, RestaurantConfig config, IReadOnlyList<MenuItem> menu, Order order, CancellationToken cancellationToken)
    {
        foreach (var item in order.Items.Where(i => i.Status is OrderItemStatus.Ready or OrderItemStatus.Queued).ToList())
        {
            if (item.Status == OrderItemStatus.Queued)
            {
                var menuItem = menu.FirstOrDefault(m => m.MenuItemId == item.MenuItemId);
                if (menuItem?.RequiredStationType is not null)
                    continue;
            }

            // Failures here are expected (fetch time not elapsed yet, no idle waiter) and
            // simply retried next tick — deliberately not reported.
            var delivered = await _deliverOrderItem.Handle(
                new DeliverOrderItemCommand(restaurant.RestaurantId, item.OrderItemId), cancellationToken);

            if (delivered.IsSuccess)
            {
                var range = config.ServiceFlow.GetEatingDuration(item.Phase);
                item.ConsumedByTick = restaurant.SimulationTick
                    + ServiceFlowTimings.RollEatingTicks(range, config.TicksPerRealSecond, _random);
            }
        }
    }

    // ---------- scheduling the next visit ----------

    // The group is waiting on food / eating. NextVisit says which visit follows the
    // current course; schedule it once that course is complete.
    private static void ScheduleNextVisitIfCourseComplete(CustomerGroup group, Order? order, RestaurantConfig config, long now)
    {
        IReadOnlyList<OrderItem> items = order?.Items ?? new List<OrderItem>();
        var flow = config.ServiceFlow;

        var seconds = group.NextVisit switch
        {
            // People order the main while sipping their drinks: waits for drinks SERVED.
            DiningVisit.Main when AllServed(items, ItemPhase.Drinks) => flow.DrinksToMainDelaySeconds,
            DiningVisit.Refill when AllServed(items, ItemPhase.EntryMain) => flow.MainToRefillVisitDelaySeconds,
            DiningVisit.Dessert when AllConsumed(items, ItemPhase.EntryMain, now) => flow.MainToDessertDelaySeconds,
            DiningVisit.Bill when AllConsumed(items, ItemPhase.Dessert, now) => flow.FinalCourseToBillDelaySeconds,
            _ => (float?)null
        };

        if (seconds is float delay)
            ScheduleVisit(group, group.NextVisit, delay, config, now);
    }

    private static void ScheduleVisit(CustomerGroup group, DiningVisit visit, float delaySeconds, RestaurantConfig config, long now)
    {
        group.NextVisit = visit;
        group.PhaseWaitState = GroupPhaseWaitState.AutoVisitPending;
        group.VisitAttempt = 0;
        group.NextAutoVisitTick = now + TickRates.SecondsToTicks(delaySeconds, config.TicksPerRealSecond);
    }

    // ---------- resolving a due visit ----------

    private async Task ResolveVisitAsync(
        Restaurant restaurant, RestaurantConfig config, IReadOnlyList<MenuItem> menu,
        CustomerGroup group, Order? order, List<string> failures, CancellationToken cancellationToken)
    {
        // No idle waiter: the visit simply stays due (and counts as overdue — mechanics §17).
        if (!restaurant.StaffRoster.Staff.Any(s => s.Role == StaffRole.Waiter && s.State == StaffState.Idle))
            return;

        // A group seated before closing is still served out, under its own period.
        var period = MealPeriodCalculator.FindServicePeriod(config.ActiveMealPeriods, config.MinutesPerTick, restaurant.SimulationTick);
        if (period is null)
            return;

        var context = new OrderContext(menu, config.MenuItemIds, period.MealPeriodId, config.ServiceFlow);

        switch (group.NextVisit)
        {
            case DiningVisit.Drinks:
                await TakeOrderAsync(restaurant, config, context, group, ItemPhase.Drinks, DiningVisit.Main, failures, cancellationToken);
                break;
            case DiningVisit.Main:
                await TakeOrderAsync(restaurant, config, context, group, ItemPhase.EntryMain, DiningVisit.Refill, failures, cancellationToken);
                break;
            case DiningVisit.Dessert:
                await TakeOrderAsync(restaurant, config, context, group, ItemPhase.Dessert, DiningVisit.Bill, failures, cancellationToken);
                break;
            case DiningVisit.Refill:
                await TakeRefillsAsync(restaurant, context, group, order, failures, cancellationToken);
                break;
            case DiningVisit.Bill:
                await CollectPaymentAsync(restaurant, config, group, failures, cancellationToken);
                break;
        }
    }

    private async Task TakeOrderAsync(
        Restaurant restaurant, RestaurantConfig config, OrderContext context, CustomerGroup group,
        ItemPhase phase, DiningVisit followedBy, List<string> failures, CancellationToken cancellationToken)
    {
        var now = restaurant.SimulationTick;
        var flow = config.ServiceFlow;

        // Not ready yet? Recheck later, with better odds each time.
        var readyChance = ServiceFlowTimings.ReadyToOrderChance(flow.GetReadyToOrderChance(phase), group.VisitAttempt + 1);
        if (readyChance < 1f && _random.NextDouble() >= readyChance)
        {
            group.VisitAttempt++;
            group.PhaseWaitState = GroupPhaseWaitState.RecheckScheduled;
            group.NextAutoVisitTick = now + TickRates.SecondsToTicks(flow.RecheckDelaySeconds, config.TicksPerRealSecond);
            return;
        }

        // Members who are away are still included — assumed to have decided already.
        var choices = OrderGenerator.Generate(phase, group.Members, context, _random);

        // Dessert is optional: nobody wants one -> straight to the bill.
        if (phase == ItemPhase.Dessert && choices.Count == 0)
        {
            ScheduleVisit(group, DiningVisit.Bill, flow.FinalCourseToBillDelaySeconds, config, now);
            return;
        }

        var placed = await _placeOrder.Handle(
            new PlaceOrderCommand(restaurant.RestaurantId, group.GroupId, phase, ToRequests(choices)), cancellationToken);

        if (placed.IsFailure)
        {
            failures.Add(placed.Error ?? $"PlaceOrder({phase}) failed.");
            return; // state untouched: the visit stays due and is retried next tick
        }

        group.PhaseWaitState = GroupPhaseWaitState.Serving;
        group.NextVisit = followedBy;
        group.VisitAttempt = 0;
    }

    private async Task TakeRefillsAsync(
        Restaurant restaurant, OrderContext context, CustomerGroup group, Order? order,
        List<string> failures, CancellationToken cancellationToken)
    {
        var now = restaurant.SimulationTick;

        // Only people whose first drink is already finished can ask for another.
        var eligible = group.Members.Where(m => HasFinishedDrinks(order, m.CustomerId, now)).ToList();
        var refills = OrderGenerator.GenerateRefills(eligible, context, _random);

        if (refills.Count > 0)
        {
            var placed = await _placeOrder.Handle(
                new PlaceOrderCommand(restaurant.RestaurantId, group.GroupId, ItemPhase.Drinks, ToRequests(refills)), cancellationToken);

            if (placed.IsFailure)
            {
                failures.Add(placed.Error ?? "PlaceOrder(Drinks refill) failed.");
                return;
            }
        }

        group.PhaseWaitState = GroupPhaseWaitState.Serving;
        group.NextVisit = DiningVisit.Dessert;
        group.VisitAttempt = 0;
    }

    private async Task CollectPaymentAsync(
        Restaurant restaurant, RestaurantConfig config, CustomerGroup group,
        List<string> failures, CancellationToken cancellationToken)
    {
        var now = restaurant.SimulationTick;

        var paid = await _payBill.Handle(new PayBillCommand(restaurant.RestaurantId, group.GroupId), cancellationToken);
        if (paid.IsFailure)
        {
            failures.Add(paid.Error ?? "PayBill failed.");
            group.NextAutoVisitTick = now + 1; // try again next tick
            return;
        }

        // PayBill moved the group to Leaving; it stands up after a short pause.
        group.PhaseWaitState = GroupPhaseWaitState.Serving;
        group.NextAutoVisitTick = now + TickRates.SecondsToTicks(config.ServiceFlow.PaymentToLeaveSeconds, config.TicksPerRealSecond);
    }

    // ---------- helpers ----------

    private static IReadOnlyList<PlaceOrderItemRequest> ToRequests(IReadOnlyList<OrderChoice> choices) =>
        choices.Select(c => new PlaceOrderItemRequest(c.CustomerId, c.MenuItemId)).ToList();

    private static bool AllServed(IReadOnlyList<OrderItem> items, ItemPhase phase) =>
        items.Where(i => i.Phase == phase).All(i => i.Status == OrderItemStatus.Served);

    private static bool AllConsumed(IReadOnlyList<OrderItem> items, ItemPhase phase, long now) =>
        items.Where(i => i.Phase == phase).All(i => IsConsumed(i, now));

    // A served item with no ConsumedByTick (served by some other path) counts as eaten
    // rather than stalling the group.
    private static bool IsConsumed(OrderItem item, long now) =>
        item.Status == OrderItemStatus.Served && (item.ConsumedByTick is null || item.ConsumedByTick <= now);

    private static bool HasFinishedDrinks(Order? order, int customerId, long now)
    {
        var drinks = order?.Items.Where(i => i.OwnerId == customerId && i.Phase == ItemPhase.Drinks).ToList();
        return drinks is { Count: > 0 } && drinks.All(i => IsConsumed(i, now));
    }
}
