namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The SAFE FAILURE MODE of the island resource handshake.
    ///
    /// The handshake is the faithful retail mechanic and the primary path: the server
    /// asks (1010 SpawnResources), the client surface-samples its own physics-checked
    /// island mesh, and replies (1011). But it has one live unknown that no test on this
    /// side can close - whether the stock <c>IslandProxyVisualizer</c> actually enables
    /// and replies against THIS server. If it does not, the naive outcome is a world with
    /// NO ore at all, which is strictly worse than the hand-placed deposits it replaced.
    ///
    /// So: when the first SpawnResources request goes out for an island, a one-shot
    /// deadline is armed. If that deadline passes with ZERO deposits spawned from client
    /// replies, the server spawns the existing hand-placed <see cref="MetalDeposits"/>
    /// table instead and says so, loudly and unambiguously, in one line.
    ///
    /// WHY "ZERO", NOT "FEWER THAN REQUESTED". A partial reply is proof the mechanic
    /// works; the client replies in batches (batchSize 30 every spawnInterval seconds),
    /// so "some but not all yet" is the NORMAL mid-flight state and must not trigger a
    /// fallback that would mix hand-placed rocks into a working world. Only a total
    /// silence - or a reply whose every placement was refused by
    /// <see cref="IslandBounds"/> - counts as failure.
    ///
    /// WHY IT LATCHES. Once the fallback has placed the static table, later client
    /// replies for that island are refused. The alternative - honouring a reply that
    /// arrives one second after the deadline - leaves the world holding both sets, which
    /// is exactly the confusing half-state the operator cannot diagnose from a log. One
    /// island, one resolved path, one line saying which.
    /// </summary>
    public static class IslandResourceFallback
    {
        /// <summary>
        /// Seconds from the first SpawnResources request to the fallback deadline.
        /// Generous on purpose: the joiner passes a loading barrier, the island's mesh
        /// and its <c>PopulateStaticPrefabs</c> must finish, and the client's own reply
        /// cadence is <see cref="IslandResourceHandshake.SpawnIntervalSeconds"/> per
        /// batch. 90 s strongly favours the REAL path; it is the operator's call to
        /// shorten it once the handshake is proven live.
        /// </summary>
        public const double DefaultSeconds = 90.0;

        /// <summary>Floor for <see cref="Seconds"/> - below this the deadline would beat a normal client.</summary>
        public const double MinSeconds = 10.0;

        /// <summary>Ceiling for <see cref="Seconds"/> - ten minutes of an empty world is not a fallback.</summary>
        public const double MaxSeconds = 600.0;

        /// <summary>The deadline knob. Unset =&gt; <see cref="DefaultSeconds"/>.</summary>
        public const string SecondsEnvVar = "WAREBORN_METAL_FALLBACK_SECONDS";

        /// <summary>
        /// The fallback kill switch. Default ON - the whole point is that the world is
        /// never left empty. Set to 0/false/off/no to disable it, which is only sensible
        /// when deliberately testing whether the handshake alone works.
        /// </summary>
        public const string EnabledEnvVar = "WAREBORN_METAL_FALLBACK";

        /// <summary>
        /// The exact marker written when the handshake path is the one that placed the
        /// ore. Greppable, and paired with <see cref="FallbackMarker"/> so exactly one of
        /// the two appears per island per run.
        /// </summary>
        public const string HandshakeMarker = "resource-handshake: PATH=handshake";

        /// <summary>The exact marker written when the static hand-placed table was used instead.</summary>
        public const string FallbackMarker = "resource-handshake: PATH=fallback";

        /// <summary>Clamps a deadline into [<see cref="MinSeconds"/>, <see cref="MaxSeconds"/>].</summary>
        public static double ClampSeconds(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < MinSeconds)
            {
                return MinSeconds;
            }
            return seconds > MaxSeconds ? MaxSeconds : seconds;
        }

        /// <summary>
        /// The deadline from a raw env value. Missing or unparseable =&gt;
        /// <see cref="DefaultSeconds"/>; parseable =&gt; clamped. Pure (env passed in).
        /// </summary>
        public static double Seconds(string? env)
        {
            if (string.IsNullOrWhiteSpace(env))
            {
                return DefaultSeconds;
            }
            if (!double.TryParse(env.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double s))
            {
                return DefaultSeconds;
            }
            return ClampSeconds(s);
        }

        /// <summary>The deadline, read from <see cref="SecondsEnvVar"/>.</summary>
        public static double Seconds()
        {
            return Seconds(System.Environment.GetEnvironmentVariable(SecondsEnvVar));
        }

        /// <summary>Whether the fallback is armed, from a raw env value. Default ON. Pure for testing.</summary>
        public static bool Enabled(string? env)
        {
            if (string.IsNullOrWhiteSpace(env))
            {
                return true;
            }
            string v = env.Trim().ToLowerInvariant();
            return v != "0" && v != "false" && v != "off" && v != "no";
        }

        /// <summary>Whether the fallback is armed, read from <see cref="EnabledEnvVar"/>.</summary>
        public static bool Enabled()
        {
            return Enabled(System.Environment.GetEnvironmentVariable(EnabledEnvVar));
        }

        /// <summary>
        /// THE RULE. Whether the deadline should place the static table, given how many
        /// deposits client replies have already produced for this island and whether the
        /// fallback has already fired. See the type remarks for why the threshold is
        /// exactly zero.
        /// </summary>
        public static bool ShouldFallBack(int spawnedFromReplies, bool alreadyFiredOnce)
        {
            return !alreadyFiredOnce && spawnedFromReplies <= 0;
        }

        /// <summary>
        /// The single line the operator greps to see the handshake WORKED. Contains
        /// <see cref="HandshakeMarker"/> and the phrase "reply received, spawned N".
        /// </summary>
        public static string HandshakeLine(long islandEntityId, int spawned, int requested)
        {
            return HandshakeMarker + ": reply received, spawned " + spawned + " of " + requested
                + " deposit(s) on island " + islandEntityId
                + " at client-chosen, physics-checked ground positions. Static fallback NOT used.";
        }

        /// <summary>
        /// The single line the operator greps to see the handshake FAILED and the static
        /// table was placed. Contains <see cref="FallbackMarker"/> and the phrase
        /// "NO reply after Ts, falling back to static placements".
        /// </summary>
        public static string FallbackLine(long islandEntityId, double seconds, int staticCount)
        {
            return FallbackMarker + ": NO reply after "
                + seconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
                + "s, falling back to static placements on island " + islandEntityId
                + " - spawned " + staticCount + " hand-placed deposit(s). "
                + "The client never returned a usable 1011 placement.";
        }

        /// <summary>
        /// The line written when the deadline passes but the handshake HAD already
        /// produced deposits - i.e. the fallback stood down. Also carries
        /// <see cref="HandshakeMarker"/>, so the "which path" grep is total.
        /// </summary>
        public static string StoodDownLine(long islandEntityId, int spawned, int requested)
        {
            return HandshakeMarker + ": deadline reached with " + spawned + " of " + requested
                + " deposit(s) already spawned from client replies on island " + islandEntityId
                + "; static fallback stood down.";
        }
    }
}
