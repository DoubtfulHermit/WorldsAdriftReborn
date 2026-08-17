using WorldsAdriftRebornGameServer.Multiplayer.Crew;

namespace WorldsAdriftRebornGameServer.Game.Crew
{
    /// <summary>The outcome of one crew action: what to say, and who must be retold.</summary>
    internal sealed class CrewOutcome
    {
        internal CrewOutcome(CrewVerdict verdict, IReadOnlyCollection<string> affected)
        {
            Verdict = verdict;
            Affected = affected;
        }

        internal CrewVerdict Verdict { get; }
        internal bool Ok => Verdict == CrewVerdict.Ok;
        internal string Message => CrewPolicy.Explain(Verdict);

        /// <summary>
        /// Every character uid whose 6900 changed, INCLUDING the actor. Getting
        /// this set wrong is the characteristic crew bug: an invite changes two
        /// players' state, and telling only the inviter leaves the invitee with a
        /// UI that never mentions the offer.
        /// </summary>
        internal IReadOnlyCollection<string> Affected { get; }

        internal static CrewOutcome Refused(CrewVerdict verdict, string actor) =>
            new CrewOutcome(verdict, new[] { actor });
    }

    /// <summary>
    /// The live crew ledger, its persistence and the actions the client can take.
    ///
    /// This is glue: every RULE lives in <see cref="CrewPolicy"/> and every
    /// mutation in <see cref="CrewLedger"/>, both pure and tested. What is here
    /// is the part that cannot be pure - who is connected, what they are called,
    /// and writing the result down.
    ///
    /// Crews key on the durable character uid. A player whose uid has not arrived
    /// (it comes in 1088, after checkout) can be SHOWN a crew but must never be
    /// written into one, exactly as their inventory is session-scoped and never
    /// saved. Every action here therefore refuses an unidentified actor rather
    /// than inventing a key for them.
    /// </summary>
    internal static class CrewService
    {
        private static readonly CrewLedger Ledger = new CrewLedger();
        private static readonly CrewPersistence Persistence = new CrewPersistence();

        /// <summary>Display names, learned as they are seen. Cosmetic only.</summary>
        private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal);

        private static int nextCrewNumber = 1;

        internal static void ReportPersistenceState()
        {
            if (Persistence.Enabled)
            {
                Console.WriteLine("[info] crew persistence is ON (Postgres).");
            }
            else
            {
                Console.WriteLine("[warning] crew persistence is OFF (" + Persistence.DisabledReason
                    + "). Crews will work for the length of a session and then be lost.");
            }
        }

        internal static void RestoreFromDatabase() => Persistence.LoadInto(Ledger);

        internal static Multiplayer.Crew.Crew? CrewOf(string uid) => Ledger.CrewOf(uid);
        internal static IReadOnlyCollection<string> InvitesFor(string uid) => Ledger.InvitesFor(uid);
        internal static Multiplayer.Crew.Crew? ById(string crewId) => Ledger.ById(crewId);

        internal static string NameOf(string uid) =>
            Names.TryGetValue(uid, out string? name) ? name : string.Empty;

        internal static void RememberName(string uid, string? displayName)
        {
            if (!string.IsNullOrWhiteSpace(uid) && !string.IsNullOrWhiteSpace(displayName))
                Names[uid] = displayName!;
        }

        internal static CrewOutcome Invite(string actorUid, string inviteeUid, string? inviteeName, int? slot)
        {
            CrewVerdict verdict = CrewPolicy.MayInvite(Ledger, actorUid, inviteeUid);
            if (verdict != CrewVerdict.Ok) return CrewOutcome.Refused(verdict, actorUid);

            RememberName(inviteeUid, inviteeName);

            Multiplayer.Crew.Crew? crew = Ledger.CrewOf(actorUid);
            if (crew == null)
            {
                // Inviting while crewless FOUNDS the crew, with the inviter
                // leading it. Retail has no separate "create crew" action.
                crew = Ledger.Create(NextCrewId(), actorUid);
                PersistCrew(crew);
                PersistMember(crew, actorUid);
            }

            Ledger.Invite(inviteeUid, crew.Id);
            _ = slot; // InvitePlayerWithSlot's seat is honoured on ACCEPT, not now.

            return new CrewOutcome(CrewVerdict.Ok, new[] { actorUid, inviteeUid });
        }

