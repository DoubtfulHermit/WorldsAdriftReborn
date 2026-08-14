using WorldsAdriftRebornGameServer.Multiplayer.Crafting;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    public enum ShipPartSalvageReject
    {
        Accept,
        NotCraftedPart,
        OutsideOwnedShipyard,
        UnknownRecipe,
    }

    public readonly record struct ShipPartSalvageRefund(string ItemTypeId, int Amount);

    /// <summary>Pure rules for dismantling one mounted part with the salvage beam.</summary>
    public static class ShipPartSalvagePolicy
    {
        /// <summary>Radius around an owned shipyard in which part dismantling is allowed.</summary>
        public const double WorkRadiusMetres = 15.0;

        public static ShipPartSalvageReject Evaluate(bool craftedPart,
            bool insideOwnedShipyard, bool recipeKnown)
        {
            if (!craftedPart) return ShipPartSalvageReject.NotCraftedPart;
            if (!insideOwnedShipyard) return ShipPartSalvageReject.OutsideOwnedShipyard;
            if (!recipeKnown) return ShipPartSalvageReject.UnknownRecipe;
            return ShipPartSalvageReject.Accept;
        }

        /// <summary>
        /// Full recipe refund, aggregated by concrete material id. The recovered catalogue
        /// mostly names concrete ids; its generic categories use the materials currently
        /// available in the Wareborn economy.
        /// </summary>
        public static IReadOnlyList<ShipPartSalvageRefund> Refunds(SchematicRecord recipe)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            var totals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (CraftingRequirement requirement in recipe.CraftingRequirements)
            {
                if (requirement.AmountRequired <= 0) continue;
                string itemType = ConcreteMaterial(requirement.Name);
                if (string.IsNullOrWhiteSpace(itemType)) continue;
                totals[itemType] = totals.TryGetValue(itemType, out int current)
                    ? current + requirement.AmountRequired
                    : requirement.AmountRequired;
            }
            return totals.Select(x => new ShipPartSalvageRefund(x.Key, x.Value)).ToArray();
        }

        private static string ConcreteMaterial(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            if (name.Equals("Metal", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Wood/Metal", StringComparison.OrdinalIgnoreCase)) return "iron";
            if (name.Equals("Wood", StringComparison.OrdinalIgnoreCase)) return "birch";
            if (name.Equals("Fuel", StringComparison.OrdinalIgnoreCase)) return "fuel";
            return name;
        }
    }
}
