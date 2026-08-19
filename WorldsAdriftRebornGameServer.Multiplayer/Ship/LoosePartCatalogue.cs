using System.Collections.Generic;
using System.Linq;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// The pure, data-driven map from a crafted recipe id to the LOOSE ship part it
    /// produces. Every CraftingStation-category recipe the assembly bench shows -
    /// helm, sail, deck, procedural engine/wing, the sky cores, structural panels,
    /// storage, decoration, instruments, power generators, the personal reviver - is
    /// one row in <see cref="Rows"/>; adding or adjusting a part is a single row, not
    /// new machinery (the shipyard Deployables table is the shape this mirrors).
    ///
    /// RENDERING CONTRACT. Every row carries the common ShipPartVisualizer/lift/
    /// material/variation closure from <see cref="LoosePartDefinition.BaseShipPartComponents"/>.
    /// Prefabs whose visible geometry is generated rather than baked add their
    /// mandatory state here: decks add 1518, panels/windows add 1118, and modular
    /// engines/wings add 12281. Functional visuals are added only where the server
    /// has crash-safe truthful state (lamp, sail, horn and passive instruments).
    /// This distinction matters: the former "baked geometry is enough" assumption
    /// consumed materials while producing invisible Deck01/Panel02 entities.
    ///
    /// PREFAB NAMES ARE THE VERIFIED CLIENT-RESOLVABLE NAMES. Every prefabName below was
    /// cross-checked against the REAL client entity-prefab set extracted straight from
    /// the unmodified client assets - the "entityprefabs/&lt;name&gt;_unityclient" bundle
    /// strings in resources.assets/sharedassets*/globalgamemanagers, saved to
    /// docs/research/loop/data/client-entity-prefabs.txt and pinned by LoosePartTests. A
    /// name resolves IFF its lower-cased form is in that set (the client lower-cases and
    /// appends the worker suffix; LocalAssetBundleLoader.cs). All 37 other rows matched
    /// on the first pass (Helm01, Sail01, Deck01, ModularEngine, ModularWing, CoreMain,
    /// Panel01.., Altimeter, Respawner01, ...); the ONE guess that did NOT resolve was
    /// the lamp's "Lamp" (no lamp_unityclient bundle - only lamp01), now corrected to
    /// "Lamp01" (see LampDefaultPrefab). They are still overridable at spawn time
    /// (per-schematic env var, see LoosePartSpawner) so a live mismatch is a config
    /// change, not a rebuild. attachmentType is a
    /// BuilderVisualizer.GetAttachmentType string; a wrong value only degrades
    /// placement snapping to None, never whether the part renders or lifts.
    /// </summary>
    public static class LoosePartCatalogue
    {
        /// <summary>The lamp recipe key, matching the "lamp" entry in schematicData.json.</summary>
        public const string LampSchematicId = "lamp";

        /// <summary>
        /// The lamp's default prefab, CORRECTED to the real client asset "Lamp01".
        /// The former value "Lamp" was a guess and does NOT resolve: the client loads a
        /// ship part by lower-casing the name and appending the worker suffix
        /// (WorkerSpecificPrefabName -> "lamp_unityclient"; LocalAssetBundleLoader does
        /// prefabName.ToLower()), and there is NO "lamp_unityclient" bundle - the only
        /// lamp entity prefab in the client assets is "entityprefabs/lamp01_unityclient"
        /// (verified by string-scanning resources.assets/sharedassets*; the full list is
        /// docs/research/loop/data/client-entity-prefabs.txt). "Lamp01" is the SAME rule
        /// every proven-working part obeys (Helm01, Deck01, Sail01, ModularEngine all
        /// match their entityprefabs bundle), and it still carries LampVisualizer so the
        /// lamp glows exactly as before. Overridable via WAREBORN_LAMP_PREFAB.
        /// </summary>
        public const string LampDefaultPrefab = "Lamp01";

        /// <summary>The lamp's default attachmentType (a surface-mounted decoration).</summary>
        public const string LampDefaultAttachment = "deck";

        // Functional component ids that are SERVED crash-safe today (ComponentsSerializer):
        //   1108 LampState, 1236 IsTooDamagedToWorkState, 1303 SailState, 1107 HornState.
        // A row may only list a functional id that has such a branch, or the
        // all-or-nothing interest batch would drop and the part would render inert.
        // 1303/1107 are new branches added alongside this catalogue; their idle Data
        // is verified crash-safe (SailVisualizer/HornVisualizer only subscribe on
        // enable, no Option deref). The riskier functional states (engine 1116+1251,
        // wing 1124, core 1115/1258, storage 1081+1210, respawn 1094) are left dormant
        // - the part still renders from its visual contract and lifts on the common base, exactly
        // as ShipParts leaves the static engine/sail dormant; waking them is a
        // documented follow-on, never a regression (best-effort interest leaves one
        // part inert, never the ship).
        private const uint LampState = 1108;
        private const uint IsTooDamagedToWorkState = 1236;
        private const uint SailState = 1303;
        private const uint HornState = 1107;
        private const uint ShipDeckState = 1518;
        private const uint ShipPanelState = 1118;
        private const uint ModularShipPartState = 12281;

        /// <summary>
        /// What a storage container seeds on top of the base: 1081 InventoryState and
        /// 1236 IsTooDamagedToWorkState, the two <c>[Require]</c>s that decide whether
        /// <c>InWorldInventoryVisualiser</c> and <c>IsTooDamagedToWorkVisualizer</c>
        /// ever enable. Both have crash-safe ComponentsSerializer branches, which is
        /// the standing condition for appearing in an all-or-nothing seed batch.
        ///
        /// WHY 1081 IS SEEDED RATHER THAN LEFT TO INTEREST. A ruin chest seeds nothing
        /// at all and works, because its prefab's own interest asks for 1081/1210 and
        /// the serve answers. The same is very probably true here - but "probably" is
        /// how this repo has shipped invisible features twice, and a loose part
        /// already carries a seed batch, so putting 1081 in it turns an inference into
        /// a certainty and costs one component in a batch that is already eight long.
        ///
        /// IF A CONTAINER GOES INVISIBLE after this change, this is the line to
        /// suspect: the batch is applied with failOnComponentInitError TRUE, so one
        /// bad id drops all nine and the part renders as nothing. Dropping 1081 from
        /// this array falls back to the ruin-chest behaviour (interest-served) without
        /// touching anything else.
        /// </summary>
        private static readonly uint[] ContainerComponents =
            ShipContainers.RequiredComponents.ToArray();

        /// <summary>
        /// One row of the catalogue: the recipe key plus everything
        /// <see cref="LoosePartDefinition"/> needs. Kept as a tiny private record so
        /// the table reads as data; <see cref="Definition"/> turns it into the pure
        /// definition the spawner and serializer consume.
        /// </summary>
        private readonly struct Row
        {
            public Row(string schematicId, string category, string title, string prefabName,
                string attachmentType, uint[] functional)
            {
                SchematicId = schematicId;
                Category = category;
                Title = title;
                PrefabName = prefabName;
                AttachmentType = attachmentType;
                Functional = functional;
            }

            public string SchematicId { get; }

            /// <summary>
            /// The schematicData.json itemType CATEGORY (basics/structural/storage/
            /// decoration/instruments/skyCore/engine/proceduralWing/power) - documented
            /// here for grouping, but NOT the value written to 1120.itemType. The 1120
            /// itemType / 1099 salvage itemTypeId is the part's OWN key
            /// (<see cref="SchematicId"/>), matching the proven working lamp ("lamp",
            /// not "decoration"); the category is not a salvageable item id.
            /// </summary>
            public string Category { get; }

            public string Title { get; }
            public string PrefabName { get; }
            public string AttachmentType { get; }
            public uint[] Functional { get; }

            public LoosePartDefinition Definition =>
                new LoosePartDefinition(SchematicId, SchematicId, Title, PrefabName, AttachmentType, Functional);
        }

        // ------------------------------------------------------------------
        // THE TABLE. One row per CraftingStation-category ship part the bench
        // shows. prefabName = the verified client-resolvable name; attachmentType
        // = the best-guess GetAttachmentType string (config-overridable); the last
        // column = prefab-specific components seeded ON TOP of the common base, and ONLY ids
        // that are served crash-safe (1108/1236) may appear there.
        // ------------------------------------------------------------------
        private static readonly Row[] Rows =
        {
            // --- Basics: helm / sail / deck -------------------------------------
            // Helm renders + lifts on the common base; HelmVisualizer needs the ship's 1111,
            // not a loose-part component, so no functional id here.
            //
            // attachmentType "deck" (NOT the former best-guess "shipSurfaces"): a helm
            // mounts on the DECK, exactly as retail. The value decides WHICH ship surface
            // the unmodified client raycasts when the helm is lifted (BuilderVisualizer
            // .GetAttachmentType -> ShipPartPlacement.DeterminePlacementType, decompiled):
            //   * "deck" -> PlacementLocationType.ShipDeck -> raycast mask Layers
            //     .ShipAttachmentSolid, tag "ShipDeck" (PlacementPreview.GetMask:449,
            //     GetTag:434). That is EXACTLY the surface our built-ship deck (Deck01,
            //     Multiplayer.Deck) presents as a solid BoxCollider, so the helm can be
            //     placed ACROSS THE WHOLE DECK - and the ShipDeck path derives a STABLE,
            //     ship-aligned base rotation (PlacementPreview.PositionOnShip:757-761), so
            //     an interactive Z-rotate composes cleanly and holds.
            //   * the old "shipSurfaces" -> PlacementLocationType.ShipSurfaces -> raycast
            //     mask Layers.Environment with an EMPTY tag (GetMask:457, GetTag:438),
            //     which NEVER hits the ShipAttachmentSolid deck. The helm then only landed
            //     on whatever single incidental Environment-layer collider the ship exposed
            //     ("helm only mounts in ONE spot"), and that generic-surface path re-derives
            //     its base rotation from the raw hit normal every frame (PositionOnShip:
            //     784-792), so a Z-rotate visibly twitched and snapped back.
            // The server's own mount gate already accepts a built-deck target (PartMount
            // + PartMountService.TargetIsChildOfShip); the missing half was authoring the
            // deck surface here so the client raycasts it. See PartMountSurfaces.
            new Row("helm",  "basics", "Helm", "Helm01", "deck", new uint[] { }),
            // Sail: 1303 SailState wakes SailVisualizer/SailBehaviour (unfurled=false,
            // no cloth force at rest); the ctor is a bool+float and OnEnable only
            // subscribes, so it is crash-safe. Served by a new 1303 branch.
            //
            // attachmentType "deck" (was the best-guess "shipSurfaces"): a sail is a MAST
            // that stands on the deck - SailVisualizer is a yawing mast (YawJoint + a sail
            // mesh, decompiled acs/SailVisualizer.cs:9-11), NOT a hull-side part (those are
            // ShipWingPlacement / "wing"). So it mounts on the same ShipDeck surface the
            // helm does (our built ship's Deck01 solid collider), placeable across the whole
            // deck. The old "shipSurfaces" raycast Layers.Environment, which our built ship
            // does not expose as a real surface (only one incidental collider - the "one
            // spot" symptom), so a sail could not be freely placed. The RETAIL string may be
            // the forward-locked "deckForward" (a sail is directional) rather than the
            // free-rotating "deck"; both raycast the SAME ShipDeck surface, so both fix the
            // placement, and "deck" is the safe choice while built ships do not yet fly (the
            // yaw is runtime, so a placed orientation is cosmetic for now).
            new Row("sail",  "basics", "Sail", "Sail01", "deck", new uint[] { SailState }),
            // Deck piece; "deck" attachment drives ShipPartPlacement's deck styling.
            // 1518 is not optional decoration: ShipDeckVisualizer builds the actual
            // visible mesh and solid collider from it. Without it a craft consumes
            // materials and creates an entity whose deck never materialises.
            new Row("deck",  "basics", "Deck", "Deck01", "deck", new uint[] { ShipDeckState }),

            // --- Engine / wing (procedural modular parts) -----------------------
            // Engine/wing render from baked geometry; their EngineVisualizer/
            // WingVisualizer functional state (1116/1251/1124) stays dormant, the
            // same call ShipParts makes for the static engine.
            // Modular prefabs are empty shells until 12281 names their component
            // meshes. Without it the recipe succeeds but ShipPartGenerator never
            // builds an engine/wing that the player can see or attach.
            new Row("proceduralEngineDefault", "engine",         "Procedural Engine", "ModularEngine", "engine", new uint[] { ModularShipPartState }),
            new Row("proceduralWingDefault",   "proceduralWing", "Procedural Wing",   "ModularWing",   "wing",   new uint[] { ModularShipPartState }),

            // --- Sky cores: the main atlas core (the BASE) + its 8 modules -------
            //
            // THE BASE IS CoreMain, settled by the shipped assets themselves: the
            // CoreMain_unityclient prefab's LOD0 carries EIGHT authored socket
            // transforms, one per module, named after the module prefabs -
            // CoreGeneratorLocator, CoreComputerLocator, CoreAirfilterLocator,
            // coreCoolantSystemLocator, CoreAtlasEnhancerLocator,
            // CoreCircuitryNetworkLocator, CoreEfficiencyModuleLocator and
            // CoreStabiliserLoacotor (the typo ships in the asset) - exactly what
            // ShipCoreVisualizer.GetTransformForModule reads. No other prefab has
            // sockets (full UnityPy census over resources.assets + sharedassets0/1 +
            // globalgamemanagers). So the retail chain is: the CORE stands on the
            // DECK, and every module - INCLUDING the generator (enum value
            // AdvancedGenerator) - snaps onto the core. The placement text's "A Sky
            // core generator" is the retail name of the CoreMain base, not of the
            // skyCoreGenerator part; the earlier reconstruction that made the
            // generator the deck base had the chain backwards (live-confirmed: the
            // core refused to place on the generator - the generator has no sockets).
            //
            // The socket components themselves (ShipCoreVisualizer on the base,
            // ShipCoreModuleVisualizer on the modules, ShipCoreModuleLocator on the
            // sockets) are STRIPPED from every prefab in this build; the client mod
            // restores them at template-compile time from SkyCoreSockets, the shared
            // per-module map (prefab -> ShipCoreModuleTypes -> locator child).
            //
            // ShipCoreVisualizer [Require]s ONLY 1236 + transform (NOT 1115), and 1236
            // is served crash-safe, so seeding it wakes the core's own visualizer
            // safely; its lift accounting (1258) only matters on a ship, dormant here.
            new Row("atlasSkyCore",             "skyCore", "Atlas Sky Core",              "CoreMain",             "deck",       new uint[] { IsTooDamagedToWorkState }),
            new Row("skyCoreAtlasEnhancer",     "skyCore", "Sky Core Atlas Enhancer",     "CoreAtlasEnhancer",    "coreModule", new uint[] { IsTooDamagedToWorkState }),
            new Row("skyCoreGenerator",         "skyCore", "Sky Core Generator",          "CoreGenerator",        "coreModule", new uint[] { IsTooDamagedToWorkState }),
            new Row("skyCoreAirFilter",         "skyCore", "Sky Core Air Filter",         "CoreAirfilter",        "coreModule", new uint[] { IsTooDamagedToWorkState }),
            new Row("skyCoreCoolantSystem",     "skyCore", "Sky Core Coolant System",     "CoreCoolantSystem",    "coreModule", new uint[] { IsTooDamagedToWorkState }),
            new Row("skyCoreStabiliser",        "skyCore", "Sky Core Stabiliser",         "CoreStabiliser",       "coreModule", new uint[] { IsTooDamagedToWorkState }),
            new Row("skyCoreComputer",          "skyCore", "Sky Core Computer",           "CoreComputer",         "coreModule", new uint[] { IsTooDamagedToWorkState }),
            new Row("skyCoreCircuitryNetwork",  "skyCore", "Sky Core Circuitry Network",  "CoreCircuitryNetwork", "coreModule", new uint[] { IsTooDamagedToWorkState }),
            new Row("skyCoreEfficiencyModule",  "skyCore", "Sky Core Efficiency Module",  "CoreEfficiencyModule", "coreModule", new uint[] { IsTooDamagedToWorkState }),

            // --- Structural: hull panels / window / stairs / railings -----------
            // "side" panels attach to the hull sides; stairs/railings to the deck.
            // Panels are generated geometry, not baked props. ShipPanelVisualizer
            // [Require]s 1118 and its ShipPanelVariationVisualizer base [Require]s
            // 1246. The live Panel02 request proved both were absent.
            new Row("smallPanel",   "structural", "Small Panel",   "Panel01",  "side", new uint[] { ShipPanelState }),
            new Row("mediumPanel",  "structural", "Medium Panel",  "Panel02",  "side", new uint[] { ShipPanelState }),
            new Row("largePanel",   "structural", "Large Panel",   "Panel03",  "side", new uint[] { ShipPanelState }),
            new Row("window",       "structural", "Window",        "Window01", "side", new uint[] { ShipPanelState }),
            new Row("stairs",       "structural", "Stairs",        "Stairs1",       "deck", new uint[] { }),
            new Row("railing",      "structural", "Railing",       "RailingStraight","deck", new uint[] { }),
            new Row("railingCorner","structural", "Railing Corner","RailingCorner", "deck", new uint[] { }),

            // --- Storage: trunk / mounted box / storage & shipping containers ----
            // Treated as LOOSE parts (lifted with the scanner tool and bolted to the
            // ship, as all WA ship storage is), NOT ground-placed deployables. The
            // container prefab sizing (Small/Medium/Large) is a best guess.
            //
            // THESE FOUR ROWS SEED 1081 + 1236 (ShipContainers.RequiredComponents) and
            // that is what turns them from props into chests. InWorldInventoryVisualiser
            // [Require]s 1210 + 1081 and IsTooDamagedToWorkVisualizer [Require]s 1236;
            // a Unity visualiser does not enable until EVERY [Require] resolves and
            // says nothing when it does not, which is why these were visible, correct
            // and dead for months. 1210 is served on demand (the prefab's own interest
            // asks for it) with the Inventory verb the prefab bakes - see
            // PartInteractionPolicy.
            //
            // 1081 CARRIES THE ONE TRAP IN THIS FILE. Its serve branch calls
            // InventoryService.ForEntity, whose create-factory is
            // InventoryWire.DefaultModel - the PLAYER STARTER KIT - and Bind runs a
            // factory at most once per key. A container reaching that branch unbound
            // gets a permanent inventory full of gauntlets in a 10x18 belt grid. The
            // 1081 branch therefore calls ShipContainerStock.Ensure FIRST, exactly as
            // it calls LootStock.Ensure for a ruin chest.
            new Row("trunk",             "storage", "Trunk",             "ContainerSmall",  "deck",         ContainerComponents),
            // The reconstructed hull has no retail Environment-layer generic skin.
            // Until that geometry exists, the mounted box uses the real deck placement
            // surface so it is usable instead of hitting one incidental frame collider.
            new Row("mountedBox",        "storage", "Mounted Box",       "ContainerMount",  "deck", ContainerComponents),
            new Row("storageContainer",  "storage", "Storage Container", "ContainerMedium", "deck",         ContainerComponents),
            new Row("shippingContainer", "storage", "Shipping Container","ContainerLarge",  "deck",         ContainerComponents),

            // --- Decoration: barrel / cupboard / horn / lamp --------------------
            new Row("barrel",   "decoration", "Barrel",   "Barrel01", "deck",         new uint[] { }),
            new Row("cupboard", "decoration", "Cupboard", "Cupboard", "deck",         new uint[] { }),
            // Horn: 1107 HornState wakes HornVisualizer (charge=0; OnEnable reads a
            // plain float, no Option deref, crash-safe). Served by a new 1107 branch.
            // Use the real deck collider; generic ShipSurfaces cannot hit it on generated
            // ships and produced the same single-frame placement failure as the lamp.
            new Row("horn",     "decoration", "Horn",     "Horn01",   "deck", new uint[] { HornState }),
            // THE LAMP - the one part already proven end-to-end. It MUST glow, so it
            // seeds 1108 LampState + 1236 IsTooDamagedToWorkState (both served
            // crash-safe). Prefab/attach are its verified-working defaults.
            new Row(LampSchematicId, "decoration", "Lamp", LampDefaultPrefab, LampDefaultAttachment,
                new uint[] { LampState, IsTooDamagedToWorkState }),

            // --- Instruments: altimeter / gauges / horizon ----------------------
            // The needle visualizers are damage-gated: they [Require] 1236, and 1236
            // is already served crash-safe (isFunctional=true), so seeding it wakes
            // the instrument to read local ship motion instead of sitting dead. No
            // NEW branch is needed - this is the one safe functional add beyond the
            // lamp, reusing the lamp's own 1236 serve.
            //
            // Their exact retail server-refdata strings are unavailable, but generated
            // ships expose one broad usable mounting surface: ShipDeck. Author all five
            // there rather than retaining the known-broken generic Environment raycast.
            new Row("altimeter",          "instruments", "Altimeter",           "Altimeter",         "deck", new uint[] { IsTooDamagedToWorkState }),
            new Row("fuelGauge",          "instruments", "Fuel Gauge",          "FuelGauge",         "deck", new uint[] { IsTooDamagedToWorkState }),
            new Row("headingIndicator",   "instruments", "Heading Indicator",   "HeadingIndicator",  "deck", new uint[] { IsTooDamagedToWorkState }),
            new Row("artificialHorizon",  "instruments", "Artificial Horizon",  "ArtificialHorizon", "deck", new uint[] { IsTooDamagedToWorkState }),
            new Row("airspeedIndicator",  "instruments", "Airspeed Indicator",  "AirspeedIndicator", "deck", new uint[] { IsTooDamagedToWorkState }),

            // --- Power generators -----------------------------------------------
            // Two schematic keys, one prefab. Render from baked geometry; any
            // generator functional state is dormant.
            new Row("powerGenerator",   "power", "Power Generator", "PowerGenerator01", "deck", new uint[] { }),
            new Row("powerGenerator01", "power", "Power Generator", "PowerGenerator01", "deck", new uint[] { }),

            // --- Personal reviver (ship respawn point) --------------------------
            // A ship-mounted respawn point. RespawnerVisualizer [Require]s 1094 +
            // 8066; 8066 is in the base set, 1094 (respawn function) is left dormant
            // until the respawn flow exists - the prop renders and lifts meanwhile.
            new Row("personalReviver", "basics", "Personal Reviver", "Respawner01", "deck", new uint[] { }),
        };

        private static readonly Dictionary<string, LoosePartDefinition> BySchematicId =
            Rows.ToDictionary(r => r.SchematicId, r => r.Definition);

        /// <summary>The lamp as a loose world part (kept as a named accessor for the tests that pin it).</summary>
        public static LoosePartDefinition Lamp => BySchematicId[LampSchematicId];

        /// <summary>Every loose-part definition in the catalogue, one per CraftingStation ship-part recipe.</summary>
        public static IReadOnlyCollection<LoosePartDefinition> All => BySchematicId.Values;

        /// <summary>Every recipe id this catalogue produces a loose part for.</summary>
        public static IReadOnlyCollection<string> SchematicIds => BySchematicId.Keys;

        /// <summary>Whether this recipe produces a loose ship part (vs an inventory item or a hull).</summary>
        public static bool IsLoosePart(string? schematicId)
        {
            return schematicId != null && BySchematicId.ContainsKey(schematicId);
        }

        /// <summary>
        /// The loose part a recipe produces, or null when the recipe is not a
        /// loose-part craft (a normal inventory item, or the separate hull-blueprint
        /// flow). The caller spawns the returned definition on craft completion.
        /// </summary>
        public static LoosePartDefinition? ForSchematic(string? schematicId)
        {
            return schematicId != null && BySchematicId.TryGetValue(schematicId, out LoosePartDefinition? def)
                ? def : null;
        }
    }
}
