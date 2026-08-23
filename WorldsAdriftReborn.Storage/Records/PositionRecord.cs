namespace WorldsAdriftReborn.Storage.Records
{
    /// <summary>
    /// One row of <c>character_positions</c>: where a character was standing when
    /// the server last saw them.
    ///
    /// Unlike <see cref="InventoryRecord"/> and <see cref="ProgressionRecord"/>
    /// this carries no opaque JSON. Three coordinates are the whole payload, and
    /// they are columns because a stored position is the one piece of
    /// per-character state an operator may genuinely need to read or correct by
    /// hand when a player reports being stuck.
    /// </summary>
    /// <param name="CharacterUid">
    /// The key. NOT an entity id: entity ids are allocated fresh every session,
    /// so an entity-keyed position is exactly the bug this table exists to fix.
    /// </param>
    /// <param name="X">World X in Q52.12 fixed point, the simulation's own units.</param>
    /// <param name="Y">World Y in Q52.12 fixed point.</param>
    /// <param name="Z">
    /// World Z in Q52.12 fixed point. Stored as the exact integers the simulation
    /// uses rather than metres, so a save/restore round trip cannot drift a
    /// player through the floor they were standing on.
    /// </param>
    public sealed record PositionRecord(
        Guid CharacterUid,
        long X,
        long Y,
        long Z,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        int? BuiltShipIndex = null,
        long? ShipLocalX = null,
        long? ShipLocalY = null,
        long? ShipLocalZ = null);
}
