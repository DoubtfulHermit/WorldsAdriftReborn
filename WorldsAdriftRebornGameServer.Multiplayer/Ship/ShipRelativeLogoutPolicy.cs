using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// A server-authored, durable anchor for a player who logged out aboard a
    /// built ship. The ship index survives reboot; the hull-local point follows
    /// the restored hull pose. Absolute XYZ is stored separately as fallback.
    /// </summary>
    public readonly record struct ShipLogoutAnchor(
        int BuiltShipIndex, FixedPointPosition LocalPosition);

    public static class ShipRelativeLogoutPolicy
    {
        public const double MaxLocalDistanceMetres = 256.0;

        public static ShipLogoutAnchor? Capture(int? builtShipIndex,
            FixedPointPosition playerWorld, FixedPointPosition hullWorld,
            uint hullPackedRotation)
        {
            if (!builtShipIndex.HasValue || builtShipIndex.Value < 0) return null;

            (float w, float x, float y, float z) = Placement.Quaternion32Packing.Decode(hullPackedRotation);
            double dx = playerWorld.MetresX - hullWorld.MetresX;
            double dy = playerWorld.MetresY - hullWorld.MetresY;
            double dz = playerWorld.MetresZ - hullWorld.MetresZ;
            (double lx, double ly, double lz) = Rotate(w, -x, -y, -z, dx, dy, dz);
            if (!Finite(lx) || !Finite(ly) || !Finite(lz)
                || Math.Abs(lx) > MaxLocalDistanceMetres
                || Math.Abs(ly) > MaxLocalDistanceMetres
                || Math.Abs(lz) > MaxLocalDistanceMetres)
            {
                return null;
            }

            return new ShipLogoutAnchor(builtShipIndex.Value,
                FixedPointPosition.FromMetres(lx, ly, lz));
        }

        public static FixedPointPosition? Resolve(ShipLogoutAnchor anchor,
            FixedPointPosition hullWorld, uint hullPackedRotation)
        {
            FixedPointPosition local = anchor.LocalPosition;
            if (anchor.BuiltShipIndex < 0
                || Math.Abs(local.MetresX) > MaxLocalDistanceMetres
                || Math.Abs(local.MetresY) > MaxLocalDistanceMetres
                || Math.Abs(local.MetresZ) > MaxLocalDistanceMetres)
            {
                return null;
            }

            return ShipSalvagePolicy.DropPose(hullWorld, hullPackedRotation, local,
                Placement.Quaternion32Packing.Identity).Position;
        }

        private static (double X, double Y, double Z) Rotate(
            double w, double x, double y, double z, double vx, double vy, double vz)
        {
            double tx = 2.0 * ((y * vz) - (z * vy));
            double ty = 2.0 * ((z * vx) - (x * vz));
            double tz = 2.0 * ((x * vy) - (y * vx));
            return (
                vx + (w * tx) + (y * tz) - (z * ty),
                vy + (w * ty) + (z * tx) - (x * tz),
                vz + (w * tz) + (x * ty) - (y * tx));
        }

        private static bool Finite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
