using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Crafting
{
    /// <summary>
    /// The MINIMAL recipe set a fresh player owns with zero knowledge - the pure,
    /// game-assembly-free policy behind 1079 defaultSchematics. Everything else in
    /// the catalogue is GATED behind the knowledge tree and reaches the book only via
    /// learnedSchematics (see <c>KnowledgeSpendPolicy</c>).
    ///
    /// Pure so the gate is unit-tested on Linux with no game install: the game-side
    /// <c>SchematicHelper.DefaultSchematicIds</c> is a thin adapter that feeds this the
    /// catalogue keys and wraps the result in an Improbable list.
    /// </summary>
    public static class StarterSchematics
    {
        /// <summary>
        /// The starter ids. "torch" and "guitar" have NO tree node, so without them
        /// they would be forever uncraftable; "clothMakeshift" is the tutorial's first
        /// craft and "makeshiftStorage" is the basic land chest. Everything richer -
        /// ship parts, atlas cores, lamps, cooking, the glider - is earned.
        /// </summary>
        public static readonly IReadOnlyList<string> Ids = new[]
        {
            "torch",
            "guitar",
            "clothMakeshift",
            "makeshiftStorage",
            // TEMPORARY (test convenience): "lamp" is a loose ship-part and
            // "assemblyStation" is the workbench you craft ship parts AT - faithfully both
            // should be knowledge-EARNED, not starters. Seeded here so the whole
            // craft-station -> place -> craft-part -> (soon) mount loop is testable
            // immediately; move both to knowledge aliases once the loop is proven.
            "lamp",
            "assemblyStation",
        };

        /// <summary>
        /// The starter ids that actually exist in the loaded catalogue, in declaration
        /// order. A starter id the catalogue does not carry is dropped rather than
        /// dangled, so the set always tracks the file while staying minimal.
        /// </summary>
        public static IEnumerable<string> Default(IEnumerable<string> catalogueKeys)
        {
            // TESTING OVERRIDE (2026-08-11): grant the WHOLE catalogue as learned so the
            // full craftable set is visible immediately for testing ("put all items there,
            // don't fake it by only 1 lamp"). Faithful knowledge-gating is preserved in the
            // data - every recipe is wired to a knowledge node via KnowledgeSpendPolicy
            // aliases - so restoring the gate is just returning to the `Ids`-filtered set
            // below. Every catalogue id is crash-safe (ReferenceDataCrashSafetyTests), so
            // learning them all is safe.
            foreach (string id in catalogueKeys)
            {
                yield return id;
            }
        }

        /// <summary>
        /// The faithful, knowledge-gated starter set (currently overridden by the testing
        /// grant-all in <see cref="Default"/>). Kept so the gate can be restored trivially.
        /// </summary>
        public static IEnumerable<string> FaithfulDefault(IEnumerable<string> catalogueKeys)
        {
            HashSet<string> keys = new HashSet<string>(catalogueKeys);
            foreach (string id in Ids)
            {
                if (keys.Contains(id))
                {
                    yield return id;
                }
            }
        }
    }
}
