using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>What to do with a returning player who has a stored logout position.</summary>
    public enum SpawnRestoreDecision
    {
        /// <summary>Send the restore teleport now. The ground is there.</summary>
        Place,

        /// <summary>
        /// Do not place them yet, and do not give up. Their destination terrain is
        /// checking out; hold them - on the loading screen if they are still behind
        /// it, at the spawn point otherwise - and ask again next poll.
        /// </summary>
        HoldForTerrain,

        /// <summary>
        /// Leave them at the spawn point, permanently, for this session. Always
        /// carries a stated reason.
        /// </summary>
        UseSpawnPoint,
    }

    /// <summary>One decision and the sentence that explains it in the log.</summary>
    public readonly record struct SpawnRestoreOutcome(SpawnRestoreDecision Decision, string Reason);

    /// <summary>
    /// Whether a returning player may be put back where they logged out YET.
    ///
    /// THE BUG THIS EXISTS FOR. Logout-position persistence restores by teleport,
    /// which was the right call (re-seeding 190602 on a live entity is the
    /// out-of-world spawn bug <see cref="SpawnPolicy"/> and <see cref="MirrorSendPolicy"/>
    /// are emphatic about). But the teleport was fired directly, bypassing the
    /// terrain-readiness deferral the operator teleport path already had, and its
    /// destination named no island - so a player whose logout position was on
    /// OPTIONAL terrain, checked out only by proximity, was moved 4 km to an island
    /// that was not on their client yet and fell through the place it should have
    /// been. Shattered Mausoleum sits 4425 m from the Haven spawn, past a 4000 m
    /// load radius, so its terrain was never even requested.
    ///
    /// WHAT THIS DOES AND DOES NOT KNOW. It does not know whether there is ground
    /// under a point - the server has no terrain query, and
    /// <see cref="PlayerPositionPolicy"/> says so plainly. It composes three things
    /// it CAN know: whether the stored coordinates are usable at all
    /// (<see cref="PlayerPositionPolicy.Decide"/>), which island's terrain bundle
    /// that point belongs to (<see cref="IslandLocationPolicy"/>), and whether that
    /// bundle is checked out and acknowledged for this exact peer
    /// (<see cref="IslandTerrainTeleportPolicy"/>). "The right terrain is on the
    /// client" is strictly weaker than "there is solid ground here", and it is the
    /// strongest claim this server is entitled to make.
    ///
    /// EVERY PATH IS BOUNDED. There is no outcome that waits forever: the terrain
    /// wait carries the caller's deadline and turns into
    /// <see cref="SpawnRestoreDecision.UseSpawnPoint"/> when it expires, and the
    /// loading-screen hold is separately capped by
    /// <see cref="MaxLoadingScreenHold"/>. Standing at the spawn point is a bad
    /// afternoon; falling out of the world, or a loading screen that never lifts,
    /// is a lost session.
    /// </summary>
    public static class SpawnRestorePolicy
    {
        /// <summary>
        /// The longest the loading screen may be held open purely because a
        /// restore is waiting for its destination terrain.
        ///
        /// Deliberately independent of the terrain wait's own deadline, and
        /// deliberately shorter than its worst case (the asset-ack timeout may be
        /// configured up to 120 s). Past this the player is released into the world
        /// AT THE SPAWN POINT and the terrain wait carries on behind them: they are
        /// standing somewhere real and can move, which beats staring at a loading
        /// screen for two minutes. See <see cref="HoldDeadline"/>.
        /// </summary>
        public static readonly TimeSpan MaxLoadingScreenHold = TimeSpan.FromSeconds(45);

        /// <summary>
        /// When a held loading screen must be released regardless: the earlier of
        /// the terrain wait's own deadline and <see cref="MaxLoadingScreenHold"/>
        /// from now. Never later than either, so neither bound can be escaped by
        /// the other being generous.
        /// </summary>
        public static TimeSpan HoldDeadline(TimeSpan now, TimeSpan terrainDeadline)
        {
            TimeSpan capped = now + MaxLoadingScreenHold;
            return terrainDeadline < capped ? terrainDeadline : capped;
        }

        /// <summary>
        /// The decision, composed from the three questions above.
        /// </summary>
        /// <param name="positionVerdict">
        /// <see cref="PlayerPositionPolicy.Decide"/>'s answer about the stored
        /// coordinates themselves. Anything but
        /// <see cref="PositionRestoreVerdict.Restore"/> - no stored position at all,
        /// below the world, outside the world, already at spawn - short-circuits
        /// everything else, because there is nothing to restore.
        /// </param>
        /// <param name="location">
        /// Which island the stored point stands on, from
        /// <see cref="IslandLocationPolicy.Locate"/>.
        /// </param>
        /// <param name="destinationTerrainRegistered">
        /// Whether that island's terrain exists on THIS server this boot. False for
        /// an island outside the registered topology - a district that is not rolled
        /// out, a rollout that shrank since the player logged out.
        /// </param>
        /// <param name="terrainDecision">
        /// <see cref="IslandTerrainTeleportPolicy.Decide"/>'s answer for this peer
        /// and this island.
        /// </param>
        /// <param name="waitExpired">
        /// Whether the bounded terrain wait has already run out. Used only to say
        /// WHY a refusal happened; the refusal itself is
        /// <paramref name="terrainDecision"/>'s call.
        /// </param>
        public static SpawnRestoreOutcome Decide(
            PositionRestoreVerdict positionVerdict,
            IslandLocation location,
            bool destinationTerrainRegistered,
            TerrainTeleportDecision terrainDecision,
            bool waitExpired)
        {
            if (positionVerdict != PositionRestoreVerdict.Restore)
            {
                return new SpawnRestoreOutcome(
                    SpawnRestoreDecision.UseSpawnPoint,
                    PlayerPositionPolicy.Explain(positionVerdict));
            }

            if (location.Kind != IslandLocationKind.OnKnownTerrain || location.Island == null)
            {
                // Nothing to wait for. A player who logged out on their ship, or in
                // the void, has no terrain bundle that would make their destination
                // safe, and refusing them would break the feature for every ship
                // crew to protect them from a hazard that is not terrain-shaped.
                // The deep fall net and F10 remain what they always were here.
                return new SpawnRestoreOutcome(
                    SpawnRestoreDecision.Place,
                    "the stored position is not on any island this server knows the shape of,"
                        + " so there is no terrain checkout to wait for");
            }

            string island = "'" + location.Island.DisplayName + "'";

            if (!destinationTerrainRegistered)
            {
                return new SpawnRestoreOutcome(
                    SpawnRestoreDecision.UseSpawnPoint,
                    "the island it is on (" + island + ") is not registered on this server,"
                        + " so that ground does not exist here at all; using the spawn point");
            }

            switch (terrainDecision)
            {
                case TerrainTeleportDecision.Send:
                    return new SpawnRestoreOutcome(
                        SpawnRestoreDecision.Place,
                        "the terrain of " + island + " is checked out for this peer");

                case TerrainTeleportDecision.Wait:
                    return new SpawnRestoreOutcome(
                        SpawnRestoreDecision.HoldForTerrain,
                        "waiting for the terrain of " + island + " to check out for this peer");

                default:
                    return new SpawnRestoreOutcome(
                        SpawnRestoreDecision.UseSpawnPoint,
                        waitExpired
                            ? "the terrain of " + island + " did not become ready within the"
                                + " bounded wait; using the spawn point"
                            : "the terrain of " + island + " is not managed by the local"
                                + " authority host; using the spawn point");
            }
        }
    }
}
