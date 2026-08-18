namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// One island's fauna, as an interest candidate: how far this peer is from the
    /// island's TERRAIN, and how many creatures admitting it would cost.
    /// </summary>
    /// <param name="IslandId">The island whose population this is.</param>
    /// <param name="DistanceSquared">Squared metres from the peer to the island's
    /// ENVELOPE - zero while the peer is standing on it.</param>
    /// <param name="Population">How many live creatures the island carries.</param>
    public readonly record struct FaunaIslandCandidate(
        IslandId IslandId,
        double DistanceSquared,
        int Population);

    /// <summary>
    /// WHICH ISLANDS' FAUNA A PEER HOLDS - and the fix for the bug that made the
    /// wildlife pop in and out.
    ///
    /// THE BUG, stated first because this whole type exists to kill it. Fauna
    /// originally rode <see cref="ResourceInterestPolicy.Reconcile"/> against each
    /// creature's LIVE position, at the global <c>WAREBORN_INTEREST_RADIUS_M</c>
    /// (120 m in production, 155 m unload). That is right for a deposit and wrong
    /// for an animal, and the difference is not a tuning matter: a deposit never
    /// moves, so its distance to a standing player is constant and it checks out
    /// once. A manta ORBITS. Measured against the release catalogue's own extracted
    /// AABBs, with a player standing on the island's landing point, the fraction of
    /// one lap a manta spends inside 120 m is 0% on 30 of the 46 tier-1 islands and
    /// under 30% on all but four. On the rest it crosses the boundary TWICE A LAP,
    /// and each crossing is a RemoveEntity followed a lap later by a fresh
    /// AssetLoadRequest + AddEntity. The production log's repeated
    /// "added MantaRay ... to &lt;same peer&gt;" lines are that cycle. The player
    /// reported it as "manta rays here and there but they kinda despawn", which is
    /// exactly what it is.
    ///
    /// THE FIX: KEY INTEREST ON THE ISLAND, NOT ON THE ANIMAL. A creature's distance
    /// oscillates by design; its ISLAND's does not. So a peer holds an island's
    /// whole population while it is near that ISLAND, and a creature's own position
    /// never enters the decision. Standing anywhere on an island gives an envelope
    /// distance of zero, so the population is held for the entire visit and cannot
    /// flicker no matter how wide the orbit is. This also removes a whole class of
    /// future bug: any movement change - a wider orbit, a faster school, a migration
    /// - is now incapable of causing checkout churn.
    ///
    /// WHY NOT JUST RAISE <c>WAREBORN_INTEREST_RADIUS_M</c>. That radius gates every
    /// tree, deposit, databank and shard in the world. Raising it to cover a manta's
    /// orbit would multiply the entity count on a client that was tuned to 120 fps,
    /// to fix a feature that has at most a couple of dozen entities in it. The cost
    /// would fall almost entirely on the things that did not need it.
    ///
    /// WHY NOT JUST ADD HYSTERESIS. There already is some - unload sits 35 m past
    /// load. It cannot help: the manta is not dithering across the boundary, it is
    /// crossing it decisively and travelling hundreds of metres past it. Widening
    /// the band enough to matter is the same thing as raising the radius.
    ///
    /// THE PER-PEER BUDGET IS THE SAFETY PROPERTY, and it is what lets the world get
    /// busier. The standing multiplayer-safety rule is about the rate ONE PEER
    /// receives, not about how many creatures exist. Admission is capped in
    /// creatures per peer, so the worst case a peer can be sent is
    /// <see cref="DefaultPerPeerCreatures"/> x the pose rate NO MATTER how large the
    /// world's population is. World population and per-peer wire cost are thereby
    /// decoupled: the world can populate every island without moving the number the
    /// soak gate measures.
    ///
    /// ADMISSION IS WHOLE-ISLAND, and that is what keeps the budget itself from
    /// causing churn. If the budget were spent creature by creature, a school
    /// orbiting past the cap boundary would swap members in and out - the original
    /// bug re-introduced through the back door. Islands are admitted whole, ordered
    /// by a distance that only changes when the PLAYER moves, and an island already
    /// held keeps its place ahead of any newcomer. It is the same "whole or not at
    /// all" rule <see cref="IslandFaunaPlan"/> applies to the world budget, for the
    /// same reason.
    /// </summary>
    public static class IslandFaunaInterestPolicy
    {
        /// <summary>How near an island a peer must be for its fauna to check out.</summary>
        public const string LoadRadiusEnvVar = "WAREBORN_ISLAND_FAUNA_RADIUS_M";

        /// <summary>How many creatures one peer may hold at once. The wire safety valve.</summary>
        public const string PerPeerBudgetEnvVar = "WAREBORN_ISLAND_FAUNA_PEER_MAX";

        /// <summary>
        /// Default fauna load radius, in metres, measured to the island's ENVELOPE.
        ///
        /// Six hundred, and the number is measured rather than picked. Against all
        /// 254 release islands' extracted AABBs, a peer standing on any island's
        /// landing point has exactly ONE island within 600 m of its envelope - the
        /// world is not dense enough for a second to reach. So this radius buys the
        /// whole of the island you are on plus the approach to it from a ship, and
        /// buys nothing else. It is five times the resource radius and costs a
        /// fraction as much, because the set it gates is capped and the resource set
        /// is not.
        /// </summary>
        public const double DefaultLoadRadiusMetres = 600.0;

        /// <summary>
        /// How far past the load radius an island is retained, in metres.
        ///
        /// Two hundred, which is wide because the thing it is smoothing is a PLAYER
        /// hovering a ship at the edge of the radius, not a creature. Unlike the
        /// creature-keyed geometry this replaces, nothing here moves on its own, so
        /// this margin is genuinely all the hysteresis the feature needs.
        /// </summary>
        public const double UnloadMarginMetres = 200.0;

        /// <summary>
        /// How many creatures one peer may hold at once, by default.
        ///
        /// Twenty-four, and it is deliberately the number the previous world-wide cap
        /// used, because that is the population the soak gate has already measured
        /// FLAT: 24 creatures at <see cref="IslandFaunaRegistry.DefaultPoseInterval"/>
        /// is a 96 update/s ceiling, and the recorded soak sat at about 68. Moving
        /// the cap from "world-wide" to "per peer" therefore raises the amount of
        /// wildlife in the world without moving the number that was measured.
        ///
        /// It is also comfortably above the largest single-island population the
        /// catalogue can produce (19, on a tier-4 island), so a player standing on an
        /// island always receives ALL of its wildlife and never a truncated school.
        /// </summary>
        public const int DefaultPerPeerCreatures = 24;

        /// <summary>
        /// The load radius an operator configured, or <see cref="DefaultLoadRadiusMetres"/>.
        ///
        /// A NON-POSITIVE VALUE IS ACCEPTED and means "no fauna checkout at all" -
        /// the same third kill switch shape the rest of this feature uses. Anything
        /// unparseable falls back rather than throwing: an environment typo must
        /// never stop a server booting.
        /// </summary>
        public static double LoadRadiusFrom(string? value)
        {
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double radius)
                || double.IsNaN(radius))
            {
                return DefaultLoadRadiusMetres;
            }
            return radius < 0.0 ? 0.0
                : Math.Min(radius, InterestPolicy.MaxRadiusMetres);
        }

        /// <summary>The unload radius for a load radius. Zero stays zero, so the kill switch stays killed.</summary>
        public static double UnloadRadiusFor(double loadRadius) =>
            loadRadius <= 0.0 ? 0.0
                : Math.Min(loadRadius + UnloadMarginMetres, InterestPolicy.MaxRadiusMetres);

        /// <summary>
        /// A per-peer creature budget from the operator, or null to accept
        /// <see cref="DefaultPerPeerCreatures"/>. Shaped exactly like
        /// <see cref="IslandFaunaPolicy.ParseBudget"/>, including zero meaning "none".
        /// </summary>
        public static int? ParsePerPeerBudget(string? raw) => IslandFaunaPolicy.ParseBudget(raw);

        /// <summary>
        /// The islands whose fauna this peer should hold, given where it is now and
        /// what it already holds.
        ///
        /// The rules - hysteresis, retention-first, whole-island-or-nothing - live in
        /// <see cref="IslandInterestAdmissionPolicy.Admit"/>, which island RESOURCES
        /// now share. They were written here first, for the manta despawn; a resource
        /// field turned out to need exactly the same answer for the mirror-image
        /// reason (the player moves rather than the entity), so the rule was lifted
        /// out rather than copied. This method remains the fauna-shaped door onto it.
        /// </summary>
        public static IReadOnlyList<IslandId> Admit(
            IEnumerable<FaunaIslandCandidate> candidates,
            ISet<IslandId> held,
            double loadRadius,
            double unloadRadius,
            int perPeerBudget)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            return IslandInterestAdmissionPolicy.Admit(
                candidates.Select(candidate => new IslandInterestCandidate(
                    candidate.IslandId, candidate.DistanceSquared, candidate.Population)),
                held, loadRadius, unloadRadius, perPeerBudget);
        }

        /// <summary>
        /// The lifecycle work that turns <paramref name="loaded"/> into
        /// <paramref name="desired"/>: every removal first, then every addition.
        ///
        /// REMOVALS LEAD, matching <see cref="ResourceInterestPolicy.Reconcile"/>, so
        /// a peer that is at its client-side limit frees a slot before it is asked to
        /// fill one. Additions are ordered by entity id rather than by distance:
        /// a school's members are contiguous ids by construction
        /// (<see cref="IslandFaunaPolicy.PopulationFor"/>), so this makes a school
        /// arrive together instead of interleaved with another island's.
        /// </summary>
        public static IReadOnlyList<ResourceStreamAction> Reconcile(
            IEnumerable<long> desired, ISet<long> loaded)
        {
            if (desired == null) throw new ArgumentNullException(nameof(desired));
            if (loaded == null) throw new ArgumentNullException(nameof(loaded));

            HashSet<long> want = new HashSet<long>(desired);
            List<ResourceStreamAction> actions = new List<ResourceStreamAction>();

            List<long> removes = loaded.Where(id => !want.Contains(id)).ToList();
            removes.Sort();
            foreach (long id in removes)
            {
                actions.Add(new ResourceStreamAction(ResourceStreamActionKind.Remove, id));
            }

            List<long> adds = want.Where(id => !loaded.Contains(id)).ToList();
            adds.Sort();
            foreach (long id in adds)
            {
                actions.Add(new ResourceStreamAction(ResourceStreamActionKind.Add, id));
            }
            return actions;
        }

        /// <summary>
        /// The most fauna transform updates one peer can receive per second, given a
        /// budget and a pose interval. Reported at boot so the sender cost the
        /// multiplayer-safety rule asks about is a stated number rather than a claim.
        /// </summary>
        public static double WorstCaseUpdatesPerSecond(int perPeerBudget, TimeSpan poseInterval)
        {
            if (poseInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(poseInterval),
                    "a non-positive pose interval has no rate");
            }
            return perPeerBudget <= 0 ? 0.0 : perPeerBudget / poseInterval.TotalSeconds;
        }
    }
}
