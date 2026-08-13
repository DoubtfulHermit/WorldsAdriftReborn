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
            foreach (string name in ShipPartClientPrecache.PrefabNames)
            {
                Assert.True(ClientEntityPrefabs.CanResolve(name),
                    "Precache entry '" + name + "' is not a loadable client prefab -"
                    + " the boot precache would log a load error every start.");
            }
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
        public void Append_to_null_yields_the_full_part_list()
        {
            List<string> merged = ShipPartClientPrecache.AppendTo(null!);
            Assert.Equal(ShipPartClientPrecache.PrefabNames.Length, merged.Count);
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
