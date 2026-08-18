namespace WorldsAdriftRebornGameServer.Multiplayer.Alliance
{
    /// <summary>
    /// One alliance rank: a name, a permission set, and the two flags the client
    /// derives "is this the leader rank" and "is this the joining rank" from.
    ///
    /// <paramref name="Editable"/> is not a convenience field. The client computes
    ///
    ///     isDefaultLeaderRank = rankType == "leader" &amp;&amp; !editable
    ///     isDefaultMemberRank = rankType == "member" &amp;&amp; !editable
    ///
    /// (SocialGroupParsers.cs:126-127), and <c>AllianceRankInformation.CreateLookup</c>
    /// fills its <c>Leader</c> and <c>BasicMember</c> slots from exactly those two
    /// booleans. A leader rank shipped as editable therefore leaves
    /// <c>rankInfo.Leader</c> NULL, and the alliance panel loses the founder.
    /// </summary>
    public sealed record AllianceRank(
        string Id,
        string Name,
        bool Editable,
        string RankType,
        IReadOnlyList<string> Permissions)
    {
        /// <summary>The client's literal for the founder's rank type.</summary>
        public const string TypeLeader = "leader";

        /// <summary>The client's literal for every other rank, including the ones
        /// players create - CreateRankPayload hardcodes it (SocialGroupParsers.cs:198).</summary>
        public const string TypeMember = "member";

        /// <summary>
        /// The membership type every alliance rank carries. Hardcoded by the
        /// client (SocialGroupParsers.cs:199) and echoed back unchanged; it exists
        /// because ranks and crew memberships shared one table in the original
        /// service.
        /// </summary>
        public const string MembershipType = "alliance_member";

        public bool IsDefaultLeader =>
            string.Equals(RankType, TypeLeader, StringComparison.Ordinal) && !Editable;

        public bool IsDefaultMember =>
            string.Equals(RankType, TypeMember, StringComparison.Ordinal) && !Editable;

        public bool Grants(string permission)
        {
            foreach (string held in Permissions)
            {
                if (string.Equals(held, permission, StringComparison.Ordinal)) return true;
            }

            return false;
        }
    }

    /// <summary>
    /// One alliance: its identity, its ranks and who holds which.
    ///
    /// Members are keyed by the same durable character key the crew ledger and the
    /// inventory use, so a player is one player across all three.
    /// </summary>
    public sealed class Alliance
    {
        private readonly Dictionary<string, string> rankByMember = new(StringComparer.Ordinal);
        private readonly List<string> members = new List<string>();
        private readonly List<AllianceRank> ranks = new List<AllianceRank>();

        public Alliance(string id, string leaderUid, string name, AllianceRank leaderRank, AllianceRank memberRank)
        {
            Id = id;
            LeaderUid = leaderUid;
            Name = name;

            ranks.Add(leaderRank);
            ranks.Add(memberRank);

            members.Add(leaderUid);
            rankByMember[leaderUid] = leaderRank.Id;
        }

        public string Id { get; }
        public string Name { get; }
        public string LeaderUid { get; private set; }

        /// <summary>Members in join order - the founder first.</summary>
        public IReadOnlyList<string> Members => members;

        public IReadOnlyList<AllianceRank> Ranks => ranks;

        public bool IsLeader(string uid) => string.Equals(LeaderUid, uid, StringComparison.Ordinal);

        public bool Holds(string uid) => rankByMember.ContainsKey(uid);

        /// <summary>
        /// The rank a member holds, or null when they are not a member.
        ///
        /// Falls back to the default member rank when the stored rank id no longer
        /// resolves. That is not defensive padding: the client's
        /// <c>AllianceClient.TryGetRank</c> THROWS <c>AllianceRankNotFoundException</c>
        /// on a rank id it cannot find in <c>ranks/{allianceUid}</c>, and that throw
        /// lands in the shared alliance-and-crew exception handler - so one member
        /// pointing at a deleted rank takes out the whole Social Sheet, both tabs.
        /// A member always has A rank here, even if it is the plainest one.
        /// </summary>
        public AllianceRank? RankOf(string uid)
        {
            if (uid == null || !rankByMember.TryGetValue(uid, out string? rankId)) return null;

            AllianceRank? exact = RankById(rankId);
            return exact ?? DefaultMemberRank;
        }

        public AllianceRank? RankById(string? rankId)
        {
            if (rankId == null) return null;

            foreach (AllianceRank rank in ranks)
            {
                if (string.Equals(rank.Id, rankId, StringComparison.Ordinal)) return rank;
            }

            return null;
        }

        public AllianceRank? DefaultLeaderRank
        {
            get
            {
                foreach (AllianceRank rank in ranks)
                {
                    if (rank.IsDefaultLeader) return rank;
                }

                return null;
            }
        }

        public AllianceRank? DefaultMemberRank
        {
            get
            {
                foreach (AllianceRank rank in ranks)
                {
                    if (rank.IsDefaultMember) return rank;
                }

                return null;
            }
        }

        internal void Join(string uid, string rankId)
        {
            if (!members.Contains(uid)) members.Add(uid);
            rankByMember[uid] = rankId;
        }

        internal void Remove(string uid)
        {
            members.Remove(uid);
            rankByMember.Remove(uid);
        }

        internal void Assign(string uid, string rankId)
        {
            if (!rankByMember.ContainsKey(uid)) return;
            rankByMember[uid] = rankId;
        }

        internal void Promote(string uid) => LeaderUid = uid;

        /// <summary>
        /// Adds a rank, or REPLACES one that already has that id IN PLACE.
        ///
        /// In place, and that word is the whole method. Replacing a rank is how it
        /// is renamed or re-permissioned, and the people holding it must keep
        /// holding it - implementing this as "remove then add" would run
        /// <see cref="RemoveRank"/>'s holder-reassignment first and silently demote
        /// every member of that rank to the basic one on every edit.
        ///
        /// Public rather than internal because the ledger is rebuilt from storage
        /// by a DIFFERENT assembly - the login server answers every alliance
        /// request and owns the rows - so hydration has to seat the founder's
        /// custom ranks alongside the two defaults the constructor takes.
        /// </summary>
        public void AddRank(AllianceRank rank)
        {
            for (int i = 0; i < ranks.Count; i++)
            {
                if (!string.Equals(ranks[i].Id, rank.Id, StringComparison.Ordinal)) continue;
                ranks[i] = rank;
                return;
            }

            ranks.Add(rank);
        }

        /// <summary>
        /// Deletes a rank and moves everyone who held it to the default member
        /// rank, because a member left pointing at nothing crashes the client's
        /// rank lookup - see <see cref="RankOf"/>.
        /// </summary>
        public void RemoveRank(string rankId)
        {
            for (int i = 0; i < ranks.Count; i++)
            {
                if (!string.Equals(ranks[i].Id, rankId, StringComparison.Ordinal)) continue;
                ranks.RemoveAt(i);
                break;
            }

            AllianceRank? fallback = DefaultMemberRank;
            if (fallback == null) return;

            foreach (string uid in members)
            {
                if (rankByMember.TryGetValue(uid, out string? held)
                    && string.Equals(held, rankId, StringComparison.Ordinal))
                {
                    rankByMember[uid] = fallback.Id;
                }
            }
        }
    }

    /// <summary>
    /// Every alliance, indexed the three ways the rules ask about them: by id, by
    /// member, and by name.
    ///
    /// Pure - no database, no clock, no wire. Alliance decisions are not local:
    /// "may this player apply" depends on whether they are already in a DIFFERENT
    /// alliance, and "is this name free" spans all of them. So the whole thing is
    /// rebuilt from the store per request, exactly as the crew ledger is, and a
    /// community server holds tens of alliances rather than millions.
    /// </summary>
    public sealed class AllianceLedger
    {
        private readonly Dictionary<string, Alliance> byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> allianceByMember = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> requestsByCharacter = new(StringComparer.Ordinal);

        public IReadOnlyCollection<Alliance> All => byId.Values;

        public Alliance? ById(string? allianceId) =>
            allianceId != null && byId.TryGetValue(allianceId, out Alliance? alliance) ? alliance : null;

        public Alliance? AllianceOf(string? uid) =>
            uid != null && allianceByMember.TryGetValue(uid, out string? id) ? ById(id) : null;

        /// <summary>
        /// Whether any alliance already uses this name, comparing the way
        /// <see cref="AllianceNamePolicy.UniquenessKey"/> says to.
        ///
        /// <paramref name="excluding"/> lets an alliance keep its own name through
        /// an edit without colliding with itself.
        /// </summary>
        public bool NameTaken(string name, string? excluding = null)
        {
            string key = AllianceNamePolicy.UniquenessKey(name);

            foreach (Alliance alliance in byId.Values)
            {
                if (excluding != null && string.Equals(alliance.Id, excluding, StringComparison.Ordinal)) continue;
                if (string.Equals(AllianceNamePolicy.UniquenessKey(alliance.Name), key, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether this character already has a live invite to, or application
        /// for, that alliance.
        ///
        /// Held against the CHARACTER rather than the alliance, mirroring how the
        /// invite table is keyed and how the crew ledger does it - a player may
        /// hold offers from several alliances at once and choose.
        /// </summary>
        public bool HasLiveRequest(string uid, string allianceId) =>
            uid != null && requestsByCharacter.TryGetValue(uid, out HashSet<string>? set)
            && set.Contains(allianceId);

        /// <summary>How many outstanding invites and applications one alliance is
        /// holding, in either direction.</summary>
        public int LiveRequestsFor(string allianceId)
        {
            if (allianceId == null) return 0;

            int count = 0;
            foreach (HashSet<string> offers in requestsByCharacter.Values)
            {
                if (offers.Contains(allianceId)) count++;
            }

            return count;
        }

        public Alliance Create(
            string allianceId,
            string leaderUid,
            string name,
            AllianceRank leaderRank,
            AllianceRank memberRank)
        {
            Alliance alliance = new Alliance(allianceId, leaderUid, name, leaderRank, memberRank);
            byId[allianceId] = alliance;
            allianceByMember[leaderUid] = allianceId;
            return alliance;
        }

        public void Request(string uid, string allianceId)
        {
            if (!requestsByCharacter.TryGetValue(uid, out HashSet<string>? set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                requestsByCharacter[uid] = set;
            }

            set.Add(allianceId);
        }

        /// <summary>
        /// Seats a member and drops every outstanding request they held. Once
        /// somebody is in an alliance their other offers are unusable, and leaving
        /// them lying about would let a second one be accepted.
        /// </summary>
        public void Join(string uid, string allianceId, string rankId)
        {
            Alliance? alliance = ById(allianceId);
            if (alliance == null) return;

            alliance.Join(uid, rankId);
            allianceByMember[uid] = allianceId;
            requestsByCharacter.Remove(uid);
        }

        /// <summary>
        /// Removes a member. The founder leaving hands the alliance to the
        /// longest-standing member left and moves them onto the leader rank; the
        /// last member leaving dissolves it, so an empty alliance can never linger
        /// and be joined by a stale invite.
        /// </summary>
        public void Remove(string uid)
        {
            Alliance? alliance = AllianceOf(uid);
            if (alliance == null) return;

            bool wasLeader = alliance.IsLeader(uid);
            string? successor = wasLeader ? AlliancePolicy.SuccessorTo(alliance, uid) : null;

            alliance.Remove(uid);
            allianceByMember.Remove(uid);

            if (alliance.Members.Count == 0)
            {
                Dissolve(alliance.Id);
                return;
            }

            if (!wasLeader || successor == null) return;

            alliance.Promote(successor);

            // The successor takes the leader RANK as well as the title. Leadership
            // in this client is two independent facts - leaderCharacterUid on the
            // alliance and the rank the member holds - and only moving one leaves
            // a founder with no permissions or a member with all of them.
            AllianceRank? leaderRank = alliance.DefaultLeaderRank;
            if (leaderRank != null) alliance.Assign(successor, leaderRank.Id);
        }

        public void Dissolve(string allianceId)
        {
            Alliance? alliance = ById(allianceId);
            if (alliance == null) return;

            foreach (string uid in alliance.Members) allianceByMember.Remove(uid);
            byId.Remove(allianceId);

            foreach (string uid in requestsByCharacter.Keys.ToArray())
            {
                requestsByCharacter[uid].Remove(allianceId);
                if (requestsByCharacter[uid].Count == 0) requestsByCharacter.Remove(uid);
            }
        }

        public void Assign(string uid, string rankId) => AllianceOf(uid)?.Assign(uid, rankId);
    }
}
