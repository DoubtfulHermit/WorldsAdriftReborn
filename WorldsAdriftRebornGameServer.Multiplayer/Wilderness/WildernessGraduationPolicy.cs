using WorldsAdriftRebornGameServer.Multiplayer.Islands;

namespace WorldsAdriftRebornGameServer.Multiplayer.Wilderness
{
    /// <summary>
    /// A crew, flattened to the only three facts graduation needs.
    ///
    /// Deliberately NOT <c>Multiplayer.Crew.Crew</c>. Crew membership is also
    /// mutated by an HTTP path on another branch, and the crew domain will keep
    /// growing slots, beacons and alliances; a snapshot means this policy can
    /// never accidentally depend on any of that, and a test can state a crew in
    /// one line. Build it with <see cref="Of"/> at the seam.
    /// </summary>
    /// <param name="CrewId">The crew's stable id. Only ever logged.</param>
    /// <param name="LeaderUid">The current leader's durable character uid.</param>
    /// <param name="Members">Every member's uid IN JOIN ORDER. The founder is
    /// index 0; a promoted successor keeps its original index, because promotion
    /// changes who leads, not who joined when.</param>
    public readonly record struct CrewSnapshot(
        string CrewId,
        string LeaderUid,
        IReadOnlyList<string> Members)
    {
        public static CrewSnapshot Of(string crewId, string leaderUid, IEnumerable<string> members) =>
            new CrewSnapshot(crewId, leaderUid, members.ToArray());
    }

    /// <summary>How the island in a granted graduation was arrived at.</summary>
    public enum WildernessSource
    {
        /// <summary>This character already had a Wilderness home. They went back to it.</summary>
        OwnHome,

        /// <summary>The crew leader had a home; the crew's island is the leader's.</summary>
        CrewLeaderHome,

        /// <summary>
        /// The leader had none, so the earliest-joined member who had one supplied
        /// the crew's island.
        /// </summary>
        CrewMemberHome,

        /// <summary>Nobody in the crew had a home. One was drawn and recorded for all of them.</summary>
        FreshCrewIsland,

        /// <summary>A crewless character with no home. One was drawn for them alone.</summary>
        FreshSoloIsland,
    }

    public enum WildernessVerdict
    {
        Granted,

        /// <summary>
        /// No Tier-1 island is registered on this boot, so there is nowhere to go.
        /// A refusal, never a teleport: the alternative is dropping somebody at a
        /// coordinate whose terrain this server never spawned.
        /// </summary>
        WildernessClosed,

        /// <summary>
        /// The actor has no durable character uid yet. It arrives with 1088, after
        /// checkout, so a player who interacts in the first moments of a session
        /// genuinely may not have one - and a graduation keyed on nothing would
        /// write a home row nobody could ever find again.
        /// </summary>
        UnknownCharacter,
    }

    /// <summary>One graduation decision and everything the seam must do about it.</summary>
    /// <param name="Verdict">Granted, or why not.</param>
    /// <param name="Destination">Where to send the actor. Meaningless unless granted.</param>
    /// <param name="Source">Which clause of the rule below produced it.</param>
    /// <param name="RecordFor">Whose stored position must be written to
    /// <see cref="Destination"/>. Always contains the actor when granted; contains
    /// the whole crew only for <see cref="WildernessSource.FreshCrewIsland"/>.</param>
    /// <param name="Message">One sentence for the player. The crew feedback line is
    /// the only single-line channel to a client this server has, so every outcome
    /// has to fit in one.</param>
    public readonly record struct WildernessGraduation(
        WildernessVerdict Verdict,
        WildernessDestination Destination,
        WildernessSource Source,
        IReadOnlyList<string> RecordFor,
        string Message)
    {
        public bool Ok => Verdict == WildernessVerdict.Granted;
    }

