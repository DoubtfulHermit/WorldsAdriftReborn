using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public class AppearanceStoreTests
    {
        private static Dictionary<string, string> Map(string value)
        {
            return new Dictionary<string, string> { { "bossaNetCharacterData", value } };
        }

        [Fact]
        public void Get_returns_null_for_unknown_entity()
        {
            AppearanceStore store = new();

            Assert.Null(store.Get(1));
        }

        [Fact]
        public void Record_then_Get_returns_the_map()
        {
            AppearanceStore store = new();
            store.Record(1, Map("alice"));

            Assert.Equal("alice", store.Get(1)!["bossaNetCharacterData"]);
            Assert.Equal(1, store.Count);
        }

        [Fact]
        public void Recording_again_replaces_the_previous_map()
        {
            AppearanceStore store = new();
            store.Record(1, Map("old"));
            store.Record(1, Map("new"));

            Assert.Equal("new", store.Get(1)!["bossaNetCharacterData"]);
            Assert.Equal(1, store.Count);
        }

        [Fact]
        public void Stored_map_is_a_copy_not_a_reference()
        {
            AppearanceStore store = new();
            Dictionary<string, string> source = Map("original");
            store.Record(1, source);

            source["bossaNetCharacterData"] = "mutated";

            Assert.Equal("original", store.Get(1)!["bossaNetCharacterData"]);
        }

        [Fact]
        public void Null_map_is_ignored()
        {
            AppearanceStore store = new();
            store.Record(1, null!);

            Assert.Null(store.Get(1));
            Assert.Equal(0, store.Count);
        }

        [Fact]
        public void Forget_removes_the_entity()
        {
            AppearanceStore store = new();
            store.Record(1, Map("alice"));
            store.Forget(1);

            Assert.Null(store.Get(1));
        }

        [Fact]
        public void Entities_are_independent()
        {
            AppearanceStore store = new();
            store.Record(1, Map("alice"));
            store.Record(3, Map("bob"));

            Assert.Equal("alice", store.Get(1)!["bossaNetCharacterData"]);
            Assert.Equal("bob", store.Get(3)!["bossaNetCharacterData"]);
        }
    }
}
