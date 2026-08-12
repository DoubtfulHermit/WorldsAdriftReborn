using Assets.Visualizers;
using Bossa.Prototype.Character.Observer;
using HarmonyLib;

namespace WorldsAdriftReborn.Patching.Knowledge
{
    // Relog shipbuilding-awareness fix (client-side only; no server fix possible).
    //
    // On relog the server pushes the authoritative KnowledgeServerState (KnowledgeNodeUses,
    // including ["Shipbuilding"] > 0), but the crafting/shipyard interaction gate reads a
    // stale UI-event cache instead: InteractAgentObserver._isShipBuildingAware.
    //
    // That flag is only written from the WAUIPlayerProfileEvents.UpdateShipbuildingAware
    // event, whose sole producer is KnowledgeManagerScreen.UpdateTutorialStartPopups() -
    // fired at screen init and on a node-use CHANGE while the knowledge screen is open.
    // WAEventSystem is not sticky and the screen unsubscribes when closed, so after a fresh
    // login the flag stays at its default false until the player buys a new node with the
    // tree open. Result: interacting with a crafting table / shipyard wrongly shows the
    // "need knowledge" chat message and refuses, until a live purchase retroactively wakes
    // everything up.
    //
    // Fix (Option A - stateless, timing-proof): OR the live server-authoritative state into
    // the gate evaluation. HasCraftingStationButUseForbidden(interactable, isShipBuildingAware)
    // is the single entry point of the gate (called from InteractAgentObserver.CheckInteraction),
    // and it fully delegates to the recursive Transform overload passing the same bool, so
    // forcing the argument true here covers every branch. We read awareness directly from
    // LocalPlayer.Instance.scanningAgentVisualizer.UnlockedLifetimeNodes, which reads
    // KnowledgeNodeUses["Shipbuilding"] > 0 off the bound KnowledgeServerState reader - the
    // same authoritative source, available from bind. This can never drift out of sync.
    //
    // Null/early-boot safety: if the local player or its scanning visualizer isn't ready yet
    // (or anything throws), we leave isShipBuildingAware untouched and fall back to the game's
    // original behavior - never throw out of the prefix.
    [HarmonyPatch(typeof(InteractAgentObserver), nameof(InteractAgentObserver.HasCraftingStationButUseForbidden),
        new[] { typeof(InteractiveObjectVisualizer), typeof(bool) })]
    internal static class ShipbuildingAware_Patch
    {
        [HarmonyPrefix]
        public static void HasCraftingStationButUseForbidden_Prefix(ref bool isShipBuildingAware)
        {
            // Already aware via the UI-event cache: nothing to correct.
            if (isShipBuildingAware)
            {
                return;
            }

            try
            {
                // LocalPlayer.Exists guards both Instance != null and its visualizers being ready.
                if (!LocalPlayer.Exists)
                {
                    return;
                }

                ScanningAgentVisualizer scanning = LocalPlayer.Instance.scanningAgentVisualizer;
                if (scanning == null)
                {
                    return;
                }

                if (scanning.UnlockedLifetimeNodes)
                {
                    isShipBuildingAware = true;
                }
            }
            catch
            {
                // Any unexpected early-boot state: fall back to the original behavior.
            }
        }
    }
}
