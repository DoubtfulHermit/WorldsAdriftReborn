namespace WorldsAdriftRebornGameServer.Multiplayer.Resources
{
    /// <summary>
    /// One extracted LOD0 surface sample on an island: an island-LOCAL metre
    /// position and the surface normal at that point. This is the raw material the
    /// <see cref="SurfacePlacementGenerator"/> filters and thins into resource
    /// placements - the same data the real Worlds Adrift client sampled at runtime
    /// (IslandSurfaceData.FindPlace), only extracted offline (TRS-composed, so on
    /// the exact geometry the runtime collides against) instead of read live.
    ///
    /// Pure value, no Unity types, so the generator that consumes it is unit-tested
    /// natively.
    /// </summary>
    public readonly struct SurfaceSample
    {
        public SurfaceSample(double localX, double localY, double localZ, double nx, double ny, double nz)
        {
            LocalX = localX;
            LocalY = localY;
            LocalZ = localZ;
            Nx = nx;
            Ny = ny;
            Nz = nz;
        }

        /// <summary>Island-local X, metres.</summary>
        public double LocalX { get; }

        /// <summary>Island-local Y (height above the island's local origin), metres.</summary>
        public double LocalY { get; }

        /// <summary>Island-local Z, metres.</summary>
        public double LocalZ { get; }

        /// <summary>Surface normal X.</summary>
        public double Nx { get; }

        /// <summary>
        /// Surface normal Y - how UPWARD-facing the surface is. Equal to
        /// dot(up, normal); 1.0 is dead flat, 0.0 is a vertical wall. This is the
        /// value WA's own placement filter tested (dot(up, normal) &gt; threshold),
        /// and the primary flatness gate in <see cref="SurfacePlacementGenerator"/>.
        /// </summary>
        public double Ny { get; }

        /// <summary>Surface normal Z.</summary>
        public double Nz { get; }
    }
}
