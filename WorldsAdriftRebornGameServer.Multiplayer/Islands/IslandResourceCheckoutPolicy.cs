namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// WHICH ISLANDS' RESOURCES A PEER HOLDS - and the fix for the bug that made an
    /// island look empty.
    ///
    /// THE BUG, stated first because this whole type exists to kill it. A player
    /// teleported to Mount Spero (<c>release-887053661</c>, tier 1) and found no
    /// trees and no mineable deposits while manta rays were plainly visible. That
    /// asymmetry is the diagnosis: fauna had already been moved to island-keyed
    /// interest at 600 m, and resources had not - they still checked out per NODE
    /// against the player's own position at the global
    /// <c>WAREBORN_INTEREST_RADIUS_M</c> (120 m load, 155 m unload).
    ///
    /// THE NUMBERS, from the release catalogue's own extracted data. Mount Spero's
    /// AABB is 735 x 320 x 598 m and it carries 19 resource nodes. The player stood
    /// 220 m from the landing point - envelope distance ZERO, unambiguously ON the
    /// island - and a 120 m sphere around them contained exactly TWO of those 19
    /// nodes. The production log for that visit reports "net keys still held on that
    /// island: 2", which is the same number arrived at independently. Standing at the
    /// landing point instead gives 3 of 19, so the island never looked full at any
    /// point of the visit. Across all 46 tier-1 islands the bubble holds a median of
    /// 6 nodes out of a median island content of 13.
    ///
    /// IT IS THE FAUNA BUG'S MIRROR IMAGE. There, the ENTITY moved and the player
    /// stood still; here, the entity is nailed down and the PLAYER moves. Both make
    /// checkout a function of a distance that changes for reasons that have nothing
    /// to do with whether the player is on the island, and both therefore produce
    /// remove/re-add churn: the same visit logged 6 deposit additions and 6 deposit
    /// removals in six minutes. The server was doing the work and immediately
    /// undoing it.
    ///
    /// THE FIX IS THE SAME ONE, AND DELIBERATELY THE SAME CODE.
    /// <see cref="IslandInterestAdmissionPolicy"/> now owns the rule for both:
    /// a peer holds an island's WHOLE content while it is within
    /// <see cref="DefaultLoadRadiusMetres"/> of that ISLAND'S ENVELOPE, and a node's
    /// own distance to the player is no longer an input to the decision - only to
    /// the ORDER things arrive in. Standing anywhere on an island gives an envelope
    /// distance of zero, so the island is held whole for the entire visit.
    ///
    /// WHY THE SAME 600 m AS FAUNA, rather than a number of its own. Three reasons,
    /// and the third is the one that matters:
    /// <list type="number">
    /// <item>it is already MEASURED. Against all 254 release islands' extracted
    ///   AABBs, a peer standing on any island's landing point has exactly ONE island
    ///   within 600 m of its envelope. The world is not dense enough for a second to
    ///   reach, so the radius buys the island you are on plus the approach to it and
    ///   buys nothing else;</item>
    /// <item>it is far inside the terrain gate (<c>WAREBORN_TERRAIN_LOAD_RADIUS_M</c>,
    ///   4000 m in production), so terrain is always long since checked out by the
    ///   time resources are admitted. The terrain gate is a CORRECTNESS requirement -
    ///   never send a resource for ground the client has not loaded - and it is
    ///   unchanged and still enforced per Add;</item>
    /// <item>DIVERGING FROM FAUNA WOULD RECREATE THE REPORTED SYMPTOM. The bug was
    ///   reported as "manta rays but no resources". Any radius here below fauna's
    ///   reproduces exactly that band of distances where one is streamed and the
    ///   other is not. Resources and wildlife should appear together, because to a
    ///   player that single event is "the island loaded".</item>
    /// </list>
    ///
    /// WHAT IT COSTS, and why the wire is not the thing that grows. The send cadence
    /// is unchanged: one lifecycle action per peer per 120 ms, and an addition costs
    /// two of those (an AssetLoadRequest, then the AddEntity a full cadence later).
    /// That is a STRUCTURAL ceiling of 8.33 actions/s per peer that no radius can
    /// move. What changes is how long the queue takes to drain and how much a peer
    /// ends up holding:
    /// <list type="bullet">
    /// <item>peak resources held by one peer: measured max 88 for the largest tier-1
    ///   island (102 across the whole catalogue), against a measured max of 49 for
    ///   the 120 m bubble at a landing point;</item>
    /// <item>the worst case is a PAIR, because the 800 m unload radius can retain a
    ///   departing island while a new one is admitted. Over all 254 islands the
    ///   worst simultaneously-holdable pair is Crimson Paradise (88) + The Land that
    ///   Man Forgot (82) = 170 entities;</item>
    /// <item>time to dress the largest tier-1 island: 88 x 2 x 120 ms = 21 s, with
    ///   the nearest nodes arriving in the first seconds because additions are sorted
    ///   nearest-first.</item>
    /// </list>
    /// </summary>
    public static class IslandResourceCheckoutPolicy
    {
        /// <summary>How near an island a peer must be for its resources to check out.</summary>
        public const string LoadRadiusEnvVar = "WAREBORN_ISLAND_RESOURCE_RADIUS_M";

        /// <summary>How many resource entities one peer may hold at once. The safety valve.</summary>
        public const string PerPeerBudgetEnvVar = "WAREBORN_ISLAND_RESOURCE_PEER_MAX";

        /// <summary>
        /// Default resource load radius, in metres, measured to the island's ENVELOPE.
        /// Six hundred; see the type remarks for why it is fauna's number and not one
        /// of its own.
        /// </summary>
        public const double DefaultLoadRadiusMetres = 600.0;

        /// <summary>
        /// How far past the load radius an island is retained, in metres. Two
        /// hundred, matching fauna, so the two features release an island together.
        /// </summary>
        public const double UnloadMarginMetres = 200.0;

        /// <summary>
        /// How many resource entities one peer may hold at once, by default.
        ///
        /// Five hundred and twelve, and it is a CEILING rather than a target: the
        /// measured worst case a peer can actually reach is 170 (the worst pair of
        /// islands whose envelopes are close enough to be held at once), so this
        /// leaves room for the world to roughly triple - which it is about to, since
        /// tier-1 tree coverage is being extended - without anyone having to
        /// re-derive this number under pressure.
        ///
        /// THE HEADROOM IS NOT DECORATION. Admission is whole-island, so a budget
        /// below an island's own content admits NOTHING for that island: an
        /// undersized cap does not degrade the island, it BLANKS it, which is
        /// indistinguishable from the bug this replaces. That failure mode is why
        /// the default is generous and why <see cref="BudgetWarning"/> exists to say
        /// so at boot rather than let it surface in a player report.
        ///
        /// It is also the queue depth <c>ResourceInterestService</c> already allows
        /// per peer, so a full budget cannot overflow the pending queue.
        /// </summary>
        public const int DefaultPerPeerResources = 512;

        /// <summary>
        /// The load radius an operator configured, or <see cref="DefaultLoadRadiusMetres"/>.
        ///
        /// A NON-POSITIVE VALUE IS ACCEPTED and means "no island resource checkout at
        /// all". Anything unparseable falls back rather than throwing: an environment
        /// typo must never stop a server booting.
        /// </summary>
        public static double LoadRadiusFrom(string? value)
        {
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double radius)
                || double.IsNaN(radius))
            {
                return DefaultLoadRadiusMetres;
            }
            return radius < 0.0 ? 0.0 : Math.Min(radius, InterestPolicy.MaxRadiusMetres);
        }

        /// <summary>The unload radius for a load radius. Zero stays zero, so the kill switch stays killed.</summary>
        public static double UnloadRadiusFor(double loadRadius) =>
            loadRadius <= 0.0 ? 0.0
                : Math.Min(loadRadius + UnloadMarginMetres, InterestPolicy.MaxRadiusMetres);

        /// <summary>
        /// A per-peer entity budget from the operator, or
        /// <see cref="DefaultPerPeerResources"/>. Zero means "hold nothing", which is
        /// the third kill switch; a negative or unparseable value falls back.
        /// </summary>
        public static int PerPeerBudgetFrom(string? raw)
        {
            if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int budget))
            {
                return DefaultPerPeerResources;
            }
            return budget < 0 ? DefaultPerPeerResources : budget;
        }

        /// <summary>
        /// The islands whose resources this peer should hold. A thin,
        /// resource-shaped door onto <see cref="IslandInterestAdmissionPolicy.Admit"/>,
        /// which fauna uses through the same shape.
        /// </summary>
        public static IReadOnlyList<IslandId> Admit(
            IEnumerable<IslandInterestCandidate> candidates,
            ISet<IslandId> held,
            double loadRadius,
            double unloadRadius,
            int perPeerBudget) =>
            IslandInterestAdmissionPolicy.Admit(
                candidates, held, loadRadius, unloadRadius, perPeerBudget);

        /// <summary>
        /// Stamps each offered resource with whether its OWNING ISLAND was admitted.
        /// This is the whole membership rule, and it is deliberately one line: a
        /// node's own position is used to ORDER the work
        /// (<see cref="ResourceInterestPolicy.Reconcile"/>) and never to decide it.
        ///
        /// A resource whose island was not admitted comes back Desired = false, which
        /// is what produces its Remove. Callers must therefore keep already-loaded
        /// resources in the offered set for one more pass, exactly as the old
        /// active-island filter did, or a departing island can never be unloaded.
        /// </summary>
        public static IReadOnlyList<(long Id, FixedPointPosition Position, bool Desired)> Desire(
            IEnumerable<IslandResource> resources,
            ISet<IslandId> admitted)
        {
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            if (admitted == null) throw new ArgumentNullException(nameof(admitted));

            List<(long, FixedPointPosition, bool)> result = new();
            foreach (IslandResource resource in resources)
            {
                result.Add((resource.EntityId, resource.Position,
                    admitted.Contains(resource.IslandId)));
            }
            return result;
        }

        /// <summary>
        /// A boot-time line naming any island whose resource count exceeds the
        /// per-peer budget, or null when every island fits.
        ///
        /// This exists because the failure it catches is SILENT and looks exactly
        /// like the bug this policy fixes: whole-island admission means an island
        /// bigger than the budget is skipped entirely, so a player standing on it
        /// sees nothing at all and reports "the island is empty" a second time. An
        /// operator who tunes the budget down, or a world-generation change that
        /// makes an island much denser, must find out at boot and not from a player.
        /// </summary>
        public static string? BudgetWarning(
            IEnumerable<(IslandId Island, int Count)> islands, int perPeerBudget)
        {
            if (islands == null) throw new ArgumentNullException(nameof(islands));
            if (perPeerBudget <= 0) return null;

            List<(IslandId Island, int Count)> tooBig = islands
                .Where(entry => entry.Count > perPeerBudget)
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.Island.ToString(), StringComparer.Ordinal)
                .ToList();
            if (tooBig.Count == 0) return null;

            IEnumerable<string> shown = tooBig.Take(4)
                .Select(entry => entry.Island + " (" + entry.Count + ")");
            int remaining = tooBig.Count - Math.Min(4, tooBig.Count);
            return tooBig.Count + " island(s) carry more resources than the per-peer"
                + " budget of " + perPeerBudget + " and will therefore be checked out"
                + " to NOBODY: " + string.Join(", ", shown)
                + (remaining > 0 ? " (+" + remaining + " more)" : string.Empty)
                + ". Raise " + PerPeerBudgetEnvVar + " above the largest of them.";
        }
    }
}
