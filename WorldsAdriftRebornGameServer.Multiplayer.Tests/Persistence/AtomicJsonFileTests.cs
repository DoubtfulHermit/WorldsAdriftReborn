using System;
using System.IO;
using WorldsAdriftRebornGameServer.Multiplayer.Persistence;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Persistence
{
    /// <summary>
    /// The game server's atomic JSON file store. These assert the three properties the
    /// whole persistence layer leans on: a round trip preserves the data, a mid-write
    /// crash cannot leave a truncated file (the write is temp-then-replace and leaves no
    /// stray temp behind), and a corrupt file is quarantined rather than silently read
    /// as an empty world (which would look exactly like a wipe).
    /// </summary>
    public class AtomicJsonFileTests : IDisposable
    {
        private readonly string _dir;

        public AtomicJsonFileTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "wareborn-atomicjson-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private string Path_(string name) => Path.Combine(_dir, name);

        public sealed class Bag
        {
            public string Name { get; set; } = "";
            public int Count { get; set; }
            public byte[] Blob { get; set; } = Array.Empty<byte>();
        }

        [Fact]
        public void Read_of_a_missing_file_is_null_not_an_error()
        {
            Assert.Null(AtomicJsonFile.Read<Bag>(Path_("nope.json")));
        }

        [Fact]
        public void Write_then_read_round_trips_every_field_including_bytes()
        {
            string path = Path_("bag.json");
            Bag original = new Bag { Name = "shipyard", Count = 42, Blob = new byte[] { 0, 1, 2, 250, 255 } };

            Assert.True(AtomicJsonFile.Write(path, original));

            Bag? read = AtomicJsonFile.Read<Bag>(path);

            Assert.NotNull(read);
            Assert.Equal("shipyard", read!.Name);
            Assert.Equal(42, read.Count);
            Assert.Equal(original.Blob, read.Blob);
        }

        [Fact]
        public void Write_leaves_no_temp_file_behind()
        {
            string path = Path_("bag.json");

            Assert.True(AtomicJsonFile.Write(path, new Bag { Name = "x" }));

            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public void Second_write_replaces_the_first_atomically()
        {
            string path = Path_("bag.json");

            AtomicJsonFile.Write(path, new Bag { Name = "first", Count = 1 });
            AtomicJsonFile.Write(path, new Bag { Name = "second", Count = 2 });

            Bag? read = AtomicJsonFile.Read<Bag>(path);
            Assert.Equal("second", read!.Name);
            Assert.Equal(2, read.Count);
        }

        [Fact]
        public void A_corrupt_file_is_quarantined_and_read_as_null()
        {
            string path = Path_("bag.json");
            File.WriteAllText(path, "{ this is not valid json ]");

            Bag? read = AtomicJsonFile.Read<Bag>(path);

            Assert.Null(read);
            // Moved aside for inspection, not deleted: a wipe must never be silent.
            Assert.True(File.Exists(path + ".broken"));
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void An_empty_file_reads_as_null()
        {
            string path = Path_("bag.json");
            File.WriteAllText(path, "   ");

            Assert.Null(AtomicJsonFile.Read<Bag>(path));
        }
    }
}
