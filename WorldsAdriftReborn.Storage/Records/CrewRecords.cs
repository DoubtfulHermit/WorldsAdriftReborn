namespace WorldsAdriftReborn.Storage.Records
{
    /// <summary>
    /// One row of <c>crews</c>: the crew itself and who leads it.
    /// </summary>
    /// <param name="CrewId">
    /// The crew's durable id. Opaque text rather than a UUID column because the
    /// game server mints it and never parses it.
    /// </param>
    /// <param name="LeaderUid">
    /// The leading character. A member like any other; this is a pointer into
    /// <see cref="CrewMemberRecord"/>, not a separate role.
    /// </param>
    public sealed record CrewRecord(
        string CrewId,
        Guid LeaderUid,
        int NumSlots,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    /// <summary>
    /// One row of <c>crew_members</c>: a character's membership of a crew.
    /// </summary>
    /// <param name="JoinOrder">
    /// Load-bearing, not decoration: leadership succession reads the
    /// longest-standing remaining member off this.
    /// </param>
    /// <param name="Slot">The seat in the crew UI, or null if they have not taken one.</param>
    public sealed record CrewMemberRecord(
        Guid CharacterUid,
        string CrewId,
        int JoinOrder,
        int? Slot,
        DateTimeOffset CreatedAt);
}
