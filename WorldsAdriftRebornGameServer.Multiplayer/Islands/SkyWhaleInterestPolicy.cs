namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// One whale (or one call) as an interest candidate: how far this peer is from
    /// it, in squared metres.
    /// </summary>
    public readonly record struct SkyWhaleCandidate(long EntityId, double DistanceSquared);

    /// <summary>
    /// WHICH WHALE A PEER HOLDS - and why this is keyed on the ANIMAL when the
    /// mantas' interest is keyed on their ISLAND.
    ///
    /// THE MANTA RULE, first, because this looks like a contradiction of it.
    /// <see cref="IslandFaunaInterestPolicy"/> moved fauna interest OFF the creature
    /// and ON to its island, because a manta ORBITS: its distance to a standing
    /// player oscillates by design, so a creature-keyed radius turned every lap into
    /// a RemoveEntity followed by a fresh AssetLoadRequest and AddEntity. Measured,
    /// that was two crossings a lap on the islands where it happened at all, and the
    /// player reported it as "they kinda despawn".
    ///
    /// A WHALE IS THE OPPOSITE ANIMAL. It does not orbit anything; it transits. Over
    /// one circuit - about twenty minutes - it enters a given peer's sphere ONCE and
    /// leaves ONCE, and that single crossing is not an artefact to be smoothed away,
    /// it IS the feature: the animal arrives, passes overhead for a minute or two,
    /// and goes. So the checkout event and the intended experience are the same
    /// event, and keying on the animal is the only rule that expresses it.
    ///
    /// KEYING IT ON AN ISLAND WOULD BE WORSE IN BOTH DIRECTIONS, which is worth
    /// stating because it was the obvious thing to reuse. The whale belongs to a
    /// REGION several kilometres across; "hold it while you are near an island of
    /// its region" would stream a 173 m animal to a player ten kilometres from it,
    /// and would DROP it while it was directly overhead but happened to be between
    /// two islands - the exact despawn the manta fix was for, reintroduced through
    /// the other door.
    ///
    /// THE BUDGET IS THE SAFETY PROPERTY AND IT IS EXACT, not statistical. At most
    /// <see cref="SkyWhalePolicy.DefaultPerPeerWhales"/> - one - whale is admitted,
    /// so the ceiling this feature adds to a peer's wire is one entity and
    /// 1 / <see cref="SkyWhalePolicy.DefaultPoseInterval"/> = two transform updates a
    /// second, WHATEVER the world's region count. It does not consume a fauna slot,
    /// so the measured 24 x 4 = 96 fauna ceiling is untouched.
    ///
    /// THE CALL USES THE SAME RULE AT A LARGER RADIUS, and that ratio is the whole
    /// of "hear it before you see it": see
    /// <see cref="SkyWhalePolicy.DefaultCallRadiusMetres"/>.
    /// </summary>
    public static class SkyWhaleInterestPolicy
    {
        /// <summary>
        /// The whales this peer should hold, nearest first, given where it is now
        /// and what it already holds.
        ///
        /// RETENTION FIRST, then newcomers, and the two use different radii - the
        /// same hysteresis shape <see cref="IslandInterestAdmissionPolicy"/> uses,
        /// restated here because the unit is an entity rather than an island and the
        /// budget is a COUNT rather than a population. A held whale keeps its place
        /// ahead of a nearer newcomer: swapping one whale for another at a cell
        /// boundary would be a remove and an add of a 19,821-vertex prefab for no
        /// gain, and a player straddling the boundary would see it happen
        /// repeatedly.
        ///
        /// AN INFINITE UNLOAD RADIUS MEANS "retain everything", which is what a peer
        /// that cannot receive RemoveEntity must be given - it could never unload the
        /// animal again, so it must never be asked to.
        /// </summary>
        public static IReadOnlyList<long> Admit(
            IEnumerable<SkyWhaleCandidate> candidates,
            ISet<long> held,
            double loadRadius,
            double unloadRadius,
            int budget)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (held == null) throw new ArgumentNullException(nameof(held));
            if (budget <= 0 || loadRadius <= 0.0)
            {
                return Array.Empty<long>();
            }

            double loadSquared = loadRadius * loadRadius;
            double unloadSquared = double.IsPositiveInfinity(unloadRadius)
                ? double.PositiveInfinity : unloadRadius * unloadRadius;

            List<SkyWhaleCandidate> ordered = candidates
                .OrderBy(candidate => candidate.DistanceSquared)
                .ThenBy(candidate => candidate.EntityId)
                .ToList();

            List<long> admitted = new List<long>(budget);
            foreach (SkyWhaleCandidate candidate in ordered)
            {
                if (admitted.Count >= budget) break;
                if (held.Contains(candidate.EntityId)
                    && candidate.DistanceSquared <= unloadSquared)
                {
                    admitted.Add(candidate.EntityId);
                }
            }
            foreach (SkyWhaleCandidate candidate in ordered)
            {
                if (admitted.Count >= budget) break;
                if (!admitted.Contains(candidate.EntityId)
                    && candidate.DistanceSquared <= loadSquared)
                {
                    admitted.Add(candidate.EntityId);
                }
            }
            return admitted;
        }

        /// <summary>
        /// The lifecycle work that turns <paramref name="loaded"/> into
        /// <paramref name="desired"/>.
        ///
        /// DELIBERATELY <see cref="IslandFaunaInterestPolicy.Reconcile"/> rather
        /// than a copy of it. It is a pure list difference over entity ids with
        /// every REMOVAL LEADING every addition, which is the rule this feature has
        /// to obey for exactly the reason that one does - a peer at its client-side
        /// limit must free a slot before it is asked to fill one - and a second
        /// implementation of a rule two features must not disagree about is how they
        /// come to disagree. Reusing a pure static across a feature boundary costs
        /// nothing: it holds no state and reads no flag.
        /// </summary>
        public static IReadOnlyList<ResourceStreamAction> Reconcile(
            IEnumerable<long> desired, ISet<long> loaded) =>
            IslandFaunaInterestPolicy.Reconcile(desired, loaded);
    }
}
