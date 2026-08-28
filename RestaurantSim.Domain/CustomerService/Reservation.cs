namespace RestaurantSim.Domain.CustomerService;

public enum ReservationStatus
{
    Booked,
    Arrived,

    // Grace window expired, host freed the hold.
    Released
}

public sealed class Reservation
{
    public required int ReservationId { get; init; }

    // Tick when the party is expected.
    public required long ScheduledTime { get; init; }
    public required int PartySize { get; init; }

    // Pre-generated group profile data (not yet a live CustomerGroup).
    // Shape intentionally unspecified — profile generation isn't designed yet;
    // revisit when Reservation logic is actually implemented (Phase 4/DLC).
    public required object GroupProfile { get; init; }

    // Optionally pre-assigned at booking; MVP can leave this to resolve at arrival.
    public int? AssignedClusterId { get; set; }

    // How long past ScheduledTime the hold persists before being releasable.
    public required int GraceWindowTicks { get; init; }

    public ReservationStatus Status { get; set; } = ReservationStatus.Booked;
}
