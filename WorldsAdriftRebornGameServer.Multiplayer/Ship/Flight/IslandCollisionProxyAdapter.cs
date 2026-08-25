using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    public readonly record struct IslandCollisionProxyBatch(
        IReadOnlyList<CollisionRuntimeProxy> Proxies,
        bool EvaluationComplete,
        int CandidateCount);

    /// <summary>
    /// Adapts the complete extracted island-envelope catalogue into nearby static
    /// collision proxies. These are always marked conservative: the AABB contains
    /// concavities and empty corners, so it can drive telemetry but never response.
    /// </summary>
    public static class IslandCollisionProxyAdapter
    {
        public const double DefaultInterestRadiusMetres = 1024.0;

        public static IslandCollisionProxyBatch Nearby(ShadowVector3 worldPosition,
            long fixedStep, long authorityGeneration,
            double interestRadiusMetres = DefaultInterestRadiusMetres)
        {
            if (!worldPosition.IsFinite || fixedStep < 0 || authorityGeneration <= 0
                || !double.IsFinite(interestRadiusMetres) || interestRadiusMetres <= 0.0)
                return new IslandCollisionProxyBatch(Array.Empty<CollisionRuntimeProxy>(), false, 0);

            double radiusSquared = interestRadiusMetres * interestRadiusMetres;
            List<(string Key, CollisionAabb Bounds)> candidates = IslandLocationPolicy.KnownWorld()
                .Select(pair => (Key: pair.Island.Id.Value,
                    Bounds: WorldBounds(pair.Island, pair.Envelope)))
                .Where(pair => DistanceSquared(worldPosition, pair.Bounds) <= radiusSquared)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToList();
            bool complete = candidates.Count <= CollisionShadowLimits.MaxTerrainProxies;
            CollisionRuntimeProxy[] proxies = candidates
                .Take(CollisionShadowLimits.MaxTerrainProxies)
                .Select(pair => new CollisionRuntimeProxy(new CollisionProxy(
                        "island:" + pair.Key, CollisionProxyKind.IslandTerrain,
                        pair.Bounds, ShadowVector3.Zero), fixedStep, authorityGeneration,
                    1.0, CollisionGeometryConfidence.ConservativeEnvelope))
                .ToArray();
            return new IslandCollisionProxyBatch(Array.AsReadOnly(proxies), complete,
                candidates.Count);
        }

        private static CollisionAabb WorldBounds(IslandDefinition island,
            IslandTerrainEnvelope envelope) => new(
                new ShadowVector3(island.GlobalOrigin.MetresX + envelope.MinX,
                    island.GlobalOrigin.MetresY + envelope.MinY,
                    island.GlobalOrigin.MetresZ + envelope.MinZ),
                new ShadowVector3(island.GlobalOrigin.MetresX + envelope.MaxX,
                    island.GlobalOrigin.MetresY + envelope.MaxY,
                    island.GlobalOrigin.MetresZ + envelope.MaxZ));

        private static double DistanceSquared(ShadowVector3 point, CollisionAabb bounds)
        {
            double dx = Axis(point.X, bounds.Minimum.X, bounds.Maximum.X);
            double dy = Axis(point.Y, bounds.Minimum.Y, bounds.Maximum.Y);
            double dz = Axis(point.Z, bounds.Minimum.Z, bounds.Maximum.Z);
            return dx * dx + dy * dy + dz * dz;
        }

        private static double Axis(double value, double min, double max) =>
            value < min ? min - value : value > max ? value - max : 0.0;
    }
}
