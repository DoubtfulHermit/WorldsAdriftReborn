namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// One island, as an interest candidate: how far this peer is from the
    /// island's TERRAIN, and how much of the peer's budget admitting it costs.
    /// </summary>
    /// <param name="IslandId">The island whose contents this is.</param>
    /// <param name="DistanceSquared">Squared metres from the peer to the island's
    /// ENVELOPE - zero while the peer is standing on it.</param>
    /// <param name="Cost">How many entities admitting this island would check out.</param>
    public readonly record struct IslandInterestCandidate(
        IslandId IslandId,
        double DistanceSquared,
        int Cost);

    /// <summary>
    /// WHICH ISLANDS' CONTENTS A PEER HOLDS. The one admission rule shared by
    /// island fauna and island resources, so the two features cannot drift apart
    /// and disagree about what "being on an island" means.
    ///
    /// WHY ISLANDS AND NOT ENTITIES. Both features arrived here from the same bug,
    /// twice. Interest was originally decided per ENTITY against the peer's own
    /// position at the global <c>WAREBORN_INTEREST_RADIUS_M</c> (120 m load,
    /// 155 m unload). For a manta ray that is wrong because the ANIMAL moves: it
    /// orbits across the boundary twice a lap and each crossing is a RemoveEntity
    /// followed by a fresh AssetLoadRequest + AddEntity. For a rock or a tree it is
    /// wrong for the mirror-image reason: the RESOURCE never moves but the PLAYER
    /// does, and a release island is up to 735 m across while the bubble is 240 m
    /// across - so a player standing on Mount Spero held 2 of its 19 nodes and
    /// emptied the island simply by walking.
    ///
    /// Keying on the ISLAND kills both. An island's envelope distance is zero
    /// everywhere on the island and changes only when the PLAYER travels, so
    /// nothing on it can flicker while the player is there, and no future change to
    /// where entities sit or how they move can bring the churn back. That is a
    /// STRUCTURAL property, not a tuning one, which is the whole point.
    ///
    /// THE PER-PEER BUDGET IS THE SAFETY PROPERTY. The standing multiplayer-safety
    /// rule is about the rate ONE PEER receives, not about how many entities exist.
    /// Admission is capped in entities per peer, so world population and per-peer
    /// wire cost are decoupled: the world can populate every island without moving
    /// the number the soak gate measures.
    ///
    /// ADMISSION IS WHOLE-ISLAND, and that is what keeps the budget itself from
    /// becoming a new source of churn. If the budget were spent entity by entity, a
    /// school orbiting past the cap boundary - or a player walking along a ridge -
    /// would swap members in and out, which is the original bug rebuilt through the
    /// back door. Islands are admitted whole, ordered by a distance that only
    /// changes when the PLAYER moves, and an island already held keeps its place
    /// ahead of any newcomer.
    ///
    /// THE COROLLARY THE CALLER MUST HONOUR: a budget smaller than an island's own
    /// content admits NOTHING for that island, which looks exactly like the bug
    /// this replaces. Callers therefore size their default against the measured
    /// worst case and say so at boot rather than discovering it in a player report.
    /// </summary>
    public static class IslandInterestAdmissionPolicy
    {
        /// <summary>
        /// The islands this peer should hold, given where it is now and what it
        /// already holds.
        ///
        /// TOTAL AND ORDER-INDEPENDENT: the result is a function of the candidates'
        /// distances and the held set, never of the order they were enumerated in,
        /// so a caller cannot change a peer's world by iterating a dictionary
        /// differently. Ties break on island id for the same reason.
        ///
        /// THE THREE RULES, in the order they are applied:
        /// <list type="number">
        /// <item>a HELD island is retained until it is past
        ///   <paramref name="unloadRadius"/>, and an unheld one is only considered
        ///   inside <paramref name="loadRadius"/>. That is the hysteresis;</item>
        /// <item>RETAINED ISLANDS ARE ADMITTED FIRST, nearest first, before any
        ///   newcomer is considered. Without this a newly approached island could
        ///   evict the one under the player's feet, which is the loudest possible
        ///   version of the bug this type exists to fix;</item>
        /// <item>an island is admitted WHOLE or not at all, while its cost fits in
        ///   the remaining budget. A cost that does not fit is skipped and a later,
        ///   smaller island may still be admitted.</item>
        /// </list>
        /// </summary>
        public static IReadOnlyList<IslandId> Admit(
            IEnumerable<IslandInterestCandidate> candidates,
            ISet<IslandId> held,
            double loadRadius,
            double unloadRadius,
            int perPeerBudget)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (held == null) throw new ArgumentNullException(nameof(held));
            if (perPeerBudget <= 0 || loadRadius <= 0.0) return Array.Empty<IslandId>();

            double load2 = loadRadius * loadRadius;
            double unload2 = unloadRadius * unloadRadius;

            List<IslandInterestCandidate> retained = new List<IslandInterestCandidate>();
            List<IslandInterestCandidate> arriving = new List<IslandInterestCandidate>();
            foreach (IslandInterestCandidate candidate in candidates)
            {
                if (held.Contains(candidate.IslandId))
                {
                    if (candidate.DistanceSquared <= unload2) retained.Add(candidate);
                }
                else if (candidate.DistanceSquared <= load2)
                {
                    arriving.Add(candidate);
                }
            }

            retained.Sort(Nearest);
            arriving.Sort(Nearest);

            List<IslandId> admitted = new List<IslandId>();
            int spent = 0;
            foreach (IslandInterestCandidate candidate in retained.Concat(arriving))
            {
                if (candidate.Cost <= 0 || spent + candidate.Cost > perPeerBudget) continue;
                admitted.Add(candidate.IslandId);
                spent += candidate.Cost;
            }
            return admitted;
        }

        private static int Nearest(IslandInterestCandidate a, IslandInterestCandidate b)
        {
            int byDistance = a.DistanceSquared.CompareTo(b.DistanceSquared);
            return byDistance != 0
                ? byDistance
                : string.CompareOrdinal(a.IslandId.ToString(), b.IslandId.ToString());
        }
    }
}
