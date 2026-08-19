namespace WorldsAdriftRebornGameServer.Multiplayer.Gathering
{
    /// <summary>
    /// What one harvest source yields per unit removed.
    ///
    /// A harvest "source" is a wood species (a tree), or a metal node kind, or
    /// anything else a tool bites into. The KEY that selects a rule is a plain
    /// string - "birch", "iron" - because that is the only fact both ends of the
    /// seam already agree on: the tree loop knows the wood species it just felled
    /// (<c>TreeSectionMaskChange.WoodType</c>), and a metal node knows the
    /// material it is made of. Neither has to learn the other's entity ids.
    ///
    /// <see cref="ItemTypeId"/> is what actually lands in the inventory, and it
    /// is separate from the source key on purpose: for wood the two happen to be
    /// equal ("birch" wood grants the "birch" item), but a metal node kind and
    /// the metal item it drops need not share a spelling, and the client NREs on
    /// an itemTypeId its item database has never heard of - so the granted id is
    /// stated explicitly rather than assumed to equal the key.
    ///
    /// Pure: no game types, no I/O. It is validated on construction so a bad rule
    /// is a loud throw at registration time, not a silent zero-yield harvest that
    /// looks like the whole loop is broken.
    /// </summary>
    public sealed record YieldRule
    {
        /// <summary>The bottom of retail's quality scale. See <see cref="Quality"/>.</summary>
        public const int MinQuality = 1;

        /// <summary>The top of retail's quality scale. See <see cref="Quality"/>.</summary>
        public const int MaxQuality = 10;

        /// <summary>
        /// The quality a material carries when it is OUTSIDE the scale rather than
        /// at the bottom of it. Fuel is the proved case: retail excludes it from
        /// quality explicitly (acs/ScannableData.cs:325).
        /// </summary>
        public const int QualityExempt = 0;

        public YieldRule(string itemTypeId, int amountPerUnit, int quality = QualityExempt)
        {
            if (string.IsNullOrEmpty(itemTypeId))
            {
                throw new ArgumentException("a yield rule with no itemTypeId grants an item the client cannot look up", nameof(itemTypeId));
            }
            if (amountPerUnit < 1)
            {
                // A zero or negative per-unit amount is the silent failure this
                // whole module exists to make impossible: harvesting would appear
                // to work, fire its animation and its toast, and grant nothing.
                throw new ArgumentOutOfRangeException(nameof(amountPerUnit), amountPerUnit,
                    "a harvest that yields fewer than one item per unit is not a harvest");
            }
            if (quality != QualityExempt && (quality < MinQuality || quality > MaxQuality))
            {
                // Out of range in EITHER direction is a thrown mistake rather than a
                // clamp, because both directions were live defects. Retail's scale is
                // 1..10 and it is a FLOOR in a crafting slot
                // (ShipBlueprintBuild.Matches: quality < required.Quality is a refusal),
                // so a negative or an 11 is a slot nothing can ever satisfy, and it
                // fails as "the recipe is broken" rather than as "the number is wrong".
                throw new ArgumentOutOfRangeException(nameof(quality), quality,
                    "quality is retail's 1..10 scale, or " + QualityExempt + " for a quality-exempt material like fuel");
            }

            ItemTypeId = itemTypeId;
            AmountPerUnit = amountPerUnit;
            Quality = quality;
        }

        /// <summary>The inventory item type this source drops. Must exist in the item database.</summary>
        public string ItemTypeId { get; }

        /// <summary>How many items each felled unit is worth. At least one.</summary>
        public int AmountPerUnit { get; }

        /// <summary>
        /// The quality the granted item carries by DEFAULT - retail's 1..10 scale,
        /// or <see cref="QualityExempt"/>.
        ///
        /// "By default" is the important word, and it is why
        /// <see cref="HarvestYield.Resolve"/> takes a per-hit override. Quality is a
        /// property of the NODE, not of the material: two iron deposits on the same
        /// island routinely carry different qualities, and this table is keyed by the
        /// material NAME. So a rule's quality can only ever be the right answer for a
        /// world where every node of a material is identical. Where a node is known,
        /// its own quality must be handed to Resolve, or the last node registered
        /// silently decides what every node of that metal pays out.
        /// </summary>
        public int Quality { get; }
    }
}
