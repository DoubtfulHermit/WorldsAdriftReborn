using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Placement
{
    /// <summary>
    /// One deployable kind: the item type that, when used from the hotbar, drives
    /// the native placement preview and, on confirm, spawns a shared world entity.
    /// A pure description - no ENet, no game types - so the table is unit-tested on
    /// Linux with no install.
    ///
    /// The three fields that make a deployable placeable at all:
    ///   * <see cref="AssetName"/>   - the bare prefab/bundle name the client loads
    ///                                  and the server names in AddEntity. Wrong here
    ///                                  and the client is told to place something it
    ///                                  never loaded -> nothing appears.
    ///   * <see cref="SeedComponents"/> - the components pushed unprompted right after
    ///                                  AddEntity. It ALWAYS contains 190602
    ///                                  (TransformState) because that is the one field
    ///                                  that places anything in this world. It may
    ///                                  additionally carry a state component (a
    ///                                  shipyard's 1205) - but ONLY when
    ///                                  ComponentsSerializer has a branch for that id,
    ///                                  because the seed push is all-or-nothing: an id
    ///                                  with no branch drops the WHOLE batch and yields
    ///                                  a rendered-but-inert entity.
    ///   * <see cref="KeyPrefix"/>    - the stable registration-key prefix a placed
    ///                                  instance is allocated a shared entity id from.
    /// </summary>
    public sealed class DeployableDef
    {
        public DeployableDef(
            string itemTypeId,
            string assetName,
            IReadOnlyList<uint> seedComponents,
            string keyPrefix,
            bool hasBackedState,
            bool assetVerified,
            bool isCraftingStation = false)
        {
            ItemTypeId = itemTypeId;
            AssetName = assetName;
            SeedComponents = seedComponents;
            KeyPrefix = keyPrefix;
            HasBackedState = hasBackedState;
            AssetVerified = assetVerified;
            IsCraftingStation = isCraftingStation;
        }

        /// <summary>The crafted item type that deploys this (e.g. "shipyard").</summary>
        public string ItemTypeId { get; }

        /// <summary>The bare prefab/bundle name spawned on confirm.</summary>
        public string AssetName { get; }

        /// <summary>
        /// The components seeded on the spawned entity. Always includes 190602; a
        /// shipyard also carries 1205. Every id here MUST have a ComponentsSerializer
        /// branch or the whole seed batch is dropped.
        /// </summary>
        public IReadOnlyList<uint> SeedComponents { get; }

        /// <summary>The registration-key prefix for a placed instance of this deployable.</summary>
        public string KeyPrefix { get; }

        /// <summary>
        /// True when this deployable carries a serializer-backed STATE component
        /// beyond the transform (the shipyard's 1205 ShipyardState, seeded from the
        /// placed-structure ledger). False means "190602 only": it renders at the
        /// placed transform but in its prefab-default (un-deployed) state until a
        /// state component + serializer branch is added for it.
        /// </summary>
        public bool HasBackedState { get; }

        /// <summary>
        /// True when <see cref="AssetName"/> is a confirmed real Worlds Adrift prefab
        /// name; false when it is a best-guess and the entity may fail to load (and so
        /// render invisible) on a live client. Documentation only - never sent on the
        /// wire; drives nothing but the residual-risk log line.
        /// </summary>
        public bool AssetVerified { get; }

        /// <summary>
        /// True when a placed instance is a generic CraftingStation-category workbench
        /// (the Assembly Station): the spawn seam records it in
        /// <c>Game.Placement.PlacedCraftingStations</c> so the 1210 branch seeds the
        /// "Craft" verb and the interact handler answers with the 1005 PlayerStartCrafting
        /// echo that opens the PARTS crafting UI. Distinct from <see cref="HasBackedState"/>
        /// (the shipyard's ledger-seeded 1205 state): a crafting station's extra seeds
        /// (1004/1005/1210) are entity-agnostic idle defaults, not ledger-sourced. The
        /// prefab's baked crafting category - NOT this flag - decides parts-vs-ship-build.
        /// </summary>
        public bool IsCraftingStation { get; }
    }

    /// <summary>
    /// The data-driven registry of every hand-placeable deployable. The placement
    /// pipeline (start preview, confirm-spawn, the 1211 use trigger and the debug
    /// trigger) is driven ENTIRELY by this table - it names no item type itself - so
    /// adding a new deployable is a row here plus (for a functional state) a
    /// ComponentsSerializer branch, and nothing in the hot path changes.
    ///
    /// Only <see cref="Shipyard"/> is proven end-to-end (asset "Shipyard" + 190602 +
    /// 1205). The chest and campfire the player reaches for are asset-named and seed
    /// 190602 so they PLACE and consume; their functional container/cooking state is
    /// a follow-on (a serializer branch + <c>HasBackedState</c>). Asset names not yet
    /// confirmed against the client are marked <c>assetVerified:false</c>.
    /// </summary>
    public static class Deployables
    {
        /// <summary>TransformState - the one field that places anything. Always seeded.</summary>
        public const uint TransformStateComponentId = 190602;

        /// <summary>ShipyardState - the shipyard's deployed/owner state (serializer-backed).</summary>
        public const uint ShipyardStateComponentId = 1205;

        /// <summary>
        /// ShipHullEditorState - the read-only editor state the client's
        /// ShipHullEditorVisualizer [Require]s (with 1205) to construct. Seeded inactive;
        /// the 1208 handler pushes Active/HullData per-peer when a frame is loaded.
        /// </summary>
        public const uint ShipHullEditorStateComponentId = 1206;

        /// <summary>InteractiveState - the 1210 that puts the "Craft" prompt on the console.</summary>
        public const uint InteractiveStateComponentId = 1210;

        /// <summary>CraftingStationGSimState - the 1004 gate CraftingStationBehaviour requires.</summary>
        public const uint CraftingStationGSimStateComponentId = 1004;

        /// <summary>CraftingStationClientState - the 1005 that carries PlayerStartCrafting.</summary>
        public const uint CraftingStationClientStateComponentId = 1005;

        /// <summary>The item type of the one fully-proven deployable.</summary>
        public const string ShipyardItemType = "shipyard";

        private static readonly uint[] TransformOnly = { TransformStateComponentId };

        /// <summary>
        /// The placed shipyard's full seed set: it renders as a deployed structure
        /// (190602 transform + 1205 ShipyardState) AND its centre console shows an
        /// interact prompt that opens the ship-build UI. The console is made
        /// interactive by 1210 InteractiveState (verb Craft); the UI is opened by
        /// CraftingStationBehaviour, which only enables when BOTH 1004
        /// CraftingStationGSimState and 1005 CraftingStationClientState are present.
        /// Every id here has a ComponentsSerializer branch (190602, 1205, 1206, 1210,
        /// 1004, 1005) - required, because the seed push is all-or-nothing: one id
        /// without a branch drops the WHOLE batch and the shipyard spawns inert at the
        /// origin. 1206 ShipHullEditorState is what lets the client CONSTRUCT its hull
        /// editor at all (ShipHullEditorVisualizer [Require]s the 1206 reader + 1205);
        /// it is seeded inactive and driven live by the 1208 command handler.
        /// </summary>
        private static readonly uint[] TransformAndShipyard =
            { TransformStateComponentId, ShipyardStateComponentId,
              ShipHullEditorStateComponentId,
              InteractiveStateComponentId, CraftingStationGSimStateComponentId,
              CraftingStationClientStateComponentId };

        /// <summary>
        /// The placed Assembly Station's full seed set: it stands in the world (190602
        /// transform) and its body shows an interact prompt (1210 InteractiveState, verb
        /// Craft) that opens the GENERIC PARTS crafting UI. The UI is opened by
        /// CraftingStationBehaviour, which [Require]s BOTH 1004 CraftingStationGSimState
        /// and 1005 CraftingStationClientState (VERIFIED via the decompile), so both must
        /// be seeded or the behaviour never enables and the Craft interaction opens
        /// nothing. It deliberately does NOT carry the shipyard-only 1205/1206/1207 - the
        /// AssemblyStation prefab bakes crafting category CraftingStation, so this SAME
        /// 1005-PlayerStartCrafting interact opens the parts tab (ItemCraft), whereas the
        /// Shipyard prefab (category Shipyard) opens ship-build (ShipCraft). Every id here
        /// has a ComponentsSerializer branch (190602, 1004, 1005, 1210) - required,
        /// because the seed push is all-or-nothing: one id without a branch drops the
        /// WHOLE batch and the station spawns inert at the origin.
        /// </summary>
        private static readonly uint[] TransformAndCraftingStation =
            { TransformStateComponentId,
              CraftingStationGSimStateComponentId,
              CraftingStationClientStateComponentId,
              InteractiveStateComponentId };

        private static readonly Dictionary<string, DeployableDef> ByType =
            BuildTable();

        private static Dictionary<string, DeployableDef> BuildTable()
        {
            var table = new Dictionary<string, DeployableDef>();

            void Add(string itemType, string asset, uint[] seeds, string keyPrefix,
                bool hasBackedState, bool assetVerified, bool isCraftingStation = false)
            {
                table[itemType] = new DeployableDef(
                    itemType, asset, seeds, keyPrefix, hasBackedState, assetVerified,
                    isCraftingStation);
            }

            // --- Fully proven: asset + transform + serializer-backed state. ---
            Add(ShipyardItemType, "Shipyard", TransformAndShipyard, "placed-shipyard",
                hasBackedState: true, assetVerified: true);

            // --- The Assembly Station: a generic crafting station that places like the
            // shipyard but opens the PARTS UI instead of ship-build. Its LOADABLE world
            // prefab is "CraftingStation" (client bundle "CraftingStation_unityclient" +
            // held-model "CraftingStationEquip_unityclient"), the SAME base-name -> worker-
            // asset resolution the shipyard uses ("Shipyard" -> "Shipyard_unityclient").
            // The player-facing name is "Assembly Station" but there is NO loadable
            // "AssemblyStation" deployable prefab in the client bundles (only the UI/quest
            // strings and a stray "AssemblyStationEquiped"); naming the asset "AssemblyStation"
            // gave the client a StartPlacingItemEvent.PlacingPrefab it could not load, so the
            // placement preview never instantiated and the client never sent the 1017 confirm
            // -> the station could be selected and "used" but never placed. "CraftingStation"
            // is the real WA prefab and resolves identically to every other deployable here.
            // Its interact seed set (190602 + 1004 + 1005 + 1210) makes CraftingStationBehaviour +
            // InteractiveObjectVisualizer enable; the prefab's baked CraftingStation category
            // routes the Craft interact to the parts tab. isCraftingStation records the placed
            // instance in PlacedCraftingStations so the 1210 verb + interact echo recognise it.
            Add("assemblyStation", "CraftingStation", TransformAndCraftingStation,
                "placed-assemblyStation", hasBackedState: false, assetVerified: true,
                isCraftingStation: true);

            // --- Storage & containers ---
            // These place + consume today (visible prop at the placed transform); their
            // container INTERACTION keys off InventoryState (1081) - there is no
            // dedicated storage-state id - which has no ComponentsSerializer branch yet,
            // so seeding it would DROP the whole batch (190602 included) and place them
            // at the origin. Hence 190602-only until a 1081 branch is added.
            // Follow-up seed id per deployable is noted per line.
            Add("makeshiftStorage", "MakeshiftStorage", TransformOnly, "placed-makeshiftStorage",
                hasBackedState: false, assetVerified: true);   // +1081 InventoryState
            Add("storageContainer", "ContainerMedium", TransformOnly, "placed-storageContainer",
                hasBackedState: false, assetVerified: false);  // +1081; exact Container* variant unconfirmed
            Add("shippingContainer", "ContainerLarge", TransformOnly, "placed-shippingContainer",
                hasBackedState: false, assetVerified: false);  // +1081; exact Container* variant unconfirmed
            Add("cupboard", "Cupboard", TransformOnly, "placed-cupboard",
                hasBackedState: false, assetVerified: true);   // +1081
            Add("trunk", "Trunk", TransformOnly, "placed-trunk",
                hasBackedState: false, assetVerified: false);  // prefab not found in scan; likely a Container* variant
            Add("barrel", "Barrel01", TransformOnly, "placed-barrel",
                hasBackedState: false, assetVerified: true);   // +1081
            Add("mountedBox", "MountedBox", TransformOnly, "placed-mountedBox",
                hasBackedState: false, assetVerified: false);  // prefab unconfirmed

            // --- Utility stations & lights that stand in the world. ---
            Add("campFire", "Campfire", TransformOnly, "placed-campFire",
                hasBackedState: false, assetVerified: true);   // +1012 CampfireState
            Add("stove", "Stove01", TransformOnly, "placed-stove",
                hasBackedState: false, assetVerified: true);   // +crafting-station family (1264/1004)
            Add("loom", "Loom01", TransformOnly, "placed-loom",
                hasBackedState: false, assetVerified: true);   // +1264 InventoryItemCraftingStationState
            Add("atlasLifter", "Lifter", TransformOnly, "placed-atlasLifter",
                hasBackedState: false, assetVerified: true);   // +1021 LifterState
            Add("lamp", "Lamp01", TransformOnly, "placed-lamp",
                hasBackedState: false, assetVerified: true);   // +1108 LampState
            Add("powerGenerator", "PowerGenerator01", TransformOnly, "placed-powerGenerator",
                hasBackedState: false, assetVerified: true);   // +fuel states (1104/1105/1106) unconfirmed
            Add("powerGenerator01", "PowerGenerator01", TransformOnly, "placed-powerGenerator01",
                hasBackedState: false, assetVerified: true);
            Add("personalReviver", "KiokiRevivalChamberA", TransformOnly, "placed-personalReviver",
                hasBackedState: false, assetVerified: false);  // prefab unconfirmed
            Add("territory_control_beacon", "TerritoryControlBeacon", TransformOnly,
                "placed-territoryControlBeacon", hasBackedState: false, assetVerified: true); // no territory-state id

            return table;
        }

        /// <summary>Whether this item type is a hand-placeable deployable.</summary>
        public static bool IsDeployable(string? itemTypeId)
        {
            return itemTypeId != null && ByType.ContainsKey(itemTypeId);
        }

        /// <summary>The deployable definition for an item type, or false if it is not one.</summary>
        public static bool TryGet(string? itemTypeId, out DeployableDef def)
        {
            if (itemTypeId != null && ByType.TryGetValue(itemTypeId, out DeployableDef? found))
            {
                def = found;
                return true;
            }

            def = null!;
            return false;
        }

        /// <summary>Every registered deployable, for tests and the startup banner.</summary>
        public static IReadOnlyCollection<DeployableDef> All => ByType.Values;

        /// <summary>How many deployable kinds are registered.</summary>
        public static int Count => ByType.Count;
    }
}
