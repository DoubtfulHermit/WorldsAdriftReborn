using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// THE ALGEBRA UNDER CREATURE FACING, checked against rotations whose answers
    /// are known by hand.
    ///
    /// This exists because a wrong quaternion does not look wrong by a little. Get
    /// the component order, the handedness or the basis ordering wrong and the
    /// result is an axis PERMUTATION - the creature faces a completely different
    /// direction, which is precisely the symptom that was reported. So the facts
    /// below are deliberately about SPECIFIC rotations with arithmetic answers
    /// rather than about invariants that a permuted implementation would also
    /// satisfy.
    ///
    /// THE CONVENTION UNDER TEST IS UNITY'S, and it is RECOVERED rather than
    /// chosen: the client assigns what we send directly to
    /// <c>CachedTransform.rotation</c>, and retail's creature physics drove
    /// <c>transform.forward</c> as the heading and <c>transform.up</c> as the
    /// dorsal axis. Left-handed, +Z forward, +Y up, +X right.
    ///
    /// THE WIRE ROUND TRIP IS PART OF THE CONTRACT. A rotation that is correct in
    /// double precision and wrong after <c>Quaternion32Packing</c> has quantised it
    /// is still a creature facing the wrong way, so the encoder is exercised here
    /// too - including the trap that its component array is (w, x, y, z) with W
    /// FIRST.
    /// </summary>
    public sealed class IslandFaunaOrientationTests
    {
        private const double Tolerance = 1e-6;

        /// <summary>Quantisation floor of the ten-bit smallest-three wire form, in radians.</summary>
        private const double WireTolerance = 0.01;

        private static readonly (double X, double Y, double Z) Right = (1.0, 0.0, 0.0);
        private static readonly (double X, double Y, double Z) Up = (0.0, 1.0, 0.0);
        private static readonly (double X, double Y, double Z) Forward = (0.0, 0.0, 1.0);
        private static readonly (double X, double Y, double Z) Back = (0.0, 0.0, -1.0);
        private static readonly (double X, double Y, double Z) Left = (-1.0, 0.0, 0.0);

        // --- LookRotation against hand-computed answers

        [Fact]
        public void Looking_along_positive_z_with_world_up_is_the_identity()
        {
            FaunaRotation q = IslandFaunaOrientation.LookRotation(Forward, Up);
            Assert.Equal(1.0, q.W, 6);
            Assert.Equal(0.0, q.X, 6);
            Assert.Equal(0.0, q.Y, 6);
            Assert.Equal(0.0, q.Z, 6);
        }

        [Fact]
        public void Looking_along_positive_x_is_a_quarter_turn_about_up()
        {
            // Unity: Quaternion.Euler(0, 90, 0) == (w .7071, y .7071). A +Z-forward
            // model turned to face +X has turned RIGHT by 90 degrees.
            FaunaRotation q = IslandFaunaOrientation.LookRotation(Right, Up);
            Assert.Equal(0.70710678, q.W, 6);
            Assert.Equal(0.0, q.X, 6);
            Assert.Equal(0.70710678, q.Y, 6);
            Assert.Equal(0.0, q.Z, 6);
        }

        [Fact]
        public void Looking_backwards_is_a_half_turn_and_does_not_lose_precision()
        {
            // The trace-based branch degenerates as the trace approaches -1, which is
            // exactly a creature flying along -Z. This is the branch-coverage fact.
            FaunaRotation q = IslandFaunaOrientation.LookRotation(Back, Up);
            AssertDirection(Back, IslandFaunaOrientation.ForwardOf(q));
            AssertDirection(Up, IslandFaunaOrientation.UpOf(q));
            Assert.Equal(1.0, Length(q), 6);
        }

        [Theory]
        [InlineData(1.0, 0.0, 0.0)]
        [InlineData(-1.0, 0.0, 0.0)]
        [InlineData(0.0, 0.0, -1.0)]
        [InlineData(0.6, 0.0, 0.8)]
        [InlineData(-0.6, 0.0, -0.8)]
        [InlineData(0.3, 0.5, -0.81)]
        public void A_rotation_actually_points_the_nose_where_it_was_asked_to(
            double x, double y, double z)
        {
            (double X, double Y, double Z) forward = (x, y, z);
            FaunaRotation q = IslandFaunaOrientation.LookRotation(forward, Up);

            AssertDirection(forward, IslandFaunaOrientation.ForwardOf(q));
            Assert.Equal(1.0, Length(q), 6);

            // And the back stays as near world up as the heading allows: the up
            // component perpendicular to forward must be positive, never inverted.
            Assert.True(Dot(IslandFaunaOrientation.UpOf(q), Up) > 0.0,
                "a creature must not be rolled upside down by a look rotation");
        }

        [Fact]
        public void A_requested_up_is_honoured_so_banking_survives()
        {
            // If LookRotation quietly ignored the up vector, every bank would be
            // discarded and mantas would fly flat - a silent failure that a
            // forward-only assertion would not catch.
            (double X, double Y, double Z) banked =
                IslandFaunaOrientation.BankedUp(Forward, 0.5);
            FaunaRotation q = IslandFaunaOrientation.LookRotation(Forward, banked);

            AssertDirection(Forward, IslandFaunaOrientation.ForwardOf(q));
            AssertDirection(banked, IslandFaunaOrientation.UpOf(q));
        }

        [Fact]
        public void Degenerate_input_falls_back_to_identity_instead_of_producing_a_nan()
        {
            // A NaN quaternion encodes to the identity SENTINEL and silently
            // un-rotates the creature, which is the exact bug being fixed - so the
            // degenerate cases are handled where they can be seen.
            Assert.Equal(FaunaRotation.Identity,
                IslandFaunaOrientation.LookRotation((0.0, 0.0, 0.0), Up));

            // Up parallel to forward: the cross product collapses and the basis must
            // be rebuilt from another axis rather than dividing by zero.
            FaunaRotation straightUp = IslandFaunaOrientation.LookRotation(Up, Up);
            Assert.Equal(1.0, Length(straightUp), 6);
            AssertDirection(Up, IslandFaunaOrientation.ForwardOf(straightUp));

            FaunaRotation straightDown = IslandFaunaOrientation.LookRotation((0.0, -1.0, 0.0), Up);
            Assert.Equal(1.0, Length(straightDown), 6);
            AssertDirection((0.0, -1.0, 0.0), IslandFaunaOrientation.ForwardOf(straightDown));
        }

        // --- Sign conventions, which decide which way a creature banks

        [Fact]
        public void Turning_from_forward_toward_right_is_a_positive_yaw()
        {
            // POSITIVE IS A RIGHT TURN. Everything about banking hangs off this sign;
            // if it inverts, every manta banks out of its turn instead of into it.
            Assert.True(IslandFaunaOrientation.SignedYawBetween(Forward, Right) > 0.0);
            Assert.True(IslandFaunaOrientation.SignedYawBetween(Forward, Left) < 0.0);
            Assert.Equal(Math.PI / 2.0,
                IslandFaunaOrientation.SignedYawBetween(Forward, Right), 6);
            Assert.Equal(0.0, IslandFaunaOrientation.SignedYawBetween(Forward, Forward), 6);

            // Vertical components are ignored: a climbing creature is not turning.
            Assert.Equal(0.0,
                IslandFaunaOrientation.SignedYawBetween(Forward, (0.0, 5.0, 1.0)), 6);
            Assert.Equal(0.0, IslandFaunaOrientation.SignedYawBetween(Up, Up), 6);
        }

        [Fact]
        public void Yawing_by_a_quarter_turn_takes_forward_to_right()
        {
            AssertDirection(Right, IslandFaunaOrientation.YawBy(Forward, Math.PI / 2.0));
            AssertDirection(Left, IslandFaunaOrientation.YawBy(Forward, -Math.PI / 2.0));
            AssertDirection(Back, IslandFaunaOrientation.YawBy(Forward, Math.PI));

            // Consistent with SignedYawBetween, which is what makes the bank sign and
            // the jitter sign mean the same thing.
            Assert.Equal(0.4,
                IslandFaunaOrientation.SignedYawBetween(
                    Forward, IslandFaunaOrientation.YawBy(Forward, 0.4)), 6);
        }

        [Fact]
        public void Banking_tilts_the_back_into_the_turn()
        {
            // RECOVERED SHAPE: retail slerped world up toward Cross(up, forward),
            // the horizontal RIGHT vector, by an amount proportional to yaw effort.
            (double X, double Y, double Z) banked =
                IslandFaunaOrientation.BankedUp(Forward, 0.3);

            Assert.True(Dot(banked, Right) > 0.0,
                "a right-hand turn must tilt the back toward the creature's right");
            Assert.Equal(0.3, IslandFaunaOrientation.AngleBetween(banked, Up), 6);

            // And the other way, which retail's clamped Slerp could not do.
            (double X, double Y, double Z) other =
                IslandFaunaOrientation.BankedUp(Forward, -0.3);
            Assert.True(Dot(other, Right) < 0.0,
                "a left-hand turn must bank the other way, not stay flat");
            Assert.Equal(0.3, IslandFaunaOrientation.AngleBetween(other, Up), 6);

            // No turn, no bank.
            AssertDirection(Up, IslandFaunaOrientation.BankedUp(Forward, 0.0));

            // Straight up has no horizontal right vector to bank about; it must not
            // divide by zero.
            AssertDirection(Up, IslandFaunaOrientation.BankedUp(Up, 0.5));
        }

        // --- The wire, which is where a correct rotation can still be lost

        [Theory]
        [InlineData(0.0, 0.0, 1.0)]
        [InlineData(1.0, 0.0, 0.0)]
        [InlineData(-1.0, 0.0, 0.0)]
        [InlineData(0.0, 0.0, -1.0)]
        [InlineData(0.7, 0.0, 0.7)]
        [InlineData(-0.35, 0.1, 0.93)]
        public void A_facing_survives_the_thirty_two_bit_wire_form(
            double x, double y, double z)
        {
            (double X, double Y, double Z) forward = (x, y, z);
            FaunaRotation q = IslandFaunaOrientation.LookRotation(
                forward, IslandFaunaOrientation.BankedUp(forward, 0.2));

            // W FIRST - the same order Quaternion32Util builds its component array
            // in. Passing (x, y, z, w) here would compile and would permute the axes.
            uint packed = Quaternion32Packing.Encode(q.W, q.X, q.Y, q.Z);
            Assert.NotEqual(Quaternion32Packing.Identity, packed);

            (float w2, float x2, float y2, float z2) = Quaternion32Packing.Decode(packed);
            FaunaRotation round = new FaunaRotation(w2, x2, y2, z2);

            Assert.True(IslandFaunaOrientation.AngleBetween(
                    forward, IslandFaunaOrientation.ForwardOf(round)) < WireTolerance,
                "the heading did not survive encoding: wanted " + Describe(forward)
                    + " got " + Describe(IslandFaunaOrientation.ForwardOf(round)));
        }

        [Fact]
        public void The_identity_still_encodes_to_the_clients_own_sentinel()
        {
            // Not decoration: 1023 is a magic value the client's decoder
            // special-cases, and a naive all-zero encoding decodes to NaN.
            Assert.Equal(Quaternion32Packing.Identity,
                Quaternion32Packing.Encode(
                    FaunaRotation.Identity.W, FaunaRotation.Identity.X,
                    FaunaRotation.Identity.Y, FaunaRotation.Identity.Z));
        }

        private static void AssertDirection(
            (double X, double Y, double Z) expected, (double X, double Y, double Z) actual)
        {
            Assert.True(IslandFaunaOrientation.AngleBetween(expected, actual) < 1e-5,
                "expected " + Describe(expected) + " got " + Describe(actual));
        }

        private static string Describe((double X, double Y, double Z) v) =>
            "(" + v.X.ToString("0.###") + ", " + v.Y.ToString("0.###")
                + ", " + v.Z.ToString("0.###") + ")";

        private static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
            (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

        private static double Length(FaunaRotation q) =>
            Math.Sqrt((q.W * q.W) + (q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z));
    }
}
