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
        private readonly Dictionary<string, List<YieldRule>> _rules = new();

        /// <summary>How many harvest sources have a yield rule.</summary>
        public int Count => _rules.Count;

        /// <summary>
        /// Declares what a source key yields, REPLACING anything already declared
        /// for it. Returns true if this was a new key.
        ///
        /// Replacement is allowed rather than thrown so a caller can seed a default
        /// and a later, more specific pass can refine it without the registration
        /// order becoming load-bearing. Use <see cref="AddYield"/> when you mean to
        /// add a SECOND thing the same cut produces rather than to correct the first.
        /// </summary>
        public bool Register(string sourceKey, YieldRule rule)
        {
            bool isNew = Prepare(sourceKey, rule, out List<YieldRule> rules);
            rules.Clear();
            rules.Add(rule);
            return isNew;
        }

        /// <summary>
        /// Adds ANOTHER thing this source yields, alongside whatever it already
        /// yields. Returns true if this was a new key.
        ///
        /// ONE CUT, SEVERAL MATERIALS is the shape retail actually had and the one
        /// this table was missing. Bossa's own tutorial says it plainly - "Cloth and
        /// Wood, both of which can be salvaged from trees", and Daccat Berries "can
        /// be salvaged from tree trunks and branches" - so a tree is not a wood
        /// source that also happens to drop things; wood, plant fibre and berries
        /// are three yields of the single beam hit the server already awards.
        ///
        /// Duplicate itemTypeIds are REFUSED rather than merged. Two rules granting
        /// the same item off one hit is always a mistake - it produces two stacks
        /// and two toasts for one material - and it is the kind of mistake that
        /// reads as a drop-rate bug rather than as a registration bug.
        /// </summary>
        public bool AddYield(string sourceKey, YieldRule rule)
        {
            bool isNew = Prepare(sourceKey, rule, out List<YieldRule> rules);

            foreach (YieldRule existing in rules)
            {
                if (string.Equals(existing.ItemTypeId, rule.ItemTypeId, StringComparison.Ordinal))
                {
                    throw new ArgumentException("source '" + sourceKey + "' already yields '"
                        + rule.ItemTypeId + "'; one hit must not grant the same item twice", nameof(rule));
                }
            }

            rules.Add(rule);
            return isNew;
        }

        private bool Prepare(string sourceKey, YieldRule rule, out List<YieldRule> rules)
        {
            if (string.IsNullOrEmpty(sourceKey))
            {
                throw new ArgumentException("a harvest source needs a non-empty key", nameof(sourceKey));
            }
            if (rule == null)
            {
                throw new ArgumentNullException(nameof(rule));
            }

            bool isNew = !_rules.TryGetValue(sourceKey, out List<YieldRule>? found);

            if (found == null)
            {
                found = new List<YieldRule>(1);
                _rules[sourceKey] = found;
            }

            rules = found;
            return isNew;
        }

        /// <summary>Whether a source key has a yield rule.</summary>
        public bool Has(string sourceKey) => sourceKey != null && _rules.ContainsKey(sourceKey);

        /// <summary>
        /// The PRIMARY rule for a source key - the first thing declared for it - or
        /// null if none is registered. The primary is the material the source is
        /// named for: a birch tree's primary yield is birch wood, and its fibre and
        /// berries are secondary.
        /// </summary>
        public YieldRule? RuleFor(string sourceKey)
        {
            IReadOnlyList<YieldRule> rules = RulesFor(sourceKey);
            return rules.Count == 0 ? null : rules[0];
        }

        /// <summary>Everything a source key yields, in declaration order. Empty if unregistered.</summary>
        public IReadOnlyList<YieldRule> RulesFor(string sourceKey)
        {
            return sourceKey != null && _rules.TryGetValue(sourceKey, out List<YieldRule>? rules)
                ? rules
                : Array.Empty<YieldRule>();
        }

        /// <summary>
        /// EVERY grant produced by removing <paramref name="units"/> units of a
        /// source - one per declared rule, in declaration order - or empty when the
        /// source is unregistered or nothing was removed.
        ///
        /// A LIST rather than a single grant, and deliberately not a single grant
        /// plus a "get the rest" method: a caller that quietly ignores the secondary
        /// yields is precisely how plant fibre and berries would go missing again,
        /// and the only defence against that is for there to be no shorter call.
        ///
        /// Empty rather than a zero grant on <paramref name="units"/> &lt;= 0: a
        /// hit that felled nothing is not a yield, and pushing a zero-count toast
        /// would tell the player "Salvaged Iron x0". An unregistered source is
        /// also null, and the caller logs it - that is the named symptom for "the
        /// nodes agent spawned a material nobody taught the yield table about",
        /// which is otherwise an invisible no-op.
        ///
        /// <paramref name="quality"/> IS THE FIX FOR THE TABLE'S ONE STRUCTURAL
        /// LIE. This table is keyed by the material NAME, but quality belongs to
        /// the NODE: `island_resources.json` gives Shattered Mausoleum eleven
        /// metals at seven different qualities, and the release catalogue stamps
        /// 1930 deposits across qualities 1..10. Registering a per-node quality
        /// into a name-keyed table means the last node registered decides what
        /// every node of that metal pays, forever. So a caller that KNOWS which
        /// node was hit passes its quality here and it wins; a caller that only
        /// knows a material name (a tree, a fuel pod) passes null and the rule's
        /// own default applies.
        ///
        /// The override applies to EVERY rule, because it describes the node rather
        /// than the material, and a node has exactly one quality by construction -
        /// retail's rock carried a single int (MetalRockStateData.quality). A source
        /// whose several yields need several qualities should pass null and declare
        /// them per rule.
        /// </summary>
        public IReadOnlyList<YieldGrant> Resolve(string sourceKey, int units, int? quality = null)
        {
            if (units <= 0)
            {
                return Array.Empty<YieldGrant>();
            }

            IReadOnlyList<YieldRule> rules = RulesFor(sourceKey);

            if (rules.Count == 0)
            {
                return Array.Empty<YieldGrant>();
            }

            List<YieldGrant> grants = new(rules.Count);

            foreach (YieldRule rule in rules)
            {
                grants.Add(new YieldGrant(rule.ItemTypeId, rule.AmountPerUnit * units,
                    quality ?? rule.Quality));
            }

            return grants;
        }
    }
}
