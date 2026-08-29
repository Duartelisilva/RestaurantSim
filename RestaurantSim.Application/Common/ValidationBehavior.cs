using FluentValidation;
using MediatR;

namespace RestaurantSim.Application.Common;

// Ordered AFTER RestaurantLockBehavior in the pipeline (architecture doc §11) —
// validation runs inside the lock, so re-checked state is guaranteed fresh.
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(result => result.Errors)
            .ToList();

        if (failures.Count == 0)
            return await next();

        var errorMessage = string.Join("; ", failures.Select(f => f.ErrorMessage));

        // Every command in this project returns Result or Result<T> (CLAUDE.md).
        // Reflection is a pragmatic shortcut for the generic case — revisit if it
        // ever feels too hacky; a shared static-factory interface is the cleaner fix.
        if (typeof(TResponse) == typeof(Result))
            return (TResponse)(object)Result.Failure(errorMessage);

        if (typeof(TResponse).IsGenericType && typeof(TResponse).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var failureMethod = typeof(Result<>)
                .MakeGenericType(typeof(TResponse).GetGenericArguments()[0])
                .GetMethod(nameof(Result.Failure), new[] { typeof(string) });
            return (TResponse)failureMethod!.Invoke(null, new object[] { errorMessage })!;
        }

        throw new ValidationException(failures);
    }
}
