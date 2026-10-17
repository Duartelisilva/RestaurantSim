namespace RestaurantSim.Contracts.Events;

// Cash is the restaurant's absolute cash after the payment, not a delta.
// Revenue and TipAmount are for display only.
public sealed record BillPaidDto(int GroupId, decimal Revenue, decimal TipAmount, decimal Cash, long Tick);
