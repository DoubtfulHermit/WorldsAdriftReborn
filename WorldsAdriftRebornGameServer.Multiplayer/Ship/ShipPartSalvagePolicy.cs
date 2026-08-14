using WorldsAdriftRebornGameServer.Multiplayer.Crafting;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    public enum ShipPartSalvageReject
    {
        Accept,
        NotMounted,
        ShipNotDocked,
        DockMismatch,
        NotShipyardOwner,
        UnknownRecipe,
    }

    public readonly record struct ShipPartSalvageRefund(string ItemTypeId, int Amount);

    /// <summary>Pure rules for dismantling one mounted part with the salvage beam.</summary>
    public static class ShipPartSalvagePolicy
    {
        public static ShipPartSalvageReject Evaluate(bool mounted, long hullEntityId,
            long shipyardEntityId, long yardDockedHullEntityId, bool ownsShipyard,
            bool recipeKnown)
        {
            if (!mounted || hullEntityId <= 0) return ShipPartSalvageReject.NotMounted;
            if (shipyardEntityId <= 0) return ShipPartSalvageReject.ShipNotDocked;
            if (yardDockedHullEntityId != hullEntityId) return ShipPartSalvageReject.DockMismatch;
            if (!ownsShipyard) return ShipPartSalvageReject.NotShipyardOwner;
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
