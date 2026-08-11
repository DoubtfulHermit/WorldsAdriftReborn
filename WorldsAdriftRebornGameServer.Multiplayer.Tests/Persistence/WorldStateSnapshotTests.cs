using System;
using System.IO;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
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
            });

            string path = Path.Combine(_dir, "world.json");
            AtomicJsonFile.Write(path, snapshot);
            WorldStateSnapshot read = AtomicJsonFile.Read<WorldStateSnapshot>(path)!;

            BuiltShipRecord r = Assert.Single(read.BuiltShips);
            Assert.Equal(hull, r.HullPosition());
            Assert.Equal(bytes, r.HullBytes);
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
    }
}
