namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// One creature the server intends to seed, together with everything
    /// <see cref="IslandFaunaRegistry.Add"/> needs to take it live.
    /// </summary>
    public readonly record struct FaunaPlacement(
        FaunaCreature Creature,
        IslandDefinition Island,
        IslandTerrainEnvelope Envelope);

    /// <summary>
    /// WHICH creatures a world actually gets, once the world is bigger than the
    /// budget.
    ///
    /// <see cref="IslandFaunaPolicy.PopulationFor"/> answers "who lives on THIS
    /// island" and deliberately knows nothing about any other island or about the
    /// wire. At release-world scale those two facts collide: forty-six tier 1
    /// Wilderness islands each want three creatures, which is 138, while
    /// <see cref="IslandFaunaPolicy.DefaultMaxConcurrent"/> allows 24. Something has
    /// to decide who is left out, and this is that something - kept PURE and
    /// separate so the decision is a testable function rather than a loop body in
    /// the game server.
    ///
    /// TWO RULES, both chosen rather than recovered:
    ///
    /// ENTITY IDS ARE ALLOCATED FROM THE FULL DEMAND, NOT FROM THE SELECTION. Each
    /// island gets a contiguous id block sized by its own complete population,
    /// handed out in island order, BEFORE anything is dropped for budget. So
    /// raising or lowering the cap - or switching the feature off and on - never
    /// moves an existing creature's id onto a different animal. An id that shifted
    /// with a budget change would make the operator's tuning knob a protocol
    /// hazard.
    ///
    /// POPULATIONS ARE SEEDED WHOLE OR NOT AT ALL. The budget is spent island by
    /// island, and an island whose complete population does not fit is SKIPPED
    /// rather than half-filled (a later, smaller population may still fit). This is
    /// the same intent <see cref="IslandFaunaPolicy"/> states for its own counts -
    /// "two rather than one so the perimeter reads as patrolled rather than as a
    /// single lost animal" - applied one level up: spreading 24 creatures thinly
    /// across 46 islands would put exactly one lost manta on each, which is the
    /// reading the counts were chosen to avoid. The cost is stated plainly rather
    /// than hidden: at tier 1 scale most islands carry NO fauna, and
    /// <see cref="Demand"/> exists so a caller can log that fact instead of
    /// discovering it in game.
    /// </summary>
    public static class IslandFaunaPlan
    {
        /// <summary>
        /// How many creatures the world WANTS, ignoring the budget entirely. The
        /// number an operator has to compare the cap against before deciding
        /// whether the cap is the right one for their world.
        /// </summary>
        public static int Demand(IReadOnlyList<ReleaseIslandRecord> islands)
        {
            if (islands == null)
            {
                throw new ArgumentNullException(nameof(islands));
            }

            int demand = 0;
            foreach (ReleaseIslandRecord island in islands)
            {
                demand += IslandFaunaPolicy.PopulationFor(
                    island, IslandFaunaPolicy.FirstFaunaEntityId).Count;
            }
            return demand;
        }

        /// <summary>
        /// The creatures to seed, in the order they should be added, capped at
        /// <paramref name="maxConcurrent"/>.
        ///
        /// Total and deterministic: no clock, no entropy, no state. A zero or
        /// negative budget returns nothing rather than throwing - zero is a
        /// documented second kill switch on
        /// <see cref="IslandFaunaPolicy.ParseBudget"/>, and a feature that refused
        /// to boot because it had been switched off would be a worse bug than the
        /// one it reported.
        /// </summary>
        public static IReadOnlyList<FaunaPlacement> Build(
            IReadOnlyList<ReleaseIslandRecord> islands, int maxConcurrent)
        {
            if (islands == null)
            {
                throw new ArgumentNullException(nameof(islands));
            }

            List<FaunaPlacement> plan = new List<FaunaPlacement>();
            long nextEntityId = IslandFaunaPolicy.FirstFaunaEntityId;

            foreach (ReleaseIslandRecord island in islands)
            {
                IReadOnlyList<FaunaCreature> population =
                    IslandFaunaPolicy.PopulationFor(island, nextEntityId);

                // Advance the id cursor over the WHOLE population, before the budget
                // test, so the block an island owns is a property of the world's
                // shape rather than of how much budget happened to be left.
                nextEntityId += population.Count;

                if (maxConcurrent <= 0 || plan.Count + population.Count > maxConcurrent)
                {
                    continue;
                }

                foreach (FaunaCreature creature in population)
                {
                    plan.Add(new FaunaPlacement(
                        creature, island.Definition, island.Envelope));
                }
            }

            return plan;
        }

        /// <summary>
        /// How many distinct islands a plan actually populates. The one number that
        /// says whether the budget covered the world or a corner of it.
        /// </summary>
        public static int IslandCount(IReadOnlyList<FaunaPlacement> plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            HashSet<IslandId> islands = new HashSet<IslandId>();
            foreach (FaunaPlacement placement in plan)
            {
                islands.Add(placement.Creature.IslandId);
            }
            return islands.Count;
        }
    }
}
