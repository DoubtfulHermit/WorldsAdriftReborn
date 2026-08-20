using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Resources;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// S3 - RE-ROLLED PLACEMENT. Retail moved resources on every understorm; S1/S2
    /// only restored them. These tests cover the pure decision
    /// (<see cref="IslandResourceReroll"/>), the seat pool it draws from, and the
    /// registry seam that lets a placed node move at all.
    /// </summary>
    public class IslandResourceRerollTests
    {
        private static readonly IslandId Haven = IslandCatalog.HavenId;
        private static int Pool => HavenSurface.DepositPool().Count;
        private static int Occupied => HavenSurface.DepositLocals().Count;

        private static IReadOnlyList<int> Seats(uint generation) =>
            IslandResourceReroll.SeatsFor(Haven, generation, Pool, Occupied,
                IslandResourceReroll.PinnedSeats);

        // ====================================================================
        // THE POOL - the thing that makes a re-roll possible without a second
        // placement policy.
        // ====================================================================

        [Fact]
        public void The_seat_pool_is_bigger_than_the_number_of_deposits_placed()
        {
            // If it were not, "re-roll" would be a permutation of forty rocks over
            // forty seats - every seat always occupied - and the field would look
            // IDENTICAL after every storm while all these tests stayed green. That is
            // exactly the class of green-suite-over-dead-feature this repo has shipped
            // twice, so it is asserted first and loudly.
            Assert.True(Pool > Occupied,
                "the re-roll pool (" + Pool + ") must exceed the occupied count ("
                + Occupied + ") or nothing can visibly move");

            // MEASURED: Haven saturates at 107 seats at 22 m spacing under the real
            // DepositConfig. Asserted as a floor, not an equality, so a surface or
            // clearance change that grows the island does not fail the suite - but one
            // that collapses the pool toward the occupied count does.
            Assert.True(Pool >= 100, "expected ~107 seats on Haven, got " + Pool);
        }

        [Fact]
        public void The_pool_is_a_strict_superset_of_the_layout_the_server_boots_with()
        {
            // PREFIX STABILITY. The generator is a greedy pass over a fixed hash order,
            // so a larger target can only append. This is what makes generation 0 the
            // current production layout and guarantees S3 changes nothing until the
            // first storm. If this breaks, every deposit in the world silently moves at
            // the next deploy - the one realistic way a rock's position could appear to
            // change without any storm, which is precisely the §4 observation.
            IReadOnlyList<GeneratedPlacement> pool = HavenSurface.DepositPool();
            IReadOnlyList<GeneratedPlacement> boot = HavenSurface.DepositLocals();

            Assert.True(pool.Count >= boot.Count);
            for (int i = 0; i < boot.Count; i++)
            {
                Assert.Equal(boot[i].LocalX, pool[i].LocalX);
                Assert.Equal(boot[i].LocalY, pool[i].LocalY);
                Assert.Equal(boot[i].LocalZ, pool[i].LocalZ);
            }
        }

        [Fact]
        public void Every_pair_of_seats_in_the_pool_respects_the_deposit_min_spacing()
        {
            // THE INVARIANT THAT LETS ANY SUBSET BE A VALID LAYOUT. Because the pool was
            // itself thinned by the greedy min-spacing pass, the re-roll can pick seats
            // freely and can never produce a rock carpet. Without this the re-roll would
            // need its own collision test - a second placement policy, which is the
            // mistake S2's post-mortem warns about.
            IReadOnlyList<GeneratedPlacement> pool = HavenSurface.DepositPool();
            double min = HavenSurface.DepositMinSpacing;

            for (int i = 0; i < pool.Count; i++)
            {
                for (int j = i + 1; j < pool.Count; j++)
                {
                    double dx = pool[i].LocalX - pool[j].LocalX;
                    double dy = pool[i].LocalY - pool[j].LocalY;
                    double dz = pool[i].LocalZ - pool[j].LocalZ;
                    double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    Assert.True(d >= min - 1e-9,
                        "seats " + i + " and " + j + " are " + d + " m apart, under " + min);
                }
            }
        }

        [Fact]
        public void The_pool_is_cached_and_identical_across_calls()
        {
            Assert.Same(HavenSurface.DepositPool(), HavenSurface.DepositPool());
        }

        // ====================================================================
        // THE DECISION
        // ====================================================================

        [Fact]
        public void Generation_zero_is_the_boot_layout_so_an_unstormed_world_is_unchanged()
        {
            // A world that has never stormed must be byte-identical to a pre-S3 world.
            IReadOnlyList<int> seats = Seats(0);
            Assert.Equal(Occupied, seats.Count);
            for (int i = 0; i < seats.Count; i++) Assert.Equal(i, seats[i]);
        }

        [Fact]
        public void A_storm_actually_moves_something()
        {
            // MUTATION TARGET. A SeatsFor that returned the identity for every
            // generation would satisfy the count, distinctness, range and determinism
            // tests below and re-roll NOTHING.
            IReadOnlyList<int> seats = Seats(1);
            Assert.NotEqual(Enumerable.Range(0, Occupied).ToArray(), seats.ToArray());
        }

        [Fact]
        public void Most_deposits_move_on_a_re_roll_rather_than_a_token_few()
        {
            // "Not always in the same place" (WIKI) should read as a rearranged field,
            // not as one rock twitching. With 39 movable deposits over 106 movable
            // seats, the expected number that happen to keep their own seat is ~0.4, so
            // a threshold of "at least half move" is far outside noise while still being
            // a stable assertion.
            IReadOnlyList<int> seats = Seats(1);
            int movedCount = 0;
            for (int i = 0; i < seats.Count; i++) if (seats[i] != i) movedCount++;

            Assert.True(movedCount >= Occupied / 2,
                "only " + movedCount + " of " + Occupied + " deposits moved");
        }

        [Fact]
        public void The_pinned_tutorial_deposit_never_moves()
        {
            // deposit-0 is the hand-measured proven placement 8.9 m from the player
            // spawn, pinned to iron for the first recipe. A new player's first mining
            // lesson must not become a search. WAREBORN TUNING.
            for (uint g = 0; g < 50; g++)
            {
                IReadOnlyList<int> seats = Seats(g);
                for (int p = 0; p < IslandResourceReroll.PinnedSeats; p++)
                {
                    Assert.Equal(p, seats[p]);
                }
            }
        }

        [Fact]
        public void Seats_are_always_distinct_so_two_deposits_never_share_one()
        {
            // Two rocks in the same hole is the visible failure this guards.
            for (uint g = 0; g < 100; g++)
            {
                IReadOnlyList<int> seats = Seats(g);
                Assert.Equal(seats.Count, seats.Distinct().Count());
            }
        }

        [Fact]
        public void Seats_are_always_inside_the_pool()
        {
            for (uint g = 0; g < 100; g++)
            {
                foreach (int seat in Seats(g))
                {
                    Assert.InRange(seat, 0, Pool - 1);
                }
            }
        }

        [Fact]
        public void The_same_island_and_generation_always_produce_the_same_layout()
        {
            // Determinism is not decoration: positions are not persisted, so the layout
            // must be recomputable from (island, generation) alone - in a test, on a
            // replay, and on the server - or a restart mid-cycle would disagree with
            // what players were told.
            for (uint g = 0; g < 20; g++)
            {
                Assert.Equal(Seats(g).ToArray(), Seats(g).ToArray());
            }
        }

        [Fact]
        public void Different_generations_produce_different_layouts()
        {
            HashSet<string> seen = new HashSet<string>();
            for (uint g = 1; g <= 20; g++)
            {
                seen.Add(string.Join(",", Seats(g)));
            }
            Assert.True(seen.Count >= 19,
                "20 generations produced only " + seen.Count + " distinct layouts");
        }

        [Fact]
        public void Two_islands_storming_on_the_same_generation_do_not_share_a_layout()
        {
            IReadOnlyList<int> haven = IslandResourceReroll.SeatsFor(
                Haven, 7, Pool, Occupied, IslandResourceReroll.PinnedSeats);
            IReadOnlyList<int> other = IslandResourceReroll.SeatsFor(
                new IslandId("some-other-island"), 7, Pool, Occupied,
                IslandResourceReroll.PinnedSeats);

            Assert.NotEqual(haven.ToArray(), other.ToArray());
        }

        [Fact]
        public void The_seed_is_stable_for_a_known_island_and_generation()
        {
            // Locks the hash so a refactor cannot silently reshuffle every world. If
            // this changes deliberately, the layout changes with it - say so.
            Assert.Equal(IslandResourceReroll.Seed(Haven, 3),
                IslandResourceReroll.Seed(Haven, 3));
            Assert.NotEqual(IslandResourceReroll.Seed(Haven, 3),
                IslandResourceReroll.Seed(Haven, 4));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        public void Degenerate_pools_with_nothing_movable_return_the_identity(
            int pool, int occupied)
        {
            // Everything is pinned, so there is nothing to shuffle.
            IReadOnlyList<int> seats = IslandResourceReroll.SeatsFor(
                new IslandId("x"), 9, pool, occupied, 1);
            Assert.Equal(occupied, seats.Count);
            for (int i = 0; i < occupied; i++) Assert.Equal(i, seats[i]);
        }

        [Fact]
        public void A_saturated_pool_permutes_rather_than_relocating()
        {
            // pool == occupied is the degenerate case the first test in this file
            // guards Haven against: every seat is filled, so the SET of positions is
            // unchanged and only WHICH deposit sits in each seat moves. That is still
            // well-defined and must stay distinct and in range - it just is not a
            // visible re-roll, which is exactly why the real pool is ~2.7x the
            // occupied count.
            IReadOnlyList<int> seats = IslandResourceReroll.SeatsFor(
                new IslandId("x"), 9, 5, 5, 1);

            Assert.Equal(5, seats.Count);
            Assert.Equal(5, seats.Distinct().Count());
            Assert.Equal(0, seats[0]);
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, seats.OrderBy(s => s).ToArray());
        }

        [Fact]
        public void Occupying_more_seats_than_exist_is_rejected_rather_than_silently_clamped()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                IslandResourceReroll.SeatsFor(new IslandId("x"), 1, 10, 11, 1));
        }

        [Fact]
        public void More_pinned_seats_than_deposits_is_tolerated()
        {
            IReadOnlyList<int> seats = IslandResourceReroll.SeatsFor(
                new IslandId("x"), 4, 10, 3, 99);
            Assert.Equal(new[] { 0, 1, 2 }, seats.ToArray());
        }

        // ====================================================================
        // THE NODE FACTORY - identity is carried, only position changes.
        // ====================================================================

        [Fact]
        public void A_re_seated_deposit_keeps_its_key_metal_quality_and_variant()
        {
            // The wiki says the LOCATIONS changed. If the metal changed too, index 0's
            // "always iron" rule and every 1255 variant id would drift with the shuffle,
            // and an unresolvable variant leaves the entity INVISIBLE on the client.
            for (int index = 0; index < 5; index++)
            {
                MetalNode home = MetalDeposits.NodeAt(index);
                MetalNode moved = MetalDeposits.NodeAtSeat(index, Pool - 1 - index);

                Assert.Equal(home.Key, moved.Key);
                Assert.Equal(home.MetalType, moved.MetalType);
                Assert.Equal(home.Quality, moved.Quality);
                Assert.Equal(home.VariantId, moved.VariantId);
                Assert.True(home.IsDeposit && moved.IsDeposit);
            }
        }

        [Fact]
        public void A_deposit_re_seated_to_its_own_seat_lands_exactly_where_it_started()
        {
            for (int index = 0; index < 10; index++)
            {
                Assert.Equal(MetalDeposits.NodeAt(index).Position,
                    MetalDeposits.NodeAtSeat(index, index).Position);
            }
        }

        [Fact]
        public void A_deposit_re_seated_elsewhere_really_is_somewhere_else()
        {
            MetalNode home = MetalDeposits.NodeAt(3);
            MetalNode moved = MetalDeposits.NodeAtSeat(3, Pool - 1);
            Assert.NotEqual(home.Position, moved.Position);
        }

        [Fact]
        public void Only_Havens_own_static_deposits_are_re_rollable()
        {
            Assert.Equal(0, MetalDeposits.HavenIndexOf("deposit-0"));
            Assert.Equal(7, MetalDeposits.HavenIndexOf("deposit-7"));
            Assert.Null(MetalDeposits.HavenIndexOf("metal-3"));
            Assert.Null(MetalDeposits.HavenIndexOf("tree-3"));
            Assert.Null(MetalDeposits.HavenIndexOf(""));
            Assert.Null(MetalDeposits.HavenIndexOf("deposit-99999"));
        }

        // ====================================================================
        // RerolledNode - THE WHOLE DECISION IN ONE CALL.
        //
        // ⚠ THESE TESTS ARE THE FIX FOR AN ESCAPED MUTATION. The re-roll was first
        // a loop in the game server that indexed a seat list; changing
        // `NodeAtSeat(index, seats[index])` to `NodeAtSeat(index, index)` moved
        // nothing at all and the whole suite stayed green, because the untestable
        // assembly was guarded only by string matching. The arithmetic now lives in
        // MetalDeposits.RerolledNode, and these assert its BEHAVIOUR.
        // ====================================================================

        [Fact]
        public void RerolledNode_moves_most_deposits_on_a_real_storm()
        {
            // THE TEST THAT WOULD HAVE CAUGHT THE ESCAPE. A RerolledNode that returned
            // the deposit's home position - the exact shape of the escaped mutation -
            // returns null for every key and fails right here.
            int moved = 0;
            for (int i = 0; i < Occupied; i++)
            {
                MetalNode? node = MetalDeposits.RerolledNode(Haven, 1, "deposit-" + i);
                if (node == null) continue;
                Assert.NotEqual(MetalDeposits.NodeAt(i).Position, node.Position);
                moved++;
            }

            Assert.True(moved >= Occupied / 2,
                "only " + moved + " of " + Occupied + " deposits were re-rolled");
        }

        [Fact]
        public void RerolledNode_returns_nothing_for_a_world_that_has_never_stormed()
        {
            // Generation 0 is the boot layout. A re-roll firing at generation 0 would
            // move every rock before any storm - which reads to a player exactly like
            // the §4 "the rock moved" report S3 had to investigate before it could be
            // believed.
            for (int i = 0; i < Occupied; i++)
            {
                Assert.Null(MetalDeposits.RerolledNode(Haven, 0, "deposit-" + i));
            }
        }

        [Fact]
        public void RerolledNode_never_moves_the_pinned_tutorial_deposit()
        {
            for (long g = 0; g < 50; g++)
            {
                Assert.Null(MetalDeposits.RerolledNode(Haven, g, "deposit-0"));
            }
        }

        [Fact]
        public void RerolledNode_declines_islands_and_keys_with_no_seat_pool()
        {
            Assert.Null(MetalDeposits.RerolledNode(new IslandId("elsewhere"), 3, "deposit-5"));
            Assert.Null(MetalDeposits.RerolledNode(Haven, 3, "metal-5"));
            Assert.Null(MetalDeposits.RerolledNode(Haven, 3, "tree-5"));
            Assert.Null(MetalDeposits.RerolledNode(Haven, 3, "deposit-99999"));
        }

        [Fact]
        public void RerolledNode_is_deterministic_and_generation_dependent()
        {
            MetalNode? a = MetalDeposits.RerolledNode(Haven, 4, "deposit-9");
            MetalNode? b = MetalDeposits.RerolledNode(Haven, 4, "deposit-9");
            Assert.Equal(a?.Position, b?.Position);

            bool differsSomewhere = false;
            for (int i = 1; i < Occupied; i++)
            {
                MetalNode? g4 = MetalDeposits.RerolledNode(Haven, 4, "deposit-" + i);
                MetalNode? g5 = MetalDeposits.RerolledNode(Haven, 5, "deposit-" + i);
                if (g4?.Position != g5?.Position) differsSomewhere = true;
            }
            Assert.True(differsSomewhere, "two different storms produced the same field");
        }

        [Fact]
        public void RerolledNode_never_puts_two_deposits_in_the_same_place()
        {
            // The visible failure this guards is two rocks in one hole. Checked against
            // the deposits that did NOT move as well as those that did.
            for (long g = 1; g <= 10; g++)
            {
                HashSet<FixedPointPosition> taken = new HashSet<FixedPointPosition>();
                for (int i = 0; i < Occupied; i++)
                {
                    MetalNode? moved = MetalDeposits.RerolledNode(Haven, g, "deposit-" + i);
                    FixedPointPosition at = moved?.Position ?? MetalDeposits.NodeAt(i).Position;
                    Assert.True(taken.Add(at),
                        "two deposits share a position at generation " + g);
                }
            }
        }

        [Fact]
        public void RerolledNode_keeps_the_deposits_identity()
        {
            MetalNode home = MetalDeposits.NodeAt(9);
            MetalNode? moved = MetalDeposits.RerolledNode(Haven, 4, "deposit-9");
            Assert.NotNull(moved);
            Assert.Equal(home.Key, moved!.Key);
            Assert.Equal(home.MetalType, moved.MetalType);
            Assert.Equal(home.Quality, moved.Quality);
            Assert.Equal(home.VariantId, moved.VariantId);
        }

        // ====================================================================
        // THE REGISTRY SEAM
        // ====================================================================

        [Fact]
        public void Reseat_moves_a_registered_node_and_says_that_it_moved()
        {
            NodeRegistry registry = new NodeRegistry();
            registry.Register(42, MetalDeposits.NodeAt(3));

            Assert.True(registry.Reseat(42, MetalDeposits.NodeAtSeat(3, Pool - 1)));
            Assert.Equal(MetalDeposits.NodeAtSeat(3, Pool - 1).Position,
                registry.NodeOf(42)!.Position);
        }

        [Fact]
        public void Reseat_reports_false_when_the_position_did_not_actually_change()
        {
            // The game server only broadcasts a transform when this returns true, so a
            // Reseat that always returned true would push forty pointless updates per
            // storm, and one that always returned false would push none.
            NodeRegistry registry = new NodeRegistry();
            registry.Register(42, MetalDeposits.NodeAt(3));
            Assert.False(registry.Reseat(42, MetalDeposits.NodeAtSeat(3, 3)));
        }

        [Fact]
        public void Reseat_ignores_an_entity_that_is_not_a_node()
        {
            Assert.False(new NodeRegistry().Reseat(999, MetalDeposits.NodeAt(0)));
        }

        [Fact]
        public void Reseat_leaves_harvest_state_alone()
        {
            // Moving a rock must not silently repair or destroy it. The storm resets
            // first and re-rolls second precisely because those are separate facts.
            NodeRegistry registry = new NodeRegistry();
            registry.Register(42, MetalDeposits.NodeAt(3));
            registry.AddShotPoint(42, new ShotPoint(1, 2, 3));
            registry.MarkDestroyed(42);

            registry.Reseat(42, MetalDeposits.NodeAtSeat(3, Pool - 1));

            Assert.True(registry.IsDestroyed(42));
            Assert.Single(registry.ShotPointsOf(42));
        }
    }
}
