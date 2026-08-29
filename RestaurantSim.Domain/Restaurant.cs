using RestaurantSim.Domain.CustomerService;
using RestaurantSim.Domain.Events;
using RestaurantSim.Domain.Floor;
using RestaurantSim.Domain.KitchenOps;
using RestaurantSim.Domain.Staff;

namespace RestaurantSim.Domain;

public enum AutoManagerSystem
{
    Seating,
    WaiterActions,
    TaskPriority
}

// Sub-state groups — thin wrappers bundling lists that would otherwise sit flat
// on Restaurant. Purely structural regrouping for cohesion/module-isolation
// (entity doc §19) — no new data introduced.

public sealed class FloorState
{
    public List<Table> Tables { get; init; } = new();
    public List<TableCluster> Clusters { get; init; } = new();
    public List<KitchenStation> KitchenStations { get; init; } = new();
    public List<HeatLamp> HeatLamps { get; init; } = new();
    public List<Bathroom> Bathrooms { get; init; } = new();
}

public sealed class StaffRosterState
{
    public List<StaffMember> Staff { get; init; } = new();
}

public sealed class CustomerServiceState
{
    public List<CustomerGroup> Queue { get; init; } = new();
    public List<CustomerGroup> ActiveGroups { get; init; } = new();
    public List<CustomerCall> PendingCalls { get; init; } = new();
    public List<Reservation> Reservations { get; init; } = new();

    // New — not in entity doc §19's listing. Schedules the next Arrival check
    // (mirrors CustomerGroup.NextAutoVisitTick's existing pattern).
    public long NextArrivalTick { get; set; }

    // Monotonic, never-reused ID counters — avoids stale-reference bugs from a
    // reassigned ID once a customer/group leaves.
    public int NextGroupId { get; set; } = 1;
    public int NextCustomerId { get; set; } = 1;
}

public sealed class KitchenOpsState
{
    public List<Order> ActiveOrders { get; init; } = new();
    public List<KitchenTicket> ActiveTickets { get; init; } = new();
}

public sealed class EventState
{
    public List<RestaurantEvent> ActiveEvents { get; init; } = new();
    public List<ComplaintRecord> ComplaintLog { get; init; } = new();
}

// Aggregate root — one instance per active session (multi-tenant model, entity doc §19).
public sealed class Restaurant
{
    public required Guid RestaurantId { get; init; }

    // Ref to the RestaurantConfig preset this session started from.
    public required string ConfigId { get; init; }

    // Authoritative clock.
    public long SimulationTick { get; set; }

    // Increments on each DayEnded (mechanics doc §15).
    public int CurrentDayNumber { get; set; }

    public decimal Cash { get; set; }
    public float Reputation { get; set; }

    public required FloorState Floor { get; init; }
    public required StaffRosterState StaffRoster { get; init; }
    public required CustomerServiceState CustomerService { get; init; }
    public required KitchenOpsState KitchenOps { get; init; }
    public required EventState Events { get; init; }

    // Session-mutable, defaults all-on. MES/IKitchenSyncStrategy is deliberately
    // NOT a toggleable entry — always automated regardless of this state.
    public Dictionary<AutoManagerSystem, bool> AutoManagerToggles { get; init; } = new()
    {
        [AutoManagerSystem.Seating] = true,
        [AutoManagerSystem.WaiterActions] = true,
        [AutoManagerSystem.TaskPriority] = true
    };

    // SnapshotHistory deliberately excluded — stats are written OUT to
    // ISnapshotStore, never accumulated on this hot aggregate (entity doc §19).
}
