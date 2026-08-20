namespace WorldsAdriftRebornGameServer.Multiplayer.Walls
{
    /// <summary>
    /// THE RELEASE MAP'S WEATHER WALLS AS SPAWNABLE ENTITIES - the seam between
    /// <see cref="WallCatalog"/> and the world registry, and the mirror of
    /// <c>Islands.ReleaseWorldTrees</c> for a different kind of world content.
    ///
    /// One entity per wall, 44 of them, ungated by interest. That is not a shortcut:
    /// <c>WallData.Add</c> merges every segment sharing a wall id into their axial
    /// extent, so one entity reproduces retail's distance field EXACTLY rather than
    /// approximately (findings-storm-walls.md section 6). Retail's thousands of
    /// segments were an interest-management device for a checkout radius we do not
    /// have.
    ///
    /// <c>SpawnOrder.AfterPlayer</c> throughout: nobody stands on a wall - it has no
    /// collider at all - and every step ordered before the player is a step the
    /// loading screen waits on.
    /// </summary>
    public static class WorldWalls
    {
        /// <summary>
        /// Every wall this server should register, or nothing at all when the feature
        /// is off.
        /// </summary>
        /// <param name="enabled">
        /// <see cref="WallPolicy.Enabled(string?)"/>'s answer. False yields an empty
        /// sequence, which is what makes "off" byte-identical on the wire: with no
        /// registration there is no entity id, no AddEntityOp, no asset request and
        /// no component seed.
        /// </param>
        /// <param name="typesEnv">
        /// The raw <c>WAREBORN_WALL_TYPES</c> value; null for every type. The lever
        /// for the ambient-bolt cost - see <see cref="WallPolicy.TypesEnvVar"/>.
        /// </param>
        public static IEnumerable<WorldEntity> All(bool enabled, string? typesEnv = null)
        {
            if (!enabled)
            {
                yield break;
            }

            IReadOnlyCollection<WallType> types = WallPolicy.SelectedTypes(typesEnv);
            foreach (WallSegmentSeed wall in WallCatalog.All)
            {
                if (!types.Contains(wall.Type))
                {
                    continue;
                }

                yield return new WorldEntity(
                    WallPolicy.KeyFor(wall.WallId),
                    WallPolicy.PrefabName,
                    WorldEntities.DefaultAssetContext,
                    wall.Midpoint,
                    // Transform FIRST, then 1204. The order is what keeps the wall
                    // where we put it - see WallPolicy.SeedComponents.
                    seedComponents: WallPolicy.SeedComponents,
                    order: SpawnOrder.AfterPlayer);
            }
        }

        /// <summary>
        /// What serving <paramref name="typesEnv"/>'s walls costs, as one line for the
        /// boot banner. The storm-rift kilometrage is the number that matters: it is
        /// the linear input to the world-wide ambient-bolt spawn rate, and it is the
        /// same for a player standing next to a rift and a player 30 km away.
        /// </summary>
        public static string Describe(bool enabled, string? typesEnv = null)
        {
            if (!enabled)
            {
                return "weather walls: OFF (" + WallPolicy.EnabledEnvVar + " unset)";
            }

            List<WallSegmentSeed> served = new();
            IReadOnlyCollection<WallType> types = WallPolicy.SelectedTypes(typesEnv);
            foreach (WallSegmentSeed wall in WallCatalog.All)
            {
                if (types.Contains(wall.Type))
                {
                    served.Add(wall);
                }
            }

            double stormKm = WallCatalog.StormWallLengthMetres(served) / 1000.0;
            int rifts = served.Count(w => w.Type == WallType.StormRift);
            return "weather walls: ON, " + served.Count + " of " + WallCatalog.All.Count
                + " served (" + rifts + " storm rift(s), "
                + stormKm.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
                + " km of storm wall -> that many km drive the world-wide ambient-bolt rate)";
        }
    }
}
