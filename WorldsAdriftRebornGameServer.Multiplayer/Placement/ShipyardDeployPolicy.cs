using System.Globalization;

namespace WorldsAdriftRebornGameServer.Multiplayer.Placement
{
    /// <summary>
    /// Pure rules for the shipyard FOLD-OUT animation (the client's
    /// <c>Shipyard_Deploy</c> clip, gated on the served 1205 <c>ShipyardState.deployed</c>).
    ///
    /// The client's <c>ShipyardVisualizer.OnVisualiserEnabled</c> reads <c>_state.Deployed</c>
    /// ONCE at checkout and calls <c>Shipyard.Deploy()</c>: <c>deployed==false</c> plays the
    /// full panel/leg fold-out, <c>deployed==true</c> snaps to the finished pose
    /// (Shipyard.cs:90-142, ShipyardVisualizer.cs:39-40). So the only server lever is which
    /// bool a client sees when it first checks the yard out:
    ///
    ///   * a LIVE placement seeds <c>deployed=false</c> so the placer watches it build out,
    ///     then the server flips the seed to <c>true</c> after the clip has played so any
    ///     LATER checkout (a re-join, persistence) snaps instead of re-animating;
    ///   * a BOOT-RESTORED yard (already deployed last session) seeds <c>deployed=true</c>
    ///     and never animates.
    ///
    /// This is the element-agnostic decision half; the timed flip + 1205 push is thin glue
    /// (Game.Placement). It depends on nothing but a bool and a string, so it unit-tests
    /// natively with no game install.
    /// </summary>
    public static class ShipyardDeployPolicy
    {
        /// <summary>
        /// The 1205 <c>deployed</c> value a yard is SEEDED with: <c>false</c> (play the
        /// fold-out) for a live placement, <c>true</c> (snap) for a boot restore.
        /// </summary>
        public static bool InitialDeployed(bool livePlacement) => !livePlacement;

        /// <summary>Whether a placement should schedule the deferred fold-out completion flip.</summary>
        public static bool AnimatesFoldOut(bool livePlacement) => livePlacement;

        /// <summary>The 1205 <c>deployed</c> value after the fold-out clip has played: always deployed.</summary>
        public const bool DeployedAfterFlip = true;

        /// <summary>
        /// Best-guess fold-out duration in seconds. The exact <c>Shipyard_Deploy</c> clip
        /// length + leg-lerp time is a live-capture unknown (an Animator clip plus a
        /// serialized <c>_legSpeed</c> raycast lerp, Shipyard.cs:103-140); any value at or
        /// above the real length works, because the flip only decides when a LATER checkout
        /// snaps. Overridable via WAREBORN_SHIPYARD_DEPLOY_SECONDS without a rebuild.
        /// </summary>
        public const float DefaultDeploySeconds = 3.0f;

        /// <summary>
        /// The fold-out duration to use: the parsed positive value of <paramref name="rawEnv"/>
        /// (WAREBORN_SHIPYARD_DEPLOY_SECONDS), or <see cref="DefaultDeploySeconds"/> when it is
        /// blank, unparseable, or non-positive. Pure: the glue reads the env and passes it here.
        /// </summary>
        public static float DeploySeconds(string? rawEnv)
        {
            if (!string.IsNullOrWhiteSpace(rawEnv)
                && float.TryParse(rawEnv.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                && parsed > 0f)
            {
                return parsed;
            }
            return DefaultDeploySeconds;
        }
    }
}
