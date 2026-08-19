using WorldsAdriftRebornGameServer.Game.Inventory;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Gathering;

namespace WorldsAdriftRebornGameServer.Game.Gathering
{
    /// <summary>
    /// Closes the gathering loop: a harvest hit becomes an inventory item plus
    /// the native "Salvaged &lt;material&gt; xN" toast. This is Phase 5.4, and it
    /// is the ONE server-side entry point every harvest source funnels through.
    ///
    /// THE INTEGRATION SEAM (what the sibling agents connect to)
    /// ---------------------------------------------------------
    /// Two ends are built by other agents; this is the join:
    ///
    ///   * The BEAM (Phase 3) produces the hit. However the wire delivers it, the
    ///     handler distils it to the triple <see cref="Award"/> takes: WHO
    ///     harvested (their player entity id), WHAT they hit (a source key), and
    ///     HOW MUCH came away (a unit count). <c>TreeCutterState_Handler</c> +
    ///     <c>TickTreeHarvest</c> already do exactly this for trees and are the
    ///     worked example: <c>change.CutterEntityId</c>, <c>change.WoodType</c>,
    ///     <c>change.SectionsFelled</c>.
    ///
    ///   * The NODES (Phase 0+4) are what gets hit. A node knows the material it
    ///     is made of; that material string is the source key. When a node spawns,
    ///     the node code calls <see cref="Register"/> to declare its yield (e.g.
    ///     <c>Register("iron", new YieldRule("iron", amountPerUnit: 12))</c>);
    ///     when the beam hits it, the node's handler calls
    ///     <c>Award(harvesterEntity, "iron", units, reason)</c>.
    ///
    /// So neither sibling touches this file's body - they call
    /// <see cref="Register"/> and <see cref="Award"/>. Wood is pre-registered
    /// here because trees are the only harvest source live in the tree branch
    /// today, which keeps the loop verifiable before the metal ends land.
    /// </summary>
    internal static class HarvestReward
    {
        /// <summary>
        /// The yield table, pre-seeded with the live wood source and open for the
        /// nodes agent to register metal (and any other) sources into.
        /// </summary>
        private static readonly HarvestYield Yields = BuildDefaultYields();

        private static HarvestYield BuildDefaultYields()
        {
            HarvestYield yields = new HarvestYield();

            // Trees. The wood species IS the itemTypeId ("birch" wood -> "birch"
            // item), and one item per felled section is the natural rate.
            //
            // ALL EIGHT woods are registered, not just the one species the world
            // plants today. Retail gave every tree type its own wood
            // (worldsadrift.fandom.com/wiki/Wood), TreeSpecies has the recovered
            // per-prefab map, and each of the eight is a real "Wood"-category row in
            // itemData.json - so registering the whole set means a species placed
            // later pays the right wood the first time, instead of tripping the
            // "no harvest yield registered for source" warning in Award below.
            //
            // PLANT FIBRE AND BERRIES RIDE THE SAME CUT. The note that used to sit
            // here said berries were deliberately unregistered because daccatBerries
            // had no itemData.json row, every attribute of one would be invented,
            // and HarvestYield could not express a second yield off one source
            // anyway. All three of those have now been answered: the rows exist, the
            // ids and their display text are PROVED from Bossa's shipped quest data
            // rather than guessed, and AddYield is the missing shape. See TreeYield
            // for the evidence and for which numbers are still ours.
            foreach (string wood in TreeSpecies.Woods)
            {
                TreeYield.RegisterSpecies(yields, wood);
            }

            return yields;
        }

        /// <summary>
        /// Declares what a harvest source yields. The nodes agent's hook: called
        /// as a node kind is spawned/learned so a later <see cref="Award"/> for
        /// that key resolves. Returns true if the key was new. Idempotent enough
        /// to call on every spawn - re-registering the same rule is harmless.
        /// </summary>
        internal static bool Register(string sourceKey, YieldRule rule) => Yields.Register(sourceKey, rule);

