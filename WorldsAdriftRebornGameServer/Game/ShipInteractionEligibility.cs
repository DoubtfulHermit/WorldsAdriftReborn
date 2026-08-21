using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using WorldsAdriftRebornGameServer.DLLCommunication;
using WorldsAdriftRebornGameServer.Networking.Singleton;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// Resolves the established interest position and the mounted part's current
    /// hull-relative world position before applying the pure interaction policy.
    /// This deliberately uses the flight session pose when one exists: immutable
    /// world-entity seeds still point at a ship's build location after it has flown.
    /// </summary>
    internal static class ShipInteractionEligibility
    {
        internal static bool Allows(ENetPeerHandle peer, long targetEntityId,
            Crafting.MountedParts.Mount mount, bool ownsPlayer, double radiusMetres,
            out double distanceMetres)
        {
            ulong peerId = PeerIdentity.IdOf(peer);
            bool positionKnown = WorldsAdriftRebornGameServer.ResourceInterest
                .TryCenterFor(peerId, out FixedPointPosition playerPosition);
            bool checkedOut = WorldsAdriftRebornGameServer.SentEntities
                .WasSent(peer, targetEntityId);
            FixedPointPosition targetPosition = MountedPartWorldPosition(mount);

            distanceMetres = positionKnown
                ? System.Math.Sqrt(ResourceInterestPolicy.DistanceSquared(
                    playerPosition, targetPosition))
                : double.PositiveInfinity;

            return ShipInteractionEligibilityPolicy.Allows(
                ownsPlayer, checkedOut, positionKnown, playerPosition,
                targetPosition, radiusMetres);
        }

        private static FixedPointPosition MountedPartWorldPosition(
            Crafting.MountedParts.Mount mount)
        {
            Multiplayer.Ship.Domains.ShipDomain? domain =
                WorldsAdriftRebornGameServer.ShipDomains.ByHull(mount.HullEntityId);
            FixedPointPosition hullPosition;
            double yaw;
            if (domain != null)
            {
                Multiplayer.Ship.Flight.FlightState state = domain.Flight.State;
                hullPosition = FixedPointPosition.FromMetres(state.X, state.Y, state.Z);
                yaw = state.YawRadians;
            }
            else
            {
                hullPosition = WorldsAdriftRebornGameServer.WorldEntities
                    .TransformSeedFor(mount.HullEntityId);
                yaw = ShipyardDockingPolicy.YawFromPacked(
                    WorldsAdriftRebornGameServer.WorldEntities
                        .RotationSeedFor(mount.HullEntityId));
            }

            double localX = mount.LocalOffset.MetresX;
            double localY = mount.LocalOffset.MetresY;
            double localZ = mount.LocalOffset.MetresZ;
            double sin = System.Math.Sin(yaw);
            double cos = System.Math.Cos(yaw);
            return FixedPointPosition.FromMetres(
                hullPosition.MetresX + (localX * cos) + (localZ * sin),
                hullPosition.MetresY + localY,
                hullPosition.MetresZ - (localX * sin) + (localZ * cos));
        }
    }
}
