using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Materials
{
    /// <summary>
    /// WHAT ONE SHIP IS MADE OF: the wood its frame is built from, the metal its
    /// fittings are built from, and the quality of each.
    ///
    /// Retail modelled this per COMPONENT SLOT (a cannon's four slots are Casing /
    /// Barrel / AmmoLoader / FiringMechanism, and <c>ModularCannon</c> reads
    /// <c>materialDefinitions[0..3]</c> positionally). A hull is simpler: the
    /// client's <c>ComponentMaterialColors.SetMaterials</c> buckets the whole list
    /// into wood and metal and uses the FIRST of each - one dominant wood and one
    /// dominant metal is all the visuals can express. So that is what this records,
    /// and it is exactly what a mass calculation needs too.
    ///
    /// Both halves are OPTIONAL, because a hull may be all-wood or all-metal. At
    /// least one is always present after <see cref="OrLegacy"/>.
    /// </summary>
    public sealed class HullMaterials
    {
        public HullMaterials(string? woodId, int woodQuality, string? metalId, int metalQuality)
        {
            WoodId = Normalise(woodId, MaterialCategory.Wood);
            MetalId = Normalise(metalId, MaterialCategory.Metal);
            WoodQuality = Clamp(woodQuality);
            MetalQuality = Clamp(metalQuality);
        }

        /// <summary>The dominant wood's itemTypeId ("birch"), or null for an all-metal hull.</summary>
        public string? WoodId { get; }

        /// <summary>The dominant metal's itemTypeId ("iron"), or null for an all-wood hull.</summary>
        public string? MetalId { get; }

        /// <summary>Quality 1..10 of the wood; 1 when unknown.</summary>
        public int WoodQuality { get; }

        /// <summary>Quality 1..10 of the metal; 1 when unknown.</summary>
        public int MetalQuality { get; }

        public ShipMaterial? Wood => MaterialCatalog.Find(WoodId);

        public ShipMaterial? Metal => MaterialCatalog.Find(MetalId);

        public bool IsEmpty => WoodId == null && MetalId == null;

        /// <summary>
        /// THE MIGRATION RULE. A hull persisted before materials were recorded gets
        /// the materials the server used to hardcode for it - birch frame, iron
        /// fittings (Deck.MaterialTypeId = Trees.WoodType = "birch";
        /// ShipPartSalvagePolicy mapped "Metal" -> "iron"). So an old hull is not
        /// defaulted to a guess, it is restated as what it has always actually been,
        /// and it keeps flying with the mass it always had.
        /// </summary>
        public HullMaterials OrLegacy() =>
            IsEmpty
                ? new HullMaterials(MaterialCatalog.LegacyWoodId, 1, MaterialCatalog.LegacyMetalId, 1)
                : this;

        /// <summary>The legacy pair, for a record that never carried materials at all.</summary>
        public static HullMaterials Legacy =>
            new HullMaterials(MaterialCatalog.LegacyWoodId, 1, MaterialCatalog.LegacyMetalId, 1);

        /// <summary>
        /// What a set of consumed materials says the ship is made of: the wood and
        /// the metal that the most of went into it, ties broken by first appearance
        /// so the result is deterministic. Items that are not ship materials (fuel,
        /// atlas shards) are ignored rather than rejected.
        ///
        /// This is how the CHOSEN material gets recorded on the output: the craft
        /// already knows exactly which <c>InventoryItem</c>s were reserved, it simply
        /// never asked them what they were.
        /// </summary>
        public static HullMaterials FromConsumed(IEnumerable<(string ItemTypeId, int Amount, int Quality)> consumed)
        {
            if (consumed == null)
            {
                return new HullMaterials(null, 1, null, 1);
            }

            var woodTotals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var metalTotals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            var quality = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach ((string itemTypeId, int amount, int itemQuality) in consumed)
            {
                ShipMaterial? material = MaterialCatalog.Find(itemTypeId);
                if (material == null || amount <= 0)
                {
                    continue;
                }

                Dictionary<string, int> bucket = material.IsWood ? woodTotals : metalTotals;
                if (!bucket.ContainsKey(material.Id))
                {
                    bucket[material.Id] = 0;
                    order.Add(material.Id);
                }
                bucket[material.Id] += amount;

                // Best quality seen for that material wins - a player who put one
                // Q9 plank in among Q2 planks built a slightly better ship, not a
                // worse one.
                if (!quality.TryGetValue(material.Id, out int best) || itemQuality > best)
                {
                    quality[material.Id] = itemQuality;
                }
            }

            string? wood = Dominant(woodTotals, order);
            string? metal = Dominant(metalTotals, order);

            return new HullMaterials(
                wood, wood != null && quality.TryGetValue(wood, out int wq) ? wq : 1,
                metal, metal != null && quality.TryGetValue(metal, out int mq) ? mq : 1);
        }

        private static string? Dominant(Dictionary<string, int> totals, List<string> order)
        {
            string? best = null;
            int bestAmount = 0;
            // Walk in first-appearance order so an exact tie is broken deterministically.
            for (int i = 0; i < order.Count; i++)
            {
                if (totals.TryGetValue(order[i], out int amount) && amount > bestAmount)
                {
                    best = order[i];
                    bestAmount = amount;
                }
            }
            return best;
        }

        private static string? Normalise(string? id, string expectedCategory)
        {
            ShipMaterial? material = MaterialCatalog.Find(id);
            // Reject a wood id offered as the metal (and vice versa) rather than
            // storing a contradiction a later mass calculation would trust.
            return material != null && material.Category == expectedCategory ? material.Id : null;
        }

        private static int Clamp(int quality) => quality < 1 ? 1 : (quality > 10 ? 10 : quality);

        public override string ToString() =>
            (WoodId ?? "-") + " Q" + WoodQuality + " / " + (MetalId ?? "-") + " Q" + MetalQuality;
    }
}
