using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Application.Commands.CleanBathroom;

public sealed class BussersCleanBathroomCommandHandler : IRequestHandler<BussersCleanBathroomCommand, Result>
{
    private readonly IRestaurantRepository _repository;

    public BussersCleanBathroomCommandHandler(IRestaurantRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result> Handle(BussersCleanBathroomCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var busser = restaurant.StaffRoster.Staff.FirstOrDefault(s => s.StaffId == request.BusserStaffId && s.Role == StaffRole.Busser);
        if (busser is null)
            return Result.Failure($"Busser {request.BusserStaffId} does not exist.");

        if (busser.State != StaffState.Idle)
            return Result.Failure($"Busser {request.BusserStaffId} is not idle.");

        var bathroom = restaurant.Floor.Bathrooms.FirstOrDefault(b => b.BathroomId == request.BathroomId);
        if (bathroom is null)
            return Result.Failure($"Bathroom {request.BathroomId} does not exist.");

        if (bathroom.CleanlinessScore >= 100f)
            return Result.Failure($"Bathroom {request.BathroomId} is already fully clean.");

        // Re-checked at handle-time, even though the strategy already checked
        // this — same "validator/handler both protect the invariant" pattern
        // used throughout (architecture doc §11).
        var assignedCount = restaurant.StaffRoster.Staff.Count(s =>
            s.Role == StaffRole.Busser && s.CurrentTaskType == TaskType.CleanBathroom && s.CurrentTaskTargetId == request.BathroomId);
        if (assignedCount >= bathroom.MaxConcurrentCleaners)
            return Result.Failure($"Bathroom {request.BathroomId} already has the maximum number of Bussers assigned.");

        busser.State = StaffState.Busy;
        busser.CurrentTaskType = TaskType.CleanBathroom;
        busser.CurrentTaskTargetId = request.BathroomId;

        await _repository.SaveAsync(restaurant, cancellationToken);

        return Result.Success();
    }
}
