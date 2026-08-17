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

        /// <summary>Registration-key prefix for a placed shard. See <see cref="KeyForHost"/>.</summary>
        public const string KeyPrefix = "atlas-shard-";

        /// <summary>
        /// The registration key for the shard lodged in the deposit registered under
        /// <paramref name="hostDepositKey"/>: the prefix followed by THE HOST'S OWN KEY,
        /// verbatim.
        ///
        /// WHY THE HOST KEY AND NOT AN INDEX. There is more than one source of deposits.
        /// The static Haven table registers <c>deposit-0..N</c> at boot, but the real
        /// resource-spawn handshake registers deposits the CLIENT ground-checked, keyed
        /// <c>handshake-deposit-&lt;island&gt;-&lt;i&gt;</c>, at runtime. A shard key built from a
        /// bare integer can only ever name the first kind, so handshake-spawned deposits
        /// silently carried no shards at all. Embedding the host key makes
        /// <see cref="HostKeyOf"/> its exact inverse, so ANY deposit - whatever names it
        /// - can host a shard with no index arithmetic and no second lookup table.
        /// </summary>
        public static string KeyForHost(string hostDepositKey) => KeyPrefix + hostDepositKey;

        /// <summary>
        /// The registration key for the shard lodged in the STATIC Haven deposit at
        /// placement <paramref name="index"/> - i.e. <see cref="KeyForHost"/> of
        /// <c>deposit-N</c>. Convenience for the boot spawn plan only; the handshake
        /// path calls <see cref="KeyForHost"/> directly.
        /// </summary>
        public static string KeyFor(int index) => KeyForHost(MetalDeposits.KeyFor(index));

        /// <summary>Whether a registration key names a placed atlas shard.</summary>
        public static bool IsShardKey(string? key) =>
            key != null && key.StartsWith(KeyPrefix, System.StringComparison.Ordinal)
            && key.Length > KeyPrefix.Length;

        /// <summary>
        /// The DEPOSIT registration key a shard key names as its host - the exact
        /// inverse of <see cref="KeyForHost"/> - or null if the key is not a shard's.
        ///
        /// This is the ONE function the spawn seam needs to wire a shard to its rock:
        /// it works for the static table and the handshake spawner alike, because it
        /// never assumes the host is called <c>deposit-N</c>.
        /// </summary>
        public static string? HostKeyOf(string? key) =>
            IsShardKey(key) ? key!.Substring(KeyPrefix.Length) : null;

        /// <summary>
        /// The STATIC placement index a shard key belongs to, or null when the key is
        /// not a shard's or its host is not a <c>deposit-N</c> from the static table (a
        /// handshake-spawned host has no index). Logging and tests only - the spawn seam
        /// uses <see cref="HostKeyOf"/>.
        /// </summary>
        public static int? IndexOf(string? key)
        {
            string? host = HostKeyOf(key);
            if (host == null || !MetalDeposits.IsDepositKey(host))
            {
                return null;
            }
            return int.TryParse(host.Substring(MetalDeposits.KeyPrefix.Length), out int index)
                   && index >= 0
                ? index
                : (int?)null;
        }

        /// <summary>
        /// The DEPOSIT registration key a shard at a STATIC placement index is lodged
        /// in. Retained for the boot plan; prefer <see cref="HostKeyOf"/>, which is
        /// source-agnostic.
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
        /// Metres above the host deposit's own position to seed the lodged shard's
        /// 190602. ZERO - the shard's ENTITY sits exactly where the deposit does.
        ///
        /// WHY ZERO, AND WHY THIS USED TO BE 1.5. A lodged shard's WORLD PLACEMENT is
        /// not a number any server can compute. The retail alignment is
        ///     MetalDepositAtlasVisualiser_fsim.AttachSlot(coreVisualiser.Visuals
        ///         .ScrapSlots[_state.SlotId])  ->  transform.AlignTo(slot.transform)
        /// (MetalDepositAtlasVisualiser_fsim.cs:78, 130-133), where ScrapSlots are
        /// authored child transforms of the CORE PREFAB the client imports at runtime
        /// (MetalDepositCoreVisuals.ScrapSlots, MetalDepositVisuals.Init ->
        /// _coreSlot.Reference.Import(_corePrefab)). Their offsets exist only inside the
        /// variant asset, and that class is [WorkerType(WorkerPlatform.UnityWorker)] -
        /// the client build never gets it (MetalDepositAtlasPreprocessor.cs:9-16 adds
        /// only MetalDepositAtlasVisualiser_client, which does NOT align anything).
        ///
        /// So the old 1.5 m guess was the bug the player reported as "a shard on the
        /// floor / floating in the air": an invented offset that had no relation to the
        /// rock's core. It is replaced by two things that ARE correct:
        ///   - this ZERO, so the entity (and therefore the interaction range the 1210
        ///     prompt measures) is centred on the rock the shard belongs to; and
        ///   - a CLIENT-SIDE port of the retail AttachSlot in the WorldsAdriftReborn
        ///     mod (Patching/Mining/AtlasShardLodging), which parents the shard's view
        ///     to ScrapSlots[slotId] of the host core - the only place the slot
        ///     transform actually exists.
        ///
        /// Overridable with <c>WAREBORN_ATLAS_LODGE_OFFSET</c> (metres, may be negative)
        /// purely as a live-tuning escape hatch if a capture ever shows the entity wants
        /// to sit off-centre.
        /// </summary>
        public const double DefaultLodgedHeightOffsetMetres = 0.0;

        /// <summary>
        /// The lodged height offset from <c>WAREBORN_ATLAS_LODGE_OFFSET</c> or
        /// <see cref="DefaultLodgedHeightOffsetMetres"/>. A garbled value falls back to
        /// the default rather than throwing during spawn.
        /// </summary>
        public static double LodgedHeightOffsetMetres(string? env)
        {
            if (!string.IsNullOrWhiteSpace(env)
                && double.TryParse(env.Trim(), System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out double m))
            {
                return m;
            }
            return DefaultLodgedHeightOffsetMetres;
        }

        /// <summary>
        /// The 190602 position seed for a shard lodged in a deposit at
        /// <paramref name="depositPosition"/>: the deposit's own position, offset by
        /// <see cref="LodgedHeightOffsetMetres(string)"/>. Pure, so the registration and
        /// any test compute the same coordinate.
        /// </summary>
        public static FixedPointPosition LodgedPositionFor(
            FixedPointPosition depositPosition, double offsetMetres)
        {
            return new FixedPointPosition(
                depositPosition.X,
                depositPosition.Y + (long)(offsetMetres * FixedPointPosition.UnitsPerMetre),
                depositPosition.Z);
        }

        /// <summary>
        /// <see cref="LodgedPositionFor(FixedPointPosition,double)"/> at the configured
        /// offset, read from the environment. The one call the spawn plan makes.
        /// </summary>
        public static FixedPointPosition LodgedPositionFor(FixedPointPosition depositPosition) =>
            LodgedPositionFor(depositPosition,
                LodgedHeightOffsetMetres(
                    System.Environment.GetEnvironmentVariable("WAREBORN_ATLAS_LODGE_OFFSET")));

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
