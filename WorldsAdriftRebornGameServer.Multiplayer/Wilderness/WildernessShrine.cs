using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer.Wilderness
{
    /// <summary>
    /// THE OBJECT. One world entity on Haven that a player walks up to and
    /// activates to leave the tutorial island for the Wilderness.
    ///
    /// WHY THIS PREFAB AND NOT AN INVENTED ONE. Retail's graduation from Haven was
    /// not a ship and not a hidden trigger - it was a device, and we know which
    /// one. The shipped Act 1 quest chain ends at quest 105, "Access the Revival
    /// chamber located at the center of the island", and the shipped instruction
    /// string is literally:
    ///
    ///   "Interact with the platform inside the Revival Chamber to activate the
    ///    Revival Chamber Interface, and teleport - together with other players on
    ///    the platform - to The Wilderness."
    ///
    /// (docs/research/loop/findings-first-hour.md.) So the authored mechanic is a
    /// Revival Chamber you interact with that teleports a GROUP to Tier 1 - which
    /// is, to the word, the mechanic being built here. The client class behind it
    /// is <c>AncientRespawner</c>, whose whole body is
    /// <c>DisplayName = "Revival Chamber"</c>, and the Haven-specific prefab is
    /// <see cref="AssetName"/>: it is line 80 of the entity-prefab census
    /// (docs/research/world-data/prefab-keys.txt,
    /// <c>entityprefabs/havenancientrespawner_unityclient</c>) and
    /// docs/research/loop/data/prefab-names.tsv:81 records both a client and a
    /// worker prefab for it. The census is the same file
    /// <see cref="Ship.ClientEntityPrefabs"/> loads at runtime to refuse prefabs a
    /// client could not resolve, so this name is EVIDENCED-loadable rather than
    /// hoped-for. Nothing in the shipped island-prop library would have been better:
    /// the two <c>shrine1</c>/<c>shrine2</c> meshes are island-bundle decoration
    /// with no entity prefab and cannot be given to a <c>WorldEntity</c> at all.
    ///
    /// WHAT WE ARE NOT DOING. The retail chamber also drove 8052
    /// <c>HavenTeleporterState</c>, a client-side barrier dome, and an 8056
    /// <c>LeaveHavenRequest</c> that has zero client references and was never
    /// implemented. None of that is served here. The prefab is used as the physical
    /// object and the interaction runs over 1210/1211, the same proven pair that
    /// already makes a placed shipyard's console and a metal nugget interactive.
    ///
    /// WHAT IS INFERRED. Which <c>InteractVerb</c> this prefab bakes is NOT
    /// evidenced - we have the class name and the quest text, not the prefab's
    /// serialized <c>InteractiveObjectVisualizer.Verb</c>. That matters because the
    /// visualizer resolves its entry ONCE, in <c>OnEnable</c>, with
    /// <c>Interactions.FirstOrDefault(i =&gt; i.verb == Verb)</c>: serve the wrong
    /// verb and the radius falls to zero and the prompt never appears, silently and
    /// permanently. The hedge is <see cref="Verbs"/> - the seed carries an entry for
    /// every plausible verb, which costs three list elements and makes the lookup
    /// succeed whichever one the prefab baked. See <see cref="Verbs"/>.
    /// </summary>
    public static class WildernessShrine
    {
        /// <summary>
        /// The bare prefab name, as <c>WorldEntity.AssetName</c> wants it: no
        /// <c>_unityclient</c> suffix, the client appends its own worker suffix.
        /// Case taken from docs/research/world-data/haven/haven-prefabs2.json, which
        /// carries the string <c>HavenAncientRespawner_unityclient</c>.
        /// </summary>
        public const string AssetName = "HavenAncientRespawner";

        /// <summary>
        /// Its stable registration key. Singular and not a prefix: there is exactly
        /// one shrine, on Haven, because Haven is the only island players are
        /// deposited on with nowhere to fly to.
        /// </summary>
        public const string WorldEntityKey = "wilderness-shrine";

        /// <summary>The name this teleport carries in the log, at both ends.</summary>
        public const string TeleportReason = "graduation";

        /// <summary>
        /// Where it stands, in Haven island-local metres - the SURFACE sample; the
        /// entity is placed exactly there, since a prop rests on the ground rather
        /// than standing off it the way a player capsule does.
        ///
        /// Derived from the same extracted LOD0 surface table every other Haven
        /// coordinate comes from (docs/research/world-data/island-surfaces/1431299145.json)
        /// under the landing rule in tools/world-import/generate-release-runtime-catalog.py,
        /// restricted to a walkable band around the spawn point:
        ///
        ///   * normal ny = 0.996, effectively flat
        ///   * all 8 neighbouring 8 m columns level within 1.42 m - a plateau
        ///   * 34.2 m from <see cref="SpawnPolicy.PlayerSpawnPosition"/>'s local
        ///     (208.00, 4.70, 4.00), and 0.20 m above it: the same shelf, a short
        ///     walk, no climb
        ///   * nearest authored static prop 15.26 m away in 3D and 13.73 m
        ///     horizontally, with NOTHING authored within 5 m horizontally anywhere
        ///     from 2 m below to 15 m above it
        ///   * clear of everything this server itself puts on Haven - 33 m from the
        ///     databank, 32 m from the static dev ship frame - which
        ///     WildernessShrineRegistrationTests checks against the whole registry
        ///     rather than against a list somebody has to remember to update
        ///
        /// WHY NOT CLOSER. The spawn point sits INSIDE the ruined metal camp, and
        /// every flat sample within ~25 m of it has that camp's platforms and
        /// walkways overhead or the dev ship frame on top of it. A monument buried
        /// under scrap is not a monument. Moving out to x = 176 leaves the camp
        /// while heading TOWARD the island's local origin - which is where retail's
        /// own quest text puts the chamber, "at the center of the island".
        ///
        /// Retail's actual pad position is NOT recoverable - everything
        /// Haven-specific was spawned by the GSim, and findings-haven.md is explicit
        /// that the barrier/teleporter geometry gives only a relative offset. So
        /// this is OUR placement, chosen for a clear approach, and it is said so
        /// here rather than dressed up as preserved.
        /// </summary>
        public static readonly (double X, double Y, double Z) HavenLocalPlacement = (176.00, 4.90, 16.00);

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
        // EntityPrefabs/HavenAncientRespawner_unityclient, read with UnityPy):
        // the prefab's ONLY InteractiveObjectVisualizer is not on the root. It is
        // on the deep child
        //
        //   HavenAncientRespawner_unityclient
        //     > HavenAncientRespawner > Ancient_Respawner > respawner_interior
        //       > SpawnPad            <- InteractiveObjectVisualizer, Verb = Activate
        //
        // which is the "platform inside the Revival Chamber" the retail quest text
        // names. SpawnPad's localPosition is (0, -2.704, 0) with an identity scale
        // chain, so its transform sits 2.704 m BELOW the entity origin, INSIDE the
        // plinth. Its own collision mesh (Respawner_Plate, convex) tops out at
        // prefab-local y = +0.39 and the decorative top plates at +0.50, so the
        // surface a player actually stands on is 3.204 m ABOVE the transform the
        // client measures range to. The plate's half-width is 3.57 m.
        //
        // This is why the shrine shipped with no prompt at all - see InteractRadius
        // below, and Multiplayer.InteractReach for the client rule it violates.
        // ------------------------------------------------------------------

        /// <summary>
        /// How far the standable top of the spawn plate is ABOVE the
        /// <c>InteractiveObjectVisualizer</c>'s own transform, metres. Measured:
        /// 2.704 m (the visualizer's local offset) + 0.500 m (the highest point of
        /// the plate's collision meshes).
        /// </summary>
        public const float PadTopAboveVisualiserMetres = 3.204f;

        /// <summary>
        /// Half-width of the spawn plate's collider, metres - how far out from the
        /// centre a player can stand and still be ON it. Measured from
        /// Respawner_Plate's local AABB (x and z both -3.57 .. +3.57).
        /// </summary>
        public const float PadHalfWidthMetres = 3.57f;

        /// <summary>
        /// WAREBORN TUNING: how far BEYOND the plate's edge the prompt should still
        /// be offered, metres, so the shrine announces itself as you walk up to it
        /// rather than only once both feet are on the plate. Two metres is about
        /// one stride.
        /// </summary>
        public const float ApproachRingMetres = 2.0f;

        /// <summary>
        /// 1210 InteractionEntry.radius, metres.
        ///
        /// THIS USED TO BE 3.0, COPIED FROM THE NUGGET, AND THAT IS THE BUG THAT
        /// MADE THE SHRINE SILENT. The client offers a prompt only while
        /// <c>Distance(visualizer.transform.position, player.transform.position)
        /// + 0.5f &lt; radius</c> (<see cref="InteractReach"/>, RECOVERED from
        /// <c>PlayerLookingAt.InRange</c>). The visualizer's transform is 3.204 m
        /// below the plate a player stands on, so a 3 m radius describes a sphere
        /// of usable radius 2.5 m centred 2.7 m underground: its highest point is
        /// still 0.2 m BELOW the entity origin, i.e. below the ground the shrine
        /// stands on. No position in the world satisfied it. Not a wrong verb, not
        /// a missing component - an interaction volume that never broke the surface.
        ///
        /// Derived instead of guessed: the radius that covers standing anywhere on
        /// the plate (<see cref="PadHalfWidthMetres"/>) plus one stride of approach
        /// (<see cref="ApproachRingMetres"/>) at the plate's standing height
        /// (<see cref="PadTopAboveVisualiserMetres"/>).
        /// <c>InteractReach.RadiusToCover(3.57 + 2.0, 3.204) = 7.0</c>, pinned by
        /// <c>WildernessShrineTests</c> so a future edit to any of the three
        /// measurements re-derives it rather than drifting away from it.
        ///
        /// It reads large next to the nugget's 3 m and it is not comparable to it:
        /// 3.204 m of it is spent going straight down to the visualizer, and 0.5 m
        /// to the client's own penalty, leaving
        /// <c>sqrt(6.5^2 - 3.204^2) = 5.66 m</c> of horizontal reach from the plate
        /// centre - about two metres past its own edge. A prompt still only appears
        /// while the player is LOOKING at a collider under the SpawnPad, so the
        /// radius widens how close you must be, never what counts as the shrine.
        /// </summary>
        public const float InteractRadius = 7.0f;

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
        /// The prefab's <c>SpawnPad</c> child carries the only
        /// <c>InteractiveObjectVisualizer</c> on the whole hierarchy and its
        /// serialized <c>Verb</c> field reads 1. Read straight out of the shipped
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
