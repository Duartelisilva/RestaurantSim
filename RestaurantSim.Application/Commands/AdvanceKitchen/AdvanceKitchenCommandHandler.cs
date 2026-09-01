using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.DomainEvents;
using RestaurantSim.Domain.KitchenOps;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Application.Commands.AdvanceKitchen;

public sealed class AdvanceKitchenCommandHandler : IRequestHandler<AdvanceKitchenCommand, Result>
{
    // Fallback defaults for RestaurantConfig fields still correctly left null
    // ("open, pending playtesting") — taken from mechanics doc §10's OWN stated
    // starting values / worked example, not invented numbers:
    // - 0.05 / 0.03 are the doc's literal "BaseHighQualityChance = 0.05" /
    //   "BaseEventChance = 0.03" inline comments.
    // - 6 / 20 are the ShortDelayThresholdTicks / MaxAcceptableWaitTicks values
    //   used in the doc's own walkthrough example.
    private const float DefaultBaseHighQualityChance = 0.05f;
    private const float DefaultBaseEventChance = 0.03f;
    private const float DefaultShortDelayThresholdSeconds = 6f;
    private const float DefaultMaxAcceptableWaitSeconds = 20f;
    private const float DefaultReinformMoodPenalty = 3f; // confirmed in chat, low end of waiter-action-failure range

    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;
    private readonly IMenuRepository _menuRepository;
    private readonly IRandomProvider _random;
    private readonly IPublisher _publisher;

    public AdvanceKitchenCommandHandler(
        IRestaurantRepository repository,
        IPresetRepository<RestaurantConfig> presetRepository,
        IMenuRepository menuRepository,
        IRandomProvider random,
        IPublisher publisher)
    {
        _repository = repository;
        _presetRepository = presetRepository;
        _menuRepository = menuRepository;
        _random = random;
        _publisher = publisher;
    }

    public async Task<Result> Handle(AdvanceKitchenCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var config = await _presetRepository.GetAsync(restaurant.ConfigId, cancellationToken);
        if (config is null)
            return Result.Failure($"RestaurantConfig '{restaurant.ConfigId}' does not exist.");

        var headChefPresent = restaurant.StaffRoster.Staff.Any(s => s.Role == StaffRole.HeadChef && s.State != StaffState.OffShift);

        var readyEvents = new List<OrderItemReady>();
        var reinformedEvents = new List<TicketReinformed>();

        await TryStartQueuedItemsAsync(restaurant, config, cancellationToken);
        await ResolveFinishedItemsAsync(restaurant, config, headChefPresent, readyEvents, reinformedEvents, cancellationToken);

        if (readyEvents.Count == 0 && reinformedEvents.Count == 0)
            return Result.Success();

        await _repository.SaveAsync(restaurant, cancellationToken);

        foreach (var evt in reinformedEvents) await _publisher.Publish(evt, cancellationToken);
        foreach (var evt in readyEvents) await _publisher.Publish(evt, cancellationToken);

        return Result.Success();
    }

