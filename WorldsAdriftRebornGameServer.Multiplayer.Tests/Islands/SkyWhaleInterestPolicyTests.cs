using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Regions;
using Xunit;
using Xunit.Abstractions;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// WHO IS SHOWN A WHALE, and the two properties the multiplayer-safety rule is
    /// actually about: that the per-peer cost is bounded by construction, and that
    /// a peer standing still does not get a churn of adds and removes.
    /// </summary>
    public class SkyWhaleInterestPolicyTests
    {
        private readonly ITestOutputHelper _output;

        public SkyWhaleInterestPolicyTests(ITestOutputHelper output) => _output = output;

        private static ISet<long> Held(params long[] ids) => new HashSet<long>(ids);

        [Fact]
        public void Nothing_is_admitted_outside_the_load_radius()
        {
            IReadOnlyList<long> admitted = SkyWhaleInterestPolicy.Admit(
                new[] { new SkyWhaleCandidate(1L, 1300.0 * 1300.0) },
                Held(), 1200.0, 1400.0, 1);
            Assert.Empty(admitted);
        }

        [Fact]
        public void A_held_whale_is_retained_out_to_the_unload_radius()
        {
            SkyWhaleCandidate leaving = new SkyWhaleCandidate(1L, 1300.0 * 1300.0);
            Assert.Equal(new[] { 1L },
                SkyWhaleInterestPolicy.Admit(new[] { leaving }, Held(1L), 1200.0, 1400.0, 1));
            Assert.Empty(
                SkyWhaleInterestPolicy.Admit(
                    new[] { new SkyWhaleCandidate(1L, 1500.0 * 1500.0) },
                    Held(1L), 1200.0, 1400.0, 1));
        }

        [Fact]
        public void A_peer_that_cannot_unload_is_never_asked_to()
        {
            // An infinite unload radius is how the service says "this peer has no
            // channel 5". Dropping a whale it could never remove would leave an
            // inert 19,821-vertex prefab in its world for the rest of the session.
            Assert.Equal(new[] { 1L }, SkyWhaleInterestPolicy.Admit(
                new[] { new SkyWhaleCandidate(1L, 90_000.0 * 90_000.0) },
                Held(1L), 1200.0, double.PositiveInfinity, 1));
        }

        [Fact]
        public void A_held_whale_keeps_its_place_ahead_of_a_nearer_newcomer()
        {
            // THE CELL-BOUNDARY TEST. Two whales in range and a budget of one: the
            // one already loaded stays. Swapping would remove and re-add a full
            // prefab for no gain, repeatedly, for a player parked on the boundary.
            IReadOnlyList<long> admitted = SkyWhaleInterestPolicy.Admit(
                new[]
                {
                    new SkyWhaleCandidate(2L, 100.0 * 100.0),
                    new SkyWhaleCandidate(1L, 900.0 * 900.0),
                },
                Held(1L), 1200.0, 1400.0, 1);
            Assert.Equal(new[] { 1L }, admitted);
        }

        [Fact]
        public void The_budget_is_a_hard_ceiling_however_many_whales_are_in_range()
        {
            IReadOnlyList<long> admitted = SkyWhaleInterestPolicy.Admit(
                Enumerable.Range(0, 20).Select(i => new SkyWhaleCandidate(i, i * 100.0)),
                Held(), 1200.0, 1400.0, SkyWhalePolicy.DefaultPerPeerWhales);
            Assert.Single(admitted);
        }

        [Fact]
        public void A_zero_budget_or_a_zero_radius_is_a_kill_switch()
        {
            SkyWhaleCandidate overhead = new SkyWhaleCandidate(1L, 0.0);
            Assert.Empty(SkyWhaleInterestPolicy.Admit(new[] { overhead }, Held(), 1200.0, 1400.0, 0));
            Assert.Empty(SkyWhaleInterestPolicy.Admit(new[] { overhead }, Held(), 0.0, 0.0, 1));
        }

        [Fact]
        public void Removals_lead_additions()
        {
            IReadOnlyList<ResourceStreamAction> actions =
                SkyWhaleInterestPolicy.Reconcile(new[] { 2L }, Held(1L));
            Assert.Equal(ResourceStreamActionKind.Remove, actions[0].Kind);
            Assert.Equal(1L, actions[0].EntityId);
            Assert.Equal(ResourceStreamActionKind.Add, actions[1].Kind);
            Assert.Equal(2L, actions[1].EntityId);
        }

        [Fact]
        public void The_stated_worst_case_is_two_updates_a_second()
        {
            // The number the standing multiplayer-safety rule asks about, and the
            // one the boot line prints. It is a property of two constants, so it
            // cannot drift without this failing.
            Assert.Equal(2.0, SkyWhalePolicy.WorstCaseUpdatesPerSecond(
                SkyWhalePolicy.DefaultPerPeerWhales, SkyWhalePolicy.DefaultPoseInterval), 9);

            // And it does not come out of the fauna budget: a whale is not a
            // creature, is not in the fauna registry and consumes no fauna slot, so
            // the measured 24 x 4 = 96 ceiling is untouched and the total is 98.
            Assert.Equal(96.0, IslandFaunaInterestPolicy.WorstCaseUpdatesPerSecond(
                IslandFaunaInterestPolicy.DefaultPerPeerCreatures,
                IslandFaunaRegistry.DefaultPoseInterval), 9);
        }

        [Fact]
        public void The_nearest_call_inside_the_radius_is_the_one_heard()
        {
            (long entity, long index) = SkyWhaleInterestPolicy.AdmitCall(
                new[]
                {
                    new SkyWhaleInterestPolicy.SkyWhaleCallCandidate(11L, 7L, 3000.0 * 3000.0),
                    new SkyWhaleInterestPolicy.SkyWhaleCallCandidate(13L, 2L, 1000.0 * 1000.0),
                },
                heldEntityId: 0, heldIndex: 0, loadRadius: 4000.0, unloadRadius: 4200.0);
            Assert.Equal(13L, entity);
            Assert.Equal(2L, index);
        }

        [Fact]
        public void A_call_beyond_the_radius_is_not_heard()
        {
            Assert.Equal((0L, 0L), SkyWhaleInterestPolicy.AdmitCall(
                new[] { new SkyWhaleInterestPolicy.SkyWhaleCallCandidate(11L, 7L, 5000.0 * 5000.0) },
                heldEntityId: 0, heldIndex: 0, loadRadius: 4000.0, unloadRadius: 4200.0));
        }

        [Fact]
        public void A_player_hovering_on_the_call_boundary_is_not_machine_gunned()
        {
            // THE ONE BOUNDARY ON THIS FEATURE THAT A PLAYER CAN SIT STILL ON. A
            // call station does not move for two minutes, so unlike every other
            // radius here the crossing is entirely the player's doing - and a
            // re-add is a fresh 4347 seed, which is a fresh CALL. Without
            // hysteresis, hovering on the line is eight calls a second.
            const double Load = 4000.0, Unload = 4200.0;
            long heldEntity = 0, heldIndex = 0;
            int rings = 0;

            // Drift outward across the line and back, at a metre a step, the way a
            // ship holding position actually wanders.
            foreach (double metres in new[]
            {
                3990.0, 4001.0, 3999.0, 4002.0, 3998.0, 4050.0, 4100.0, 4150.0,
                4199.0, 4150.0, 4100.0, 4001.0, 3999.0,
            })
            {
                (long entity, long index) = SkyWhaleInterestPolicy.AdmitCall(
                    new[]
                    {
                        new SkyWhaleInterestPolicy.SkyWhaleCallCandidate(
                            11L, 7L, metres * metres),
                    },
                    heldEntity, heldIndex, Load, Unload);
                if (entity != heldEntity || index != heldIndex)
                {
                    rings++;
                    heldEntity = entity;
                    heldIndex = index;
                }
            }

            _output.WriteLine("call checkouts while hovering on the boundary: " + rings);
            Assert.Equal(1, rings);
        }

        [Fact]
        public void A_new_call_from_the_same_whale_replaces_the_old_one()
        {
            // The caller's ENTITY ID is reused for every call that whale ever
            // makes, so a rule keyed on the entity alone would hold call 7 forever
            // and never sound call 8. It is the INDEX that means "new event".
            (long entity, long index) = SkyWhaleInterestPolicy.AdmitCall(
                new[] { new SkyWhaleInterestPolicy.SkyWhaleCallCandidate(11L, 8L, 100.0) },
                heldEntityId: 11L, heldIndex: 7L, loadRadius: 4000.0, unloadRadius: 4200.0);
            Assert.Equal(11L, entity);
            Assert.Equal(8L, index);
        }

        [Fact]
        public void A_peer_that_cannot_unload_keeps_the_call_it_was_given()
        {
            // Infinite unload radius is how the service says "no channel 5". A
            // second AddEntity for an id the client still holds would corrupt its
            // entity map, so such a peer must never be asked to swap.
            Assert.Equal((11L, 7L), SkyWhaleInterestPolicy.AdmitCall(
                new[] { new SkyWhaleInterestPolicy.SkyWhaleCallCandidate(11L, 7L, 9e12) },
                heldEntityId: 11L, heldIndex: 7L,
                loadRadius: 4000.0, unloadRadius: double.PositiveInfinity));
        }

        [Fact]
        public void A_standing_player_sees_whole_flybys_rather_than_a_flickering_animal()
        {
            // THE ANTI-CHURN PROPERTY, and the reason whale interest is keyed on the
            // ANIMAL where fauna interest had to be keyed on the ISLAND. A manta
            // orbiting its island crossed a creature-keyed boundary repeatedly and
            // that WAS the reported despawn bug. Walk the real world route past a
            // real island with the real hysteresis and count what a standing player
            // actually gets.
            //
            // THE CLAIM CHANGED SHAPE WITH THE MIGRATION and is worth stating
            // exactly. It used to be "two transitions a lap", because a region ring
            // passed each of its islands once. A single world route can also fly a
            // CROSSING over a cell it is leaving or entering, so an island can get a
            // second, separate flyby in the same lap - and that is not churn, it is
            // two events three quarters of an hour apart. What must still hold, and
            // is what the manta bug actually was, is that every checkout is a WHOLE
            // FLYBY: minutes long, not a flicker, and never more than a handful a
            // lap.
            SkyWhalePlacement placement = SkyWhalePlan
                .Build(ReleaseWorldRolloutPolicy.Select("tier1"))!.Value;
            SkyWhaleCircuit circuit = placement.Circuit;
            SkyWhaleWaypoint standingOn = circuit.Waypoints
                .First(waypoint => !waypoint.IsTransit
                    && waypoint.Region.Value == "release-b3-region");

            double load = SkyWhalePolicy.DefaultLoadRadiusMetres;
            double unload = SkyWhalePolicy.UnloadRadiusFor(load);
            HashSet<long> held = new HashSet<long>();
            List<double> flybys = new List<double>();
            double heldSince = 0.0;
            const double Step = 0.5;

            for (double t = 0.0; t < circuit.CircuitSeconds; t += Step)
            {
                (double x, double y, double z) = circuit.PositionAtTime(t);
                double dx = x - standingOn.X, dy = y - standingOn.Y, dz = z - standingOn.Z;
                IReadOnlyList<long> admitted = SkyWhaleInterestPolicy.Admit(
                    new[] { new SkyWhaleCandidate(1L, (dx * dx) + (dy * dy) + (dz * dz)) },
                    held, load, unload, 1);
                bool wanted = admitted.Count > 0;
                if (wanted == held.Contains(1L)) continue;
                if (wanted) { held.Add(1L); heldSince = t; }
                else { held.Remove(1L); flybys.Add(t - heldSince); }
            }
            if (held.Contains(1L)) flybys.Add(circuit.CircuitSeconds - heldSince);

            _output.WriteLine("over one " + (circuit.CircuitSeconds / 60.0).ToString("0.0")
                + " min world lap, " + standingOn.IslandId + " gets "
                + flybys.Count + " flyby(s): "
                + string.Join(", ", flybys.Select(seconds => seconds.ToString("0") + " s")));

            Assert.InRange(flybys.Count, 1, 3);
            foreach (double seconds in flybys)
            {
                // A whole pass, never a flicker. The manta bug looked like seconds.
                Assert.InRange(seconds, 60.0, 900.0);
            }
        }
    }
}
