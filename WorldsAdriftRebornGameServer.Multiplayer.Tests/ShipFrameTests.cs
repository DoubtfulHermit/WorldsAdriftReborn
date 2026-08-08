using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The first thing this server puts in the world that is neither terrain nor
    /// a person: one static procedural ship hull on Haven.
    ///
    /// Almost everything about it is a VALUE rather than a mechanism - 39 opaque
    /// bytes, four component ids, one coordinate - and every one of those values
    /// fails silently if it is wrong. A bad hull blob throws inside the CLIENT;
    /// a fifth component id drops all four seeds and leaves a rendered inert
    /// hull; a coordinate that is 47 m out looks exactly like a ship that did
    /// not spawn. None of that is observable from here, which is precisely why
    /// the values are pinned here instead.
    /// </summary>
    public class ShipFrameTests
    {
        /// <summary>
        /// The `one_cell` line of docs/research/loop/data/hulldata-samples.txt,
        /// which is the committed output of make_hulldata.py.
        ///
        /// Duplicated as HEX on purpose: the constant in production is base64, so
        /// a test that re-derived it from the same base64 string would only be
        /// asserting that Convert.FromBase64String works. This is the generator's
        /// other rendering of the same bytes, so the two agreeing means the
        /// constant really is what the generator produced.
        /// </summary>
        private const string OneCellHex =
            "010000000000e80000180000e800001800000000000001e80000180000e8000018000000000000";

        private static byte[] FromHex(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = System.Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        // ------------------------------------------------------------------
        // The hull blob
        // ------------------------------------------------------------------

        [Fact]
        public void The_seeded_hull_is_the_committed_generator_output_byte_for_byte()
        {
            Assert.Equal(FromHex(OneCellHex), ShipHull.MinimumHullData());
        }

        [Fact]
        public void The_hull_is_thirty_nine_bytes_which_is_the_smallest_legal_ship()
        {
            // 2 cellCount + 2 cellNumber + 2 deckNumber + 16 Front + 1 hasBack
            // + 16 Back. ShipPlan.Load reads exactly this and throws otherwise.
            Assert.Equal(ShipHull.MinimumHullDataLength, ShipHull.MinimumHullData().Length);
            Assert.Equal(39, ShipHull.MinimumHullDataLength);
        }

        [Fact]
        public void The_hull_declares_one_cell_at_deck_zero_and_carries_its_back_section()
        {
            // Reading the header back the way ShipPlan.Load does. If someone
            // swaps in the 3x1 or 3x2 variant this is the assertion that says so
            // out loud rather than letting a different ship appear in the world
            // by surprise.
            byte[] hull = ShipHull.MinimumHullData();

            Assert.Equal(1, System.BitConverter.ToInt16(hull, 0));   // cellCount
            Assert.Equal(0, System.BitConverter.ToInt16(hull, 2));   // cellNumber
            Assert.Equal(0, System.BitConverter.ToInt16(hull, 4));   // deckNumber
            Assert.Equal(1, hull[22]);                               // hasBack, after the 16-byte Front
        }

        [Fact]
        public void Every_caller_gets_its_own_copy_of_the_hull_so_one_bad_write_cannot_poison_the_rest()
        {
            // The array is handed to the game's serializer once per client. A
            // shared instance would turn a single accidental in-place edit into
            // "the ship stopped working for everyone who joined after 9pm".
            byte[] first = ShipHull.MinimumHullData();
            first[0] = 0xFF;

            Assert.Equal(1, System.BitConverter.ToInt16(ShipHull.MinimumHullData(), 0));
        }

        // ------------------------------------------------------------------
        // The control point's two non-obvious numbers
        // ------------------------------------------------------------------

        [Fact]
        public void The_fsim_id_hash_is_a_fixed_non_zero_constant()
        {
            // Non-zero because zero is also what an unset field decodes to.
            // FIXED because a change between consecutive points makes the client
            // ignore half a second of motion; that costs nothing today and is
            // the whole ballgame once this ship moves.
            Assert.NotEqual(0, ShipHull.FsimIdHash);
            Assert.Equal(ShipHull.FsimIdHash, ShipHull.FsimIdHash);
        }

        [Fact]
        public void Control_point_timestamps_are_milliseconds_since_the_clients_own_2018_epoch()
        {
            Assert.Equal(0, ShipHull.MillisecondsSinceEpoch(ShipHull.ControlPointEpochUtc));
            Assert.Equal(1000, ShipHull.MillisecondsSinceEpoch(ShipHull.ControlPointEpochUtc.AddSeconds(1)));
            Assert.Equal(
                86_400_000,
                ShipHull.MillisecondsSinceEpoch(ShipHull.ControlPointEpochUtc.AddDays(1)));
        }

        [Fact]
        public void The_epoch_is_the_one_the_client_uses_and_now_is_after_it()
        {
            Assert.Equal(new System.DateTime(2018, 3, 1, 0, 0, 0, System.DateTimeKind.Utc),
                ShipHull.ControlPointEpochUtc);
            Assert.True(ShipHull.NowMillisecondsSinceEpoch() > 0);
        }

        // ------------------------------------------------------------------
        // The registration
        // ------------------------------------------------------------------

        [Fact]
        public void The_ship_seeds_exactly_the_four_components_the_prefab_requires()
        {
            // FOUR, in this order, measured off ShipFrame_unityclient's [Require]
            // map. The batch is all-or-nothing: a fifth id with no branch in
            // ComponentsSerializer drops the other four and leaves a fully
            // rendered hull that does nothing at all.
            Assert.Equal(new uint[] { 190602, 1209, 1099, 1130 },
                WorldEntities.ShipFrame().SeedComponents.ToArray());
        }

        [Fact]
        public void The_ship_asks_for_the_bare_procedural_hull_prefab()
        {
            // "ShipFrame", not "ShipFrame_unityclient" - the client appends its
            // own worker suffix - and not ShipFrame01/02, whose geometry is baked
            // and whose client prefab has no root Rigidbody for PathFollower to
            // find.
            Assert.Equal("ShipFrame", WorldEntities.ShipFrame().AssetName);
            Assert.Equal(WorldEntities.DefaultAssetContext, WorldEntities.ShipFrame().AssetContext);
        }

        [Fact]
        public void The_ship_spawns_after_the_player_because_nobody_wakes_up_standing_on_it()
        {
            Assert.Equal(SpawnOrder.AfterPlayer, WorldEntities.ShipFrame().Order);
        }

        [Fact]
        public void The_ship_sits_on_measured_haven_ground_twelve_metres_north_of_the_spawn_point()
        {
            // Island-local (208.00, 5.30, 16.00): the surface vertex
            // (208.00, 4.80, 16.00) from the TRS-corrected LOD0 table, plus a
            // 0.50 m stand-off. The hull's deck plane is at its own local y = 0,
            // so that stand-off is literally the height of the step onto the
            // ship - too high and it cannot be walked onto, zero and it z-fights
            // with the ground.
            FixedPointPosition ship = WorldEntities.ShipFrame().Position;
            FixedPointPosition island = SpawnPolicy.IslandPosition;

            Assert.Equal(208.00, ship.MetresX - island.MetresX, 3);
            Assert.Equal(5.30, ship.MetresY - island.MetresY, 3);
            Assert.Equal(16.00, ship.MetresZ - island.MetresZ, 3);

            // Same X as the player, 12 m further north, so it is a straight walk.
            Assert.Equal(SpawnPolicy.PlayerSpawnPosition.X, ship.X);
            Assert.Equal(12.00, ship.MetresZ - SpawnPolicy.PlayerSpawnPosition.MetresZ, 3);

            // Below the player, who stands 2.00 m over their own surface vertex.
            Assert.True(ship.Y < SpawnPolicy.PlayerSpawnPosition.Y);
        }

        // ------------------------------------------------------------------
        // What the server actually runs with
        // ------------------------------------------------------------------

        [Fact]
        public void The_ship_is_in_the_default_registry_with_or_without_the_proof_island()
        {
            foreach (bool proof in new[] { false, true })
            {
                WorldEntityRegistry registry =
                    WorldEntities.Default(new EntityIdAllocator(), proof);

                WorldEntity? ship = registry.ByKey(WorldEntities.ShipFrameKey);
                Assert.NotNull(ship);
                Assert.Equal(WorldEntities.ShipFrame().Position, ship!.Position);
            }
        }

        [Fact]
        public void The_ship_gets_its_own_position_and_its_own_entity_id_from_the_registry()
        {
            // The point of the seam: the component serializer asks the registry
            // where THIS entity goes, so the ship needed no new branch in the
            // 190602 seed - and it must not be handed the player's spawn point,
            // which is what every unregistered id gets.
            EntityIdAllocator ids = new EntityIdAllocator();
            WorldEntityRegistry registry = WorldEntities.Default(ids);

            long islandId = registry.EntityIdFor(registry.ByKey(WorldEntities.IslandKey)!);
            long shipId = registry.EntityIdFor(registry.ByKey(WorldEntities.ShipFrameKey)!);

            Assert.NotEqual(islandId, shipId);
            Assert.Equal(SeededEntityKind.World, registry.KindOf(shipId));
            Assert.Equal(WorldEntities.ShipFrame().Position, registry.TransformSeedFor(shipId));
            Assert.NotEqual(SpawnPolicy.PlayerSpawnPosition, registry.TransformSeedFor(shipId));
        }

        [Fact]
        public void Every_client_calls_the_ship_by_the_same_entity_id()
        {
            // Not a nicety - it is what will let a later 1130 update, sent per
            // peer, address the same hull on every screen.
            EntityIdAllocator ids = new EntityIdAllocator();
            WorldEntityRegistry registry = WorldEntities.Default(ids);
            WorldEntity ship = registry.ByKey(WorldEntities.ShipFrameKey)!;

            Assert.Equal(registry.EntityIdFor(ship), registry.EntityIdFor(ship));
        }

        [Fact]
        public void The_ship_adds_two_plan_steps_and_neither_of_them_precedes_the_player()
        {
            IReadOnlyList<SpawnPlanStep> plan = SpawnPlan.For(WorldEntities.Default(new EntityIdAllocator()));

            Assert.True(SpawnPlan.GroundPrecedesPlayer(plan));
            Assert.True(SpawnPlan.EveryAssetIsRequestedBeforeItsEntity(plan));

            int player = plan.ToList().FindIndex(s => s.IsPlayer && s.Op == SpawnOp.AddEntity);
            int asset = plan.ToList().FindIndex(s =>
                s.Entity?.Key == WorldEntities.ShipFrameKey && s.Op == SpawnOp.RequestAsset);
            int entity = plan.ToList().FindIndex(s =>
                s.Entity?.Key == WorldEntities.ShipFrameKey && s.Op == SpawnOp.AddEntity);

            Assert.True(asset >= 0 && entity >= 0);
            Assert.True(player < asset);
            Assert.True(asset < entity);
        }
    }
}
