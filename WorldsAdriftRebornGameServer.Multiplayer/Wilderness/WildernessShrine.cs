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
        ///   * normal ny = 1.000, dead flat
        ///   * all 8 neighbouring 8 m columns level within 1.64 m - a plateau
        ///   * 12.0 m from <see cref="SpawnPolicy.PlayerSpawnPosition"/>'s local
        ///     (208.00, 4.70, 4.00): in front of a player the moment they spawn,
        ///     without being on top of them
        ///   * nearest authored static prop 7.34 m away in 3D, and NO authored prop
        ///     within 4 m horizontally and 12 m overhead, so it is neither buried
        ///     nor under the ruined camp's platforms
        ///   * 8.0 m from the Haven databank at local (208.00, 4.99, 8.00), which
        ///     puts spawn, databank and shrine on one straight walk up +z
        ///
        /// Retail's own pad position is NOT recoverable - everything Haven-specific
        /// was spawned by the GSim, and findings-haven.md is explicit that the
        /// barrier/teleporter geometry gives only a relative offset. So this is OUR
        /// placement, chosen for discoverability, and it is said so here rather
        /// than dressed up as preserved.
        /// </summary>
        public static readonly (double X, double Y, double Z) HavenLocalPlacement = (208.00, 4.80, 16.00);

        /// <summary>Its global position, given the Haven definition it stands on.</summary>
        public static FixedPointPosition PositionOn(IslandDefinition haven)
        {
            if (haven == null) throw new ArgumentNullException(nameof(haven));
            return haven.LocalToGlobal(
                HavenLocalPlacement.X, HavenLocalPlacement.Y, HavenLocalPlacement.Z);
        }

        /// <summary>
        /// 1210 InteractionEntry.radius, metres. Matched to the shipyard console's,
        /// the helm's and the nugget's 3 m so "how close do I have to be" has one
        /// answer across every interaction this server seeds.
        /// </summary>
        public const float InteractRadius = 3.0f;

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
        /// THE HEDGE, stated plainly. <c>InteractiveObjectVisualizer.OnEnable</c>
        /// does <c>Interactions.FirstOrDefault(i =&gt; i.verb == Verb)</c> against the
        /// verb the PREFAB baked, and we do not know which one
        /// <c>HavenAncientRespawner</c> baked. A single wrong guess is not a
        /// degraded prompt, it is no prompt at all, with nothing in any log to say
        /// why. Serving one entry per plausible verb makes that lookup succeed
        /// regardless: the visualizer takes the first entry MATCHING its own verb
        /// and ignores the rest, so extra entries are inert.
        ///
        /// The three: <c>Activate</c> because the quest text says "activate";
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