    /// <summary>
    /// WHERE THE SHRINE SENDS YOU. Pure: no ENet, no database, no clock, and - the
    /// point of the whole exercise - no global RNG. The draw is an injected
    /// <c>Func&lt;int, int&gt;</c>, so every case below is asserted natively with a
    /// stated island rather than by running the server until it happens.
    ///
    /// ==================== THE RULE ====================
    ///
    /// A character's HOME is the Tier-1 island their stored position sits on. It is
    /// not a new table: <c>character_positions</c> already persists per character,
    /// and <see cref="HomeIslandOf"/> reads an island back out of a coordinate with
    /// <see cref="IslandLocationPolicy"/>. So "where do you live" and "where do you
    /// log back in" cannot drift apart - they are the same row.
    ///
    /// CREWLESS:
    ///   1. Your own home, if you have one and it is open this boot -> OwnHome.
    ///   2. Otherwise draw one -> FreshSoloIsland, recorded for you.
    ///
    /// IN A CREW, resolve THE CREW'S ISLAND, in this order:
    ///   1. The LEADER's home, if they have one that is open -> CrewLeaderHome.
    ///   2. Otherwise the home of the EARLIEST-JOINED member who has one that is
    ///      open, scanning <see cref="CrewSnapshot.Members"/> front to back and
    ///      skipping the leader (already considered) -> CrewMemberHome.
    ///   3. Otherwise draw one -> FreshCrewIsland, recorded for EVERY member.
    ///
    /// TIE-BREAKS, all of them:
    ///   * "Earliest-joined" is the member list's own order, which
    ///     <c>CrewLedger</c> maintains as join order with the founder at index 0.
    ///     Promotion after a leader leaves does NOT reorder it, so a successor is
    ///     scanned at the position they joined at. Duplicates cannot occur
    ///     (<c>Crew.Add</c> is idempotent) so the scan has a total order and there
    ///     is nothing left to break.
    ///   * A home naming an island that is NOT open this boot is treated as absent
    ///     at every step. It is not an error and it is not a refusal: the character
    ///     keeps that stored position, and if the district is registered again they
    ///     get it back. What must not happen is being sent to terrain that does not
    ///     exist tonight.
    ///   * The draw is <c>pick(open.Count)</c> over the list ordered by island id,
    ///     so the same index is the same island on any server with the same
    ///     districts. An out-of-range answer is clamped rather than trusted.
    ///   * Clause 3 can only fire when clauses 1 AND 2 both failed, i.e. when NO
    ///     member has an open home. Recording the drawn island "for the whole crew"
    ///     therefore never overwrites anybody's existing Wilderness home - the case
    ///     where it would have to cannot arise.
    ///
    /// WHY THE CREW BEATS YOUR OWN HOME. A crewed player who already had a home
    /// goes to the CREW's island, not theirs: clause 1 of the crewless branch is
    /// not reachable from the crew branch. That is the whole point the mechanic was
    /// asked for - a crew arrives together - and it is stable rather than
    /// order-dependent, because whichever member goes first, later members resolve
    /// through the same leader-then-earliest-member scan and land on the same rock.
    /// Leaving the crew and using the shrine again returns you to your own home,
    /// which by then may be the crew's; nothing is destroyed either way, because
    /// the island you left is still there and still yours to fly back to.
    ///
    /// WHY IT IS STICKY. Going through the shrine twice takes you to the SAME
    /// island. A Wilderness island is where your ship, your shipyard and your
    /// stored logout position are; re-rolling it on every use would strand all
    /// three and turn a graduation device into a way to lose your things. The
    /// randomness is a WORLD-SPREAD mechanism - it is what stops 254 players
    /// piling onto one island - not a per-use thrill, so it is spent once per
    /// character (or once per crew) and then remembered.
    /// </summary>
    public static class WildernessGraduationPolicy
    {
        /// <summary>
        /// Whose home this graduation resolves to, and how. Split out from
        /// <see cref="Decide"/> so the crew rule can be asserted on its own,
        /// without a destination list or a draw in the way.
        /// </summary>
        public static (string? Uid, WildernessSource Source) ResolveHomeOwner(
            string actorUid,
            CrewSnapshot? crew,
            Func<string, bool> hasOpenHome)
        {
            if (hasOpenHome == null) throw new ArgumentNullException(nameof(hasOpenHome));

            if (crew == null)
            {
                return hasOpenHome(actorUid)
                    ? (actorUid, WildernessSource.OwnHome)
                    : (null, WildernessSource.FreshSoloIsland);
            }

            CrewSnapshot snapshot = crew.Value;

            // 1. The leader. Asked FIRST and asked by name rather than by position,
            //    because after a succession the leader is not Members[0].
            if (!string.IsNullOrEmpty(snapshot.LeaderUid) && hasOpenHome(snapshot.LeaderUid))
                return (snapshot.LeaderUid, WildernessSource.CrewLeaderHome);

            // 2. The earliest-joined member who has one. Skips the leader: they were
            //    just asked, and asking twice would only add a way for the two
            //    answers to disagree.
            foreach (string member in snapshot.Members ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(member)) continue;
                if (string.Equals(member, snapshot.LeaderUid, StringComparison.Ordinal)) continue;
                if (hasOpenHome(member)) return (member, WildernessSource.CrewMemberHome);
            }

