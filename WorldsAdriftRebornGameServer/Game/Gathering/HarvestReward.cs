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
            foreach (string wood in TreeSpecies.Woods)
            {
                yields.Register(wood, new YieldRule(wood, amountPerUnit: 1));
            }

            // DACCAT BERRIES ARE DELIBERATELY NOT REGISTERED, and this is the note
            // for whoever comes to add them. Retail's trees dropped edible daccat
            // berries alongside the logs (worldsadrift.fandom.com/wiki/Trees), and
            // "daccatBerries" is unquestionably a real retail item id - it survives
            // in the client's own harvest-SFX table, mapped to the PlantsVegetation
            // sound (acs/Travellers.UI.PlayerInventory/InventoryContents.cs:55).
            //
            // But it is NOT a row in itemData.json (395 items, zero matches), and
            // that file is the catalogue this server serves as reference data - so
            // registering the id today would resolve to an item the client's
            // database has never heard of. Adding the row is possible, and it would
            // not crash (InventoryIconManager falls back to placeholder_icon on an
            // unknown icon), but every attribute of it - display name, description,
            // grid size, icon, the health it restores - would be INVENTED, and
            // nothing in the decompile constrains any of them. The only tree/food
            // link that survives is FoodSourceVisualizer setting
            // FoodSourceType.TreeFruit, and that is the CREATURE feeding system
            // (namespace Bossa.Travellers.Creatures.Food), not the player's harvest.
            //
            // So this is left as a reported gap rather than a fabricated item. The
            // wiring itself is one line once a real row exists: berries would be a
            // SECOND yield off the same cut, which is a shape HarvestYield does not
            // have yet (one source key resolves to one rule).

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
        internal static void Award(long harvesterEntityId, string sourceKey, int units, string reason)
        {
            YieldGrant? resolved = Yields.Resolve(sourceKey, units);

            if (resolved == null)
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

            YieldGrant grant = resolved.Value;

            int? itemId = InventoryService.Grant(
                harvesterEntityId,
                grant.ItemTypeId,
                grant.Amount,
                grant.Quality);

            if (itemId == null)
            {
                // Grant refused: unknown type, or the inventory is full. The push
                // seam already logged which. No toast for an item that is not
                // there.
                return;
            }

            SalvageFeedback.Send(harvesterEntityId, grant.ItemTypeId, grant.Amount, reason);
        }
    }
}
