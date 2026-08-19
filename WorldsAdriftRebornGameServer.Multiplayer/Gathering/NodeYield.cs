namespace WorldsAdriftRebornGameServer.Multiplayer.Gathering
{
    /// <summary>
    /// What a placed metal node - a nugget or an anchored deposit - is worth, as
    /// one function of the node itself.
    ///
    /// WHY THIS EXISTS AT ALL, rather than the two lines it replaces being inlined
    /// at the two spawn sites and the two hit sites. Those four sites each used to
    /// spell out `new YieldRule(node.MetalType, 1)` or
    /// `Award(..., node.MetalType, ...)` by hand, and every one of them dropped
    /// <see cref="MetalNode.Quality"/> on the floor - not because anybody decided
    /// to, but because the node's quality and the node's metal name were two
    /// separate arguments and only one of them was obviously needed. The result
    /// was that every metal a player has ever mined on this server arrived at
    /// quality 0, which is off the bottom of retail's 1..10 scale and therefore
    /// satisfies no crafting slot that asks for anything, and renders in the
    /// tooltip as the literal string "Quality: 0".
    ///
    /// So the shape here is deliberate: a node goes in, and BOTH facts come out
    /// together. There is no way to ask this type for a node's material without
    /// also being handed its quality, which is the only durable defence against
    /// the same omission happening again the next time a harvest source is added.
    ///
    /// THE DATA IS ALREADY RIGHT, and that is worth stating because it changes
    /// what this is fixing. `island_resources.json` and the shipped
    /// release-runtime catalogue carry a real per-island metal table for all 254
    /// islands - 1930 deposits, 15 metals, qualities 1..10, with the provenance of
    /// each island's table recorded and enforced by IslandSurveyProfile. That
    /// table already reaches <see cref="MetalNode"/>. It simply stopped one step
    /// short of the player's inventory.
    ///
    /// Pure: a node in, a rule out. No I/O, no game types.
    /// </summary>
    public static class NodeYield
    {
        /// <summary>
        /// How many items one harvested unit of a metal node is worth. One, because
        /// the unit counts (MetalNodes.NuggetYieldUnits, MetalDeposits.YieldUnits)
        /// are already denominated in items - the per-unit multiplier is where a
        /// future "this metal comes in bigger lumps" rule would live, and there is
        /// no evidence retail had one.
        /// </summary>
        public const int ItemsPerUnit = 1;

        /// <summary>
        /// The source key a node's hits will be reported under. The metal name, for
        /// the reason the yield table documents: it is the only fact both the node
        /// and the hit already agree on without either learning the other's ids.
        /// </summary>
        public static string SourceKeyFor(MetalNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            return node.MetalType;
        }

        /// <summary>
        /// The yield rule to register for a node, carrying its quality.
        ///
        /// Registering the quality is belt to <see cref="QualityOf"/>'s braces. The
        /// per-hit override is the CORRECT path - it is the only one that can tell
        /// two iron nodes of different quality apart - but seeding the rule with a
        /// real quality too means that if the override is ever lost, the failure
        /// degrades to "the wrong node's quality" rather than all the way back to
        /// the out-of-range 0 that started this. Both would have to be broken for
        /// quality to disappear again.
        /// </summary>
        public static YieldRule RuleFor(MetalNode node)
        {
            return new YieldRule(SourceKeyFor(node), ItemsPerUnit, QualityOf(node));
        }

        /// <summary>
        /// The quality a hit on this node should grant, normalised into the range
        /// <see cref="YieldRule"/> accepts.
        ///
        /// A node whose quality is outside 1..10 is clamped rather than thrown on,
        /// and this is the one place in this module that forgives bad input. The
        /// reason is that a node's quality comes from a 254-island community
        /// survey and from a tier-cohort generator, not from code - so a single bad
        /// row must not be able to take the server down at boot, when the visible
        /// cost of the clamp is one node paying quality 1 or 10 instead of
        /// something absurd.
        /// </summary>
        public static int QualityOf(MetalNode node)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            if (node.Quality < YieldRule.MinQuality)
            {
                return YieldRule.MinQuality;
            }

            return node.Quality > YieldRule.MaxQuality ? YieldRule.MaxQuality : node.Quality;
        }
    }
}
