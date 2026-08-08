namespace WorldsAdriftReborn.Storage.Records
{
    /// <summary>
    /// One row of <c>character_inventories</c>: a character's inventory, with the
    /// item list carried opaquely in <paramref name="DataJson"/>.
    ///
    /// The same split as <see cref="CharacterRecord"/>, for the same reason. The
    /// only column is the key, because the only question anything asks of this
    /// table is "what does this character own"; there is no query that filters by
    /// an item, no leaderboard of who has the most iron, and inventing columns
    /// for a query nobody makes would turn every change to the item shape into a
    /// schema migration.
    /// </summary>
    /// <param name="CharacterUid">
    /// The key, and the whole reason this table can exist at all. NOT an entity
    /// id: entity ids are never reused, so an entity-keyed inventory is a new
    /// empty inventory every session. Whether the uid actually reaches the game
    /// server is the load-bearing uncertainty this design is built around - see
    /// InventoryKey in WorldsAdriftRebornGameServer.Multiplayer, which is the
    /// seam that decides.
    /// </param>
    /// <param name="DataJson">
    /// The inventory, written by InventorySnapshot in the game server and
    /// understood by nothing else. Never empty: an empty payload would restore
    /// as an inventory with no grid, which the client reads exactly once at
    /// checkout and can then never be corrected.
    /// </param>
    public sealed record InventoryRecord(
        Guid CharacterUid,
        string DataJson,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