            // 3. Nobody has one.
            return (null, WildernessSource.FreshCrewIsland);
        }

        /// <summary>
        /// The whole decision.
        /// </summary>
        /// <param name="actorUid">The durable character uid of whoever touched the shrine.</param>
        /// <param name="crew">Their crew, or null when they are not in one.</param>
        /// <param name="open">Tier-1 destinations registered on this boot, ordered
        /// by island id - <see cref="WildernessCatalog.Open"/> guarantees both.</param>
        /// <param name="homeOf">A character's recorded Wilderness home island, or
        /// null. Must already have applied "is it open this boot"; the seam does
        /// that with <see cref="HomeIslandOf"/>, which only ever answers with an
        /// island from <paramref name="open"/>.</param>
        /// <param name="pick">The draw: given a count, an index into
        /// <paramref name="open"/>. Injected so tests are deterministic and so the
        /// policy never reaches for a global RNG.</param>
        public static WildernessGraduation Decide(
            string? actorUid,
            CrewSnapshot? crew,
            IReadOnlyList<WildernessDestination> open,
            Func<string, IslandId?> homeOf,
            Func<int, int> pick)
        {
            if (open == null) throw new ArgumentNullException(nameof(open));
            if (homeOf == null) throw new ArgumentNullException(nameof(homeOf));
            if (pick == null) throw new ArgumentNullException(nameof(pick));

            if (string.IsNullOrWhiteSpace(actorUid))
            {
                return Refused(WildernessVerdict.UnknownCharacter,
                    "The shrine cannot read who you are yet. Try again in a moment.");
            }

            if (open.Count == 0)
            {
                return Refused(WildernessVerdict.WildernessClosed,
                    "The Wilderness is closed: no Tier-1 island is running on this world.");
            }

            Dictionary<string, IslandId?> asked = new(StringComparer.Ordinal);
            IslandId? Home(string uid)
            {
                if (asked.TryGetValue(uid, out IslandId? cached)) return cached;
                IslandId? answer = homeOf(uid);
                // Fail closed on a home that is not open tonight. Doing it here and
                // not in the caller means every clause of the rule sees the same
                // filtered view and none of them can forget.
                if (answer.HasValue && !Contains(open, answer.Value)) answer = null;
                asked[uid] = answer;
                return answer;
            }

            (string? ownerUid, WildernessSource source) =
                ResolveHomeOwner(actorUid!, crew, uid => Home(uid).HasValue);

            WildernessDestination destination;
            IReadOnlyList<string> recordFor;
            if (ownerUid != null)
            {
                IslandId island = Home(ownerUid)!.Value;
                destination = Find(open, island);
                recordFor = new[] { actorUid! };
            }
            else
            {
                destination = open[Clamp(pick(open.Count), open.Count)];
                recordFor = source == WildernessSource.FreshCrewIsland
                    ? CrewRoll(crew!.Value, actorUid!)
                    : new[] { actorUid! };
            }

            return new WildernessGraduation(
                WildernessVerdict.Granted, destination, source, recordFor,
                Explain(source, destination));
        }

