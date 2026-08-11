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
    /// WHY BASE-7 IS THE WHOLE STORY FOR RENDERING. A ship-part prefab renders from
    /// its BAKED geometry the instant the client loads it (AssetLoadRequest +
    /// AddEntity); the seed set does NOT make it visible, it makes it LIFTABLE and
    /// (for a lamp) FUNCTIONAL. This is the same finding <see cref="ShipParts"/>
    /// records for the static engine/sail ("they render from their BAKED prefab
    /// geometry ... the server seeds NONE of their special-visualizer components ...
    /// those visualizers stay dormant ... but the parts still appear"). So every part
    /// here carries <see cref="LoosePartDefinition.BaseShipPartComponents"/> - the
    /// ShipPartVisualizer [Require] union that makes it render-and-liftable - and adds
    /// a functional component ONLY when that component is served with crash-safe idle
    /// data AND its absence would make the part visibly broken rather than merely
    /// dormant. The lamp adds 1108/1236 (it must glow); the motion-driven instruments
    /// add 1236 (their needle visualizers are damage-gated and 1236 is already served
    /// crash-safe). Everything else renders as an inert-but-correct prop, exactly as
    /// the codebase already ships the static engine and sail - dormant functional
    /// visuals are a documented follow-on, never a regression, because best-effort
    /// interest leaves one missing part inert, not the ship.
    ///
    /// PREFAB NAMES ARE THE VERIFIED CLIENT-RESOLVABLE NAMES. Unlike the original
    /// lamp guess, the prefab per row is the real client/worker-resolvable bundle name
    /// from docs/research/loop/data/prefab-names.tsv (Helm01, Sail01, ModularEngine,
    /// ModularWing, CoreMain, Panel01.., Altimeter, Respawner01, ...). They are still
    /// overridable at spawn time (per-schematic env var, see LoosePartSpawner) so a
    /// live mismatch is a config change, not a rebuild. attachmentType is a
    /// BuilderVisualizer.GetAttachmentType string; a wrong value only degrades
    /// placement snapping to None, never whether the part renders or lifts.
    /// </summary>
    public static class LoosePartCatalogue
    {
        /// <summary>The lamp recipe key, matching the "lamp" entry in schematicData.json.</summary>
        public const string LampSchematicId = "lamp";

        /// <summary>
        /// The lamp's default prefab. The client demonstrably resolves this (the lamp
        /// is the one part that already worked end-to-end), so it is left as-is for
        /// back-compat rather than switched to the tsv's "Lamp01"; both are overridable.
        /// </summary>
        public const string LampDefaultPrefab = "Lamp";

        /// <summary>The lamp's default attachmentType (a surface-mounted decoration).</summary>
        public const string LampDefaultAttachment = "shipSurfaces";

        // Functional component ids that are SERVED crash-safe today (ComponentsSerializer):
        //   1108 LampState, 1236 IsTooDamagedToWorkState, 1303 SailState, 1107 HornState.
        // A row may only list a functional id that has such a branch, or the
        // all-or-nothing interest batch would drop and the part would render inert.
        // 1303/1107 are new branches added alongside this catalogue; their idle Data
        // is verified crash-safe (SailVisualizer/HornVisualizer only subscribe on
        // enable, no Option deref). The riskier functional states (engine 1116+1251,
        // wing 1124, core 1115/1258, storage 1081+1210, respawn 1094) are left dormant
        // - the part still renders from baked geometry and lifts on the base 7, exactly
        // as ShipParts leaves the static engine/sail dormant; waking them is a
        // documented follow-on, never a regression (best-effort interest leaves one
        // part inert, never the ship).
        private const uint LampState = 1108;
        private const uint IsTooDamagedToWorkState = 1236;
        private const uint SailState = 1303;
        private const uint HornState = 1107;

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
        // column = functional components seeded ON TOP of the base 7, and ONLY ids
        // that are served crash-safe (1108/1236) may appear there.
        // ------------------------------------------------------------------
        private static readonly Row[] Rows =
        {
            // --- Basics: helm / sail / deck -------------------------------------
            // Helm renders + lifts on base 7; HelmVisualizer needs the ship's 1111,
            // not a loose-part component, so no functional id here.
            new Row("helm",  "basics", "Helm", "Helm01", "shipSurfaces", new uint[] { }),
            // Sail: 1303 SailState wakes SailVisualizer/SailBehaviour (unfurled=false,
            // no cloth force at rest); the ctor is a bool+float and OnEnable only
            // subscribes, so it is crash-safe. Served by a new 1303 branch.
            new Row("sail",  "basics", "Sail", "Sail01", "shipSurfaces", new uint[] { SailState }),
            // Deck piece; "deck" attachment drives ShipPartPlacement's deck styling.
            new Row("deck",  "basics", "Deck", "Deck01", "deck",         new uint[] { }),

            // --- Engine / wing (procedural modular parts) -----------------------
            // Engine/wing render from baked geometry; their EngineVisualizer/
            // WingVisualizer functional state (1116/1251/1124) stays dormant, the
            // same call ShipParts makes for the static engine.
            new Row("proceduralEngineDefault", "engine",         "Procedural Engine", "ModularEngine", "engine", new uint[] { }),
            new Row("proceduralWingDefault",   "proceduralWing", "Procedural Wing",   "ModularWing",   "wing",   new uint[] { }),

            // --- Sky cores: the main atlas core + its 8 module variants ----------
            // coreModule attachment routes to ShipCoreAttachmentPlacement.
            // ShipCoreVisualizer [Require]s ONLY 1236 + transform (NOT 1115), and 1236
            // is served crash-safe, so seeding it wakes the core's own visualizer
            // safely; its lift accounting (1258) only matters on a ship, dormant here.
            new Row("atlasSkyCore",             "skyCore", "Atlas Sky Core",              "CoreMain",             "coreModule", new uint[] { IsTooDamagedToWorkState }),
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
            new Row("smallPanel",   "structural", "Small Panel",   "Panel01",       "side", new uint[] { }),
            new Row("mediumPanel",  "structural", "Medium Panel",  "Panel02",       "side", new uint[] { }),
            new Row("largePanel",   "structural", "Large Panel",   "Panel03",       "side", new uint[] { }),
            new Row("window",       "structural", "Window",        "Window01",      "side", new uint[] { }),
            new Row("stairs",       "structural", "Stairs",        "Stairs1",       "deck", new uint[] { }),
            new Row("railing",      "structural", "Railing",       "RailingStraight","deck", new uint[] { }),
            new Row("railingCorner","structural", "Railing Corner","RailingCorner", "deck", new uint[] { }),

            // --- Storage: trunk / mounted box / storage & shipping containers ----
            // Treated as LOOSE parts (lifted with the scanner tool and bolted to the
            // ship, as all WA ship storage is), NOT ground-placed deployables. Their
            // InWorldInventoryVisualiser (1210+1081) is dormant, so they render as
            // props that do not yet OPEN - the inventory wiring is the follow-on. The
            // container prefab sizing (Small/Medium/Large) is a best guess.
            new Row("trunk",             "storage", "Trunk",             "ContainerSmall",  "deck",         new uint[] { }),
            new Row("mountedBox",        "storage", "Mounted Box",       "ContainerMount",  "shipSurfaces", new uint[] { }),
            new Row("storageContainer",  "storage", "Storage Container", "ContainerMedium", "deck",         new uint[] { }),
            new Row("shippingContainer", "storage", "Shipping Container","ContainerLarge",  "deck",         new uint[] { }),

            // --- Decoration: barrel / cupboard / horn / lamp --------------------
            new Row("barrel",   "decoration", "Barrel",   "Barrel01", "deck",         new uint[] { }),
            new Row("cupboard", "decoration", "Cupboard", "Cupboard", "deck",         new uint[] { }),
            // Horn: 1107 HornState wakes HornVisualizer (charge=0; OnEnable reads a
            // plain float, no Option deref, crash-safe). Served by a new 1107 branch.
            new Row("horn",     "decoration", "Horn",     "Horn01",   "shipSurfaces", new uint[] { HornState }),
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
            new Row("altimeter",          "instruments", "Altimeter",           "Altimeter",         "shipSurfaces", new uint[] { IsTooDamagedToWorkState }),
            new Row("fuelGauge",          "instruments", "Fuel Gauge",          "FuelGauge",         "shipSurfaces", new uint[] { IsTooDamagedToWorkState }),
            new Row("headingIndicator",   "instruments", "Heading Indicator",   "HeadingIndicator",  "shipSurfaces", new uint[] { IsTooDamagedToWorkState }),
            new Row("artificialHorizon",  "instruments", "Artificial Horizon",  "ArtificialHorizon", "shipSurfaces", new uint[] { IsTooDamagedToWorkState }),
            new Row("airspeedIndicator",  "instruments", "Airspeed Indicator",  "AirspeedIndicator", "shipSurfaces", new uint[] { IsTooDamagedToWorkState }),

            // --- Power generators -----------------------------------------------
            // Two schematic keys, one prefab. Render from baked geometry; any
            // generator functional state is dormant.
            new Row("powerGenerator",   "power", "Power Generator", "PowerGenerator01", "shipSurfaces", new uint[] { }),
            new Row("powerGenerator01", "power", "Power Generator", "PowerGenerator01", "shipSurfaces", new uint[] { }),

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
