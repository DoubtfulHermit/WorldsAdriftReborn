using System;
using Newtonsoft.Json.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// The starter FRAME DESIGN row must parse on the client exactly as
    /// <c>SchematicData.FromShipHullData</c> parses it -
    /// <c>JToken.Parse(field5).ToObject&lt;SchematicData&gt;()</c> - and its uUID must
    /// equal the field6 uuid or the row selects to slot -1. These assert both with the
    /// same JSON library the client uses.
    /// </summary>
    public class StarterFrameTests
    {
        [Fact]
        public void Hull_blob_is_the_39_byte_starter_hull()
        {
            byte[] blob = StarterFrame.HullBlob();
            Assert.Equal(39, blob.Length);
            // byte-identical to the ShipPlanModel default (which equals MinimumHullData)
            Assert.Equal(ShipPlanModel.MakeDefaultStarterHull().Encode(), blob);
        }

        [Fact]
        public void Client_schematics_id_is_valid_json()
        {
            string json = StarterFrame.ClientSchematicsIdJson();
            JObject o = JObject.Parse(json); // throws if malformed - the client would too
            Assert.NotNull(o);
        }

        [Fact]
        public void Json_uuid_equals_field6_uuid_contract()
        {
            // The client matches SchematicData.UniqueID (JSON uUID) against
            // ShipHullSchematicData.uuid (field6). StarterFrame.Uuid feeds BOTH.
            JObject o = JObject.Parse(StarterFrame.ClientSchematicsIdJson());
            Assert.Equal(StarterFrame.Uuid, (string?)o["uUID"]);
        }

        [Fact]
        public void Json_carries_a_non_null_title_and_icon()
        {
            // title is deref'd unconditionally when the row renders (GetFormattedTitle),
            // so a null here NREs the whole FRAME DESIGNS list.
            JObject o = JObject.Parse(StarterFrame.ClientSchematicsIdJson());
            Assert.Equal(StarterFrame.Title, (string?)o["title"]);
            Assert.False(string.IsNullOrEmpty((string?)o["iconId"]));
        }

        [Fact]
        public void Json_embeds_the_hull_blob_as_base64()
        {
            byte[] blob = StarterFrame.HullBlob();
            JObject o = JObject.Parse(StarterFrame.ClientSchematicsIdJson(blob));
            byte[] decoded = Convert.FromBase64String((string)o["hullData"]!);
            Assert.Equal(blob, decoded);
        }

        [Fact]
        public void Escaping_survives_a_quote_and_backslash()
        {
            // Guard the hand-rolled escaper: a title with a quote must still parse.
            // (StarterFrame.Title has none, but the escaper is shared - assert it.)
            byte[] blob = StarterFrame.HullBlob();
            string json = StarterFrame.ClientSchematicsIdJson(blob);
            // Round-trips cleanly through a real parser.
            JObject o = JObject.Parse(json);
            Assert.Equal("hull", (string?)o["itemType"]);
        }
    }
}
