namespace WorldsAdriftRebornGameServer.Multiplayer.Walls
{
    /// <summary>
    /// EVERY DECISION THE WEATHER-WALL FEATURE MAKES, in the assembly that has a test
    /// project. Nothing here touches ENet, the game's assemblies or the process
    /// environment except through the explicit <c>*FromEnvironment</c> helpers, so
    /// each rule below is asserted natively rather than by staring at a game client.
    ///
    /// That placement is not stylistic. The last feature to put a decision inline in
    /// the game-server assembly could only be guarded by string-matching its source,
    /// because that assembly needs a Windows game install to compile and therefore
    /// has no tests of its own.
    ///
    /// WHAT THE FEATURE IS. It serves <c>1204 WallSegmentState</c> on
    /// <c>WallSegment</c> entities so the SHIPPED client renders the release map's 44
    /// weather walls. It is VISUAL ONLY, and structurally so: the three force paths
    /// (<c>WindPhysicsVisualizer</c>, <c>WallTorquePhysicsVisualizer</c>, and the
    /// gust/torque behaviours they drive) are added only in <c>ShipPreprocessor</c>'s
    /// <c>UnityWorker</c> branch and are therefore NOT ON OUR HULLS AT ALL. A wall
    /// cannot add a newton to a rigidbody that has no component reading it. Serving
    /// 1204 applies exactly zero force, today and until ships Stage B lands.
    ///
    /// WHAT THIS FEATURE MUST NEVER DO: serve <c>1229 GlobalWallDataState</c>. It
    /// carries only wind/gust/torque scalars as a <c>Map&lt;string,float&gt;</c>, it
    /// <c>Debug.LogError</c>s once per missing key, and retail's 50 tuning values are
    /// UNRECOVERABLE - they lived in Bossa's server config and no copy survives in
    /// the client, the asset bundles, the world-data dumps or any snapshot
    /// (findings-storm-walls.md section 5.1, with controls). Anything we put in it is
    /// invented, and half-populating it is worse than not serving it.
    ///
    /// A NOTE FOR WHOEVER EVENTUALLY MAKES WALLS PUSH SHIPS, recorded here because it
    /// is the kind of fact that gets rediscovered expensively. The wall wind is
    /// attenuated by <c>1 - Clamp01(mass/4000)*0.75</c>, and the maintainer remembers
    /// "heavier is better" as a real mechanic - it is, and the wiki says so. But
    /// MEASURED against our actual hull masses (feat/massalign, 2026-08-20) the whole
    /// fleet sits at ramp 0.80-0.95: a cedar skiff is 260 kg (0.951), a one-cell iron
    /// hull 780 kg (0.854), a legacy two-cell 1071 kg (0.799), and only a six-cell
    /// all-metal four-deck ship at 3976 kg gets near the 4000 kg saturation (0.254).
    /// So across the ships players actually fly the ramp is roughly 0.8 and NEARLY
    /// CONSTANT, and "heavier ships push through better" will be almost invisible.
    /// The honest response to that is to say so, not to inflate wall strength to make
    /// the difference felt - that would be tuning around a measurement.
    /// </summary>
    public static class WallPolicy
    {
        /// <summary>
        /// The master switch. DEFAULT OFF, and off is byte-identical on the wire: no
        /// wall entity is registered, so no AddEntity, no asset request and no
        /// component seed is ever sent. Rollback is unsetting one env var.
        /// </summary>
        public const string EnabledEnvVar = "WAREBORN_WALLS";

        /// <summary>
        /// Which wall TYPES to serve, as a comma-separated list of the numeric
        /// <see cref="WallType"/> values ("0,3,5"). Unset means every type in the map.
        ///
        /// THIS IS THE COST LEVER, and it exists because of one measured-by-formula
        /// risk. Serving the 11 storm rifts pins <c>TotalStormWallLength</c> at
        /// ~53 km world-wide and the ambient-bolt spawn rate scales linearly with it,
        /// for every client, everywhere, permanently
        /// (<see cref="WallCatalog.StormWallLengthMetres"/> says exactly why). If a
        /// soak shows that hurts, <c>WAREBORN_WALL_TYPES=0,3,5</c> keeps the 20 wind
        /// rifts, the 12 sand storms and the world edge - 33 of the 44 walls, and the
        /// cheap 33 - while dropping every bolt. It is a knob rather than a code
        /// change so the operator can answer that question without a rebuild.
        /// </summary>
        public const string TypesEnvVar = "WAREBORN_WALL_TYPES";

