using Framework.Promise;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Ship
{
    /// <summary>
    /// Keeps the generated hull's virtual deck as placement/aboard trigger geometry
    /// without drawing it beneath the real Deck01 entities.
    ///
    /// Retail creates both surfaces from the same ShipDeck polygon. MeshGenerator's
    /// virtual deck is explicitly converted to a trigger; the separately replicated
    /// ShipDeckVisualizer creates the solid, material-bearing floor. Drawing both
    /// coplanar meshes produces the photographed alternating/flickering floor panels.
    /// Removing the virtual renderers preserves its trigger, mesh and placement
    /// semantics while leaving the real wooden/metal deck as the sole visible floor.
    /// </summary>
    [HarmonyPatch(typeof(MeshGenerator), "MakeVirtualDeck")]
    internal static class VirtualDeckPresentation_Patch
    {
        private static void Postfix(MeshGenerator __instance, IPromise<GameObject> __result)
        {
            if (__instance == null || __result == null || !WorldsAdrift.IsClient)
            {
                return;
            }

            __result.Then(delegate(GameObject virtualDeck)
            {
                if (virtualDeck == null)
                {
                    return;
                }

                Renderer[] renderers = virtualDeck.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].enabled = false;
                }
            });
        }
    }
}
