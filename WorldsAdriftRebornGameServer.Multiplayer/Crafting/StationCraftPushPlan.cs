namespace WorldsAdriftRebornGameServer.Multiplayer.Crafting
{
    /// <summary>
    /// The shape of ONE crafting-state (1005) push for a timed station craft: WHICH entity
    /// it lands on, the served countdown, which events fire, and - the regression fix - HOW
    /// MANY SlottedMaterial entries the wire list must carry.
    ///
    /// WHY THE SLOT COUNT MATTERS (the "stuck in the crafting animation forever" bug): the
    /// client's <c>CraftingStationBehaviour.OnCraftingCompleted</c> calls
    /// <c>RefreshCraftingData -&gt; CraftingStationData.SyncCraftingItems</c>, which loops
    /// <c>for i in [0, CraftingSlotData.Count)</c> - CraftingSlotData being sized from the
    /// still-loaded schematic's craftingRequirements - and indexes
    /// <c>gsimSlottedMaterials[i]</c>. If the COMPLETE push carries an EMPTY slotted list
    /// (the old code emptied the session before pushing), that index throws
    /// IndexOutOfRange and the throw aborts OnCraftingCompleted BEFORE
    /// <c>FinishCrafting()</c> + <c>StopVfxAtomizer()</c> run - so the aperture never
    /// closes, the atomizer keeps sparking and the station stays busy/uninteractable. The
    /// COMPLETE push must therefore still emit ONE SlottedMaterial per requirement (zeroed),
    /// exactly as START does; the fields are otherwise all default and harmless.
    /// </summary>
    public readonly struct CraftingStatePush
    {
        /// <summary>The entity whose 1005 the active station UI reads (the STATION for a placed-station craft).</summary>
        public long Target { get; }

        /// <summary>The served countdown: a POSITIVE hold at START, <see cref="StationCraftPushPlan.ClosedCountdown"/> at COMPLETE.</summary>
        public int ItemReadyInSeconds { get; }

        /// <summary>Whether this push fires CraftingStarted (opens the atomizer / begins the countdown).</summary>
        public bool CraftingStarted { get; }

        /// <summary>Whether this push fires CraftingCompleted (closes the aperture, stops the atomizer, unlocks the station).</summary>
        public bool CraftingCompleted { get; }

        /// <summary>How many SlottedMaterial entries the wire list must carry - one per recipe requirement, at START AND COMPLETE.</summary>
        public int SlotCount { get; }

        /// <summary>
        /// The clientSchematicId this push must carry on the station's 1005. It is the SAME
        /// crafted recipe at START AND COMPLETE, deliberately: see the class remarks
        /// ("the second wedge"). The COMPLETE push must NOT blank it to "".
        /// </summary>
        public string SchematicId { get; }

        public CraftingStatePush(long target, int itemReadyInSeconds, bool craftingStarted, bool craftingCompleted, int slotCount, string schematicId)
        {
            Target = target;
            ItemReadyInSeconds = itemReadyInSeconds;
            CraftingStarted = craftingStarted;
            CraftingCompleted = craftingCompleted;
            SlotCount = slotCount;
            SchematicId = schematicId ?? "";
        }
    }

    /// <summary>
    /// Pure plan for the two 1005 pushes of a timed station craft - START (hold the aperture
    /// open) and COMPLETE (close it, stop the atomizer, unlock the station) - so the handler
    /// cannot drift the two apart. The invariants the client depends on:
    ///
    ///   * START and COMPLETE push to the SAME target (the station), or the animation the
    ///     START opened on the station never gets its COMPLETE and the station wedges;
    ///   * COMPLETE serves <see cref="ClosedCountdown"/> (-1) so
    ///     <c>OnItemReadyInSecondsUpdated</c> closes the aperture, AND fires CraftingCompleted;
    ///   * BOTH pushes carry one SlottedMaterial per requirement - a shorter (empty) COMPLETE
    ///     list throws IndexOutOfRange in the client's SyncCraftingItems and aborts
    ///     OnCraftingCompleted before it can stop the animation (see <see cref="CraftingStatePush"/>).
    ///   * BOTH pushes carry the SAME crafted <c>schematicId</c> - the COMPLETE push must NOT
    ///     blank clientSchematicId to "". This is THE SECOND WEDGE ("craft one, then every
    ///     schematic click leaves the detail on 'Choose a schematic to craft'"): the station
    ///     UI's schematic list raises its input blocker whenever <c>IsCurrentlyCrafting()</c>
    ///     is true, and that is <c>CraftingInProgress || !AllSlotsAreEmptyRemotely ||
    ///     !allSlotsEmptyLocally</c> (SchematicList.cs). <c>AllSlotsAreEmptyRemotely</c> is only
    ///     recomputed by <c>CraftingStationData.SyncCraftingItems</c>, which EARLY-RETURNS when
    ///     <c>!HasLoadedSchematic</c>. If the COMPLETE push blanks clientSchematicId, the
    ///     client's <c>ClientSchematicIdUpdated("")</c> fires <c>LoadSchematic(null)</c> and can
    ///     clear the loaded schematic in the same update that finishes the craft; SyncCraftingItems
    ///     then skips the recompute and <c>AllSlotsAreEmptyRemotely</c> stays FALSE from the start
    ///     push - so the input blocker never lifts and every later schematic click is eaten. By
    ///     keeping the SAME schematicId on both pushes, the completion's ClientSchematicIdUpdated
    ///     never fires (no change), <c>HasLoadedSchematic</c> stays true through the completion,
    ///     SyncCraftingItems always recomputes <c>AllSlotsAreEmptyRemotely = true</c>, and the
    ///     station returns to a fully interactive idle. The server session is still idled
    ///     (CraftSession.ReturnToIdle) so the NEXT open/select starts clean; only the WIRE keeps
    ///     the recipe visible. Deterministic - independent of SpatialOS callback ordering.
    ///
    /// Dependency-free (longs, ints and a string id), so it unit-tests natively - no game
    /// install, no wire.
    /// </summary>
    public static class StationCraftPushPlan
    {
        /// <summary>
        /// The served countdown that tells the client the craft is DONE and the aperture must
        /// shut. -1 (not 0): the client's stop condition is <c>itemReadyInSeconds &lt; 0</c>
        /// (CraftingStationBehaviour.OnItemReadyInSecondsUpdated), and a bare 0 is the
        /// protobuf int default that would drop off the wire entirely.
        /// </summary>
        public const int ClosedCountdown = -1;

        /// <summary>
        /// The START push: hold the aperture open for <paramref name="seconds"/> (a positive
        /// served countdown) and fire CraftingStarted, with one slot per requirement so the
        /// station shows the filled slots while it works, carrying the crafted
        /// <paramref name="schematicId"/> on clientSchematicId.
        /// </summary>
        public static CraftingStatePush Start(long target, int requirementCount, int seconds, string schematicId) =>
            new CraftingStatePush(target, seconds, craftingStarted: true, craftingCompleted: false, slotCount: requirementCount, schematicId: schematicId);

        /// <summary>
        /// The COMPLETE push: close the aperture (<see cref="ClosedCountdown"/>) and fire
        /// CraftingCompleted, STILL carrying one slot per requirement - the fix for the
        /// stuck-animation IndexOutOfRange - AND STILL carrying the SAME crafted
        /// <paramref name="schematicId"/> as <see cref="Start"/> (never "") - the fix for the
        /// "detail blank after one craft" wedge (see class remarks). Same target as
        /// <see cref="Start"/>.
        /// </summary>
        public static CraftingStatePush Complete(long target, int requirementCount, string schematicId) =>
            new CraftingStatePush(target, ClosedCountdown, craftingStarted: false, craftingCompleted: true, slotCount: requirementCount, schematicId: schematicId);
    }
}
