namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// Every NUMBER a second evaluator of <see cref="SkyWhaleCircuit"/> and
    /// <see cref="SkyWhaleMotion"/> needs, read from those types rather than
    /// restated. Shaped and motivated exactly like
    /// <see cref="FaunaMapConstants"/>: the browser is given no numbers of its own,
    /// so retuning the whale's speed or altitude moves the map with it and cannot be
    /// forgotten.
    /// </summary>
    public readonly record struct SkyWhaleMapConstants(
        double MetresPerSecond,
        double AltitudeAboveIslandMetres,
        double CallIntervalSeconds,
        double LoadRadiusMetres,
        double UnloadRadiusMetres,
        double CallRadiusMetres,
        double PoseIntervalSeconds,
        int MinimumIslands,
        int PerPeerWhales);

    /// <summary>
    /// THE route, flattened so something other than this process can fly the whale
    /// along it.
    ///
    /// THE WAYPOINTS TRAVEL, and that is the difference from the fauna projection.
    /// A creature's motion is derived from its ISLAND's envelope, which the map
    /// already draws, so <see cref="FaunaIslandMotion"/> can publish a handful of
    /// scalars. A whale's path is a property of the whole WORLD - which islands
    /// exist, which cell each is in and where they are - and there is no smaller
    /// honest summary of that than the route itself. Fifty-odd island waypoints plus
    /// the resampled crossings between zones is a couple of kilobytes, published
    /// ONCE in a static block rather than in the live feed.
    ///
    /// THE MIGRATION IS ENTIRELY IN THIS DATA, which is the quiet win of the
    /// single-whale rework: the browser's motion mirror did not change at all, and
    /// could not have needed to, because zone-to-zone travel is expressed as control
    /// points on the same closed spline rather than as an event a second evaluator
    /// would have had to re-implement.
    ///
    /// WHAT IS AND IS NOT GUARANTEED IDENTICAL. The map's waypoints are built from
    /// the preserved MapFile's island placements; the game server's are built from
    /// its own island origins, which are allowed to differ. So the two evaluators
    /// are NOT guaranteed to put the whale on the same world coordinate, and the
    /// parity test does not claim they do. What IS guaranteed, and what the parity
    /// test asserts to a nanometre, is that GIVEN THE SAME RING both evaluators
    /// return the same metres - the motion is one function, and only the geometry it
    /// is fed has two sources. That is the same split
    /// <see cref="IslandFaunaMapModel"/> already makes.
    /// </summary>
    public readonly record struct SkyWhaleRouteMotion(
        string RouteId,
        double LengthMetres,
        double CircuitSeconds,
        double PhaseFraction,
        IReadOnlyList<SkyWhaleWaypoint> Waypoints);

    /// <summary>
    /// The sky whale's motion model, flattened for a second evaluator. Pure,
    /// engine-free and total; it decides nothing and only re-presents what
    /// <see cref="SkyWhalePolicy"/> and <see cref="SkyWhaleCircuit"/> already say.
    /// </summary>
    public static class SkyWhaleMapModel
    {
        /// <summary>
        /// The constants, read straight off the policy. A property built from the
        /// real fields rather than a literal table, for the reason
        /// <see cref="IslandFaunaMapModel.Constants"/> gives: a literal would be a
        /// second place to change a tuning value.
        /// </summary>
        public static SkyWhaleMapConstants Constants { get; } = new SkyWhaleMapConstants(
            MetresPerSecond: SkyWhalePolicy.MetresPerSecond,
            AltitudeAboveIslandMetres: SkyWhalePolicy.AltitudeAboveIslandMetres,
            CallIntervalSeconds: SkyWhalePolicy.CallIntervalSeconds,
            LoadRadiusMetres: SkyWhalePolicy.DefaultLoadRadiusMetres,
            UnloadRadiusMetres: SkyWhalePolicy.UnloadRadiusFor(
                SkyWhalePolicy.DefaultLoadRadiusMetres),
            CallRadiusMetres: SkyWhalePolicy.DefaultCallRadiusMetres,
            PoseIntervalSeconds: SkyWhalePolicy.DefaultPoseInterval.TotalSeconds,
            MinimumIslands: SkyWhalePolicy.MinimumIslands,
            PerPeerWhales: SkyWhalePolicy.DefaultPerPeerWhales);

        /// <summary>
        /// The route, flattened. Every field is the circuit's own accessor, called -
        /// never a re-derivation of it.
        /// </summary>
        public static SkyWhaleRouteMotion MotionFor(SkyWhaleCircuit circuit)
        {
            if (circuit == null) throw new ArgumentNullException(nameof(circuit));
            return new SkyWhaleRouteMotion(
                RouteId: circuit.RouteId,
                LengthMetres: circuit.LengthMetres,
                CircuitSeconds: circuit.CircuitSeconds,
                PhaseFraction: circuit.PhaseFraction,
                Waypoints: circuit.Waypoints);
        }
    }
}
