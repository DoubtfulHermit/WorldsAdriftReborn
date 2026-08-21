using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The server's ledger of every SAIL currently MOUNTED on a built ship and each
    /// sail's furl state. The ONE place "is this sail unfurled" lives, so the 1303
    /// SailState serve branch, the 1211 interact toggle and any later flight-speed
    /// reader all agree on the same bit - pinned by xUnit rather than a running
    /// client, exactly like <see cref="AtlasShardRegistry"/> and the other pure
    /// ledgers in this assembly.
    ///
    /// LIFECYCLE mirrors the mount ledger it shadows (<c>Crafting.MountedParts</c>):
    /// registered when a sail part is MOUNTED onto a hull (or restored mounted at
    /// boot), removed when the sail is LIFTED off with the scanner. A LOOSE sail is
    /// deliberately NOT here - retail sails are only operable rigged on a mast on a
    /// ship, and our 1303 serve keeps a loose sail furled/idle, so absence from this
    /// ledger IS the "loose" answer.
    ///
    /// A freshly mounted sail starts FURLED (unfurled=false): the canvas is bound to
    /// the yard until a player unfurls it, which matches the 1303 idle seed a loose
    /// sail already gets (unfurled=false, power=0) and means a mount is never a
    /// surprise visual state change.
    ///
    /// FLIGHT HOOK, deliberately read-only from here: <see cref="UnfurledCountFor"/>
    /// is exposed so the flight integrator can later scale thrust/speed by rigged
    /// canvas. This ledger does NOT reach into the flight service - the integrator
    /// owns that wiring and reads us when it wants to.
    ///
    /// Pure: no ENet, no Improbable types, no game install. NOT thread-safe,
    /// deliberately - the server is a single poll loop, like every ledger here.
    /// </summary>
    public sealed class Sails
    {
        private sealed class Sail
        {
            public Sail(long hullEntityId, bool unfurled)
            {
                HullEntityId = hullEntityId;
                Unfurled = unfurled;
            }

            public long HullEntityId { get; }
            public bool Unfurled { get; set; }
        }

        private readonly Dictionary<long, Sail> _byEntityId = new Dictionary<long, Sail>();

        /// <summary>
        /// Records that a sail part entity is now mounted on
        /// <paramref name="hullEntityId"/>, starting in <paramref name="unfurled"/>
        /// (false for a fresh mount; the persisted value for a boot restore).
        /// Idempotent per entity id: a re-registration of a KNOWN sail must not blow
        /// away a state a player already set (the spawn-plan-walks-twice rule every
        /// registry here obeys). Returns true on first registration.
        /// </summary>
        public bool Register(long sailEntityId, long hullEntityId, bool unfurled = false)
        {
            if (_byEntityId.ContainsKey(sailEntityId))
            {
                return false;
            }

            _byEntityId[sailEntityId] = new Sail(hullEntityId, unfurled);
            return true;
        }

        /// <summary>
        /// Removes a sail - it was LIFTED off its ship and is loose again. Its furl
        /// state is deliberately forgotten: a re-mounted sail starts furled, like a
        /// fresh one. Returns true if it was registered.
        /// </summary>
        public bool Unregister(long sailEntityId)
        {
            return _byEntityId.Remove(sailEntityId);
        }

        /// <summary>Whether this entity id is a mounted sail this ledger tracks.</summary>
        public bool IsSail(long sailEntityId)
        {
            return _byEntityId.ContainsKey(sailEntityId);
        }

        /// <summary>The hull this mounted sail belongs to, or null if unknown.</summary>
        public long? HullFor(long sailEntityId)
        {
            return _byEntityId.TryGetValue(sailEntityId, out Sail? sail)
                ? sail.HullEntityId
                : null;
        }

        /// <summary>
        /// The furl state served on the sail's 1303: true = canvas out. False for an
        /// unknown id - an unregistered (loose) sail is always furled.
        /// </summary>
        public bool IsUnfurled(long sailEntityId)
        {
            return _byEntityId.TryGetValue(sailEntityId, out Sail? sail) && sail.Unfurled;
        }

        /// <summary>
        /// Flips a mounted sail's furl state - THE interaction. Returns the NEW state,
        /// or null when the id is not a mounted sail (the caller logs and ignores:
        /// never invent state for an entity this ledger does not own).
        /// </summary>
        public bool? Toggle(long sailEntityId)
        {
            if (!_byEntityId.TryGetValue(sailEntityId, out Sail? sail))
            {
                return null;
            }

            sail.Unfurled = !sail.Unfurled;
            return sail.Unfurled;
        }

        /// <summary>
        /// How many of <paramref name="hullEntityId"/>'s sails are unfurled - the
        /// read-only hook the flight integrator can scale speed by. Linear over a
        /// per-ship handful of sails, same call the mount ledger makes.
        /// </summary>
        public int UnfurledCountFor(long hullEntityId)
        {
            int count = 0;
            foreach (KeyValuePair<long, Sail> entry in _byEntityId)
            {
                if (entry.Value.HullEntityId == hullEntityId && entry.Value.Unfurled)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>How many sails are mounted across all ships (diagnostics).</summary>
        public int Count => _byEntityId.Count;
    }
}
