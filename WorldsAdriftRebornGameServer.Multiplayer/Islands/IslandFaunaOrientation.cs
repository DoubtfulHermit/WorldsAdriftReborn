namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// A rotation as a unit quaternion, in the component order the wire uses.
    ///
    /// W FIRST, matching <c>Quaternion32Packing.Encode(w, x, y, z)</c> and the
    /// client's own <c>Quaternion32Util</c>, whose smallest-three encoder builds
    /// <c>new float[4] { rawQuaternion.w, rawQuaternion.x, rawQuaternion.y, rawQuaternion.z }</c>
    /// before choosing which component to drop. Getting that order wrong does not
    /// produce a slightly-off rotation, it produces an axis PERMUTATION - which on
    /// screen looks exactly like "the creature is facing the wrong way", so the
    /// order is stated here rather than left to a call site to remember.
    /// </summary>
    public readonly record struct FaunaRotation(float W, float X, float Y, float Z)
    {
        /// <summary>No rotation. The client has a dedicated sentinel for this; see <c>Quaternion32Packing.Identity</c>.</summary>
        public static FaunaRotation Identity => new FaunaRotation(1f, 0f, 0f, 0f);
    }

    /// <summary>
    /// QUATERNION MATHS FOR CREATURE FACING, with no Unity and no engine.
    ///
    /// Split out from <see cref="IslandFaunaMovement"/> because it is general
    /// rotation algebra with no fauna in it: the movement module decides WHERE a
    /// creature looks, this decides how to say so. That keeps the movement module
    /// about geometry and lets the algebra be tested against known rotations
    /// instead of against a manta.
    ///
    /// THE CONVENTION IS UNITY'S, and that is a RECOVERED requirement rather than
    /// a preference. The client applies what we send straight to the transform -
    /// <c>LerpLocalTransformBehaviour.SetRotation</c> does
    /// <c>CachedTransform.rotation = newRotation.ToUnityQuaternion()</c> - and
    /// retail's own creature physics drove <c>transform.forward</c> as the heading
    /// (<c>RigidbodyX.CalculateTorqueForTargetHeading</c> crosses against
    /// <c>rigidbody.transform.forward</c>) and <c>transform.up</c> as the dorsal
    /// axis (<c>CalculateTorqueForTargetUp</c> crosses against
    /// <c>transform.up</c>). So a creature model is authored nose-along-+Z,
    /// back-along-+Y, and no per-species correction quaternion exists anywhere in
    /// the decompiled client - searched for and NOT found, which is the useful
    /// result. <see cref="LookRotation"/> is therefore all that is needed.
    ///
    /// Left-handed, +Z forward, +Y up, +X right, exactly as
    /// <c>UnityEngine.Quaternion.LookRotation(forward, up)</c> builds it.
    /// </summary>
    public static class IslandFaunaOrientation
    {
        /// <summary>Below this squared length a direction vector is treated as absent rather than normalised into noise.</summary>
        private const double MinimumDirectionSquared = 1e-12;

        /// <summary>
        /// The rotation that points +Z along <paramref name="forward"/> and puts +Y
        /// as near <paramref name="up"/> as that allows - Unity's
        /// <c>Quaternion.LookRotation</c>, reimplemented.
        ///
        /// TOTAL: a degenerate forward, a degenerate up, or an up parallel to
        /// forward all fall back rather than producing NaN. A NaN quaternion would
        /// be encoded as the identity sentinel and silently un-rotate the creature,
        /// which is the bug this whole file exists to fix - so the degenerate cases
        /// are handled here where they can be tested, not left to the packer.
        /// </summary>
        public static FaunaRotation LookRotation(
            (double X, double Y, double Z) forward, (double X, double Y, double Z) up)
        {
            if (!TryNormalise(forward, out (double X, double Y, double Z) z))
            {
                return FaunaRotation.Identity;
            }

            // x = up cross z, y = z cross x. If up is parallel to forward the cross
            // collapses, so pick any axis not parallel to z and rebuild from that.
            if (!TryNormalise(Cross(up, z), out (double X, double Y, double Z) x))
            {
                (double X, double Y, double Z) fallback =
                    Math.Abs(z.Y) < 0.9 ? (0.0, 1.0, 0.0) : (1.0, 0.0, 0.0);
                if (!TryNormalise(Cross(fallback, z), out x))
                {
                    return FaunaRotation.Identity;
                }
            }

            (double X, double Y, double Z) y = Cross(z, x);

            // The basis as a rotation matrix, columns (x, y, z), then the standard
            // matrix-to-quaternion with all four branches: the trace form loses
            // precision as the trace approaches -1, and a creature flying along -Z
            // is exactly that case.
            double m00 = x.X, m01 = y.X, m02 = z.X;
            double m10 = x.Y, m11 = y.Y, m12 = z.Y;
            double m20 = x.Z, m21 = y.Z, m22 = z.Z;
            double trace = m00 + m11 + m22;

            double qw, qx, qy, qz;
            if (trace > 0.0)
            {
                double s = Math.Sqrt(trace + 1.0) * 2.0;
                qw = 0.25 * s;
                qx = (m21 - m12) / s;
                qy = (m02 - m20) / s;
                qz = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                double s = Math.Sqrt(1.0 + m00 - m11 - m22) * 2.0;
                qw = (m21 - m12) / s;
                qx = 0.25 * s;
                qy = (m01 + m10) / s;
                qz = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                double s = Math.Sqrt(1.0 + m11 - m00 - m22) * 2.0;
                qw = (m02 - m20) / s;
                qx = (m01 + m10) / s;
                qy = 0.25 * s;
                qz = (m12 + m21) / s;
            }
            else
            {
                double s = Math.Sqrt(1.0 + m22 - m00 - m11) * 2.0;
                qw = (m10 - m01) / s;
                qx = (m02 + m20) / s;
                qy = (m12 + m21) / s;
                qz = 0.25 * s;
            }

            return Normalise(qw, qx, qy, qz);
        }

        /// <summary>
        /// The signed yaw from <paramref name="from"/> to <paramref name="to"/> about
        /// world up, in radians, in (-pi, pi].
        ///
        /// POSITIVE IS A RIGHT TURN in Unity's left-handed frame: rotating +Z toward
        /// +X gives a cross of +Y and therefore a positive result. That sign is what
        /// decides which way a manta banks, so it is pinned by a test rather than
        /// assumed.
        /// </summary>
        public static double SignedYawBetween(
            (double X, double Y, double Z) from, (double X, double Y, double Z) to)
        {
            if (!TryNormalise(Flatten(from), out (double X, double Y, double Z) a)
                || !TryNormalise(Flatten(to), out (double X, double Y, double Z) b))
            {
                return 0.0;
            }

            (double X, double Y, double Z) cross = Cross(a, b);
            return Math.Atan2(cross.Y, Dot(a, b));
        }

        /// <summary>
        /// World up tilted by <paramref name="bankRadians"/> toward the creature's
        /// own right - a BANK INTO THE TURN.
        ///
        /// RECOVERED SHAPE from <c>MovementController.UpdateAngle</c>:
        /// <c>_upDirection = Vector3.Slerp(Vector3.up, Vector3.Cross(Vector3.up, transform.forward) * Mathf.Sign(torque.y), torque.y * turnBankingScale)</c>.
        /// <c>Cross(up, forward)</c> is the horizontal RIGHT vector, and tilting the
        /// up vector toward the inside of the turn is what makes the lift point at
        /// the turn centre - ordinary aircraft banking, and very visible on a body
        /// as broad and flat as a manta.
        ///
        /// A DELIBERATE DEPARTURE FROM RETAIL, stated because it is a real
        /// difference rather than an approximation. Retail's <c>Vector3.Slerp</c>
        /// CLAMPS its interpolant to [0,1], so a LEFT turn (negative yaw torque)
        /// produced t &lt; 0, clamped to 0, and no bank at all - the
        /// <c>Mathf.Sign</c> on the target vector is dead code in that branch.
        /// Retail mantas therefore only banked on right-hand turns. Reproducing that
        /// would be visibly wrong here for a reason retail did not have: an island
        /// picks its patrol direction with
        /// <c>counterClockwise = UnityEngine.Random.value > 0.5f</c>, so replicating
        /// the clamp would leave HALF of all islands with mantas that never bank at
        /// all, and the player would be looking at the flat ones. This banks both
        /// ways.
        /// </summary>
        public static (double X, double Y, double Z) BankedUp(
            (double X, double Y, double Z) forward, double bankRadians)
        {
            if (!TryNormalise(Cross((0.0, 1.0, 0.0), forward),
                    out (double X, double Y, double Z) right))
            {
                // Straight up or straight down: there is no horizontal right vector,
                // so there is nothing to bank about.
                return (0.0, 1.0, 0.0);
            }

            double cos = Math.Cos(bankRadians);
            double sin = Math.Sin(bankRadians);
            return (cos * 0.0 + sin * right.X,
                    cos * 1.0 + sin * right.Y,
                    cos * 0.0 + sin * right.Z);
        }

        /// <summary>
        /// <paramref name="direction"/> rotated by <paramref name="radians"/> about
        /// world up. Used for the small per-member heading jitter that keeps a
        /// school from flying as a rigid rank.
        /// </summary>
        public static (double X, double Y, double Z) YawBy(
            (double X, double Y, double Z) direction, double radians)
        {
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);
            // Left-handed rotation about +Y: +Z turns toward +X for a positive angle,
            // which is the same "positive is a right turn" sign SignedYawBetween uses.
            return ((cos * direction.X) + (sin * direction.Z),
                    direction.Y,
                    (cos * direction.Z) - (sin * direction.X));
        }

        /// <summary>The vector with its vertical component removed, for a heading that must stay level.</summary>
        public static (double X, double Y, double Z) Flatten(
            (double X, double Y, double Z) vector) => (vector.X, 0.0, vector.Z);

        /// <summary>
        /// <paramref name="vector"/> rotated by <paramref name="rotation"/>.
        ///
        /// The inverse direction of <see cref="LookRotation"/>, and the only way to
        /// ask a quaternion a question a test can check: "which way is this creature
        /// actually facing" is <c>Rotate(q, +Z)</c>, and "which way is its back" is
        /// <c>Rotate(q, +Y)</c>. Without it every orientation assertion would have to
        /// compare raw quaternion components, which is unreadable and passes happily
        /// for a rotation that is off by a whole axis.
        /// </summary>
        public static (double X, double Y, double Z) Rotate(
            FaunaRotation rotation, (double X, double Y, double Z) vector)
        {
            double w = rotation.W, x = rotation.X, y = rotation.Y, z = rotation.Z;

            // v' = v + 2w(q x v) + 2(q x (q x v)), with q the vector part.
            (double X, double Y, double Z) q = (x, y, z);
            (double X, double Y, double Z) t = Cross(q, vector);
            t = (2.0 * t.X, 2.0 * t.Y, 2.0 * t.Z);
            (double X, double Y, double Z) qt = Cross(q, t);
            return (vector.X + (w * t.X) + qt.X,
                    vector.Y + (w * t.Y) + qt.Y,
                    vector.Z + (w * t.Z) + qt.Z);
        }

        /// <summary>Where a creature with this rotation points its nose: +Z rotated.</summary>
        public static (double X, double Y, double Z) ForwardOf(FaunaRotation rotation) =>
            Rotate(rotation, (0.0, 0.0, 1.0));

        /// <summary>Where a creature with this rotation points its back: +Y rotated.</summary>
        public static (double X, double Y, double Z) UpOf(FaunaRotation rotation) =>
            Rotate(rotation, (0.0, 1.0, 0.0));

        /// <summary>The angle between two directions, in radians. Total for degenerate input.</summary>
        public static double AngleBetween(
            (double X, double Y, double Z) a, (double X, double Y, double Z) b)
        {
            if (!TryNormalise(a, out (double X, double Y, double Z) na)
                || !TryNormalise(b, out (double X, double Y, double Z) nb))
            {
                return 0.0;
            }
            return Math.Acos(Math.Clamp(Dot(na, nb), -1.0, 1.0));
        }

        private static (double X, double Y, double Z) Cross(
            (double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
            ((a.Y * b.Z) - (a.Z * b.Y),
             (a.Z * b.X) - (a.X * b.Z),
             (a.X * b.Y) - (a.Y * b.X));

        private static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
            (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

        private static bool TryNormalise(
            (double X, double Y, double Z) vector, out (double X, double Y, double Z) result)
        {
            double squared = Dot(vector, vector);
            if (squared < MinimumDirectionSquared || double.IsNaN(squared) || double.IsInfinity(squared))
            {
                result = (0.0, 0.0, 0.0);
                return false;
            }
            double length = Math.Sqrt(squared);
            result = (vector.X / length, vector.Y / length, vector.Z / length);
            return true;
        }

        private static FaunaRotation Normalise(double w, double x, double y, double z)
        {
            double squared = (w * w) + (x * x) + (y * y) + (z * z);
            if (squared <= 0.0 || double.IsNaN(squared) || double.IsInfinity(squared))
            {
                return FaunaRotation.Identity;
            }
            double length = Math.Sqrt(squared);
            return new FaunaRotation(
                (float)(w / length), (float)(x / length),
                (float)(y / length), (float)(z / length));
        }
    }
}
