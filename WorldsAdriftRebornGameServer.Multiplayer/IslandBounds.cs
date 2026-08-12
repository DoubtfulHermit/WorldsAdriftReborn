namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The COORDINATE-FRAME SAFETY NET for client-supplied resource placements: an
    /// axis-aligned box, in GLOBAL metres, that any position a client replies with on
    /// 1011 must fall inside before this server will spawn an entity there.
    ///
    /// WHY IT EXISTS. The client computes its placements in its OWN Unity space and
    /// converts them with <c>RemapUnityVectorToGlobalCoordinates()</c>, which is
    /// <c>unityPosition + CoordinateRemappingBehaviour.OffsetOrigin</c>
    /// (AbstractDetermineOriginStrategy.UnityPositionToGlobalPosition). The live origin
    /// strategy is <c>ActiveIslandBasedRemapping</c>, whose OffsetOrigin is set to
    /// <c>currentActiveIsland.transform.position.RemapUnityVectorToGlobalVector()</c> -
    /// i.e. the island's own GLOBAL position, which is exactly the
    /// <see cref="SpawnPolicy.IslandPosition"/> we seed into the island's 190602. So the
    /// arithmetic is self-consistent BY CONSTRUCTION: a surface point on Haven remaps
    /// back to <c>islandGlobalMetres + islandLocalOffset</c>, in metres, the same frame
    /// and the same units <see cref="FixedPointPosition.FromMetres"/> encodes.
    ///
    /// THAT IS THE THEORY. The failure modes it does not cover are what this box is for:
    ///  - the island's Unity transform has not yet been driven by our 190602 when the
    ///    visualizer samples, so OffsetOrigin is still zero and the reply arrives in
    ///    ISLAND-LOCAL metres (magnitudes of ~100, not ~17000);
    ///  - a units/SCALE error anywhere in the chain - the exact class of bug that made
    ///    the offline surface extractor wrong on every axis;
    ///  - a hostile or wedged client replying with garbage.
    /// Every one of those puts deposits somewhere absurd - floating in the sky, or
    /// inside another island - which is precisely the outcome the player already
    /// (rightly) rejected once. So a placement outside this box is REFUSED and LOGGED
    /// with its raw metres, rather than spawned.
    ///
    /// It is deliberately GENEROUS. It is not a "is this on the ground" check - the
    /// client already guarantees that, physics-checked, and it is the whole reason the
    /// handshake is the right mechanic. It only has to catch a catastrophe measured in
    /// thousands of metres, so the margin is wide enough that no legitimate on-island
    /// sample can ever be rejected.
    /// </summary>
    public readonly struct IslandBounds
    {
        /// <summary>
        /// Haven's MEASURED island-local bounding box, metres, from the extracted LOD0
        /// surface table (<c>docs/research/world-data/island-surfaces/1431299145.json</c>,
        /// <c>meta.localAABB</c>, TRS-composed, 28616 verts). Min corner.
        /// </summary>
        public static readonly (double X, double Y, double Z) HavenLocalMin = (-303.0, -86.0, -122.4);

        /// <summary>Haven's measured island-local AABB, max corner. See <see cref="HavenLocalMin"/>.</summary>
        public static readonly (double X, double Y, double Z) HavenLocalMax = (256.5, 98.0, 169.1);

        /// <summary>
        /// How far OUTSIDE the measured island AABB a placement may still be accepted,
        /// metres. Deliberately large: the extracted surface is a sample of the mesh, not
        /// a guarantee of its full extent, and rejecting a real placement is worse than
        /// accepting one a little wide. Every frame/scale error this guard exists to catch
        /// is off by thousands of metres, so 250 loses nothing.
        /// </summary>
        public const double DefaultMarginMetres = 250.0;

        private IslandBounds(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            MinX = minX;
            MinY = minY;
            MinZ = minZ;
            MaxX = maxX;
            MaxY = maxY;
            MaxZ = maxZ;
        }

        public double MinX { get; }
        public double MinY { get; }
        public double MinZ { get; }
        public double MaxX { get; }
        public double MaxY { get; }
        public double MaxZ { get; }

        /// <summary>
        /// The acceptance box for an island whose GLOBAL origin is
        /// <paramref name="islandOrigin"/> (its 190602 seed) and whose island-local AABB is
        /// [<paramref name="localMin"/>, <paramref name="localMax"/>], widened by
        /// <paramref name="marginMetres"/> on every side.
        /// </summary>
        public static IslandBounds Around(
            FixedPointPosition islandOrigin,
            (double X, double Y, double Z) localMin,
            (double X, double Y, double Z) localMax,
            double marginMetres)
        {
            double m = marginMetres < 0 ? 0 : marginMetres;
            double ox = islandOrigin.MetresX;
            double oy = islandOrigin.MetresY;
            double oz = islandOrigin.MetresZ;
            return new IslandBounds(
                ox + localMin.X - m, oy + localMin.Y - m, oz + localMin.Z - m,
                ox + localMax.X + m, oy + localMax.Y + m, oz + localMax.Z + m);
        }

        /// <summary>
        /// The acceptance box for HAVEN as this server seeds it: the measured local AABB
        /// around <see cref="SpawnPolicy.IslandPosition"/> plus
        /// <see cref="DefaultMarginMetres"/>. This is what production validates against.
        /// </summary>
        public static IslandBounds Haven()
        {
            return Around(SpawnPolicy.IslandPosition, HavenLocalMin, HavenLocalMax, DefaultMarginMetres);
        }

        /// <summary>Whether a global-metres position is inside the box (inclusive on every face).</summary>
        public bool Contains(double x, double y, double z)
        {
            return x >= MinX && x <= MaxX
                && y >= MinY && y <= MaxY
                && z >= MinZ && z <= MaxZ;
        }

        /// <summary>
        /// A one-line human description of the box, for the rejection log. Reading it
        /// beside the rejected coordinate is enough to tell an origin error (the reply is
        /// near zero, or near another island) from a scale error (the reply is a multiple
        /// of the right answer).
        /// </summary>
        public override string ToString()
        {
            return "x[" + MinX.ToString("0.#") + ".." + MaxX.ToString("0.#") + "] "
                 + "y[" + MinY.ToString("0.#") + ".." + MaxY.ToString("0.#") + "] "
                 + "z[" + MinZ.ToString("0.#") + ".." + MaxZ.ToString("0.#") + "] m";
        }
    }
}
