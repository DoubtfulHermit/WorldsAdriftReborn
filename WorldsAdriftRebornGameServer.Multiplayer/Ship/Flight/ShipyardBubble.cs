using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// The player-visible shipyard "bubble" as a server-side volume.
    ///
    /// RECOVERED (docs/research/findings-shipyard-dome.md): the bubble is the
    /// shipyard INFLUENCE DOME. The client's <c>Shipyard</c> MonoBehaviour carries
    /// <c>ImpactRadius</c> (default 35 m), <c>_influenceDome</c>,
    /// <c>_influenceDomeComponent</c> (<c>ShipyardDomeTrigger</c>) and the animator
    /// constants <c>SpawnBubble</c>/<c>DespawnBubble</c>, and it answers membership
    /// with <c>bool IsWithinRange(Vector3 position)</c> - a plain sphere of
    /// <c>ImpactRadius</c> about the yard. <see cref="IsWithinRange"/> is that
    /// recovered test.
    ///
    /// APPROXIMATION: the dome MESH's own geometry (<c>targetDomeRadius</c>,
    /// <c>_radiusThresholdScalar</c>, <c>_domeColliderOffset</c>) is prefab
    /// serialized and did not survive - WA streamed prefab bundles from a dead CDN
    /// and this install caches island bundles only. We therefore use the recovered
    /// <c>ImpactRadius</c> as the single radius for the visible bubble as well,
    /// rather than minting a second radius that would read as retail truth.
    ///
    /// WAREBORN TUNING: <see cref="DomeFloorOffsetMetres"/> and
    /// <see cref="ExitMarginMetres"/>. See their own notes.
    /// </summary>
    public readonly record struct ShipyardBubble(
        ShadowVector3 YardPosition,
        double ImpactRadiusMetres,
        double DomeFloorOffsetMetres,
        double ExitMarginMetres)
    {
        public bool IsValid => YardPosition.IsFinite
            && double.IsFinite(ImpactRadiusMetres) && ImpactRadiusMetres > 0.0
            && double.IsFinite(DomeFloorOffsetMetres)
            && double.IsFinite(ExitMarginMetres) && ExitMarginMetres >= 0.0;

        /// <summary>
        /// The world height at which "above the shipyard" begins: the yard's own
        /// registered Y plus <see cref="DomeFloorOffsetMetres"/>. The client's word
        /// for the bubble is a DOME - a hemisphere standing on the yard - so the
        /// yard's registration plane is the natural floor.
        /// </summary>
        public double DomeFloorMetres => YardPosition.Y + DomeFloorOffsetMetres;

        public double DistanceFromYard(ShadowVector3 point) =>
            (point - YardPosition).Magnitude;

        /// <summary>
        /// RECOVERED semantics: <c>Shipyard.IsWithinRange(Vector3)</c> - inside the
        /// influence sphere of <c>ImpactRadius</c> about the yard, in any direction.
        /// </summary>
        public bool IsWithinRange(ShadowVector3 point) => IsValid && point.IsFinite
            && DistanceFromYard(point) <= ImpactRadiusMetres;

        /// <summary>ABOVE the shipyard: at or above the dome floor.</summary>
        public bool IsAboveYard(ShadowVector3 point) => IsValid && point.IsFinite
            && point.Y >= DomeFloorMetres;

        /// <summary>
        /// The docking volume the player describes as "inside the bubble and above
        /// the shipyard": the UPPER half of the influence sphere. The horizontal
        /// reach is the recovered 35 m <c>ImpactRadius</c>; the floor is the
        /// vertical band (WAReborn tuning) that keeps a hull passing UNDER an
        /// island-mounted yard from being treated as parked on it.
        /// </summary>
        public bool ContainsDock(ShadowVector3 point) =>
            IsWithinRange(point) && IsAboveYard(point);

        /// <summary>
        /// Whether a hull is FULLY outside the bubble - the departure-completion
        /// test. "Fully" is literal: the hull's own clearance radius comes off the
        /// distance, so a hull whose near edge still overlaps the dome has not
        /// cleared it. <see cref="ExitMarginMetres"/> is the hysteresis band: entry
        /// happens at <see cref="ImpactRadiusMetres"/> and exit only past
        /// radius + margin, so a hull hovering exactly at the visible edge cannot
        /// flap between linked and unlinked.
        /// </summary>
        public bool HasFullyCleared(ShadowVector3 point,
            double hullClearanceRadiusMetres = 0.0)
        {
            if (!IsValid || !point.IsFinite) return false;
            double hull = double.IsFinite(hullClearanceRadiusMetres)
                && hullClearanceRadiusMetres > 0.0 ? hullClearanceRadiusMetres : 0.0;
            return DistanceFromYard(point) - hull > ImpactRadiusMetres + ExitMarginMetres;
        }
    }
}
