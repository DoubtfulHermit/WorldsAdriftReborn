using WorldsAdriftRebornGameServer.Multiplayer.Regions;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// THE SKY WHALE: what it is, what it costs, and which half of that this
    /// project recovered rather than invented.
    ///
    /// RECOVERED, from the shipped client (docs/research, branch
    /// research/sky-whales, commit ef404ff, and the tooling committed with it):
    /// <list type="bullet">
    /// <item>the prefab EXISTS and RESOLVES. <c>Ship/client-entity-prefabs.txt</c>
    ///   line 65 carries <c>discowhale</c>, and <c>ClientEntityPrefabs.CanResolve</c>
    ///   lower-cases its argument into exactly that census, so an AddEntity naming
    ///   <see cref="PrefabName"/> draws a whale rather than nothing;</item>
    /// <item>it needs EXACTLY ONE SpatialOS component, 190602 TransformState.
    ///   Every always-on MonoBehaviour on the root has no <c>[Require]</c> at all;
    ///   the entire ship-part visualiser stack it inherits ships DISABLED. 190602
    ///   is not optional decoration: the root <c>Rigidbody</c> is
    ///   <c>m_UseGravity=true, m_IsKinematic=false</c>, and it is
    ///   <c>TransformManageRigidbodyBehaviour</c> - activated only once its 190602
    ///   reader is injected - that makes it kinematic. Without the component the
    ///   whale free-falls out of the world. This is the same mechanism that holds
    ///   the manta rays up;</item>
    /// <item>it is 172.88 m long, 128.00 m across the fins and 33.44 m tall at
    ///   prefab scale 1.0 (skinned-mesh local AABB, centre (0, -11.61, -40.92));</item>
    /// <item>it carries ONE looping animation clip, <c>Whale_Swim</c> (3.3 s), in
    ///   animator controller <c>Sky_Whale</c>, state "Swim Test", with NO
    ///   parameters and no turn or idle state to blend to. It swims forward,
    ///   always, and the motion below is designed for an animal that cannot be
    ///   shown stopping or banking;</item>
    /// <item>it has NO <c>AgeVisualizer</c>, so there is no calf and no scale
    ///   channel; 190602 carries position and rotation and nothing else;</item>
    /// <item>it does not flock, has no species enum entry, no movement controller
    ///   and no spawner. It is identified purely by prefab name, exactly like a
    ///   ship part.</item>
    /// </list>
    ///
    /// NOT RECOVERED - and therefore WAREBORN TUNING, labelled individually below:
    /// the transit PATH, the circuit period, the speed, the altitude, the pose
    /// cadence, the interest radii and the call cadence. Retail's whale behaviour
    /// was CUT: five <c>Play_SkyWhale_*</c> Wwise events are declared in the
    /// generated header and present in ZERO of the twenty shipped banks, and no
    /// code path could fire them. Nothing in this file should be read as Bossa's
    /// design for how a sky whale moves, because Bossa shipped none.
    ///
    /// ONE PER REGION, and the region is the MapFile cell - the same grouping
    /// <see cref="RegionRegistry.CreateReleaseWorld"/> turns into
    /// <c>release-&lt;cell&gt;-region</c>. That is deliberate scarcity, not a budget
    /// accident: the prefab has 19,821 vertices across 35 renderers sharing one
    /// material and NO LODs at all, so it is paid for in full at any distance. One
    /// is an event; four in a cell would be wallpaper and would cost four times as
    /// much to draw.
    /// </summary>
    public static class SkyWhalePolicy
    {
        /// <summary>
        /// The operator switch. Its own flag, following
        /// <c>WAREBORN_ISLAND_FAUNA_ECOLOGY</c>'s precedent: a new relayed sender
        /// arrives OFF and is turned on deliberately, and with it off this server
        /// is byte-identical on the wire to one built without this feature.
        /// </summary>
        public const string EnabledEnvVar = "WAREBORN_SKY_WHALE";

        /// <summary>How near a whale a peer must be to be shown it, in metres.</summary>
        public const string LoadRadiusEnvVar = "WAREBORN_SKY_WHALE_RADIUS_M";

        /// <summary>How near a CALL a peer must be to hear it, in metres.</summary>
        public const string CallRadiusEnvVar = "WAREBORN_SKY_WHALE_CALL_RADIUS_M";

        /// <summary>
        /// The client prefab name. RECOVERED and resolvable; see the type remarks.
        ///
        /// The name is a joke wrapper Bossa left on a finished animal - the
        /// <c>DiscoWhale</c> MonoBehaviour hue-rotates the SHARED material and
        /// drives a 200 m point light, and the prefab carries a fireworks
        /// particle system and a stray tree-trunk mesh. None of that is fixable
        /// from the server, because the server sends a NAME: undoing it is the
        /// client mod's job (WorldsAdriftReborn/Patching/InGameChanges/
        /// SkyWhaleUndisco_Patch.cs). Renaming the prefab here would simply make
        /// the entity fail to resolve and draw nothing.
        /// </summary>
        public const string PrefabName = "DiscoWhale";

        /// <summary>
        /// The invisible caller's prefab. RECOVERED: <c>BigCall_unityclient</c> is
        /// a three-component entity with NO renderer, mesh or collider whose only
        /// visualiser, <c>BigCallVisualiser</c>, <c>[Require]</c>s 4347
        /// <c>BigCallState</c> and posts the Wwise event <c>Big_DistantCall</c>
        /// (FNV-1 3395764039, present in <c>AmbienceWeatherWildlife_Shared.bnk</c>)
        /// on a rising <c>playAudio</c>, then again 15 s later.
        /// </summary>
        public const string CallPrefabName = "BigCall";

        /// <summary>
        /// The first entity id a whale or its caller may use.
        ///
        /// A DISJOINT BAND, one hundred million ids above
        /// <see cref="IslandFaunaPolicy.FirstFaunaEntityId"/> for exactly the
        /// reason that constant sits a hundred million above
        /// <c>TreeFall.FirstLogEntityId</c>: two transform streams naming the same
        /// entity corrupt the client's entity table in a way that reads as a
        /// protocol bug rather than as an allocation bug. Fauna's own world-wide
        /// budget is 4,000, so the two bands are separated by four orders of
        /// magnitude more headroom than any world can consume.
        ///
        /// Like a creature and like a felled log, a whale is deliberately NOT a
        /// world registration: it must not enter the connect-time spawn plan, the
        /// loading barrier's count, or the domain host's expected-owned list.
        /// </summary>
        public const long FirstWhaleEntityId = 2_200_000_000L;

        /// <summary>
        /// How many entity ids one whale consumes: the animal, then its caller.
        /// Contiguous so a region's pair is a readable block in a log and so the
        /// id arithmetic is a pure function of the region's index.
        /// </summary>
        public const int EntityIdsPerWhale = 2;

        /// <summary>
        /// How fast a whale travels along its circuit, in metres per second.
        ///
        /// WAREBORN TUNING. Eighteen, and the number is argued from the two things
        /// that are not tuning - the animal's RECOVERED length and the recovered
        /// manta wander speed:
        /// <list type="bullet">
        /// <item>a manta glides at 8 m/s (RECOVERED, <c>WanderingConductVisualiser</c>'s
        ///   compiled-in <c>targetWanderVelocityMagnitude</c>) and is about eleven
        ///   metres long, so it covers about 0.7 body-lengths a second;</item>
        /// <item>this animal is 172.88 m long (RECOVERED), so 18 m/s is about 0.10
        ///   body-lengths a second. It is more than twice the manta's ground speed
        ///   and reads as SEVEN TIMES SLOWER, which is what a creature this size
        ///   has to look like.</item>
        /// </list>
        /// It also sets the two durations the feature is actually judged on, and
        /// those are MEASURED against the preserved catalogue rather than estimated
        /// (<c>SkyWhalePlanTests</c> prints and pins both). The four tier-1 cells'
        /// circuits are 16.3 to 25.7 km, so a lap is 15.1 to 23.8 minutes and any
        /// one island is visited three or four times an hour. A pass through the
        /// 600 m island bubble a standing player has lasts 57 to 86 seconds across
        /// all twelve islands of B3; a pass through the whale's own
        /// <see cref="DefaultLoadRadiusMetres"/> checkout sphere is about twice
        /// that. "A minute or two, three times an hour" is the shape being aimed
        /// at, and this constant is the only knob that sets it.
        /// </summary>
        public const double MetresPerSecond = 18.0;

        /// <summary>
        /// How far above an island's HIGHEST terrain the whale's waypoint sits, in
        /// metres. WAREBORN TUNING.
        ///
        /// One hundred and twenty, and the floor under it is RECOVERED geometry
        /// rather than taste: the whale's skinned mesh is centred 11.61 m BELOW the
        /// prefab origin and is 33.44 m tall, so its belly hangs about 28 m under
        /// the transform this server drives. An altitude under about 30 m would put
        /// the animal through the rock. 120 m clears that by four times, keeps the
        /// whale inside a player's upward field of view from the ground, and stays
        /// far below the terrain interest ceiling so nothing about the flyby
        /// depends on how the island streams.
        /// </summary>
        public const double AltitudeAboveIslandMetres = 120.0;

        /// <summary>
        /// How often one whale's transform is pushed while it is checked out.
        ///
        /// 500 ms - 2 Hz - and HALF the fauna cadence
        /// (<see cref="IslandFaunaRegistry.DefaultPoseInterval"/>), for a reason
        /// that is the mirror image of the fauna one. A creature is cheap and
        /// numerous; a whale is singular and enormous. What a whale gets wrong at a
        /// low cadence is not smoothness of position but ANGULAR error at close
        /// range, because it is 172.88 m long: 18 m/s x 0.5 s is nine metres of
        /// travel between updates, about a twentieth of the animal's own body
        /// length, which the client's
        /// <c>FixedUpdateLerpGlobalTransformBehaviour</c> interpolates invisibly.
        /// A whole second - 18 m - starts to show on the flyby.
        ///
        /// THE RESULTING BUDGET, stated so it can be checked rather than trusted.
        /// A peer holds at most <see cref="DefaultPerPeerWhales"/> (1) whale, so the
        /// ceiling this feature adds to ONE peer's wire is 1 / 0.5 = TWO transform
        /// updates a second. The fauna ceiling is 24 x 4 = 96 and is untouched:
        /// a whale is not a creature, is not in the fauna registry, and consumes no
        /// fauna slot. Ninety-eight is the new number, and it is still under a
        /// fifth of one 20 Hz avatar relay.
        /// </summary>
        public static readonly TimeSpan DefaultPoseInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// How near a whale a peer must be to be shown it, in metres. WAREBORN TUNING.
        ///
        /// Twelve hundred - twice
        /// <see cref="IslandFaunaInterestPolicy.DefaultLoadRadiusMetres"/> - because
        /// the animal is roughly sixteen times a manta's length and therefore still
        /// subtends a real angle at a distance where a manta is one pixel. It buys
        /// about two minutes of visible whale per pass at
        /// <see cref="MetresPerSecond"/>, which is the flyby.
        ///
        /// INTEREST IS KEYED ON THE ANIMAL, NOT ON AN ISLAND, and that is the
        /// OPPOSITE of what <see cref="IslandFaunaInterestPolicy"/> concluded for
        /// the mantas - deliberately, because the two animals fail differently. A
        /// manta ORBITS its island, so a creature-keyed radius made every lap a
        /// remove/re-add cycle: the crossing was noise. A whale TRANSITS: it enters
        /// a peer's sphere once and leaves once per circuit, roughly every twenty
        /// minutes, and that single crossing IS the feature. Keying the whale on an
        /// island instead would be worse in both directions - it would hold the
        /// animal while it was ten kilometres away and drop it while it was
        /// overhead but between islands.
        /// </summary>
        public const double DefaultLoadRadiusMetres = 1200.0;

        /// <summary>
        /// How far past <see cref="DefaultLoadRadiusMetres"/> a whale is retained.
        /// WAREBORN TUNING; 200 m, matching
        /// <see cref="IslandFaunaInterestPolicy.UnloadMarginMetres"/>, because the
        /// thing it smooths is the same thing - a player hovering a ship near the
        /// boundary - and a whale approaching at 18 m/s crosses that band in eleven
        /// seconds, so it can never dither in it.
        /// </summary>
        public const double UnloadMarginMetres = 200.0;

        /// <summary>
        /// How many whales one peer may hold at once. ONE.
        ///
        /// This is the safety property, and it is exact rather than probabilistic.
        /// A region is a MapFile cell several kilometres across and its whale never
        /// leaves it, so a peer can only be near two whales at all standing on a
        /// cell boundary; capping at one means the per-peer cost of this entire
        /// feature is ONE entity and TWO transform updates a second, WHATEVER the
        /// world's region count. World size and per-peer wire cost are decoupled by
        /// construction, exactly as <see cref="IslandFaunaInterestPolicy"/> decoupled
        /// them for the creatures.
        /// </summary>
        public const int DefaultPerPeerWhales = 1;

        /// <summary>
        /// How near a CALL a peer must be to be sent it, in metres. WAREBORN TUNING.
        ///
        /// Four thousand, and this number is the feature. "Hear it before you see
        /// it" is not a property of the sound file; it is the ratio between this
        /// radius and <see cref="DefaultLoadRadiusMetres"/>. A caller checked out at
        /// 4 km from a whale that only becomes visible at 1.2 km means the call
        /// always arrives from an animal that is not yet there, from roughly the
        /// direction it is coming from, and a player who turns and waits is
        /// rewarded about two and a half minutes later. Set this equal to the whale
        /// radius and the feature disappears even though every packet still flows.
        /// </summary>
        public const double DefaultCallRadiusMetres = 4000.0;

        /// <summary>
        /// How often a whale calls, in seconds. WAREBORN TUNING, with two RECOVERED
        /// anchors either side of it.
        ///
        /// The cut retail whale called on a client-local <c>Random.Range(25f, 45f)</c>
        /// from the <c>DiscoWhale</c> MonoBehaviour's own coroutine, and
        /// <c>BigCallVisualiser</c> replays every call once more after a RECOVERED
        /// 15 s delay. One hundred and twenty seconds is far slower than the first
        /// and is chosen against the circuit rather than against the animal: at
        /// <see cref="MetresPerSecond"/> a call moves about 2.2 km along the path,
        /// so a listener inside <see cref="DefaultCallRadiusMetres"/> gets a
        /// sequence of calls that AUDIBLY APPROACHES rather than a single cue. A
        /// 30 s cadence would put four calls in the same 2 km and read as a loop.
        /// </summary>
        public const double CallIntervalSeconds = 120.0;

        /// <summary>
        /// The fewest islands a region needs before it gets a whale.
        ///
        /// THREE, and this is a structural requirement rather than a taste one: the
        /// circuit is a CLOSED uniform Catmull-Rom spline through one waypoint per
        /// island (<see cref="SkyWhaleCircuit"/>), and a closed spline through fewer
        /// than three distinct control points is not a loop - it is a degenerate
        /// segment with no tangent, so the animal would have no heading. A region
        /// under this floor is reported at boot and simply carries no whale, which
        /// is the state the world was in before this feature existed. Every tier-1
        /// MapFile cell carries eleven or twelve islands, so no released region is
        /// near it.
        /// </summary>
        public const int MinimumIslandsPerRegion = 3;

        /// <summary>
        /// Whether the sky whale is switched on, from the operator's
        /// <see cref="EnabledEnvVar"/> string. Exactly
        /// <see cref="IslandFaunaPolicy.EnabledFrom"/>'s tokens, so an operator who
        /// has learned one flag has learned all of them, and a typo fails SAFE.
        /// </summary>
        public static bool EnabledFrom(string? value) => IslandFaunaPolicy.EnabledFrom(value);

        /// <summary>
        /// A radius from the operator, or the supplied default. Shaped exactly like
        /// <see cref="IslandFaunaInterestPolicy.LoadRadiusFrom"/>, including the
        /// non-positive kill switch and the fall-back-rather-than-throw rule: an
        /// environment typo must never stop a server booting.
        /// </summary>
        public static double RadiusFrom(string? value, double fallback)
        {
            if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double radius)
                || double.IsNaN(radius))
            {
                return fallback;
            }
            return radius < 0.0 ? 0.0 : Math.Min(radius, InterestPolicy.MaxRadiusMetres);
        }

        /// <summary>The unload radius for a load radius. Zero stays zero, so a kill switch stays killed.</summary>
        public static double UnloadRadiusFor(double loadRadius) =>
            loadRadius <= 0.0 ? 0.0
                : Math.Min(loadRadius + UnloadMarginMetres, InterestPolicy.MaxRadiusMetres);

        /// <summary>
        /// WHERE ON ITS CIRCUIT a region's whale starts, as a fraction of a lap.
        ///
        /// FNV-1a over the region id, and the choice of hash is load-bearing for the
        /// same reason it is in <see cref="IslandFaunaPolicy.JellySpeciesFor"/>:
        /// .NET string hashing is RANDOMISED PER PROCESS, so a restarted server
        /// would re-phase every whale and a reconnecting player would find the
        /// animal somewhere else entirely. FNV-1a over the id's characters is a pure
        /// function of the name, forever - which is what makes the whole pose a
        /// replayable function of the clock.
        ///
        /// Spreading the regions' phases is not cosmetic either: it stops every
        /// whale in the world from being over an island at the same instant, so the
        /// world-wide call schedule is spread rather than synchronised.
        /// </summary>
        public static double PhaseFractionFor(RegionId region)
        {
            const uint OffsetBasis = 2166136261;
            const uint Prime = 16777619;
            uint hash = OffsetBasis;
            string id = region.ToString();
            for (int i = 0; i < id.Length; i++)
            {
                hash = (hash ^ id[i]) * Prime;
            }
            // Divided by 2^32 rather than by a modulus, so the phase is spread over
            // the whole lap instead of onto a lattice of a few dozen positions.
            return hash / 4294967296.0;
        }

        /// <summary>
        /// The region id a MapFile cell becomes. The ONE place this string is
        /// formed for the whale, kept character-identical to
        /// <see cref="RegionRegistry.CreateReleaseWorld"/>'s so a whale's region and
        /// the world directory's region are the same name rather than two names that
        /// happen to agree today.
        /// </summary>
        public static RegionId RegionIdForCell(string cellId)
        {
            if (string.IsNullOrWhiteSpace(cellId))
            {
                throw new ArgumentException("a cell id must not be empty", nameof(cellId));
            }
            return new RegionId("release-" + cellId.ToLowerInvariant() + "-region");
        }

        /// <summary>
        /// The most whale transform updates one peer can receive per second. Stated
        /// at boot so the number the multiplayer-safety rule asks about is reported
        /// rather than claimed. Shaped like
        /// <see cref="IslandFaunaInterestPolicy.WorstCaseUpdatesPerSecond"/>.
        /// </summary>
        public static double WorstCaseUpdatesPerSecond(int perPeerWhales, TimeSpan poseInterval)
        {
            if (poseInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(poseInterval),
                    "a non-positive pose interval has no rate");
            }
            return perPeerWhales <= 0 ? 0.0 : perPeerWhales / poseInterval.TotalSeconds;
        }
    }
}
