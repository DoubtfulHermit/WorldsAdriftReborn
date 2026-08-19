using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// A felled tree FALLS OVER instead of blinking out of existence.
    ///
    /// The behaviour these tests pin is the half of retail's chopping that this
    /// server never had: <c>TreeSection.Harvest</c> called
    /// <c>SpawnNewTree(salvagerId, fallingMask)</c> BEFORE it shrank the standing
    /// tree, and the severed crown lived on as its own dynamic entity that the
    /// UnityWorker tipped over. There is no UnityWorker here, but there does not
    /// need to be: a retail CLIENT never simulated the fall either
    /// (<c>TreeBase.ResetCOMHackCoroutine</c> keeps a dynamic tree kinematic when
    /// <c>WorldsAdrift.IsClient</c>), it replayed served transforms through an
    /// interpolator. So the arc is authored here and served, and the client's own
    /// code path is the one retail used.
    ///
    /// The conservation law is the important one and it is asserted directly: what
    /// the LOG renders is exactly what LEFT the standing tree, bit for bit. A log
    /// carrying a section the tree still has would be a section rendered twice; a
    /// tree keeping a section the log took would be one rendered nowhere.
    /// </summary>
    public class TreeFallTests
    {
        private sealed class FakeClock : IClock
        {
            public TimeSpan Elapsed { get; set; }

            public void Advance(TimeSpan by) => Elapsed += by;
        }

        private const long Tree = 500;
        private const long Cutter = 7;
        private const long LogId = 90001;

        private static readonly TimeSpan Fall = TimeSpan.FromSeconds(1.6);
        private static readonly TimeSpan Linger = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan Pose = TimeSpan.FromSeconds(0.05);
        private static readonly FixedPointPosition Where = FixedPointPosition.FromMetres(208, 6.7, 4);
        private const string Asset = "Tree";
        private const string Context = "Default";

        private static FallingLogs Logs(FakeClock clock, int max = 8, int repeats = 4)
        {
            return new FallingLogs(clock, Fall, Linger, max, Pose, repeats);
        }

        private static TreeSectionMaskChange Change(int sectionId, int fallingMask, int remaining)
        {
            return new TreeSectionMaskChange(Tree, Cutter, sectionId, fallingMask, remaining,
                CountBits(fallingMask), Trees.WoodType);
        }

        private static int CountBits(int mask)
        {
            int n = 0;
            while (mask != 0) { n += mask & 1; mask >>= 1; }
            return n;
        }

        /// <summary>The angle between two packed rotations, in degrees.</summary>
        private static double AngleBetween(uint a, uint b)
        {
            (float aw, float ax, float ay, float az) = Quaternion32Packing.Decode(a);
            (float bw, float bx, float by, float bz) = Quaternion32Packing.Decode(b);
            double dot = Math.Abs((aw * bw) + (ax * bx) + (ay * by) + (az * bz));
            if (dot > 1.0) dot = 1.0;
            return 2.0 * Math.Acos(dot) * 180.0 / Math.PI;
        }

        // ------------------------------------------------------------------
        // The fall curve
        // ------------------------------------------------------------------

        [Fact]
        public void A_tree_starts_upright_and_ends_flat()
        {
            Assert.Equal(0.0, TreeFall.FallAngleDegrees(TimeSpan.Zero, Fall), 6);
            Assert.Equal(90.0, TreeFall.FallAngleDegrees(Fall, Fall), 6);
        }

        [Fact]
        public void A_landed_log_stays_landed_however_long_the_tick_is_late()
        {
            // The tick that finishes a fall can arrive arbitrarily late; the angle
            // must not keep climbing past flat and start the log spinning.
            Assert.Equal(90.0, TreeFall.FallAngleDegrees(TimeSpan.FromHours(1), Fall), 6);
        }

        [Fact]
        public void The_topple_accelerates_rather_than_sweeping_evenly()
        {
            // A body toppling about its base under gravity barely moves at first and
            // then goes over fast. A LINEAR sweep reads as a door closing, which is
            // the single cheapest way to make this look wrong.
            double firstHalf = TreeFall.FallAngleDegrees(
                TimeSpan.FromSeconds(Fall.TotalSeconds / 2), Fall);
            double secondHalf = 90.0 - firstHalf;

            Assert.True(firstHalf < secondHalf,
                "the second half of a fall must cover more angle than the first");
            Assert.Equal(22.5, firstHalf, 6);   // 90 * 0.5^2
        }

        [Fact]
        public void The_angle_never_goes_backwards()
        {
            double previous = -1;
            for (int i = 0; i <= 40; i++)
            {
                double angle = TreeFall.FallAngleDegrees(
                    TimeSpan.FromSeconds(Fall.TotalSeconds * i / 40.0), Fall);
                Assert.True(angle >= previous, "angle went backwards at step " + i);
                previous = angle;
            }
        }

        [Fact]
        public void A_zero_length_fall_is_already_down_rather_than_dividing_by_zero()
        {
            Assert.Equal(90.0, TreeFall.FallAngleDegrees(TimeSpan.Zero, TimeSpan.Zero), 6);
        }

        // ------------------------------------------------------------------
        // Which way it goes over
        // ------------------------------------------------------------------

        [Fact]
        public void Every_viewer_computes_the_same_direction_for_the_same_cut()
        {
            // Not a style point: a direction drawn per-recipient would have two
            // players watching one tree fall two different ways.
            Assert.Equal(TreeFall.FallHeadingDegrees(Tree, 8), TreeFall.FallHeadingDegrees(Tree, 8));
            Assert.Equal(TreeFall.FallHeadingDegrees(4242, 3), TreeFall.FallHeadingDegrees(4242, 3));
        }

        [Fact]
        public void A_heading_is_a_bearing()
        {
            for (int section = 0; section < Trees.SectionCount; section++)
            {
                double heading = TreeFall.FallHeadingDegrees(Tree, section);
                Assert.InRange(heading, 0.0, 359.0);
            }
        }

        [Fact]
        public void A_tree_does_not_shed_every_limb_onto_the_same_spot()
        {
            HashSet<double> headings = new HashSet<double>();
            for (int section = 0; section < Trees.SectionCount; section++)
            {
                headings.Add(TreeFall.FallHeadingDegrees(Tree, section));
            }
            Assert.True(headings.Count > 1,
                "every section of one tree fell in the same direction");
        }

        [Fact]
        public void Two_trees_do_not_fall_identically()
        {
            HashSet<double> headings = new HashSet<double>();
            for (long tree = 500; tree < 540; tree++)
            {
                headings.Add(TreeFall.FallHeadingDegrees(tree, 8));
            }
            Assert.True(headings.Count > 20,
                "forty trees produced only " + headings.Count + " distinct fall directions");
        }

        // ------------------------------------------------------------------
        // The rotation on the wire
        // ------------------------------------------------------------------

        [Fact]
        public void A_log_starts_at_the_standing_trees_exact_rotation()
        {
            // If this ever returns anything else the log SNAPS to a new facing on
            // the frame it appears, before it has begun to fall - the one artefact
            // that would be more obviously wrong than the tree vanishing.
            uint parent = Quaternion32Packing.Identity;
            Assert.Equal(parent, TreeFall.PackedRotationAt(parent, 137, TimeSpan.Zero, Fall));

            uint rotated = Quaternion32Packing.Encode(0.9238795f, 0f, 0.3826834f, 0f); // 45 deg yaw
            Assert.Equal(rotated, TreeFall.PackedRotationAt(rotated, 137, TimeSpan.Zero, Fall));
        }

        [Fact]
        public void A_log_off_a_rotated_tree_still_falls_a_quarter_turn()
        {
            // The topple composes ON the parent's rotation rather than replacing it,
            // so the swept angle is ninety whatever way the tree was facing.
            uint parent = Quaternion32Packing.Encode(0.9238795f, 0f, 0.3826834f, 0f);
            uint down = TreeFall.PackedRotationAt(parent, 42, Fall, Fall);

            Assert.Equal(90.0, AngleBetween(parent, down), 0);
        }

        [Fact]
        public void Different_headings_put_the_log_in_different_places()
        {
            uint parent = Quaternion32Packing.Identity;
            uint north = TreeFall.PackedRotationAt(parent, 0, Fall, Fall);
            uint east = TreeFall.PackedRotationAt(parent, 90, Fall, Fall);

            // Not ninety: two ninety-degree rotations about perpendicular axes
            // compose to a hundred and twenty. What matters is that the two logs end
            // up nowhere near each other.
            Assert.NotEqual(north, east);
            Assert.True(AngleBetween(north, east) > 60.0,
                "two logs felled in opposite directions ended up " + AngleBetween(north, east) + " deg apart");
        }

        // ------------------------------------------------------------------
        // Dropping a log
        // ------------------------------------------------------------------

        [Fact]
        public void A_cut_becomes_a_log_carrying_exactly_what_left_the_tree()
        {
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);

            // A real cut, computed by the topology rather than made up, so the
            // conservation law below is asserted against production arithmetic.
            TreeTopology tree = Trees.Topology();
            TreeCut cut = tree.Cut(Trees.FullSectionMask, 8, false);
            Assert.True(cut.DidCut);

            TreeSectionMaskChange change = new TreeSectionMaskChange(
                Tree, Cutter, cut.SectionId, cut.FallingMask, cut.RemainingMask,
                tree.ActiveCount(cut.FallingMask), Trees.WoodType);

            FelledLog? log = logs.Drop(LogId, change, Asset, Context, Where, Quaternion32Packing.Identity, Trees.SectionCount);

            Assert.NotNull(log);
            Assert.Equal(change.LogMask, log!.Value.SectionMask);
            Assert.Equal(Tree, log.Value.TreeEntityId);
            Assert.Equal(Where, log.Value.Position);
            Assert.Equal(Trees.SectionCount, log.Value.SectionCount);
            Assert.Equal(Trees.WoodType, log.Value.WoodType);
            Assert.Equal(Asset, log.Value.AssetName);
            Assert.Equal(Context, log.Value.AssetContext);

            // THE CONSERVATION LAW, in the three-way form it took when the cut
            // stopped paying for everything it severed. Every section of the tree is
            // in exactly one of THREE places: still standing, lying in the log, or
            // already in the chopper's inventory as the one section that splintered
            // under the beam. Two would be a section rendered twice or nowhere; the
            // third is the whole reason a tree no longer comes apart in one go.
            Assert.Equal(0, log.Value.SectionMask & cut.RemainingMask);
            Assert.Equal(0, log.Value.SectionMask & change.SplinterMask);
            Assert.Equal(0, cut.RemainingMask & change.SplinterMask);
            Assert.Equal(Trees.FullSectionMask,
                log.Value.SectionMask | cut.RemainingMask | change.SplinterMask);

            // And the splinter is exactly one section, so a cut can never pay for
            // more than the piece it broke off.
            Assert.Equal(1, change.SectionsSplintered);
        }

        [Fact]
        public void Conservation_holds_for_a_whole_tree_chopped_from_every_angle()
        {
            TreeTopology tree = Trees.Topology();

            for (int aimed = 0; aimed < Trees.SectionCount; aimed++)
            {
                FakeClock clock = new FakeClock();
                FallingLogs logs = Logs(clock, max: 64);

                int mask = Trees.FullSectionMask;
                long nextId = LogId;

                while (true)
                {
                    TreeCut cut = tree.Cut(mask, aimed, false);
                    if (!cut.DidCut)
                    {
                        break;
                    }

                    TreeSectionMaskChange change = new TreeSectionMaskChange(
                        Tree, Cutter, cut.SectionId, cut.FallingMask, cut.RemainingMask,
                        tree.ActiveCount(cut.FallingMask), Trees.WoodType);

                    FelledLog? log = logs.Drop(nextId++, change, Asset, Context, Where,
                        Quaternion32Packing.Identity, Trees.SectionCount);

                    // A cut that severed a single outermost section leaves nothing to
                    // fall - the whole of it splintered into wood - and drops no log.
                    int logMask = log?.SectionMask ?? 0;

                    Assert.Equal(0, logMask & cut.RemainingMask);
                    Assert.Equal(0, logMask & change.SplinterMask);
                    Assert.Equal(0, cut.RemainingMask & change.SplinterMask);
                    Assert.Equal(mask, logMask | cut.RemainingMask | change.SplinterMask);
                    Assert.Equal(1, change.SectionsSplintered);

                    mask = cut.RemainingMask;
                }

                // Something is always left standing - the shipped game never clears
                // the last section - so a chopped-out tree ends as a stump, exactly
                // as it did before logs existed.
                Assert.True(tree.ActiveCount(mask) >= 1);
                Assert.NotEqual(0, mask);
            }
        }

        [Fact]
        public void A_cut_that_severed_nothing_drops_no_log()
        {
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);

            Assert.Null(logs.Drop(LogId, Change(3, 0, Trees.FullSectionMask), Asset, Context, Where,
                Quaternion32Packing.Identity, Trees.SectionCount));
            Assert.Equal(0, logs.Count);
        }

        [Fact]
        public void The_budget_refuses_rather_than_letting_a_treeline_flood_the_wire()
        {
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock, max: 2);

            Assert.NotNull(logs.Drop(1, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12));
            Assert.NotNull(logs.Drop(2, Change(9, 0x600, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12));
            Assert.Null(logs.Drop(3, Change(10, 0x400, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12));

            Assert.Equal(2, logs.Count);
        }

        [Fact]
        public void The_budget_frees_up_again_once_a_log_is_retired()
        {
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock, max: 1);

            Assert.NotNull(logs.Drop(1, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12));
            Assert.Null(logs.Drop(2, Change(9, 0x600, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12));

            clock.Advance(Fall + Linger);
            Assert.Single(logs.DueRemovals());

            Assert.NotNull(logs.Drop(2, Change(9, 0x600, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12));
        }

        [Fact]
        public void The_same_entity_id_is_never_two_logs()
        {
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);

            Assert.NotNull(logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12));
            Assert.Null(logs.Drop(LogId, Change(9, 0x600, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12));
            Assert.Equal(1, logs.Count);
        }

        // ------------------------------------------------------------------
        // The arc on the wire
        // ------------------------------------------------------------------

        [Fact]
        public void The_first_pose_is_the_log_standing_exactly_where_the_crown_was()
        {
            // It has to be on the wire before the crown's mask push removes the
            // crown, or there is a frame in which the tree is visibly bald.
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);
            logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);

            FallingLogPose pose = Assert.Single(logs.DuePoses());

            Assert.Equal(LogId, pose.LogEntityId);
            Assert.Equal(Where, pose.Position);
            Assert.Equal(Quaternion32Packing.Identity, pose.PackedRotation);
            Assert.False(pose.Landed);
        }

        [Fact]
        public void Poses_come_at_the_interval_and_not_once_per_main_loop_turn()
        {
            // THE trap this project has already paid for once: the main loop turns
            // once per ENet EVENT, so a per-call pose would push hundreds of
            // transform updates a second on a busy server.
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);
            logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);

            Assert.Single(logs.DuePoses());
            for (int i = 0; i < 100; i++)
            {
                Assert.Empty(logs.DuePoses());
            }

            clock.Advance(Pose);
            Assert.Single(logs.DuePoses());
        }

        [Fact]
        public void A_whole_fall_is_a_bounded_number_of_updates()
        {
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);
            logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);

            int poses = 0;
            for (int i = 0; i < 200; i++)
            {
                poses += logs.DuePoses().Count;
                clock.Advance(TimeSpan.FromSeconds(0.02));
            }

            // 1.6 s of falling at the pose interval, plus the flat pose and its four
            // unreliable-channel repeats. The point of the assertion is the ORDER of
            // magnitude: a fall must cost tens of packets, not hundreds.
            Assert.InRange(poses, 25, 45);
        }

        [Fact]
        public void The_last_pose_is_flat_and_flagged_even_when_the_tick_is_late()
        {
            // A tick can arrive well after the fall ended. Without the clamp the log
            // would be left frozen at whatever angle the previous tick produced.
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);
            logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);

            logs.DuePoses();
            clock.Advance(TimeSpan.FromSeconds(9));

            FallingLogPose pose = Assert.Single(logs.DuePoses());
            Assert.True(pose.Landed);
            Assert.Equal(90.0, AngleBetween(Quaternion32Packing.Identity, pose.PackedRotation), 0);
        }

        [Fact]
        public void A_settled_log_is_silent_for_the_rest_of_its_life()
        {
            // Otherwise a clearing full of logs costs 20 Hz per log for ever.
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);
            logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);

            for (int i = 0; i < 200; i++)
            {
                logs.DuePoses();
                clock.Advance(Pose);
            }

            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Empty(logs.DuePoses());
        }

        [Fact]
        public void The_flat_pose_is_repeated_because_190602_is_unreliable()
        {
            // 190602 is in MirrorSendPolicy's unreliable set - correctly, a
            // superseding stream must never build a reliable backlog - so the ONE
            // update that says "the log is down" can simply be lost. Lose it and the
            // log hangs in the air at eighty-odd degrees until it is removed.
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock, repeats: 4);
            logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);

            int flat = 0;
            for (int i = 0; i < 400; i++)
            {
                foreach (FallingLogPose pose in logs.DuePoses())
                {
                    if (pose.Landed)
                    {
                        flat++;
                        Assert.Equal(90.0, AngleBetween(Quaternion32Packing.Identity, pose.PackedRotation), 0);
                    }
                }
                clock.Advance(Pose);
            }

            Assert.Equal(5, flat);   // the landing, plus four repeats
        }

        [Fact]
        public void Repeats_can_be_turned_off_without_stranding_a_log()
        {
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock, repeats: 0);
            logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);

            int flat = 0;
            for (int i = 0; i < 400; i++)
            {
                foreach (FallingLogPose pose in logs.DuePoses())
                {
                    if (pose.Landed) flat++;
                }
                clock.Advance(Pose);
            }

            Assert.Equal(1, flat);
        }

        // ------------------------------------------------------------------
        // What the component serializer asks a log
        // ------------------------------------------------------------------

        [Fact]
        public void A_log_reports_its_own_mask_so_it_does_not_check_out_as_a_whole_tree()
        {
            // The 1036 branch falls back to Trees.FullSectionMask for an entity it
            // does not recognise. Without this lookup a log would render as a complete
            // tree standing inside the one it fell off in the window between being
            // dropped and being planted as a felled stand.
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);
            logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);

            Assert.True(logs.IsLog(LogId));

            // Section 8 splintered into wood under the beam, so the log carries the
            // other three bits of what came away and not that one.
            Assert.Equal(0xF00 & ~(1 << 8), logs.MaskOf(LogId));
            Assert.NotEqual(Trees.FullSectionMask, logs.MaskOf(LogId));
            Assert.Equal(12, logs.SectionCountOf(LogId));
            Assert.Equal(Trees.WoodType, logs.WoodTypeOf(LogId));
            Assert.Equal(Tree, logs.TreeOf(LogId));
        }

        [Fact]
        public void Anything_that_is_not_a_log_answers_null_rather_than_guessing()
        {
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);

            Assert.False(logs.IsLog(Tree));
            Assert.Null(logs.MaskOf(Tree));
            Assert.Null(logs.SectionCountOf(Tree));
            Assert.Null(logs.WoodTypeOf(Tree));
            Assert.Null(logs.TreeOf(Tree));
        }

        [Fact]
        public void A_retired_log_stops_answering_at_once()
        {
            // A stale answer here would be a removed entity still resolving a mask.
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);
            logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);

            clock.Advance(Fall + Linger);
            logs.DueRemovals();

            Assert.Null(logs.MaskOf(LogId));
        }

        [Fact]
        public void The_angle_climbs_monotonically_across_a_real_fall()
        {
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);
            logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);

            double previous = -1;
            for (int i = 0; i < 60; i++)
            {
                foreach (FallingLogPose pose in logs.DuePoses())
                {
                    double angle = AngleBetween(Quaternion32Packing.Identity, pose.PackedRotation);
                    Assert.True(angle >= previous - 0.5,
                        "the log went back up: " + angle + " after " + previous);
                    previous = angle;
                }
                clock.Advance(Pose);
            }

            Assert.Equal(90.0, previous, 0);
        }

        // ------------------------------------------------------------------
        // Retiring a log
        // ------------------------------------------------------------------

        [Fact]
        public void A_log_lies_there_for_the_linger_before_it_is_removed()
        {
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);
            logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);

            clock.Advance(Fall);
            Assert.Empty(logs.DueRemovals());

            clock.Advance(Linger - TimeSpan.FromSeconds(0.1));
            Assert.Empty(logs.DueRemovals());

            clock.Advance(TimeSpan.FromSeconds(0.2));
            Assert.Equal(new[] { LogId }, logs.DueRemovals());
        }

        [Fact]
        public void A_log_is_reported_for_removal_exactly_once()
        {
            // Reported twice, the second RemoveEntity names an entity the client has
            // already dropped; never reported, the log is on screen for ever.
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);
            logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);

            clock.Advance(Fall + Linger);
            Assert.Single(logs.DueRemovals());
            Assert.Empty(logs.DueRemovals());
            Assert.Equal(0, logs.Count);
            Assert.False(logs.IsLog(LogId));
        }

        [Fact]
        public void Lengthening_the_fall_never_shortens_the_time_the_trunk_is_visible()
        {
            FakeClock clock = new FakeClock();
            FallingLogs slow = new FallingLogs(clock, TimeSpan.FromSeconds(5), Linger, 8, Pose);
            slow.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);

            clock.Advance(TimeSpan.FromSeconds(5) + Linger - TimeSpan.FromSeconds(0.1));
            Assert.Empty(slow.DueRemovals());

            clock.Advance(TimeSpan.FromSeconds(0.2));
            Assert.Single(slow.DueRemovals());
        }

        [Fact]
        public void Several_logs_fall_and_retire_independently()
        {
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);

            logs.Drop(1, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);
            clock.Advance(TimeSpan.FromSeconds(1));
            logs.Drop(2, Change(9, 0x600, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);

            clock.Advance(Fall + Linger - TimeSpan.FromSeconds(1));
            Assert.Equal(new[] { 1L }, logs.DueRemovals());
            Assert.Equal(1, logs.Count);

            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(new[] { 2L }, logs.DueRemovals());
            Assert.Equal(0, logs.Count);
        }

        [Fact]
        public void Clear_drops_everything_without_reporting_it()
        {
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);
            logs.Drop(1, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);
            logs.Drop(2, Change(9, 0x600, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);

            logs.Clear();

            Assert.Equal(0, logs.Count);
            clock.Advance(Fall + Linger);
            Assert.Empty(logs.DueRemovals());
        }

        // ------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void A_fall_with_no_duration_is_refused(int seconds)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new FallingLogs(new FakeClock(), TimeSpan.FromSeconds(seconds)));
        }

        [Fact]
        public void A_pose_interval_of_zero_is_refused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new FallingLogs(new FakeClock(), Fall, Linger, 8, TimeSpan.Zero));
        }

        [Fact]
        public void A_zero_budget_simply_drops_no_logs()
        {
            // Not an error: it is the kill switch, and it must behave exactly like
            // the server did before falling logs existed.
            FakeClock clock = new FakeClock();
            FallingLogs logs = new FallingLogs(clock, Fall, Linger, 0, Pose);

            Assert.Null(logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12));
            Assert.Equal(0, logs.Count);
            Assert.Empty(logs.DuePoses());
            Assert.Empty(logs.DueRemovals());
        }

        // ------------------------------------------------------------------
        // Entity ids
        // ------------------------------------------------------------------

        [Fact]
        public void Log_ids_come_from_a_band_the_world_registry_can_never_reach()
        {
            // A log is NOT a world registration - see TreeFall.FirstLogEntityId for
            // the three registry-driven paths that would otherwise leak it. Its ids
            // therefore have to be allocated somewhere else, and somewhere that
            // cannot collide.
            FallingLogs logs = Logs(new FakeClock());

            long first = logs.NextEntityId();
            long second = logs.NextEntityId();

            Assert.Equal(TreeFall.FirstLogEntityId, first);
            Assert.Equal(TreeFall.FirstLogEntityId + 1, second);
            Assert.True(first > 1_000_000, "a log id must be far above any registry id");
        }

        [Fact]
        public void A_log_id_is_never_reused_after_the_log_is_removed()
        {
            // Same rule EntityIdAllocator keeps: a packet still in flight for a
            // retired log must never be able to name a new one.
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);

            long first = logs.NextEntityId();
            logs.Drop(first, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);
            clock.Advance(Fall + Linger);
            logs.DueRemovals();

            Assert.NotEqual(first, logs.NextEntityId());
        }

        // ------------------------------------------------------------------
        // Checking a log out mid-fall
        // ------------------------------------------------------------------

        [Fact]
        public void A_player_arriving_mid_fall_is_seeded_at_the_angle_reached_so_far()
        {
            // Seeded upright, the log would visibly snap flat the instant the next
            // pose arrived.
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);
            logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where, Quaternion32Packing.Identity, 12);

            Assert.Equal(Quaternion32Packing.Identity, logs.RotationOf(LogId));

            clock.Advance(TimeSpan.FromSeconds(Fall.TotalSeconds / 2));
            double half = AngleBetween(Quaternion32Packing.Identity, logs.RotationOf(LogId)!.Value);
            Assert.InRange(half, 20.0, 25.0);   // 22.5 deg, the quadratic midpoint

            clock.Advance(Fall);
            Assert.Equal(90.0, AngleBetween(Quaternion32Packing.Identity, logs.RotationOf(LogId)!.Value), 0);
        }

        [Fact]
        public void A_late_checkout_gets_the_logs_own_prefab_not_the_default_tree()
        {
            // A palm must shed a palm: the asset comes off the PARENT registration.
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);
            logs.Drop(LogId, Change(8, 0xF00, 0x0FF), "TreePalm1", Context, Where,
                Quaternion32Packing.Identity, 13);

            Assert.Equal("TreePalm1", logs.AssetNameOf(LogId));
            Assert.Equal(Context, logs.AssetContextOf(LogId));
            Assert.Equal(Where, logs.PositionOf(LogId));
            Assert.Equal(13, logs.SectionCountOf(LogId));
        }

        [Fact]
        public void A_log_is_dynamic_and_a_standing_tree_is_not()
        {
            // The two constants MUST disagree. dynamic=true starts falling audio and
            // leaves the relative-transform behaviour enabled - a trap on a standing
            // tree, the whole point on a log.
            Assert.True(TreeFall.LogIsDynamic);
            Assert.False(Trees.Dynamic);
        }

        [Fact]
        public void The_shipped_cadence_is_the_one_every_other_moving_thing_uses()
        {
            // 20 Hz, DERIVED from RelayCadencePolicy rather than written out again.
            // The standing rule after this project's desync spiral is that no new
            // sender invents its own rate.
            Assert.Equal(RelayCadencePolicy.IntervalFor(RelayCadencePolicy.DefaultHz),
                TreeFall.PoseInterval);
        }

        // ------------------------------------------------------------------
        // The operator knobs
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("1")]
        [InlineData("yes")]
        public void Felled_logs_are_on_unless_the_operator_says_otherwise(string? env)
        {
            Assert.True(TreeFall.FallEnabled(env));
        }

        [Theory]
        [InlineData("0")]
        [InlineData(" 0 ")]
        public void Exactly_zero_is_the_kill_switch(string env)
        {
            Assert.False(TreeFall.FallEnabled(env));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("-1")]
        [InlineData("lots")]
        public void A_bad_budget_falls_back_rather_than_stopping_a_boot(string? env)
        {
            // A typo in an environment variable must never stop a server booting.
            Assert.Null(TreeFall.ParseBudget(env));
        }

        [Fact]
        public void A_budget_of_zero_is_accepted_as_a_second_kill_switch()
        {
            Assert.Equal(0, TreeFall.ParseBudget("0"));
            Assert.Equal(3, TreeFall.ParseBudget(" 3 "));
        }

        // ------------------------------------------------------------------
        // The multiplayer-safety contract
        // ------------------------------------------------------------------

        [Fact]
        public void The_pose_stream_rides_the_unreliable_channel()
        {
            // The rule this project learned twice, at the cost of a congestion
            // collapse each time: a per-tick superseding stream sent RELIABLY builds
            // an ordered backlog and takes the peer down with it. Every pose is the
            // complete absolute transform, so losing one costs a frame and nothing
            // more - which is exactly what makes unreliable correct here.
            Assert.Equal(RelayReliability.Unreliable, MirrorSendPolicy.RelayReliabilityFor(190602));
        }

        [Fact]
        public void A_clearing_full_of_landed_logs_costs_nothing_to_keep()
        {
            // The number the multiplayer-safety audit asks for, DRIVEN rather than
            // arithmetic: what does a full budget of logs cost once they are down?
            // The previous version of this test computed `budget / interval` in the
            // test and asserted the answer, which is a restatement of two constants
            // and survives deleting the entire pose path. This one fills the budget,
            // lets every log land, and asserts the registry then asks for nothing -
            // which is the property that makes raising the budget affordable.
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock, max: TreeFall.DefaultMaxConcurrent);

            for (int i = 0; i < TreeFall.DefaultMaxConcurrent; i++)
            {
                Assert.NotNull(logs.Drop(LogId + i, Change(8, 0xF00, 0x0FF), Asset, Context, Where,
                    Quaternion32Packing.Identity, 12));
            }
            Assert.False(logs.HasCapacity);

            // Run the whole fall out, draining poses the way the main loop does.
            for (int turn = 0; turn < 200; turn++)
            {
                logs.DuePoses();
                clock.Advance(Pose);
            }

            // Every log is down and silent. Not "sends less", NONE: a settled log is
            // never pushed again until it is removed.
            Assert.Equal(TreeFall.DefaultMaxConcurrent, logs.Count);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Empty(logs.DuePoses());
        }

        // ------------------------------------------------------------------
        // WHICH WAY IT ACTUALLY GOES OVER
        // ------------------------------------------------------------------

        /// <summary>
        /// Rotates a vector by a packed rotation - q v q* - so a test can ask where
        /// the crown ENDED UP rather than only how far it turned.
        ///
        /// This is the oracle the suite was missing. Every existing assertion about
        /// the fall measures an ANGLE, and an angle is blind to the two things most
        /// likely to be wrong: the order the topple is composed with the tree's own
        /// rotation, and the mapping from a compass heading to a world direction.
        /// Both are conjugations, so both leave every angle in the file unchanged.
        /// </summary>
        private static (double X, double Y, double Z) Rotate(uint packed, double x, double y, double z)
        {
            (float w, float qx, float qy, float qz) = Quaternion32Packing.Decode(packed);

            // t = 2 * (q_vec x v); v' = v + w*t + q_vec x t
            double tx = 2.0 * ((qy * z) - (qz * y));
            double ty = 2.0 * ((qz * x) - (qx * z));
            double tz = 2.0 * ((qx * y) - (qy * x));

            return (x + (w * tx) + ((qy * tz) - (qz * ty)),
                    y + (w * ty) + ((qz * tx) - (qx * tz)),
                    z + (w * tz) + ((qx * ty) - (qy * tx)));
        }

        [Fact]
        public void A_log_off_a_ROTATED_tree_still_falls_the_way_the_heading_says()
        {
            // THE HOLE THIS CLOSES. TreeFall.PackedRotationAt composes the world-frame
            // topple with the tree's own rotation, and the argument order matters: the
            // topple must be applied in the WORLD frame, after the tree's rotation,
            // or the axis is itself spun by the tree's yaw and every log off a rotated
            // tree goes over in the wrong direction. Swapping the two operands leaves
            // the ANGLE between the standing and fallen poses at exactly ninety
            // degrees either way, so nothing that measures an angle can see it - and
            // nothing in this suite measured anything else.
            //
            // The 13,266 release-world trees are placed with authored yaws, so this is
            // the case that actually ships.
            const double half = Math.PI / 4.0;    // 90 degrees, halved for the quaternion
            uint yawed = Quaternion32Packing.Encode(
                (float)Math.Cos(half), 0f, (float)Math.Sin(half), 0f);

            // Quaternion32 is a lossy wire format, so everything here is asserted to
            // a tolerance an eye could not see rather than to decimal places.
            const double slop = 0.02;

            // The tree is turned but still upright: its trunk points at world up.
            (double ux, double uy, double uz) = Rotate(yawed, 0, 1, 0);
            Assert.True(Math.Abs(ux) < slop && Math.Abs(uy - 1.0) < slop && Math.Abs(uz) < slop,
                "a yawed tree is still upright, got (" + ux + ", " + uy + ", " + uz + ")");

            foreach (double heading in new[] { 0.0, 90.0, 180.0, 270.0 })
            {
                uint down = TreeFall.PackedRotationAt(yawed, heading, Fall, Fall);

                // Where the trunk points once the log is flat.
                (double x, double y, double z) = Rotate(down, 0, 1, 0);

                // It is on the ground: no vertical component left.
                Assert.True(Math.Abs(y) < slop,
                    "a landed log is flat; heading " + heading + " left y=" + y);

                // And it points along the heading it was given, in the WORLD frame,
                // unaffected by the tree's own yaw. This is the assertion the yaw
                // would break if the composition were the other way round.
                double radians = heading * Math.PI / 180.0;
                Assert.True(Math.Abs(x - Math.Sin(radians)) < slop
                            && Math.Abs(z - Math.Cos(radians)) < slop,
                    "heading " + heading + " should drop the crown towards ("
                    + Math.Sin(radians) + ", 0, " + Math.Cos(radians) + "), got ("
                    + x + ", " + y + ", " + z + ")");
            }
        }

        // ------------------------------------------------------------------
        // Who is shown a log - the predicate that silently broke the feature
        // ------------------------------------------------------------------

        [Fact]
        public void A_peer_that_merely_holds_the_tree_is_shown_the_log_it_sheds()
        {
            // THE REGRESSION TEST FOR THE BUG THAT MADE THIS WHOLE FEATURE INVISIBLE.
            // Every release-world tree is streamed in by ResourceInterestService,
            // which keeps its own per-peer loaded set and never writes the global
            // send ledger. Gating the log on the send ledger alone therefore answered
            // "no" for every tree in the world: the live server logged
            // "shown to 0 peer(s)" on the same line as "pushed sectionMask ... to 1
            // checked-out peer(s)", i.e. the tree lost its sections on screen and
            // nothing fell. Holding the tree's components is the evidence the mask
            // push itself uses, so the two can never disagree about who is watching.
            Assert.True(TreeFall.MayShowLog(
                addEntitySent: false, holdsTreeComponents: true, canReceiveRemove: true));
        }

        [Fact]
        public void A_peer_from_the_connect_plan_is_shown_the_log_too()
        {
            // The other ledger, for a tree that came out of the connect-time spawn
            // plan rather than out of streaming. Either is sufficient; that is what
            // OR means and why neither was allowed to become the only question asked.
            Assert.True(TreeFall.MayShowLog(
                addEntitySent: true, holdsTreeComponents: false, canReceiveRemove: true));
        }

        [Fact]
        public void A_peer_that_does_not_hold_the_tree_is_shown_nothing()
        {
            // The parent tree is the spatial proxy. A log appearing out of a tree the
            // player cannot see would be a trunk materialising in empty air.
            Assert.False(TreeFall.MayShowLog(
                addEntitySent: false, holdsTreeComponents: false, canReceiveRemove: true));
        }

        [Fact]
        public void A_peer_that_could_never_be_sent_the_removal_is_shown_nothing()
        {
            // A VETO, not a third alternative: a peer with no REMOVE_ENTITY_OP channel
            // would keep the log for the rest of its session. Permanent litter is
            // worse than a missing log, so it is never shown one however well it
            // holds the tree.
            Assert.False(TreeFall.MayShowLog(
                addEntitySent: true, holdsTreeComponents: true, canReceiveRemove: false));
        }

        // ------------------------------------------------------------------
        // A piece broken off something already on the ground
        // ------------------------------------------------------------------

        [Fact]
        public void A_piece_split_off_a_fallen_log_does_not_topple_again()
        {
            // Chopping a trunk that is already lying down splits a piece off it. That
            // piece is seeded at the trunk's CURRENT pose and is down from the instant
            // it exists; running it through the topple would tip an already-flat log
            // over a second time, and it is also what would make raising the budget
            // expensive, because a toppling log streams and a settled one does not.
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);

            uint lyingDown = TreeFall.PackedRotationAt(
                Quaternion32Packing.Identity, 90.0, Fall, Fall);

            Assert.NotNull(logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where,
                lyingDown, 12, alreadyDown: true));

            // It never moves off the pose it was born at, at any point in its life.
            Assert.Equal(lyingDown, logs.RotationOf(LogId));

            IReadOnlyList<FallingLogPose> first = logs.DuePoses();
            Assert.Single(first);
            Assert.True(first[0].Landed);
            Assert.Equal(lyingDown, first[0].PackedRotation);

            clock.Advance(Fall + Fall);
            Assert.Equal(lyingDown, logs.RotationOf(LogId));
        }

        [Fact]
        public void A_settled_piece_sends_only_its_landed_repeats_and_then_stops()
        {
            // The bandwidth claim behind the raised budget, asserted rather than
            // asserted-about: a piece that never topples costs one pose plus the
            // repeats that protect it from a dropped packet, and then nothing.
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock, repeats: 4);

            logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where,
                Quaternion32Packing.Identity, 12, alreadyDown: true);

            int sends = 0;
            for (int turn = 0; turn < 100; turn++)
            {
                sends += logs.DuePoses().Count;
                clock.Advance(Pose);
            }

            Assert.Equal(5, sends);
        }

        // ------------------------------------------------------------------
        // A log being chopped must not be deleted under the player
        // ------------------------------------------------------------------

        [Fact]
        public void Cutting_a_log_restarts_the_clock_that_would_have_removed_it()
        {
            // A log is now something you take apart at one section per cut interval.
            // A fixed countdown from the moment it landed would delete a half-chopped
            // trunk mid-swing, which is the same trap TreeHarvest's regrowth timer
            // avoids by counting from the LAST cut rather than the first.
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);
            logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where,
                Quaternion32Packing.Identity, 12);

            // Sit on it until it is one tick short of being retired, then cut it.
            clock.Advance(Fall + Linger - TimeSpan.FromSeconds(0.1));
            Assert.Empty(logs.DueRemovals());

            Assert.True(logs.Touch(LogId, 0x700));
            Assert.Equal(0x700, logs.MaskOf(LogId));

            // The old deadline passes and the log is still there.
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.Empty(logs.DueRemovals());
            Assert.True(logs.IsLog(LogId));

            // A full linger with nobody touching it and it goes, exactly as before.
            clock.Advance(Fall + Linger);
            Assert.Equal(new[] { LogId }, logs.DueRemovals());
        }

        [Fact]
        public void Touching_something_that_is_not_a_log_changes_nothing()
        {
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);

            Assert.False(logs.Touch(Tree, 0xF));
            Assert.False(logs.Forget(Tree));
        }

        [Fact]
        public void A_log_whose_last_section_has_gone_can_be_forgotten_at_once()
        {
            // The caller retires a log the moment its mask empties rather than
            // leaving an entity that draws nothing on every client until its linger
            // runs out.
            FakeClock clock = new FakeClock();
            FallingLogs logs = Logs(clock);
            logs.Drop(LogId, Change(8, 0xF00, 0x0FF), Asset, Context, Where,
                Quaternion32Packing.Identity, 12);

            Assert.True(logs.Forget(LogId));
            Assert.False(logs.IsLog(LogId));
            Assert.Null(logs.MaskOf(LogId));
            Assert.Equal(0, logs.Count);
        }
    }
}
