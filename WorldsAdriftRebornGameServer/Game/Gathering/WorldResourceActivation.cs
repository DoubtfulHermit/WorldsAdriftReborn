using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftRebornGameServer.Game.Gathering
{
    /// <summary>
    /// Makes a registered resource authoritative exactly once, independently of
    /// whether any particular peer currently has it checked out. Spatial interest
    /// controls visibility only; it must never decide whether harvesting exists.
    /// </summary>
    internal sealed class WorldResourceActivation
    {
        private readonly WorldEntityRegistry _world;
        private readonly TreeHarvest _trees;
        private readonly NodeRegistry _nodes;
        private readonly MetalHarvest _metal;
        private readonly AtlasShardRegistry _shards;
        private readonly FuelCanisterRegistry _fuel;

        public WorldResourceActivation(
            WorldEntityRegistry world,
            TreeHarvest trees,
            NodeRegistry nodes,
            MetalHarvest metal,
            AtlasShardRegistry shards,
            FuelCanisterRegistry fuel)
        {
            _world = world;
            _trees = trees;
            _nodes = nodes;
            _metal = metal;
            _shards = shards;
            _fuel = fuel;
        }

        /// <summary>Activates one boot-registered resource; idempotent across peers.</summary>
        public bool Activate(WorldEntity entity, long entityId)
        {
            bool activated = false;
            string? treeWood = TreeSpecies.WoodFor(entity.AssetName);
            if (treeWood != null)
            {
                TreeTopology? ownTopology = TreeTopologies.For(entity.AssetName);
                TreeTopology topology = ownTopology ?? Trees.Topology();
                if (_trees.Plant(entityId, topology, treeWood))
                {
                    Console.WriteLine("[world-resource] activated tree '" + entity.Key + "' as entity "
                        + entityId + ": " + topology.SectionCount + " sections, " + treeWood + ".");
                    activated = true;
                }
            }

            if (entity.AssetName == MetalNodes.AssetName)
            {
                MetalNode? node = MetalNodes.ByKey(entity.Key);
                if (node != null && _nodes.Register(entityId, node))
                {
                    // NodeYield, not a hand-written rule: it carries the node's own
                    // quality across, which every hand-written rule here used to drop.
                    HarvestReward.Register(
                        Multiplayer.Gathering.NodeYield.SourceKeyFor(node),
                        Multiplayer.Gathering.NodeYield.RuleFor(node));
                    _metal.Place(entityId, MetalNodes.NuggetYieldUnits);
                    Console.WriteLine("[world-resource] activated metal node '" + entity.Key
                        + "' as entity " + entityId + ".");
                    activated = true;
                }
            }

            if (entity.AssetName == MetalDeposits.AssetName)
            {
                MetalNode? deposit = MetalDeposits.ByKey(entity.Key);
                if (deposit != null && _nodes.Register(entityId, deposit))
                {
                    HarvestReward.Register(
                        Multiplayer.Gathering.NodeYield.SourceKeyFor(deposit),
                        Multiplayer.Gathering.NodeYield.RuleFor(deposit));
                    _metal.Place(entityId, MetalDeposits.YieldUnits, MetalDeposits.ShotsToDeplete);
                    Console.WriteLine("[world-resource] activated deposit '" + entity.Key
                        + "' as entity " + entityId + ".");
                    activated = true;
                }
            }

            if (entity.AssetName == AtlasShardCatalogue.AssetName)
            {
                string? hostKey = AtlasShardCatalogue.HostKeyOf(entity.Key);
                long? hostId = hostKey == null ? null : _world.BoundEntityIdFor(hostKey);
                if (hostId == null)
                {
                    Console.WriteLine("[warning] [world-resource] atlas shard '" + entity.Key
                        + "' has no bound host '" + (hostKey ?? "?") + "'; not activated.");
                }
                else if (_shards.Register(entityId, hostId.Value, AtlasShardCatalogue.DefaultSlotId))
                {
                    Console.WriteLine("[world-resource] activated atlas shard '" + entity.Key
                        + "' as entity " + entityId + " in deposit " + hostId.Value + ".");
                    activated = true;
                }
            }

            if (FuelPods.IsPodKey(entity.Key) && _fuel.Register(entityId))
            {
                // Fuel is QUALITY-EXEMPT and stays so. Retail excludes it from the
                // quality scale explicitly (acs/ScannableData.cs:325), so it is the one
                // material for which the 0 that broke every metal is the right answer.
                HarvestReward.Register(
                    FuelPods.ItemTypeId,
                    new Multiplayer.Gathering.YieldRule(FuelPods.ItemTypeId, 1,
                        Multiplayer.Gathering.YieldRule.QualityExempt));
                Console.WriteLine("[world-resource] activated fuel canister '" + entity.Key
                    + "' as entity " + entityId + ".");
                activated = true;
            }

            // A LOOT CONTAINER is recognised by its KEY, not its asset name, for the
            // same reason a fuel canister is: the asset is a shared prefab and the key
            // is the thing that says which island's table this one belongs to. The
            // tier travels with the key so that opening a chest asks nothing about
            // who opened it - see LootTable on why contents must be peer-independent.
            // Haven's containers have no release record and fall back to tier 1, which
            // is what the tutorial island is.
            if (LootContainers.IsLootKey(entity.Key))
            {
                int tier = Multiplayer.Islands.ReleaseWorldLoot.TierForKey(entity.Key)
                    ?? Multiplayer.Loot.LootScrapTable.MinTier;
                if (Multiplayer.Loot.LootContainerLedger.Register(entityId, entity.Key, tier))
                {
                    Console.WriteLine("[world-resource] activated loot container '" + entity.Key
                        + "' as entity " + entityId + " (tier " + tier + ", "
                        + Multiplayer.Loot.LootContainerLedger.ContentsOf(entityId).Count + " items).");
                    activated = true;
                }
            }

            if (entity.AssetName == Databanks.AssetName
                && DatabankLedger.Register(entityId, Databanks.GrantAmount,
                    Databanks.NoteTitle, Databanks.NoteDescription))
            {
                Console.WriteLine("[world-resource] activated databank '" + entity.Key
                    + "' as entity " + entityId + ".");
                activated = true;
            }

            return activated;
        }

        /// <summary>
        /// Activates every boot resource whose id spatial interest already bound.
        /// Registration order is preserved so deposits precede their lodged shards.
        /// </summary>
        public int ActivateBoundResources()
        {
            int count = 0;
            foreach (WorldEntity entity in _world.Registrations)
            {
                if (!ResourceInterestPolicy.IsStreamedResourceKey(entity.Key))
                    continue;

                long? entityId = _world.BoundEntityIdFor(entity.Key);
                if (entityId.HasValue && Activate(entity, entityId.Value)) count++;
            }
            return count;
        }
    }
}
