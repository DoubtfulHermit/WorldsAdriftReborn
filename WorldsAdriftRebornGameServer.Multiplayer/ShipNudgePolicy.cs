namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// One parsed ship-nudge instruction: a translation in metres, in global
    /// axes, to add to the hull's current commanded position.
    /// </summary>
    public readonly struct ShipNudge : IEquatable<ShipNudge>
    {
        public ShipNudge(double dx, double dy, double dz)
        {
            Dx = dx;
            Dy = dy;
            Dz = dz;
        }

        public double Dx { get; }
        public double Dy { get; }
        public double Dz { get; }

        /// <summary>The straight-line distance of the nudge, in metres.</summary>
        public double Magnitude => Math.Sqrt(Dx * Dx + Dy * Dy + Dz * Dz);

        public bool Equals(ShipNudge other) => Dx == other.Dx && Dy == other.Dy && Dz == other.Dz;

        public override bool Equals(object? obj) => obj is ShipNudge other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Dx, Dy, Dz);

        public override string ToString() =>
            "(" + Dx.ToString("0.##") + ", " + Dy.ToString("0.##") + ", " + Dz.ToString("0.##") + ") m";
    }

    /// <summary>
    /// Parses the step-3 CARRY-TEST trigger file into a single ship translation.
    /// Pure, so the grammar is pinned by tests and not by watching a client - the
    /// same footing as <see cref="TeleportPolicy.TryParseCommand"/>, which this
    /// deliberately mirrors so the two operator files read alike.
    ///
    /// WHAT STEP 3 IS FOR. The one thing findings-first-ship.md could not settle
    /// is whether a player STANDING ON a <c>PathFollower</c>-driven hull is
    /// carried with it - there is no explicit carry code, only PhysX friction
    /// between the player's dynamic rigidbody and the hull's kinematic mesh
    /// colliders. This file fires exactly ONE 1130 control point that translates
    /// the ship a few metres, so a human can stand on the beams, trigger it, and
    /// watch whether they travel with it BEFORE the ferry is trusted.
    ///
    /// THE GRAMMAR, tiny because a human types it under an <c>echo</c>:
    /// <code>
    ///   (blank)          -- the default nudge: 5 m north (+Z)
    ///   nudge            -- same, spelled out
    ///   nudge &lt;metres&gt;   -- that many metres north (+Z); negative = south
    ///   &lt;dx&gt; &lt;dy&gt; &lt;dz&gt;    -- an explicit translation in metres, global axes
    ///   # anything        -- comment, ignored
    /// </code>
    /// North is +Z because that is the direction the hull sits from the spawn
    /// point (WorldEntities.ShipFrame: same X, +12 m Z), so the default nudge
    /// pushes it further along the straight line the player already walked.
    /// </summary>
    public static class ShipNudgePolicy
    {
        /// <summary>The default carry-test translation: 5 m north, matching the doc's "~5 m".</summary>
        public static readonly ShipNudge Default = new ShipNudge(0.0, 0.0, 5.0);

        /// <summary>The keyword form's own name, so the log and the parser agree on it.</summary>
        public const string Keyword = "nudge";

        /// <summary>
        /// Parses one line into a nudge. Returns false for a comment or garbage
        /// and puts the reason in <paramref name="error"/>; an EMPTY reason with a
        /// false result means "there was nothing to do" (a comment), which the
        /// caller must not log as a failure. A genuinely blank line, by contrast,
        /// IS a command - the default nudge - because the whole point of this file
        /// is that <c>echo &gt; /tmp/wareborn-ship</c> should just work.
        /// </summary>
        public static bool TryParseCommand(string? line, out ShipNudge nudge, out string error)
        {
            nudge = default;
            error = string.Empty;

            if (line == null)
            {
                return false;
            }

            string trimmed = line.Trim();

            // A comment is "nothing to do", not an error: no message, so a file of
            // comments does not spam.
            if (trimmed.Length > 0 && trimmed[0] == '#')
            {
                return false;
            }

            string[] parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            // Blank line, or the bare keyword: the default nudge.
            if (parts.Length == 0
                || (parts.Length == 1 && parts[0].Equals(Keyword, StringComparison.OrdinalIgnoreCase)))
            {
                nudge = Default;
                return true;
            }

            // `nudge <metres>`: a distance north.
            if (parts.Length == 2 && parts[0].Equals(Keyword, StringComparison.OrdinalIgnoreCase))
            {
                if (!TryParseMetres(parts[1], out double north))
                {
                    error = "'" + parts[1] + "' is not a distance in metres";
                    return false;
                }
                nudge = new ShipNudge(0.0, 0.0, north);
                return true;
            }

            // `<dx> <dy> <dz>`: an explicit translation.
            if (parts.Length == 3)
            {
                if (!TryParseMetres(parts[0], out double dx)
                    || !TryParseMetres(parts[1], out double dy)
                    || !TryParseMetres(parts[2], out double dz))
                {
                    error = "expected three metre values '<dx> <dy> <dz>', got '" + trimmed + "'";
                    return false;
                }
                nudge = new ShipNudge(dx, dy, dz);
                return true;
            }

            error = "expected blank, 'nudge', 'nudge <metres>' or '<dx> <dy> <dz>', got '" + trimmed + "'";
            return false;
        }

        private static bool TryParseMetres(string token, out double metres)
        {
            return double.TryParse(token, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out metres)
                && !double.IsNaN(metres) && !double.IsInfinity(metres);
        }
    }
}
