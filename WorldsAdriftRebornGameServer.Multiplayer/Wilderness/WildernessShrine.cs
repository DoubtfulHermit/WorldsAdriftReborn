using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer.Wilderness
{
    /// <summary>
    /// THE OBJECT. One world entity on Haven that a player walks up to and
    /// activates to leave the tutorial island for the Wilderness.
    ///
    /// WHAT RETAIL AUTHORED. The shipped Act 1 quest chain ends at quest 105,
    /// "Access the Revival chamber located at the center of the island", and the
    /// shipped instruction string is literally:
    ///
    ///   "Interact with the platform inside the Revival Chamber to activate the
    ///    Revival Chamber Interface, and teleport - together with other players on
    ///    the platform - to The Wilderness."
    ///
    /// (docs/research/loop/findings-first-hour.md.) So the authored mechanic is a
    /// PLATFORM you interact with that teleports a GROUP to Tier 1 - which is, to
    /// the word, the mechanic being built here.
    ///
    /// WHY NOT <c>HavenAncientRespawner</c>, WHICH IS THE ACTUAL REVIVAL CHAMBER.
    /// It was tried, it shipped, and it could not work - PROVED by measuring the
    /// shipped prefab's own collision meshes (resources.assets, UnityPy):
    ///
    ///   * Its only <c>InteractiveObjectVisualizer</c> is on the deep child
    ///     <c>SpawnPad</c>, the plate at the bottom of the chamber.
    ///   * <c>respawner_exterior_LOD0</c>, its collision shell, is CLOSED on
    ///     360/360 bearings from prefab-local y = -1.0 all the way up to y = 9.3.
    ///     The only aperture is a doorway on the +x bearing whose sill is at
    ///     y = 9.35 (+-0.05), reached by <c>Ramp01</c>/<c>Ramp02</c> at y = 10.03
    ///     and 10.57 - which is also where the <c>Barrier_Wall</c> (11.17) and both
    ///     <c>Access-Ancient-Respawner-Trigger</c> boxes (9.09 and 11.06) sit.
    ///   * So the plate stands at the bottom of a sealed 9.35 m well. Place the
    ///     prefab with its origin at ground and the plate is walled in by a 9.35 m
    ///     wall at radius 8.69 m. Bury the origin ~10 m so the authored doorway
    ///     meets the terrain and the plate is then 10 m UNDER the terrain mesh,
    ///     which fills the well and occludes it. There is no height at which a
    ///     player can reach it.
    ///   * On 2026-08-18 a player logged in, ended up inside that shell, saw the
    ///     interactive highlight (the range test passes near the buried plate) and
    ///     had to be rescued with the admin teleport. That is the well, observed.
    ///   * It is also 40 m x 36 m in plan, and its 44 m footprint does not fit
    ///     anywhere on Haven's spawn shelf: the nearest measured spot that could
    ///     hold it is 141 m away and 25 m up.
    ///
    /// WHAT IS USED INSTEAD. <see cref="AssetName"/> - <c>Respawner01</c>, retail's
    /// REVIVER platform, the same authored vocabulary ("interact with the platform")
    /// and the object <c>InteractiveObjectVisualizer.GetTutorialStep</c> maps to
    /// <c>TutorialStep.MOUSE_OVER_REVIVER</c> for verb <c>Activate</c>. It is line
    /// 223 of the entity-prefab census
    /// (<see cref="Ship.ClientEntityPrefabs"/>), so it is EVIDENCED-loadable, and
    /// its geometry is everything the chamber's was not:
    ///
    ///   * <c>InteractiveObjectVisualizer</c> on the prefab ROOT, offset (0, 0, 0) -
    ///     the same shape as the metal nugget's and the helm's, both of which are
    ///     live-proven to prompt on this server.
    ///   * Verb <c>Activate</c> (serialized value 1), RECOVERED.
    ///   * Root GameObject on layer 15 "Interactive", inside
    ///     <c>Layers.Interactables</c>, so the look raycast can hit it.
    ///   * Collision extent x/z +-0.60 m, y 0.00..0.20 m: a flat plate you walk onto
    ///     with nothing to be enclosed by and nothing to clip into.
    ///
    /// It is a ship part, like the static helm this server already stands up as its
    /// own world entity with the same 190602 + 1210 seed. That is the precedent it
    /// is being placed on.
    ///
    /// WHAT WE ARE NOT DOING. The retail chamber also drove 8052
    /// <c>HavenTeleporterState</c>, a client-side barrier dome, and an 8056
    /// <c>LeaveHavenRequest</c> that has zero client references and was never
    /// implemented. None of that is served here. The interaction runs over
    /// 1210/1211, the same proven pair that already makes a placed shipyard's
    /// console and a metal nugget interactive.
    /// </summary>
    public static class WildernessShrine
    {
        /// <summary>
        /// The bare prefab name, as <c>WorldEntity.AssetName</c> wants it: no
        /// <c>_unityclient</c> suffix, the client appends its own worker suffix.
        /// Case taken from the prefab itself in resources.assets, which carries the
        /// GameObject <c>Respawner01_unityclient</c>.
        /// </summary>
        public const string AssetName = "Respawner01";

        /// <summary>
        /// Its stable registration key. Singular and not a prefix: there is exactly
        /// one shrine, on Haven, because Haven is the only island players are
        /// deposited on with nowhere to fly to.
        /// </summary>
        public const string WorldEntityKey = "wilderness-shrine";

        /// <summary>The name this teleport carries in the log, at both ends.</summary>
        public const string TeleportReason = "graduation";

        /// <summary>
        /// Where it stands, in Haven island-local metres: on the floor INSIDE the
        /// Revival Chamber, at its exact centre.
        ///
        /// THIS IS THE THIRD PLACEMENT AND EACH ONE FAILED FOR A DIFFERENT REASON,
        /// which is why it is spelled out.
        ///
        ///   * (176.00, 4.90, 16.00) put a 40 m prefab 13.7 m from the ruined metal
        ///     camp - i.e. through it. A player logged in inside the result and had
        ///     to be rescued with the admin teleport.
        ///   * (168.00, 4.47, 24.00) cleared the camp by 24.5 m, but by then the
        ///     object was a bare 1.2 m plate 45 m from spawn and a live player could
        ///     not find it: "i cant find the teleporter now".
        ///   * (160.00, 4.18, 32.00) put it inside the chamber, but 25.3 m from the
        ///     spot the user had twice pointed at, with the chamber's one doorway
        ///     facing 132 deg away from them.
        ///   * This one is INSIDE <see cref="WildernessChamber"/>, at chamber-local
        ///     (0, 0) - which is where retail's own spawn plate sits, 11 m further
        ///     down under the terrain. The 20 m tower is the landmark; the room is
        ///     the "clean slot"; the plate you walk onto is in the middle of it.
        ///
        /// X and Z are the chamber's, exactly - the invariant is "the shrine is at
        /// the centre of the chamber", not "the shrine is near the chamber", and
        /// WildernessShrineTests pins it as an equality so the two can never drift.
        /// Y is the MEASURED Haven LOD0 surface vertex there (4.18), because the
        /// chamber's floor is Haven's own terrain: the building is buried so that
        /// its doorway sill lands on the ground, and everything below - the sealed
        /// drum, the 9.7 m internal drop, the unreachable plate - is under the
        /// terrain mesh where nobody can reach or fall into it.
        ///
        /// Measured clearances at this point, all island-local:
        ///
        ///   * 10.0 m from the nearest chamber geometry at the player's standing
        ///     band (2.2 m capsule tested against the prefab's collision meshes on a
        ///     1 m grid) - the middle of a clear room, not a corner
        ///   * the entry corridor is 12.7 m away in chamber-local +x and its terrain
        ///     spans 0.11 m; the ramps and both quest trigger boxes are further out
        ///     still and 11 m below the floor, buried
        ///   * zero authored rocks within 12 m; the nearest authored structure is
        ///     7.2 m clear of the chamber's whole 40 m x 36 m footprint
        ///   * 55.6 m from the spawn point and 0.52 m below its ground vertex - a
        ///     flat walk, reachable by a flood fill that never climbs more than 2 m
        ///     per 8 m cell
        /// </summary>
        public static readonly (double X, double Y, double Z) HavenLocalPlacement = (156.00, 4.16, 28.00);

        /// <summary>Its global position, given the Haven definition it stands on.</summary>
        public static FixedPointPosition PositionOn(IslandDefinition haven)
        {
            if (haven == null) throw new ArgumentNullException(nameof(haven));
            return haven.LocalToGlobal(
                HavenLocalPlacement.X, HavenLocalPlacement.Y, HavenLocalPlacement.Z);
        }

        // ------------------------------------------------------------------
        // THE PREFAB'S OWN GEOMETRY - measured, not chosen.
        //
        // RECOVERED from resources.assets (the shipped client's own copy of
        // EntityPrefabs/Respawner01_unityclient, read with UnityPy): the
        // InteractiveObjectVisualizer is on the prefab ROOT at offset (0, 0, 0),
        // the root GameObject is on layer 15 "Interactive", and the plate's
        // collision extent is x/z -0.60..+0.60 with y 0.00..0.20.
        //
        // The offset being ZERO is the whole point of choosing this prefab. The
        // client measures interaction range to the VISUALIZER's transform
        // (Multiplayer.InteractReach), so a visualizer on the root means the range
        // is measured to the entity origin - the geometry the metal nugget and the
        // helm already prove works with a small radius on this server.
        // ------------------------------------------------------------------

        /// <summary>
        /// How far the standable top of the plate is ABOVE the
        /// <c>InteractiveObjectVisualizer</c>'s own transform, metres. Measured:
        /// 0.00 m (the visualizer's local offset) + 0.20 m (the top of the plate's
        /// collider).
        /// </summary>
        public const float PadTopAboveVisualiserMetres = 0.20f;

        /// <summary>
        /// Half-width of the plate's collider, metres - how far out from the centre
        /// a player can stand and still be ON it. Measured from the prefab's
        /// collision extent (x and z both -0.60 .. +0.60).
        /// </summary>
        public const float PadHalfWidthMetres = 0.60f;

        /// <summary>
        /// 1210 InteractionEntry.radius, metres.
        ///
        /// RECOVERED, not tuned: 5 m is the client's OWN default for an Activate
        /// interaction. <c>InteractiveObjectVisualizer._interaction</c> is field-
        /// initialised to <c>new InteractionEntry(InteractVerb.Activate, 5f,
        /// lockOnUse: false, "", "", "", exclusiveUse: false, 1f)</c>, which is what
        /// the visualizer falls back on when no server entry matches. It is also the
        /// radius this server already serves for the mounted sail/lamp/horn Activate
        /// (<c>Ship.PartInteractionPolicy.ActivateRadius</c>), so "how close do I
        /// have to be to Activate something" has ONE answer here.
        ///
        /// WHY THE NUMBER MATTERS AT ALL. The client offers a prompt only while
        /// <c>Distance(visualizer.transform.position, player.transform.position)
        /// + 0.5f &lt; radius</c> (<see cref="InteractReach"/>, RECOVERED from
        /// <c>PlayerLookingAt.InRange</c>). The shrine's first build seeded 3.0 on a
        /// prefab whose visualizer was 3.204 m below the plate, which described a
        /// reachable sphere entirely underground - no prompt from any position in
        /// the world, and nothing in any log to say why. Here the visualizer is ON
        /// the entity origin, so 5 m leaves
        /// <c>sqrt(4.5^2 - 0.20^2) = 4.50 m</c> of horizontal reach: the whole plate
        /// plus a 3.9 m walk-up ring, which is what a player has to find this thing
        /// with. WildernessShrineTests pins that against the measured geometry.
        /// </summary>
        public const float InteractRadius = 5.0f;

        /// <summary>
        /// 1210 InteractionEntry.timeToUse, seconds. Longer than the shipyard's
        /// 0.5 s on purpose: this is the one interaction on Haven that cannot be
        /// undone in the next second, and the hold is the only "are you sure" the
        /// retail interaction vocabulary has.
        /// </summary>
        public const float InteractTimeToUse = 1.5f;

        /// <summary>
        /// Every verb the shrine answers to, and every verb its 1210 seed carries
        /// an entry for.
        ///
        /// RECOVERED, and no longer a guess: the baked verb is <c>Activate</c> (1).
        /// <c>Respawner01_unityclient</c>'s root carries the only
        /// <c>InteractiveObjectVisualizer</c> on the whole hierarchy and its
        /// serialized <c>Verb</c> field reads 1 - as does the Revival Chamber's own
        /// <c>SpawnPad</c>, so the two agree. Read straight out of the shipped
        /// client's resources.assets; the same 48-byte MonoBehaviour layout decodes
        /// all 191 <c>InteractiveObjectVisualizer</c> instances in that file and
        /// agrees with every independently known one (Helm01 = Man, Sail01 =
        /// Activate, Stove01 = Craft, every container = Inventory), so the reading
        /// is cross-checked rather than asserted.
        ///
        /// THE HEDGE IS KEPT ANYWAY, and deliberately.
        /// <c>InteractiveObjectVisualizer.OnEnable</c> does
        /// <c>Interactions.FirstOrDefault(i =&gt; i.verb == Verb)</c> ONCE, and
        /// <c>GetVerb(collider)</c> can be overridden per-collider by an
        /// <c>InteractiveObjectVerbOverrider</c> anywhere in the collider's parent
        /// chain. A wrong single entry is not a degraded prompt, it is no prompt at
        /// all with nothing in any log to say why, and the extra entries cost two
        /// list elements and nothing else: the visualizer takes the one MATCHING
        /// its own verb and ignores the rest. Drop them once a live client has been
        /// seen to send back a 1211 naming Activate.
        ///
        /// The three: <c>Activate</c>, recovered above and what the quest text says;
        /// <c>Default</c> because it is the enum's zero and an unset field lands
        /// there; <c>Man</c> because the retail flow has the player STAND ON a
        /// platform, which is the verb the helm uses for taking a position.
        /// <c>PickUp</c> is deliberately absent - a monument is not portable, and a
        /// PickUp prompt on it would be a lie.
        ///
        /// Values from the verified enum Bossa.Travellers.Interact.InteractVerb
        /// { Default = 0, Activate = 1, PickUp = 2, Man = 3, Inventory = 4,
        ///   Craft = 5, ... } as recorded in Placement.ShipyardInteraction.
        /// </summary>
        public static readonly IReadOnlyList<int> Verbs = new[]
        {
            VerbActivate,
            VerbDefault,
            VerbMan,
        };

        public const int VerbDefault = 0;
        public const int VerbActivate = 1;
        public const int VerbMan = 3;

        /// <summary>
        /// Whether an interact event on the shrine counts as "use the shrine".
        ///
        /// Deliberately NOT "any verb": the dispatcher routes <c>Craft</c>,
        /// <c>PickUp</c> and <c>ReclaimShip</c> elsewhere, and a shrine that
        /// swallowed those would break station pickup for anyone who happened to be
        /// standing near it.
        /// </summary>
        public static bool Accepts(int verb)
        {
            for (int i = 0; i < Verbs.Count; i++)
                if (Verbs[i] == verb) return true;
            return false;
        }

        /// <summary>
        /// The components seeded on the shrine entity, in send order.
        ///
        /// 190602 places it; 1210 makes it interactive. Nothing else, and that is a
        /// rule rather than minimalism: the seed push is ALL-OR-NOTHING, so naming
        /// an id with no ComponentsSerializer branch drops the whole batch and
        /// yields an entity that renders at the world origin and does nothing. 6905
        /// <c>AncientRespawnerState</c> exists in the schema (docs/component-ids.md)
        /// and is exactly the kind of id that would be tempting to add; it has no
        /// branch, so adding it would break the shrine rather than improve it.
        /// </summary>
        public static readonly IReadOnlyList<uint> SeedComponents = new uint[]
        {
            190602,
            1210,
        };

        /// <summary>
        /// Whether the shrine is switched on for this boot. Default ON: it is the
        /// only exit from Haven, and a server that registers Tier-1 districts and
        /// then hides the door is worse than one that never opened.
        ///
        /// The kill switch exists because it spawns a world entity for every
        /// connecting player, and anything on the connect path deserves a way to be
        /// turned off from the unit file without a rebuild. Refusing to graduate
        /// when Tier 1 is closed is handled separately and is NOT this flag: the
        /// shrine still stands and still explains itself, because a door that says
        /// "not tonight" beats a door that is not there.
        /// </summary>
        public const string EnabledEnvVar = "WAREBORN_WILDERNESS_SHRINE";

        public static bool EnabledFrom(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return true;
            string value = raw.Trim();
            return !(value.Equals("0", StringComparison.Ordinal)
                || value.Equals("off", StringComparison.OrdinalIgnoreCase)
                || value.Equals("false", StringComparison.OrdinalIgnoreCase)
                || value.Equals("no", StringComparison.OrdinalIgnoreCase));
        }
    }
}
