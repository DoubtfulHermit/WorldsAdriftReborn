using System;
using System.Globalization;
using WorldsAdriftRebornGameServer.Multiplayer.Placement;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// The HELM's mount-rotation lock: hull-local identity COMPOSED with a fixed
    /// yaw offset about +Y, in degrees, from <c>WAREBORN_HELM_MOUNT_YAW</c>.
    ///
    /// WHY A LOCK AT ALL: retail helms always face the bow - the Helm01 prefab
    /// blocks both placement-rotation modes (ShipHelmPlacement.Awake, decompile)
    /// and the pilot camera aligns to the SHIP's rotation on man - so the helm's
    /// hull-local rotation is server policy, never the placing player's facing
    /// (see PartMountService.Commit, the isHelmMount branch).
    ///
    /// WHY AN OFFSET ON THE IDENTITY: the first live lock used raw identity and
    /// the wheel came out 90 degrees off on the built ship ("the rotation of the
    /// helm was switched... should be 90 change"). The static evidence is
    /// genuinely split - the Helm01 prefab's authored forward is +Z (its
    /// #PilotPosition child sits at z = -1.41, the pilot stands BEHIND the wheel),
    /// the hull plan's fore-aft axis is +Z too (ShipSection.GetVertexOffset maps
    /// sectionN onto Z), and the flight integrator flies +Z at yaw 0 - yet the
    /// live ship reads 90 degrees rotated, because a built hull is WIDER than it
    /// is long (a cell is 12 m port-starboard by 4 m fore-aft) and the long beam
    /// run is what a pilot reads as the keel. So the offset is a KNOB, defaulting
    /// to the live-reported 90, and flipping the sign (or zeroing it) is an env
    /// edit plus restart, never a rebuild.
    ///
    /// One module so the three commit sites (the 190602 packed rotation, the 1120
    /// SetAttachRot/SetLastAttachment, the MountedParts.Register/persistence
    /// packed value) can never disagree about what "locked" means.
    /// </summary>
    public static class HelmMountLock
    {
        /// <summary>The env knob: the helm lock's yaw offset in DEGREES about +Y.</summary>
        public const string YawEnvVar = "WAREBORN_HELM_MOUNT_YAW";

        /// <summary>
        /// The default yaw offset, degrees. 90: the live report after the identity
        /// lock said the wheel was 90 degrees off; +90 turns hull-local +Z toward
        /// hull-local +X (Unity yaw sense, clockwise seen from above). If the live
        /// wheel faces the wrong END of the beam run, set the env var to -90 - and
        /// re-run the saved-helm one-liner with the matching packed value.
        /// </summary>
        public const double DefaultYawDegrees = 90.0;

        /// <summary>
        /// The yaw offset currently in force: <see cref="YawEnvVar"/> parsed
        /// invariant-culture, else <see cref="DefaultYawDegrees"/>. Read per call,
        /// like WorldEntities.ShipFramePosition() - cheap, and the value is fixed
        /// for a server process anyway.
        /// </summary>
        public static double YawDegrees()
        {
            return ParseYawDegrees(Environment.GetEnvironmentVariable(YawEnvVar));
        }

        /// <summary>
        /// The pure parse: invariant-culture double, non-finite or malformed input
        /// falls back to the default so a typo in a unit file can never NaN a
        /// quaternion onto the wire (Encode would catch it, but the fallback keeps
        /// the DEFAULT facing rather than identity).
        /// </summary>
        public static double ParseYawDegrees(string? raw)
        {
            if (!string.IsNullOrWhiteSpace(raw)
                && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                && !double.IsNaN(parsed) && !double.IsInfinity(parsed))
            {
                return parsed;
            }
            return DefaultYawDegrees;
        }

        /// <summary>
        /// The Hamilton product a ∘ b, (w,x,y,z) component order - "apply b, then
        /// a". Public so the tests assert the identity-composition law directly.
        /// </summary>
        public static (float W, float X, float Y, float Z) Compose(
            (float W, float X, float Y, float Z) a, (float W, float X, float Y, float Z) b)
        {
            return (
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
                a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
                a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);
        }

        /// <summary>A pure yaw about +Y, degrees to (w,x,y,z).</summary>
        public static (float W, float X, float Y, float Z) YawQuaternion(double degrees)
        {
            double half = degrees * Math.PI / 360.0;
            return ((float)Math.Cos(half), 0f, (float)Math.Sin(half), 0f);
        }

        /// <summary>
        /// THE lock rotation, hull-local: identity composed with the yaw offset.
        /// The composition is written out (not just "return the yaw") so the
        /// stated semantics - identity is the base, the knob is an offset ON it -
        /// are the code, and the tests pin identity ∘ q == q.
        /// </summary>
        public static (float W, float X, float Y, float Z) LockRotation(double yawDegrees)
        {
            return Compose((1f, 0f, 0f, 0f), YawQuaternion(yawDegrees));
        }

        /// <summary>
        /// The lock rotation in the game's packed Quaternion32 wire form - the
        /// value the 190602 update, the mount ledger and the persistence record
        /// all carry. 0 degrees packs to the identity SENTINEL 1023 by
        /// construction (Encode special-cases |w| == 1), so a zeroed knob is
        /// byte-identical to the old raw-identity lock.
        /// </summary>
        public static uint PackedLockRotation(double yawDegrees)
        {
            (float w, float x, float y, float z) = LockRotation(yawDegrees);
            return Quaternion32Packing.Encode(w, x, y, z);
        }
    }
}
