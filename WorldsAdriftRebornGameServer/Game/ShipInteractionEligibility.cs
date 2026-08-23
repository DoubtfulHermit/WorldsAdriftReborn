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
            FixedPointPosition targetPosition = MountedPartWorldPose(mount).Position;

            distanceMetres = positionKnown
                ? System.Math.Sqrt(ResourceInterestPolicy.DistanceSquared(
                    playerPosition, targetPosition))
                : double.PositiveInfinity;

            return ShipInteractionEligibilityPolicy.Allows(
                ownsPlayer, checkedOut, positionKnown, playerPosition,
                targetPosition, radiusMetres);
        }

        internal static (FixedPointPosition Position, uint PackedRotation) MountedPartWorldPose(
            Crafting.MountedParts.Mount mount)
        {
            (FixedPointPosition hullPosition, uint hullRotation) = HullWorldPose(mount.HullEntityId);
            return Multiplayer.Ship.ShipSalvagePolicy.DropPose(
                hullPosition, hullRotation, mount.LocalOffset, mount.PackedRotation);
        }

        /// <summary>
        /// Resolves the exact transform used by the retail client's grounded-object
        /// frame.  A player's 1073 positionRelative is local to this entity, not to
        /// the ship root.  Treating it as hull-local displaced players by an entire
        /// deck-panel offset and made valid sail/helm prompts fail authority checks.
        /// </summary>
        internal static bool TryShipSurfaceWorldPose(long shipRootEntityId, long surfaceEntityId,
            out FixedPointPosition position, out uint packedRotation)
        {
            position = default;
            packedRotation = Multiplayer.Placement.Quaternion32Packing.Identity;
            if (WorldsAdriftRebornGameServer.ShipMembership.RootOf(surfaceEntityId)
                != shipRootEntityId)
            {
                return false;
            }

            if (surfaceEntityId == shipRootEntityId)
            {
                (position, packedRotation) = HullWorldPose(shipRootEntityId);
                return true;
            }

            Crafting.MountedParts.Mount? mount = Crafting.MountedParts.MountFor(surfaceEntityId);
            if (mount.HasValue && mount.Value.HullEntityId == shipRootEntityId)
            {
                (position, packedRotation) = MountedPartWorldPose(mount.Value);
                return true;
            }

            FixedPointPosition? deckOffset = Crafting.BuiltShips.LocalOffsetForDeck(surfaceEntityId);
            if (deckOffset.HasValue)
            {
                (FixedPointPosition hullPosition, uint hullRotation) = HullWorldPose(shipRootEntityId);
                (position, packedRotation) = Multiplayer.Ship.ShipSalvagePolicy.DropPose(
                    hullPosition, hullRotation, deckOffset.Value,
                    Multiplayer.Placement.Quaternion32Packing.Identity);
                return true;
            }

            return false;
        }

        internal static FixedPointPosition TransformSurfaceLocalPoint(
            FixedPointPosition surfacePosition, uint surfaceRotation,
            float localX, float localY, float localZ)
        {
            return Multiplayer.Ship.ShipSalvagePolicy.DropPose(
                surfacePosition, surfaceRotation,
                FixedPointPosition.FromMetres(localX, localY, localZ),
                Multiplayer.Placement.Quaternion32Packing.Identity).Position;
        }

        internal static (FixedPointPosition Position, uint PackedRotation) HullWorldPose(long hullEntityId)
        {
            Multiplayer.Ship.Domains.ShipDomain? domain =
                WorldsAdriftRebornGameServer.ShipDomains.ByHull(hullEntityId);
            if (domain != null)
            {
                Multiplayer.Ship.Flight.FlightState state = domain.Flight.State;
                return (FixedPointPosition.FromMetres(state.X, state.Y, state.Z),
                    Multiplayer.Ship.Flight.FlightIntegrator.PackedRotation(state));
            }

            return (WorldsAdriftRebornGameServer.WorldEntities.TransformSeedFor(hullEntityId),
                WorldsAdriftRebornGameServer.WorldEntities.RotationSeedFor(hullEntityId));
        }
    }
}
