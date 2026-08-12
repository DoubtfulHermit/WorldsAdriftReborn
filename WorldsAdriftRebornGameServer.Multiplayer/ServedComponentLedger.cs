using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Which components the server has ALREADY delivered to a given peer for a
    /// given entity, so the interest handler never re-ADDS one the client still
    /// holds.
    ///
    /// WHY THIS EXISTS - the walk-on-then-fall-through deck. A client re-declares
    /// its whole interest set for an entity from time to time (the client's
    /// SpatialCommunicator clears its dict and resends) WITHOUT dropping the
    /// components it already has - which is why a re-add logs "component X added
    /// to entity N, but it already exists". The old interest handler answered
    /// every such request by re-serialising and re-sending the entity's full
    /// component set. For most entities that is merely wasteful, but for the
    /// spawned Deck01 it is DESTRUCTIVE: re-delivering a ShipDeckVisualizer
    /// [Require] (1518 ShipDeckState or 1099 SalvageAndRepairState) cycles the
    /// reader, and ShipDeckVisualizer.OnDisable runs Clear(), which DESTROYS the
    /// solid deck GameObject; the async rebuild that follows can be dropped
    /// (its callback bails when the visualizer is mid-cycle), leaving the player
    /// standing on the hull's by-design TRIGGER virtual deck. Symptom, confirmed
    /// live: the deck is solid on first render, then the player falls through it
    /// ever after a second seed of 1518/190602 arrives. VERIFIED against the
    /// decompiled ShipDeckVisualizer (Clear at .cs:169-185; the rebuild's
    /// enabled/token guards at .cs:87-96) and the generated ShipDeckState reader
    /// (subscribing to VerticesUpdated re-invokes the build immediately -
    /// ShipDeckState.gen.cs:184-193).
    ///
    /// This is add-only bookkeeping and it NEVER suppresses component VALUE
    /// updates: those travel on COMPONENT_UPDATE_OP, a different channel, not
    /// through the interest AddComponent path this ledger gates. A component is
    /// recorded only once it has actually been serialised and sent, so an id the
    /// server could not seed on the first request (no branch yet, or a genuinely
    /// absent component) stays eligible for a later serve.
    ///
    /// Generic over the peer handle so it unit-tests without the native
    /// ENetPeerHandle; the server instantiates it with that handle, which already
    /// serves as a dictionary key elsewhere (PeerManager.playerState).
    /// </summary>
    public sealed class ServedComponentLedger<TPeer> where TPeer : notnull
    {
        private readonly Dictionary<TPeer, Dictionary<long, HashSet<uint>>> _served =
            new Dictionary<TPeer, Dictionary<long, HashSet<uint>>>();

        /// <summary>
        /// The subset of <paramref name="requested"/> not yet delivered to this
        /// peer for this entity, in request order, each id at most once. These are
        /// the only ids the interest handler should (re-)serve; everything else in
        /// the request the client already has.
        /// </summary>
        public IReadOnlyList<uint> UnservedOf(TPeer peer, long entityId, IEnumerable<uint> requested)
        {
            HashSet<uint>? already = SetFor(peer, entityId, create: false);
            List<uint> unserved = new List<uint>();
            HashSet<uint> seenThisCall = new HashSet<uint>();
            foreach (uint id in requested)
            {
                if (already != null && already.Contains(id))
                {
                    continue;
                }
                if (!seenThisCall.Add(id))
                {
                    continue;
                }
                unserved.Add(id);
            }
            return unserved;
        }

        /// <summary>Record ids the server actually serialised and sent.</summary>
        public void MarkServed(TPeer peer, long entityId, IEnumerable<uint> served)
        {
            HashSet<uint> set = SetFor(peer, entityId, create: true)!;
            foreach (uint id in served)
            {
                set.Add(id);
            }
        }

        /// <summary>Whether this exact component was already delivered.</summary>
        public bool HasServed(TPeer peer, long entityId, uint componentId)
        {
            HashSet<uint>? set = SetFor(peer, entityId, create: false);
            return set != null && set.Contains(componentId);
        }

        /// <summary>Drop everything remembered for a departed peer.</summary>
        public void ForgetPeer(TPeer peer)
        {
            _served.Remove(peer);
        }

        private HashSet<uint>? SetFor(TPeer peer, long entityId, bool create)
        {
            if (!_served.TryGetValue(peer, out Dictionary<long, HashSet<uint>>? byEntity))
            {
                if (!create)
                {
                    return null;
                }
                byEntity = new Dictionary<long, HashSet<uint>>();
                _served[peer] = byEntity;
            }
            if (!byEntity.TryGetValue(entityId, out HashSet<uint>? set))
            {
                if (!create)
                {
                    return null;
                }
                set = new HashSet<uint>();
                byEntity[entityId] = set;
            }
            return set;
        }
    }
}
