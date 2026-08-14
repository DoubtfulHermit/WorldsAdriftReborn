using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The RECOVERED retail fuel yield curve: a fuel canister takes THREE gauntlet
    /// salvage shots and yields 8, then 8, then 9 fuel - 25 total
    /// (worldsadrift.fandom.com/wiki/Fuel, /wiki/Resources, /wiki/Mining). These are
    /// preserved retail numbers, so they are pinned exactly rather than approximated.
    /// </summary>
    public class FuelCanisterYieldTests
    {
        [Fact]
        public void The_retail_schedule_is_eight_eight_nine()
            => Assert.Equal(new[] { 8, 8, 9 }, FuelCanisterYield.Schedule.ToArray());

        [Fact]
        public void A_canister_takes_three_shots_and_is_worth_twenty_five_fuel()
        {
            Assert.Equal(3, FuelCanisterYield.ShotsToDeplete);
            Assert.Equal(25, FuelCanisterYield.TotalFuel);
        }

        [Theory]
        [InlineData(1, 8)]
        [InlineData(2, 8)]
        [InlineData(3, 9)]
        public void Each_shot_frees_its_scheduled_fuel(int shot, int expected)
            => Assert.Equal(expected, FuelCanisterYield.FuelForShot(shot));

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(-1)]
        [InlineData(99)]
        public void A_shot_outside_the_schedule_frees_nothing_rather_than_throwing(int shot)
            => Assert.Equal(0, FuelCanisterYield.FuelForShot(shot));

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 8)]
        [InlineData(2, 16)]
        [InlineData(3, 25)]
        [InlineData(9, 25)] // clamped: a canister never pays out more than it holds
        public void The_running_total_tracks_the_curve_and_clamps(int throughShot, int expected)
            => Assert.Equal(expected, FuelCanisterYield.TotalThrough(throughShot));
    }

    /// <summary>
    /// The fuel-canister ledger: counting gauntlet salvage shots and reporting what
    /// each freed. The fuel analogue of <see cref="MetalHarvest"/> - and NOT a pickup:
    /// there is no reservation, no interact verb, no lodged state, because retail fuel
    /// is salvaged with the beam.
    /// </summary>
    public class FuelCanisterRegistryTests
    {
        private const long Canister = 7000;
        private const long Other = 7001;

        private static FuelCanisterRegistry Registered()
        {
            FuelCanisterRegistry reg = new FuelCanisterRegistry();
            Assert.True(reg.Register(Canister));
            return reg;
        }

        [Fact]
        public void A_registered_canister_is_intact_and_shootable()
        {
            FuelCanisterRegistry reg = Registered();
            Assert.True(reg.IsCanister(Canister));
            Assert.False(reg.IsDepleted(Canister));
            Assert.Equal(0, reg.ShotsOn(Canister));
            Assert.Equal(0, reg.FuelPaidOut(Canister));
        }

        [Fact]
        public void Registration_is_idempotent_so_a_second_joiner_cannot_refill_a_spent_canister()
        {
            FuelCanisterRegistry reg = Registered();
            for (int i = 0; i < FuelCanisterYield.ShotsToDeplete; i++)
            {
                reg.Hit(Canister);
            }
            Assert.True(reg.IsDepleted(Canister));

            Assert.False(reg.Register(Canister));
            Assert.True(reg.IsDepleted(Canister));
        }

        [Fact]
        public void An_unknown_id_answers_false_and_zero_everywhere()
        {
            FuelCanisterRegistry reg = new FuelCanisterRegistry();
            Assert.False(reg.IsCanister(Canister));
            Assert.False(reg.IsDepleted(Canister));
            Assert.Equal(0, reg.ShotsOn(Canister));
            Assert.Equal(FuelHitOutcome.Nothing, reg.Hit(Canister));
        }

        [Fact]
        public void Three_shots_pay_out_eight_eight_nine_and_the_third_empties_it()
        {
            FuelCanisterRegistry reg = Registered();

            FuelHitOutcome first = reg.Hit(Canister);
            Assert.Equal(8, first.FuelGranted);
            Assert.False(first.Depleted);
            Assert.Equal(1, first.ShotNumber);
            Assert.True(first.Granted);

            FuelHitOutcome second = reg.Hit(Canister);
            Assert.Equal(8, second.FuelGranted);
            Assert.False(second.Depleted);
            Assert.Equal(2, second.ShotNumber);

            FuelHitOutcome third = reg.Hit(Canister);
            Assert.Equal(9, third.FuelGranted);
            Assert.True(third.Depleted);
            Assert.Equal(3, third.ShotNumber);

            Assert.True(reg.IsDepleted(Canister));
            Assert.Equal(25, reg.FuelPaidOut(Canister));
        }

        [Fact]
        public void Operator_reset_restores_partial_and_depleted_canisters()
        {
            FuelCanisterRegistry reg = Registered();
            reg.Register(Other);
            reg.Hit(Canister);
            reg.Hit(Other); reg.Hit(Other); reg.Hit(Other);

            Assert.Equal(2, reg.ResetAll());
            Assert.Equal(0, reg.ShotsOn(Canister));
            Assert.Equal(0, reg.ShotsOn(Other));
            Assert.False(reg.IsDepleted(Other));
            Assert.Equal(0, reg.ResetAll());
        }

        [Fact]
        public void The_running_total_across_the_three_shots_is_exactly_twenty_five()
        {
            FuelCanisterRegistry reg = Registered();
            int total = 0;
            for (int i = 0; i < FuelCanisterYield.ShotsToDeplete; i++)
            {
                total += reg.Hit(Canister).FuelGranted;
            }
            Assert.Equal(FuelCanisterYield.TotalFuel, total);
        }

        [Fact]
        public void A_held_beam_on_an_emptied_canister_never_pays_out_again()
        {
            FuelCanisterRegistry reg = Registered();
            for (int i = 0; i < FuelCanisterYield.ShotsToDeplete; i++)
            {
                reg.Hit(Canister);
            }

            // The beam legitimately keeps resting on the husk and publishing ShotEvents.
            for (int i = 0; i < 5; i++)
            {
                FuelHitOutcome extra = reg.Hit(Canister);
                Assert.Equal(FuelHitOutcome.Nothing, extra);
                Assert.False(extra.Granted);
            }
            Assert.Equal(25, reg.FuelPaidOut(Canister));
        }

        [Fact]
        public void Depleted_is_reported_on_exactly_one_shot_so_the_sink_fires_once()
        {
            FuelCanisterRegistry reg = Registered();
            int depletedCount = 0;
            for (int i = 0; i < 10; i++)
            {
                if (reg.Hit(Canister).Depleted)
                {
                    depletedCount++;
                }
            }
            Assert.Equal(1, depletedCount);
        }

        [Fact]
        public void Shots_on_one_canister_never_touch_another()
        {
            FuelCanisterRegistry reg = Registered();
            reg.Register(Other);

            reg.Hit(Canister);
            reg.Hit(Canister);

            Assert.Equal(2, reg.ShotsOn(Canister));
            Assert.Equal(0, reg.ShotsOn(Other));
            Assert.False(reg.IsDepleted(Other));
        }

        [Fact]
        public void Two_players_shooting_the_same_canister_share_its_three_shots()
        {
            // No reservation, unlike a pickup: salvage is a shared drain, so whoever
            // fires each shot gets that shot's fuel and the canister still yields 25
            // in total. This is the multiplayer-safety property for a salvage target.
            FuelCanisterRegistry reg = Registered();

            int playerA = reg.Hit(Canister).FuelGranted; // 8
            int playerB = reg.Hit(Canister).FuelGranted; // 8
            int playerAAgain = reg.Hit(Canister).FuelGranted; // 9

            Assert.Equal(8, playerA);
            Assert.Equal(8, playerB);
            Assert.Equal(9, playerAAgain);
            Assert.Equal(FuelCanisterYield.TotalFuel, playerA + playerB + playerAAgain);
            Assert.True(reg.IsDepleted(Canister));
        }

        [Fact]
        public void EntityIds_and_count_track_registrations()
        {
            FuelCanisterRegistry reg = Registered();
            reg.Register(Other);
            Assert.Equal(2, reg.Count);
            Assert.Contains(Canister, (IEnumerable<long>)reg.EntityIds);
            Assert.Contains(Other, (IEnumerable<long>)reg.EntityIds);
        }
    }

    /// <summary>
    /// The static facts about a fuel canister as a world entity: prefab name, the real
    /// granted item, key helpers, the starter placement set, and - critically - that it
    /// exposes NO pickup surface, because retail fuel is salvaged, not picked up.
    /// </summary>
    public class FuelPodsTests
    {
        [Fact]
        public void A_canister_grants_the_real_fuel_item_which_is_not_a_pending_placeholder()
        {
            Assert.Equal("fuel", FuelPods.ItemTypeId);
            Assert.DoesNotContain("PENDING", FuelPods.ItemTypeId, StringComparison.Ordinal);
        }

        [Fact]
        public void The_canister_reports_the_recovered_retail_shot_count_and_total()
        {
            Assert.Equal(3, FuelPods.ShotsToDeplete);
            Assert.Equal(25, FuelPods.TotalFuel);
        }

        [Fact]
        public void The_default_asset_name_is_the_egg_prefab_and_is_env_overridable()
        {
            Assert.Equal("Egg", FuelPods.DefaultAssetName);

            string? saved = Environment.GetEnvironmentVariable("WAREBORN_FUELPOD_ASSET");
            try
            {
                Environment.SetEnvironmentVariable("WAREBORN_FUELPOD_ASSET", null);
                Assert.Equal(FuelPods.DefaultAssetName, FuelPods.AssetName);

                Environment.SetEnvironmentVariable("WAREBORN_FUELPOD_ASSET", "  ");
                Assert.Equal(FuelPods.DefaultAssetName, FuelPods.AssetName); // blank falls back

                Environment.SetEnvironmentVariable("WAREBORN_FUELPOD_ASSET", "FuelPodCustom");
                Assert.Equal("FuelPodCustom", FuelPods.AssetName);
            }
            finally
            {
                Environment.SetEnvironmentVariable("WAREBORN_FUELPOD_ASSET", saved);
            }
        }

        [Theory]
        [InlineData(0, "fuel-pod-0")]
        [InlineData(3, "fuel-pod-3")]
        public void Keys_round_trip_through_index(int index, string expectedKey)
        {
            Assert.Equal(expectedKey, FuelPods.KeyFor(index));
            Assert.True(FuelPods.IsPodKey(expectedKey));
            Assert.Equal(index, FuelPods.IndexOf(expectedKey));
        }

        [Fact]
        public void A_non_canister_key_is_not_a_canister_and_has_no_index()
        {
            Assert.False(FuelPods.IsPodKey("atlas-shard-0"));
            Assert.False(FuelPods.IsPodKey("tree-3"));
            Assert.False(FuelPods.IsPodKey(null));
            Assert.Null(FuelPods.IndexOf("tree-3"));
        }

        [Fact]
        public void The_starter_set_places_several_canisters_each_at_a_distinct_position()
        {
            Assert.True(FuelPods.HavenPlacements.Count >= 3);
            var seen = new HashSet<(long, long, long)>();
            for (int i = 0; i < FuelPods.HavenPlacements.Count; i++)
            {
                FixedPointPosition p = FuelPods.PositionAt(i);
                Assert.True(seen.Add((p.X, p.Y, p.Z)),
                    $"canister {i} shares a position with another canister");
            }
        }

        [Fact]
        public void Fuel_keeps_the_five_legacy_ids_then_spans_the_whole_island()
        {
            Assert.Equal(global::WorldsAdriftRebornGameServer.Multiplayer.Resources.HavenSurface.FuelTargetCount,
                FuelPods.HavenPlacements.Count);

            double[] legacyX = { 192.0, 152.0, 176.0, 128.0, 184.0 };
            double[] legacyY = { 7.13, 4.71, 6.39, 6.12, 3.10 };
            double[] legacyZ = { 8.0, 0.0, -16.0, 0.0, -32.0 };
            for (int i = 0; i < legacyX.Length; i++)
            {
                Assert.Equal(legacyX[i], FuelPods.HavenPlacements[i].LocalX);
                Assert.Equal(legacyY[i], FuelPods.HavenPlacements[i].LocalY);
                Assert.Equal(legacyZ[i], FuelPods.HavenPlacements[i].LocalZ);
            }

            double spanX = FuelPods.HavenPlacements.Max(p => p.LocalX)
                - FuelPods.HavenPlacements.Min(p => p.LocalX);
            double spanZ = FuelPods.HavenPlacements.Max(p => p.LocalZ)
                - FuelPods.HavenPlacements.Min(p => p.LocalZ);
            Assert.True(spanX > 400.0, "fuel X span was only " + spanX);
            Assert.True(spanZ > 200.0, "fuel Z span was only " + spanZ);
            Assert.Contains(FuelPods.HavenPlacements, p => p.LocalY > 20.0);
        }

        [Fact]
        public void Generated_fuel_seats_are_flat_spaced_and_deterministic()
        {
            IReadOnlyList<global::WorldsAdriftRebornGameServer.Multiplayer.Resources.GeneratedPlacement> locals =
                global::WorldsAdriftRebornGameServer.Multiplayer.Resources.HavenSurface.FuelLocals();
            Assert.Equal(locals,
                global::WorldsAdriftRebornGameServer.Multiplayer.Resources.HavenSurface.FuelLocals());

            for (int i = global::WorldsAdriftRebornGameServer.Multiplayer.Resources.HavenSurface.LegacyFuelLocals.Count;
                 i < locals.Count; i++)
            {
                Assert.True(locals[i].Ny
                    >= global::WorldsAdriftRebornGameServer.Multiplayer.Resources.HavenSurface.FuelMinUpwardNormal);
                for (int j = 0; j < i; j++)
                {
                    double dx = locals[i].LocalX - locals[j].LocalX;
                    double dy = locals[i].LocalY - locals[j].LocalY;
                    double dz = locals[i].LocalZ - locals[j].LocalZ;
                    Assert.True(Math.Sqrt(dx * dx + dy * dy + dz * dz)
                        >= global::WorldsAdriftRebornGameServer.Multiplayer.Resources.HavenSurface.FuelMinSpacing - 1e-9);
                }
            }
        }

        [Fact]
        public void The_count_knob_clamps_to_the_table_and_never_drops_the_first_canister()
        {
            int full = FuelPods.HavenPlacements.Count;
            Assert.Equal(full, FuelPods.CountFrom(null));
            Assert.Equal(1, FuelPods.CountFrom("1"));
            Assert.Equal(1, FuelPods.CountFrom("0"));       // clamped up to 1
            Assert.Equal(full, FuelPods.CountFrom("9999")); // clamped down to full
            Assert.Equal(full, FuelPods.CountFrom("junk")); // bad value -> full
        }

        [Fact]
        public void A_canister_exposes_no_pickup_surface_at_all()
        {
            // REGRESSION GUARD for the pickup->salvage correction. Fuel is salvaged with
            // the gauntlet beam; if a PickUp radius/hold-time or a per-pod grant count
            // ever reappears on this type, the wrong mechanic is creeping back in.
            Type t = typeof(FuelPods);
            foreach (string banned in new[]
                     { "PickUpRadius", "PickUpTimeToUse", "FuelPerPod" })
            {
                Assert.True(t.GetMember(banned).Length == 0,
                    $"FuelPods.{banned} exists - fuel is SALVAGED, not picked up.");
            }
        }
    }
}