        /// <summary>
        /// The registration-key prefix. NOT one of the <c>ResourceInterestPolicy</c>
        /// streamed prefixes, and that is deliberate - see <see cref="IsStreamed"/>.
        /// </summary>
        public const string KeyPrefix = "wall-";

        /// <summary>
        /// The bare client prefab name. Present in the runtime prefab census
        /// (<c>Ship/client-entity-prefabs.txt</c>, line "wallsegment") and listed as
        /// resolvable in <c>docs/research/loop/data/prefab-names.tsv:314</c>, so an
        /// AddEntityOp naming it resolves on an unmodified client. The client
        /// lower-cases and appends the worker suffix itself; do not write
        /// "WallSegment_unityclient".
        /// </summary>
        public const string PrefabName = "WallSegment";

        /// <summary>The <c>8065 Blueprint</c> value every other entity in this world gets.</summary>
        public const string DefaultBlueprintName = "Player";

        /// <summary><c>190602 TransformState</c>.</summary>
        public const uint TransformStateComponentId = 190602;

        /// <summary><c>1204 WallSegmentState</c>.</summary>
        public const uint WallSegmentStateComponentId = 1204;

        /// <summary>
        /// What a wall entity is seeded with, IN THIS ORDER, and the order is
        /// load-bearing.
        ///
        /// <c>WallSegmentVisualizer.OnEnable</c> reads <c>transform.position</c> and
        /// hands it to <c>WeatherWalls.Register</c>, which captures P1/P2 ONCE and
        /// never revisits them (acs/WallData.cs:111-120). The position itself is
        /// applied by a DIFFERENT behaviour on the same prefab -
        /// <c>StaticLocalTransformBehaviour.OnEnable</c>, whose <c>[Require]</c> is
        /// <c>TransformStateReader</c> - and the AddEntityOp carries no position at
        /// all (SendOPHelper.SendAddEntityOP takes only a prefab name and context).
        /// So if 1204 resolved before 190602, the visualiser would register a wall at
        /// wherever the prefab was instantiated and it would stay there for the
        /// entity's whole life, silently. Seeding the transform FIRST, in one ordered
        /// batch, is what prevents that; <c>SendAddComponentOp</c> preserves list
        /// order.
        ///
        /// WHY SEED AT ALL, when this repo's convention for scenery is
        /// <c>seedComponents: null</c> and letting the client ask over
        /// SEND_COMPONENT_INTEREST: because that convention hands the ORDER to the
        /// client, and here the order decides whether the wall is in the right place.
        /// <c>WildernessChamber</c> is the existing precedent for a purely visual
        /// static prop that seeds its own transform.
        ///
        /// WHY EXACTLY THESE TWO AND NO MORE. The batch goes out with
        /// <c>failOnComponentInitError: true</c>, so one id without a serializer
        /// branch drops the WHOLE batch and leaves a rendered, inert wall. The
        /// <c>WallSegment_unityclient</c> prefab's full component list is Transform,
        /// <c>WallSegmentVisualizer</c>, <c>TransformNature</c>,
        /// <c>TransformOffsetsRegistry</c>, <c>TransformParentHierarchyBehaviour</c>,
        /// <c>TransformChildHierarchyBehaviour</c>, <c>StaticGlobalTransformBehaviour</c>
        /// and <c>StaticLocalTransformBehaviour</c> - no renderer, no collider, no
        /// other visualiser - and their <c>[Require]</c>s are 1204,
        /// <c>TransformState</c>, <c>TransformHierarchyState</c> and
        /// <c>GlobalTransformState</c>. The last two belong to the PARENT-hierarchy
        /// and GLOBAL-mode behaviours, which a free-standing local-mode object does
        /// not need enabled - the same stack every island, tree and node in this
        /// world already runs on 190602 alone.
        /// </summary>
        public static readonly IReadOnlyList<uint> SeedComponents =
            new[] { TransformStateComponentId, WallSegmentStateComponentId };

        /// <summary>The registration key for one wall.</summary>
        public static string KeyFor(int wallId) => KeyPrefix + wallId;

        /// <summary>Whether a registration key belongs to this feature.</summary>
        public static bool IsWallKey(string? key) =>
            key != null && key.StartsWith(KeyPrefix, StringComparison.Ordinal);

