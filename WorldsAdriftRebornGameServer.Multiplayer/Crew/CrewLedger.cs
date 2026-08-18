namespace WorldsAdriftRebornGameServer.Multiplayer.Crew
{
    /// <summary>
    /// One crew: a leader, an ordered member list and a slot map.
    ///
    /// Members are ordered by JOIN ORDER, which is why the list is a list and not
    /// a set: <see cref="CrewPolicy.SuccessorTo"/> reads succession straight off
    /// it, so the order is data, not presentation.
    ///
    /// Slots are separate from membership on purpose. Retail's CrewSlot carries
    /// its own Slot index and an Active flag, and the UI lays out fixed positions,
    /// so a member always exists but may not have chosen a seat.
    /// </summary>
    public sealed class Crew
    {
        private readonly List<string> members = new List<string>();
        private readonly Dictionary<int, string> slots = new Dictionary<int, string>();

        public Crew(string id, string leaderUid, int numSlots)
        {
            Id = id;
            LeaderUid = leaderUid;
            NumSlots = Math.Clamp(numSlots, 1, CrewPolicy.MaxSlots);
            members.Add(leaderUid);
        }

        public string Id { get; }
        public string LeaderUid { get; private set; }
        public int NumSlots { get; }

        public IReadOnlyList<string> Members => members;
        public IReadOnlyDictionary<int, string> Slots => slots;

        public bool IsLeader(string uid) => string.Equals(LeaderUid, uid, StringComparison.Ordinal);
        public bool IsFull => members.Count >= NumSlots;

        public string? OccupantOf(int slot) => slots.TryGetValue(slot, out string? uid) ? uid : null;

        public int? SlotOf(string uid)
        {
            foreach (KeyValuePair<int, string> pair in slots)
                if (string.Equals(pair.Value, uid, StringComparison.Ordinal)) return pair.Key;
            return null;
        }

        internal void Add(string uid)
        {
            if (!members.Contains(uid)) members.Add(uid);
        }

        internal void Remove(string uid)
        {
            members.Remove(uid);
            int? slot = SlotOf(uid);
            if (slot.HasValue) slots.Remove(slot.Value);
        }

        internal void Promote(string uid) => LeaderUid = uid;

        internal void TakeSlot(string uid, int slot)
        {
            int? previous = SlotOf(uid);
            if (previous.HasValue) slots.Remove(previous.Value);
            slots[slot] = uid;
        }
    }

    /// <summary>
    /// Every crew and every outstanding invite, keyed by durable character uid.
    ///
    /// Invites are held against the INVITEE, mirroring retail's 6900
    /// <c>InvitesReceived</c> map living on the invited player's own component.
    /// That shape lets one player hold offers from several crews at once and
    /// survives the inviter disconnecting, both of which a crew-side list would
    /// lose.
    ///
    /// Pure: no ENet, no database, no clock. The service layer applies these
    /// mutations and pushes the components; the rules live in
    /// <see cref="CrewPolicy"/> and are checked BEFORE anything here mutates.
    /// </summary>
    public sealed class CrewLedger
    {
        private readonly Dictionary<string, Crew> byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> crewByMember = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> invitesByInvitee = new(StringComparer.Ordinal);

        public IReadOnlyCollection<Crew> All => byId.Values;

        public Crew? ById(string crewId) =>
            crewId != null && byId.TryGetValue(crewId, out Crew? crew) ? crew : null;

        public Crew? CrewOf(string uid) =>
            uid != null && crewByMember.TryGetValue(uid, out string? id) ? ById(id) : null;

        public bool HasInviteFrom(string inviteeUid, string crewId) =>
            inviteeUid != null && invitesByInvitee.TryGetValue(inviteeUid, out HashSet<string>? set)
            && set.Contains(crewId);

        public IReadOnlyCollection<string> InvitesFor(string inviteeUid) =>
            inviteeUid != null && invitesByInvitee.TryGetValue(inviteeUid, out HashSet<string>? set)
                ? set
                : Array.Empty<string>();

        /// <summary>
        /// How many outstanding invites one CREW is holding.
        ///
        /// Invites are stored against the invitee, mirroring retail's 6900
        /// InvitesReceived map, which answers "who invited me" in O(1) and "how
        /// many has this crew sent" not at all. That asymmetry is why nothing
        /// counted them and a crew could offer unlimited seats. The crews on one
        /// community server number in the tens, so the scan is honest and cheap;
        /// a second index would be one more thing to keep in step.
        /// </summary>
        public int LiveInvitesFor(string crewId)
        {
            if (crewId == null) return 0;

            int count = 0;
            foreach (HashSet<string> offers in invitesByInvitee.Values)
            {
                if (offers.Contains(crewId)) count++;
            }

            return count;
        }

        /// <summary>Founds a crew led by <paramref name="leaderUid"/>.</summary>
        public Crew Create(string crewId, string leaderUid, int numSlots = CrewPolicy.DefaultSlots)
        {
            Crew crew = new Crew(crewId, leaderUid, numSlots);
            byId[crewId] = crew;
            crewByMember[leaderUid] = crewId;
            return crew;
        }

        public void Invite(string inviteeUid, string crewId)
        {
            if (!invitesByInvitee.TryGetValue(inviteeUid, out HashSet<string>? set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                invitesByInvitee[inviteeUid] = set;
            }
            set.Add(crewId);
        }

        public void CancelInvite(string inviteeUid, string crewId)
        {
            if (invitesByInvitee.TryGetValue(inviteeUid, out HashSet<string>? set))
            {
                set.Remove(crewId);
                if (set.Count == 0) invitesByInvitee.Remove(inviteeUid);
            }
        }

        /// <summary>
        /// Joins a crew and drops EVERY outstanding invite for that player: once
        /// you are crewed, stale offers from other crews are unusable, and leaving
        /// them lying around would let a player accept a second one.
        /// </summary>
        public void Join(string uid, string crewId)
        {
            Crew? crew = ById(crewId);
            if (crew == null) return;
            crew.Add(uid);
            crewByMember[uid] = crewId;
            invitesByInvitee.Remove(uid);
        }

        /// <summary>
        /// Removes a player. Promotes a successor if the leader left, and disbands
        /// the crew when the last member goes, so an empty crew can never linger
        /// and be joined by a stale invite.
        /// </summary>
        public void Remove(string uid)
        {
            Crew? crew = CrewOf(uid);
            if (crew == null) return;

            bool wasLeader = crew.IsLeader(uid);
            string? successor = wasLeader ? CrewPolicy.SuccessorTo(crew, uid) : null;

            crew.Remove(uid);
            crewByMember.Remove(uid);

            if (crew.Members.Count == 0)
            {
                byId.Remove(crew.Id);
                foreach (string invitee in invitesByInvitee.Keys.ToArray())
                    CancelInvite(invitee, crew.Id);
                return;
            }

            if (wasLeader && successor != null) crew.Promote(successor);
        }

        public void TakeSlot(string uid, int slot) => CrewOf(uid)?.TakeSlot(uid, slot);
    }
}
