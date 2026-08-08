using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The fall floor: where it is, and why it is not somewhere else.
    ///
    /// Every number here is checkable against a file in
    /// docs/research/world-data/ or docs/research/findings-haven.md. That is the
    /// point of the suite - the floor is a safety net whose only failure mode
    /// that anyone will ever notice is being in the WRONG PLACE, and no amount
    /// of playing the game demonstrates that it is in the right one.
    /// </summary>
    public class FallPolicyTests
    {
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; set; }

            public void Advance(TimeSpan by) => Elapsed += by;
        }

        private const long Player = 3;
        private const long OtherPlayer = 4;

        private static FixedPointPosition AtHeight(double metresY)
        {
            // x and z are the spawn point's. Nothing in FallPolicy reads them -
            // and that is deliberate, so a test that says "y" cannot be passing
            // because of an x.
            return FixedPointPosition.FromMetres(
                SpawnPolicy.PlayerSpawnPosition.MetresX,
                metresY,
                SpawnPolicy.PlayerSpawnPosition.MetresZ);
        }

        // ------------------------------------------------------------------
        // WHERE THE FLOOR IS
        // ------------------------------------------------------------------

        [Fact]
        public void The_floor_is_a_hundred_metres_below_the_deepest_point_of_the_island_we_spawn()
        {
            // Haven #5 is at world y -318.669 (SpawnPolicy.IslandPosition, from
            // Bossa's own map file) and its collider mesh bottoms out at
            // island-local -86.0 (meta.localAABB.min[1] of
            // island-surfaces/1431299145.json, 28,616 LOD0 vertices). Those two
            // numbers, and only those two, decide the floor.
            Assert.Equal(-404.669189453125, FallPolicy.RearmMetres, 6);
            Assert.Equal(-504.669189453125, FallPolicy.FloorMetres, 6);

            // And the margin between them is the whole safety argument: 100 m
            // beats the 51 m worst-case error the pre-TRS extractor put into
            // this very dataset (findings-haven.md).
            Assert.Equal(100.0, FallPolicy.RearmMetres - FallPolicy.FloorMetres, 6);
            Assert.True(FallPolicy.RearmMetres - FallPolicy.FloorMetres > 51.0,
                "the margin must beat the largest error this surface data has ever carried");
        }

        [Fact]
        public void Nothing_a_player_can_stand_on_is_below_the_floor()
        {
            // The one thing that must never be true. Haven's deepest collider
            // vertex, the spawn point, and the camp underside (about 34 m below
            // the camp, findings-haven.md) all have to be clear of it.
            Assert.False(FallPolicy.IsBelowFloor(SpawnPolicy.PlayerSpawnPosition));
            Assert.False(FallPolicy.IsBelowFloor(SpawnPolicy.IslandPosition));
            Assert.False(FallPolicy.IsBelowFloor(AtHeight(FallPolicy.RearmMetres)));
            Assert.False(FallPolicy.IsBelowFloor(AtHeight(-318.669189453125 - 34.0)));

            // One unit below the island's deepest point is still not below the
            // floor - that is what the 100 m margin buys.
            Assert.False(FallPolicy.IsBelowFloor(new FixedPointPosition(0, FallPolicy.RearmY - 1, 0)));
        }

        [Fact]
        public void The_safe_destination_is_far_above_the_floor()
        {
            // A rescue that lands you below the floor is an infinite loop, and it
            // would be an infinite loop that teleports a player every five
            // seconds forever. 190 m of clearance.
            TeleportDestination home = TeleportPolicy.SafeDestination;
            Assert.False(FallPolicy.IsBelowFloor(home.Position));
            Assert.True(FallPolicy.IsInTheWorld(home.Position),
                "arriving home must immediately re-arm the watch, or the next fall is not rescued");
            Assert.True(home.Position.MetresY - FallPolicy.FloorMetres > 150.0);
        }

        [Fact]
        public void The_floor_is_exactly_on_the_wire_encoding_no_rounding_drift()
        {
            // The comparison runs against fixed point straight off the wire, so
            // the threshold has to BE fixed point. If this ever becomes a double
            // round-trip it will be wrong by up to a unit and nobody will notice.
            Assert.Equal(SpawnPolicy.IslandPosition.Y - 86L * 4096L, FallPolicy.RearmY);
            Assert.Equal(FallPolicy.RearmY - 100L * 4096L, FallPolicy.FloorY);
        }

        [Fact]
        public void Falling_from_the_camp_reaches_the_floor_in_a_few_seconds()
        {
            // The floor is also a duration: too deep and the rescue arrives long
            // after the player has alt-F4'd. From the underside beneath the camp
            // (about -352.7 m) at Unity's gravity with no drag - which is the
            // SLOWEST this can be, since the client cannot fall slower than
            // gravity - the floor is about 5.6 s away.
            double fromCampUnderside = -318.669189453125 - 34.0;
            double drop = fromCampUnderside - FallPolicy.FloorMetres;
            double seconds = Math.Sqrt(2.0 * drop / 9.81);
            Assert.InRange(seconds, 3.0, 8.0);
        }

        [Fact]
        public void Teleporting_to_a_destination_below_the_floor_bounces_the_player_home()
        {
            // A DOCUMENTED CONSEQUENCE, not an accident. The Shattered Mausoleum
            // destination is at world y -707.1, below a floor derived from Haven,
            // so `echo mausoleum > <trigger>` now sends the player there and the
            // fall floor sends them straight back. That is the correct trade
            // while the mausoleum has no entity spawned at it - it is flagged
            // LandsOnLoadedGround: false precisely because the fall there never
            // ends - but if someone ever spawns that island, THIS TEST is the one
            // that has to be looked at first.
            Assert.True(TeleportPolicy.TryResolve(TeleportPolicy.MausoleumName, out TeleportDestination mausoleum));
            Assert.False(mausoleum.LandsOnLoadedGround);
            Assert.True(FallPolicy.IsBelowFloor(mausoleum.Position));

            // Haven North is above the floor, so a player sent there gets a real
            // fall and a real rescue rather than an instant bounce.
            Assert.True(TeleportPolicy.TryResolve(TeleportPolicy.HavenNorthName, out TeleportDestination north));
            Assert.False(FallPolicy.IsBelowFloor(north.Position));
            Assert.True(FallPolicy.IsInTheWorld(north.Position));
        }

        // ------------------------------------------------------------------
        // ONE RESCUE PER FALL
        // ------------------------------------------------------------------

        [Fact]
        public void Standing_on_the_island_is_never_a_rescue()
        {
            FallWatch watch = new FallWatch(new FakeClock());
            Assert.Equal(FallVerdict.InTheWorld, watch.Observe(Player, SpawnPolicy.PlayerSpawnPosition));
            Assert.Equal(FallVerdict.InTheWorld, watch.Observe(Player, AtHeight(FallPolicy.RearmMetres)));
            Assert.False(watch.IsFalling(Player));
        }

        [Fact]
        public void The_margin_below_the_island_is_watched_but_not_acted_on()
        {
            // Between the island's underside and the floor the server does
            // nothing. That band exists so a rescue can never fire on somebody
            // who is standing on geometry the surface table got wrong.
            FallWatch watch = new FallWatch(new FakeClock());
            Assert.Equal(FallVerdict.Descending, watch.Observe(Player, AtHeight(FallPolicy.RearmMetres - 50.0)));
            Assert.Equal(FallVerdict.Descending, watch.Observe(Player, new FixedPointPosition(0, FallPolicy.FloorY, 0)));
            Assert.False(watch.IsFalling(Player));
        }

        [Fact]
        public void Crossing_the_floor_rescues_once_and_then_stays_quiet()
        {
            // THE POINT OF THE WHOLE CLASS. The client owns its transform and
            // republishes it several times a second while falling; without this
            // the server would teleport the player on every packet for the whole
            // round trip, fighting a client that is still falling.
            FakeClock clock = new FakeClock();
            FallWatch watch = new FallWatch(clock);

            Assert.Equal(FallVerdict.Rescue, watch.Observe(Player, AtHeight(-520.0)));
            for (int packet = 0; packet < 50; packet++)
            {
                clock.Advance(TimeSpan.FromMilliseconds(50));
                Assert.Equal(FallVerdict.RescueInFlight, watch.Observe(Player, AtHeight(-540.0 - packet)));
            }
            Assert.Equal(1, watch.AttemptsFor(Player));
        }

        [Fact]
        public void A_rescue_that_never_took_is_retried()
        {
            // The only evidence a teleport landed is the 1073 ack, and a dropped
            // 190607 looks identical to one that was ignored. If the player is
            // still under the floor five seconds later, try again.
            FakeClock clock = new FakeClock();
            FallWatch watch = new FallWatch(clock);

            Assert.Equal(FallVerdict.Rescue, watch.Observe(Player, AtHeight(-520.0)));
            clock.Advance(FallWatch.RetryInterval - TimeSpan.FromMilliseconds(1));
            Assert.Equal(FallVerdict.RescueInFlight, watch.Observe(Player, AtHeight(-600.0)));

            clock.Advance(TimeSpan.FromMilliseconds(2));
            Assert.Equal(FallVerdict.Rescue, watch.Observe(Player, AtHeight(-620.0)));
            Assert.Equal(2, watch.AttemptsFor(Player));
        }

        [Fact]
        public void A_client_that_ignores_every_rescue_is_reported_once_and_then_left_alone()
        {
            FakeClock clock = new FakeClock();
            FallWatch watch = new FallWatch(clock);

            for (int attempt = 1; attempt <= FallWatch.MaxAttemptsPerFall; attempt++)
            {
                Assert.Equal(FallVerdict.Rescue, watch.Observe(Player, AtHeight(-1000.0 * attempt)));
                Assert.Equal(attempt, watch.AttemptsFor(Player));
                clock.Advance(FallWatch.RetryInterval);
            }

            // The last attempt is owed its interval before being called a
            // failure; it has just had it.
            Assert.Equal(FallVerdict.GaveUp, watch.Observe(Player, AtHeight(-5000.0)));

            // And then silence, forever, however many packets arrive. A rescue
            // every five seconds until the process dies is not a better outcome
            // than one unreadable line.
            for (int packet = 0; packet < 20; packet++)
            {
                clock.Advance(TimeSpan.FromSeconds(30));
                Assert.Equal(FallVerdict.Abandoned, watch.Observe(Player, AtHeight(-9000.0)));
            }
        }

        [Fact]
        public void Getting_home_re_arms_the_watch_immediately()
        {
            // A second genuine fall must not have to wait out the first fall's
            // retry interval - it is a different fall.
            FakeClock clock = new FakeClock();
            FallWatch watch = new FallWatch(clock);

            Assert.Equal(FallVerdict.Rescue, watch.Observe(Player, AtHeight(-520.0)));

            // The teleport lands: the client's next transform is the spawn point.
            Assert.Equal(FallVerdict.InTheWorld, watch.Observe(Player, SpawnPolicy.PlayerSpawnPosition));
            Assert.False(watch.IsFalling(Player));

            // Straight off the edge again, no clock advance at all.
            Assert.Equal(FallVerdict.Rescue, watch.Observe(Player, AtHeight(-520.0)));
            Assert.Equal(1, watch.AttemptsFor(Player));
        }

        [Fact]
        public void One_players_fall_says_nothing_about_anothers()
        {
            FakeClock clock = new FakeClock();
            FallWatch watch = new FallWatch(clock);

            Assert.Equal(FallVerdict.Rescue, watch.Observe(Player, AtHeight(-520.0)));
            Assert.Equal(FallVerdict.Rescue, watch.Observe(OtherPlayer, AtHeight(-520.0)));

            clock.Advance(TimeSpan.FromMilliseconds(100));
            Assert.Equal(FallVerdict.RescueInFlight, watch.Observe(Player, AtHeight(-530.0)));

            Assert.Equal(FallVerdict.InTheWorld, watch.Observe(OtherPlayer, SpawnPolicy.PlayerSpawnPosition));
            Assert.True(watch.IsFalling(Player));
            Assert.False(watch.IsFalling(OtherPlayer));
        }

        [Fact]
        public void A_departed_player_leaves_nothing_behind()
        {
            // Entity ids climb monotonically so a stale record is only wasted
            // memory, but ForgetPeer's contract is that it drops EVERY piece of
            // per-peer state and this is now one of them.
            FakeClock clock = new FakeClock();
            FallWatch watch = new FallWatch(clock);

            Assert.Equal(FallVerdict.Rescue, watch.Observe(Player, AtHeight(-520.0)));
            watch.Forget(Player);
            Assert.False(watch.IsFalling(Player));
            Assert.Equal(0, watch.AttemptsFor(Player));

            // And forgetting re-arms, so a reconnecting player is rescued at once.
            Assert.Equal(FallVerdict.Rescue, watch.Observe(Player, AtHeight(-520.0)));
        }

        // ------------------------------------------------------------------
        // NOT FIGHTING SOMEBODY WHO IS STANDING ON SOMETHING
        // ------------------------------------------------------------------

        [Fact]
        public void A_parented_transform_is_never_judged_against_a_world_floor()
        {
            // A parented 190602 carries the position RELATIVE TO ITS PARENT, so
            // "y = -600" on a deck means 600 m below the ship, not below the
            // world. Rescuing on that number would teleport somebody who is
            // standing perfectly still.
            FallWatch watch = new FallWatch(new FakeClock());

            Assert.Equal(FallVerdict.Parented, watch.Observe(Player, AtHeight(-20000.0), parentPresent: true));
            Assert.True(watch.IsParented(Player));
            Assert.False(watch.IsFalling(Player));
        }

        [Fact]
        public void Being_parented_is_remembered_because_the_client_only_sends_the_change()
        {
            // THE TRAP. The generated writer puts a field on the wire only when it
            // changes, so an entity announces its parent ONCE and then publishes
            // bare positions forever. A watch that read the current packet alone
            // would decline the first update and then rescue on the second.
            FallWatch watch = new FallWatch(new FakeClock());

            Assert.Equal(FallVerdict.Parented, watch.Observe(Player, AtHeight(-600.0), parentPresent: true));
            for (int packet = 0; packet < 10; packet++)
            {
                Assert.Equal(FallVerdict.Parented, watch.Observe(Player, AtHeight(-600.0 - packet)));
            }
        }

        [Fact]
        public void Losing_the_parent_puts_the_entity_back_under_the_floor_rules()
        {
            FallWatch watch = new FallWatch(new FakeClock());

            Assert.Equal(FallVerdict.Parented, watch.Observe(Player, AtHeight(-600.0), parentPresent: true));
            Assert.Equal(FallVerdict.Rescue, watch.Observe(Player, AtHeight(-600.0), parentPresent: false));
            Assert.False(watch.IsParented(Player));
        }

        [Fact]
        public void A_departed_player_leaves_no_parent_flag_behind_either()
        {
            // Entity ids are not reused today, but a parent flag surviving a
            // disconnect would silently disable the floor for whoever inherited
            // the id - a failure with no symptom until somebody falls.
            FallWatch watch = new FallWatch(new FakeClock());

            Assert.Equal(FallVerdict.Parented, watch.Observe(Player, AtHeight(-600.0), parentPresent: true));
            watch.Forget(Player);
            Assert.False(watch.IsParented(Player));
            Assert.Equal(FallVerdict.Rescue, watch.Observe(Player, AtHeight(-600.0)));
        }

        [Fact]
        public void Forgetting_somebody_who_never_fell_is_harmless()
        {
            FallWatch watch = new FallWatch(new FakeClock());
            watch.Forget(Player);
            Assert.False(watch.IsFalling(Player));
        }
    }
}
