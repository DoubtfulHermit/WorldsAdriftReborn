using System;
using System.Text;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// The one FRAME DESIGN a fresh player is served in their 1207
    /// <c>ShipHullAgentState.field1_schematics</c> so the placed-shipyard build UI's
    /// FRAME DESIGNS list is non-empty. Pure, engine-free, and unit-tested on Linux:
    /// the geometry blob comes from <see cref="ShipPlanModel.MakeDefaultStarterHull"/>
    /// (byte-identical to <c>ShipHull.MinimumHullData()</c>) and the row's
    /// title/icon/uuid come from a hand-built JSON that mirrors the client's
    /// <c>SchematicData</c> shape.
    ///
    /// WHY THE JSON IS HAND-BUILT: the client parses
    /// <c>ShipHullSchematicData.field5_client_schematics_id</c> with
    /// <c>SchematicData.FromShipHullData</c> -&gt;
    /// <c>JToken.Parse(clientSchematicsId).ToObject&lt;SchematicData&gt;()</c>
    /// (acs/SchematicData.cs:288-299). If that string is null the client returns an
    /// empty SchematicData whose <c>title</c> is null and every row-render NREs; if it
    /// is malformed JSON the parse throws. So it MUST be present and valid JSON. The
    /// production Multiplayer assembly stays dependency-free (no Newtonsoft), so the
    /// object is emitted by hand; the test project re-parses it with the same library
    /// the client uses to assert the contract.
    ///
    /// THE UUID CONTRACT: <see cref="Uuid"/> is written BOTH into the JSON's
    /// <c>uUID</c> field AND into <c>ShipHullSchematicData.field6_uuid</c>. The client
    /// finds the slot to load via
    /// <c>ShipHullAgentVisualizer.GetSchematicSlotIndex(schematicData.UniqueID)</c>,
    /// which compares <c>SchematicData.UniqueID</c> (the JSON <c>uUID</c>) against
    /// <c>ShipHullSchematicData.uuid</c> (field6). They must be identical or a
    /// selected frame resolves to slot -1 and never loads.
    /// </summary>
    public static class StarterFrame
    {
        /// <summary>The starter frame's stable id, used for BOTH the JSON uUID and field6_uuid.</summary>
        public const string Uuid = "makeshift-hull-0001";

        /// <summary>The row title shown in FRAME DESIGNS.</summary>
        public const string Title = "Makeshift Hull";

        /// <summary>A client icon key known to resolve (used elsewhere by ShipCraftingUI).</summary>
        public const string IconId = "shipyard_placeholder_icon";

        /// <summary>The item type string; drives HumanReadableItemType only.</summary>
        public const string ItemType = "hull";

        /// <summary>Must parse to a real <c>CraftingCategory</c>; ship rows live under Shipyard.</summary>
        public const string Category = "Shipyard";

        /// <summary>One deck, the single-cell starter hull.</summary>
        public const int NumberOfDecks = 1;

        /// <summary>
        /// The hull beam in metres. The single starter cell is a section of half-width
        /// 3 m, so the full beam is 6 m. Feeds the diagnostics panel only; never gates
        /// the row rendering.
        /// </summary>
        public const float BeamsLength = 6f;

        /// <summary>The 39-byte ShipPlan blob for the starter hull (field1_data).</summary>
        public static byte[] HullBlob()
        {
            return ShipPlanModel.MakeDefaultStarterHull().Encode();
        }

        /// <summary>
        /// The <c>field5_client_schematics_id</c> JSON for the starter row, with the
        /// hull blob embedded as base64 in <c>hullData</c> and <see cref="Uuid"/> in
        /// <c>uUID</c>. Deterministic; safe to serve to every player.
        /// </summary>
        public static string ClientSchematicsIdJson()
        {
            return ClientSchematicsIdJson(HullBlob());
        }

        /// <summary>
        /// The JSON for a given hull blob. Overload kept pure so tests can pass a known
        /// blob and assert the base64 embedding without touching the encoder twice.
        /// </summary>
        public static string ClientSchematicsIdJson(byte[] hullBlob)
        {
            if (hullBlob == null)
            {
                throw new ArgumentNullException(nameof(hullBlob));
            }

            string hullBase64 = Convert.ToBase64String(hullBlob);

            var b = new StringBuilder(512);
            b.Append('{');
            Str(b, "uUID", Uuid); b.Append(',');
            Str(b, "schematicId", Uuid); b.Append(',');
            Str(b, "title", Title); b.Append(',');
            Str(b, "iconId", IconId); b.Append(',');
            Str(b, "description", "A basic starter hull frame."); b.Append(',');
            Str(b, "itemType", ItemType); b.Append(',');
            Str(b, "category", Category); b.Append(',');
            Num(b, "timeToCraft", 0); b.Append(',');
            Num(b, "amountToCraft", 1); b.Append(',');
            Num(b, "rarity", 0); b.Append(',');
            b.Append("\"baseStats\":{},");
            b.Append("\"craftingRequirements\":[],");
            Str(b, "hullData", hullBase64);
            b.Append('}');
            return b.ToString();
        }

        private static void Str(StringBuilder b, string key, string value)
        {
            b.Append('"').Append(key).Append("\":\"");
            Escape(b, value);
            b.Append('"');
        }

        private static void Num(StringBuilder b, string key, long value)
        {
            b.Append('"').Append(key).Append("\":").Append(value);
        }

        private static void Escape(StringBuilder b, string s)
        {
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\b': b.Append("\\b"); break;
                    case '\f': b.Append("\\f"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            b.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            b.Append(c);
                        }
                        break;
                }
            }
        }
    }
}
