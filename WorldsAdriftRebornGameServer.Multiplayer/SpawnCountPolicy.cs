namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// HOW MANY of a repeated world entity to place - the trees and the ore -
    /// without a rebuild, and nothing else.
    ///
    /// WHY THIS EXISTS. The island was populated ~15x for testing (one tree and
    /// one node became ~21 of each), which is what made the first-load volume
    /// worth pacing (see <see cref="SpawnPacePolicy"/>). Dialling that back to try
    /// a smaller world used to mean editing <see cref="WorldEntities"/> and
    /// recompiling. This makes it an environment variable, exactly like the
    /// existing WAREBORN_SPAWN_* kill switches: WAREBORN_TREE_COUNT and
    /// WAREBORN_ORE_COUNT cap the count, defaulting to the full placed set.
    ///
    /// THE FLOOR IS 1, ALWAYS. The near-spawn HavenTree and the proven near-spawn
    /// metal node (placement index 0, the one coordinate validated against a
    /// running client) sit FIRST in their ordered sets, so "the first N" keeps
    /// them for any N &gt;= 1, and the count can never be driven below 1. A count
    /// of 0 would silently delete the one node whose position is actually trusted;
    /// there is no reason to allow it, and the WAREBORN_SPAWN_METAL=0 /
    /// WAREBORN_SPAWN_TREE=0 kill switches already exist for "none at all".
    /// </summary>
    public static class SpawnCountPolicy
    {
        /// <summary>
        /// A raw count clamped to [1, <paramref name="full"/>]. Never below 1 (the
        /// near-spawn anchor entity is not optional) and never above what is
        /// actually placed.
        /// </summary>
        public static int Clamp(int count, int full)
        {
            int floor = full < 1 ? full : 1;

            if (count < floor)
            {
                return floor;
            }

            return count > full ? full : count;
        }

        /// <summary>
        /// The count for an environment value, clamped to [1, <paramref name="full"/>].
        /// Unset, empty or unparsable =&gt; the full set: a missing or fat-fingered
        /// knob leaves the world exactly as it is placed, it does not empty it.
        /// </summary>
        public static int CountFrom(string? env, int full)
        {
            if (!int.TryParse(env, out int parsed))
            {
                return Clamp(full, full);
            }

            return Clamp(parsed, full);
        }
    }
}
