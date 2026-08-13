using System.Reflection;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// Pins the SHARED client precache list (source-linked into the BepInEx mod)
    /// against the loose-part catalogue and the client census.
    ///
    /// WHY: the client instantiates a runtime-crafted part the moment its
    /// AddEntityOp arrives, but loads prefabs via an async Resources coroutine
    /// that needs at least a frame - so only a prefab that is ALREADY cached
    /// renders reliably. The stock game precaches 19 prefabs; every part outside
    /// that list came out invisible on first craft (the atlas sky core being the
    /// live case). The mod appends <see cref="ShipPartClientPrecache.PrefabNames"/>
    /// to the boot precache, so:
    ///   - a catalogue row whose prefab is NOT here would reintroduce the race
    ///     (only saved by the sync-load rescue's hitch) -> test 1 fails the build;
    ///   - a precache name the client cannot load would error at every boot
    ///     -> test 2 fails the build.
    /// </summary>
    public class ShipPartClientPrecacheTests
    {
        [Fact]
        public void Every_catalogue_prefab_is_on_the_client_precache_list()
        {
            HashSet<string> precached = new(ShipPartClientPrecache.PrefabNames, StringComparer.Ordinal);
            foreach (LoosePartDefinition part in LoosePartCatalogue.All)
            {
                Assert.True(precached.Contains(part.PrefabName),
                    "Catalogue part '" + part.SchematicId + "' prefab '" + part.PrefabName
                    + "' is missing from ShipPartClientPrecache.PrefabNames - its first craft"
                    + " would race the async prefab load again. Add it to the shared list.");
            }
        }

        [Fact]
        public void Every_precache_name_is_a_real_client_prefab()
        {
            foreach (string name in ShipPartClientPrecache.PrefabNames
                         .Concat(ShipPartClientPrecache.WorldPrefabNames))
            {
                Assert.True(ClientEntityPrefabs.CanResolve(name),
                    "Precache entry '" + name + "' is not a loadable client prefab -"
                    + " the boot precache would log a load error every start.");
            }
        }

        [Fact]
        public void Every_resolvable_deployable_asset_is_on_the_client_precache_list()
        {
            // A deployable the census can resolve WILL be broadcast
            // AssetLoadRequest+AddEntity back-to-back on placement, and can be
            // timeout-advanced past its request at connect - both lose the async
            // load race unless the prefab is already warm. Rows the census cannot
            // resolve (Trunk, MountedBox: assetVerified false) are exempt: they
            // cannot be precached without a boot error, and they cannot render
            // anyway until a real prefab name is found for them.
            HashSet<string> precached = new(
                ShipPartClientPrecache.PrefabNames.Concat(ShipPartClientPrecache.WorldPrefabNames),
                StringComparer.Ordinal);

            foreach (Multiplayer.Placement.DeployableDef def in Multiplayer.Placement.Deployables.All)
            {
                if (!ClientEntityPrefabs.CanResolve(def.AssetName))
                {
                    continue;
                }

                Assert.True(precached.Contains(def.AssetName),
                    "Deployable '" + def.ItemTypeId + "' asset '" + def.AssetName
                    + "' is missing from the client precache - its placement broadcast"
                    + " would race the async prefab load. Add it to"
                    + " ShipPartClientPrecache.WorldPrefabNames.");
            }
        }

        [Fact]
        public void The_global_entity_and_the_stations_are_precached()
        {
            // The exact prefabs the 2026-08-12 spawn-chain stall left invisible:
            // the streamed-after tail. Pinned by name so a list refactor cannot
            // silently drop them.
            Assert.Contains("GlobalEntity", ShipPartClientPrecache.WorldPrefabNames);
            Assert.Contains("Shipyard", ShipPartClientPrecache.WorldPrefabNames);
            Assert.Contains("CraftingStation", ShipPartClientPrecache.WorldPrefabNames);
        }

        [Fact]
        public void Append_deduplicates_and_preserves_the_stock_list()
        {
            // The stock list already contains Helm01/Lamp01 etc.; appending must not
            // duplicate them, must keep stock order/entries intact, and must return
            // the same instance the game handed the postfix.
            List<string> stock = new() { "Helm01", "Lamp01", "ShipFrame01", "Shipyard" };
            List<string> merged = ShipPartClientPrecache.AppendTo(stock);

            Assert.Same(stock, merged);
            Assert.Equal("Helm01", merged[0]);
            Assert.Equal(1, merged.Count(n => n == "Helm01"));
            Assert.Equal(1, merged.Count(n => n == "Lamp01"));
            Assert.Contains("CoreMain", merged);
            Assert.Contains("Deck01", merged);
            // Nothing lost, everything of ours present exactly once.
            Assert.Equal(merged.Count, merged.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void Append_to_null_yields_the_full_deduplicated_list()
        {
            // Parts + world prefabs, each exactly once (the two arrays share
            // ContainerMedium/ContainerLarge so the union is smaller than the sum).
            int distinct = ShipPartClientPrecache.PrefabNames
                .Concat(ShipPartClientPrecache.WorldPrefabNames)
                .Distinct(StringComparer.Ordinal)
                .Count();

            List<string> merged = ShipPartClientPrecache.AppendTo(null!);
            Assert.Equal(distinct, merged.Count);
            Assert.Contains("GlobalEntity", merged);
        }

        [Fact]
        public void The_shared_source_stays_net35_clean()
        {
            // The file is compiled into the net35 BepInEx mod via a source link; the
            // strongest portable proxy for "keeps compiling there" is that it uses
            // nothing newer than plain static state (no records/init-only, which would
            // surface as generated members). Pin the shape: a static class exposing
            // exactly the array and the merge.
            Type t = typeof(ShipPartClientPrecache);
            Assert.True(t.IsAbstract && t.IsSealed, "must stay a static class");
            Assert.NotNull(t.GetField("PrefabNames", BindingFlags.Public | BindingFlags.Static));
            Assert.NotNull(t.GetMethod("AppendTo", BindingFlags.Public | BindingFlags.Static));
        }
    }
}
