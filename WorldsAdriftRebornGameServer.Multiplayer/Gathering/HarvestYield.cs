namespace WorldsAdriftRebornGameServer.Multiplayer.Gathering
{
    /// <summary>
    /// The table that turns "player P harvested source S for N units" into a
    /// concrete item grant. The pure half of Phase 5.4's yield step.
    ///
    /// THE SEAM. This project builds the loop from a harvest HIT to an inventory
    /// YIELD, but it does not own the two ends:
    ///
    ///   - The BEAM (a sibling, Phase 3) produces the hit. On the wire that is a
    ///     salvage/cut signal; the server turns it into "cutter entity E removed
    ///     N units of source S", the shape <c>TreeCutterState_Handler</c> already
    ///     demonstrates for trees (its <c>TreeSectionMaskChange</c> carries the
    ///     cutter, the source key as <c>WoodType</c>, and the unit count as
    ///     <c>SectionsFelled</c>). A metal beam produces the same triple.
    ///
    ///   - The NODES (a sibling, Phase 0+4) are the entities being harvested.
    ///     Each node knows the material it is made of, and that material string
    ///     is the source key. When a node spawns, the node code registers its
    ///     yield here (<see cref="Register"/>); when it is hit, its handler calls
    ///     the award pipeline with that same key.
    ///
    /// So the contract this file defines is exactly: source key in, item grant
    /// out. Wood is pre-registered (trees are the one live harvest source today);
    /// metal node kinds are registered by the nodes agent as it learns them.
    ///
    /// Pure: a dictionary and arithmetic. No ENet, no game types, no item
    /// database - the caller validates the resolved itemTypeId against the real
    /// database at the glue boundary, because an unknown itemTypeId is a hard
    /// client-side NRE and must be rejected there, not guessed at here.
    ///
    /// NOT thread-safe, like the rest of this assembly: the server is one loop.
    /// </summary>
    public sealed class HarvestYield
    {
        private readonly Dictionary<string, YieldRule> _rules = new();

        /// <summary>How many harvest sources have a yield rule.</summary>
        public int Count => _rules.Count;

        /// <summary>
        /// Declares what a source key yields. Returns true if this was a new key,
        /// false if it replaced an existing rule.
        ///
        /// Replacement is allowed rather than thrown so the nodes agent can seed a
        /// default and a later, more specific pass can refine it without the
        /// registration order becoming load-bearing. It is the caller's job not to
        /// register two contradictory rules and expect a particular winner.
        /// </summary>
        public bool Register(string sourceKey, YieldRule rule)
        {
            if (string.IsNullOrEmpty(sourceKey))
            {
                throw new ArgumentException("a harvest source needs a non-empty key", nameof(sourceKey));
            }
            if (rule == null)
            {
                throw new ArgumentNullException(nameof(rule));
            }

            bool isNew = !_rules.ContainsKey(sourceKey);
            _rules[sourceKey] = rule;
            return isNew;
        }

        /// <summary>Whether a source key has a yield rule.</summary>
        public bool Has(string sourceKey) => sourceKey != null && _rules.ContainsKey(sourceKey);

        /// <summary>The rule for a source key, or null if none is registered.</summary>
        public YieldRule? RuleFor(string sourceKey)
        {
            return sourceKey != null && _rules.TryGetValue(sourceKey, out YieldRule? rule) ? rule : null;
        }

        /// <summary>
        /// The grant produced by removing <paramref name="units"/> units of a
        /// source, or null when the source is unregistered or nothing was
        /// removed.
        ///
        /// Null rather than a zero grant on <paramref name="units"/> &lt;= 0: a
        /// hit that felled nothing is not a yield, and pushing a zero-count toast
        /// would tell the player "Salvaged Iron x0". An unregistered source is
        /// also null, and the caller logs it - that is the named symptom for "the
        /// nodes agent spawned a material nobody taught the yield table about",
        /// which is otherwise an invisible no-op.
        /// </summary>
        public YieldGrant? Resolve(string sourceKey, int units)
        {
            if (units <= 0)
            {
                return null;
            }

            YieldRule? rule = RuleFor(sourceKey);

            if (rule == null)
            {
                return null;
            }

            return new YieldGrant(rule.ItemTypeId, rule.AmountPerUnit * units, rule.Quality);
        }
    }
}
