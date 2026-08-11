namespace WorldsAdriftReborn.Storage.Records
{
    /// <summary>
    /// One row of <c>character_progression</c>: a character's knowledge state,
    /// with the totals, node uses, learned schematics and scanned ledger carried
    /// opaquely in <paramref name="DataJson"/>.
    ///
    /// The same split, and the same single-key shape, as
    /// <see cref="InventoryRecord"/>: the only question anything asks of this
    /// table is "what does this character know", so the only column is the key
    /// and everything the game server alone understands stays in the JSON. That
    /// is what stops a change to the progression shape from being a schema
    /// migration.
    /// </summary>
    /// <param name="CharacterUid">
    /// The key. NOT an entity id - entity ids are never reused, so an
    /// entity-keyed progression is a fresh seed every session, which is exactly
    /// the bug (knowledge lost on relog) this table exists to fix.
    /// </param>
    /// <param name="DataJson">
    /// The progression, written by ProgressionSnapshot in the game server. Never
    /// empty: a blank payload is refused by the table's CHECK so a truncated
    /// write cannot masquerade as "no progression".
    /// </param>
    public sealed record ProgressionRecord(
        Guid CharacterUid,
        string DataJson,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