        /// <summary>Whether a source key has a registered yield.</summary>
        internal static bool Knows(string sourceKey) => Yields.Has(sourceKey);

        /// <summary>
        /// Turns one harvest hit into yield: resolve the item, put it in the
        /// harvester's inventory (stacking onto an existing pile of the same
        /// material), and toast it.
        ///
        /// ORDER MATTERS. The grant is attempted first and the toast fires ONLY if
        /// the grant succeeded. A toast without a grant is the worst outcome in
        /// this whole area - the player is told "Salvaged Iron x12", looks in the
        /// panel, and it is not there - so the two are never allowed to disagree:
        /// the 8060 event reports the same amount the 1081 grant actually placed.
        /// </summary>
        /// <summary>
        /// Turns one hit on a KNOWN NODE into yield, at that node's own quality.
        ///
        /// THE ENTRY POINT EVERY NODE SOURCE MUST USE, and the reason it exists
        /// rather than being an optional argument on <see cref="Award"/>: quality
        /// is a property of the node, the yield table is keyed by the material
        /// name, and for as long as the two were separate arguments every single
        /// call site forgot the second one. Taking the node itself makes the
        /// omission impossible to spell - there is no shorter call that compiles.
        ///
        /// <see cref="Award"/> remains for sources that genuinely have no node:
        /// a tree (the species is the source, and wood carries no per-node
        /// quality) and a fuel canister (fuel is quality-EXEMPT in retail,
        /// acs/ScannableData.cs:325 excludes it explicitly - do not give it one).
        /// </summary>
        internal static void AwardFromNode(long harvesterEntityId, Multiplayer.MetalNode node,
            int units, string reason)
        {
            Award(harvesterEntityId, NodeYield.SourceKeyFor(node), units, reason,
                NodeYield.QualityOf(node));
        }

        internal static void Award(long harvesterEntityId, string sourceKey, int units, string reason,
            int? quality = null)
        {
            IReadOnlyList<YieldGrant> resolved = Yields.Resolve(sourceKey, units, quality);

            if (resolved.Count == 0)
            {
                // Either the source felled nothing, or - more useful to see - a
                // source the yield table was never taught about. Named here so it
                // is not an invisible no-op when the nodes agent adds a material.
                if (units > 0)
                {
                    Console.WriteLine("[warning] no harvest yield registered for source '" + sourceKey
                        + "' (" + reason + "); harvested nothing.");
                }
                return;
            }

            // ONE PUSH FOR THE WHOLE HIT, not one per material. 1081 is a
            // full-state component that is re-sent in its entirety and persisted on
            // every push, so a tree paying wood, fibre and berries would otherwise
            // cost three full inventory sends and three database writes for one
            // swing of the beam. The grants are suppressed individually and the
            // single push below states the finished inventory.
            List<YieldGrant> landed = new(resolved.Count);

            foreach (YieldGrant grant in resolved)
            {
                int? itemId = InventoryService.Grant(
                    harvesterEntityId,
                    grant.ItemTypeId,
                    grant.Amount,
                    grant.Quality,
                    push: false);

                if (itemId == null)
                {
                    // Grant refused: unknown type, or the inventory is full. The
                    // grant seam already logged which. No toast for an item that is
                    // not there - and, importantly, the OTHER yields of the same hit
                    // still land. A full grid should cost you the berries, not the
                    // wood.
                    continue;
                }

                landed.Add(grant);
            }

            if (landed.Count == 0)
            {
                return;
            }

            InventoryPush.Push(harvesterEntityId, "harvested " + landed.Count + " material(s) from " + reason);

            // Toasts AFTER the push, and only for what actually landed. The player
            // being told "Salvaged Plant Fiber x3" for something the panel does not
            // contain is the one outcome this whole path exists to prevent.
            foreach (YieldGrant grant in landed)
            {
                SalvageFeedback.Send(harvesterEntityId, grant.ItemTypeId, grant.Amount, reason);
            }
        }
    }
}
