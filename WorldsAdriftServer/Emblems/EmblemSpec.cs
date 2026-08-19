using System.Globalization;

namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// One alliance emblem, as six indices into <see cref="EmblemVocabulary"/>.
    ///
    /// WAREBORN TUNING - the whole builder is ours; see the provenance note on
    /// <see cref="EmblemVocabulary"/>.
    ///
    /// THE POINT OF THIS TYPE is that an emblem cannot be malformed. Every field
    /// is an index into a closed table, so <see cref="TryParse"/> is total: it
    /// either produces a spec whose every component is in range, or it produces
    /// nothing. There is no free text, no length to cap, no path to escape and no
    /// host to resolve. That is what replaced the file upload and the
    /// third-party-URL designs, and it is why this file contains no validation
    /// beyond a bounds check.
    ///
    /// THE CODE FORM is <c>v-shape-division-charge-field-detail-charge</c>, seven
    /// decimal integers separated by hyphens, e.g. <c>1-0-6-3-1-7-13</c>. It is
    /// written into the alliance's stored <c>emblem_url</c> (behind the
    /// <see cref="EmblemUrlPolicy.Marker"/> prefix) and into the query string of
    /// the PNG route, so it has to survive a URL and a database column with no
    /// escaping: digits and hyphens do.
    ///
    /// Pure: no clock, no disk, no request.
    /// </summary>
    internal readonly struct EmblemSpec : IEquatable<EmblemSpec>
    {
        /// <summary>
        /// The code's leading version number.
        ///
        /// It exists so the vocabulary can grow without silently re-colouring
        /// every existing crest. Appending to the tables is safe (old indices keep
        /// their meaning); REORDERING or removing an entry is not, and that is the
        /// day this number changes and old codes get read by a compatibility path
        /// rather than by luck.
        ///
        /// That day was version 2. The device table grew from fourteen to
        /// <see cref="EmblemVocabulary.ChargeCount"/> when the drawn sheet landed,
        /// and three procedural devices the sheet draws better were dropped, which
        /// shifted every index after them. So a version 1 code is not read as if it
        /// were a version 2 one - it is read through
        /// <see cref="EmblemVocabulary.MigrateCharge"/>, which maps it to the
        /// device of the same name. The alternative, refusing version 1 outright,
        /// would have been the worst outcome available: every stored crest would
        /// have fallen back to its alliance's generated default, so a table growing
        /// would silently have wiped every emblem anybody had built.
        /// </summary>
        internal const int Version = 2;

        /// <summary>
        /// The one older code form still in the database. Read, never written:
        /// <see cref="ToCode"/> always emits <see cref="Version"/>, so a crest
        /// re-saved through the builder is stored in the current form and the
        /// compatibility path only has to survive, not spread.
        /// </summary>
        private const int LegacyVersion = 1;

        internal EmblemVocabulary.Shape Shape { get; }
        internal EmblemVocabulary.Division Division { get; }
        internal EmblemVocabulary.Charge Charge { get; }

        /// <summary>The field's main colour, as a <see cref="EmblemVocabulary.Palette"/> index.</summary>
        internal int FieldColour { get; }

        /// <summary>The second colour the division paints with.</summary>
        internal int DetailColour { get; }

        /// <summary>The charge's colour.</summary>
        internal int ChargeColour { get; }

        private EmblemSpec(
            EmblemVocabulary.Shape shape,
            EmblemVocabulary.Division division,
            EmblemVocabulary.Charge charge,
            int fieldColour,
            int detailColour,
            int chargeColour)
        {
            Shape = shape;
            Division = division;
            Charge = charge;
            FieldColour = fieldColour;
            DetailColour = detailColour;
            ChargeColour = chargeColour;
        }

        /// <summary>
        /// Builds a spec from raw indices, refusing anything out of range.
        ///
        /// Out of range is a REFUSAL rather than a clamp because these arrive from
        /// a form: a player who picked charge 40 picked nothing, and quietly
        /// giving them charge 13 would look like the builder ignoring them.
        /// </summary>
        internal static bool TryCreate(
            int shape, int division, int charge, int fieldColour, int detailColour, int chargeColour,
            out EmblemSpec spec)
        {
            spec = default;

            if (shape < 0 || shape >= EmblemVocabulary.ShapeCount) return false;
            if (division < 0 || division >= EmblemVocabulary.DivisionCount) return false;
            if (charge < 0 || charge >= EmblemVocabulary.ChargeCount) return false;
            if (fieldColour < 0 || fieldColour >= EmblemVocabulary.ColourCount) return false;
            if (detailColour < 0 || detailColour >= EmblemVocabulary.ColourCount) return false;
            if (chargeColour < 0 || chargeColour >= EmblemVocabulary.ColourCount) return false;

            spec = new EmblemSpec(
                (EmblemVocabulary.Shape)shape,
                (EmblemVocabulary.Division)division,
                (EmblemVocabulary.Charge)charge,
                fieldColour, detailColour, chargeColour);
            return true;
        }

        /// <summary>
        /// Parses a code. Total: every rejection returns false and leaves
        /// <paramref name="spec"/> default, and nothing here throws on any input
        /// including null, empty, absurdly long, or full of unicode.
        /// </summary>
        internal static bool TryParse(string? code, out EmblemSpec spec)
        {
            spec = default;

            if (string.IsNullOrEmpty(code)) return false;

            // A cheap length gate BEFORE the split, so a megabyte of hyphens in a
            // query string cannot make us allocate a million-element array.
            if (code!.Length > 64) return false;

            string[] parts = code.Split('-');
            if (parts.Length != 7) return false;

            int[] values = new int[7];
            for (int i = 0; i < 7; i++)
            {
                // NumberStyles.None: no sign, no whitespace, no thousands
                // separator. "+1" and " 1" and "1 " are all refused, so exactly
                // one string maps to each spec and the code is canonical.
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]))
                {
                    return false;
                }
            }

            int charge = values[3];

            if (values[0] == LegacyVersion)
            {
                // A version 1 charge index that was out of range then is still not
                // an emblem now: migrating it would invent a device the player
                // never chose. Only indices that were valid get carried over.
                if (charge < 0 || charge >= EmblemVocabulary.LegacyChargeCount) return false;

                charge = EmblemVocabulary.MigrateCharge(charge);
            }
            else if (values[0] != Version)
            {
                return false;
            }

            return TryCreate(values[1], values[2], charge, values[4], values[5], values[6], out spec);
        }

        /// <summary>
        /// The canonical code for this spec. Round-trips through
        /// <see cref="TryParse"/> exactly, which is what lets the PNG route treat
        /// the code as a cache key.
        /// </summary>
        internal string ToCode()
        {
            return string.Join("-", new[]
            {
                Version,
                (int)Shape,
                (int)Division,
                (int)Charge,
                FieldColour,
                DetailColour,
                ChargeColour,
            }.Select(v => v.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// The crest an alliance gets before anybody opens the builder.
        ///
        /// Derived from the alliance's own GUID rather than being one fixed
        /// design, so every alliance starts with a distinct crest instead of the
        /// client's grey placeholder, and so an alliance that never touches the
        /// builder still looks deliberate. Deterministic: the same GUID always
        /// yields the same emblem, on any machine and any boot, because it reads
        /// the GUID's own bytes and does no hashing that could change with the
        /// runtime (<c>string.GetHashCode</c> is randomised per process and would
        /// have re-rolled every crest on every restart).
        /// </summary>
        internal static EmblemSpec DefaultFor(Guid allianceId)
        {
            byte[] bytes = allianceId.ToByteArray();

            // Fold all sixteen bytes into each choice so that two alliances whose
            // GUIDs differ anywhere differ in the crest, rather than only in the
            // one byte a given field happened to read.
            int a = Mix(bytes, 0), b = Mix(bytes, 1), c = Mix(bytes, 2);
            int d = Mix(bytes, 3), e = Mix(bytes, 4), f = Mix(bytes, 5);

            int field = d % EmblemVocabulary.ColourCount;

            // The detail and the charge must not land on the field colour, or the
            // division and the device vanish into the background - the one way a
            // generated crest can come out looking broken rather than merely
            // unlucky. Rotating off the collision keeps it deterministic.
            int detail = e % EmblemVocabulary.ColourCount;
            if (detail == field) detail = (detail + 5) % EmblemVocabulary.ColourCount;

            int chargeColour = f % EmblemVocabulary.ColourCount;
            if (chargeColour == field) chargeColour = (chargeColour + 9) % EmblemVocabulary.ColourCount;
            if (chargeColour == detail) chargeColour = (chargeColour + 3) % EmblemVocabulary.ColourCount;
            if (chargeColour == field) chargeColour = (chargeColour + 1) % EmblemVocabulary.ColourCount;

            // The device is rolled in the VERSION 1 index space and then migrated,
            // not rolled across the table as it stands today. That is what keeps a
            // growing table from re-rolling crests: an alliance that has never
            // opened the builder has no stored code, so its crest is recomputed on
            // every request, and dividing by a table that got longer would have
            // handed it a different device the day the sheet landed. Never index 0
            // (None) either - a generated crest with no device reads as "unset",
            // which is exactly what this replaces.
            int legacyCharge = 1 + (c % (EmblemVocabulary.LegacyChargeCount - 1));

            TryCreate(
                a % EmblemVocabulary.ShapeCount,
                b % EmblemVocabulary.DivisionCount,
                EmblemVocabulary.MigrateCharge(legacyCharge),
                field, detail, chargeColour,
                out EmblemSpec spec);

            return spec;
        }

        /// <summary>
        /// An FNV-1a fold of all sixteen GUID bytes, salted by which field is
        /// asking. Not cryptography - it just has to be stable and to spread.
        /// </summary>
        private static int Mix(byte[] bytes, int salt)
        {
            unchecked
            {
                uint hash = 2166136261u ^ (uint)salt;
                foreach (byte value in bytes)
                {
                    hash ^= value;
                    hash *= 16777619u;
                }
                return (int)(hash & 0x7FFFFFFF);
            }
        }

        public bool Equals(EmblemSpec other) =>
            Shape == other.Shape && Division == other.Division && Charge == other.Charge
            && FieldColour == other.FieldColour && DetailColour == other.DetailColour
            && ChargeColour == other.ChargeColour;

        public override bool Equals(object? obj) => obj is EmblemSpec other && Equals(other);

        public override int GetHashCode() => ToCode().GetHashCode(StringComparison.Ordinal);

        public override string ToString() => ToCode();
    }
}
