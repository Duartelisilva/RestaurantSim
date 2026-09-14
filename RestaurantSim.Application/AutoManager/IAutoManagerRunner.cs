namespace RestaurantSim.Application.AutoManager;

// Defined here (Application), NOT in the AutoManager project itself — needed
// so RestaurantTickProcessor (Application) can trigger an AutoManager pass
// without Infrastructure/Application ever referencing the AutoManager project
// directly (locked dependency graph: Infrastructure -> Application, Domain
// only; AutoManager -> Domain, Application). The concrete implementation
// (AutoManagerProcessor, in RestaurantSim.AutoManager) is wired in via DI at
// Api's composition root.
public interface IAutoManagerRunner
{
    // Acquires/releases the per-restaurant lock PER INDIVIDUAL DECISION
    // internally (architecture doc §11) — this call itself runs OUTSIDE any
    // lock, since it may dispatch zero, one, or many commands, each with its
    // own lock cycle via the normal MediatR pipeline.
    Task RunPassAsync(Guid restaurantId, CancellationToken cancellationToken);
}
