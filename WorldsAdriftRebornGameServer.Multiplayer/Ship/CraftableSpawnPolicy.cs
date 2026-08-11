using System.Globalization;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// The three fields of a loose part's 1013 <c>CraftableSpawningState</c>
    /// (<c>spawning</c>, <c>timeLeft</c>, <c>totalTime</c>), as a pure value the
    /// serializer's 1013 branch reads back per entity.
    /// </summary>
    public readonly struct CraftableSpawnState
    {
        public CraftableSpawnState(bool spawning, float timeLeft, float totalTime)
        {
            Spawning = spawning;
            TimeLeft = timeLeft;
            TotalTime = totalTime;
        }

        /// <summary>True while the MATERIALIZE dissolve is playing; the client holds the part
        /// kinematic and blocks pickup while this is set (ShipPartVisualizer.cs:155-161,233).</summary>
        public bool Spawning { get; }

        /// <summary>Seconds left in the dissolve (drives the Materializer shader alpha-clip).</summary>
        public float TimeLeft { get; }

        /// <summary>Total dissolve length (the denominator for the shader progress).</summary>
        public float TotalTime { get; }
    }

    /// <summary>
    /// Pure rules for the crafted-part MATERIALIZE dissolve (the client's
    /// <c>CraftableSpawningVisualizer</c> Materializer shader, driven by the served 1013
    /// <c>CraftableSpawningState</c>).
    ///
    /// The client runs the dissolve only when <c>spawning</c> flips TRUE
    /// (CraftableSpawningVisualizer.cs:100-135); a freshly-crafted loose part must therefore
    /// be SEEDED spawning=true so the dissolve-in plays, then flipped to spawning=false after
    /// the dissolve so the part becomes non-kinematic and liftable. The flip-to-false is
    /// MANDATORY: a part left spawning=true is frozen in place and cannot be picked up
    /// (ShipPartVisualizer.OnSpawningUpdated holds it kinematic + blocks CanPickUp). A
    /// boot-restored / already-spawned part stays <see cref="Done"/> (no re-dissolve).
    ///
    /// Element-agnostic and dependency-free, so it unit-tests natively.
    /// </summary>
    public static class CraftableSpawnPolicy
    {
        /// <summary>The finished state: not spawning, no timers. Seeded on a settled part.</summary>
        public static readonly CraftableSpawnState Done = new CraftableSpawnState(false, 0f, 0f);

        /// <summary>
        /// The in-progress dissolve state a FRESH craft is seeded with: spawning=true with a
        /// full <paramref name="totalTime"/> (timeLeft==totalTime so the shader starts at
        /// progress 0). Flipped to <see cref="Done"/> after <paramref name="totalTime"/>.
        /// </summary>
        public static CraftableSpawnState Materializing(float totalTime) =>
            new CraftableSpawnState(true, totalTime, totalTime);

        /// <summary>
        /// Best-guess dissolve length in seconds. Retail drove <c>totalTime</c> from the
        /// server; its exact value is a live-capture unknown, so this is a sane default,
        /// overridable via WAREBORN_MATERIALIZE_SECONDS without a rebuild.
        /// </summary>
        public const float DefaultMaterializeSeconds = 2.0f;

        /// <summary>
        /// The dissolve length to use: the parsed positive value of <paramref name="rawEnv"/>
        /// (WAREBORN_MATERIALIZE_SECONDS), or <see cref="DefaultMaterializeSeconds"/> when it
        /// is blank, unparseable, or non-positive. Pure: the glue reads the env and passes it.
        /// </summary>
        public static float MaterializeSeconds(string? rawEnv)
        {
            if (!string.IsNullOrWhiteSpace(rawEnv)
                && float.TryParse(rawEnv.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                && parsed > 0f)
            {
                return parsed;
            }
            return DefaultMaterializeSeconds;
        }
    }
}
