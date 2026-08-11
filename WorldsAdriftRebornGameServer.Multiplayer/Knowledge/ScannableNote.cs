using System.Text;

namespace WorldsAdriftRebornGameServer.Multiplayer.Knowledge
{
    /// <summary>
    /// Builds the minimal <c>ScannableData</c> JSON the client's scanner prints as the scan
    /// NOTE, hand-rolled with no JSON dependency (the production Multiplayer assembly stays
    /// dependency-free; the test project re-parses the output with Newtonsoft to assert the
    /// contract).
    ///
    /// WHY: a scan response carries a <c>scanData</c> string the client parses with
    /// <c>ScannableData.Parse</c> (Unity <c>JsonUtility.FromJson&lt;ScannableData&gt;</c>) and
    /// only prints the note when the parse returns non-null
    /// (PlayerScannerToolVisualizer.cs:96-107, ScannableData.cs:186-210). We previously sent
    /// the raw asset GUID, which is not valid JSON, so <c>Parse</c> returned null and the note
    /// printed blank. <c>ScannableData</c> is <c>[Serializable]</c> with public FIELDS
    /// (<c>title</c>, <c>description</c>, ...), and <c>JsonUtility</c> maps by field name, so a
    /// minimal object with those two fields renders the note's heading and body.
    /// </summary>
    public static class ScannableNote
    {
        /// <summary>
        /// The <c>scanData</c> JSON for a databank note with the given <paramref name="title"/>
        /// and <paramref name="description"/>. Both are JSON-string-escaped, so quotes,
        /// backslashes and control characters in a note are safe. Produces, e.g.
        /// <c>{"title":"Ancient Databank","description":"..."}</c> - a valid
        /// <c>JsonUtility</c>-parseable <c>ScannableData</c>.
        /// </summary>
        public static string Json(string? title, string? description)
        {
            StringBuilder sb = new StringBuilder(64);
            sb.Append("{\"title\":");
            AppendJsonString(sb, title ?? "");
            sb.Append(",\"description\":");
            AppendJsonString(sb, description ?? "");
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendJsonString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
