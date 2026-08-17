using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The safety gate on "log out where you were, log back in there".
    ///
    /// The feature shipped restoring by teleport, which was right, but firing that
    /// teleport unconditionally, which was not: a character whose logout position
    /// was on optional terrain was moved onto an island their client had never been
    /// sent. These assertions pin the four endings - place, hold, spawn point, and
    /// nothing stored - so that cannot come back.
    /// </summary>
    public sealed class SpawnRestorePolicyTests
    {
        private static readonly IslandLocation OnMausoleum = new IslandLocation(
            IslandLocationKind.OnKnownTerrain,
            IslandCatalog.ShatteredMausoleum,
            0.0);

        // --- the destination's terrain is ready: place them -------------------

        [Fact]
        public void Terrain_that_is_checked_out_for_this_peer_places_the_player()
        {
            SpawnRestoreOutcome outcome = SpawnRestorePolicy.Decide(
                PositionRestoreVerdict.Restore,
                OnMausoleum,
                destinationTerrainRegistered: true,
                terrainDecision: TerrainTeleportDecision.Send,
                waitExpired: false);

            Assert.Equal(SpawnRestoreDecision.Place, outcome.Decision);
            Assert.Contains("Shattered Mausoleum", outcome.Reason);
        }

        /// <summary>
        /// The path where the composition actually matters: the readiness half is
        /// <see cref="IslandTerrainTeleportPolicy"/>'s answer, so a Ready status for
        /// this exact peer is what turns into a placement, and nothing else does.
        /// </summary>
        [Fact]
        public void Readiness_is_taken_from_the_terrain_policy_not_assumed()
        {
            TerrainTeleportDecision ready = IslandTerrainTeleportPolicy.Decide(
                terrainManaged: true, destinationKnown: true, terrainReady: true, waitExpired: false);
            TerrainTeleportDecision notReady = IslandTerrainTeleportPolicy.Decide(
                terrainManaged: true, destinationKnown: true, terrainReady: false, waitExpired: false);

            Assert.Equal(SpawnRestoreDecision.Place, SpawnRestorePolicy.Decide(
                PositionRestoreVerdict.Restore, OnMausoleum, true, ready, false).Decision);
            Assert.Equal(SpawnRestoreDecision.HoldForTerrain, SpawnRestorePolicy.Decide(
                PositionRestoreVerdict.Restore, OnMausoleum, true, notReady, false).Decision);
        }

        // --- the destination's terrain is not ready yet: hold them ------------

        [Fact]
        public void Terrain_that_is_still_checking_out_holds_the_player_rather_than_dropping_them()
        {
            SpawnRestoreOutcome outcome = SpawnRestorePolicy.Decide(
                PositionRestoreVerdict.Restore,
                OnMausoleum,
                destinationTerrainRegistered: true,
                terrainDecision: TerrainTeleportDecision.Wait,
                waitExpired: false);

            Assert.Equal(SpawnRestoreDecision.HoldForTerrain, outcome.Decision);
            Assert.Contains("waiting", outcome.Reason);
        }

        // --- the terrain never arrives: fall back, with a reason ---------------

        [Fact]
        public void Terrain_that_never_becomes_ready_falls_back_to_the_spawn_point_with_a_stated_reason()
        {
            SpawnRestoreOutcome outcome = SpawnRestorePolicy.Decide(
                PositionRestoreVerdict.Restore,
                OnMausoleum,
                destinationTerrainRegistered: true,
                terrainDecision: TerrainTeleportDecision.Refuse,
                waitExpired: true);

            Assert.Equal(SpawnRestoreDecision.UseSpawnPoint, outcome.Decision);
            Assert.Contains("bounded wait", outcome.Reason);
            Assert.Contains("spawn point", outcome.Reason);
        }

        /// <summary>
        /// A refusal that is NOT an expiry - terrain nobody local is authoritative
        /// for - must not be reported as a timeout, or the operator chases a wait
        /// that never happened.
        /// </summary>
        [Fact]
        public void A_refusal_that_is_not_a_timeout_says_so()
        {
            SpawnRestoreOutcome outcome = SpawnRestorePolicy.Decide(
                PositionRestoreVerdict.Restore,
                OnMausoleum,
                destinationTerrainRegistered: true,
                terrainDecision: TerrainTeleportDecision.Refuse,
                waitExpired: false);

            Assert.Equal(SpawnRestoreDecision.UseSpawnPoint, outcome.Decision);
            Assert.DoesNotContain("bounded wait", outcome.Reason);
            Assert.Contains("authority host", outcome.Reason);
        }

        /// <summary>
        /// An island the server is not hosting at all this boot is decidable
        /// WITHOUT any terrain query: its ground is not merely late, it does not
        /// exist here. That is a spawn-point case, not a wait.
        /// </summary>
        [Fact]
        public void An_island_that_is_not_registered_on_this_server_is_refused_immediately()
        {
            SpawnRestoreOutcome outcome = SpawnRestorePolicy.Decide(
                PositionRestoreVerdict.Restore,
                OnMausoleum,
                destinationTerrainRegistered: false,
                // Even a Send from the terrain half must not override this.
                terrainDecision: TerrainTeleportDecision.Send,
                waitExpired: false);

            Assert.Equal(SpawnRestoreDecision.UseSpawnPoint, outcome.Decision);
            Assert.Contains("not registered on this server", outcome.Reason);
        }

        // --- no stored position at all ----------------------------------------

        [Fact]
        public void A_character_with_no_stored_position_uses_the_spawn_point_and_never_waits()
        {
            SpawnRestoreOutcome outcome = SpawnRestorePolicy.Decide(
                PositionRestoreVerdict.NoStoredPosition,
                IslandLocation.OpenSky,
                destinationTerrainRegistered: false,
                terrainDecision: TerrainTeleportDecision.Refuse,
                waitExpired: false);

            Assert.Equal(SpawnRestoreDecision.UseSpawnPoint, outcome.Decision);
            Assert.Equal(
                PlayerPositionPolicy.Explain(PositionRestoreVerdict.NoStoredPosition),
                outcome.Reason);
        }

        /// <summary>
        /// Every coordinate-level rejection short-circuits the terrain question
        /// entirely: there is no destination to check out, so a Ready terrain must
        /// not smuggle a refused position back in.
        /// </summary>
        [Theory]
        [InlineData(PositionRestoreVerdict.NoStoredPosition)]
        [InlineData(PositionRestoreVerdict.BelowTheWorld)]
        [InlineData(PositionRestoreVerdict.OutsideTheWorld)]
        [InlineData(PositionRestoreVerdict.AlreadyAtSpawn)]
        public void A_rejected_stored_position_is_never_rescued_by_ready_terrain(
            PositionRestoreVerdict verdict)
        {
            SpawnRestoreOutcome outcome = SpawnRestorePolicy.Decide(
                verdict,
                OnMausoleum,
                destinationTerrainRegistered: true,
                terrainDecision: TerrainTeleportDecision.Send,
                waitExpired: false);

            Assert.Equal(SpawnRestoreDecision.UseSpawnPoint, outcome.Decision);
            Assert.Equal(PlayerPositionPolicy.Explain(verdict), outcome.Reason);
        }

        // --- open sky ----------------------------------------------------------

        /// <summary>
        /// A player who logged out on their ship has no terrain bundle that would
        /// make the destination safe. Refusing them would break the feature for
        /// every crew to guard against a hazard that is not terrain-shaped, so this
        /// places and says plainly that nothing was verified.
        /// </summary>
        [Fact]
        public void A_position_in_open_sky_is_placed_because_there_is_no_terrain_to_wait_for()
        {
            SpawnRestoreOutcome outcome = SpawnRestorePolicy.Decide(
                PositionRestoreVerdict.Restore,
                IslandLocation.OpenSky,
                destinationTerrainRegistered: false,
                terrainDecision: TerrainTeleportDecision.Refuse,
                waitExpired: false);

            Assert.Equal(SpawnRestoreDecision.Place, outcome.Decision);
            Assert.Contains("no terrain checkout to wait for", outcome.Reason);
        }

        // --- the loading-screen hold is bounded twice over ---------------------

        [Fact]
        public void The_loading_screen_hold_never_outlasts_its_own_cap()
        {
            TimeSpan now = TimeSpan.FromSeconds(100);
            TimeSpan generousTerrainDeadline = now + TimeSpan.FromMinutes(5);

            TimeSpan deadline = SpawnRestorePolicy.HoldDeadline(now, generousTerrainDeadline);

            Assert.Equal(now + SpawnRestorePolicy.MaxLoadingScreenHold, deadline);
        }

        [Fact]
        public void The_loading_screen_hold_never_outlasts_the_terrain_wait_either()
        {
            TimeSpan now = TimeSpan.FromSeconds(100);
            TimeSpan shortTerrainDeadline = now + TimeSpan.FromSeconds(3);

            Assert.Equal(shortTerrainDeadline,
                SpawnRestorePolicy.HoldDeadline(now, shortTerrainDeadline));
        }

        /// <summary>
        /// A terrain deadline already in the past must produce a deadline in the
        /// past, so the sweep releases the peer on its very next turn rather than
        /// arming a fresh 45 seconds.
        /// </summary>
        [Fact]
        public void An_already_expired_terrain_wait_does_not_re_arm_the_hold()
        {
            TimeSpan now = TimeSpan.FromSeconds(100);
            TimeSpan past = now - TimeSpan.FromSeconds(1);

            Assert.True(SpawnRestorePolicy.HoldDeadline(now, past) <= now);
        }
    }
}
