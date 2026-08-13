using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Game.Placement
{
    /// <summary>
    /// The server's record of every shipyard a player has DEPLOYED this session,
    /// keyed by the world-entity id it was spawned as. It is the 1205 ShipyardState
    /// counterpart of <c>Nodes</c>/<c>DatabankLedger</c>: the spawn seam writes one
    /// entry when a placement is accepted, and <c>ComponentsSerializer</c>'s 1205
    /// branch reads it back to seed the deployed/owner state so the client's
    /// <c>ShipyardVisualizer</c> renders it as a placed, deployed structure rather
    /// than an inert prop.
    ///
    /// In-memory only, exactly like the node and databank ledgers: "persistent" for
    /// this milestone means "visible to every connected client until the server
    /// restarts". A restart-durable placed-structure ledger is the documented
    /// follow-on (findings-deployable-placement.md, "Persistence And Multiplayer").
    ///
    /// NOT thread-safe, deliberately: the server is a single poll loop, and every
    /// writer here runs on it.
    /// </summary>
    internal static class PlacedShipyards
    {
        /// <summary>The seed state one deployed shipyard is served with.</summary>
        internal readonly struct Seed
        {
            internal Seed(string ownerCharacterUid, bool deployed, bool active)
            {
                OwnerCharacterUid = ownerCharacterUid ?? "";
                Deployed = deployed;
                Active = active;
            }

            internal string OwnerCharacterUid { get; }
            internal bool Deployed { get; }
            internal bool Active { get; }
        }

        private static readonly Dictionary<long, Seed> ByEntityId = new Dictionary<long, Seed>();
        private static int _sequence;

        /// <summary>
        /// A never-reused suffix for a placed shipyard's registration key. The key
        /// is what the world-entity registry allocates a shared entity id from, so
        /// it must be unique for the life of the process; a monotonic counter is
        /// the simplest thing that cannot collide.
        /// </summary>
        internal static int NextSequence()
        {
            return _sequence++;
        }

        /// <summary>Records a newly deployed shipyard's seed state. Idempotent per id.</summary>
        internal static void Register(long entityId, string ownerCharacterUid, bool deployed = true, bool active = true)
        {
            ByEntityId[entityId] = new Seed(ownerCharacterUid, deployed, active);
        }

        /// <summary>
        /// Flip a placed shipyard's seed to deployed=true (owner/active unchanged) after its
        /// fold-out clip has played, so a LATER checkout of the yard (a re-join, a boot
        /// restore) snaps to the finished pose instead of re-animating. A no-op for an id we
        /// do not know. See <see cref="Multiplayer.Placement.ShipyardDeployPolicy"/>.
        /// </summary>
        internal static void MarkDeployed(long entityId)
        {
            if (ByEntityId.TryGetValue(entityId, out Seed seed) && !seed.Deployed)
            {
                ByEntityId[entityId] = new Seed(seed.OwnerCharacterUid, deployed: true, active: seed.Active);
            }
        }

        /// <summary>The seed for a placed shipyard id, or a default deployed seed if unknown.</summary>
        internal static Seed SeedFor(long entityId)
        {
            return ByEntityId.TryGetValue(entityId, out Seed seed)
                ? seed
                : new Seed("", deployed: true, active: true);
        }

        /// <summary>Whether this entity id is a placed shipyard.</summary>
        internal static bool IsPlacedShipyard(long entityId)
        {
            return ByEntityId.ContainsKey(entityId);
        }

        /// <summary>
        /// Drops a shipyard that was PACKED back into inventory (station pickup).
        /// After this the 1205/1210 serve branches stop treating the entity as a
        /// placed yard; the pickup tombstone (StationPickupLedger) is what keeps
        /// the ghost entity sunk and unavailable for late joiners. A no-op for an
        /// unknown id; returns whether anything was removed.
        /// </summary>
        internal static bool Remove(long entityId)
        {
            return ByEntityId.Remove(entityId);
        }

        /// <summary>How many shipyards have been deployed this session.</summary>
        internal static int Count => ByEntityId.Count;
    }
}
