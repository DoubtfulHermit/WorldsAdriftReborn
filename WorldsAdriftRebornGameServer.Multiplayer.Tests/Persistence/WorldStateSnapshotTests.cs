using System;
using System.IO;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Persistence
{
    /// <summary>
    /// The shared-world snapshot round trip. The load-bearing case is that a placed
    /// deployable and a built ship survive a write/read cycle with every field the
    /// spawn path needs - item type, exact fixed-point coordinates (including negatives
    /// and the millimetre-level low bits), packed rotation, owner, and the hull byte
    /// blob - because anything lost here is a shipyard that reappears in the wrong place
    /// or a ship that renders nothing.
    /// </summary>
    public class WorldStateSnapshotTests : IDisposable
    {
        private readonly string _dir;

        public WorldStateSnapshotTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "wareborn-worldstate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        [Fact]
        public void An_empty_snapshot_round_trips_to_empty_lists()
        {
            string path = Path.Combine(_dir, "world.json");
            AtomicJsonFile.Write(path, new WorldStateSnapshot());

            WorldStateSnapshot? read = AtomicJsonFile.Read<WorldStateSnapshot>(path);

            Assert.NotNull(read);
            Assert.Empty(read!.PlacedDeployables);
            Assert.Empty(read.BuiltShips);
        }

        [Fact]
        public void A_placed_deployable_round_trips_every_field()
        {
            FixedPointPosition pos = FixedPointPosition.FromMetres(17212.5, -310.25, -1130.75);

            WorldStateSnapshot snapshot = new WorldStateSnapshot();
            snapshot.PlacedDeployables.Add(new PlacedDeployableRecord
            {
                ItemTypeId = "shipyard",
                X = pos.X,
                Y = pos.Y,
                Z = pos.Z,
                PackedRotation = 987654u,
                OwnerCharacterUid = "6f9619ff-8b86-d011-b42d-00c04fc964ff",
            });

            string path = Path.Combine(_dir, "world.json");
            AtomicJsonFile.Write(path, snapshot);
            WorldStateSnapshot read = AtomicJsonFile.Read<WorldStateSnapshot>(path)!;

            PlacedDeployableRecord r = Assert.Single(read.PlacedDeployables);
            Assert.Equal("shipyard", r.ItemTypeId);
            Assert.Equal(pos, r.Position());
            Assert.Equal(987654u, r.PackedRotation);
            Assert.Equal("6f9619ff-8b86-d011-b42d-00c04fc964ff", r.OwnerCharacterUid);
        }

        [Fact]
        public void A_built_ship_round_trips_position_and_hull_bytes()
        {
            FixedPointPosition hull = FixedPointPosition.FromMetres(-42.0, 0.5, 9000.125);
            byte[] bytes = new byte[] { 8, 0, 255, 12, 0, 0, 77, 200 };

            WorldStateSnapshot snapshot = new WorldStateSnapshot();
            snapshot.BuiltShips.Add(new BuiltShipRecord
            {
                HullX = hull.X,
                HullY = hull.Y,
                HullZ = hull.Z,
                HullBytes = bytes,
                Salvaged = true,
            });

            string path = Path.Combine(_dir, "world.json");
            AtomicJsonFile.Write(path, snapshot);
            WorldStateSnapshot read = AtomicJsonFile.Read<WorldStateSnapshot>(path)!;

            BuiltShipRecord r = Assert.Single(read.BuiltShips);
            Assert.Equal(hull, r.HullPosition());
            Assert.Equal(bytes, r.HullBytes);
            Assert.True(r.Salvaged);
        }

        [Fact]
        public void Clearing_a_built_ships_dock_link_keeps_the_ship_but_prevents_redock_on_restart()
        {
            FixedPointPosition yard = FixedPointPosition.FromMetres(17200, -310, -1100);
            var record = new BuiltShipRecord
            {
                HullX = 1,
                HullBytes = new byte[] { 4, 2 },
                ShipyardX = yard.X,
                ShipyardY = yard.Y,
                ShipyardZ = yard.Z,
            };
            Assert.Equal(yard, record.ShipyardPosition());

            record.ClearShipyardDock();

            Assert.Null(record.ShipyardPosition());
            Assert.Equal(1, record.HullX);
            Assert.Equal(new byte[] { 4, 2 }, record.HullBytes);
        }

        [Fact]
        public void Flight_pose_and_a_new_shipyard_dock_round_trip_on_the_same_record()
        {
            var record = new BuiltShipRecord { HullBytes = new byte[] { 1 } };
            FixedPointPosition flown = FixedPointPosition.FromMetres(25, 80, -140);
            FixedPointPosition yard = FixedPointPosition.FromMetres(30, 74, -140);

            record.UpdatePose(flown, 1.25);
            record.DockTo(yard);

            Assert.Equal(flown, record.HullPosition());
            Assert.Equal(1.25, record.HullYawRadians);
            Assert.Equal(yard, record.ShipyardPosition());
        }

        [Fact]
        public void Multiple_records_of_each_kind_all_survive_in_order()
        {
            WorldStateSnapshot snapshot = new WorldStateSnapshot();
            for (int i = 0; i < 3; i++)
            {
                snapshot.PlacedDeployables.Add(new PlacedDeployableRecord { ItemTypeId = "shipyard", X = i });
                snapshot.BuiltShips.Add(new BuiltShipRecord { HullX = i, HullBytes = new byte[] { (byte)i } });
            }

            string path = Path.Combine(_dir, "world.json");
            AtomicJsonFile.Write(path, snapshot);
            WorldStateSnapshot read = AtomicJsonFile.Read<WorldStateSnapshot>(path)!;

            Assert.Equal(3, read.PlacedDeployables.Count);
            Assert.Equal(3, read.BuiltShips.Count);
            Assert.Equal(0, read.PlacedDeployables[0].X);
            Assert.Equal(2, read.PlacedDeployables[2].X);
            Assert.Equal(new byte[] { 2 }, read.BuiltShips[2].HullBytes);
        }

        [Fact]
        public void A_loose_part_round_trips_every_field_and_rebuilds_its_definition()
        {
            FixedPointPosition pos = FixedPointPosition.FromMetres(1234.5, 67.25, -890.125);

            WorldStateSnapshot snapshot = new WorldStateSnapshot();
            snapshot.LooseParts.Add(new LoosePartRecord
            {
                PartUid = "abc123",
                SchematicId = "lamp",
                ItemType = "lamp",
                Title = "Lamp",
                PrefabName = "Lamp01",
                AttachmentType = "shipSurfaces",
                PartSpecificComponents = new uint[] { 1108u, 1236u },
                X = pos.X,
                Y = pos.Y,
                Z = pos.Z,
                PackedRotation = 424242u,
                OwnerCharacterUid = "6f9619ff-8b86-d011-b42d-00c04fc964ff",
            });

            string path = Path.Combine(_dir, "world.json");
            AtomicJsonFile.Write(path, snapshot);
            WorldStateSnapshot read = AtomicJsonFile.Read<WorldStateSnapshot>(path)!;

            LoosePartRecord r = Assert.Single(read.LooseParts);
            Assert.Equal("abc123", r.PartUid);
            Assert.Equal("lamp", r.SchematicId);
            Assert.Equal(pos, r.Position());
            Assert.Equal(424242u, r.PackedRotation);
            Assert.Equal("6f9619ff-8b86-d011-b42d-00c04fc964ff", r.OwnerCharacterUid);
            Assert.Equal(new uint[] { 1108u, 1236u }, r.PartSpecificComponents);

            // The prefab/item state remains exact, while the obsolete generic surface is
            // migrated to the usable deck so old saves do not retain frame-only placement.
            WorldsAdriftRebornGameServer.Multiplayer.Ship.LoosePartDefinition def = r.Definition();
            Assert.Equal("Lamp01", def.PrefabName);
            Assert.Equal("deck", def.AttachmentType);
            Assert.Equal("lamp", def.ItemType);
            Assert.Equal(new uint[] { 1108u, 1236u }, def.PartSpecificComponents);
        }

        [Fact]
        public void A_mounted_part_round_trips_its_ship_link_transform_rotation_and_owner()
        {
            FixedPointPosition local = FixedPointPosition.FromMetres(0.5, 1.25, -2.75);

            WorldStateSnapshot snapshot = new WorldStateSnapshot();
            snapshot.MountedParts.Add(new MountedPartRecord
            {
                PartUid = "part-xyz",
                BuiltShipIndex = 2,
                SchematicId = "lamp",
                ItemType = "lamp",
                Title = "Lamp",
                PrefabName = "Lamp01",
                AttachmentType = "shipSurfaces",
                PartSpecificComponents = new uint[] { 1108u, 1236u },
                LocalX = local.X,
                LocalY = local.Y,
                LocalZ = local.Z,
                PackedRotation = 55555u,
                OwnerCharacterUid = "6f9619ff-8b86-d011-b42d-00c04fc964ff",
            });

            string path = Path.Combine(_dir, "world.json");
            AtomicJsonFile.Write(path, snapshot);
            WorldStateSnapshot read = AtomicJsonFile.Read<WorldStateSnapshot>(path)!;

            MountedPartRecord r = Assert.Single(read.MountedParts);
            Assert.Equal("part-xyz", r.PartUid);
            Assert.Equal(2, r.BuiltShipIndex);
            Assert.Equal(local, r.LocalOffset());
            Assert.Equal(55555u, r.PackedRotation);
            Assert.Equal("6f9619ff-8b86-d011-b42d-00c04fc964ff", r.OwnerCharacterUid);
            Assert.Equal("Lamp01", r.Definition().PrefabName);
            Assert.Equal("deck", r.Definition().AttachmentType);
        }

        [Theory]
        [InlineData("deck")]
        [InlineData("shipSurfaces")]
        public void Existing_mounted_instruments_restore_with_the_pipe_mount_surface(string persistedSurface)
        {
            var record = new MountedPartRecord
            {
                PartUid = "existing-gauge",
                BuiltShipIndex = 4,
                SchematicId = "fuelGauge",
                ItemType = "fuelGauge",
                Title = "Fuel Gauge",
                PrefabName = "FuelGauge",
                AttachmentType = persistedSurface,
                PartSpecificComponents = new uint[] { 1105u, 1236u },
            };

            LoosePartDefinition restored = record.Definition();
            Assert.Equal("shipSurfaces", restored.AttachmentType);
            Assert.True(ShipInstruments.IsInstrument(restored.ItemType));
        }

        [Fact]
        public void Part_interaction_state_round_trips_and_legacy_records_default_safely()
        {
            WorldStateSnapshot snapshot = new WorldStateSnapshot();
            snapshot.MountedParts.Add(new MountedPartRecord
            {
                PartUid = "sail-1",
                ItemType = "sail",
                SailUnfurled = true,
            });
            snapshot.MountedParts.Add(new MountedPartRecord
            {
                PartUid = "lamp-1",
                ItemType = "lamp",
                LampOff = true,
            });

            string path = Path.Combine(_dir, "world.json");
            AtomicJsonFile.Write(path, snapshot);
            WorldStateSnapshot read = AtomicJsonFile.Read<WorldStateSnapshot>(path)!;

            Assert.True(read.MountedParts[0].SailUnfurled);
            Assert.True(read.MountedParts[1].LampOff);

            // LEGACY SAFETY: a record written before these fields existed must load
            // as the fresh-mount state - sail furled, lamp ON. LampOff is stored
            // INVERTED precisely so the JSON default (false) means ON.
            MountedPartRecord legacy = new MountedPartRecord();
            Assert.False(legacy.SailUnfurled);
            Assert.False(legacy.LampOff); // false = the lamp is ON
        }

    }
}
