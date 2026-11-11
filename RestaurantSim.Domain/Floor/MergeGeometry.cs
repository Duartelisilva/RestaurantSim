namespace RestaurantSim.Domain.Floor;

// What the merge rules need to know about the room besides the tables themselves: how far
// apart two tables may be ("nearby only", RestaurantConfig.MaxMergeGapCells) and which cells
// are occupied, because a table can only slide into a free gap.
public sealed record MergeGeometry(int MaxGapCells, IReadOnlySet<GridPosition> BlockedCells)
{
    // Tables must already touch — the behaviour before tables could be moved.
    public static MergeGeometry TouchingOnly { get; } = new(0, new HashSet<GridPosition>());

    // Every cell of every placeable on the floor, tables included.
    public static MergeGeometry For(FloorState floor, int maxGapCells) =>
        new(maxGapCells, BlockedCellsOf(floor, excludedTableIds: Array.Empty<int>()));

    // Same, but ignoring the given tables — the cells a group of tables can return to.
    public static IReadOnlySet<GridPosition> BlockedCellsOf(FloorState floor, IReadOnlyCollection<int> excludedTableIds)
    {
        var placeables = new List<IPlaceable>();
        placeables.AddRange(floor.Tables.Where(t => !excludedTableIds.Contains(t.TableId)));
        placeables.AddRange(floor.KitchenStations);
        placeables.AddRange(floor.HeatLamps);
        placeables.AddRange(floor.Bathrooms);
        placeables.AddRange(floor.Freezers);

        return placeables.SelectMany(Footprint.Cells).ToHashSet();
    }
}
