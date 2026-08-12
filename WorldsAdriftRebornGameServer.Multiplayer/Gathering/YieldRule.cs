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
        public YieldRule(string itemTypeId, int amountPerUnit, int quality = 0)
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

            ItemTypeId = itemTypeId;
            AmountPerUnit = amountPerUnit;
            Quality = quality;
        }

        /// <summary>The inventory item type this source drops. Must exist in the item database.</summary>
        public string ItemTypeId { get; }

        /// <summary>How many items each felled unit is worth. At least one.</summary>
        public int AmountPerUnit { get; }

        /// <summary>The quality the granted item carries. 0 for plain materials.</summary>
        public int Quality { get; }
    }
}
