using System;
using System.Collections.Generic;
using System.IO;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// THE LONG-SHIP PATH, END TO END - the remedy for "my ship flies sideways",
    /// proved through the real production code rather than asserted in prose.
    ///
    /// WHY THIS SUITE EXISTS. A live player's two-cell raft measures 12.09 m of
    /// BEAM by 8.00 m of KEEL: genuinely 1.5x wider than it is long, so its bow -
    /// hull-local +Z, where the pilot camera looks and where W drives - runs across
    /// its short side. Two separate sessions were then spent hunting a rotation bug
    /// that does not exist. An adversarial verification pass settled it from four
    /// independent subsystems (the editor gizmo emits no lateral cell axis; the
    /// client's own <c>PlacementPreview</c> aligns deck parts to <c>ship.transform
    /// .forward</c> = local +Z and mirrors port/starboard on X; the hull editor
    /// renders at world identity with +Z into the screen; the player's helm is
    /// already at packed identity and <c>ShipHelmPlacement.Awake</c> blocks both
    /// placement-rotation modes so it cannot be misrotated). +Z is the bow.
    ///
    /// SO THE ONLY REAL REMEDY IS A LONGER HULL, and the thing worth testing is that
    /// the long-hull path actually delivers: that a keel-dominant design produces a
    /// long-on-Z deck, that its helm faces the bow, that flight drives that same
    /// bow, and that a restart does not quietly change any of it. Everything here
    /// runs on the pure production types with no game install.
    ///
    /// The REAL 60-byte live hull is asserted in <see cref="ShipHullMetricsTests"/>;
    /// this suite is deliberately synthetic so each hull's dimensions are obvious
    /// from its builder.
    /// </summary>
    public class LongHullForwardTests : IDisposable
    {
        private readonly string _dir;

        public LongHullForwardTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "wareborn-longhull-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        // ---- hull builders -----------------------------------------------------

        /// <summary>
        /// A contiguous single-deck row of <paramref name="cells"/> stock-width cells
        /// at cell numbers 0..n-1, written exactly as the client writes it: only the
        /// aft-most cell (the one with no astern neighbour) carries a Back section.
        ///
        /// The resulting keel is <c>4 * cells</c> metres (sections land at raw z =
        /// -1, +1, +3, ... and <see cref="ShipHullMetrics.ShipScale"/> is 2) and the
        /// beam is a flat 12 m at the stock half-width of 3.
        /// </summary>
        private static ShipPlanModel StockRow(int cells)
        {
            var plan = new ShipPlanModel();
            for (int c = 0; c < cells; c++)
            {
                plan.Cells.Add(new ShipCellModel(
                    cellNumber: c,
                    deckNumber: 0,
                    front: ShipSectionModel.MakeDefault(),
                    back: c == 0 ? ShipSectionModel.MakeDefault() : null));
            }
            return plan;
        }

        /// <summary>THE hull under test: four stock cells, keel 16 m &gt; beam 12 m.</summary>
        private static ShipPlanModel FourCellStockHull() => StockRow(4);

        /// <summary>The player's shape, synthesised: two stock cells, keel 8 m &lt; beam 12 m.</summary>
        private static ShipPlanModel TwoCellStockHull() => StockRow(2);

        // ---- geometry helpers --------------------------------------------------

        private readonly struct Extent
        {
            public Extent(float minX, float maxX, float minZ, float maxZ)
            {
                MinX = minX; MaxX = maxX; MinZ = minZ; MaxZ = maxZ;
            }

            public float MinX { get; }
            public float MaxX { get; }
            public float MinZ { get; }
            public float MaxZ { get; }

            public float SpanX => MaxX - MinX;
            public float SpanZ => MaxZ - MinZ;
        }

        /// <summary>
        /// The bounding box, in HULL-LOCAL METRES, of every deck panel the real
        /// <see cref="DeckGenerator"/> emits. A panel's vertices are centroid-relative
        /// and in RAW ShipPlan units while its position is already in metres (see
        /// <see cref="DeckPanel"/>), so a vertex's hull-local metre coordinate is
        /// <c>position + vertex * ShipScale</c> - the same recomposition the client's
        /// <c>MeshGenerator.MakeMesh</c> does.
        /// </summary>
        private static Extent DeckExtent(ShipPlanModel plan)
        {
            IReadOnlyList<DeckPanel> panels = DeckGenerator.Generate(plan);
            Assert.NotEmpty(panels);

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (DeckPanel panel in panels)
            {
                foreach (ShipVector3 v in panel.LocalVertices)
                {
                    float x = panel.HullLocalPositionMetres.X + (v.X * (float)ShipHullMetrics.ShipScale);
                    float z = panel.HullLocalPositionMetres.Z + (v.Z * (float)ShipHullMetrics.ShipScale);
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (z < minZ) minZ = z;
                    if (z > maxZ) maxZ = z;
                }
            }
            return new Extent(minX, maxX, minZ, maxZ);
        }

        /// <summary>Rotates a vector by a (w,x,y,z) quaternion: <c>q * v * conj(q)</c>.</summary>
        private static ShipVector3 Rotate((double W, double X, double Y, double Z) q, ShipVector3 v)
        {
            double w = q.W, x = q.X, y = q.Y, z = q.Z;
            // v' = v + 2w(q_v x v) + 2(q_v x (q_v x v)), expanded.
            double tx = 2.0 * ((y * v.Z) - (z * v.Y));
            double ty = 2.0 * ((z * v.X) - (x * v.Z));
            double tz = 2.0 * ((x * v.Y) - (y * v.X));
            return new ShipVector3(
                (float)(v.X + (w * tx) + ((y * tz) - (z * ty))),
                (float)(v.Y + (w * ty) + ((z * tx) - (x * tz))),
                (float)(v.Z + (w * tz) + ((x * ty) - (y * tx))));
        }

        private static void AssertSameDirection(ShipVector3 expected, ShipVector3 actual, int precision = 3)
        {
            Assert.Equal(expected.X, actual.X, precision);
            Assert.Equal(expected.Y, actual.Y, precision);
            Assert.Equal(expected.Z, actual.Z, precision);
        }

        // =====================================================================
        // (i) THE DECK: a long hull generates a long-on-Z floor.
        // =====================================================================

        /// <summary>
        /// Four stock cells measure keel 16 m by beam 12 m - keel-dominant, which is
        /// the shape a player has to build for the bow to be the long axis. Three
        /// cells only TIE (12 x 12); four is the first unambiguous win, which is why
        /// this is the hull the whole suite verifies.
        /// </summary>
        [Fact]
        public void A_four_cell_stock_hull_is_longer_than_it_is_wide()
        {
            ShipHullMetrics m = ShipHullMetrics.Measure(FourCellStockHull());

            Assert.Equal(12.0, m.BeamMetres, 4);
            Assert.Equal(16.0, m.KeelMetres, 4);
            Assert.True(m.KeelIsLongestAxis);
            Assert.Equal(4, m.CellsAlongKeel);
            Assert.Equal(0, m.CellsToAddForKeelToMatchBeam);
            Assert.Null(m.WideHullAdvice());
        }

        /// <summary>
        /// THE MEASUREMENT THAT KILLS THE "our pipeline swaps the axes" HYPOTHESIS.
        /// Run the REAL <see cref="DeckGenerator"/> - the same code the spawner calls
        /// to build a ship's floors - and measure the bounding box of the panels it
        /// emits. A four-cell hull's deck is 16 m along Z and 12 m along X: long on
        /// Z, exactly as the plan says. No rotation, no swap, nowhere.
        /// </summary>
        [Fact]
        public void DeckGenerator_emits_a_long_on_Z_deck_for_a_four_cell_hull()
        {
            Extent e = DeckExtent(FourCellStockHull());

            Assert.Equal(16.0f, e.SpanZ, 3);
            Assert.Equal(12.0f, e.SpanX, 3);
            Assert.True(e.SpanZ > e.SpanX,
                "a keel-dominant hull must generate a deck that is longest along Z");
        }

        /// <summary>
        /// The deck's fore-and-aft extremes ARE the hull's measured bow and stern, so
        /// "the deck is long on Z" and "the bow is at +Z" are the same fact rather
        /// than two coincidences. This is what would break first if the generator
        /// ever mirrored or offset the deck relative to the frame.
        /// </summary>
        [Fact]
        public void The_generated_decks_fore_edge_is_the_measured_bow()
        {
            ShipPlanModel plan = FourCellStockHull();
            ShipHullMetrics m = ShipHullMetrics.Measure(plan);
            Extent e = DeckExtent(plan);

            Assert.Equal((float)m.BowLocalZMetres, e.MaxZ, 3);
            Assert.Equal((float)m.SternLocalZMetres, e.MinZ, 3);
            Assert.True(e.MaxZ > e.MinZ, "the bow must sit at the +Z end of the deck");
        }

        /// <summary>
        /// The contrast case, so the suite states the diagnosis as well as the cure:
        /// the same generator on the player's two-cell shape produces a deck that is
        /// WIDER (12 m on X) than it is LONG (8 m on Z). Identical code, opposite
        /// result - which is only possible if the result follows the design.
        /// </summary>
        [Fact]
        public void The_same_generator_emits_a_wide_deck_for_the_two_cell_shape()
        {
            Extent e = DeckExtent(TwoCellStockHull());

            Assert.Equal(8.0f, e.SpanZ, 3);
            Assert.Equal(12.0f, e.SpanX, 3);
            Assert.True(e.SpanX > e.SpanZ);
        }

        // =====================================================================
        // (ii) THE HELM: mounted at the default lock, it faces the bow.
        // =====================================================================

        /// <summary>
        /// The helm lock's default is 0 degrees, which composes to hull-local
        /// IDENTITY and therefore leaves the Helm01 prefab's authored forward (+Z)
        /// pointing exactly at the hull's bow (+Z). It also packs to the identity
        /// SENTINEL, so a helm mounted under the default is byte-identical on the
        /// wire to an unrotated part - and the player's live helm, whose persisted
        /// PackedRotation is already 1023, is proof the live server does exactly
        /// this.
        /// </summary>
        [Fact]
        public void A_helm_at_the_default_lock_faces_the_hulls_bow()
        {
            Assert.Equal(0.0, HelmMountLock.DefaultYawDegrees);

            (float w, float x, float y, float z) = HelmMountLock.LockRotation(HelmMountLock.DefaultYawDegrees);
            Assert.Equal(1f, w, 6);
            Assert.Equal(0f, x, 6);
            Assert.Equal(0f, y, 6);
            Assert.Equal(0f, z, 6);

            Assert.Equal(Quaternion32Packing.Identity,
                HelmMountLock.PackedLockRotation(HelmMountLock.DefaultYawDegrees));

            // The prefab forward, rotated by the lock, is still the hull's bow.
            AssertSameDirection(
                ShipHullMetrics.BowDirection,
                Rotate((w, x, y, z), ShipHullMetrics.BowDirection));
        }

        /// <summary>
        /// A helm on the four-cell hull points down the hull's LONG axis, which is
        /// the whole claim being verified: bow direction rotated by the lock lands on
        /// +Z, and +Z is the 16 m side of a 16 x 12 deck.
        /// </summary>
        [Fact]
        public void On_a_long_hull_the_helm_points_down_the_long_axis()
        {
            ShipPlanModel plan = FourCellStockHull();
            Extent e = DeckExtent(plan);

            (float w, float x, float y, float z) = HelmMountLock.LockRotation(HelmMountLock.DefaultYawDegrees);
            ShipVector3 helmForward = Rotate((w, x, y, z), ShipHullMetrics.BowDirection);

            // The helm faces +Z...
            AssertSameDirection(new ShipVector3(0f, 0f, 1f), helmForward);
            // ...and +Z is the deck's longest axis.
            Assert.True(e.SpanZ > e.SpanX);
        }

        // =====================================================================
        // (iii) FLIGHT: the integrator drives that same +Z.
        // =====================================================================

        private static FlightState FlyStraight(int steps, double throttle = 1.0)
        {
            var tuning = new FlightTuning();
            var input = new FlightControlInput((float)throttle, 0f, 0f, 0f, 0f);
            FlightState state = FlightState.AtRestAt(0, 0, 0);
            for (int i = 0; i < steps; i++)
            {
                state = FlightIntegrator.Step(state, input, ShipMotionPolicy.SendIntervalSeconds, tuning);
            }
            return state;
        }

        /// <summary>
        /// Full throttle from rest drives the ship along +Z and nowhere else: the
        /// heading stays 0, the velocity is purely +Z, and the position advances on
        /// +Z. This is the flight half of "forward is the bow".
        /// </summary>
        [Fact]
        public void Full_throttle_from_rest_drives_the_ship_along_positive_Z()
        {
            FlightState s = FlyStraight(40);

            Assert.Equal(0.0, s.YawRadians, 9);
            Assert.Equal(0.0, s.VxMps, 6);
            Assert.True(s.VzMps > 0.0, "throttle forward must produce +Z velocity");
            Assert.True(s.Z > 0.0, "the ship must have travelled toward +Z");
            Assert.Equal(0.0, s.X, 6);
        }

        /// <summary>
        /// A ship flying straight ahead renders UNROTATED - the packed rotation is
        /// the identity sentinel - so its hull-local +Z is world +Z, which is the
        /// direction it is travelling. Camera, mesh and motion are the same axis by
        /// construction, and an unflown ship stays byte-identical on the wire.
        /// </summary>
        [Fact]
        public void A_ship_flying_straight_ahead_renders_unrotated()
        {
            FlightState s = FlyStraight(40);

            Assert.Equal(Quaternion32Packing.Identity, FlightIntegrator.PackedRotation(s));

            (double w, double x, double y, double z) = FlightIntegrator.AttitudeQuaternion(s);
            AssertSameDirection(
                new ShipVector3(0f, 0f, 1f),
                Rotate((w, x, y, z), ShipHullMetrics.BowDirection));
        }

        // =====================================================================
        // THE INTEGRATION ASSERTION: all three agree, level AND after a turn.
        // =====================================================================

        /// <summary>
        /// THE ONE THAT WOULD HAVE CAUGHT A REAL BUG. For the four-cell hull, four
        /// independently-derived directions must be the SAME world vector:
        /// <list type="number">
        ///   <item>the deck's long axis, rotated into the world by the hull's
        ///     rendered rotation;</item>
        ///   <item>the hull's bow (+Z), same rotation - what the pilot camera looks
        ///     down (<c>acs/PilotCameraController</c> takes the vehicle's rotation);</item>
        ///   <item>the helm's facing: the lock composed onto the hull's rotation;</item>
        ///   <item>the direction the ship is actually travelling.</item>
        /// </list>
        /// If any consumer ever grew a private idea of "forward", this fails.
        /// </summary>
        [Fact]
        public void Flight_forward_helm_facing_and_rendered_hull_rotation_are_one_direction()
        {
            ShipPlanModel plan = FourCellStockHull();
            Extent e = DeckExtent(plan);
            Assert.True(e.SpanZ > e.SpanX); // (1) the deck's long axis is local +Z

            FlightState s = FlyStraight(40);
            (double w, double x, double y, double z) hull = FlightIntegrator.AttitudeQuaternion(s);

            ShipVector3 deckLongAxisWorld = Rotate(hull, new ShipVector3(0f, 0f, 1f));
            ShipVector3 hullBowWorld = Rotate(hull, ShipHullMetrics.BowDirection);

            (float lw, float lx, float ly, float lz) =
                HelmMountLock.LockRotation(HelmMountLock.DefaultYawDegrees);
            // The helm's WORLD facing = hull rotation composed with the hull-local lock.
            (float cw, float cx, float cy, float cz) = HelmMountLock.Compose(
                ((float)hull.w, (float)hull.x, (float)hull.y, (float)hull.z), (lw, lx, ly, lz));
            ShipVector3 helmForwardWorld = Rotate((cw, cx, cy, cz), ShipHullMetrics.BowDirection);

            double speed = Math.Sqrt((s.VxMps * s.VxMps) + (s.VzMps * s.VzMps));
            Assert.True(speed > 0.1, "the ship must actually be moving for this to mean anything");
            var travel = new ShipVector3((float)(s.VxMps / speed), 0f, (float)(s.VzMps / speed));

            AssertSameDirection(travel, deckLongAxisWorld);
            AssertSameDirection(travel, hullBowWorld);
            AssertSameDirection(travel, helmForwardWorld);
        }

        /// <summary>
        /// The same four directions after a TURN, which is the case that actually
        /// exercises the rotation composition rather than the identity. Turn hard for
        /// a while, then centre the stick and hold throttle until the carve washes
        /// out (the velocity vector chases the heading and SNAPS onto it inside the
        /// integrator's epsilon), and the ship's rendered heading must again be
        /// exactly the direction it is travelling - with the helm still on the nose.
        /// </summary>
        [Fact]
        public void After_a_turn_the_rendered_heading_is_still_the_direction_of_travel()
        {
            var tuning = new FlightTuning();
            FlightState s = FlightState.AtRestAt(0, 0, 0);

            var turning = new FlightControlInput(1f, 0f, 0f, 1f, 0f);
            for (int i = 0; i < 30; i++)
            {
                s = FlightIntegrator.Step(s, turning, ShipMotionPolicy.SendIntervalSeconds, tuning);
            }
            Assert.True(Math.Abs(s.YawRadians) > 0.1, "the ship must have actually turned");

            var straight = new FlightControlInput(1f, 0f, 0f, 0f, 0f);
            for (int i = 0; i < 60; i++)
            {
                s = FlightIntegrator.Step(s, straight, ShipMotionPolicy.SendIntervalSeconds, tuning);
            }

            (double w, double x, double y, double z) hull = FlightIntegrator.AttitudeQuaternion(s);
            ShipVector3 hullBowWorld = Rotate(hull, ShipHullMetrics.BowDirection);

            (float lw, float lx, float ly, float lz) =
                HelmMountLock.LockRotation(HelmMountLock.DefaultYawDegrees);
            (float cw, float cx, float cy, float cz) = HelmMountLock.Compose(
                ((float)hull.w, (float)hull.x, (float)hull.y, (float)hull.z), (lw, lx, ly, lz));
            ShipVector3 helmForwardWorld = Rotate((cw, cx, cy, cz), ShipHullMetrics.BowDirection);

            double speed = Math.Sqrt((s.VxMps * s.VxMps) + (s.VzMps * s.VzMps));
            var travel = new ShipVector3((float)(s.VxMps / speed), 0f, (float)(s.VzMps / speed));

            AssertSameDirection(travel, hullBowWorld);
            AssertSameDirection(travel, helmForwardWorld);
        }

        // =====================================================================
        // (iv) PERSISTENCE: a restart changes none of it.
        // =====================================================================

        /// <summary>
        /// A four-cell ship written to the world-state file and read back reproduces
        /// the IDENTICAL hull bytes, the identical measured shape and - through the
        /// real <see cref="DeckGenerator"/> and the real
        /// <see cref="BuiltShipPlacement.ResolveHullBytes"/> gate the boot restore
        /// runs - the identical long-on-Z deck. Forward is derived from the design,
        /// so persistence has nothing separate to keep in sync and cannot drift.
        ///
        /// BOTH SIDES ARE MEASURED FROM THE BYTES, because that is what production
        /// does: the runtime build path decodes the very bytes it registers for 1209
        /// (<c>BuiltShipSpawner.TryGeneratePanels(effectiveBytes)</c>) and so does the
        /// restore. Comparing the decoded plan against the in-memory one instead
        /// would only be re-measuring the wire format's sbyte quantisation - which is
        /// why the beam here reads 12.09 m and not a round 12: a stock half-width of
        /// 3 quantises to 24/127*16 = 3.0236, the same 12.09 the live player's hull
        /// reports. The KEEL is exact, because the section planes are computed from
        /// cell numbers and the stock z offsets are zero.
        /// </summary>
        [Fact]
        public void A_restored_four_cell_ship_keeps_its_long_on_Z_forward()
        {
            byte[] built = FourCellStockHull().Encode();
            Assert.True(ShipPlanModel.TryDecode(built, out ShipPlanModel? asBuilt, out _));
            Extent before = DeckExtent(asBuilt!);

            string path = Path.Combine(_dir, "world.json");
            var snapshot = new WorldStateSnapshot();
            snapshot.BuiltShips.Add(new BuiltShipRecord
            {
                HullX = 1, HullY = 2, HullZ = 3,
                HullBytes = built,
                OwnerCharacterUid = "owner",
            });
            AtomicJsonFile.Write(path, snapshot);

            WorldStateSnapshot? read = AtomicJsonFile.Read<WorldStateSnapshot>(path);
            Assert.NotNull(read);
            BuiltShipRecord record = Assert.Single(read!.BuiltShips);

            // The restore path's own gate on the stored bytes - the fallback must NOT trip.
            byte[] resolved = BuiltShipPlacement.ResolveHullBytes(record.HullBytes, out bool usedFallback);
            Assert.False(usedFallback);
            Assert.Equal(built, resolved);

            Assert.True(ShipPlanModel.TryDecode(resolved, out ShipPlanModel? restored, out _));
            ShipHullMetrics m = ShipHullMetrics.Measure(restored!);
            Assert.Equal(16.0, m.KeelMetres, 4);          // exact: computed from cell numbers
            Assert.Equal(12.09, m.BeamMetres, 2);         // quantised, exactly as the live hull reads
            Assert.True(m.KeelIsLongestAxis);
            Assert.Null(m.WideHullAdvice());

            Extent after = DeckExtent(restored!);
            Assert.Equal(before.SpanZ, after.SpanZ, 3);
            Assert.Equal(before.SpanX, after.SpanX, 3);
            Assert.Equal(before.MaxZ, after.MaxZ, 3);
        }

        /// <summary>
        /// A mounted helm's persisted rotation is the identity lock, and it survives
        /// the same round trip - so a restored ship's wheel comes back on the bow
        /// rather than at whatever facing the player happened to be standing at.
        /// </summary>
        [Fact]
        public void A_restored_helm_mount_keeps_the_identity_lock()
        {
            string path = Path.Combine(_dir, "world-mount.json");
            var snapshot = new WorldStateSnapshot();
            snapshot.MountedParts.Add(new MountedPartRecord
            {
                PartUid = "helm-uid",
                BuiltShipIndex = 0,
                ItemType = "helm",
                PackedRotation = HelmMountLock.PackedLockRotation(HelmMountLock.DefaultYawDegrees),
            });
            AtomicJsonFile.Write(path, snapshot);

            WorldStateSnapshot? read = AtomicJsonFile.Read<WorldStateSnapshot>(path);
            MountedPartRecord record = Assert.Single(read!.MountedParts);

            Assert.Equal(Quaternion32Packing.Identity, record.PackedRotation);

            (float w, float x, float y, float z) = Quaternion32Packing.Decode(record.PackedRotation);
            AssertSameDirection(
                ShipHullMetrics.BowDirection,
                Rotate((w, x, y, z), ShipHullMetrics.BowDirection));
        }

        // =====================================================================
        // THE DIAGNOSTIC the player actually needs, since the fix is theirs.
        // =====================================================================

        /// <summary>
        /// The wide-hull advice names the symptom, the cause and the exact number of
        /// cells to extrude, and says nothing at all for a normal ship. This is the
        /// sentence both the spawn log and the man-the-helm log print, so it is
        /// pinned here rather than left to drift in two Console.WriteLines.
        /// </summary>
        [Fact]
        public void The_wide_hull_advice_names_the_fix_in_cells()
        {
            ShipHullMetrics wide = ShipHullMetrics.Measure(TwoCellStockHull());
            string? advice = wide.WideHullAdvice();

            Assert.NotNull(advice);
            Assert.Contains("BEAM EXCEEDS KEEL", advice!);
            Assert.Contains("SHIPYARD", advice);
            // 12 m beam / 4 m per cell = 3 cells needed; it has 2, so extrude 1 more.
            Assert.Equal(3, wide.CellsForKeelToMatchBeam);
            Assert.Equal(2, wide.CellsAlongKeel);
            Assert.Equal(1, wide.CellsToAddForKeelToMatchBeam);
            Assert.Contains("extrude 1 more cell(s)", advice);

            Assert.Null(ShipHullMetrics.Measure(FourCellStockHull()).WideHullAdvice());
        }

        /// <summary>
        /// A multi-deck hull must not be told to build fore-and-aft cells it already
        /// has: <see cref="ShipHullMetrics.CellsAlongKeel"/> counts distinct CELL
        /// NUMBERS, not plan entries, so stacking a second deck on a two-cell hull
        /// leaves the advice unchanged rather than pretending the ship got longer.
        /// </summary>
        [Fact]
        public void Stacking_a_second_deck_does_not_count_as_extra_length()
        {
            ShipPlanModel twoDecks = TwoCellStockHull();
            twoDecks.Cells.Add(new ShipCellModel(0, 1, ShipSectionModel.MakeDefault(), ShipSectionModel.MakeDefault()));
            twoDecks.Cells.Add(new ShipCellModel(1, 1, ShipSectionModel.MakeDefault(), null));

            ShipHullMetrics m = ShipHullMetrics.Measure(twoDecks);

            Assert.Equal(4, m.CellCount);
            Assert.Equal(2, m.CellsAlongKeel);
            Assert.Equal(2, m.DeckCount);
            Assert.Equal(8.0, m.KeelMetres, 4);
            Assert.Equal(1, m.CellsToAddForKeelToMatchBeam);
        }
    }
}
