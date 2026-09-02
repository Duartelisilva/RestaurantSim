using FluentValidation;
using RestaurantSim.Domain.Abstractions;
using RestaurantSim.Domain.CustomerService;

namespace RestaurantSim.Application.Commands.LeaveTable;

public sealed class LeaveTableCommandValidator : AbstractValidator<LeaveTableCommand>
{
    private readonly IRestaurantRepository _repository;

    public LeaveTableCommandValidator(IRestaurantRepository repository)
    {
        _repository = repository;

        RuleFor(x => x.RestaurantId).NotEmpty();
        RuleFor(x => x.GroupId).GreaterThan(0);

        RuleFor(x => x)
            .MustAsync(GroupHasPaidAndCanLeaveAsync)
            .WithMessage(x => $"Group {x.GroupId} is not currently in a Leaving state (must pay first).");
    }

    private async Task<bool> GroupHasPaidAndCanLeaveAsync(LeaveTableCommand command, CancellationToken cancellationToken)
    {
        var restaurant = await _repository.GetAsync(command.RestaurantId, cancellationToken);
        var group = restaurant?.CustomerService.ActiveGroups.FirstOrDefault(g => g.GroupId == command.GroupId);
        return group?.LifecycleState == GroupLifecycleState.Leaving;
    }
}
