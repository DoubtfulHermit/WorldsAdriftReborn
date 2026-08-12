namespace WorldsAdriftRebornGameServer.Multiplayer.Resources
{
    /// <summary>
    /// A lateral (X/Z) keep-out disc in island-local metres: no generated placement
    /// may fall inside it. Used to keep resources off the player spawn, the ship /
    /// shipyard footprint, and any dense static prop the research says to avoid
    /// (findings-resource-placement.md, "Distribution Rules"). Height-independent on
    /// purpose - the obstacles that matter (a player, a hull, a tree trunk) occupy a
    /// vertical column, so the test is on the ground plane, exactly like WA's own
    /// <c>BoundsContainsLateral</c>.
    /// </summary>
    public readonly struct PlacementExclusion
    {
        public PlacementExclusion(double localX, double localZ, double radiusMetres)
        {
            LocalX = localX;
            LocalZ = localZ;
            RadiusMetres = radiusMetres;
        }

        public double LocalX { get; }
        public double LocalZ { get; }
        public double RadiusMetres { get; }

        /// <summary>Whether a lateral point falls inside this disc.</summary>
        public bool Contains(double x, double z)
        {
            double dx = x - LocalX;
            double dz = z - LocalZ;
            return (dx * dx + dz * dz) < (RadiusMetres * RadiusMetres);
        }
    }
}
