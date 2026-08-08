namespace WorldsAdriftReborn.Storage.Records
{
    /// <summary>
    /// One row of <c>characters</c>: the storage-side shape of a roster entry,
    /// with the client's own payload carried opaquely in
    /// <paramref name="DataJson"/>.
    ///
    /// The split is deliberate. The columns are the things something queries or
    /// constrains - who owns it, where it sits, whether it is the create-new
    /// slot. Everything the client alone understands stays in the JSON and is
    /// round-tripped byte for byte, so a change to the client's cosmetics format
    /// is not a schema change.
    /// </summary>
    /// <param name="CharacterUid">
    /// A Guid, not a string. The client's social code calls new Guid(uid) on
    /// this, and the upstream placeholder "valid-UIDs-have-at-least-one-" gets
    /// past its Contains("-") check before throwing there. Typing the field as a
    /// Guid means the adapter has to do that parse at the boundary, where it can
    /// be handled, instead of the client doing it mid-frame.
    /// </param>
    /// <param name="IsEmptySlot">
    /// True for the trailing create-a-character row. Stored as its own column
    /// rather than inferred from the JSON because the client infers it from
    /// Cosmetics == null, and an entry that is empty by one definition and full
    /// by the other is an NRE in the customisation visualiser.
    /// </param>
    public sealed record CharacterRecord(
        Guid CharacterUid,
        long AccountId,
        string Name,
        int SlotIndex,
        bool IsEmptySlot,
        string DataJson,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
