using System;
using System.Collections;
using Bossa.Travellers.Biomes;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Mining
{
    /// <summary>
    /// Says out loud whether a metal deposit actually built its rock.
    ///
    /// Every failure mode in <c>MetalDepositVisualiser</c>'s chain is silent from the
    /// player's seat: an unresolved biome polls forever with no log
    /// (MetalDepositVisualiser.cs:151), a queued <c>LoadBalancing</c> action can sit
    /// behind island geometry indefinitely (:96-116), and an entity that goes away
    /// mid-queue leaves a promise that is never resolved OR rejected (:98). All three
    /// look identical from outside: an entity that exists, is streamed, has all its
    /// components, and draws nothing. That is precisely the state the players described
    /// as "there are still no metal nodes properly".
    ///
    /// This patch is pure DIAGNOSTICS. It changes no behaviour; it logs the deposit's
    /// entity id, its 1255 variantId and the biome resolved at its position when the
    /// visualiser starts, and then - once, after a grace period - whether the variant
    /// geometry ever appeared. With <see cref="DepositBiome_Patch"/> and
    /// <see cref="DepositVariant_Patch"/> also loaded, the resulting three lines are
    /// enough to tell biome failure, variant failure and load-balancer starvation
    /// apart from a client log alone.
    /// </summary>
    [HarmonyPatch(typeof(MetalDepositVisualiser), "OnVisualiserInit")]
    internal static class DepositRenderWatchdog_Patch
    {
        [HarmonyPostfix]
        public static void OnVisualiserInit_Postfix(MetalDepositVisualiser __instance)
        {
            try
            {
                if (__instance == null || __instance.gameObject == null)
                {
                    return;
                }
                if (__instance.gameObject.GetComponent<DepositRenderWatchdog>() != null)
                {
                    return;
                }
                __instance.gameObject.AddComponent<DepositRenderWatchdog>().Begin(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WAR][deposit] watchdog attach failed: " + ex.Message);
            }
        }
    }

    /// <summary>The per-deposit half of <see cref="DepositRenderWatchdog_Patch"/>.</summary>
    internal class DepositRenderWatchdog : MonoBehaviour
    {
        /// <summary>
        /// How long a deposit is allowed to take before "still nothing" is worth saying.
        /// Generous: the load balancer runs on a 1/30 s per-frame budget and deposits sit
        /// at <c>Priority.IslandColliderObjects</c>, behind island geometry, so a busy
        /// island stream can legitimately take many seconds.
        /// </summary>
        private const float GraceSeconds = 30f;

        internal void Begin(MetalDepositVisualiser visualiser)
        {
            StartCoroutine(WatchRoutine(visualiser));
        }

        private IEnumerator WatchRoutine(MetalDepositVisualiser visualiser)
        {
            long entityId = 0L;
            string variantId = "?";
            string biome = "?";

            try
            {
                entityId = gameObject.EntityId().Id;

                object state = AccessTools
                    .Field(typeof(MetalDepositVisualiser), "_state")
                    ?.GetValue(visualiser);
                if (state is Bossa.Travellers.Materials.MetalDepositStateReader reader)
                {
                    variantId = reader.VariantId ?? "(null)";
                }

                BiomeType? at = GlobalBiomeDataVisualizer.GetBiomeAt(transform.position);
                biome = at.HasValue ? at.Value.ToString() : "UNRESOLVED";
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WAR][deposit] watchdog could not read deposit state: " + ex.Message);
            }

            Debug.Log("[WAR][deposit] entity " + entityId + " init at " + transform.position
                + ": variantId='" + variantId + "', biome=" + biome + ".");

            // Poll at 4 Hz, not every frame: GetComponentInChildren walks the
            // child hierarchy, and this coroutine runs on EVERY deposit in the
            // world for up to GraceSeconds - per-frame polling made the whole
            // load-in window pay a hierarchy walk per deposit per frame. The
            // watchdog only exists to LOG a missing rock; 250 ms of detection
            // latency changes nothing it does.
            WaitForSeconds pollDelay = new WaitForSeconds(0.25f);
            float deadline = Time.realtimeSinceStartup + GraceSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (this == null || gameObject == null)
                {
                    yield break;
                }
                if (GetComponentInChildren<MetalDepositVisuals>() != null)
                {
                    Debug.Log("[WAR][deposit] entity " + entityId
                        + " built its rock (variant '" + variantId + "').");
                    yield break;
                }
                yield return pollDelay;
            }

            BiomeType? still = null;
            try
            {
                still = GlobalBiomeDataVisualizer.GetBiomeAt(transform.position);
            }
            catch (Exception)
            {
                // Reported below as UNRESOLVED.
            }

            Debug.LogWarning("[WAR][deposit] entity " + entityId + " has NO geometry after "
                + GraceSeconds + "s: variantId='" + variantId + "', biome now "
                + (still.HasValue ? still.Value.ToString() : "UNRESOLVED")
                + ", visualiser enabled=" + (visualiser != null && visualiser.enabled)
                + ". It is an invisible, un-shootable rock. If the biome is UNRESOLVED the "
                + "global entity's 1253/8064 never arrived; if a [WAR][deposit] variant "
                + "warning preceded this, the 1255 variantId is wrong; otherwise the "
                + "LoadBalancing queue is starved.");
        }
    }
}
