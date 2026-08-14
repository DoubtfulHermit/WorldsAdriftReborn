using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    public enum ShipSalvageReject
    {
        Accept,
        NotOwnedPlayer,
        NotShipyardOwner,
        NoDockedShip,
        HullNotBuilt,
        DockMismatch,
        HullPiloted,
    }

    /// <summary>Pure validation and transform math for shipyard frame salvage.</summary>
    public static class ShipSalvagePolicy
    {
        public static ShipSalvageReject Evaluate(bool ownsPlayerEntity, string requesterUid,
            string shipyardOwnerUid, long dockedHullId, bool hullIsBuilt, long hullDockedAtYard,
            long requestedShipyardId)
        {
            if (!ownsPlayerEntity) return ShipSalvageReject.NotOwnedPlayer;
            if (string.IsNullOrEmpty(requesterUid)
                || !string.Equals(requesterUid, shipyardOwnerUid, StringComparison.Ordinal))
                return ShipSalvageReject.NotShipyardOwner;
            if (dockedHullId <= 0) return ShipSalvageReject.NoDockedShip;
            if (!hullIsBuilt) return ShipSalvageReject.HullNotBuilt;
            if (hullDockedAtYard != requestedShipyardId) return ShipSalvageReject.DockMismatch;
            return ShipSalvageReject.Accept;
        }

        /// <summary>Turns a hull-local part pose into a parentless world pose.</summary>
        public static (FixedPointPosition Position, uint PackedRotation) DropPose(
            FixedPointPosition hullPosition, uint hullPackedRotation,
            FixedPointPosition localPosition, uint localPackedRotation)
        {
            (float hw, float hx, float hy, float hz) = Placement.Quaternion32Packing.Decode(hullPackedRotation);
            (double rx, double ry, double rz) = Rotate(hw, hx, hy, hz,
                localPosition.MetresX, localPosition.MetresY, localPosition.MetresZ);

            (float lw, float lx, float ly, float lz) = Placement.Quaternion32Packing.Decode(localPackedRotation);
            // World rotation = hull * local.
            float w = (hw * lw) - (hx * lx) - (hy * ly) - (hz * lz);
            float x = (hw * lx) + (hx * lw) + (hy * lz) - (hz * ly);
            float y = (hw * ly) - (hx * lz) + (hy * lw) + (hz * lx);
            float z = (hw * lz) + (hx * ly) - (hy * lx) + (hz * lw);

            return (
                FixedPointPosition.FromMetres(
                    hullPosition.MetresX + rx,
                    hullPosition.MetresY + ry,
                    hullPosition.MetresZ + rz),
                Placement.Quaternion32Packing.Encode(w, x, y, z));
        }

        private static (double X, double Y, double Z) Rotate(
            double w, double x, double y, double z, double vx, double vy, double vz)
        {
            // q*v*q^-1, expanded to avoid an engine dependency.
            double tx = 2.0 * ((y * vz) - (z * vy));
            double ty = 2.0 * ((z * vx) - (x * vz));
            double tz = 2.0 * ((x * vy) - (y * vx));
            return (
                vx + (w * tx) + (y * tz) - (z * ty),
                vy + (w * ty) + (z * tx) - (x * tz),
                vz + (w * tz) + (x * ty) - (y * tx));
        }
    }
}
