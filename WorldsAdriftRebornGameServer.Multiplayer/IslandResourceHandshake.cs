namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The REAL Worlds Adrift island-resource placement mechanic, server half.
    ///
    /// Retail flow (VERIFIED against the decompiled client):
    ///   - the SERVER serves component 1010 IslandResourceSpawnerState on the island
    ///     and raises the <c>SpawnResources{ number, resourceType }</c> event on it
    ///     (gencode/Bossa.Travellers.Islands/IslandResourceSpawnerState.cs:1047 AddSpawnResources,
    ///     :88/:599 TriggerSpawnResources);
    ///   - the stock CLIENT's <c>IslandProxyVisualizer.OnSpawnResources</c>
    ///     (acs/IslandProxyVisualizer.cs:142) accumulates the count, then in its
    ///     Update loop samples its OWN island mesh via
    ///     <c>IslandSurfaceData.FindPlace</c> (acs/IslandSurfaceData.cs:131) - which
    ///     guarantees a point that is on the LOD0 surface AND physics-clear
    ///     (Physics.CheckSphere, :157) - and replies on component 1011
    ///     IslandResourceSpawnerClientState with
    ///     <c>TriggerSpawnResourcesReply(List&lt;SpawnResourceRequest&gt;)</c>
    ///     (acs/IslandProxyVisualizer.cs:231; gencode .../IslandResourceSpawnerClientState.cs:69);
    ///   - the SERVER (the worker half this build LOST, which we reimplement) consumes
    ///     the 1011 reply and spawns a real deposit entity at each client-provided
    ///     world position - always on ground, at any count, no hand-measured or
    ///     offline-guessed coordinate.
    ///
    /// This type is the PURE policy of the server half: how many to ask for, the
    /// clamp that stops a hostile or buggy client from asking us to spawn thousands,
    /// and the 1011 seed values the client reads back. Everything here is either a
    /// verified constant or an explicitly-reconstructed default, because the retail
    /// count/density and biome refdata did not survive (see
    /// docs/research/gathering/findings-resource-placement.md).
    /// </summary>
    public static class IslandResourceHandshake
    {
        /// <summary>
        /// The metal count requested per island when <see cref="CountEnvVar"/> is
        /// unset. RECONSTRUCTED: retail chose this from lost server refdata
        /// (initialMetalRockDeposits / metalDepositDensity * islandMeshCount). 40 is
        /// a healthy, sane starter density for Haven; tune live with the env knob.
        /// </summary>
        public const int DefaultMetalCount = 40;

        /// <summary>
        /// The hard upper clamp on how many deposits ONE island may ever spawn from
        /// the handshake, whatever the env knob or a client reply says. Trust bound:
        /// a modified client cannot make us register thousands of world entities.
        /// </summary>
        public const int MaxMetalCount = 200;

        /// <summary>The lower clamp - a count below zero is zero.</summary>
        public const int MinMetalCount = 0;

        /// <summary>
        /// 1011 IslandResourceSpawnerClientState.batchSize seed - how many placements
        /// the client computes and replies with per Update tick. The client defaults
        /// this to 30 itself (acs/IslandProxyVisualizer.cs:31) and overwrites the
        /// field from our seed (:82), so 30 reproduces the stock cadence. RECONSTRUCTED
        /// (the retail seed is lost) but matched to the client's own default.
        /// </summary>
        public const int BatchSize = 30;

        /// <summary>
        /// 1011 spawnInterval seed, seconds between reply batches. The client defaults
        /// to 10 s (acs/IslandProxyVisualizer.cs:33) and reads this field (:83); 5 s is
        /// a deliberately snappier reconstructed value so the first deposits appear a
        /// few seconds after checkout rather than after ten. Tune live.
        /// </summary>
        public const float SpawnIntervalSeconds = 5f;

        /// <summary>
        /// 1010 metalOnSurfaceProb seed. The client copies it (acs/IslandProxyVisualizer.cs:60)
        /// but <c>FindPlaceForMetalSpawn</c> then forces it to 1 anyway
        /// (acs/IslandSurfaceData.cs:184), so the value barely matters; 0.3 is the
        /// client field's own default (acs/IslandSurfaceData.cs:43).
        /// </summary>
        public const float MetalOnSurfaceProb = 0.3f;

        /// <summary>The count knob, per the task. Unset =&gt; <see cref="DefaultMetalCount"/>.</summary>
        public const string CountEnvVar = "WAREBORN_METAL_COUNT";

        /// <summary>
        /// The primary-path switch. Default ON: the handshake is the real mechanic and
        /// the point of this work. Set to 0/false/off/no to disable it and fall back to
        /// the hand-placed <see cref="MetalDeposits"/> static path (WAREBORN_SPAWN_DEPOSIT).
        /// </summary>
        public const string EnabledEnvVar = "WAREBORN_METAL_HANDSHAKE";

        /// <summary>Clamps a requested count into [<see cref="MinMetalCount"/>, <see cref="MaxMetalCount"/>].</summary>
        public static int ClampCount(int requested)
        {
            if (requested < MinMetalCount)
            {
                return MinMetalCount;
            }
            if (requested > MaxMetalCount)
            {
                return MaxMetalCount;
            }
            return requested;
        }

        /// <summary>
        /// The metal count to request, from a raw env value. A missing or unparseable
        /// value is <see cref="DefaultMetalCount"/>; a parseable one is clamped. Pure
        /// (env passed in) so the parse and clamp are unit-tested without touching the
        /// process environment.
        /// </summary>
        public static int MetalCount(string? env)
        {
            if (string.IsNullOrWhiteSpace(env))
            {
                return DefaultMetalCount;
            }
            if (!int.TryParse(env.Trim(), out int n))
            {
                return DefaultMetalCount;
            }
            return ClampCount(n);
        }

        /// <summary>The metal count to request, read from <see cref="CountEnvVar"/>.</summary>
        public static int MetalCount()
        {
            return MetalCount(System.Environment.GetEnvironmentVariable(CountEnvVar));
        }

        /// <summary>
        /// Whether the handshake is enabled, from a raw env value. Default ON; only an
        /// explicit 0/false/off/no disables. Pure for testing.
        /// </summary>
        public static bool Enabled(string? env)
        {
            if (string.IsNullOrWhiteSpace(env))
            {
                return true;
            }
            string v = env.Trim().ToLowerInvariant();
            return v != "0" && v != "false" && v != "off" && v != "no";
        }

        /// <summary>Whether the handshake is enabled, read from <see cref="EnabledEnvVar"/>.</summary>
        public static bool Enabled()
        {
            return Enabled(System.Environment.GetEnvironmentVariable(EnabledEnvVar));
        }
    }
}
