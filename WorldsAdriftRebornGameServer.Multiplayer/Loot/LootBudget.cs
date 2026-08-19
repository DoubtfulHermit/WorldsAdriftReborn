namespace WorldsAdriftRebornGameServer.Multiplayer.Loot
{
    /// <summary>
    /// HOW MANY LOOT CONTAINERS AN ISLAND GETS.
    ///
    /// THE FORMULA IS RECOVERED VERBATIM. It is the only part of retail's loot
    /// system that survived in the shipped client, at
    /// <c>acs/LootablePerAreaDataVisualizer.cs:50-62</c>:
    ///
    /// <code>
    ///   DoMath(area, min, areaForMin, max, areaForMax, expLerp):
    ///       if area &lt; areaForMin: return min
    ///       if area &gt; areaForMax: return max
    ///       f = (area - areaForMin) / (areaForMax - areaForMin)
    ///       return min + pow(f, expLerp) * (max - min)
    ///
    ///   containers = (int)(DoMath(...) * extraLootContainersMultiplier)
    ///   chests     = (int)(DoMath(...) * extraLootChestsMultiplier)
    ///   databanks  = (int) DoMath(...)                 // no multiplier
    /// </code>
    ///
    /// Three things follow that are worth not re-deriving:
    ///
    ///   1. <b>Loot volume was driven by AREA, not by tier.</b> The tier decides
    ///      what is inside (see <see cref="LootScrapTable"/>); the area decides how
    ///      much of it there is.
    ///   2. <b>The area is FLAT area, not island area.</b> The caller passes
    ///      <c>IslandSurfaceData.CalculateMostlyFlatAreaInfo(FlatnessThreshold)
    ///      .totalSurfaceArea</c>. Our nearest equivalent is the extracted surface's
    ///      8 m SAMPLE count times 64 m<sup>2</sup> - the same quantity measured a
    ///      different way. See <see cref="SquareMetresPerSample"/> for the trap
    ///      hiding in that sentence.
    ///   3. <b>Ruin piles are NOT in this formula.</b> The component budgets
    ///      databanks, containers and chests and nothing else, so retail's ruin
    ///      piles were authored props rather than an area-scaled population.
    ///
    /// THE CONSTANTS ARE NOT RECOVERED, AND THIS SAYS SO. All nineteen fields of
    /// <c>1244 LootablePerAreaDataState</c> lived on a global-data entity that did
    /// not ship. There is exactly one calibration anchor and it is weak: the
    /// Cardinal Guild survey's real databank counts across all 254 islands are
    /// 247x5, minimum 3, maximum 5, with a correlation against flat area of 0.09.
    /// That pins <c>maxDataBanks = 5</c> and shows the curve was SATURATED for
    /// essentially every island - retail's own budgets were near-flat in practice.
    /// It gives no usable slope, so the container constants below are chosen for
    /// feel and are labelled WAREBORN TUNING.
    ///
    /// Sanity of the chosen curve against the real island population (surface
    /// samples, 254 islands): the smallest island (3 samples) gets the floor of 2,
    /// the 10th percentile (846) gets 5, the median (2,795) gets 9, and everything
    /// from the 75th percentile (5,063) up clamps to the ceiling of 12. That is
    /// 2,243 containers across the whole world, against 13,266 trees. The
    /// ceiling, not the density, sets the world budget - deliberately, and for the
    /// same reason <see cref="Islands.ReleaseTreeBudget"/> is clamped: a peer
    /// arriving at an island checks out that island's WHOLE resource set at about
    /// 0.24 s per entity, so an unbounded count on the largest island would be
    /// paid in streaming time by every player who lands there.
    ///
    /// Pure: arithmetic only. No I/O, no game types.
    /// </summary>
    public static class LootBudget
    {
        /// <summary>
        /// The extracted island surfaces are sampled on an 8 m lattice and keep one
        /// accepted, upward-facing sample per square, so one sample is 64
        /// m<sup>2</sup> of mostly-flat walkable surface. RECOVERED from the surface
        /// extraction (<c>cell: 8.0</c> in every island-surface JSON).
        ///
        /// This is the sample count (<c>meta.candidates</c>), NOT <c>meta.cells</c>.
        /// The latter is the coarse LOD0 MESH cell count - a different quantity by a
        /// factor of about 25, and using it here would under-budget every island in
        /// the world. <see cref="Islands.ReleaseTreeBudget"/> legitimately uses
        /// <c>cells</c> because its density was calibrated against Haven's 90 of
        /// them; this class is transcribing retail's AREA formula, so it needs an
        /// area.
        /// </summary>
        public const double SquareMetresPerSample = 64.0;

        /// <summary>WAREBORN TUNING. Floor: even an islet is worth a look.</summary>
        public const int MinContainers = 2;

        /// <summary>
        /// WAREBORN TUNING. Below this much flat surface an island gets the floor.
        /// 3,200 m<sup>2</sup> is 50 surface samples - a rock in the sky.
        /// </summary>
        public const double AreaForMinContainers = 3200.0;

        /// <summary>
        /// WAREBORN TUNING. Ceiling. Twelve containers is roughly a sixth of an
        /// island's tree count at the same clamp, which keeps loot a find rather
        /// than a harvest.
        /// </summary>
        public const int MaxContainers = 12;

        /// <summary>
        /// WAREBORN TUNING. Above this much flat surface an island gets the ceiling.
        /// 300,000 m<sup>2</sup> is about 4,690 samples, the 71st percentile.
        /// </summary>
        public const double AreaForMaxContainers = 300000.0;

        /// <summary>
        /// WAREBORN TUNING. Below 1 the curve is front-loaded, so a small island
        /// climbs off the floor quickly and a large one approaches the ceiling
        /// slowly. Retail's field is a free float and could have been anything.
        /// </summary>
        public const double ContainersExponentialLerpFactor = 0.55;

        /// <summary>
        /// WAREBORN TUNING. Retail's <c>extraLootContainersMultiplier</c> was a
        /// live event dial - it is how a Bossa weekend could double the world's
        /// loot without a deploy. Neutral here, and kept as a named constant so
        /// that use stays available rather than being folded into the curve.
        /// </summary>
        public const double ExtraContainersMultiplier = 1.0;

        /// <summary>
        /// Retail's clamped exponential lerp, transcribed. Kept as its own method
        /// with retail's own parameter names so it can be read against
        /// <c>LootablePerAreaDataVisualizer.DoMath</c> line for line.
        /// </summary>
        public static double DoMath(
            double area, int min, double areaForMin, int max, double areaForMax, double expLerpFactor)
        {
            if (area < areaForMin) return min;
            if (area > areaForMax) return max;
            if (areaForMax <= areaForMin) return max;

            double f = (area - areaForMin) / (areaForMax - areaForMin);
            return min + System.Math.Pow(f, expLerpFactor) * (max - min);
        }

        /// <summary>
        /// How many containers this much mostly-flat surface earns. The cast to int
        /// is retail's own truncation, applied after the multiplier exactly as
        /// <c>CalculateLootContainers</c> does.
        /// </summary>
        public static int ContainersForArea(double flatAreaSquareMetres)
        {
            double raw = DoMath(
                flatAreaSquareMetres,
                MinContainers, AreaForMinContainers,
                MaxContainers, AreaForMaxContainers,
                ContainersExponentialLerpFactor);

            return (int)(raw * ExtraContainersMultiplier);
        }

        /// <summary>
        /// The same answer from an extracted surface's 8 m sample count, which is how
        /// every island in this world states the size of its walkable surface. This
        /// is the entry point the release-world catalogue and its offline generator
        /// both use, and the one <c>ReleaseLootCatalogTests</c> asserts against every
        /// shipped island so the C# and the Python cannot drift apart.
        /// </summary>
        public static int ContainersForSurfaceSamples(int surfaceSamples)
        {
            if (surfaceSamples <= 0) return 0;
            return ContainersForArea(surfaceSamples * SquareMetresPerSample);
        }
    }
}
