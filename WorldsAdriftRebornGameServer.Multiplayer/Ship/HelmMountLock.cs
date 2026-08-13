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
    /// WHY THE OFFSET DEFAULTS TO ZERO, AND WHY TUNING IT CANNOT FIX A "FORWARD"
    /// COMPLAINT. The offset exists because an early live report said the wheel
    /// was "90 degrees off"; it was set to 90 on that report alone. The orientation
    /// audit then settled every axis from the decompile and the shipped assets, and
    /// they all agree on +Z, with no rotation anywhere between them:
    /// <list type="bullet">
    ///   <item>THE EDITOR: <c>acs/ShipExtruderGizmo.MoveTo</c> drives
    ///     <c>ShipDir.Forward</c>/<c>Astern</c> along component index 2 (z) in steps
    ///     of 2, and <c>acs/ShipCell.GetMidPoint</c> maps Forward to +z. What the
    ///     player extrudes "forward" in the build UI becomes +Z.</item>
    ///   <item>THE HULL: <c>acs/ShipSection.GetVertexOffset</c> puts the section
    ///     index on z as <c>(sectionN - 0.5f) * 2f</c>; <c>acs/MeshGenerator</c>
    ///     places every piece at <c>pos * 2</c> with <c>Quaternion.identity</c>.</item>
    ///   <item>THE HELM PREFAB: <c>HelmFounder01_unityclient</c> (resources.assets)
    ///     has <c>#PilotPosition</c> at local z = -1.407 - the pilot stands BEHIND
    ///     the wheel - and its forward wind VfxNode at z = +37. Authored forward
    ///     is +Z.</item>
    ///   <item>THE FLIGHT: <c>FlightIntegrator</c> flies +Z at yaw 0 and a fresh
    ///     session starts at yaw 0 (identity rotation on the wire).</item>
    /// </list>
    /// So hull-local IDENTITY already points the wheel at the bow, and 0 is the
    /// correct default; a non-zero value only twists the wheel prop off the bow.
    ///
    /// CRITICALLY, THIS KNOB CANNOT CHANGE WHERE THE PILOT LOOKS.
    /// <c>acs/PilotCameraController</c> takes <c>vehicle.UnderlyingGameObject
    /// .transform.rotation</c> - the HULL's - as its forward, never the helm's, so
    /// the pilot's view runs down the hull's +Z whatever this offset is. A report
    /// that "the ship goes sideways" is therefore never fixable here: it means the
    /// hull's BEAM is longer than its KEEL (a stock cell is 12 m across by 4 m
    /// fore-aft, so a 2-cell ship is 12 x 8 and its bow is its SHORT axis) and the
    /// answer is a longer hull, not a rotation. See <see cref="ShipHullMetrics"/>,
    /// which logs those dimensions at spawn so this is visible without guessing.
    /// The knob stays only as insurance against a genuine future prefab surprise.
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
        /// The default yaw offset, degrees: ZERO. Hull-local identity already aims
        /// the Helm01 prefab's authored forward (+Z, evidenced in the type remarks)
        /// straight down the hull's bow (+Z), which is also where the pilot camera
        /// looks and where the ship flies at yaw 0. Zero packs to the identity
        /// SENTINEL, so a helm mounted under this default is byte-identical on the
        /// wire to an unrotated part. +90 would turn hull-local +Z toward +X (Unity
        /// yaw sense, clockwise from above) and put the wheel across the ship.
        /// </summary>
        public const double DefaultYawDegrees = 0.0;

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