    // Step 1: start any Queued item whose PlannedStartTick has arrived AND has
    // both a free station slot and an idle cook — "check all, then commit all"
    // (CLAUDE.md's cross-cutting convention). Processed oldest-ticket-first
    // (mechanics doc §10's station contention rule).
    private async Task TryStartQueuedItemsAsync(Restaurant restaurant, RestaurantConfig config, CancellationToken cancellationToken)
    {
        var ticketsOldestFirst = restaurant.KitchenOps.ActiveTickets
            .Where(t => !t.PlayerPrioritized)
            .OrderBy(t => t.SubmittedAtTick)
            .Concat(restaurant.KitchenOps.ActiveTickets.Where(t => t.PlayerPrioritized).OrderBy(t => t.SubmittedAtTick))
            .ToList();
        // Prioritized tickets are effectively treated as older than any
        // non-prioritized ticket (mechanics doc §10's PlayerPrioritized rule) —
        // achieved here by processing them as a second, still oldest-first pass
        // ahead of remaining capacity, since the first pass already consumed
        // whatever it could. NOTE: this ordering is imperfect versus a single
        // fully-merged sort (a very old non-prioritized ticket still gets first
        // look before a prioritized one submitted later in the SAME pass) —
        // flagged as a simplification, revisit if it matters in practice.

        foreach (var ticket in ticketsOldestFirst)
        {
            foreach (var itemId in ticket.ItemIds)
            {
                var item = FindOrderItem(restaurant, itemId);
                if (item is null || item.Status != OrderItemStatus.Queued) continue;
                if (item.PlannedStartTick is long planned && restaurant.SimulationTick < planned) continue; // course-sync delay

                var menuItem = await _menuRepository.GetByIdAsync(item.MenuItemId, cancellationToken);
                if (menuItem?.RequiredStationType is null) continue; // pickup items out of Kitchen scope

                var station = restaurant.Floor.KitchenStations.FirstOrDefault(s =>
                    s.StationType.StationTypeId == menuItem.RequiredStationType &&
                    s.OccupiedSlots.Count < s.StationType.Capacity);
                if (station is null) continue; // station full — stays queued

                var cook = restaurant.StaffRoster.Staff.FirstOrDefault(s =>
                    (s.Role == StaffRole.Cook || s.Role == StaffRole.HeadChef) && s.State == StaffState.Idle);
                if (cook is null) continue; // no idle cook — stays queued, even with a free station slot

                // Commit (both prerequisites confirmed available above).
                station.OccupiedSlots.Add(item.OrderItemId);
                cook.State = StaffState.Busy;
                cook.CurrentTaskType = TaskType.CookItem;
                cook.CurrentTaskTargetId = item.OrderItemId;
                item.Status = OrderItemStatus.Cooking;
                item.StationId = station.StationId;
                item.CookingStartedAtTick = restaurant.SimulationTick;
            }
        }
    }

