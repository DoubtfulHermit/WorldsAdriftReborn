using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer.Wilderness
{
    /// <summary>
    /// One Tier-1 island a graduating player may be sent to, together with the
    /// exact point on it they will stand.
    ///
    /// It is a strictly narrower thing than a <see cref="TeleportDestination"/>:
    /// that type can name an arbitrary coordinate with no ground under it (see its
    /// <c>coord</c> grammar), whereas every value of this one is a registered
    /// island plus an evidenced surface sample. The conversion runs one way only -
    /// <see cref="WildernessCatalog.AsTeleportDestination"/> - so a wilderness
    /// arrival can never be built out of a coordinate somebody typed.
    /// </summary>
    /// <param name="IslandId">Stable island identity, the key everything else joins on.</param>
    /// <param name="DisplayName">Bossa's own island name, for the log and the player-facing line.</param>
    /// <param name="CellId">The MapFile district it sits in: A2, A3, B2 or B3.</param>
    /// <param name="WorldEntityKey">The terrain registration that must exist before
    /// anybody may be sent here. This is the load-bearing field for safety: it is
    /// what lets the teleport path request the island's terrain for that peer and
    /// what lets the landing pin it as confirmed ground.</param>
    /// <param name="Position">Global Q52.12, the island origin plus the landing
    /// point plus the stand-off.</param>
    /// <param name="Provenance">Why this exact point, in one sentence.</param>
    public readonly record struct WildernessDestination(
        IslandId IslandId,
        string DisplayName,
        string CellId,
        string WorldEntityKey,
        FixedPointPosition Position,
        string Provenance)
    {
        public override string ToString() =>
            DisplayName + " (" + CellId + ") " + Position;
    }
}