        /// <summary>
        /// Accepts an outstanding invite.
        ///
        /// The client's AcceptInvite event carries NO arguments, so which invite
        /// is not stated on the wire. When a player holds several, the OLDEST is
        /// accepted. That is our choice and not recovered from retail: the
        /// component holds invites in a map with no ordering, and the UI appears
        /// to present one at a time, so any rule here is a decision. Oldest-first
        /// is the one a player can predict.
        /// </summary>
        internal static CrewOutcome Accept(string actorUid)
        {
            string? crewId = Ledger.InvitesFor(actorUid).FirstOrDefault();
            if (crewId == null) return CrewOutcome.Refused(CrewVerdict.NoSuchInvite, actorUid);

            CrewVerdict verdict = CrewPolicy.MayAccept(Ledger, actorUid, crewId);
            if (verdict != CrewVerdict.Ok) return CrewOutcome.Refused(verdict, actorUid);

            Multiplayer.Crew.Crew crew = Ledger.ById(crewId)!;
            List<string> affected = new List<string>(crew.Members) { actorUid };

            Ledger.Join(actorUid, crewId);
            PersistMember(crew, actorUid);

            return new CrewOutcome(CrewVerdict.Ok, affected);
        }

        internal static CrewOutcome Reject(string actorUid)
        {
            string? crewId = Ledger.InvitesFor(actorUid).FirstOrDefault();
            if (crewId == null) return CrewOutcome.Refused(CrewVerdict.NoSuchInvite, actorUid);

            Multiplayer.Crew.Crew? crew = Ledger.ById(crewId);
            List<string> affected = new List<string> { actorUid };
            if (crew != null) affected.AddRange(crew.Members);

            Ledger.CancelInvite(actorUid, crewId);
            return new CrewOutcome(CrewVerdict.Ok, affected);
        }

        internal static CrewOutcome Boot(string actorUid, string targetUid)
        {
            CrewVerdict verdict = CrewPolicy.MayBoot(Ledger, actorUid, targetUid);
            if (verdict != CrewVerdict.Ok) return CrewOutcome.Refused(verdict, actorUid);

            Multiplayer.Crew.Crew crew = Ledger.CrewOf(actorUid)!;
            List<string> affected = new List<string>(crew.Members);

            Remove(targetUid);
            return new CrewOutcome(CrewVerdict.Ok, affected);
        }

        internal static CrewOutcome Leave(string actorUid)
        {
            CrewVerdict verdict = CrewPolicy.MayLeave(Ledger, actorUid);
            if (verdict != CrewVerdict.Ok) return CrewOutcome.Refused(verdict, actorUid);

            Multiplayer.Crew.Crew crew = Ledger.CrewOf(actorUid)!;
            List<string> affected = new List<string>(crew.Members);

            Remove(actorUid);
            return new CrewOutcome(CrewVerdict.Ok, affected);
        }

        /// <summary>
        /// Removes a member and writes the consequences down: a promoted leader
        /// and a disbanded crew are both persisted here, because a crash between
        /// the two would otherwise restore a crew whose leader had left.
        /// </summary>
        private static void Remove(string uid)
        {
            Multiplayer.Crew.Crew? crew = Ledger.CrewOf(uid);
            if (crew == null) return;

            string crewId = crew.Id;
            Ledger.Remove(uid);

            Guid? uidValue = CrewPersistence.UidFromKey(uid);
            if (uidValue.HasValue) Persistence.RemoveMember(uidValue.Value);

            Multiplayer.Crew.Crew? survivor = Ledger.ById(crewId);
            if (survivor == null)
            {
                Persistence.DeleteCrew(crewId);
                return;
            }

            // The leader may have changed; rewrite the crew row so a restart
            // restores the successor rather than the departed leader.
            PersistCrew(survivor);
        }

        private static void PersistCrew(Multiplayer.Crew.Crew crew)
        {
            Guid? leader = CrewPersistence.UidFromKey(crew.LeaderUid);
            if (leader.HasValue) Persistence.SaveCrew(crew.Id, leader.Value, crew.NumSlots);
        }

        private static void PersistMember(Multiplayer.Crew.Crew crew, string uid)
        {
            Guid? value = CrewPersistence.UidFromKey(uid);
            if (!value.HasValue) return;

            int joinOrder = 0;
            for (int i = 0; i < crew.Members.Count; i++)
                if (string.Equals(crew.Members[i], uid, StringComparison.Ordinal)) joinOrder = i;

            Persistence.SaveMember(value.Value, crew.Id, joinOrder, crew.SlotOf(uid));
        }

        /// <summary>
        /// The next unused crew id.
        ///
        /// This used to be a bare counter starting at 1, which lost a crew on
        /// every restart: persistence restores `crew:1`, the counter starts at 1
        /// again, and the first crew founded afterwards is created under an id
        /// that already exists. The ledger's Create then replaces the restored
        /// crew outright, and the database's ON CONFLICT rewrites its leader -
        /// so a real crew silently became somebody else's.
        ///
        /// Skipping ids the ledger already holds is the whole fix, and it is
        /// checked against the LEDGER rather than a remembered high-water mark
        /// because the ledger is the thing Create would collide with, restored
        /// rows and this session's crews alike.
        /// </summary>
        private static string NextCrewId()
        {
            while (true)
            {
                string candidate = "crew:" + nextCrewNumber++;
                if (Ledger.ById(candidate) == null) return candidate;
            }
        }
    }
}
