namespace WorldsAdriftRebornGameServer.Game.Crafting
{
    /// <summary>
    /// One craft slot's server-side reservation: how much matching material the
    /// player has committed to this slot, and which type they dropped (for the
    /// slot icon). It is an OVERLAY, not a withdrawal - nothing leaves the bag
    /// when a slot is filled; the atomic consume happens once, at craft, in
    /// CraftingPolicy. That keeps the player from losing materials by closing the
    /// Craft tab, and keeps the consume in one testable place.
    /// </summary>
    internal struct SlotHold
    {
        public int Amount;
        public string MaterialTypeId;
    }

    /// <summary>
    /// One player's in-progress personal craft: the recipe they have selected and
    /// what they have slotted so far. Sized to the recipe's requirement count on
    /// selection, so the 1005 slottedMaterials list the client indexes positionally
    /// is always at least as long as craftingRequirements.
    /// </summary>
    internal sealed class CraftSession
    {
        public string? SchematicId;
        public SlotHold[] Slots = System.Array.Empty<SlotHold>();
    }

    /// <summary>
    /// The per-player crafting sessions, keyed by the player's entity id.
    ///
    /// Server-owned and per-player: a session is never relayed to any other
    /// player, and 1005 is pushed only to the crafting player's own peer. This is
    /// UI reservation state, event-driven, with nothing per-frame - it adds no
    /// multiplayer relay surface.
    /// </summary>
    internal static class CraftSessions
    {
        private static readonly Dictionary<long, CraftSession> Sessions = new Dictionary<long, CraftSession>();

        public static CraftSession For(long entityId)
        {
            if (!Sessions.TryGetValue(entityId, out CraftSession? session))
            {
                session = new CraftSession();
                Sessions[entityId] = session;
            }

            return session;
        }

        public static void Forget(long entityId) => Sessions.Remove(entityId);
    }
}
