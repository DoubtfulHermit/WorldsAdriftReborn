using WorldsAdriftRebornGameServer.Multiplayer.Inventory;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Inventory
{
    /// <summary>
    /// The uid extraction, which is the single load-bearing unknown of the whole
    /// persistence design. These tests do not prove the uid ARRIVES - only a
    /// running client can do that - but they do prove that every way it can fail
    /// to arrive produces a volatile key rather than a wrong durable one.
    /// </summary>
    public class CharacterIdentityTests
    {
        private const string Uid = "3f1a5e2c-8b40-4c19-9d77-0a2b6e5f1c30";

        private static Dictionary<string, string> Published(string characterData)
        {
            return new Dictionary<string, string>
            {
                ["Head"] = "hair_dreads",
                ["Body"] = "torso_ponchoVariantB",
                ["Feet"] = "legs_wrap",
                ["Face"] = "face_C",
                [CharacterIdentity.CharacterDataKey] = characterData,
            };
        }

        [Fact]
        public void The_uid_is_read_out_of_the_published_character_record()
        {
            // The shape the mod actually publishes: JToken.FromObject over the
            // client's CharacterCreationData.
            string json = "{\"Id\":0,\"characterUid\":\"" + Uid + "\",\"Name\":\"Timu\","
                + "\"Server\":\"\",\"serverIdentifier\":\"\",\"Cosmetics\":{},\"isMale\":true}";

            Assert.Equal(Guid.Parse(Uid), CharacterIdentity.UidFrom(Published(json)));
        }

        [Fact]
        public void A_published_list_is_read_as_its_first_entry()
        {
            string json = "[{\"characterUid\":\"" + Uid + "\"}]";

            Assert.Equal(Guid.Parse(Uid), CharacterIdentity.UidFrom(Published(json)));
        }

        [Fact]
        public void The_upstream_placeholder_uid_is_refused()
        {
            // "valid-UIDs-have-at-least-one-" passes the client's own
            // Contains("-") check. If it were accepted here every player who saw
            // it would share one inventory.
            string json = "{\"characterUid\":\"valid-UIDs-have-at-least-one-\"}";

            Assert.Null(CharacterIdentity.UidFrom(Published(json)));
        }

        [Fact]
        public void A_map_without_the_character_record_yields_no_uid()
        {
            Dictionary<string, string> seedOnly = new()
            {
                ["Head"] = "hair_dreads",
                ["Body"] = "torso_ponchoVariantB",
            };

            Assert.Null(CharacterIdentity.UidFrom(seedOnly));
        }

        [Fact]
        public void Malformed_input_yields_no_uid_rather_than_an_exception()
        {
            // This runs on a network path, so hostile input must not be able to
            // take the server down.
            Assert.Null(CharacterIdentity.UidFrom(null));
            Assert.Null(CharacterIdentity.UidFrom(Published("")));
            Assert.Null(CharacterIdentity.UidFrom(Published("not json at all")));
            Assert.Null(CharacterIdentity.UidFrom(Published("{")));
            Assert.Null(CharacterIdentity.UidFrom(Published("[]")));
            Assert.Null(CharacterIdentity.UidFrom(Published("\"a string\"")));
            Assert.Null(CharacterIdentity.UidFrom(Published("{\"characterUid\":null}")));
            Assert.Null(CharacterIdentity.UidFrom(Published("{\"characterUid\":42}")));
        }

        [Fact]
        public void A_present_uid_produces_a_durable_key()
        {
            InventoryKey key = CharacterIdentity.KeyFor(7, Published("{\"characterUid\":\"" + Uid + "\"}"));

            Assert.True(key.IsDurable);
            Assert.Equal(Guid.Parse(Uid), key.CharacterUid);
        }

        [Fact]
        public void An_absent_uid_produces_a_session_key_that_can_never_be_persisted()
        {
            InventoryKey key = CharacterIdentity.KeyFor(7, new Dictionary<string, string>());

            Assert.False(key.IsDurable);
            Assert.Null(key.CharacterUid);
        }

        [Fact]
        public void Two_entities_never_share_a_session_key()
        {
            // Entity ids are never reused, which is precisely why a session key
            // is not a durable identity - and why it must at least be unique.
            Assert.NotEqual(InventoryKey.ForSession(7), InventoryKey.ForSession(8));
        }

        [Fact]
        public void The_same_character_always_gets_the_same_key()
        {
            Assert.Equal(InventoryKey.ForCharacter(Guid.Parse(Uid)), InventoryKey.ForCharacter(Guid.Parse(Uid)));
        }

        [Fact]
        public void A_session_key_and_a_character_key_are_never_equal()
        {
            Assert.NotEqual(InventoryKey.ForCharacter(Guid.Parse(Uid)), InventoryKey.ForSession(7));
        }
    }
}
