using System;
using System.Reflection;
using Bossa.Travellers.Craftingstation;
using Bossa.Travellers.CraftingStation;
using Bossa.Travellers.Materials;
using HarmonyLib;
using Travellers.UI.PlayerInventory;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Interactions
{
    /*
     * CRAFTING-UI STUCK-BLOCKER DIAGNOSIS + SELF-HEAL.
     *
     * Symptom: after a station craft completes, reopening the Assembly Station
     * UI renders but eats every click until relog.
     *
     * Root mechanism (decompile): the schematic list's _inputBlocker activates
     * while SchematicList.IsCurrentlyCrafting() is true, i.e. while ANY of
     *   !_allSlotsAreEmptyLocally
     *   !CraftingStationData.AllSlotsAreEmptyRemotely
     *   CraftingStationData.CraftingInProgress
     * holds (SchematicList.cs:71, CraftingStationSchematicList.cs:306).
     * CraftingInProgress is set true by OnCraftingStarted -> StartCrafting()
     * and cleared ONLY by OnCraftingCompleted -> FinishCrafting()
     * (CraftingStationBehaviour.cs:172/189, CraftingStationData.cs:218/225).
     * The 1005 completion update runs its field callbacks (clientSchematicId,
     * slottedMaterials, itemReadyInSeconds) BEFORE the transient
     * CraftingCompleted event, so an exception in any of them - especially
     * CraftingStationData.SyncCraftingItems, which indexes slottedMaterials[i]
     * once per loaded recipe requirement without a length check
     * (CraftingStationData.cs:274/285) - kills the CraftingCompleted delivery
     * and latches CraftingInProgress true for the rest of the login.
     *
     * The server-side padding fix is deployed but the bug still reproduced, so
     * the actual culprit exception is unconfirmed. This patch does two things:
     *
     * DIAGNOSIS - name the culprit in the log:
     *  - Finalizer on CraftingStationData.SyncCraftingItems: log any throw
     *    with the wire slot count vs the requirement count, then SWALLOW it so
     *    the CraftingCompleted event queued behind it still delivers. If the
     *    wire list is short (the known culprit shape), re-run the sync once
     *    with a locally padded copy so AllSlotsAreEmptyRemotely is recomputed.
     *  - Postfixes on OnCraftingStarted / OnCraftingCompleted: one log line
     *    per transition with the schematic id and the resulting flag.
     *  - Postfix on OnStartInteraction (the UI-open seam - it binds the
     *    station data, pushes the ItemCraft UI state and refreshes, see
     *    CraftingStationBehaviour.cs:209): log the three blocker predicates.
     *
     * SELF-HEAL - unblock without a relog, on UI open only:
     *  - Never while a craft is genuinely running (itemReadyInSeconds >= 0 on
     *    the station's own 1005 reader; itemReadyInSeconds < 0 is the game's
     *    "no live craft" signal, CraftingStationBehaviour.cs:151-163).
     *  - Stale CraftingInProgress -> call the game's own FinishCrafting()
     *    (CraftingStationData.cs:225). Its body is: progress=1, reset the
     *    timer display, flip the flag, fire the WAUICraftingEvents
     *    .CraftingCompleted UI event - exactly the retail completion path,
     *    and that event is what re-runs UpdateCraftingState and drops the
     *    _inputBlocker (CraftingUI.cs:66/182, CraftingStationCraftingUI
     *    .CraftingFinished -> _craftingStationSchematicList.CraftingFinished).
     *    Only caveat: SetupCraftingTimerAndDisplay dereferences
     *    LoadedSchematic, so when no schematic object is resolved (async
     *    lookup still pending) we fall back to clearing the flag through its
     *    private setter instead of NRE-ing.
     *  - Stale local slot amounts (local non-empty while the wire says every
     *    slot is empty) -> CraftingStationData.DestroyItems(), the same call
     *    the client's own reset path uses (CraftingUI.ResetCraftingState,
     *    CraftingUI.cs:373). Its CurrentAmount=0 writes raise the game's own
     *    CraftingSlotEmptyUpdated events.
     *  - After any heal, fire WAUICraftingEvents.SlottedMaterialsUpdated with
     *    the station data - the exact event a normal wire sync fires
     *    (CraftingStationData.cs:325) - so the open screen re-evaluates the
     *    blocker (CraftingUI.SlottedMaterialsUpdated ->
     *    UpdateSchematicListState -> UpdateCraftingState).
     *
     * Internal members are reached via AccessTools (the mod's established
     * pattern). Any reflection gap or unexpected throw logs once and disables
     * the patch for the session instead of spamming or breaking the UI.
     * Registration: pure [HarmonyPatch] classes, auto-picked by the plugin's
     * Harmony.CreateAndPatchAll.
     */
    internal static class CraftUiSelfHeal
    {
        // AccessTools returns null (never throws) on a missing member, so these
        // are safe as static initializers even if the assembly drifts.
        internal static readonly FieldInfo StateField =
            AccessTools.Field(typeof(CraftingStationBehaviour), "_state");
        internal static readonly FieldInfo LoadedSchematicField =
            AccessTools.Field(typeof(CraftingStationData), "_loadedSchematic");
        internal static readonly MethodInfo CraftingInProgressSetter =
            AccessTools.PropertySetter(typeof(CraftingStationData), "CraftingInProgress");
        internal static readonly FieldInfo SchematicDataTemplateField =
            AccessTools.Field(typeof(SchematicList), "_craftingDataTemplate");
        internal static readonly FieldInfo SchematicLocalSlotsEmptyField =
            AccessTools.Field(typeof(SchematicList), "_allSlotsAreEmptyLocally");
        internal static readonly FieldInfo SchematicCurrentStateField =
            AccessTools.Field(typeof(SchematicList), "_currentState");
        internal static readonly FieldInfo SchematicInputBlockerField =
            AccessTools.Field(typeof(SchematicList), "_inputBlocker");

        private static bool _disabled;

        internal static bool Disabled
        {
            get { return _disabled; }
        }

        internal static void Disable(string where, Exception e)
        {
            if (_disabled)
            {
                return;
            }
            _disabled = true;
            Debug.LogWarning("[WAR][craft] self-heal patch disabled after error in " + where + ": " + e);
        }

        internal static void DisableMissingMember(string member)
        {
            if (_disabled)
            {
                return;
            }
            _disabled = true;
            Debug.LogWarning("[WAR][craft] self-heal patch disabled: reflection target missing (" + member + ")");
        }

        /// <summary>
        /// Reads the station's own 1005 reader (CraftingStationBehaviour._state)
        /// for the live wire truth: itemReadyInSeconds and slottedMaterials.
        /// Returns false when the reader is not bound (station not checked out)
        /// - in that case the caller must not heal, because it cannot prove
        /// there is no live countdown.
        /// </summary>
        internal static bool TryReadWireState(CraftingStationBehaviour behaviour,
            out int itemReadyInSeconds, out Improbable.Collections.List<SlottedMaterial> slottedMaterials)
        {
            itemReadyInSeconds = int.MinValue;
            slottedMaterials = null;
            if (StateField == null)
            {
                DisableMissingMember("CraftingStationBehaviour._state");
                return false;
            }
            CraftingStationClientState.Reader reader =
                StateField.GetValue(behaviour) as CraftingStationClientState.Reader;
            if (reader == null)
            {
                return false;
            }
            CraftingStationClientStateData data = reader.Data;
            itemReadyInSeconds = data.itemReadyInSeconds;
            slottedMaterials = data.slottedMaterials;
            return true;
        }

        /// <summary>
        /// The local-side half of the blocker: true when every local slot has
        /// no material amount (mirrors what feeds SchematicList
        /// ._allSlotsAreEmptyLocally through CraftingSlotEmptyUpdated).
        /// </summary>
        internal static bool AllLocalSlotsEmpty(CraftingStationData data)
        {
            System.Collections.Generic.List<CraftingSlotData> slots = data.CraftingSlotData;
            if (slots == null)
            {
                return true;
            }
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null && !slots[i].IsEmpty)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// True when the wire list says no slot holds anything - the same
        /// emptiness rule SyncCraftingItems applies (amount > 0 or a present
        /// customizationMaterial marks a slot non-empty).
        /// </summary>
        internal static bool WireSlotsAllEmpty(Improbable.Collections.List<SlottedMaterial> wire)
        {
            if (wire == null)
            {
                return false; // cannot verify - never clear local slots blind
            }
            for (int i = 0; i < wire.Count; i++)
            {
                if (wire[i].amount > 0 || wire[i].customizationMaterial.HasValue)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Reconciles the state which ACTUALLY eats schematic-list clicks. The
        /// list keeps its own cached local-slot flag, current-state enum and
        /// blocker GameObject; none of those are represented by
        /// CraftingStationData. In particular SchematicList.ChangeState is a
        /// no-op when the enum already equals FreeToUse, so a blocker left
        /// active by an earlier callback can survive even though every model
        /// predicate is clean. Run only after the wire proves the craft idle.
        /// </summary>
        internal static void ReconcileIdleSchematicList(CraftingStationData data)
        {
            if (SchematicDataTemplateField == null
                || SchematicLocalSlotsEmptyField == null
                || SchematicCurrentStateField == null
                || SchematicInputBlockerField == null)
            {
                DisableMissingMember("SchematicList UI state fields");
                return;
            }

            CraftingStationSchematicList[] lists =
                Resources.FindObjectsOfTypeAll<CraftingStationSchematicList>();
            int matches = 0;
            for (int i = 0; i < lists.Length; i++)
            {
                CraftingStationSchematicList list = lists[i];
                if (list == null
                    || !object.ReferenceEquals(SchematicDataTemplateField.GetValue(list), data))
                {
                    continue;
                }

                matches++;
                bool cachedLocalEmpty = (bool)SchematicLocalSlotsEmptyField.GetValue(list);
                CraftingState state = (CraftingState)SchematicCurrentStateField.GetValue(list);
                GameObject blocker = SchematicInputBlockerField.GetValue(list) as GameObject;
                bool blockerActive = blocker != null && blocker.activeSelf;

                Debug.Log("[WAR][craft] schematic-list actual state: cachedLocalSlotsEmpty="
                    + cachedLocalEmpty + " currentState=" + state
                    + " inputBlockerActive=" + (blocker != null ? blockerActive.ToString() : "null"));

                // The wire and CraftingStationData are idle at the only call
                // site. Make the list's hidden copy agree, then ask retail to
                // derive FreeToUse. ChangeState may deliberately do nothing
                // when its enum is already FreeToUse, so enforce the resulting
                // presentation too: an idle station must not have a click
                // shield, regardless of how the enum and GameObject diverged.
                if (!cachedLocalEmpty)
                {
                    SchematicLocalSlotsEmptyField.SetValue(list, true);
                }
                list.UpdateCraftingState();
                if (blocker != null && blocker.activeSelf)
                {
                    blocker.SetActive(false);
                    Debug.Log("[WAR][craft] SELF-HEAL: disabled stale schematic input blocker on idle UI open");
                }
            }

            if (matches == 0)
            {
                Debug.LogWarning("[WAR][craft] no CraftingStationSchematicList was bound to the opened station data");
            }
        }
    }

    // ------------------------------------------------------------------
    // DIAGNOSIS 1: the suspected killer of the CraftingCompleted delivery.
    // A finalizer (NOT a prefix replacement) so the original body still runs;
    // if it threw we log the culprit and swallow, so the transient event
    // processed right after this field callback still reaches
    // OnCraftingCompleted -> FinishCrafting.
    // ------------------------------------------------------------------
    [HarmonyPatch(typeof(CraftingStationData), nameof(CraftingStationData.SyncCraftingItems))]
    internal static class CraftUiSelfHeal_SyncCraftingItems_Patch
    {
        private static bool _resyncing;

        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, CraftingStationData __instance,
            Improbable.Collections.List<SlottedMaterial> __0)
        {
            if (__exception == null)
            {
                return null;
            }
            if (CraftUiSelfHeal.Disabled)
            {
                return __exception; // stock behavior once we are switched off
            }
            try
            {
                int wireCount = (__0 != null) ? __0.Count : -1;
                int requirementCount = (__instance != null && __instance.CraftingSlotData != null)
                    ? __instance.CraftingSlotData.Count
                    : -1;
                Debug.LogWarning("[WAR][craft] SyncCraftingItems THREW: " + __exception
                    + ", wire slot count=" + wireCount
                    + ", requirement count=" + requirementCount);

                // Known culprit shape: the wire list is shorter than the loaded
                // recipe's requirement list (CraftingStationData.cs:285 indexes
                // per requirement). Re-run the sync once with a padded copy so
                // AllSlotsAreEmptyRemotely is recomputed instead of staying
                // stale. Padding uses materialTypeId="" which SyncCraftingItems
                // treats as an empty slot (amount write only).
                if (!_resyncing && __0 != null && requirementCount > wireCount)
                {
                    _resyncing = true;
                    try
                    {
                        Improbable.Collections.List<SlottedMaterial> padded =
                            new Improbable.Collections.List<SlottedMaterial>();
                        for (int i = 0; i < __0.Count; i++)
                        {
                            padded.Add(__0[i]);
                        }
                        for (int i = __0.Count; i < requirementCount; i++)
                        {
                            padded.Add(new SlottedMaterial(i,
                                new RawMaterial(string.Empty, 0, string.Empty,
                                    new Improbable.Collections.Map<string, string>()),
                                0,
                                default(Improbable.Collections.Option<RawMaterial>)));
                        }
                        __instance.SyncCraftingItems(padded);
                        Debug.Log("[WAR][craft] re-synced slots with a padded wire list ("
                            + wireCount + " -> " + requirementCount + ")");
                    }
                    catch (Exception retryError)
                    {
                        Debug.LogWarning("[WAR][craft] padded re-sync also failed: " + retryError);
                    }
                    finally
                    {
                        _resyncing = false;
                    }
                }
            }
            catch (Exception e)
            {
                CraftUiSelfHeal.Disable("SyncCraftingItems finalizer", e);
            }
            // Swallow: the queued CraftingCompleted event behind this field
            // callback must still deliver so FinishCrafting can run.
            return null;
        }
    }

    // ------------------------------------------------------------------
    // DIAGNOSIS 2: one line per state-machine transition.
    // Base-class patches also cover StoveVisualizer / LoomVisualizer - their
    // overrides call base.OnCraftingStarted/Completed, which IS this method.
    // ------------------------------------------------------------------
    [HarmonyPatch(typeof(CraftingStationBehaviour), "OnCraftingStarted")]
    internal static class CraftUiSelfHeal_CraftingStarted_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(CraftingStationBehaviour __instance, CraftingStarted __0)
        {
            if (CraftUiSelfHeal.Disabled)
            {
                return;
            }
            try
            {
                CraftingStationData data = __instance.CraftingStationData;
                Debug.Log("[WAR][craft] started schematic=" + __0.schematicId
                    + " CraftingInProgress=" + (data != null ? data.CraftingInProgress.ToString() : "null")
                    + " readyInSeconds=" + __0.readyInSeconds);
            }
            catch (Exception e)
            {
                CraftUiSelfHeal.Disable("OnCraftingStarted postfix", e);
            }
        }
    }

    [HarmonyPatch(typeof(CraftingStationBehaviour), "OnCraftingCompleted")]
    internal static class CraftUiSelfHeal_CraftingCompleted_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(CraftingStationBehaviour __instance, CraftingCompleted __0)
        {
            if (CraftUiSelfHeal.Disabled)
            {
                return;
            }
            try
            {
                CraftingStationData data = __instance.CraftingStationData;
                Debug.Log("[WAR][craft] completed schematic=" + __0.schematicId
                    + " CraftingInProgress=" + (data != null ? data.CraftingInProgress.ToString() : "null"));
            }
            catch (Exception e)
            {
                CraftUiSelfHeal.Disable("OnCraftingCompleted postfix", e);
            }
        }
    }

    // ------------------------------------------------------------------
    // DIAGNOSIS 3 + SELF-HEAL: the UI-open seam. OnStartInteraction is the
    // method that (for the local player, in radius) binds the station data,
    // pushes the crafting UI state and calls RefreshCraftingData - so a
    // postfix sees exactly the state the just-opened screen evaluated.
    // ------------------------------------------------------------------
    [HarmonyPatch(typeof(CraftingStationBehaviour), "OnStartInteraction")]
    internal static class CraftUiSelfHeal_UiOpen_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(CraftingStationBehaviour __instance, PlayerStartCrafting __0)
        {
            if (CraftUiSelfHeal.Disabled)
            {
                return;
            }
            try
            {
                CraftingStationData data = __instance.CraftingStationData;
                if (data == null || !LocalPlayer.Exists)
                {
                    return;
                }
                // Same guard as the original body: only the local player's own
                // open (a foreign player's PlayerStartCrafting event reaches
                // every checked-out copy of this station).
                if (__0.playerId != LocalPlayer.Instance.EntityId)
                {
                    return;
                }

                int itemReadySeconds;
                Improbable.Collections.List<SlottedMaterial> wireSlots;
                bool haveWire = CraftUiSelfHeal.TryReadWireState(__instance, out itemReadySeconds, out wireSlots);
                bool localSlotsEmpty = CraftUiSelfHeal.AllLocalSlotsEmpty(data);

                Debug.Log("[WAR][craft] UI open: localSlotsEmpty=" + localSlotsEmpty
                    + " AllSlotsAreEmptyRemotely=" + data.AllSlotsAreEmptyRemotely
                    + " CraftingInProgress=" + data.CraftingInProgress
                    + " itemReadyInSeconds=" + (haveWire ? itemReadySeconds.ToString() : "unknown"));

                if (!haveWire)
                {
                    return; // cannot prove there is no live countdown - never heal blind
                }
                if (itemReadySeconds >= 0)
                {
                    return; // a craft is genuinely running - the blocker is correct
                }

                bool healed = false;

                // Stale local slot display: local amounts non-empty while the
                // wire says every slot is empty. Clear them the way the
                // client's own reset does (CraftingUI.ResetCraftingState ->
                // DestroyItems), never by hand-nulling internals.
                if (!localSlotsEmpty && CraftUiSelfHeal.WireSlotsAllEmpty(wireSlots))
                {
                    data.DestroyItems();
                    Debug.Log("[WAR][craft] SELF-HEAL: cleared stale local slot amounts on UI open");
                    healed = true;
                }

                // The latch itself: CraftingInProgress stuck true with no live
                // countdown. Prefer the game's own FinishCrafting() - it flips
                // the flag AND fires the CraftingCompleted UI event that
                // re-runs UpdateCraftingState. Its only hazard is
                // SetupCraftingTimerAndDisplay dereferencing LoadedSchematic,
                // so fall back to the bare setter when no schematic object is
                // resolved yet.
                if (data.CraftingInProgress)
                {
                    bool schematicResolved = CraftUiSelfHeal.LoadedSchematicField != null
                        && CraftUiSelfHeal.LoadedSchematicField.GetValue(data) != null;
                    if (schematicResolved)
                    {
                        data.FinishCrafting();
                        Debug.Log("[WAR][craft] SELF-HEAL: cleared stale CraftingInProgress on UI open (FinishCrafting route)");
                    }
                    else
                    {
                        if (CraftUiSelfHeal.CraftingInProgressSetter == null)
                        {
                            CraftUiSelfHeal.DisableMissingMember("CraftingStationData.CraftingInProgress setter");
                            return;
                        }
                        CraftUiSelfHeal.CraftingInProgressSetter.Invoke(data, new object[] { false });
                        Debug.Log("[WAR][craft] SELF-HEAL: cleared stale CraftingInProgress on UI open (flag-only route, schematic not resolved)");
                    }
                    healed = true;
                }

                if (healed)
                {
                    // Re-evaluate the blocker the same way a normal wire sync
                    // does: CraftingUI listens for this event and runs
                    // UpdateSlotStates + UpdateSchematicListState (->
                    // SchematicList.UpdateCraftingState) + CheckCraftButtonState.
                    Singleton<WAEventSystem>.Instance.TriggerEvent(
                        WAUICraftingEvents.SlottedMaterialsUpdated, data);
                }

                // The first live self-heal build proved these model values can
                // all be clean while the screen still eats clicks. The actual
                // shield is owned by CraftingStationSchematicList and has its
                // own cached local-empty flag/state enum. Only reconcile when
                // BOTH model and wire prove the craft fully idle.
                if (CraftUiSelfHeal.AllLocalSlotsEmpty(data)
                    && data.AllSlotsAreEmptyRemotely
                    && !data.CraftingInProgress
                    && CraftUiSelfHeal.WireSlotsAllEmpty(wireSlots))
                {
                    CraftUiSelfHeal.ReconcileIdleSchematicList(data);
                }
            }
            catch (Exception e)
            {
                CraftUiSelfHeal.Disable("OnStartInteraction postfix", e);
            }
        }
    }
}
