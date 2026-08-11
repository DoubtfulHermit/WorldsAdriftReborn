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

        /// <summary>How many shipyards have been deployed this session.</summary>
        internal static int Count => ByEntityId.Count;
    }
}
