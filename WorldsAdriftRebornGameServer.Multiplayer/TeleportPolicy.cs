namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// One named place the server can put a player.
    /// </summary>
    public readonly struct TeleportDestination : IEquatable<TeleportDestination>
    {
        public TeleportDestination(string name, FixedPointPosition position, bool landsOnLoadedGround, string description)
        {
            Name = name;
            Position = position;
            LandsOnLoadedGround = landsOnLoadedGround;
            Description = description;
        }

        /// <summary>The lookup key. Lower-case ASCII; see <see cref="TeleportPolicy.TryResolve"/>.</summary>
        public string Name { get; }

        /// <summary>
        /// Where the player ends up, in the game's own Q52.12 world encoding -
        /// the SAME space as 190602 TransformState.localPosition, because the
        /// client's teleport consumer remaps it identically (see
        /// <see cref="TeleportPolicy"/> remarks).
        /// </summary>
        public FixedPointPosition Position { get; }

        /// <summary>
        /// Whether there is, TODAY, collidable geometry at this position on a
        /// connected client.
        ///
        /// False for every destination that is not Haven instance #5, and that
        /// is not pedantry: this server spawns exactly ONE island entity. A
        /// player teleported to any other island's coordinates arrives in empty
        /// air over an island that was never streamed in, and this server writes
        /// no fall damage and no world bounds - so the fall does not end. Those
        /// destinations become real only once entity spawning is generalised
        /// past {island, player} (findings-first-ship.md, build order step 1).
        /// </summary>
        public bool LandsOnLoadedGround { get; }

        /// <summary>Human-readable provenance, for the log line and for whoever reads this next.</summary>
        public string Description { get; }

        public bool Equals(TeleportDestination other) => Name == other.Name && Position == other.Position;

        public override bool Equals(object? obj) => obj is TeleportDestination other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Name, Position);

        public override string ToString() => Name + " " + Position;
    }

    /// <summary>
    /// One parsed teleport instruction: where to, and for whom.
    /// </summary>
    public readonly struct TeleportCommand
    {
        public TeleportCommand(TeleportDestination destination, long? entityId)
        {
            Destination = destination;
            EntityId = entityId;
        }

        public TeleportDestination Destination { get; }

        /// <summary>The single player entity to move, or null for "everyone in the world".</summary>
        public long? EntityId { get; }

        public override string ToString()
        {
            return "teleport " + (EntityId.HasValue ? "entity " + EntityId.Value : "everyone")
                + " -> " + Destination;
        }
    }

    /// <summary>
    /// WHERE a teleport goes and WHICH request number carries it. Pure: no ENet,
    /// no Improbable types, no game install, so the coordinates and the
    /// counter rule are asserted natively rather than by watching a client.
    ///
    /// THE MECHANISM (verified against the decompile at
    /// ~/Games/WAReborn-decompiled, and see docs/research/loop/findings-first-ship.md
    /// "TELEPORT IS CHEAPER THAN ASSUMED"):
    ///
    /// The shipped player prefab carries THREE readers of 190607
    /// TeleportRequestState. Only one of them can enable on this server:
    ///
    /// * <c>TeleportTransformVisualizer</c> (Assets.Scripts.Physics) needs
    ///   190602 Reader, <b>1073 Writer</b>, 190607 Reader. We already grant 1073
    ///   (MirrorSendPolicy.AuthoritativeComponents), so <b>no new authority
    ///   grant is needed for teleport at all</b>. It sets transform.position
    ///   DIRECTLY, calls PlayerMove.Respawn (which zeroes velocity), and acks by
    ///   writing 1073 <c>lastExecutedRequest</c>.
    /// * <c>LocalTransformTeleportBehaviour</c> needs <b>190606 Writer</b>,
    ///   which we do not grant, so it never runs. It is the expensive path.
    /// * <c>RespawnVisualizer</c> needs 1092 R, <b>1093 W</b>, <b>1072 W</b>,
    ///   190602 R and 1077 R on top of 190607. We grant neither 1093 nor 1072
    ///   and seed neither 1092 nor 1093, so it stays disabled - which matters,
    ///   because it subscribes to LocalPositionUpdated with NO request dedup at
    ///   all and would fire a second Respawn on every update.
    ///   (findings-first-ship.md names 1160 here; that is wrong. There are two
    ///   HealthStateReader types and RespawnVisualizer imports the
    ///   Bossa.Travellers.Player one, which is 1077, not the Creatures one.)
    ///
    /// TWO RULES THAT ARE NOT OBVIOUS AND ARE SILENT WHEN BROKEN:
    ///
    /// 1. <b>Seed <c>request</c> = <see cref="SeedRequest"/> = 0.</b> The
    ///    generated <c>RequestUpdated</c> event replays the CURRENT value the
    ///    instant a subscriber attaches (TeleportRequestState.cs:283-294 does
    ///    <c>value(Data.request)</c> inside <c>add</c>). The visualizer
    ///    subscribes in OnEnable, i.e. at checkout. Seed anything above zero and
    ///    every player teleports the moment they finish loading - and so does
    ///    every re-serve of the component, since the serializer fabricates the
    ///    seed fresh each time. Zero is the one value that cannot fire, because
    ///    the guard is strictly greater-than against a 1073 field that also
    ///    defaults to 0.
    /// 2. <b>Send <c>parent</c> ABSENT.</b> The visualizer's branch is
    ///    <c>if (!Parent.HasValue)</c> -> set position. With a parent PRESENT it
    ///    computes a GameObject name string, discards it, moves nothing, and
    ///    still acks. A parented teleport is therefore not a different teleport,
    ///    it is a no-op that lies.
    ///
    /// The position type differs from 190602's: 190607 carries
    /// <c>Option&lt;Vector3d&gt;</c> (three doubles), not FixedPointVector3.
    /// Both are fed through the same <c>RemapGlobalToUnityVector()</c>, so the
    /// NUMBERS are in the same global-metre space - which is why every
    /// coordinate here is a <see cref="FixedPointPosition"/> and converted to
    /// metres at the wire edge. Keeping one representation is what lets a
    /// destination be compared to <see cref="SpawnPolicy.PlayerSpawnPosition"/>
    /// in a test.
    /// </summary>
    public static class TeleportPolicy
    {
        /// <summary>
        /// TeleportRequestState. Seeded on the player and updated to move them.
        /// Deliberately NOT added to MirrorSendPolicy.AuthoritativeComponents:
        /// the server is the only writer, and granting it would let a client
        /// teleport itself anywhere.
        /// </summary>
        public const uint TeleportRequestStateComponentId = 190607;

        /// <summary>
        /// ClientAuthoritativePlayerState - where the ACK lands, in its
        /// <c>lastExecutedRequest</c> field. Same id as
        /// <see cref="MirrorSendPolicy.ClientAuthoritativePlayerStateComponentId"/>;
        /// restated here so the teleport story reads on its own.
        /// </summary>
        public const uint AckComponentId = MirrorSendPolicy.ClientAuthoritativePlayerStateComponentId;

        /// <summary>
        /// The <c>request</c> value 190607 is SEEDED with. Must be 0 - see rule 1
        /// in the type remarks. This is the only value that cannot teleport a
        /// player at checkout.
        /// </summary>
        public const int SeedRequest = 0;

        /// <summary>Haven instance #5's spawn point: exactly where players already wake up.</summary>
        public const string HavenName = "haven";

        /// <summary>Haven instance #6, the next copy north up the shard column.</summary>
        public const string HavenNorthName = "haven-north";

        /// <summary>949069116 "Shattered Mausoleum" - a genuinely different island.</summary>
        public const string MausoleumName = "mausoleum";

        /// <summary>
        /// The island-local offset that produced <see cref="SpawnPolicy.PlayerSpawnPosition"/>
        /// from <see cref="SpawnPolicy.IslandPosition"/>: (208.00, 6.70, 4.00) m,
        /// a measured LOD0 surface vertex at 4.70 m plus a 2.00 m stand-off.
        ///
        /// Named because it is what makes <see cref="HavenNorthName"/> a real
        /// place rather than a number: Haven is ONE asset at TWELVE positions,
        /// so the same local offset is the same patch of ground on every copy.
        /// </summary>
        public static readonly (double X, double Y, double Z) HavenSpawnLocalOffset = (208.00, 6.70, 4.00);

        /// <summary>
        /// Every place the server can send someone, in menu order.
        ///
        /// Coordinates come from docs/research/world-data/wamap-islands.json -
        /// Bossa's own map file, 266 placements - and, for the local offsets,
        /// from docs/research/world-data/island-surfaces/, the TRS-composed
        /// surface grid. They are written as METRES and encoded by
        /// <see cref="FixedPointPosition.FromMetres"/> rather than as raw Q52.12
        /// integers, so the provenance stays legible; the truncation is the
        /// client's own, so the numbers still agree to the last unit.
        /// </summary>
        public static readonly IReadOnlyList<TeleportDestination> Destinations = new[]
        {
            // The get-me-unstuck destination, and the only one that is real
            // today. Shares its value with SpawnPolicy so a test can assert the
            // two never drift apart - if they ever did, "teleport me home" would
            // put you somewhere you have never spawned.
            new TeleportDestination(
                HavenName,
                SpawnPolicy.PlayerSpawnPosition,
                landsOnLoadedGround: true,
                "Haven #5 spawn point, island-local (208.00, 6.70, 4.00). The only "
                + "destination with ground under it: it is the island this server spawns."),

            // Haven instance #6, wamap-islands.json entry 259, at
            // (17003.416, -212.325027, 1826.00183) m - 2962 m north of #5 and the
            // NEAREST island in the entire world. Same asset as #5, so the
            // client's bundle is already loaded; only the entity is missing.
            // That makes this the cheapest possible second place to stand, and
            // the honest test of "is teleport the exit from one rock?".
            new TeleportDestination(
                HavenNorthName,
                FixedPointPosition.FromMetres(
                    17003.416 + HavenSpawnLocalOffset.X,
                    -212.325027 + HavenSpawnLocalOffset.Y,
                    1826.00183 + HavenSpawnLocalOffset.Z),
                landsOnLoadedGround: false,
                "Haven #6 (wamap entry 259), the next copy north, 2962 m away - the "
                + "nearest island to spawn. Same bundle as #5; NO entity is spawned there yet."),

            // 949069116 "Shattered Mausoleum" at (14321.44, -527.0027, -4647.39648),
            // 4425 m from Haven #5 - the island THIS SERVER SHIPPED BEFORE HAVEN
            // (SpawnPolicy.PreviousIslandAssetName), so it is the one other island
            // known to load and stand on.
            //
            // Local (-72.0, -182.10, -128.0) is a top-surface cell from
            // island-surfaces/949069116.json with normal ny = 1.000 and all eight
            // 8 m neighbours within 0.8 m - a genuine plateau, not a spire. The
            // +2.00 m stand-off matches Haven's convention. It has NOT been
            // ground-truthed the way Haven's point was, and the y is the grid's,
            // not a measured vertex.
            new TeleportDestination(
                MausoleumName,
                FixedPointPosition.FromMetres(
                    14321.44 + -72.0,
                    -527.0027 + -182.10 + 2.00,
                    -4647.39648 + -128.0),
                landsOnLoadedGround: false,
                "Shattered Mausoleum (949069116), 4425 m away - the island this server "
                + "used before Haven. Flat top-surface cell; NO entity is spawned there yet."),
        };

        /// <summary>Destination names, in menu order. For the log banner and error messages.</summary>
        public static IReadOnlyList<string> Names
        {
            get
            {
                List<string> names = new List<string>(Destinations.Count);
                foreach (TeleportDestination destination in Destinations)
                {
                    names.Add(destination.Name);
                }
                return names;
            }
        }

        /// <summary>
        /// The destination that is safe to send someone to unprompted: the one
        /// with ground under it. Used as the fallback so a typo can never drop a
        /// player into an endless fall.
        /// </summary>
        public static TeleportDestination SafeDestination
        {
            get
            {
                foreach (TeleportDestination destination in Destinations)
                {
                    if (destination.LandsOnLoadedGround)
                    {
                        return destination;
                    }
                }
                throw new InvalidOperationException("no destination has ground under it");
            }
        }

        /// <summary>
        /// Looks a destination up by name. Case- and whitespace-insensitive
        /// because the name arrives from a human typing into a file.
        /// </summary>
        public static bool TryResolve(string? name, out TeleportDestination destination)
        {
            destination = default;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            string key = name.Trim().ToLowerInvariant();
            foreach (TeleportDestination candidate in Destinations)
            {
                if (candidate.Name == key)
                {
                    destination = candidate;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Parses one line of the trigger file into a command.
        ///
        /// Grammar, deliberately tiny because a human types it under a `echo`:
        /// <code>
        ///   &lt;destination&gt;                 -- everyone in the world
        ///   &lt;destination&gt; &lt;entityId&gt;      -- just that player entity
        ///   # anything                     -- comment, ignored
        ///   (blank)                        -- ignored
        /// </code>
        /// Returns false for blank/comment/garbage and puts the reason in
        /// <paramref name="error"/>; an empty reason means "nothing to do", which
        /// the caller must not log as a failure or an empty file would spam.
        /// </summary>
        public static bool TryParseCommand(string? line, out TeleportCommand command, out string error)
        {
            command = default;
            error = string.Empty;

            if (line == null)
            {
                return false;
            }

            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                return false;
            }

            string[] parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 2)
            {
                error = "expected '<destination> [entityId]', got " + parts.Length + " words";
                return false;
            }

            if (!TryResolve(parts[0], out TeleportDestination destination))
            {
                error = "unknown destination '" + parts[0] + "'; known: " + string.Join(", ", Names);
                return false;
            }

            long? entityId = null;
            if (parts.Length == 2)
            {
                if (!long.TryParse(parts[1], out long parsed))
                {
                    error = "'" + parts[1] + "' is not an entity id";
                    return false;
                }
                entityId = parsed;
            }

            command = new TeleportCommand(destination, entityId);
            return true;
        }
    }

    /// <summary>
    /// The request-counter rule, and the record of which teleports have landed.
    ///
    /// WHY IT IS A TYPE AND NOT AN INT++: the client's guard is
    /// <c>requestId &gt; _playerState.Data.lastExecutedRequest</c>, compared
    /// against a field on 1073 - a component the CLIENT owns and re-publishes
    /// every tick. So the number the server must beat is not the server's own
    /// last send, it is whatever the client last told us it executed. Those two
    /// can disagree in both directions:
    ///
    /// * A dropped or ignored teleport leaves the server's high-water mark ahead
    ///   of the ack. Counting from the ack alone would re-send a number the
    ///   client has already seen and silently do nothing.
    /// * A reconnect, or a re-seed of 1073, resets the client's field toward 0.
    ///   Counting from the server's high-water mark alone is then merely
    ///   wasteful, not wrong - but a client that somehow reports a HIGHER value
    ///   than we ever sent (another writer, a stale mirror) would make every
    ///   subsequent teleport a no-op.
    ///
    /// Taking the max of both and adding one is correct under all four
    /// combinations, which is the whole content of this class and is why it has
    /// tests instead of a comment.
    /// </summary>
    public sealed class TeleportRequestCounter
    {
        private readonly Dictionary<long, int> _highWater = new();
        private readonly Dictionary<long, int> _acked = new();

        /// <summary>
        /// The next request number to put on the wire for one entity, given what
        /// we have sent and what the client has acked. Records it as sent.
        /// </summary>
        public int Next(long entityId)
        {
            _highWater.TryGetValue(entityId, out int highWater);
            _acked.TryGetValue(entityId, out int acked);

            int next = NextRequest(highWater, acked);
            _highWater[entityId] = next;
            return next;
        }

        /// <summary>
        /// The rule itself, free of any bookkeeping: strictly greater than both
        /// the last value we sent and the last value the client says it ran.
        ///
        /// Never returns 0 or less. 0 is reserved as the SEED value (see
        /// <see cref="TeleportPolicy.SeedRequest"/>) precisely because it can
        /// never satisfy the client's strictly-greater-than guard, which is what
        /// stops the checkout-time replay of RequestUpdated from teleporting
        /// everyone the instant they load in.
        /// </summary>
        public static int NextRequest(int lastSent, int lastAcked)
        {
            int baseline = lastSent > lastAcked ? lastSent : lastAcked;

            // A negative baseline can only be garbage or an uninitialised read;
            // the seed is 0 and the counter only ever climbs. Clamping rather
            // than trusting it keeps the first real request at 1.
            if (baseline < 0)
            {
                baseline = 0;
            }

            // 2^31 teleports is not reachable, but wrapping to a negative number
            // would permanently disable teleport for that entity, so saturate.
            return baseline == int.MaxValue ? int.MaxValue : baseline + 1;
        }

        /// <summary>
        /// Records a 1073 <c>lastExecutedRequest</c> the client published.
        /// Returns true if this ack is NEW information - i.e. it advanced the
        /// recorded value - so the caller can log a landing exactly once instead
        /// of on every one of the client's per-tick 1073 updates.
        /// </summary>
        public bool RecordAck(long entityId, int lastExecutedRequest)
        {
            if (_acked.TryGetValue(entityId, out int previous) && lastExecutedRequest <= previous)
            {
                return false;
            }

            _acked[entityId] = lastExecutedRequest;
            return true;
        }

        /// <summary>The last request number sent to this entity, or null if none.</summary>
        public int? LastSent(long entityId)
        {
            return _highWater.TryGetValue(entityId, out int value) ? value : null;
        }

        /// <summary>The last request number this entity acked, or null if none.</summary>
        public int? LastAcked(long entityId)
        {
            return _acked.TryGetValue(entityId, out int value) ? value : null;
        }

        /// <summary>
        /// A request sent that the client has not yet acked, or null if
        /// everything we asked for has landed. This is the entire observable
        /// answer to "did the teleport work?" available to a server that cannot
        /// see the client's transform.
        /// </summary>
        public int? Outstanding(long entityId)
        {
            _highWater.TryGetValue(entityId, out int sent);
            _acked.TryGetValue(entityId, out int acked);
            return sent > acked ? sent : null;
        }

        /// <summary>Drops an entity's counters. Called when its peer disconnects.</summary>
        public void Forget(long entityId)
        {
            _highWater.Remove(entityId);
            _acked.Remove(entityId);
        }
    }
}
