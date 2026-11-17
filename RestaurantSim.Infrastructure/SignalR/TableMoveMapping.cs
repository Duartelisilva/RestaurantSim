using RestaurantSim.Contracts.Events;
using RestaurantSim.Contracts.Snapshot;
using RestaurantSim.Domain.Floor;

namespace RestaurantSim.Infrastructure.SignalR;

internal static class TableMoveMapping
{
    public static IReadOnlyList<TableMoveDto> ToDtos(IEnumerable<TableMove> moves) =>
        moves
            .Select(m => new TableMoveDto(m.TableId, new GridPointDto(m.From.X, m.From.Y), new GridPointDto(m.To.X, m.To.Y)))
            .ToList();
}
