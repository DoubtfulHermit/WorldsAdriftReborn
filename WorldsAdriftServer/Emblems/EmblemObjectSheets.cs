using System.Globalization;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;

namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// The two hundred objects traced off the four artwork sheets, as the
    /// catalogue wants them.
    ///
    /// WHY THIS IS A RESOURCE AND NOT A GENERATED C# TABLE. The fifty devices
    /// before these live in <see cref="EmblemDeviceGeometry"/> as string literals,
    /// and that file is 321kB of one-line-per-device that no diff can be read and
    /// every build has to parse. Four times as much artwork the same way is a
    /// megabyte of source, twice that again in the assembly's UTF-16 string heap,
    /// and - the part that actually matters - a SECOND copy of the artwork that
    /// somebody has to remember to regenerate. What ships here is the tracer's own
    /// <c>emblem-objects.json</c>, embedded byte for byte, so re-running
    /// <c>tools/emblem-objects/trace_objects.py</c> is the whole of "the server now
    /// draws the new icon". The two cannot disagree because there is only one of
    /// them.
    ///
    /// The cost is a JSON parse, paid once, lazily, on the first request that
    /// needs the catalogue - measured at a few tens of milliseconds for the whole
    /// two hundred including building every winding index.
    ///
    /// ORDER IS INDEX ORDER AND INDEX ORDER IS FROZEN. These are appended to
    /// <see cref="EmblemObjects"/> after everything that shipped before them, and
    /// a layer stores its object as an index - so this loader does not trust the
    /// file's array order. It sorts by <see cref="Sheets"/> (an append-only
    /// declaration of which sheet comes after which) and then by the icon's
    /// printed number, and refuses to load a sheet it has not been told about.
    /// A retrace that shuffled the JSON therefore cannot shuffle anybody's crest,
    /// and a fifth sheet is one row added to the END of <see cref="Sheets"/>.
    /// </summary>
    internal static class EmblemObjectSheets
    {
        /// <summary>The unit the stored integers are in: 1000 = the box edge.</summary>
        internal const double Unit = 1000.0;

        // ------------------------------------------------------------ categories

        /// <summary>
        /// The panels these four sheets are shown under.
        ///
        /// The tab names are OURS, not the tracer's. Its category keys name the
        /// sheet a drawing came off; a player browsing for a torii gate is not
        /// looking for a filename. "Eastern" rather than "Japan" because every
        /// other name in this vocabulary - Badlands, Remnants, lamplight, timber -
        /// belongs to the world the crest hangs in, and a real country's name on an
        /// alliance shield is the one label that reads as coming from outside it.
        /// One string each if the maintainer disagrees.
        /// </summary>
        internal const string EasternCategory = "Eastern";

        internal const string SalvageCategory = "Salvage";

        internal const string OutlineCategory = "Outlines";

        internal const string SolidCategory = "Solids";

        /// <summary>
        /// The sheets, in the order their icons take indices. APPEND ONLY - a new
        /// sheet goes on the END of this list, because everything below a row here
        /// would shift if a row were inserted above it.
        /// </summary>
        private static readonly (string Key, string Category)[] Sheets =
        {
            ("japan", EasternCategory),
            ("objects", SalvageCategory),
            ("shapes-outline", OutlineCategory),
            ("shapes-solid", SolidCategory),
        };

        // ------------------------------------------------------------ suppression

        /// <summary>
        /// Objects that are IN the catalogue but not offered on the panel.
        ///
        /// HIDING, NOT DELETING, and the difference is the whole reason this list
        /// exists. Removing a row would renumber every object after it and silently
        /// redraw every saved crest that used one. A name here keeps its index, so
        /// a crest that already uses it still renders exactly as it did; it just
        /// stops being something new can be built from.
        ///
        /// TO SUPPRESS ONE: uncomment its line. TO BRING IT BACK: comment the line
        /// out again. TO REPLACE ONE: redraw it in its place on its sheet, re-run
        /// <c>trace_objects.py</c> and <c>verify_objects.py</c>, and leave this list
        /// alone - the index and the name are unchanged, so the new drawing lands
        /// under the old crests as well as the new ones.
        ///
        /// The eight below are the ones <c>tools/emblem-objects/README.md</c> grades
        /// BAD: legible on the sheet, illegible at the ~130px a device is actually
        /// drawn at, because the drawings carry more interior detail than that size
        /// can hold. They are listed and NOT suppressed, because that is an art call
        /// the maintainer has not made yet and shipping them beats hiding artwork
        /// nobody asked us to hide.
        /// </summary>
        private static readonly string[] Suppressed =
        {
            // "koi",          // japan 03  - interior scaling collapses into speckle
            // "turtle",       // japan 11  - shell pattern reads as noise
            // "komainu",      // japan 23  - mane detail fills the silhouette
            // "shishi-lion",  // japan 28  - same
            // "scorpion",     // japan 41  - segments merge
            // "ruins",        // objects 40 - broken-stroke masonry disappears
            // "airship",      // objects 41 - rigging disappears, hull reads as a blob
            // "crane",        // objects 46 - lattice disappears
        };

        /// <summary>
        /// The three forms where the outline and the solid sheets carry the SAME
        /// drawing, so there is no outline/solid contrast to offer.
        ///
        /// Declared here rather than inferred, and kept even though nothing surfaces
        /// a variant control yet: <c>verify_objects.py</c> fails the trace if a
        /// fourth one appears, and whoever adds that control needs the three names
        /// to grey it out on. See the pairing table in the tools README.
        /// </summary>
        private static readonly string[] Unpaired =
        {
            "dashed-ring", "vesica-leaf", "diamond-ring",
        };

        // ----------------------------------------------------------------- table

        internal readonly struct Icon
        {
            /// <summary>The name a player reads: "Torii gate".</summary>
            internal string Name { get; }

            /// <summary>The name the sheets and the tools use: "torii-gate". The
            /// stable identity, and the key <see cref="Suppressed"/> is written
            /// in.</summary>
            internal string Key { get; }

            internal string Category { get; }

            /// <summary>The shared name of an outline/solid pair, or null.</summary>
            internal string? Form { get; }

            /// <summary>"outline", "solid", or null.</summary>
            internal string? Variant { get; }

            /// <summary>Whether this form's two variants actually look different.
            /// False only for the three in <see cref="Unpaired"/>.</summary>
            internal bool Contrasts { get; }

            /// <summary>Whether the panel offers it. A hidden object still DRAWS -
            /// it has to, because a crest may already use it.</summary>
            internal bool Hidden { get; }

            internal EmblemPath Path { get; }

            internal Icon(
                string name, string key, string category, string? form, string? variant,
                bool contrasts, bool hidden, EmblemPath path)
            {
                Name = name;
                Key = key;
                Category = category;
                Form = form;
                Variant = variant;
                Contrasts = contrasts;
                Hidden = hidden;
                Path = path;
            }
        }

        internal static IReadOnlyList<Icon> All => Loaded.Value;

        internal const string ResourceName = "emblem-objects.json";

        private static readonly Lazy<IReadOnlyList<Icon>> Loaded =
            new Lazy<IReadOnlyList<Icon>>(Load, isThreadSafe: true);

        private static IReadOnlyList<Icon> Load()
        {
            JObject source = JObject.Parse(Text());

            if (source["objects"] is not JArray objects)
            {
                throw new InvalidOperationException("The embedded object sheets have no objects.");
            }

            // The unit the file declares, not the one we hope it used. A retrace at
            // a different scale would otherwise land every object silently wrong.
            double unit = (double?)source["unit"] ?? 0;
            if (unit != Unit)
            {
                throw new InvalidOperationException(
                    "The embedded object sheets are in units of " + unit.ToString(CultureInfo.InvariantCulture)
                    + ", not " + Unit.ToString(CultureInfo.InvariantCulture) + ".");
            }

            HashSet<string> suppressed = new HashSet<string>(Suppressed, StringComparer.Ordinal);
            HashSet<string> unpaired = new HashSet<string>(Unpaired, StringComparer.Ordinal);

            List<(int Sheet, int Index, Icon Icon)> loaded =
                new List<(int, int, Icon)>(objects.Count);

            foreach (JObject entry in objects.OfType<JObject>())
            {
                string key = (string?)entry["name"]
                    ?? throw new InvalidOperationException("An object on the sheets has no name.");
                string category = (string?)entry["category"]
                    ?? throw new InvalidOperationException(key + " has no category.");

                int sheet = Array.FindIndex(Sheets, s => string.Equals(s.Key, category, StringComparison.Ordinal));
                if (sheet < 0)
                {
                    // A sheet nobody declared. Refused rather than appended blind:
                    // where its icons sort decides what index they take, and taking
                    // a guess at that is how saved crests get redrawn.
                    throw new InvalidOperationException(
                        "Sheet '" + category + "' is not declared in EmblemObjectSheets.Sheets.");
                }

                string? form = (string?)entry["form"];

                loaded.Add((sheet, (int?)entry["index"] ?? 0, new Icon(
                    Display(key),
                    key,
                    Sheets[sheet].Category,
                    form,
                    (string?)entry["variant"],
                    form == null || !unpaired.Contains(form),
                    suppressed.Contains(key),
                    EmblemPath.ParseDrawing(
                        (string?)entry["path"]
                            ?? throw new InvalidOperationException(key + " has no path."),
                        unit))));
            }

            loaded.Sort((a, b) => a.Sheet != b.Sheet ? a.Sheet - b.Sheet : a.Index - b.Index);

            for (int i = 1; i < loaded.Count; i++)
            {
                if (loaded[i].Sheet == loaded[i - 1].Sheet && loaded[i].Index == loaded[i - 1].Index)
                {
                    throw new InvalidOperationException(
                        "Two objects claim slot " + loaded[i].Index.ToString(CultureInfo.InvariantCulture)
                        + " on the same sheet, so their order - and their indices - would be arbitrary.");
                }
            }

            return loaded.Select(l => l.Icon).ToArray();
        }

        private static string Text()
        {
            Assembly assembly = typeof(EmblemObjectSheets).Assembly;

            string name = assembly.GetManifestResourceNames()
                .SingleOrDefault(n => n.EndsWith(ResourceName, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("The embedded object sheets are missing.");

            using Stream stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException("The embedded object sheets could not be opened.");
            using StreamReader reader = new StreamReader(stream);

            return reader.ReadToEnd();
        }

        /// <summary>
        /// A sheet name as a player reads it: "kabuto-helmet" becomes "Kabuto
        /// helmet".
        ///
        /// The variant suffix is kept - "Hexagon outline", not "Hexagon" - because
        /// the panel's search box is flat across every category, and three shapes
        /// all called "Hexagon" in one list of results is worse than a long name.
        /// </summary>
        private static string Display(string key)
        {
            StringBuilder name = new StringBuilder(key.Length);

            foreach (char c in key)
            {
                name.Append(c == '-' ? ' ' : c);
            }

            if (name.Length > 0) name[0] = char.ToUpperInvariant(name[0]);

            return name.ToString();
        }
    }
}
