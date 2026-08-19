namespace WorldsAdriftRebornGameServer.Multiplayer.Gathering
{
    /// <summary>
    /// EVERYTHING A TREE GIVES, not just its wood.
    ///
    /// Retail's trees paid three materials off one beam, and this is Bossa's own
    /// tutorial saying so rather than a wiki claim. From the shipped quest data
    /// (docs/research/loop/data/quests.json):
    ///
    ///   :1918  "A Torch will help you light up dark caves. To craft one you will
    ///           need Cloth and Wood, both of which can be salvaged from trees."
    ///   :2762  "Daccat Berries are the most basic form of food, and can be
    ///           salvaged from tree trunks and branches."
    ///
    /// Both quest steps carry highlight ["Tree"] (:1935-1937, :1948-1950), so the
    /// tutorial arrow literally pointed at a tree entity for the fibre step. The
    /// resource audit had this as INFERRED from the community wiki; it is PROVED.
    ///
    /// THE VISIBLE FRUIT ON A TREE IS NOT THE BERRY SOURCE. TreePreprocessor forces
    /// fruit to Layers.IgnoreRaycast on the server (:52-57) and DestroyImmediates its
    /// collider on the client (:80-84). Fruit is decoration plus a FoodSourceType for
    /// CREATURE feeding AI. Berries came off the beam, alongside the wood. Do not
    /// build a fruit-picking interaction.
    ///
    /// ONE BERRY, NOT ONE PER BIOME. Exactly one berry identifier exists in the
    /// whole shipped build, and exactly one raw berry icon (foods/2x2_berries); the
    /// other three berry icons are cooked products. The 22 biome-suffixed icons
    /// retail did ship are all CREATURE materials - meat, chitin, neural clusters,
    /// conductive vessels. The biome axis is real and belongs on creatures; there is
    /// no evidence it was ever on plants, so no per-biome berry is invented here.
    ///
    /// Pure: ids and rates. No I/O.
    /// </summary>
    public static class TreeYield
    {
        /// <summary>
        /// PROVED, and not an icon-derived guess. `plantFiber` is a verbatim
        /// itemCategory in shipped quest data - quest-conditions.json:74,82 and
        /// quests.json:1933,2506 carry
        /// HaveItemByCategory{itemCategory:"plantFiber", requiredQuantities:[15]}.
        /// Its display name, "Plant Fiber(s)", is verbatim shipped UI text
        /// (quests.json:1924,2137,2230,2235,2499).
        /// </summary>
        public const string PlantFiberItemTypeId = "plantFiber";

        /// <summary>
        /// PROVED. The client's collect-SFX table maps it to the PlantsVegetation
        /// sound (acs/Travellers.UI.PlayerInventory/InventoryContents.cs:55), and
        /// the shipped quest data uses it as itemIdToKeep / itemIdToLookFor with the
        /// asset names ConsumeItem-daccatBerries and ItemPresent-daccatBerries
        /// (quest-conditions.json:161-262).
        /// </summary>
        public const string DaccatBerriesItemTypeId = "daccatBerries";

        /// <summary>
        /// Plant fibre per felled tree section. **WAREBORN TUNING**, with a proved
        /// anchor.
        ///
        /// Retail's real rate did not survive: it lived in the server-authored
        /// RawMaterialSourceStateData.amount, which the client only displayed, and
        /// TreeFSimStateData.resourcePerSection has zero client readers. The one
        /// number that IS shipped is what the tutorial asks for in a single early
        /// step - 15 Plant Fibers alongside 20 Wood (quests.json:1924 and :1946) -
        /// i.e. a designed ratio of 0.75 fibre per wood.
        ///
        /// We pay 1 wood per section, so faithful would be 3 fibre per 4 sections.
        /// This rounds it up to 1, for two reasons worth stating rather than
        /// hiding: a fractional per-section rate would make a small tree pay no
        /// fibre at all, which reads as the feature being broken; and this project
        /// is deliberately generous where retail was thin.
        /// </summary>
        public const int PlantFiberPerSection = 1;

        /// <summary>
        /// Berries per felled tree section. **WAREBORN TUNING** - no rate for
        /// berries survives anywhere, in any form.
        ///
        /// One per section, matched to fibre, because the only shipped constraint on
        /// berries is a health threshold rather than a count: the tutorial gates on
        /// Quests.EatBerriesHealthThreshold = 0.35 (acs/ConfigDefaults.cs:89), so
        /// what matters is that a wounded player can reach a tree and eat, not that
        /// any particular number falls out of it.
        /// </summary>
        public const int BerriesPerSection = 1;

        /// <summary>
        /// Fibre and berries are QUALITY-EXEMPT. Retail's quality scale is a
        /// property of metals and woods in crafting slots; nothing in the shipped
        /// build gives a plant material a quality, and inventing one would create a
        /// floor that no recipe was written against.
        /// </summary>
        public const int PlantQuality = YieldRule.QualityExempt;

        /// <summary>The rule that pays plant fibre off one section of any tree.</summary>
        public static YieldRule PlantFiberRule() =>
            new YieldRule(PlantFiberItemTypeId, PlantFiberPerSection, PlantQuality);

        /// <summary>The rule that pays berries off one section of any tree.</summary>
        public static YieldRule BerriesRule() =>
            new YieldRule(DaccatBerriesItemTypeId, BerriesPerSection, PlantQuality);

        /// <summary>
        /// Declares a wood species and its two secondary yields, in the order the
        /// player should see them toasted: the wood the tree is named for first.
        ///
        /// EVERY species gets fibre and berries, not just the one Haven plants.
        /// Retail's fibre and berry steps name no species and the tutorial simply
        /// says "trees", so a species-specific plant yield would be an invention -
        /// and the failure mode of forgetting one is silent, which is the failure
        /// mode this whole module is written against.
        /// </summary>
        public static void RegisterSpecies(HarvestYield yields, string wood)
        {
            if (yields == null)
            {
                throw new ArgumentNullException(nameof(yields));
            }
            if (string.IsNullOrWhiteSpace(wood))
            {
                throw new ArgumentException("a tree yields a named wood", nameof(wood));
            }

            yields.Register(wood, new YieldRule(wood, amountPerUnit: 1));
            yields.AddYield(wood, PlantFiberRule());
            yields.AddYield(wood, BerriesRule());
        }
    }
}
