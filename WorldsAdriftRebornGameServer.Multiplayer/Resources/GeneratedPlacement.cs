namespace WorldsAdriftRebornGameServer.Multiplayer.Resources
{
    /// <summary>
    /// One accepted resource placement the generator emits: an island-LOCAL metre
    /// position that passed every acceptance rule (upward normal, reachable height,
    /// min-spacing, exclusions). It carries the surface normal it was accepted from
    /// too, so a later pass can orient the prop to the ground if it ever needs to.
    ///
    /// This is the resource-TYPE-agnostic output: metal deposits, fuel deposits and
    /// trees all come out of the same generator as these; the caller decides what
    /// asset and metadata to hang on each position. See
    /// <see cref="SurfacePlacementGenerator"/>.
    /// </summary>
    public readonly struct GeneratedPlacement
    {
        public GeneratedPlacement(double localX, double localY, double localZ, double ny)
        {
            LocalX = localX;
            LocalY = localY;
            LocalZ = localZ;
            Ny = ny;
        }

        public double LocalX { get; }
        public double LocalY { get; }
        public double LocalZ { get; }

        /// <summary>The upward-facing measure of the surface this sits on (dot(up, normal)).</summary>
        public double Ny { get; }
    }
}