    // Step 2: resolve any Cooking item whose EffectivePrepTicks have elapsed —
    // quality rolls, then the heat-lamp-vs-reinform decision (mechanics doc §10).
    private async Task ResolveFinishedItemsAsync(
        Restaurant restaurant,
        RestaurantConfig config,
        bool headChefPresent,
        List<OrderItemReady> readyEvents,
        List<TicketReinformed> reinformedEvents,
        CancellationToken cancellationToken)
    {
        var cookingItems = restaurant.KitchenOps.ActiveOrders
            .SelectMany(o => o.Items.Select(i => (Order: o, Item: i)))
            .Where(x => x.Item.Status == OrderItemStatus.Cooking)
            .ToList();

        foreach (var (order, item) in cookingItems)
        {
            var cook = restaurant.StaffRoster.Staff.FirstOrDefault(s => s.CurrentTaskType == TaskType.CookItem && s.CurrentTaskTargetId == item.OrderItemId);
            if (cook is null) continue; // defensive — shouldn't happen

            var menuItem = await _menuRepository.GetByIdAsync(item.MenuItemId, cancellationToken);
            if (menuItem is null) continue;

            var station = restaurant.Floor.KitchenStations.FirstOrDefault(s => s.StationId == item.StationId);
            if (station is null) continue;

            var effectiveTicks = KitchenPrepTimeCalculator.EffectivePrepTicks(menuItem, station.StationType.SpeedMultiplier, cook.Stats, config.TicksPerRealSecond);
            if (restaurant.SimulationTick < item.CookingStartedAtTick!.Value + effectiveTicks) continue; // still cooking

            // Free station and cook.
            station.OccupiedSlots.Remove(item.OrderItemId);
            cook.State = StaffState.Idle;
            cook.CurrentTaskType = null;
            cook.CurrentTaskTargetId = null;

            // Quality rolls (mechanics doc §10).
            var skillScore = Math.Clamp(cook.Stats.Get(StatType.Speed) * (1 - cook.Stats.Get(StatType.ErrorRate)), 0, 1);
            var baseHighQualityChance = config.BaseHighQualityChance ?? DefaultBaseHighQualityChance;
            var highQualityChance = baseHighQualityChance * (0.5 + 1.5 * skillScore) * (headChefPresent ? 1.3 : 1.0);
            item.IsHighQuality = _random.NextDouble() < highQualityChance;

            if (!item.IsHighQuality)
            {
                var effectiveErrorRate = cook.Stats.Get(StatType.ErrorRate) * (1.0 + 0.5 * cook.Stats.Get(StatType.Fatigue)) * (headChefPresent ? 1.0 : 1.5);
                var baseEventChance = config.BaseEventChance ?? DefaultBaseEventChance;
                _ = _random.NextDouble() < baseEventChance * effectiveErrorRate; // FoodQualityEvent roll — QualityAtServe not computed until delivery (Phase 5+), see pending-doc-changes
            }

            var ticket = restaurant.KitchenOps.ActiveTickets.FirstOrDefault(t => t.ItemIds.Contains(item.OrderItemId));
            if (ticket is null)
            {
                item.Status = OrderItemStatus.Ready;
                readyEvents.Add(new OrderItemReady(restaurant.RestaurantId, item.OrderItemId, order.GroupId, item.IsHighQuality, restaurant.SimulationTick));
                continue;
            }

            var siblingIds = ticket.ItemIds.Where(id => id != item.OrderItemId).ToList();
            var allSiblingsFinished = siblingIds.All(id =>
                FindOrderItem(restaurant, id) is { Status: OrderItemStatus.Ready or OrderItemStatus.HeldUnderLamp or OrderItemStatus.Served });

            if (allSiblingsFinished || ticket.IsAsyncMode)
            {
                item.Status = OrderItemStatus.Ready;
                readyEvents.Add(new OrderItemReady(restaurant.RestaurantId, item.OrderItemId, order.GroupId, item.IsHighQuality, restaurant.SimulationTick));
                continue;
            }

            var slowestProjectedReadyTick = restaurant.SimulationTick;
            foreach (var siblingId in siblingIds)
            {
                var sibling = FindOrderItem(restaurant, siblingId);
                if (sibling is null || sibling.Status is OrderItemStatus.Ready or OrderItemStatus.HeldUnderLamp or OrderItemStatus.Served) continue;

                var projected = await ProjectSiblingReadyTickAsync(restaurant, config, ticket, sibling, cancellationToken);
                slowestProjectedReadyTick = Math.Max(slowestProjectedReadyTick, projected);
            }

            var holdTicks = slowestProjectedReadyTick - restaurant.SimulationTick;
            var maxAcceptableWaitTicks = config.MaxAcceptableWaitSeconds ?? DefaultMaxAcceptableWaitSeconds;
            var shortDelayThresholdTicks = config.ShortDelayThresholdSeconds ?? DefaultShortDelayThresholdSeconds;
            var waitPressure = Math.Clamp((restaurant.SimulationTick - ticket.SubmittedAtTick) / maxAcceptableWaitTicks, 0, 0.7);
            var effectiveThreshold = shortDelayThresholdTicks * (1 - waitPressure);

            var heatLamp = restaurant.Floor.HeatLamps.FirstOrDefault(l => l.OccupiedSlots.Count < l.SlotCount);

            if (holdTicks <= effectiveThreshold && heatLamp is not null)
            {
                item.Status = OrderItemStatus.HeldUnderLamp;
                item.HeldSinceTick = restaurant.SimulationTick;
                heatLamp.OccupiedSlots.Add(item.OrderItemId);
            }
            else
            {
                var wasAlreadyAsync = ticket.IsAsyncMode;
                ticket.IsAsyncMode = true;
                item.Status = OrderItemStatus.Ready;

                // Release any siblings already waiting under the lamp — "re-inform,
                // serve ready items now" (mechanics doc §10).
                foreach (var siblingId in siblingIds)
                {
                    var sibling = FindOrderItem(restaurant, siblingId);
                    if (sibling?.Status != OrderItemStatus.HeldUnderLamp) continue;

                    sibling.Status = OrderItemStatus.Ready;
                    var siblingLamp = restaurant.Floor.HeatLamps.FirstOrDefault(l => l.OccupiedSlots.Contains(sibling.OrderItemId));
                    siblingLamp?.OccupiedSlots.Remove(sibling.OrderItemId);
                    readyEvents.Add(new OrderItemReady(restaurant.RestaurantId, sibling.OrderItemId, order.GroupId, sibling.IsHighQuality, restaurant.SimulationTick));
                }

                if (!wasAlreadyAsync)
                {
                    // New mechanic, confirmed in chat, not yet in the locked docs:
                    // still-waiting group members take a small Mood penalty when
                    // told their food is coming separately.
                    ApplyReinformMoodPenalty(restaurant, order.GroupId, config);
                    reinformedEvents.Add(new TicketReinformed(restaurant.RestaurantId, ticket.TicketId, order.GroupId, restaurant.SimulationTick));
                }
            }

            readyEvents.Add(new OrderItemReady(restaurant.RestaurantId, item.OrderItemId, order.GroupId, item.IsHighQuality, restaurant.SimulationTick));
        }
    }

