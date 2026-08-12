using System;
using System.Collections.Generic;
using System.Text;
using Bossa.Travellers.Biomes;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Mining
{
    /// <summary>
    /// Makes a metal deposit's VARIANT LOOKUP survivable, and - more importantly -
    /// makes its failure VISIBLE.
    ///
    /// THE PROBLEM. <c>MetalDepositVisualiser</c> imports the rock's geometry at
    /// runtime from the id in its 1255 <c>MetalDepositState.variantId</c>:
    ///
    ///     AssetVariant&lt;string, MetalDepositVisuals&gt; variant =
    ///         SharedResourceData.MetalDepositVariant(biome, variantId);   // :128
    ///     if (variant == null)
    ///         return promise.Reject(new MissingReferenceException(...));  // :129-132
    ///
    /// and the reject lands in a <c>.Catch</c> that does <c>base.enabled = false</c>
    /// (:47-51). Underneath, <c>SharedResourceData.FetchMetalDepositVariants</c> THROWS
    /// <c>MissingReferenceException</c> outright when the biome has no PropLibrary
    /// (SharedResourceData.cs:80-83), and that throw happens inside a
    /// <c>LoadBalancing.Execute</c> delegate whose promise is then never resolved OR
    /// rejected - so the deposit hangs half-initialised, with no geometry and no
    /// collider, but still "enabled".
    ///
    /// Either way the player gets an INVISIBLE, UNSHOOTABLE rock. Only the second case
    /// leaves the deposit's core/crust visualisers enabled, which is exactly the state
    /// that lets a lodged atlas shard render on its own next to nothing - the "shard
    /// floating with no rock" the players reported.
    ///
    /// WHAT THIS DOES.
    ///   1. LOGS every lookup that fails, with the biome, the requested id, and the
    ///      COMPLETE list of ids the biome's PropLibrary actually contains. That list
    ///      is not in the decompile - it lives in the shipped assets - so this is the
    ///      only way to learn the real ids without guessing.
    ///   2. Swallows the "no variant container for this biome" throw so it degrades to
    ///      a null result (a clean, logged, disabled visualiser) rather than a dangling
    ///      promise.
    ///   3. Falls back to the FIRST variant the biome offers when the requested id does
    ///      not resolve, so a wrong id in the server's 1255 is a differently-shaped rock
    ///      rather than no rock. The first entry - not <c>RandomMetalDepositVariant</c> -
    ///      because every client must pick the SAME rock: a random per-client variant
    ///      would give two players different geometry, different colliders and
    ///      different core slots for one shared entity, and an atlas shard lodged in
    ///      slot N would sit somewhere else for each of them.
    ///
    /// The fallback is a safety net, not a licence to keep a wrong id: the log line it
    /// prints names the ids to put in <c>MetalDeposits.DefaultVariantId</c> /
    /// <c>WAREBORN_DEPOSIT_VARIANT</c>.
    /// </summary>
    [HarmonyPatch(typeof(SharedResourceData), nameof(SharedResourceData.MetalDepositVariant))]
    internal static class DepositVariant_Patch
    {
        /// <summary>Biomes already reported, so a per-frame poll cannot spam the log.</summary>
        private static readonly HashSet<string> _reported = new HashSet<string>();

        /// <summary>
        /// Turns the biome-has-no-PropLibrary throw into a null return. Harmony
        /// finalizers see the exception in <paramref name="__exception"/>; returning
        /// null from the finalizer suppresses it.
        /// </summary>
        [HarmonyFinalizer]
        public static Exception MetalDepositVariant_Finalizer(
            Exception __exception, BiomeType biome, string id, ref AssetVariant<string, MetalDepositVisuals> __result)
        {
            if (__exception == null)
            {
                return null;
            }

            ReportOnce("throw:" + biome,
                "[WAR][deposit] no metal-deposit variant container for biome " + biome
                + " while resolving '" + id + "': " + __exception.Message
                + ". The rock cannot be built. Serve a biome whose MetalDeposits_BiomeNN "
                + "PropLibrary exists (the shipped table defines Biome1..Biome4).");

            __result = null;
            return null;
        }

        /// <summary>
        /// Reports an unresolved id and substitutes the biome's first variant.
        /// </summary>
        [HarmonyPostfix]
        public static void MetalDepositVariant_Postfix(
            BiomeType biome, string id, ref AssetVariant<string, MetalDepositVisuals> __result)
        {
            if (__result != null)
            {
                return;
            }

            List<AssetVariant<string, MetalDepositVisuals>> available = SafeVariantsFor(biome);

            if (available == null || available.Count == 0)
            {
                ReportOnce("empty:" + biome,
                    "[WAR][deposit] biome " + biome + " offers NO MetalDepositVisuals at all, so "
                    + "'" + id + "' cannot be substituted. Every deposit in this biome will be "
                    + "invisible.");
                return;
            }

            StringBuilder ids = new StringBuilder();
            for (int i = 0; i < available.Count; i++)
            {
                if (i > 0)
                {
                    ids.Append(", ");
                }
                ids.Append('\'').Append(available[i].Id).Append('\'');
            }

            ReportOnce("miss:" + biome + ":" + id,
                "[WAR][deposit] variantId '" + id + "' does not exist in biome " + biome
                + ". Available: " + ids + ". Falling back to '" + available[0].Id
                + "' so the rock still renders - set WAREBORN_DEPOSIT_VARIANT (or "
                + "MetalDeposits.DefaultVariantId) to one of the ids above.");

            __result = available[0];
        }

        private static List<AssetVariant<string, MetalDepositVisuals>> SafeVariantsFor(BiomeType biome)
        {
            try
            {
                // The public random accessor walks the same cached per-biome list; asking
                // for one entry is the cheapest way to force the list to exist without
                // reaching into SharedResourceData's private cache. Its result is
                // discarded - the list itself is what matters.
                SharedResourceData.RandomMetalDepositVariant(biome);
            }
            catch (Exception)
            {
                return null;
            }

            try
            {
                object cache = AccessTools
                    .Field(typeof(SharedResourceData), "_metalDepositsByBiome")
                    ?.GetValue(null);

                if (cache is Dictionary<BiomeType, List<AssetVariant<string, MetalDepositVisuals>>> byBiome
                    && byBiome.TryGetValue(biome, out List<AssetVariant<string, MetalDepositVisuals>> list))
                {
                    return list;
                }
            }
            catch (Exception)
            {
                // Fall through - a missing cache just means no substitution.
            }

            return null;
        }

        private static void ReportOnce(string key, string message)
        {
            if (_reported.Add(key))
            {
                Debug.LogWarning(message);
            }
        }
    }
}