        /// <summary>
        /// The wall id behind a registration key, or null if the key is not ours.
        /// Strict: only the exact <c>wall-&lt;non-negative int&gt;</c> form, so a
        /// future "wall-debug-3" cannot be mistaken for wall 3.
        /// </summary>
        public static int? WallIdFor(string? key)
        {
            if (!IsWallKey(key))
            {
                return null;
            }
            string tail = key!.Substring(KeyPrefix.Length);
            return int.TryParse(tail, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int id) && id >= 0
                ? id
                : null;
        }

        /// <summary>
        /// The <c>8065 Blueprint</c> string for an entity, by registration key.
        ///
        /// Every entity in this world has always been sent the literal "Player" here,
        /// and every non-wall key still is - byte for byte, including a null key.
        /// Only a wall gets "WallSegment". Widening a hard-coded literal is exactly
        /// the kind of edit that quietly changes what every other entity receives,
        /// which is why the decision is one testable function rather than an
        /// expression inside the serializer.
        ///
        /// It tests <see cref="WallIdFor"/> and not the looser
        /// <see cref="IsWallKey"/> ON PURPOSE, and the first version of this function
        /// got it wrong. A key like "wall-debug-3" has the prefix but resolves to no
        /// wall, so under the loose test it would be told it is a WallSegment while
        /// the 1204 branch found nothing to send it - a prefab with an unsatisfiable
        /// [Require], which is the invisible-dead-prop failure this feature has to
        /// avoid. The two answers must come from the same predicate.
        /// </summary>
        public static string BlueprintNameFor(string? entityKey) =>
            WallIdFor(entityKey).HasValue ? PrefabName : DefaultBlueprintName;

        /// <summary>
        /// Whether wall entities go through spatial interest streaming. They do NOT,
        /// and this states it so the answer is a decision with a test rather than an
        /// oversight.
        ///
        /// A key outside <c>ResourceInterestPolicy.IsStreamedResourceKey</c> is
        /// broadcast to every client eagerly. For walls that is not a compromise, it
        /// is the only correct answer: this server's interest radius is 120 m, a wall
        /// influences a client from 800 m, and <c>WeatherWalls.Register</c> runs on
        /// <c>OnEnable</c> - so an interest-gated wall would always check out long
        /// after the player was already inside it. 44 permanent entities is
        /// negligible next to the resource population already streamed.
        /// </summary>
        public static bool IsStreamed => false;

        // ---------------------------------------------------------------
        // ENV PARSING - a typo must never stop a server booting.
        // ---------------------------------------------------------------

        /// <summary>
        /// Whether the raw <see cref="EnabledEnvVar"/> value turns the feature on.
        /// Unset, blank or unrecognised is OFF. Same accepted vocabulary as
        /// <c>WAREBORN_STORMS</c>, deliberately: two adjacent weather features
        /// answering to different spellings of "yes" is a trap.
        /// </summary>
        public static bool Enabled(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }
            string value = raw.Trim();
            return value.Equals("1", StringComparison.Ordinal)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary><see cref="Enabled(string?)"/> against the real environment.</summary>
        public static bool EnabledFromEnvironment() =>
            Enabled(Environment.GetEnvironmentVariable(EnabledEnvVar));

        /// <summary>
        /// The wall types <see cref="TypesEnvVar"/> selects. Null, blank or a value
        /// with no recognisable type in it means EVERY type - an unparseable knob
        /// must leave the feature at its documented default, never silently empty.
        /// Unknown numbers and junk entries are skipped individually.
        /// </summary>
        public static IReadOnlyCollection<WallType> SelectedTypes(string? raw)
        {
            HashSet<WallType> all = new()
            {
                WallType.WindRift, WallType.StormRift, WallType.Typhon,
                WallType.SandStorm, WallType.IceStorm, WallType.WorldEndWall,
            };

            if (string.IsNullOrWhiteSpace(raw))
            {
                return all;
            }

            HashSet<WallType> chosen = new();
            foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(part.Trim(), System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int value)
                    && Enum.IsDefined(typeof(WallType), value))
                {
                    chosen.Add((WallType)value);
                }
            }

            return chosen.Count == 0 ? all : chosen;
        }

        /// <summary><see cref="SelectedTypes(string?)"/> against the real environment.</summary>
        public static IReadOnlyCollection<WallType> SelectedTypesFromEnvironment() =>
            SelectedTypes(Environment.GetEnvironmentVariable(TypesEnvVar));
    }
}