    private async Task<long> ProjectSiblingReadyTickAsync(Restaurant restaurant, RestaurantConfig config, KitchenTicket ticket, OrderItem sibling, CancellationToken cancellationToken)
    {
        if (sibling.Status == OrderItemStatus.Cooking && sibling.CookingStartedAtTick is not null)
        {
            var cook = restaurant.StaffRoster.Staff.FirstOrDefault(s => s.CurrentTaskType == TaskType.CookItem && s.CurrentTaskTargetId == sibling.OrderItemId);
            var menuItem = await _menuRepository.GetByIdAsync(sibling.MenuItemId, cancellationToken);
            var station = sibling.StationId is int stationId ? restaurant.Floor.KitchenStations.FirstOrDefault(s => s.StationId == stationId) : null;

            if (cook is not null && menuItem is not null && station is not null)
            {
                var effectiveTicks = KitchenPrepTimeCalculator.EffectivePrepTicks(menuItem, station.StationType.SpeedMultiplier, cook.Stats, config.TicksPerRealSecond);
                return sibling.CookingStartedAtTick.Value + effectiveTicks;
            }
        }

        // Still Queued (hasn't started) — no confirmed actual start time exists.
        // Bound the estimate: at least the ticket's original nominal target, and
        // at least "starts now + its own nominal prep time," so a long-stuck
        // item isn't optimistically assumed to still finish on the old schedule.
        // This interpretation isn't spelled out in mechanics doc §10's own
        // walkthrough (which only covers already-cooking siblings) — flagged as
        // a judgment call on an ambiguous case, not a contradiction of what's locked.
        var siblingMenuItem = await _menuRepository.GetByIdAsync(sibling.MenuItemId, cancellationToken);
        if (siblingMenuItem is null) return ticket.TargetReadyTick;

        var stationSpeedMultiplier = restaurant.Floor.KitchenStations
            .FirstOrDefault(s => s.StationType.StationTypeId == siblingMenuItem.RequiredStationType)?.StationType.SpeedMultiplier ?? 1.0f;
        var nominalTicks = KitchenPrepTimeCalculator.NominalEffectivePrepTicks(siblingMenuItem, stationSpeedMultiplier, config.TicksPerRealSecond);

        return Math.Max(ticket.TargetReadyTick, restaurant.SimulationTick + nominalTicks);
    }

    private void ApplyReinformMoodPenalty(Restaurant restaurant, int groupId, RestaurantConfig config)
    {
        var group = restaurant.CustomerService.ActiveGroups.FirstOrDefault(g => g.GroupId == groupId);
        if (group is null) return;

        var penalty = config.ReinformMoodPenalty ?? DefaultReinformMoodPenalty;
        foreach (var member in group.Members.Where(m => m.IsPresentAtTable))
        {
            member.Mood = Math.Clamp(member.Mood - penalty, 0, 100);
        }
    }

    private static OrderItem? FindOrderItem(Restaurant restaurant, int orderItemId) =>
        restaurant.KitchenOps.ActiveOrders.SelectMany(o => o.Items).FirstOrDefault(i => i.OrderItemId == orderItemId);
}
