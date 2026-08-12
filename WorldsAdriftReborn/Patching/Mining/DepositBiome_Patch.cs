using System;
using Bossa.Travellers.Biomes;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Mining
{
    /// <summary>
    /// Stops a missing biome table from silently making every metal deposit invisible.
    ///
    /// THE HANG. <c>MetalDepositVisualiser.InitRoutine</c> cannot start building a rock
    /// until it knows the biome:
    ///
    ///     _waitForBiomeRoutine = StartCoroutine(Job.WaitUntilRoutine(
    ///         () =&gt; GlobalBiomeDataVisualizer.GetBiomeAt(transform.position).HasValue, ...))
    ///                                                    // MetalDepositVisualiser.cs:151
    ///
    /// <c>Job.WaitUntilRoutine</c> is an unbounded <c>while (!condition) yield return
    /// null;</c> - no timeout, no log. And <c>GetBiomeAt</c> returns null whenever the
    /// static zone table is empty:
    ///
    ///     if (_zones.Length &lt; 1) return -1;            // GlobalBiomeDataVisualizer.cs:68-71
    ///
    /// <c>_zones</c> is filled ONLY in that visualizer's <c>OnEnable</c>, which only
    /// runs once BOTH of its <c>[Require]</c>s are checked out - 1253
    /// <c>GlobalBiomeVoronoiCentresState</c> and 8064 <c>DevBiome</c> - on the single
    /// SpatialOS global entity. If that entity never arrives, or arrives with an empty
    /// <c>biomes</c> list, every deposit in the world polls forever and never draws a
    /// single triangle, with NOTHING in the log to say so. The deposit's crust and core
    /// visualisers stay enabled the whole time, which is what lets a lodged atlas shard
    /// render beside an invisible rock.
    ///
    /// There is no radius check anywhere in <c>FindClosestZone</c> - it is a nearest
    /// centre scan over X/Z - so ONE centre anywhere covers the entire world. The
    /// server is expected to serve exactly that.
    ///
    /// WHAT THIS DOES. It leaves the real path completely alone: when the table has any
    /// zone at all, the game's own answer is returned untouched. It only steps in for
    /// the degenerate "no table at all" case, where the honest alternatives are an
    /// invisible world of rocks or a default biome. It picks the default, shouts about
    /// it ONCE, and moves on - a deposit that renders with possibly-wrong-biome
    /// materials is strictly better for a revival than one that never renders.
    ///
    /// Deliberately narrow: only <c>GetBiomeAt</c> is patched. <c>GetZoneDataAt</c>,
    /// <c>GetZoneIdAt</c>, <c>GetRespawnerCountAt</c> and the PVE helpers still index
    /// the empty <c>_zonesData</c> and still report "unknown", so nothing else in the
    /// game is told a biome exists when it does not.
    /// </summary>
    [HarmonyPatch(typeof(GlobalBiomeDataVisualizer), nameof(GlobalBiomeDataVisualizer.GetBiomeAt))]
    internal static class DepositBiome_Patch
    {
        /// <summary>
        /// The biome assumed when the world has no biome table. Biome1 is the first
        /// member of <c>Bossa.Travellers.Biomes.BiomeType</c> (= 1) and the one the
        /// server's own single Voronoi centre names, so client and server agree on which
        /// PropLibrary a deposit's variant id is looked up in.
        /// </summary>
        private const BiomeType FallbackBiome = BiomeType.Biome1;

        private static bool _warned;

        [HarmonyPostfix]
        public static void GetBiomeAt_Postfix(ref BiomeType? __result)
        {
            // The table answered. Never second-guess a real biome.
            if (__result.HasValue)
            {
                return;
            }

            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning("[WAR][deposit] no biome table on the client: the global entity's "
                    + "1253 GlobalBiomeVoronoiCentresState / 8064 DevBiome never checked out, or "
                    + "its biomes list is empty. Every metal deposit would otherwise poll "
                    + "GetBiomeAt forever and never render. Assuming " + FallbackBiome
                    + " so the rocks build - fix the server's global entity to remove this.");
            }

            __result = FallbackBiome;
        }
    }
}
