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

        /// <summary>
        /// The placed crafting station this session's craft is bound to, or null for
        /// a personal (multitool) craft. Set from the 1003 craftingStationEntityId
        /// field as updates arrive, so a station console re-open can tell an in-
        /// progress craft AT THIS station (preserve it) from a fresh open or a craft
        /// that belonged to a different station / to personal crafting (reset it to
        /// the crash-safe idle shape). See StationCraftRouting.ShouldResetToIdleOnOpen.
        /// </summary>
        public long? StationEntityId;

        /// <summary>
        /// Fully return this context to idle: no recipe, no slot reservations, and no
        /// station binding. Called when a craft COMPLETES (or is abandoned) so the next
        /// SetSchematic starts clean AND a console re-open resets to the crash-safe idle
        /// shape - StationCraftRouting.ShouldResetToIdleOnOpen sees an empty schematic AND
        /// no bound station, so it always resets after a completed craft. Clearing
        /// StationEntityId too (not just SchematicId) is what stops a COMPLETED craft from
        /// leaving a stale (player, station) binding that could read as "still active here".
        /// </summary>
        public void ReturnToIdle()
        {
            SchematicId = null;
            Slots = System.Array.Empty<SlotHold>();
            StationEntityId = null;
        }
    }

    /// <summary>
    /// The crafting sessions, keyed by (player entity id, craft-target entity id).
    ///
    /// A player has ONE session per crafting CONTEXT, not one session total: their
    /// personal (multitool) craft is keyed (player, player); a craft at a placed
    /// Assembly Station is keyed (player, station). Keeping the two apart is what
    /// stops a station recipe (category CraftingStation) from leaking into the
    /// player's own 1005 personal model and blanking the personal Crafting tab
    /// (CraftingStationSchematicList.SelectSchematic NRE). Even a delayed or
    /// mis-tagged update lands in its own bucket rather than becoming the next
    /// personal transaction. The per-target session also keeps the
    /// one-SlottedMaterial-per-requirement sizing correct per context, so the
    /// client's positional SyncCraftingItems never indexes past the wire list.
    ///
    /// Server-owned and per-player: a session is never relayed to any other
    /// player, and 1005 is pushed only to the crafting player's own peer. This is
    /// UI reservation state, event-driven, with nothing per-frame - it adds no
    /// multiplayer relay surface.
    /// </summary>
    internal static class CraftSessions
    {
        private static readonly Dictionary<(long Player, long Target), CraftSession> Sessions =
            new Dictionary<(long Player, long Target), CraftSession>();

        /// <summary>The player's PERSONAL (multitool) session: target is the player itself.</summary>
        public static CraftSession For(long playerEntityId) => For(playerEntityId, playerEntityId);

        /// <summary>
        /// The session for a specific crafting target: (player, player) for a personal
        /// multitool craft, (player, station) for a craft at a placed station.
        /// </summary>
        public static CraftSession For(long playerEntityId, long craftTargetEntityId)
        {
            (long, long) key = (playerEntityId, craftTargetEntityId);
            if (!Sessions.TryGetValue(key, out CraftSession? session))
            {
                session = new CraftSession();
                Sessions[key] = session;
            }

            return session;
        }

        /// <summary>Drops every session belonging to this player (personal and any station targets).</summary>
        public static void Forget(long playerEntityId)
        {
            List<(long Player, long Target)> mine = new List<(long Player, long Target)>();
            foreach ((long Player, long Target) key in Sessions.Keys)
            {
                if (key.Player == playerEntityId)
                {
                    mine.Add(key);
                }
            }

            foreach ((long Player, long Target) key in mine)
            {
                Sessions.Remove(key);
            }
        }
    }
}
