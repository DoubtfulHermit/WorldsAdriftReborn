using WorldsAdriftRebornGameServer.Multiplayer.Ship;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Pure connect-plan spatial policy. Resources use the small spawn bubble;
    /// complete built-ship domains use the island-scale ship radius. Everything
    /// else keeps the loading-barrier classification it already had.
    /// </summary>
    public static class ConnectInterestPolicy
    {
        public static bool IsShipManaged(string? key, bool isMountedPart) =>
            isMountedPart || BuiltShipPlacement.IsBuiltShipEntityKey(key);

        public static bool IsGateable(string? key, bool isMountedPart,
            bool resourceInterestEnabled) =>
            IsShipManaged(key, isMountedPart)
            || (resourceInterestEnabled
                && ResourceInterestPolicy.IsStreamedResourceKey(key));

        public static double RadiusFor(string? key, bool isMountedPart,
            double resourceRadiusMetres, double shipRadiusMetres) =>
            IsShipManaged(key, isMountedPart)
                ? shipRadiusMetres
                : resourceRadiusMetres;

        public static bool IsInitial(string? key, bool isMountedPart,
            bool baseInitial, bool resourceInterestEnabled,
            FixedPointPosition spawnPosition, FixedPointPosition gatePosition,
            double resourceRadiusMetres, double shipRadiusMetres)
        {
            if (!IsGateable(key, isMountedPart, resourceInterestEnabled))
            {
                return baseInitial;
            }

            return InterestPolicy.InRange(spawnPosition, gatePosition,
                RadiusFor(key, isMountedPart, resourceRadiusMetres,
                    shipRadiusMetres));
        }
    }
}
