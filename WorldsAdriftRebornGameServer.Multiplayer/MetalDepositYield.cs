namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// WHEN a deposit pays out its metal, and how much per shot.
    ///
    /// RETAIL (worldsadrift.gamepedia.com/Getting_Started, and the Steam "Comprehensive
    /// Guide"): mining a metal node has two distinct stages. First you break the OUTER
    /// SHELL - that stage yields NOTHING, it only opens the rock up. Then
    /// "you will be able to find some pieces of scrap metal sticking out of the rock in
    /// the center", and "using the salvage tool on the scraps will give you 50 metal for
    /// each piece harvested". The metal comes from the CENTRE, in discrete pieces, and
    /// it goes straight to the inventory - there is no chunk lying on the ground to pick
    /// up. Update 31 pins the piece count: "The amount of chunks per metal node is now
    /// always 3."
    ///
    /// THE BUG THIS REPLACES. This server used to grant the whole deposit's metal in one
    /// lump on the shot that DESTROYED the core - the exact opposite of retail, where
    /// destroying the core is the failure case that loses whatever you had not taken
    /// yet. It also meant a player who followed the real game's advice (open the rock,
    /// take the atlas shard, leave the core intact) walked away with nothing.
    ///
    /// WHAT THIS MODEL DOES. The shell stage pays nothing. Once the core is exposed
    /// (<see cref="MetalDepositExposure"/>) the remaining shots free
    /// <see cref="DefaultChunks"/> scrap pieces at evenly spaced shot counts, each
    /// crediting its share of the deposit's total yield. The last piece lands on the
    /// shot BEFORE the core would break, so - as in retail - a careful player can take
    /// all of a node's metal without destroying it.
    ///
    /// WHERE IT DELIBERATELY DIVERGES. Retail's pieces were separate physics entities
    /// (<c>MetalDepositScrap</c> / 2101 <c>MetalRockScrapState</c>) that you aimed at
    /// individually, and destroying the core knocked the un-taken ones loose to roll off
    /// the island. This server does not spawn scrap entities (see the report), so:
    ///   - a piece is freed by the SHOT COUNT rather than by aiming at that piece; and
    ///   - the depletion shot pays out whatever is still owed instead of destroying it.
    /// Losing a player's metal to model a failure state we cannot render honestly would
    /// be hostile, so over-mining is forgiving here. Both are noted as reconstructions,
    /// not measurements.
    ///
    /// Pure: no ENet, no Improbable types, no game install.
    /// </summary>
    public static class MetalDepositYield
    {
        /// <summary>
        /// Scrap pieces in one deposit's core. THREE - Update 31 patch notes, verbatim:
        /// "The amount of chunks per metal node is now always 3. This is an increase of
        /// 1.333x on average." One of the very few hard retail numbers recoverable for
        /// this loop.
        /// </summary>
        public const int DefaultChunks = 3;

        /// <summary>
        /// The chunk count from <c>WAREBORN_DEPOSIT_CHUNKS</c>, or
        /// <see cref="DefaultChunks"/>. Clamped to at least 1; a garbled value falls
        /// back to the default.
        /// </summary>
        public static int Chunks(string? env)
        {
            if (!string.IsNullOrWhiteSpace(env) && int.TryParse(env.Trim(), out int n) && n >= 1)
            {
                return n;
            }
            return DefaultChunks;
        }

        /// <summary>
        /// The shot counts at which each scrap piece comes free, ascending.
        ///
        /// The pieces are spread over the EXPOSED stage only - strictly after
        /// <paramref name="shotsToExpose"/> - and the last one lands at
        /// <paramref name="shotsToDeplete"/> - 1, so the whole yield is obtainable
        /// without breaking the core.
        ///
        /// Returns an EMPTY list when there is no room for a piece before depletion
        /// (a deposit that empties in one or two shots, or an exposure threshold that
        /// leaves no gap). The depletion shot then pays the whole yield -
        /// <see cref="UnitsFor"/> always totals <c>totalUnits</c> over a full mine-out.
        /// </summary>
        public static IReadOnlyList<int> ChunkShots(int shotsToExpose, int shotsToDeplete, int chunks)
        {
            List<int> shots = new List<int>();
            if (chunks < 1 || shotsToDeplete < 1)
            {
                return shots;
            }

            int first = shotsToExpose + 1;
            int last = shotsToDeplete - 1;
            if (first > last)
            {
                return shots;
            }

            int span = last - first + 1;   // how many shots the exposed-but-alive stage has
            for (int k = 1; k <= chunks; k++)
            {
                // Ceiling of k*span/chunks maps chunk k onto the span so that chunk
                // `chunks` always lands on the LAST pre-depletion shot.
                int offset = ((k * span) + chunks - 1) / chunks;
                int shot = first + offset - 1;
                if (shots.Count == 0 || shots[shots.Count - 1] != shot)
                {
                    shots.Add(shot);
                }
            }
            return shots;
        }

        /// <summary>
        /// How <paramref name="totalUnits"/> divides between <paramref name="chunks"/>
        /// pieces, largest-remainder style so the parts always sum to the total and no
        /// piece is empty when the total allows it.
        /// </summary>
        public static IReadOnlyList<int> ChunkUnits(int totalUnits, int chunks)
        {
            List<int> units = new List<int>();
            if (chunks < 1)
            {
                return units;
            }
            if (totalUnits < 0)
            {
                totalUnits = 0;
            }

            long previous = 0;
            for (int k = 1; k <= chunks; k++)
            {
                long cumulative = ((long)totalUnits * k) / chunks;
                units.Add((int)(cumulative - previous));
                previous = cumulative;
            }
            return units;
        }

        /// <summary>
        /// Units of metal the shot that brought a deposit to <paramref name="hits"/>
        /// salvage shots credits.
        ///
        /// Zero through the whole shell stage. A piece's share on each of the chunk
        /// shots. On the depletion shot, whatever is still owed (zero when every piece
        /// was already taken - the retail-shaped case where a careful player got
        /// everything before breaking the core).
        ///
        /// Summing this over hits 1..<paramref name="shotsToDeplete"/> always gives
        /// exactly <paramref name="totalUnits"/>.
        /// </summary>
        public static int UnitsFor(int hits, int shotsToExpose, int shotsToDeplete,
            int totalUnits, int chunks)
        {
            if (hits < 1 || hits > shotsToDeplete || chunks < 1)
            {
                return 0;
            }
            if (totalUnits < 0)
            {
                totalUnits = 0;
            }

            IReadOnlyList<int> shots = ChunkShots(shotsToExpose, shotsToDeplete, chunks);
            IReadOnlyList<int> units = ChunkUnits(totalUnits, chunks);

            int granted = 0;
            int awardedBefore = 0;
            for (int k = 0; k < units.Count; k++)
            {
                // ChunkShots collapses duplicates, so map chunk k onto the shot it would
                // have landed on and let several chunks share one shot.
                int shot = k < shots.Count ? shots[k] : (shots.Count == 0 ? -1 : shots[shots.Count - 1]);
                if (shot < 0)
                {
                    continue;
                }
                if (shot == hits)
                {
                    granted += units[k];
                }
                if (shot <= shotsToDeplete)
                {
                    awardedBefore += units[k];
                }
            }

            if (hits == shotsToDeplete)
            {
                // Everything the pieces did not cover (all of it when there was no room
                // for a piece at all).
                granted += totalUnits - awardedBefore;
            }

            return granted;
        }

        /// <summary>
        /// <see cref="UnitsFor(int,int,int,int,int)"/> with the chunk count read from
        /// the environment - the one call the wire glue makes.
        /// </summary>
        public static int UnitsFor(int hits, int shotsToExpose, int shotsToDeplete, int totalUnits) =>
            UnitsFor(hits, shotsToExpose, shotsToDeplete, totalUnits,
                Chunks(System.Environment.GetEnvironmentVariable("WAREBORN_DEPOSIT_CHUNKS")));
    }
}
