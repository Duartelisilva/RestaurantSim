using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.Staff;
using RestaurantSim.Domain.Statistics;

namespace RestaurantSim.Application.Commands.CreateRestaurant;

public sealed class CreateRestaurantCommandHandler : IRequestHandler<CreateRestaurantCommand, Result<Guid>>
{
    private readonly IPresetRepository<RestaurantConfig> _presetRepository;
    private readonly ILayoutRepository _layoutRepository;
    private readonly IRestaurantRepository _repository;

    public CreateRestaurantCommandHandler(
        IPresetRepository<RestaurantConfig> presetRepository,
        ILayoutRepository layoutRepository,
        IRestaurantRepository repository)
    {
        _presetRepository = presetRepository;
        _layoutRepository = layoutRepository;
        _repository = repository;
    }

    public async Task<Result<Guid>> Handle(CreateRestaurantCommand request, CancellationToken cancellationToken)
    {
        var config = await _presetRepository.GetAsync(request.ConfigId, cancellationToken);
        if (config is null)
            return Result<Guid>.Failure($"RestaurantConfig '{request.ConfigId}' does not exist.");

        var layout = await _layoutRepository.GetByIdAsync(config.TableLayoutId, cancellationToken);
        if (layout is null)
            return Result<Guid>.Failure($"Layout '{config.TableLayoutId}' does not exist.");

        var restaurant = new Restaurant
        {
            RestaurantId = Guid.NewGuid(),
            ConfigId = config.ConfigId,
            SimulationTick = 0,
            Cash = config.StartingCash,
            Reputation = config.ReputationStart,
            Floor = new FloorState
            {
                Tables = layout.Tables.ToList(),
                Clusters = layout.Clusters.ToList(),
                KitchenStations = layout.KitchenStations.ToList(),
                HeatLamps = layout.HeatLamps.ToList(),
                Freezers = layout.Freezers.ToList(),
                Bathrooms = layout.Bathrooms.ToList()
            },
            StaffRoster = new StaffRosterState { Staff = SeedStaff(config) },
            CustomerService = new CustomerServiceState(),
            KitchenOps = new KitchenOpsState(),
            Events = new EventState(),
            // Seeds day-1's ReputationDelta baseline correctly — without this,
            // EndDayCommand would compute day 1's delta against 0 instead of
            // the actual starting Reputation, overstating it.
            DailyStats = new DailyStatsAccumulator { ReputationAtDayStart = config.ReputationStart }
        };

        await _repository.SaveAsync(restaurant, cancellationToken);

        return Result<Guid>.Success(restaurant.RestaurantId);
    }

    private static List<StaffMember> SeedStaff(RestaurantConfig config)
    {
        var staff = new List<StaffMember>();
        var nextId = 1;
        var assignedPeriods = config.ActiveMealPeriods.Select(p => p.MealPeriodId).ToList();

        foreach (var (role, count) in config.StaffRosterSeed)
        {
            for (var i = 1; i <= count; i++)
            {
                var stats = new StaffStats();
                stats.Set(StatType.Speed, 0.5f);
                stats.Set(StatType.ErrorRate, 0.5f);
                stats.Set(StatType.Fatigue, 0f);
                stats.Set(StatType.Charisma, 0.5f);
                stats.Set(StatType.Experience, 0.5f);
                stats.Set(StatType.Morale, 0.5f);

                staff.Add(new StaffMember
                {
                    StaffId = nextId++,
                    Name = $"{role} {i}",
                    Role = role,
                    State = StaffState.Idle,
                    Stats = stats,
                    AssignedMealPeriods = assignedPeriods
                });
            }
        }

        return staff;
    }
}
