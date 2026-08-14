using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>Pure capture/release rules for returning built ships to empty yards.</summary>
    public static class ShipyardDockingPolicy
    {
        public const double CaptureRadiusMetres = 9.0;
        public const double RearmRadiusMetres = 18.0;

        public static FixedPointPosition DockPose(FixedPointPosition shipyardPosition) =>
            BuiltShipPlacement.HullNextTo(shipyardPosition);

        public static bool OwnersMatch(string? hullOwner, string? yardOwner) =>
            !string.IsNullOrEmpty(hullOwner)
            && string.Equals(hullOwner, yardOwner, StringComparison.Ordinal);

        public static bool IsWithin(FixedPointPosition hullPosition,
            FixedPointPosition shipyardPosition, double radiusMetres)
        {
            FixedPointPosition target = DockPose(shipyardPosition);
            double dx = hullPosition.MetresX - target.MetresX;
            double dy = hullPosition.MetresY - target.MetresY;
            double dz = hullPosition.MetresZ - target.MetresZ;
            return dx * dx + dy * dy + dz * dz <= radiusMetres * radiusMetres;
        }

        public static bool CanDock(bool captureArmed, bool hullAtRest, bool inputNeutral,
            bool yardOccupied, string? hullOwner, string? yardOwner,
            FixedPointPosition hullPosition, FixedPointPosition shipyardPosition) =>
            captureArmed && hullAtRest && inputNeutral && !yardOccupied
            && OwnersMatch(hullOwner, yardOwner)
            && IsWithin(hullPosition, shipyardPosition, CaptureRadiusMetres);

        public static uint PackedYaw(double yawRadians)
        {
            double half = yawRadians * 0.5;
            return Placement.Quaternion32Packing.Encode(
                (float)Math.Cos(half), 0f, (float)Math.Sin(half), 0f);
        }

        public static double YawFromPacked(uint packed)
        {
            (float w, float x, float y, float z) = Placement.Quaternion32Packing.Decode(packed);
            return Math.Atan2(2.0 * ((w * y) + (x * z)),
                1.0 - (2.0 * ((x * x) + (y * y))));
        }
    }
}
