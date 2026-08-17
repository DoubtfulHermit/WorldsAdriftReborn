using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// Pins the sky-core socket restoration map (SkyCoreSockets, source-linked
    /// into the client mod) against the catalogue, the client census, and the
    /// facts recovered from the shipped assets.
    ///
    /// THE MODEL THESE GUARD: CoreMain is the BASE - the only prefab with
    /// authored socket transforms (eight Core*Locator children on CoreMain_LOD0,
    /// UnityPy-verified) - so it stands on the deck, and all eight modules
    /// (including the generator) snap onto it via the client's own coreModule
    /// placement path once the mod restores the stripped components. Getting the
    /// direction wrong is exactly the live bug this replaces (the generator as
    /// base has no sockets; the core refused to place on it).
    /// </summary>
    public class SkyCoreSocketsTests
    {
        /// <summary>The decompiled ShipCoreModuleTypes members (acs/ShipCoreModuleTypes.cs), verbatim.</summary>
        private static readonly HashSet<string> EnumMembers = new(StringComparer.Ordinal)
        {
            "Undefined", "AtlasEnhancer", "EnergyAmplifier", "AirFilter", "CoolantSystem",
            "CoreStabiliser", "AdvancedGenerator", "EfficiencyModule", "CircuitryNetwork",
        };

        [Fact]
        public void The_base_is_CoreMain_and_it_stands_on_the_deck()
        {
            // The base must be the atlasSkyCore part's own prefab...
            LoosePartDefinition core = LoosePartCatalogue.ForSchematic("atlasSkyCore")!;
            Assert.Equal(SkyCoreSockets.BasePrefabName, core.PrefabName);
            // ...and it PLACES ON THE DECK: "coreModule" would demand a ShipCoreVisualizer
            // parent, i.e. the core would demand itself - the unplaceable-chain regression.
            Assert.Equal("deck", core.AttachmentType);
            Assert.Equal(PartMountSurface.ShipDeck, PartMountSurfaces.ForAttachmentType(core.AttachmentType));
        }

        [Fact]
        public void The_eight_socket_rows_are_exactly_the_eight_coreModule_catalogue_parts()
        {
            // Every sky-core part EXCEPT the base must be a coreModule (it snaps onto the
            // base), and the socket map must cover each one's prefab - no more, no less.
            // A ninth module without a socket row would place-NRE-free but never snap; a
            // socket row without a catalogue part is dead weight that hides a rename.
            HashSet<string> modulePrefabs = new(StringComparer.Ordinal);
            foreach (LoosePartDefinition part in LoosePartCatalogue.All)
            {
                if (part.AttachmentType == "coreModule")
                {
                    modulePrefabs.Add(part.PrefabName);
                }
            }

            HashSet<string> mapped = new(StringComparer.Ordinal);
            foreach (SkyCoreSocketRow row in SkyCoreSockets.Modules)
            {
                Assert.True(mapped.Add(row.PrefabName), "duplicate socket row for " + row.PrefabName);
            }

            Assert.True(mapped.SetEquals(modulePrefabs),
                "The socket map and the catalogue's coreModule parts diverge. Map: ["
                + string.Join(", ", mapped) + "] catalogue: [" + string.Join(", ", modulePrefabs) + "]");
        }

        [Fact]
        public void Every_module_type_is_a_real_enum_member_used_once_and_never_Undefined()
        {
            // Both sides of the client's placement check (the locator's _moduleType and
            // the module's _type) are assigned from this map via Enum.Parse, so a
            // misspelled member throws at restore time and a duplicated one breaks the
            // dedup ("one module of each type"). Undefined would let two different
            // modules collide on the same key.
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (SkyCoreSocketRow row in SkyCoreSockets.Modules)
            {
                Assert.Contains(row.ModuleTypeName, EnumMembers);
                Assert.NotEqual("Undefined", row.ModuleTypeName);
                Assert.True(seen.Add(row.ModuleTypeName),
                    "ShipCoreModuleTypes." + row.ModuleTypeName + " is used by two modules - dedup would conflate them.");
            }
        }

        [Fact]
        public void Locator_names_are_the_authored_asset_names_including_the_shipped_quirks()
        {
            // The socket child names come VERBATIM from the shipped CoreMain prefab
            // (UnityPy dump of CoreMain_LOD0). Two of them look like mistakes and are
            // NOT: the lower-case "coreCoolantSystemLocator" and the typo'd
            // "CoreStabiliserLoacotor" ship that way, and FindDeepChild matches
            // exactly. Someone "fixing" the spelling here would silently break those
            // two sockets (the restore would synthesize root stand-ins instead).
            Dictionary<string, string> byPrefab = new(StringComparer.Ordinal);
            foreach (SkyCoreSocketRow row in SkyCoreSockets.Modules)
            {
                Assert.False(string.IsNullOrWhiteSpace(row.LocatorChildName));
                byPrefab[row.PrefabName] = row.LocatorChildName;
            }

            Assert.Equal("coreCoolantSystemLocator", byPrefab["CoreCoolantSystem"]);
            Assert.Equal("CoreStabiliserLoacotor", byPrefab["CoreStabiliser"]);
            Assert.Equal("CoreGeneratorLocator", byPrefab["CoreGenerator"]);
            Assert.Equal("CoreComputerLocator", byPrefab["CoreComputer"]);
            Assert.Equal("CoreAtlasEnhancerLocator", byPrefab["CoreAtlasEnhancer"]);
            Assert.Equal("CoreAirfilterLocator", byPrefab["CoreAirfilter"]);
            Assert.Equal("CoreCircuitryNetworkLocator", byPrefab["CoreCircuitryNetwork"]);
            Assert.Equal("CoreEfficiencyModuleLocator", byPrefab["CoreEfficiencyModule"]);
            // All eight sockets are distinct transforms.
            Assert.Equal(8, new HashSet<string>(byPrefab.Values, StringComparer.Ordinal).Count);
        }

        [Fact]
        public void The_forced_name_pairings_hold_and_the_two_free_ones_are_pinned()
        {
            // Six pairings are forced by name; the two leftovers (Generator, Computer)
            // take the two leftover enum values (AdvancedGenerator by name,
            // EnergyAmplifier by elimination). Pinned so a re-shuffle is a deliberate,
            // reviewed change - it would re-key every already-placed module.
            Dictionary<string, string> map = new(StringComparer.Ordinal);
            foreach (SkyCoreSocketRow row in SkyCoreSockets.Modules)
            {
                map[row.PrefabName] = row.ModuleTypeName;
            }

            Assert.Equal("AtlasEnhancer", map["CoreAtlasEnhancer"]);
            Assert.Equal("AirFilter", map["CoreAirfilter"]);
            Assert.Equal("CoolantSystem", map["CoreCoolantSystem"]);
            Assert.Equal("CoreStabiliser", map["CoreStabiliser"]);
            Assert.Equal("EfficiencyModule", map["CoreEfficiencyModule"]);
            Assert.Equal("CircuitryNetwork", map["CoreCircuitryNetwork"]);
            Assert.Equal("AdvancedGenerator", map["CoreGenerator"]);
            Assert.Equal("EnergyAmplifier", map["CoreComputer"]);
        }

        [Fact]
        public void Base_and_every_module_prefab_are_loadable_client_assets()
        {
            Assert.True(ClientEntityPrefabs.CanResolve(SkyCoreSockets.BasePrefabName));
            foreach (SkyCoreSocketRow row in SkyCoreSockets.Modules)
            {
                Assert.True(ClientEntityPrefabs.CanResolve(row.PrefabName),
                    "socket row prefab '" + row.PrefabName + "' is not a loadable client asset");
            }
        }

        [Fact]
        public void ForPrefab_finds_modules_and_rejects_everything_else()
        {
            Assert.NotNull(SkyCoreSockets.ForPrefab("CoreGenerator"));
            Assert.Equal("AdvancedGenerator", SkyCoreSockets.ForPrefab("CoreGenerator")!.ModuleTypeName);
            // The BASE is not a module of itself...
            Assert.Null(SkyCoreSockets.ForPrefab("CoreMain"));
            // ...and unrelated prefabs stay untouched by the restore.
            Assert.Null(SkyCoreSockets.ForPrefab("Helm01"));
            Assert.Null(SkyCoreSockets.ForPrefab(""));
        }
    }
}
