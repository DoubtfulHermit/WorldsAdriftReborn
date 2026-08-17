using WorldsAdriftRebornGameServer.Game.Crew;
using WorldsAdriftRebornGameServer.Game.Persistence;
using WorldsAdriftRebornGameServer.Multiplayer;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using WorldsAdriftRebornGameServer.Multiplayer.Wilderness;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// THE SEAM between the shrine on Haven and the four systems that already
    /// exist: crews, stored positions, the island topology, and teleport.
    ///
    /// It makes no decisions. Which island a player goes to is
    /// <see cref="WildernessGraduationPolicy"/>; whether the arrival is safe yet is
    /// the terrain gate inside <see cref="TeleportService.Graduate"/>; what "the
    /// Wilderness" even is is <see cref="WildernessCatalog"/>. All three are pure
    /// and tested natively. What is left here - reading a uid off an entity,
    /// reading a row, writing a row, and calling the wire - is the part that cannot
    /// be tested without a server, which is exactly why it is kept this thin.
    ///
    /// ORDER MATTERS, and the order is: decide, RECORD, then teleport.
    /// Recording first means a crash between the two leaves the character with a
    /// stored Wilderness position, which the logout restore then honours safely on
    /// their next login - through the same terrain gate. Teleporting first and
    /// crashing would leave them standing on an island the server has no memory of
    /// putting them on, which their next login would undo by pulling them back to
    /// Haven.
    /// </summary>
    internal static class WildernessGraduationService
    {
        /// <summary>
        /// The draw. A shared <see cref="Random"/> and not a per-call one, because a
        /// per-call <c>new Random()</c> seeded from the clock hands the same island
        /// to every member of a crew who touches the shrine in the same
        /// millisecond - which is precisely the moment this feature is used.
        ///
        /// It is behind a lock rather than a ThreadStatic: the draw is spent at most
        /// once per graduation, so contention is not a real number, and one shared
        /// sequence keeps a boot's island spread reproducible from its own log.
        /// This is the ONLY randomness in the feature; the policy never sees a
        /// global RNG, it is handed an index.
        /// </summary>
        private static readonly Random Draw = new Random();
        private static readonly object DrawLock = new object();

        private static int PickIndex(int count)
        {
            lock (DrawLock) return Draw.Next(count);
        }

        /// <summary>
        /// The Tier-1 destinations this boot can actually deliver: registered
        /// island topology intersected with the Wilderness.
        /// </summary>
        internal static IReadOnlyList<WildernessDestination> Open() =>
            WildernessCatalog.Open(WorldsAdriftRebornGameServer.IslandTopology.All);

        /// <summary>The startup banner's line about the shrine.</summary>
        internal static void ReportState()
        {
            if (!WorldsAdriftRebornGameServer.WildernessShrineEnabled)
            {
                Console.WriteLine("[info] wilderness shrine is OFF ("
                    + WildernessShrine.EnabledEnvVar + "); Haven has no exit this boot.");
                return;
            }

            IReadOnlyList<WildernessDestination> open = Open();
            if (open.Count == 0)
            {
                Console.WriteLine("[warning] wilderness shrine stands on Haven but the Wilderness is"
                    + " CLOSED: no tier-1 island is registered. Set "
                    + ReleaseWorldRolloutPolicy.EnvVar + "=tier1 to open it. Players who use the"
                    + " shrine are refused with a message rather than moved.");
                return;
            }

            Console.WriteLine("[info] wilderness shrine is ON at Haven local ("
                + WildernessShrine.HavenLocalPlacement.X.ToString("0.##") + ", "
                + WildernessShrine.HavenLocalPlacement.Y.ToString("0.##") + ", "
                + WildernessShrine.HavenLocalPlacement.Z.ToString("0.##") + "): "
                + open.Count + " tier-1 island(s) open, "
                + string.Join(", ", open.Take(3).Select(d => d.DisplayName))
                + (open.Count > 3 ? ", ..." : string.Empty) + ".");
        }

        /// <summary>
        /// One player used the shrine. Returns true when they are on their way.
        ///
        /// Safe to call more than once: a second use while the first is still
        /// deferred simply resolves to the same island (the home is recorded before
        /// the teleport), so a player leaning on the key cannot end up somewhere
        /// else.
        /// </summary>
        internal static bool Use(long playerEntityId)
        {
            string actorUid = CharacterOwnership.UidForEntity(playerEntityId);
            IReadOnlyList<WildernessDestination> open = Open();

            Multiplayer.Crew.Crew? crew = string.IsNullOrEmpty(actorUid)
                ? null
                : CrewService.CrewOf(actorUid);
            CrewSnapshot? snapshot = crew == null
                ? null
                : CrewSnapshot.Of(crew.Id, crew.LeaderUid, crew.Members);

            WildernessGraduation decision = WildernessGraduationPolicy.Decide(
                actorUid, snapshot, open, HomeOf, PickIndex);

            if (!decision.Ok)
            {
                Console.WriteLine("[warning] " + WildernessShrine.TeleportReason + ": refusing entity "
                    + playerEntityId + " (" + decision.Verdict + "): " + decision.Message);
                Tell(actorUid, decision.Message, ok: false);
                return false;
            }

            Console.WriteLine("[info] " + WildernessShrine.TeleportReason + ": entity " + playerEntityId
                + " (character:" + Describe(actorUid) + ") -> " + decision.Destination
                + " via " + decision.Source + "; " + decision.Destination.Provenance + ".");

            // RECORD FIRST. See the type remarks: a stored Wilderness position is
            // safe on its own, because the logout restore honours it through the
            // same terrain gate. A teleport with no record is not.
            foreach (string uid in decision.RecordFor)
            {
                if (!Guid.TryParse(uid, out Guid parsed)) continue;
                if (PlayerPositionService.Record(parsed, decision.Destination.Position))
                {
                    Console.WriteLine("[info] " + WildernessShrine.TeleportReason
                        + ": recorded character:" + uid + "'s home as "
                        + decision.Destination.DisplayName + ".");
                }
                else
                {
                    // Not fatal, and not silent. Without the row this graduation is
                    // a one-session trip: the player arrives, and their next login
                    // puts them back at the spawn point.
                    Console.WriteLine("[warning] " + WildernessShrine.TeleportReason
                        + ": could not record character:" + uid + "'s home; they will arrive but"
                        + " will not return here on their next login.");
                }
            }

            Tell(actorUid, decision.Message, ok: true);
            return WorldsAdriftRebornGameServer.Teleports.Graduate(
                playerEntityId,
                WildernessCatalog.AsTeleportDestination(
                    decision.Destination, WildernessShrine.TeleportReason));
        }

        /// <summary>
        /// A character's Wilderness home: the Tier-1 island their already-persisted
        /// logout position stands on, or null.
        ///
        /// Deliberately reads the position TABLE and not the live transform. A
        /// crew's leader is frequently offline when a member graduates, and their
        /// stored row is the only thing that can answer for them; using a live
        /// transform would make the crew rule depend on who happened to be logged
        /// in, which is the one thing it must not do.
        /// </summary>
        private static IslandId? HomeOf(string uid)
        {
            if (!Guid.TryParse(uid, out Guid parsed)) return null;
            return WildernessGraduationPolicy.HomeIslandOf(
                PlayerPositionService.StoredFor(parsed), Open());
        }

        /// <summary>
        /// Says one line to the player.
        ///
        /// It rides the CREW feedback event, which is not where a shrine's message
        /// obviously belongs - but it is the ONLY single-line channel to a client
        /// this server has, the retail UI renders it verbatim, and graduation is a
        /// crew mechanic in retail's own telling ("teleport together with other
        /// players on the platform"). Best-effort by construction: a player whose
        /// uid never arrived gets nothing, and the log line above is then the only
        /// record, which is the same bargain every other feedback path here makes.
        /// </summary>
        private static void Tell(string uid, string message, bool ok)
        {
            if (string.IsNullOrEmpty(uid)) return;
            CrewPush.Feedback(uid, message, ok);
        }

        private static string Describe(string uid) =>
            string.IsNullOrEmpty(uid) ? "unknown" : uid;
    }
}
