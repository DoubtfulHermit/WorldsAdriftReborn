namespace WorldsAdriftRebornGameServer.Multiplayer.Knowledge
{
    /// <summary>
    /// The single decision that stands between a database read and a player's
    /// live knowledge: may what the database returned REPLACE what is in memory?
    ///
    /// The exact analogue of InventoryLoadPolicy, and it exists for the same
    /// reason: getting it wrong DESTROYS progress instead of merely misplacing
    /// it, and a transient database hiccup is exactly the moment it would fire.
    /// Turning progression persistence on against a live server must be incapable
    /// of resetting a player who scanned a databank a moment ago, so the rule is
    /// stated once, here, and unit-tested without a database.
    /// </summary>
    public static class ProgressionLoadPolicy
    {
        /// <summary>
        /// Whether a stored progression should overwrite the current in-memory one.
        ///
        /// <paramref name="stored"/> is null for the three cases the persistence
        /// layer collapses into "nothing to restore": no row, a database that
        /// could not be read, and a payload that would not parse. All three mean
        /// KEEP what the session already holds - a first login keeps its fresh
        /// seed, and a database error keeps the knowledge the player has earned
        /// rather than trading it for a reset. A transient error must never look
        /// like a wipe.
        ///
        /// A stored record that has NO progress over a session that DOES is
        /// refused for the same reason an empty inventory is: a seed-only row is
        /// indistinguishable from a truncated one, and the cost of guessing wrong
        /// is asymmetric - refusing a genuinely-fresh restore costs nothing (the
        /// live seed is identical), while accepting a spurious-fresh restore
        /// deletes everything the player has learned. When in doubt, do not wipe;
        /// the next save writes the kept state back and corrects the suspect row.
        /// </summary>
        public static bool ShouldApplyStored(bool currentHasProgress, ProgressionState? stored)
        {
            if (stored == null)
            {
                return false;
            }

            if (!stored.HasProgress && currentHasProgress)
            {
                return false;
            }

            return true;
        }
    }
}
