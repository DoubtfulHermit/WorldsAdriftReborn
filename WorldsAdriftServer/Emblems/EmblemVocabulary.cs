namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// The closed vocabulary an alliance emblem is composed from - WAREBORN
    /// TUNING, ours, invented here.
    ///
    /// PROVENANCE, stated once and plainly. Bossa never shipped an emblem editor.
    /// <c>emblemUrl</c> is RECOVERED and it is READ-ONLY: the client GETs it with
    /// <c>SpriteDownloader</c> and never sends it, and there is no picker,
    /// uploader or composer anywhere in the decompile
    /// (docs/research/findings-social-api.md). Everything in this file - the
    /// shapes, the divisions, the charges, the palette - is a Wareborn addition
    /// that exists ONLY because the client will render whatever image the URL we
    /// serve happens to return.
    ///
    /// WHY A VOCABULARY RATHER THAN AN UPLOAD. An emblem here is six small
    /// integers, every one of them an index into a table below. That makes an
    /// emblem <b>incapable</b> of being malformed: there is no file, no MIME type,
    /// no path, no XML and no third-party host, so there is nothing to validate
    /// beyond "is this index in range", and nothing an attacker can put in the
    /// field that is not already one of the pictures we drew. It also keeps every
    /// alliance crest inside one visual language instead of whatever somebody
    /// pasted.
    ///
    /// Pure data. No I/O, no colour maths, no rendering - <see cref="EmblemPainter"/>
    /// reads these tables and nothing writes them.
    /// </summary>
    internal static class EmblemVocabulary
    {
        // ------------------------------------------------------------- shapes

        /// <summary>The outline the field is cut to.</summary>
        internal enum Shape
        {
            /// <summary>The classic heater shield: square shoulders, pointed foot.</summary>
            Heater = 0,

            /// <summary>A plain disc. The most legible at roster size.</summary>
            Round = 1,

            /// <summary>A flat-top hexagon - the shape Worlds Adrift's own marks lean on.</summary>
            Hex = 2,

            /// <summary>A lozenge stood on its point.</summary>
            Kite = 3,

            /// <summary>A hanging banner with a swallow-tail cut into the foot.</summary>
            Banner = 4,
        }

        /// <summary>Shape names for the builder's dropdown, indexed by <see cref="Shape"/>.</summary>
        internal static readonly IReadOnlyList<string> ShapeNames = new[]
        {
            "Heater shield", "Roundel", "Hexagon", "Lozenge", "Banner",
        };

        // ---------------------------------------------------------- divisions

        /// <summary>
        /// How the field is split between the two field colours. Named after the
        /// heraldic divisions because that is what they are, and because a fixed
        /// set of divisions is what makes two alliances look like they belong to
        /// the same world.
        /// </summary>
        internal enum Division
        {
            /// <summary>One colour, whole field.</summary>
            Solid = 0,

            /// <summary>Split horizontally: top field, bottom detail.</summary>
            PerFess = 1,

            /// <summary>Split vertically.</summary>
            PerPale = 2,

            /// <summary>Split on the diagonal.</summary>
            PerBend = 3,

            /// <summary>A chevron rising from the foot.</summary>
            Chevron = 4,

            /// <summary>Four quarters, alternating.</summary>
            Quarterly = 5,

            /// <summary>A border band all the way round.</summary>
            Bordure = 6,

            /// <summary>A band across the top.</summary>
            Chief = 7,

            /// <summary>A vertical stripe up the middle.</summary>
            Pale = 8,

            /// <summary>A horizontal band across the middle.</summary>
            Fess = 9,
        }

        internal static readonly IReadOnlyList<string> DivisionNames = new[]
        {
            "Solid", "Per fess", "Per pale", "Per bend", "Chevron",
            "Quarterly", "Bordure", "Chief", "Pale", "Fess",
        };

        // ------------------------------------------------------------ charges

        /// <summary>
        /// The device on the field.
        ///
        /// TWO HALVES, ON PURPOSE. The entries NAMED here are drawn in code: plain
        /// heraldic geometry that still reads at the sixteen-pixel roster crest,
        /// where anything with interior detail collapses into a smudge. From
        /// <see cref="FirstDrawnDevice"/> upwards the entries are the fifty tribal
        /// icons traced off the artwork sheet
        /// (tools/emblem-devices/device-sheet.png); they carry no enum names
        /// because fifty of them would be fifty lines restating
        /// <see cref="EmblemDeviceGeometry.Names"/>, and nothing in the renderer
        /// dispatches on which one it is - it fills whichever outline
        /// <see cref="EmblemGeometry.Device"/> hands back. Values above the named
        /// ones are legal and expected; <see cref="ChargeCount"/> is the bound.
        ///
        /// WHAT WAS DROPPED, AND WHY THE VERSION MOVED. Version 1 also had a
        /// Compass, an Anchor and a Sun drawn in code. The sheet draws all three
        /// better, so the procedural ones are gone and the indices after them
        /// shifted - which is exactly the change the leading version field in an
        /// emblem code exists for. Nothing about a saved crest changes silently:
        /// see <see cref="EmblemSpec.TryParse"/>, which reads a version 1 code
        /// through <see cref="MigrateCharge"/> and lands it on the device of the
        /// same NAME.
        /// </summary>
        internal enum Charge
        {
            /// <summary>No charge - the field and its division alone.</summary>
            None = 0,
            Hexagon = 1,
            Star = 2,
            Gear = 3,
            Bolt = 4,
            Ring = 5,
            Triangle = 6,
            Crescent = 7,
            Saltire = 8,
            Cross = 9,

            /// <summary>Three stacked chevrons - reads as rank stripes, and is the
            /// one device here that is not a silhouette of an object.</summary>
            Chevrons = 10,
        }

        /// <summary>
        /// The first charge index that is traced artwork rather than code. Also
        /// the count of the drawn-in-code half, since that half starts at zero.
        /// </summary>
        internal const int FirstDrawnDevice = 11;

        /// <summary>Whether a charge is one of the traced devices.</summary>
        internal static bool IsDrawnDevice(Charge charge) => (int)charge >= FirstDrawnDevice;

        private static readonly string[] GeometricChargeNames =
        {
            "None", "Hexagon", "Star", "Gear", "Bolt", "Ring",
            "Triangle", "Crescent", "Saltire", "Cross", "Chevrons",
        };

        internal static readonly IReadOnlyList<string> ChargeNames = BuildChargeNames();

        private static string[] BuildChargeNames()
        {
            string[] names = new string[GeometricChargeNames.Length + EmblemDeviceGeometry.Names.Count];

            GeometricChargeNames.CopyTo(names, 0);
            for (int i = 0; i < EmblemDeviceGeometry.Names.Count; i++)
            {
                names[GeometricChargeNames.Length + i] = EmblemDeviceGeometry.Names[i];
            }

            return names;
        }

        // ---------------------------------------------------- version 1 charges

        /// <summary>
        /// How many charges version 1 of the code had.
        ///
        /// Frozen. It is not "the old value of <see cref="ChargeCount"/>" that
        /// happens to be stale - it is the width of the index space every code
        /// written before this change was drawn from, and reading one of those
        /// codes needs the number the writer used, not the number the table has
        /// grown to.
        /// </summary>
        internal const int LegacyChargeCount = 14;

        /// <summary>
        /// A version 1 charge index, as the current index for the SAME device.
        ///
        /// Every entry is the device it always was. The three that moved rather
        /// than merely shifting - Compass rose, Anchor, Sun - land on the traced
        /// drawing of the same subject, because that is the whole reason the
        /// procedural ones were dropped: the sheet has a better one of each.
        /// </summary>
        internal static int MigrateCharge(int legacy)
        {
            if (legacy < 0 || legacy >= LegacyChargeCount) return (int)Charge.None;
            return LegacyCharges[legacy];
        }

        private static readonly int[] LegacyCharges =
        {
            (int)Charge.None,
            (int)Charge.Hexagon,
            (int)Charge.Star,
            (int)Charge.Gear,
            FirstDrawnDevice + 24,   // Compass, now the drawn compass rose (sheet 25)
            (int)Charge.Bolt,
            (int)Charge.Ring,
            (int)Charge.Triangle,
            (int)Charge.Crescent,
            (int)Charge.Saltire,
            (int)Charge.Cross,
            FirstDrawnDevice + 31,   // Anchor, now the drawn anchor (sheet 32)
            (int)Charge.Chevrons,
            FirstDrawnDevice + 29,   // Sun, now the drawn sun (sheet 30)
        };

        // ------------------------------------------------------------ palette

        /// <summary>
        /// The colour palette, as packed 0xRRGGBB.
        ///
        /// Sixteen entries, and deliberately not a colour picker. Two reasons: an
        /// index cannot be malformed where a "#rrggbb" string can, and a curated
        /// set keeps the crests inside one world's palette. The first eight are
        /// the muted airship-and-weathered-timber tones the rest of this server's
        /// pages already use; the last eight are the saturated accents an emblem
        /// needs to read at roster size, including the four map tier hues so a
        /// crest and the live map look like the same game.
        /// </summary>
        internal static readonly IReadOnlyList<int> Palette = new[]
        {
            0x1E2833, // 0  midnight    - the dark ink the map uses for deep ocean
            0x3C4A57, // 1  slate
            0x6E7F8B, // 2  fog
            0xC9D3D8, // 3  bone
            0xF2EDE1, // 4  canvas
            0x7D4D2A, // 5  timber      - the deck-plank brown of the patch pages
            0xB07A46, // 6  brass
            0xE0B070, // 7  lamplight
            0xA8321F, // 8  rust        - the alert red the console uses
            0xD9603C, // 9  ember
            0x4B934F, // 10 wilderness  - map tier 1
            0x204C8A, // 11 expanse     - map tier 2
            0xBC9BE2, // 12 remnants    - map tier 3
            0xEED059, // 13 badlands    - map tier 4
            0x2C6B52, // 14 verdigris
            0x59C3D1, // 15 skyglass
        };

        internal static readonly IReadOnlyList<string> PaletteNames = new[]
        {
            "Midnight", "Slate", "Fog", "Bone", "Canvas", "Timber", "Brass",
            "Lamplight", "Rust", "Ember", "Wilderness", "Expanse", "Remnants",
            "Badlands", "Verdigris", "Skyglass",
        };

        /// <summary>The ink every outline is drawn in. Not a choice - one dark edge
        /// is what stops two adjacent palette colours from smearing into each
        /// other at roster size, and letting it be picked would let a player pick
        /// the one value that makes their own crest illegible.</summary>
        internal const int OutlineInk = 0x141B22;

        internal static int ShapeCount => ShapeNames.Count;
        internal static int DivisionCount => DivisionNames.Count;
        internal static int ChargeCount => ChargeNames.Count;
        internal static int ColourCount => Palette.Count;

        /// <summary>The packed colour at an index that has ALREADY been validated
        /// by <see cref="EmblemSpec"/>. Clamped rather than thrown, because a
        /// renderer that throws mid-request answers a player with a 500 where a
        /// slightly wrong colour would have been a picture.</summary>
        internal static int ColourAt(int index)
        {
            if (index < 0) index = 0;
            if (index >= Palette.Count) index = Palette.Count - 1;
            return Palette[index];
        }
    }
}
