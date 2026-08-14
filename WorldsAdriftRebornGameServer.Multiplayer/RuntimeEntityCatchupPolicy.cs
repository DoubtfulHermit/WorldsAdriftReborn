using System;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Identifies entities which can be registered after the process-wide spawn plan
    /// has been frozen and therefore need a late-join catch-up pass.
    /// </summary>
    public static class RuntimeEntityCatchupPolicy
    {
        /// <summary>
        /// Runtime player-made entities: placed deployables, built hull/deck entities,
        /// and crafted loose parts (including parts later mounted onto a ship).
        /// Static world/resource registrations are deliberately excluded; their
        /// visibility continues to be owned by the boot plan and resource interest.
        /// </summary>
        public static bool NeedsLateJoinCatchup(string? key)
        {
            return HasPrefix(key, "placed-")
                || Ship.BuiltShipPlacement.IsBuiltShipEntityKey(key)
                || Ship.LoosePartPlacement.IsLoosePartKey(key);
        }

        /// <summary>Full queue decision, split out so loading-race and tombstone rules are pinned.</summary>
        public static bool ShouldQueue(string? key, bool isBound, bool addEntityAlreadySent, bool retired)
        {
            return NeedsLateJoinCatchup(key) && isBound && !addEntityAlreadySent && !retired;
        }

        private static bool HasPrefix(string? value, string prefix)
        {
            return value != null && value.StartsWith(prefix, StringComparison.Ordinal);
        }
    }
}
