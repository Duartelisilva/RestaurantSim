using MediatR;
using RestaurantSim.Application.Common;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Application.Commands.CleanTable;

public sealed class CleanTableCommandHandler : IRequestHandler<CleanTableCommand, Result>
{
    private readonly IRestaurantRepository _repository;

    public CleanTableCommandHandler(IRestaurantRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result> Handle(CleanTableCommand request, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(request.RestaurantId, cancellationToken);
        if (restaurant is null)
            return Result.Failure($"Restaurant {request.RestaurantId} does not exist.");

        var busser = restaurant.StaffRoster.Staff.FirstOrDefault(s => s.StaffId == request.BusserStaffId && s.Role == StaffRole.Busser);
        if (busser is null)
            return Result.Failure($"Busser {request.BusserStaffId} does not exist.");

        if (busser.State != StaffState.Idle)
            return Result.Failure($"Busser {request.BusserStaffId} is not idle.");

        var table = restaurant.Floor.Tables.FirstOrDefault(t => t.TableId == request.TableId);
        if (table is null)
            return Result.Failure($"Table {request.TableId} does not exist.");

        if (table.IsClean)
            return Result.Failure($"Table {request.TableId} is already clean.");

        busser.State = StaffState.Busy;
        busser.CurrentTaskType = TaskType.CleanTable;
        busser.CurrentTaskTargetId = request.TableId;

        await _repository.SaveAsync(restaurant, cancellationToken);

        return Result.Success();
    }
}
