using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Crafting
{
    /// <summary>
    /// Process-global registry of live ship-blueprint builds, keyed by BOTH the
    /// shipyard entity and the acting player.
    ///
    /// WHY THE KEY IS A PAIR. The shipyard is a SHARED world entity: two players can
    /// have the same shipyard's build UI open at once. Each must load materials into
    /// their OWN blueprint view without the other's fill state clobbering theirs, and
    /// each 1271 push is addressed only to the acting peer. Keying the build on
    /// (shipyard, player) is what keeps those two views separate; a shipyard-only key
    /// would merge them into one shared bill, exactly the cross-player clobber the
    /// multiplayer-safety rule forbids.
    ///
    /// In-session only, like <c>PlayerShipBlueprints</c> / <c>PlayerShipDesigns</c>.
    /// </summary>
    public static class ShipBlueprintBuildStore
    {
        private readonly struct Key : IEquatable<Key>
        {
            public Key(long shipyardEntityId, long playerEntityId)
            {
                ShipyardEntityId = shipyardEntityId;
                PlayerEntityId = playerEntityId;
            }

            public long ShipyardEntityId { get; }
            public long PlayerEntityId { get; }

            public bool Equals(Key other) =>
                ShipyardEntityId == other.ShipyardEntityId && PlayerEntityId == other.PlayerEntityId;

            public override bool Equals(object? obj) => obj is Key other && Equals(other);

            public override int GetHashCode() =>
                unchecked((ShipyardEntityId.GetHashCode() * 397) ^ PlayerEntityId.GetHashCode());
        }

        private static readonly Dictionary<Key, ShipBlueprintBuild> Builds =
            new Dictionary<Key, ShipBlueprintBuild>();

        /// <summary>Store (replacing any previous) the build a player has selected on a shipyard.</summary>
        public static void Set(long shipyardEntityId, long playerEntityId, ShipBlueprintBuild build)
        {
            Builds[new Key(shipyardEntityId, playerEntityId)] = build;
        }

        /// <summary>The player's live build on a shipyard, or null if none is selected.</summary>
        public static ShipBlueprintBuild? Get(long shipyardEntityId, long playerEntityId)
        {
            return Builds.TryGetValue(new Key(shipyardEntityId, playerEntityId), out ShipBlueprintBuild? b) ? b : null;
        }

        /// <summary>Drop the player's build on a shipyard (blueprint deselected).</summary>
        public static void Clear(long shipyardEntityId, long playerEntityId)
        {
            Builds.Remove(new Key(shipyardEntityId, playerEntityId));
        }

        /// <summary>Drop every build a player holds, on any shipyard (they left).</summary>
        public static void ForgetPlayer(long playerEntityId)
        {
            List<Key> toRemove = new List<Key>();
            foreach (KeyValuePair<Key, ShipBlueprintBuild> entry in Builds)
            {
                if (entry.Key.PlayerEntityId == playerEntityId)
                {
                    toRemove.Add(entry.Key);
                }
            }
            foreach (Key key in toRemove)
            {
                Builds.Remove(key);
            }
        }
    }
}
