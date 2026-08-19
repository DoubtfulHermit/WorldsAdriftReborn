using WorldsAdriftRebornGameServer.Multiplayer.Crafting;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    public enum ShipPartSalvageReject
    {
        Accept,
        NotCraftedPart,
        OutsideOwnedShipyard,
        UnknownRecipe,

        /// <summary>
        /// The part is a ship storage container and something is still inside it.
        /// Dismantling destroys the entity, and its inventory dies with it - so this
        /// refusal is the only thing standing between a player and losing whatever
        /// they stowed. It is a REJECT rather than a "drop the contents on the deck"
        /// because this server has no loose-item-on-the-ground entity to drop them
        /// into; a refusal the player can act on beats a silent deletion they cannot.
        /// </summary>
        ContainerNotEmpty,
    }

    public readonly record struct ShipPartSalvageRefund(string ItemTypeId, int Amount);

    /// <summary>Pure rules for dismantling one mounted part with the salvage beam.</summary>
    public static class ShipPartSalvagePolicy
    {
        /// <summary>Radius around an owned shipyard in which part dismantling is allowed.</summary>
        public const double WorkRadiusMetres = 15.0;

        /// <summary>
        /// Whether this shot may dismantle the part.
        ///
        /// <paramref name="containerHoldsItems"/> has NO default on purpose. Ship
        /// storage became real state the moment the four container rows started
        /// serving 1081, and a caller that forgets to ask "is anything in it?"
        /// silently deletes a player's belongings with no log and no tell. A
        /// required parameter makes that omission a compile error instead of a
        /// green test suite - the strongest guard available for wiring that no
        /// unit test can reach.
        /// </summary>
        public static ShipPartSalvageReject Evaluate(bool craftedPart,
            bool insideOwnedShipyard, bool recipeKnown, bool containerHoldsItems)
        {
            if (!craftedPart) return ShipPartSalvageReject.NotCraftedPart;
            if (!insideOwnedShipyard) return ShipPartSalvageReject.OutsideOwnedShipyard;
            if (!recipeKnown) return ShipPartSalvageReject.UnknownRecipe;
            // LAST, so a container refusal is only ever reported for a shot that
            // would otherwise have succeeded. Reported first it would tell a player
            // standing nowhere near their shipyard the wrong reason.
            if (containerHoldsItems) return ShipPartSalvageReject.ContainerNotEmpty;
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