        /// <summary>
        /// The Wilderness island a stored logout position sits on, or null for
        /// "not on one of tonight's Tier-1 islands".
        ///
        /// This is the ONLY definition of "home" in the system. It reuses
        /// <see cref="IslandLocationPolicy"/> exactly as the logout restore does,
        /// including its ground slack, so a character standing on their island's
        /// peak or on a structure they built is still home. A position in open sky
        /// - logged out on a ship - is not a home, and that is right: a crew whose
        /// leader is flying has no island to inherit, so the rule falls through to
        /// the next clause instead of guessing.
        /// </summary>
        public static IslandId? HomeIslandOf(
            FixedPointPosition? stored,
            IReadOnlyList<WildernessDestination> open)
        {
            if (open == null) throw new ArgumentNullException(nameof(open));
            if (!stored.HasValue || open.Count == 0) return null;

            IslandLocation location = IslandLocationPolicy.Locate(
                stored.Value, WildernessCatalog.Envelopes(open));
            if (location.Kind != IslandLocationKind.OnKnownTerrain || location.Island == null)
                return null;
            return Contains(open, location.Island.Id) ? location.Island.Id : null;
        }

        /// <summary>
        /// Every uid a fresh crew island is written for: the whole crew, plus the
        /// actor in case they are somehow not in the member list. Ordered and
        /// de-duplicated so the caller's writes are stable and countable.
        /// </summary>
        private static IReadOnlyList<string> CrewRoll(CrewSnapshot crew, string actorUid)
        {
            List<string> roll = new();
            HashSet<string> seen = new(StringComparer.Ordinal);
            void AddUid(string? uid)
            {
                if (!string.IsNullOrEmpty(uid) && seen.Add(uid!)) roll.Add(uid!);
            }

            AddUid(actorUid);
            AddUid(crew.LeaderUid);
            foreach (string member in crew.Members ?? Array.Empty<string>()) AddUid(member);
            return roll;
        }

        private static WildernessGraduation Refused(WildernessVerdict verdict, string message) =>
            new WildernessGraduation(verdict, default, WildernessSource.FreshSoloIsland,
                Array.Empty<string>(), message);

        private static string Explain(WildernessSource source, WildernessDestination destination)
        {
            string where = destination.DisplayName + " (" + destination.CellId + ")";
            return source switch
            {
                WildernessSource.OwnHome => "Returning you to " + where + ".",
                WildernessSource.CrewLeaderHome => "Joining your crew at " + where + ".",
                WildernessSource.CrewMemberHome => "Joining your crew at " + where + ".",
                WildernessSource.FreshCrewIsland => "Your crew's home in the Wilderness is " + where + ".",
                _ => "Your home in the Wilderness is " + where + ".",
            };
        }

        private static bool Contains(IReadOnlyList<WildernessDestination> open, IslandId island)
        {
            for (int i = 0; i < open.Count; i++)
                if (open[i].IslandId == island) return true;
            return false;
        }

        private static WildernessDestination Find(
            IReadOnlyList<WildernessDestination> open, IslandId island)
        {
            for (int i = 0; i < open.Count; i++)
                if (open[i].IslandId == island) return open[i];
            // Unreachable: Home() already filtered to open islands. Throwing rather
            // than returning open[0] because a silent substitution here would move
            // a whole crew somewhere nobody chose.
            throw new KeyNotFoundException("island '" + island + "' is not open this boot");
        }

        /// <summary>
        /// A draw that cannot leave the list. An injected picker is somebody else's
        /// code by definition - a test double, a seeded generator, one day an
        /// operator override - and none of those are worth trusting with an index.
        /// </summary>
        private static int Clamp(int index, int count)
        {
            if (index < 0) return 0;
            return index >= count ? count - 1 : index;
        }
    }
}
