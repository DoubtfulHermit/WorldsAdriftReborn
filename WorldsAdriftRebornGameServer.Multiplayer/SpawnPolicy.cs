namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// What a server-fabricated component seed is FOR. The component serializer
    /// used to switch on component id alone, so every entity that asked for
    /// 190602 TransformState got byte-identical data.
    /// </summary>
    public enum SeededEntityKind
    {
        /// <summary>The one shared island entity. Gets the island's world position.</summary>
        Island,

        /// <summary>
        /// A player avatar - the peer's own, or another player mirrored onto it.
        /// Gets the spawn point. A mirrored remote is overwritten within a tick
        /// by the relayed real transform, so its seed only has to be somewhere
        /// harmless; the OWNER's seed is the one that decides where they wake up.
        /// </summary>
        Player,
    }

    /// <summary>
    /// WHERE things go, and which entity gets which seed. Pure: no ENet, no
    /// Improbable types, no game install, so every coordinate below is asserted
    /// on natively in the test suite rather than by staring at a game client.
    ///
    /// THE ONE MECHANISM THAT MATTERS (docs/research/findings-spawn.md):
    /// a player is placed by <c>190602 TransformState.localPosition</c>, sent as
    /// FixedPointVector3 with <c>parent</c> ABSENT, in that entity's FIRST
    /// AddComponentOp - before the transform behaviours enable - and never
    /// re-sent. Position, gravity and ground collision already worked; the value
    /// was the only thing that was ever wrong. 190607 TeleportRequestState is for
    /// LATER teleports and respawn and is deliberately NOT used for first spawn.
    ///
    /// Consequently: never send a 190602 ComponentUpdate to a player (the client
    /// is the authoritative writer), and never re-send AddComponents to a live
    /// entity - see <see cref="MirrorSendPolicy.MayResend"/>. That rule got more
    /// dangerous with this change, not less: the default seed used to land you at
    /// the world origin, which is where the island was. The island is now 17 km
    /// away, so a stray re-seed is an out-of-world drop with no
    /// WorldEdgePushback to catch it (that behaviour never runs - it gates on
    /// world bounds this server never sends).
    /// </summary>
    public static class SpawnPolicy
    {
        /// <summary>
        /// Haven, the game's original starter island - Bossa-authored, 4.31 MiB
        /// and 90 colliders against the 28.21 MiB / 497 colliders of the island
        /// this server used before, so it streams in faster and narrows the
        /// window in which a player could be published before the ground exists.
        ///
        /// It is a constant because the id appeared as a bare string literal at
        /// three sites (the asset-load request, the AddEntityOp, and
        /// IslandState's prefab name) that MUST agree - a mismatch means the
        /// client is told to place an island it never loaded.
        ///
        /// Note what Haven is NOT: its bundle contains no teleporter, no barrier
        /// dome, no respawner, no starter ship. All of that was GSim-spawned
        /// entities and is gone. This is a small pretty island with a ruined
        /// metal camp, not a tutorial.
        /// </summary>
        public const string IslandAssetName = "1431299145@Island";

        /// <summary>
        /// The island this server shipped before Haven. Kept only so a test can
        /// assert we actually moved off it; nothing sends it.
        /// </summary>
        public const string PreviousIslandAssetName = "949069116@Island";

        /// <summary>
        /// Haven instance #5's world position, (17004.43, -318.6693420,
        /// -1134.16748) m.
        ///
        /// NOT a guess: it is entry 5 of the twelve `1431299145.json` placements
        /// in `docs/research/world-data/wamap-islands.json`, a preserved copy of
        /// the studio's own world map in Bossa's `MapFile` shape (266 islands).
        /// The same file places the island this server shipped before at
        /// (14321.44, -527.0027, -4647.39648) - which the server ignored, seeding
        /// it at the origin instead. There is a real world layout; we have simply
        /// never used it.
        ///
        /// Haven ships as ONE asset placed at TWELVE world positions - a
        /// north-south column, one physical copy per shard band. Any of the
        /// twelve is functionally identical; #5 is simply nearest the world
        /// centre in z. We spawn exactly one.
        ///
        /// This is the island's FIRST and ONLY 190602. It must never arrive as a
        /// follow-up update: IslandLocalTransformVisualizer.UpdatePosition does
        /// not teleport, it starts a 5-second smoothstep slide, which would drag
        /// the terrain out from under everyone standing on it.
        /// </summary>
        public static readonly FixedPointPosition IslandPosition =
            new FixedPointPosition(69650145, -1305269, -4645549);

        /// <summary>
        /// PROVISIONAL - the altitude is expected to change, the X and Z are not.
        ///
        /// Island-local (200.00, 3.96, 5.00) on Haven instance #5, i.e. world
        /// (17204.4300, -314.7093420, -1129.16748) m: about 8 m from the centroid
        /// of the ruined metal camp, the only constructed area on the island.
        ///
        /// WHY PROVISIONAL: the Y came from our extracted island-surface tables,
        /// and those tables are known wrong. On the one island we can check
        /// empirically they are off by ~25 m, because the extractor's offs()
        /// accumulates only m_LocalPosition up the transform hierarchy and
        /// ignores rotation and scale. A corrected coordinate is being derived
        /// separately.
        ///
        /// SWAPPING IT IN IS A ONE-LINE CHANGE: replace the three numbers here
        /// (or call FixedPointPosition.FromMetres with the corrected metres) and
        /// nothing else in the server needs to know.
        ///
        /// If the altitude is too low the player interpenetrates the ground; if
        /// it is too high they free-fall - and fall damage does not exist on this
        /// server, so a bad spawn is an endless fall rather than a death.
        /// </summary>
        public static readonly FixedPointPosition PlayerSpawnPosition =
            new FixedPointPosition(70469345, -1289049, -4625069);

        /// <summary>
        /// The value seeded into 8055 NewPlayerState. FALSE, deliberately, and
        /// spawning on the real Haven does not change that.
        ///
        /// 8055 is the SOLE runtime source of truth for "this player is in
        /// Haven" - the client does not derive it from position. The only exit is
        /// component 8056 LeaveHavenRequest, which has ZERO references in the
        /// entire client, is triggered and consumed server-side, and is not
        /// implemented here. There is no handler that could ever flip 8055 back,
        /// and the client cannot: it has no writer, and 8055 is correctly absent
        /// from <see cref="MirrorSendPolicy.AuthoritativeComponents"/>.
        ///
        /// So `true` is a permanent prison: five UI features disabled forever,
        /// plus every biome banner in the game suppressed, because
        /// DisplayBiomeNotification is called from RespawnVisualizer.Update on a
        /// one-second poll that checks this flag.
        ///
        /// `false` is silent - NewPlayerVisualiser.OnNewPlayerChanged only acts
        /// on the true-to-false EDGE, so seeding false fires nothing.
        ///
        /// If real Haven progression is ever built, the trigger is server-side:
        /// watch for the RevivalChamberInterface knowledge node, then push 8055
        /// false and the bloom flash and quest unlock fire for free.
        /// </summary>
        public const bool SeedIsNewPlayer = false;

        /// <summary>
        /// Which kind of entity a seed is being fabricated for.
        ///
        /// The island is identified by its entity id, which is allocated once and
        /// shared by every client (cross-client references resolve by id). Anything
        /// else this server creates is a player avatar.
        ///
        /// <paramref name="islandEntityId"/> is nullable because the id is
        /// allocated lazily, on the island's AddEntityOp. Before that moment
        /// nothing can be the island - and asking must not be what allocates it,
        /// or the answer would depend on who asked first.
        /// </summary>
        public static SeededEntityKind KindOf(long entityId, long? islandEntityId)
        {
            return islandEntityId.HasValue && entityId == islandEntityId.Value
                ? SeededEntityKind.Island
                : SeededEntityKind.Player;
        }

        /// <summary>The 190602 localPosition seed for a kind of entity.</summary>
        public static FixedPointPosition TransformSeedFor(SeededEntityKind kind)
        {
            return kind == SeededEntityKind.Island ? IslandPosition : PlayerSpawnPosition;
        }

        /// <summary>
        /// The 190602 localPosition seed for one entity. The whole point of this
        /// module: before it existed the serializer switched on component id
        /// alone, so the island and the player were handed the same transform.
        /// With Haven that is fatal rather than untidy - it is one asset placed
        /// at twelve world positions, so "the default" is not a position any
        /// island is actually at.
        /// </summary>
        public static FixedPointPosition TransformSeedFor(long entityId, long? islandEntityId)
        {
            return TransformSeedFor(KindOf(entityId, islandEntityId));
        }
    }
}
