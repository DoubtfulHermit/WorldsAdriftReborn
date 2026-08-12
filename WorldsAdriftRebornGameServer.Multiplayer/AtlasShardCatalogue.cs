namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The static facts about an ATLAS SHARD as a world entity - the REAL retail
    /// acquisition object, as opposed to the placeholder metal nugget. A shard is a
    /// distinct <c>MetalDepositAtlas</c> entity LODGED in a numbered slot of a metal
    /// deposit's core (2102 LodgeableState + 1305 MetalDepositAtlasShardState). When
    /// the core is destroyed the shard is released into the world; the player then
    /// performs an ordinary PickUp interaction (1211), and the server grants an
    /// inventory item (1081) and removes the world shard. See
    /// docs/research/findings-atlas-shards.md.
    ///
    /// This module holds the constants a shard needs; the live per-shard state
    /// (lodged/released/collected, reservation) lives in <see cref="AtlasShardRegistry"/>
    /// and the pickup rules in <see cref="AtlasPickupPolicy"/>. Pure: no ENet, no
    /// Improbable types, no game install, so every value here is asserted on natively
    /// in the test suite rather than by staring at a client.
    /// </summary>
    public static class AtlasShardCatalogue
    {
        /// <summary>
        /// The shard prefab name on the wire, BARE.
        ///
        /// VERIFIED: <c>MetalDepositAtlas</c> is line 158 of
        /// docs/research/loop/data/prefab-names.tsv with BOTH the client and worker
        /// columns "yes". Its client visualiser (MetalDepositAtlasVisualiser_client)
        /// imports an authored <c>_atlasShardPrefab</c> via MetalDepositAtlasView, so
        /// the entity displays a real shard model. Sent bare for the same reason the
        /// deposit and nugget are: the client appends the worker suffix itself
        /// (WorkerSpecificPrefabName), so a "_unityclient" suffix would be doubled and
        /// resolve to nothing.
        /// </summary>
        public const string AssetName = "MetalDepositAtlas";

        // ==================================================================
        // THE ONE VALUE THAT IS NOT IN THE DECOMPILE.
        // ==================================================================

        /// <summary>
        /// The inventory <c>itemTypeId</c> the player receives when a shard is
        /// collected - routed through <c>InventoryService.Grant</c> in the pickup
        /// transaction, and the material id the Atlas Sky Core / Enhancer / Lifter
        /// recipes consume.
        ///
        /// RECONSTRUCTED, not retail. The retail id is unrecoverable: component 1305
        /// carries no item type (only rockCoreId + slotId), and the exhaustive refdata
        /// hunt found no <c>atlasShard</c>/<c>scrapItem-atlas*</c> row anywhere on disk
        /// (docs/research/findings-atlas-refdata.md #1) - WA served item/schematic
        /// refdata from the now-dead servers, never in the client depot. Because the
        /// servers are gone, the row was DEFINED for the revival rather than left
        /// pending: <c>atlasShard</c> is a real <c>itemData.json</c> row (Metal,
        /// stack 10, rarity 3), so <c>InventoryService.Grant</c> now accepts it and the
        /// pickup COMPLETES. <c>scrapItem-atlashod</c> (Atlas Hod) is a deliberate
        /// non-choice - it is an unrelated salvage tool the recipe dump used as a
        /// placeholder (findings-atlas-refdata #3), never shipped as the shard.
        /// </summary>
        public const string ItemTypeId = "atlasShard";

        /// <summary>
        /// Whether <see cref="ItemTypeId"/> is still a placeholder rather than a real
        /// grantable <c>itemData.json</c> row. False now that the reconstructed
        /// <c>atlasShard</c> row exists - kept so the pickup transaction can still emit
        /// a "no row" hint if the id is ever pointed at a missing type again.
        /// </summary>
        public static bool IsItemIdPending =>
            ItemTypeId.EndsWith("PENDING_REFDATA", System.StringComparison.Ordinal);

        // ==================================================================
        // Identity + deposit pairing.
        // ==================================================================

        /// <summary>Registration-key prefix for a placed shard. See <see cref="KeyFor"/>.</summary>
        public const string KeyPrefix = "atlas-shard-";

        /// <summary>The registration key for the shard lodged in deposit index N.</summary>
        public static string KeyFor(int index) => KeyPrefix + index;

        /// <summary>Whether a registration key names a placed atlas shard.</summary>
        public static bool IsShardKey(string? key) =>
            key != null && key.StartsWith(KeyPrefix, System.StringComparison.Ordinal);

        /// <summary>
        /// The placement index for a shard key ("atlas-shard-N"), or null if the key
        /// is not a shard's or carries no parseable index. Deterministic, so the spawn
        /// seam can recover a shard's host deposit from its key alone.
        /// </summary>
        public static int? IndexOf(string? key)
        {
            if (!IsShardKey(key))
            {
                return null;
            }
            return int.TryParse(key!.Substring(KeyPrefix.Length), out int index) && index >= 0
                ? index
                : (int?)null;
        }

        /// <summary>
        /// The DEPOSIT registration key a shard at a given index is lodged in. A shard
        /// pairs one-to-one with the deposit of the SAME index (atlas-shard-0 lodges in
        /// deposit-0), so the shard's 1305 rockCoreId and the deposit's 2103
        /// attachedEntities can be wired from the key alone at spawn time.
        /// </summary>
        public static string HostDepositKeyFor(int index) => MetalDeposits.KeyFor(index);

        // ==================================================================
        // Slot + placement.
        // ==================================================================

        /// <summary>
        /// The 1305 slotId (and the index into the core visual's <c>ScrapSlots</c> the
        /// client's fsim aligns the shard to). 0 is the first slot; a deposit core
        /// carries several, but one lodged shard is the retail acquisition case this
        /// vertical builds. VERIFIED field: MetalDepositAtlasShardStateData.slotId is a
        /// plain int (gencode Bossa.Travellers.Materials/MetalDepositAtlasShardStateData.cs:6-16).
        /// </summary>
        public const int DefaultSlotId = 0;

        /// <summary>
        /// The 2102 LodgeableState.slotName. Empty: the client fsim indexes the core's
        /// slots by the 1305 <c>slotId</c> INT (ScrapSlots[slotId]), not by this
        /// string, and no client reader gates on it - it drives only a
        /// SlotNameUpdated callback nothing subscribes to. The Data struct copies it
        /// by value, so empty (not null) is the correct benign value.
        /// </summary>
        public const string SlotName = "";

        /// <summary>
        /// Metres above the host deposit's own position to place the lodged shard, so
        /// on the stock client (whose UnityClient visualiser renders the shard at its
        /// 190602 position and does NOT run the UnityWorker slot-alignment fsim) the
        /// shard reads as sitting in the core rather than buried inside it. An
        /// APPROXIMATION: the retail slot transform is a prefab fact not in the
        /// decompile (findings §5), so the exact lodged offset is a live-capture item.
        /// </summary>
        public const double LodgedHeightOffsetMetres = 1.5;

        /// <summary>
        /// The 190602 position seed for a shard lodged in a deposit at
        /// <paramref name="depositPosition"/>: the deposit's own position raised by
        /// <see cref="LodgedHeightOffsetMetres"/>. Pure, so the registration and any
        /// test compute the same coordinate.
        /// </summary>
        public static FixedPointPosition LodgedPositionFor(FixedPointPosition depositPosition)
        {
            return new FixedPointPosition(
                depositPosition.X,
                depositPosition.Y + (long)(LodgedHeightOffsetMetres * FixedPointPosition.UnitsPerMetre),
                depositPosition.Z);
        }

        // ==================================================================
        // Interaction prompt sizing (1210). Reuses the nugget's PickUp values -
        // both are an "E to pick up" on a small ground object, and the retail
        // radius/timeToUse are not recoverable (findings §5), so the nugget's
        // measured-feel values are the honest reuse rather than a second invention.
        // ==================================================================

        /// <summary>The 1210 PickUp interaction radius for a released shard, metres.</summary>
        public const float PickUpRadius = MetalNodes.PickUpRadius;

        /// <summary>The 1210 PickUp interaction hold time for a released shard, seconds.</summary>
        public const float PickUpTimeToUse = MetalNodes.PickUpTimeToUse;
    }
}
