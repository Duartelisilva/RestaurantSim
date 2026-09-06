using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.KitchenOps;
using RestaurantSim.Domain.Scheduling;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Application.Commands.AdvanceStaffShift;

public sealed class AdvanceStaffShiftCommandHandler : IRequestHandler<AdvanceStaffShiftCommand, Result>
{
    // No doc-suggested starting values exist for these anywhere (unlike
    // Kitchen's, which had worked examples). Derived from a stated design
    // intent instead of guessed: "a staff member should reach roughly full
    // Fatigue (1.0) by the end of a typical 12-hour shift" ->
    // 1 / (12 hours x 3600 sec) ticks at TicksPerRealSecond=1.
    private const float DefaultBaseFatigueAccrualRate = 1f / (12f * 3600f);
    private const float DefaultOvertimeFatigueMultiplier = 2.0f; // confirmed in chat: overtime is more tiring, not less

    private readonly IRestaurantRepository _repository;
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;

    public AdvanceStaffShiftCommandHandler(IRestaurantRepository repository, IPresetRepository<RestaurantConfig> presetRepository)
    {
        _repository = repository;
        _presetRepository = presetRepository;
    }

    public async Task<Result> Handle(AdvanceStaffShiftCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var config = await _presetRepository.GetAsync(restaurant.ConfigId, cancellationToken);
        if (config is null)
            return Result.Failure($"RestaurantConfig '{restaurant.ConfigId}' does not exist.");

        var baseFatigueAccrualRate = config.BaseFatigueAccrualRate ?? DefaultBaseFatigueAccrualRate;
        var overtimeFatigueMultiplier = config.OvertimeFatigueMultiplier ?? DefaultOvertimeFatigueMultiplier;
        var minutesIntoDay = MealPeriodCalculator.MinutesIntoDay(config.MinutesPerTick, restaurant.SimulationTick);

        foreach (var staff in restaurant.StaffRoster.Staff)
        {
            var (shiftStart, shiftEnd) = ComputeShiftWindow(staff, config);
            var isWithinShift = minutesIntoDay >= shiftStart && minutesIntoDay < shiftEnd;
            var isHeldOvertime = false;

            if (isWithinShift && staff.State == StaffState.OffShift)
            {
                // Clock in. Fatigue is NOT reset here — it should already be 0
                // from the reset that happened at the previous clock-out
                // (entity doc §13a: reset happens ON the transition INTO
                // OffShift, not out of it).
                staff.State = StaffState.Idle;
            }
            else if (!isWithinShift && staff.State == StaffState.Idle)
            {
                // Mid-task (Busy) staff finish their current task before
                // clocking out (already-locked rule, mechanics doc §13's
                // treatment extended to shifts) — only Idle staff are
                // considered for clock-out here.
                if (IsRoleCovered(restaurant, staff))
                {
                    staff.State = StaffState.OffShift;
                    staff.Stats.Set(StatType.Fatigue, 0f);
                }
                else
                {
                    isHeldOvertime = true; // blocked by the coverage rule — accrues overtime Fatigue below
                }
            }

            if (staff.State == StaffState.OffShift) continue; // no accrual while off

            var fatigueDelta = isHeldOvertime
                ? baseFatigueAccrualRate * overtimeFatigueMultiplier
                : baseFatigueAccrualRate;

            var currentFatigue = staff.Stats.Get(StatType.Fatigue);
            staff.Stats.Set(StatType.Fatigue, Math.Clamp(currentFatigue + fatigueDelta, 0, 1));
        }

        await _repository.SaveAsync(restaurant, cancellationToken);

        return Result.Success();
    }

    // Entity doc §24a — ShiftStart/End span a staff member's FULL daily shift
    // across all their AssignedMealPeriods, not each period individually (a
    // gap between two assigned periods, e.g. breakfast+lunch, is not a
    // clock-out point).
    private static (int ShiftStartMinutes, int ShiftEndMinutes) ComputeShiftWindow(StaffMember staff, RestaurantConfig config)
    {
        var assignedPeriods = config.ActiveMealPeriods.Where(p => staff.AssignedMealPeriods.Contains(p.MealPeriodId)).ToList();
        if (assignedPeriods.Count == 0) return (0, 0); // no assignment — treat as always OffShift-eligible

        var windows = assignedPeriods.Select(MealPeriodCalculator.PeriodWindow).ToList();
        return (windows.Min(w => w.StartMinutes), windows.Max(w => w.EndMinutes));
    }

    // Coverage-gated clock-out (confirmed in chat, entity doc §24a) — may go
    // OffShift only if there's no demand for their role, or another
    // non-OffShift staff member of the same role remains to cover it.
    private static bool IsRoleCovered(Restaurant restaurant, StaffMember staff)
    {
        var hasDemand = staff.Role switch
        {
            StaffRole.Waiter or StaffRole.Host =>
                restaurant.CustomerService.ActiveGroups.Count > 0 || restaurant.CustomerService.Queue.Count > 0,

            StaffRole.Cook or StaffRole.HeadChef =>
                restaurant.KitchenOps.ActiveOrders.SelectMany(o => o.Items)
                    .Any(i => i.Status is OrderItemStatus.Queued or OrderItemStatus.Cooking),

            // Bathroom cleanliness thresholds aren't checked here — the
            // two-tier Busser priority system that would consume them
            // (mechanics doc §13) isn't built yet. Table dirtiness only, for now.
            StaffRole.Busser =>
                restaurant.Floor.Tables.Any(t => !t.IsClean),

            _ => false
        };

        if (!hasDemand) return true;

        return restaurant.StaffRoster.Staff.Any(s =>
            s.StaffId != staff.StaffId && s.Role == staff.Role && s.State != StaffState.OffShift);
    }
}
