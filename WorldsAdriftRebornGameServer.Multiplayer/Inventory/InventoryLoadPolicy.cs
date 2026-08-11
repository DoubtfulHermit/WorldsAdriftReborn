namespace WorldsAdriftRebornGameServer.Multiplayer.Inventory
{
    /// <summary>
    /// The single decision that stands between a database read and a player's
    /// live inventory: may what the database returned REPLACE what is in memory?
    ///
    /// It exists as its own pure function, rather than an <c>if</c> buried in the
    /// load glue, because getting it wrong is the one bug in this whole area that
    /// DESTROYS data instead of merely misplacing it - and a transient database
    /// hiccup is exactly the moment it would fire. Turning persistence on against
    /// a live server must be incapable of emptying an inventory that was full a
    /// moment ago, so the rule is stated once, here, and unit-tested without a
    /// database, an entity id or a socket.
    /// </summary>
    public static class InventoryLoadPolicy
    {
        /// <summary>
        /// Whether a stored inventory should overwrite the current in-memory one.
        ///
        /// <paramref name="stored"/> is null for the three cases the persistence
        /// layer deliberately collapses into "nothing to restore": no row for
        /// this character, a database that could not be read, and a payload that
        /// would not parse. All three mean KEEP what the session already holds -
        /// a first login keeps its fresh seed, and a database error keeps the
        /// items the player is carrying rather than trading them for an empty
        /// grid. A transient error must never look like a wipe.
        ///
        /// An empty stored inventory over a NON-EMPTY session is refused too. A
        /// row that parses to zero items is indistinguishable from a truncated or
        /// half-written one, and the cost of guessing wrong is asymmetric:
        /// refusing a genuinely-empty restore hands a player a starter kit they
        /// can drop again, while accepting a spurious-empty restore deletes
        /// everything they own with no way back. When in doubt, do not wipe. The
        /// kept inventory is then written back by the next save, correcting the
        /// suspect row rather than obeying it.
        ///
        /// Every other case applies: a stored inventory with items always wins
        /// (that is the relog this whole workstream exists for), and an empty
        /// restore onto an already-empty session is a harmless no-op.
        /// </summary>
        public static bool ShouldApplyStored(int currentItemCount, IReadOnlyList<InventoryItem>? stored)
        {
            if (stored == null)
            {
                return false;
            }

            if (stored.Count == 0 && currentItemCount > 0)
            {
                return false;
            }

            return true;
        }
    }
}
