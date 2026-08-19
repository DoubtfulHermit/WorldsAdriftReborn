using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using WorldsAdriftRebornGameServer.Multiplayer.Resources;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// A FELLED LOG COMES TO REST ON THE GROUND, rather than through it or above it.
    ///
    /// The complaint these tests pin came from the live world in two halves that are
    /// the same bug seen from either side of a hill: a log felled uphill drove into
    /// the slope and half of it was inside the mountain, and a log felled downhill
    /// stayed level while the ground fell away and hung in the air. Both follow from
    /// toppling exactly ninety degrees about a pivot at the trunk's base, which is
    /// only correct on flat ground.
    ///
    /// THREE THINGS ARE WORTH PINNING AND THEY ARE PINNED SEPARATELY.
    /// <list type="number">
    /// <item>The RULE - given ground, where does a trunk lie. Pure arithmetic on
    ///   made-up hillsides, so the intent is readable.</item>
    /// <item>The DATA - the baked profile table agreeing with the code that reads
    ///   it, on the one island that carries both a baked row and the extracted
    ///   surface it was baked from. Without this the 332 KB file is a number nobody
    ///   ever checks, and a generator that drifted would be silent.</item>
    /// <item>The PATH THAT ACTUALLY RUNS - a log dropped through the production
    ///   wiring, at a real tree's real world position, coming out grounded. The
    ///   previous tree-fall fix shipped invisible because its tests stopped at the
    ///   pure registry and never touched the decision the server made, and this
    ///   class is not allowed to repeat that.</item>
    /// </list>
    /// </summary>
    public class LogGroundingTests
    {
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; set; }
        }

        private const long Tree = 500;
        private const long Cutter = 7;
        private const long LogId = 90001;

        private static TreeSectionMaskChange Change(int sectionId, int fallingMask, int remaining)
        {
            int bits = 0;
            for (int m = fallingMask; m != 0; m >>= 1)
            {
                bits += m & 1;
            }
            return new TreeSectionMaskChange(Tree, Cutter, sectionId, fallingMask, remaining,
                bits, Trees.WoodType);
        }

        private static GroundProfile Uniform(sbyte riseDecimetres)
        {
            sbyte[] rises = new sbyte[GroundProfile.Bearings];
            for (int i = 0; i < rises.Length; i++)
            {
                rises[i] = riseDecimetres;
            }
            return new GroundProfile(rises);
        }

        // ------------------------------------------------------------------
        // 1. The rule
        // ------------------------------------------------------------------

        [Fact]
        public void Flat_ground_still_lays_a_log_flat()
        {
            // The whole point of grounding is that it changes nothing where nothing
            // needed changing. A fix that tilted every log on the level would be a
            // worse regression than the bug.
            Assert.Equal(TreeFall.FlatRestAngleDegrees, LogGrounding.RestAngleDegrees(0.0), 6);
        }

        [Fact]
        public void Ground_that_rises_ahead_stops_the_trunk_short_of_flat()
        {
            // Felled INTO a hill, the trunk leans up it. This is the half of the
            // complaint that read as "it clips through the side of the mountain":
            // swinging the full ninety degrees drives the crown underground.
            double rest = LogGrounding.RestAngleDegrees(4.0);

            Assert.True(rest < TreeFall.FlatRestAngleDegrees,
                "a log felled uphill must stop short of flat, not swing through the hill");
            Assert.Equal(TreeFall.FlatRestAngleDegrees
                - (Math.Atan2(4.0, LogGrounding.ReachMetres) * 180.0 / Math.PI), rest, 6);
        }

        [Fact]
        public void Ground_that_falls_away_lets_the_trunk_follow_it_down()
        {
            // Felled downhill, the trunk swings PAST flat. This is the other half of
            // the complaint - "the wood is in the air" - and it is the same
            // subtraction with the other sign, which is why there is no separate
            // downhill branch to get wrong.
            double rest = LogGrounding.RestAngleDegrees(-4.0);

            Assert.True(rest > TreeFall.FlatRestAngleDegrees,
                "a log felled downhill must follow the slope rather than hang level");
        }

        [Fact]
        public void A_freak_sample_cannot_stand_a_log_on_end()
        {
            // The extracted surface is an 8 m decimation, so one sample on a boulder
            // or on the lip of a chasm can imply a slope the terrain does not have.
            // Unclamped that points a log vertically, which is far more alarming
            // than the flat log this replaces.
            Assert.Equal(TreeFall.FlatRestAngleDegrees - LogGrounding.MaxTiltDegrees,
                LogGrounding.RestAngleDegrees(10_000.0), 6);
            Assert.Equal(TreeFall.FlatRestAngleDegrees + LogGrounding.MaxTiltDegrees,
                LogGrounding.RestAngleDegrees(-10_000.0), 6);
        }

        [Fact]
        public void A_log_is_lifted_by_a_trunk_radius_even_where_the_ground_is_unknown()
        {
            // A tree's origin is on its trunk's AXIS, so a ninety-degree topple lays
            // the axis on the ground and buries the trunk's lower half. That is a
            // real part of "it clips through" and it has nothing to do with slope,
            // which is why the lift survives having no profile at all.
            GroundedRest rest = LogGrounding.Rest(null, 123.0, LogGrounding.DefaultLiftMetres);

            Assert.False(rest.Measured);
            Assert.Equal(TreeFall.FlatRestAngleDegrees, rest.RestAngleDegrees, 6);
            Assert.Equal(LogGrounding.DefaultLiftMetres, rest.LiftMetres, 6);
        }

        [Fact]
        public void An_unmeasured_bearing_still_puts_the_log_on_the_surface()
        {
            // ABOUT ONE BEARING IN NINE IS UNMEASURED - the ground ran off the island
            // edge, into a decimation gap, or down a face steeper than the deck band
            // admits. If that degraded to "leave the pose alone" then a tenth of all
            // cuts would reproduce the original complaint exactly, and it would look
            // like the fix had not worked.
            //
            // It must degrade to "lie FLAT ON the surface at the seat height", which
            // is honest: the seat is itself a measured surface vertex. Flat and
            // visible, never untouched and half-buried.
            sbyte[] blind = new sbyte[GroundProfile.Bearings];
            for (int i = 0; i < blind.Length; i++)
            {
                blind[i] = GroundProfile.Unknown;
            }

            GroundedRest rest = LogGrounding.Rest(new GroundProfile(blind), 217.0, 0.4);

            Assert.Equal(TreeFall.FlatRestAngleDegrees, rest.RestAngleDegrees, 6);
            Assert.Equal(0.4, rest.LiftMetres, 6);
            Assert.False(rest.Measured);
        }

        [Fact]
        public void A_log_on_ground_nobody_measured_is_still_lifted_by_the_drop_path()
        {
            // The same guarantee, through the registry rather than the rule, because
            // that is where it could quietly stop happening. If the lift were ever
            // dropped from FallingLogs.Drop for the unmeasured case this fails; a
            // test that only asked LogGrounding.Rest would not.
            sbyte[] blind = new sbyte[GroundProfile.Bearings];
            for (int i = 0; i < blind.Length; i++)
            {
                blind[i] = GroundProfile.Unknown;
            }

            FixedPointPosition at = FixedPointPosition.FromMetres(0.0, 0.0, 0.0);
            FallingLogs logs = new FallingLogs(new FakeClock(),
                groundProfiles: _ => new GroundProfile(blind));

            FelledLog? log = logs.Drop(1, Change(8, 0xF00, 0x0FF), "Tree", "Default", at,
                Quaternion32Packing.Identity, 12);

            Assert.NotNull(log);
            Assert.False(log!.Value.Ground.Measured);
            Assert.Equal(TreeFall.FlatRestAngleDegrees, log.Value.Ground.RestAngleDegrees, 6);
            Assert.True(log.Value.Position.Y > at.Y,
                "a log on unmeasured ground must still be lifted clear of it, not left half buried");
            Assert.Equal(LogGrounding.DefaultLiftMetres, log.Value.Position.MetresY - at.MetresY, 3);
        }

        [Fact]
        public void The_clamp_never_lays_a_log_flatter_than_the_hill_it_is_on()
        {
            // Clamping for looks would bury the far end, which is the defect this
            // file exists to remove. As the profiles are baked today the deck band
            // over the minimum distance bounds any measurable rise well inside the
            // rail, so the rail cannot be what decides a real log's angle.
            double bound = GroundProfile.DeckBandMetres * GroundProfile.ReachMetres
                / GroundProfile.MinDistanceMetres;
            double steepestMeasurable = Math.Atan2(bound, LogGrounding.ReachMetres) * 180.0 / Math.PI;

            Assert.True(LogGrounding.MaxTiltDegrees > steepestMeasurable,
                "the safety clamp (" + LogGrounding.MaxTiltDegrees
                + " deg) is inside the steepest slope the bake can express ("
                + steepestMeasurable.ToString("0.0")
                + " deg), so it would flatten real logs into the hillside");
        }

        [Fact]
        public void A_measured_profile_is_reported_as_measured()
        {
            // The flag is what lets a live cut say out loud whether the server is
            // guessing. It is the same class of evidence as "shown to N peer(s)",
            // which is what eventually found the previous defect.
            GroundedRest rest = LogGrounding.Rest(Uniform(-20), 0.0, 0.4);

            Assert.True(rest.Measured);
            Assert.True(rest.RestAngleDegrees > TreeFall.FlatRestAngleDegrees);
        }

        [Fact]
        public void The_lift_moves_the_log_up_and_only_up()
        {
            // Moving a log sideways to find better ground would put it somewhere the
            // player did not watch it fall, which is a worse lie than a wrong height.
            FixedPointPosition at = FixedPointPosition.FromMetres(100.0, 20.0, -50.0);
            FixedPointPosition raised = LogGrounding.Raise(at, 0.4);

            Assert.Equal(at.X, raised.X);
            Assert.Equal(at.Z, raised.Z);
            Assert.Equal(0.4, raised.MetresY - at.MetresY, 3);
        }

        // ------------------------------------------------------------------
        // Bearings
        // ------------------------------------------------------------------

        [Fact]
        public void A_bearing_between_two_samples_is_interpolated_rather_than_snapped()
        {
            // A fall heading is a hash and lands anywhere in the circle. Snapping to
            // the nearest of eight would make a log's tilt jump by the whole
            // difference between neighbours for a bearing change of one degree.
            sbyte[] rises = new sbyte[GroundProfile.Bearings];
            rises[0] = 0;
            rises[1] = 40;
            for (int i = 2; i < rises.Length; i++)
            {
                rises[i] = 0;
            }

            GroundProfile profile = new GroundProfile(rises);

            Assert.Equal(0.0, profile.RiseAt(0.0)!.Value, 6);
            Assert.Equal(4.0, profile.RiseAt(45.0)!.Value, 6);
            Assert.Equal(2.0, profile.RiseAt(22.5)!.Value, 6);
        }

        [Fact]
        public void An_unmeasured_bearing_does_not_claim_flat_ground()
        {
            // Forty per cent of a typical island's footprint has no extracted sample
            // at all. Reporting zero there would be claiming a measurement we do not
            // have, which is how a log ends up confidently inside a cliff.
            sbyte[] rises = new sbyte[GroundProfile.Bearings];
            for (int i = 0; i < rises.Length; i++)
            {
                rises[i] = GroundProfile.Unknown;
            }

            Assert.Null(new GroundProfile(rises).RiseAt(90.0));
            Assert.False(new GroundProfile(rises).HasAnyMeasurement);
        }

        [Fact]
        public void One_unknown_neighbour_does_not_poison_a_measured_one()
        {
            sbyte[] rises = new sbyte[GroundProfile.Bearings];
            for (int i = 0; i < rises.Length; i++)
            {
                rises[i] = GroundProfile.Unknown;
            }
            rises[2] = 30;

            GroundProfile profile = new GroundProfile(rises);

            // Bearing 90 is index 2 exactly; 60 and 100 straddle it against unknowns.
            Assert.Equal(3.0, profile.RiseAt(90.0)!.Value, 6);
            Assert.Equal(3.0, profile.RiseAt(60.0)!.Value, 6);
            Assert.Equal(3.0, profile.RiseAt(100.0)!.Value, 6);
        }

        [Fact]
        public void Quantising_never_manufactures_the_unknown_sentinel()
        {
            // -128 has to stay unreachable from real data or a very steep downhill
            // would be read as "no measurement" and silently flatten the log.
            Assert.Equal((sbyte)127, GroundProfile.Quantise(999.0));
            Assert.Equal((sbyte)(-127), GroundProfile.Quantise(-999.0));
            Assert.NotEqual(GroundProfile.Unknown, GroundProfile.Quantise(-12.8));
        }

        // ------------------------------------------------------------------
        // The high-side rule
        // ------------------------------------------------------------------

        [Fact]
        public void A_uniform_slope_is_reproduced_so_neither_end_floats()
        {
            // Every sample on a constant slope has the same rise-over-run, so the
            // maximum IS the slope and the trunk lies along it.
            List<(double X, double Y, double Z)> hill = new();
            for (double d = 2.0; d <= GroundProfile.ReachMetres; d += 2.0)
            {
                hill.Add((0.0, d * 0.25, d));
            }

            GroundProfile profile = LogGrounding.FromSamples(0.0, 0.0, 0.0, hill);

            // 0.25 rise per metre over a 16 m reach is 4 m, i.e. 40 decimetres.
            Assert.Equal(40, profile.RiseDecimetres(0));
        }

        [Fact]
        public void A_bulge_in_the_middle_wins_so_the_trunk_bridges_it()
        {
            // Averaging would bury the trunk in the bulge, and burying is the half of
            // the complaint that looks worst. A real trunk laid over a hummock
            // bridges it with its far end in the air.
            List<(double X, double Y, double Z)> ground = new()
            {
                (0.0, 0.0, 4.0),
                (0.0, 3.0, 8.0),    // the bulge: 3 m up at 8 m out
                (0.0, 0.0, 12.0),
                (0.0, 0.0, 16.0),
            };

            GroundProfile profile = LogGrounding.FromSamples(0.0, 0.0, 0.0, ground);

            // 3 m at 8 m out extrapolates to 6 m at the 16 m reach.
            Assert.Equal(60, profile.RiseDecimetres(0));
        }

        [Fact]
        public void A_drop_off_at_the_far_end_leaves_the_trunk_cantilevered()
        {
            // Flat ground then a cliff. A trunk does not nose-dive over the edge, it
            // sticks out over it, and the near-flat samples are what say so.
            List<(double X, double Y, double Z)> ground = new()
            {
                (0.0, 0.0, 4.0),
                (0.0, 0.0, 8.0),
                (0.0, -30.0, 12.0),
                (0.0, -60.0, 16.0),
            };

            GroundProfile profile = LogGrounding.FromSamples(0.0, 0.0, 0.0, ground);

            Assert.Equal(0, profile.RiseDecimetres(0));
        }

        [Fact]
        public void Another_storey_overhead_is_not_read_as_a_wall_to_climb()
        {
            // The extracted surface was decimated on a THREE-dimensional voxel grid,
            // so a cave roof or a built deck puts several Y values above one spot.
            // Without the deck band the roof would be read as ground the log must
            // rise to meet.
            List<(double X, double Y, double Z)> ground = new()
            {
                (0.0, 0.0, 8.0),
                (0.0, GroundProfile.DeckBandMetres + 5.0, 8.0),
            };

            GroundProfile profile = LogGrounding.FromSamples(0.0, 0.0, 0.0, ground);

            Assert.Equal(0, profile.RiseDecimetres(0));
        }

        [Fact]
        public void A_sample_against_the_trunk_cannot_become_a_vertical_wall()
        {
            // The rise is divided by the distance, so a sample at the trunk turns a
            // centimetre of decimation noise into a cliff.
            List<(double X, double Y, double Z)> ground = new()
            {
                (0.0, 6.0, GroundProfile.MinDistanceMetres / 2.0),
            };

            GroundProfile profile = LogGrounding.FromSamples(0.0, 0.0, 0.0, ground);

            Assert.Equal(GroundProfile.Unknown, profile.RiseDecimetres(0));
        }

        [Fact]
        public void Ground_behind_the_log_is_not_ground_under_it()
        {
            // A tree at the foot of a cliff must still be able to fall AWAY from it.
            // If the cliff behind counted, every such log would be tilted skyward.
            List<(double X, double Y, double Z)> ground = new()
            {
                (0.0, 40.0, -8.0),
            };

            GroundProfile profile = LogGrounding.FromSamples(0.0, 0.0, 0.0, ground);

            Assert.Equal(GroundProfile.Unknown, profile.RiseDecimetres(0));
        }

        // ------------------------------------------------------------------
        // 2. The data
        // ------------------------------------------------------------------

        [Fact]
        public void Every_authored_tree_seat_in_the_release_world_carries_a_profile()
        {
            // A seat with no row is a tree whose logs can never be grounded, and it
            // would be invisible: the log would simply fall flat as it always did.
            foreach (ReleaseTreeIsland island in ReleaseTreeCatalog.All)
            {
                if (island.Points.Count == 0)
                {
                    continue;
                }

                GroundProfile? last = TreeGroundProfiles.BakedFor(
                    island.WorkshopId, island.Points.Count - 1);

                Assert.True(last != null,
                    "island " + island.WorkshopId + " (" + island.Name + ") has "
                    + island.Points.Count + " seats but no profile for the last one");
                Assert.Null(TreeGroundProfiles.BakedFor(island.WorkshopId, island.Points.Count));
            }
        }

        [Fact]
        public void The_profile_table_covers_the_whole_release_world()
        {
            Assert.Equal(ReleaseTreeCatalog.TotalTrees, TreeGroundProfiles.BakedSeatCount);
        }

        [Fact]
        public void The_baked_table_agrees_with_the_code_that_reads_it()
        {
            // THE GENERATOR GATE. The Trades Challenge is the one island that carries
            // BOTH a baked row per seat and the whole extracted surface those rows
            // were baked from, so the offline Python and this assembly can be held
            // against each other on real terrain. Without this the 332 KB file is a
            // number nobody ever checks and a drifted generator would be silent.
            ReleaseTreeIsland island = ReleaseTreeCatalog.ForWorkshopId(
                TradesChallengeResources.WorkshopId)!;

            List<(double X, double Y, double Z)> samples = new();
            foreach (SurfaceSample sample in TradesChallengeResources.Samples)
            {
                samples.Add((sample.LocalX, sample.LocalY, sample.LocalZ));
            }

            for (int seat = 0; seat < island.Points.Count; seat++)
            {
                (double x, double y, double z) = island.Points[seat];
                GroundProfile built = LogGrounding.FromSamples(x, y, z, samples);
                GroundProfile baked = TreeGroundProfiles.BakedFor(island.WorkshopId, seat)!.Value;

                for (int bearing = 0; bearing < GroundProfile.Bearings; bearing++)
                {
                    Assert.True(
                        built.RiseDecimetres(bearing) == baked.RiseDecimetres(bearing),
                        "seat " + seat + " bearing " + bearing + ": the generator baked "
                        + baked.RiseDecimetres(bearing) + " but this assembly measures "
                        + built.RiseDecimetres(bearing)
                        + ". Regenerate release-tree-ground-profiles.txt, or the two have drifted.");
                }
            }
        }

        [Fact]
        public void A_release_tree_resolves_its_own_ground_from_its_world_position()
        {
            // The lookup as production performs it: island resolution from a world
            // fixed-point position, the conversion back into island-local metres, and
            // the seat identification - on real catalogue data rather than a fake.
            ReleaseIslandRecord island = ReleaseWorldCatalog.All.First(record =>
                ReleaseTreeCatalog.ForWorkshopId(record.Survey.WorkshopId)?.Points.Count > 0);
            ReleaseTreeIsland seats = ReleaseTreeCatalog.ForWorkshopId(island.Survey.WorkshopId)!;

            int measured = 0;
            for (int i = 0; i < seats.Points.Count; i++)
            {
                (double x, double y, double z) = seats.Points[i];
                GroundProfile? profile = TreeGroundProfiles.For(island.Definition.LocalToGlobal(x, y, z));
                if (profile?.HasAnyMeasurement == true)
                {
                    measured++;
                }
            }

            Assert.True(measured > seats.Points.Count / 2,
                "only " + measured + " of " + seats.Points.Count + " seats on "
                + island.Survey.WorkshopId + " resolved measured ground");
        }

        [Fact]
        public void A_position_that_is_not_a_tree_seat_gets_no_answer()
        {
            // Grounding must not extrapolate. A point between seats is ground nobody
            // measured, and inventing a profile there would tilt logs from a guess.
            ReleaseIslandRecord island = ReleaseWorldCatalog.All.First(record =>
                ReleaseTreeCatalog.ForWorkshopId(record.Survey.WorkshopId)?.Points.Count > 0
                && TreeGroundProfiles.EmbeddedSurfaceFor(record.Survey.WorkshopId) == null);
            ReleaseTreeIsland seats = ReleaseTreeCatalog.ForWorkshopId(island.Survey.WorkshopId)!;

            (double x, double y, double z) = seats.Points[0];
            double offset = TreeGroundProfiles.SeatToleranceMetres + 4.0;

            Assert.Null(TreeGroundProfiles.For(island.Definition.LocalToGlobal(x + offset, y, z)));
        }

        [Fact]
        public void Havens_own_trees_are_grounded_from_its_embedded_surface()
        {
            // Haven is the spawn island, it is where the first tree anybody fells
            // stands, and it is NOT in the release catalogue - its eighty trees are
            // generated at boot, so there is no baked row to look up. If only the
            // baked path worked, the island players actually chop on would be the one
            // island the fix missed.
            int measured = 0;
            IReadOnlyList<GeneratedPlacement> trees = HavenSurface.TreeLocals();

            foreach (GeneratedPlacement tree in trees)
            {
                GroundProfile? profile = TreeGroundProfiles.For(
                    IslandCatalog.Haven.LocalToGlobal(tree.LocalX, tree.LocalY, tree.LocalZ));
                if (profile?.HasAnyMeasurement == true)
                {
                    measured++;
                }
            }

            Assert.True(measured > trees.Count / 2,
                "only " + measured + " of Haven's " + trees.Count + " trees resolved measured ground");
        }

        // ------------------------------------------------------------------
        // 3. The path that actually runs
        // ------------------------------------------------------------------

        [Fact]
        public void A_log_dropped_through_the_production_wiring_comes_out_grounded()
        {
            // THE REGRESSION GATE, and the reason FallingLogs resolves the ground
            // itself instead of being handed it. This constructs the registry the way
            // FallingLogService does - no injected profile source, no test double -
            // and drops a log at a real tree's real world position. If grounding
            // stops being applied on the path the server runs, this fails; a test
            // that stopped at the pure rule would not.
            ReleaseIslandRecord island = ReleaseWorldCatalog.All.First(record =>
                ReleaseTreeCatalog.ForWorkshopId(record.Survey.WorkshopId)?.Points.Count > 0);
            ReleaseTreeIsland seats = ReleaseTreeCatalog.ForWorkshopId(island.Survey.WorkshopId)!;

            int tilted = 0;
            int lifted = 0;

            for (int i = 0; i < seats.Points.Count; i++)
            {
                (double x, double y, double z) = seats.Points[i];
                FixedPointPosition at = island.Definition.LocalToGlobal(x, y, z);

                FallingLogs logs = new FallingLogs(new FakeClock());
                FelledLog? log = logs.Drop(logs.NextEntityId(), Change(8, 0xF00, 0x0FF),
                    "Tree", "Default", at, Quaternion32Packing.Identity, 12);

                Assert.NotNull(log);
                if (log!.Value.Position.Y > at.Y)
                {
                    lifted++;
                }
                if (log.Value.Ground.Measured
                    && Math.Abs(log.Value.Ground.RestAngleDegrees - TreeFall.FlatRestAngleDegrees) > 0.5)
                {
                    tilted++;
                }
            }

            Assert.Equal(seats.Points.Count, lifted);
            Assert.True(tilted > 0,
                "not one of " + seats.Points.Count + " real tree seats produced a tilted log; "
                + "grounding is not reaching the drop path");
        }

        [Fact]
        public void A_grounded_log_settles_at_its_grounded_angle_and_not_at_flat()
        {
            // The tilt has to survive the arc, not just the decision. The last pose of
            // a fall is the one the client keeps, so if the arc still finished at
            // ninety the whole thing would be decoration.
            FakeClock clock = new FakeClock();
            FixedPointPosition at = FixedPointPosition.FromMetres(0.0, 0.0, 0.0);

            FallingLogs downhill = new FallingLogs(clock, groundProfiles: _ => Uniform(-40));
            FallingLogs flat = new FallingLogs(clock, groundProfiles: _ => Uniform(0));

            downhill.Drop(1, Change(8, 0xF00, 0x0FF), "Tree", "Default", at,
                Quaternion32Packing.Identity, 12);
            flat.Drop(1, Change(8, 0xF00, 0x0FF), "Tree", "Default", at,
                Quaternion32Packing.Identity, 12);

            clock.Elapsed = TreeFall.DefaultFallDuration + TimeSpan.FromSeconds(1);

            Assert.NotEqual(flat.RotationOf(1)!.Value, downhill.RotationOf(1)!.Value);
        }

        [Fact]
        public void A_piece_broken_off_a_grounded_log_inherits_its_grounding_exactly()
        {
            // The cheap, common case: a trunk already on the ground being taken apart.
            // A sub-piece never swings and never re-measures - it is seeded with its
            // parent's settled pose - so grounding reaches it for free. It must NOT be
            // lifted a second trunk-radius or tipped a second time, which is what a
            // naive "ground everything" would do.
            FakeClock clock = new FakeClock();
            FixedPointPosition at = FixedPointPosition.FromMetres(0.0, 0.0, 0.0);

            FallingLogs logs = new FallingLogs(clock, groundProfiles: _ => Uniform(-40));
            logs.Drop(1, Change(8, 0xF00, 0x0FF), "Tree", "Default", at,
                Quaternion32Packing.Identity, 12);

            clock.Elapsed = TreeFall.DefaultFallDuration + TimeSpan.FromSeconds(1);

            FixedPointPosition parentAt = logs.PositionOf(1)!.Value;
            uint parentRotation = logs.RotationOf(1)!.Value;

            logs.Drop(2, Change(9, 0x600, 0x0FF), "Tree", "Default", parentAt,
                parentRotation, 12, alreadyDown: true);

            Assert.Equal(parentAt, logs.PositionOf(2)!.Value);
            Assert.Equal(parentRotation, logs.RotationOf(2)!.Value);

            // And it stays there: a settled piece is silent, so its one pose is all
            // the client will ever be told.
            clock.Elapsed += TimeSpan.FromSeconds(5);
            Assert.Equal(parentRotation, logs.RotationOf(2)!.Value);
        }

        [Fact]
        public void The_lift_is_tunable_without_a_rebuild()
        {
            // The lift is a reconstructed trunk radius and the only instrument that
            // can read it is somebody standing next to a felled log. An environment
            // variable turns "that looks a bit high" into a restart rather than a
            // build, a deploy and a round trip.
            Assert.Null(LogGrounding.ParseLift(null));
            Assert.Null(LogGrounding.ParseLift("   "));
            Assert.Null(LogGrounding.ParseLift("waist-high"));
            Assert.Null(LogGrounding.ParseLift("-1"));
            Assert.Equal(0.0, LogGrounding.ParseLift("0")!.Value, 6);
            Assert.Equal(0.75, LogGrounding.ParseLift("0.75")!.Value, 6);
        }
    }
}
