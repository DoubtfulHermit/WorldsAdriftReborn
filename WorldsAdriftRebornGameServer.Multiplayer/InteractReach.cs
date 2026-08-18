namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// THE RULE THAT DECIDES WHETHER A 1210 PROMPT CAN EVER APPEAR, restated as
    /// arithmetic so a seeded radius can be checked against a prefab's geometry in
    /// a unit test instead of on a live client.
    ///
    /// RECOVERED, verbatim, from <c>Assets.Scripts.Player.PlayerLookingAt</c>
    /// (decompile of the shipped client's Assembly-CSharp):
    ///
    /// <code>
    /// public bool InRange(InteractiveObjectVisualizer o, float leeway)
    /// {
    ///     return Vector3.Distance(o.transform.position, base.transform.position)
    ///            + 0.5f &lt; o.InteractRange + leeway;
    /// }
    /// </code>
    ///
    /// with <c>InteractRange =&gt; _interaction.radius</c>, i.e. the radius on the
    /// 1210 <c>InteractionEntry</c> THIS SERVER seeds, and <c>leeway = 0</c> on the
    /// per-frame look test that decides whether the prompt is offered at all.
    /// (The completed-hold re-check in <c>InteractAgentObserver.CheckInteraction</c>
    /// passes <c>leeway = 2f</c>, so it is strictly more forgiving and never the
    /// binding constraint.)
    ///
    /// THE TRAP THIS EXISTS TO CATCH. The distance is measured to the
    /// <c>InteractiveObjectVisualizer</c>'s OWN transform, which is wherever the
    /// prefab author put that component in the hierarchy - NOT to the entity's
    /// origin and NOT to the collider the player is looking at. When those differ
    /// by more than the radius allows, the reachable set is a sphere the player
    /// cannot stand in, and the prompt is missing with nothing in any log to say
    /// why. That is exactly how the Wilderness shrine shipped dead: its visualizer
    /// sits 2.704 m BELOW the entity origin, under the plate the player stands on,
    /// so a 3 m radius put the whole reachable sphere underground.
    ///
    /// Every other 1210 this server seeds is on a prefab whose visualizer sits on
    /// or above the entity origin (nugget and helm: on the root, offset 0; placed
    /// shipyard: +1.299 m on the <c>Crafting_Station</c> child), which is why
    /// copying the nugget's 3 m worked for them and only for them.
    /// </summary>
    public static class InteractReach
    {
        /// <summary>
        /// The constant the client adds to the measured distance before comparing
        /// it with the radius - RECOVERED from <c>PlayerLookingAt.InRange</c>, not
        /// chosen here. Every seeded radius is effectively half a metre smaller
        /// than it reads.
        /// </summary>
        public const float LookRangePenaltyMetres = 0.5f;

        /// <summary>
        /// Whether a player standing <paramref name="horizontalMetres"/> away and
        /// <paramref name="verticalMetres"/> above the visualizer's transform is
        /// offered the prompt, given a seeded <paramref name="radiusMetres"/>.
        ///
        /// Strictly less-than, like the client's own <c>&lt;</c>: a player exactly on
        /// the boundary gets nothing.
        /// </summary>
        public static bool IsReachable(float radiusMetres, float horizontalMetres, float verticalMetres)
        {
            return DistanceTo(horizontalMetres, verticalMetres) + LookRangePenaltyMetres < radiusMetres;
        }

        /// <summary>
        /// The smallest radius that would put a player at
        /// (<paramref name="horizontalMetres"/>, <paramref name="verticalMetres"/>)
        /// exactly ON the boundary. Since the client's test is strict, a seeded
        /// radius must be strictly GREATER than this - see
        /// <see cref="RadiusToCover"/> for the value to actually seed.
        /// </summary>
        public static float MinimumRadiusFor(float horizontalMetres, float verticalMetres)
        {
            return DistanceTo(horizontalMetres, verticalMetres) + LookRangePenaltyMetres;
        }

        /// <summary>
        /// <see cref="MinimumRadiusFor"/> rounded UP to the next tenth of a metre,
        /// which is both strictly greater than the boundary and a number a human
        /// can read in a seed. Use this when deriving a radius from measured prefab
        /// geometry.
        /// </summary>
        public static float RadiusToCover(float horizontalMetres, float verticalMetres)
        {
            float minimum = MinimumRadiusFor(horizontalMetres, verticalMetres);
            float rounded = (float)(Math.Ceiling(minimum * 10.0) / 10.0);
            // Ceiling of an exact tenth is that same tenth, which would leave the
            // player ON the boundary the client refuses. Step once more.
            return rounded > minimum ? rounded : rounded + 0.1f;
        }

        /// <summary>
        /// The highest a player's feet can be above the visualizer's transform and
        /// still be offered the prompt, standing directly over it. Zero (rather
        /// than negative) when the radius cannot reach at all - a visualizer buried
        /// deeper than its own radius is unreachable from ANY position, which is
        /// the failure this whole type exists to make visible.
        /// </summary>
        public static float MaxHeightAbove(float radiusMetres)
        {
            float reach = radiusMetres - LookRangePenaltyMetres;
            return reach > 0f ? reach : 0f;
        }

        private static float DistanceTo(float horizontalMetres, float verticalMetres)
        {
            return (float)Math.Sqrt(
                (double)horizontalMetres * horizontalMetres
                + (double)verticalMetres * verticalMetres);
        }
    }
}
